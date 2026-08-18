/*
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Threading;

namespace DBreeze.Storage.RemoteInstance
{
    public class RemoteTablesHandler : IDisposable
    {
        readonly ReaderWriterLockSlim _sync = new ReaderWriterLockSlim();
        readonly Dictionary<ulong, RemoteTable> _tables = new Dictionary<ulong, RemoteTable>();
        readonly Dictionary<string, ulong> _tableIds = new Dictionary<string, ulong>(StringComparer.Ordinal);
        readonly Dictionary<ulong, int> _openCounts = new Dictionary<ulong, int>();
        ulong _lastTableId;
        bool _disposed;
        const int MaxReadResponseSize = 64 * 1024 * 1024;
        internal readonly DBreezeConfiguration configuration;

        public RemoteTablesHandler(DBreezeConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException("configuration");
            if (configuration.FSFactory == null)
                throw new ArgumentException("configuration.FSFactory must be initialized.", "configuration");
            this.configuration = configuration;
        }

        public void Dispose()
        {
            _sync.EnterWriteLock();
            try
            {
                if (_disposed)
                    return;
                _disposed = true;
                foreach (KeyValuePair<ulong, RemoteTable> table in _tables)
                    table.Value.Dispose();
                _tables.Clear();
                _tableIds.Clear();
                _openCounts.Clear();
            }
            finally
            {
                _sync.ExitWriteLock();
            }
        }

        public byte[] ParseProtocol(byte[] protocol)
        {
            try
            {
                if (_disposed || protocol == null || protocol.Length < 2 || protocol[0] != 1)
                    return ErrorResponse();

                byte command = protocol[1];
                if (command == 1)
                    return OpenTable(protocol);
                if (command < 2 || command > 12 || protocol.Length < 10)
                    return ErrorResponse();

                ulong tableId = BitConverter.ToUInt64(protocol, 2);
                if (command == 2)
                    return protocol.Length == 10 ? CloseTable(tableId) : ErrorResponse();
                if (command == 3)
                    return protocol.Length == 10 ? DeleteTable(tableId) : ErrorResponse();

                _sync.EnterReadLock();
                try
                {
                    RemoteTable table;
                    if (_disposed || !_tables.TryGetValue(tableId, out table))
                        return ErrorResponse();

                    if (command >= 4 && command <= 6)
                    {
                        if (protocol.Length < 19)
                            return ErrorResponse();
                        long position = BitConverter.ToInt64(protocol, 10);
                        int count = protocol.Length - 19;
                        if (position < 0 || position > Int64.MaxValue - count || protocol[18] > 1)
                            return ErrorResponse();
                        bool flush = protocol[18] == 1;
                        if (command == 4)
                            return table.DataFileWrite(position, flush, protocol, 19, count);
                        if (command == 5)
                            return table.RollbackFileWrite(position, flush, protocol, 19, count);
                        return table.RollbackHelperFileWrite(position, flush, protocol, 19, count);
                    }

                    if (command >= 7 && command <= 9)
                    {
                        if (protocol.Length != 22)
                            return ErrorResponse();
                        long position = BitConverter.ToInt64(protocol, 10);
                        int count = BitConverter.ToInt32(protocol, 18);
                        if (position < 0 || count < 0 || count > MaxReadResponseSize)
                            return ErrorResponse();
                        if (command == 7)
                            return table.DataFileRead(position, count);
                        if (command == 8)
                            return table.RollbackFileRead(position, count);
                        return table.RollbackHelperFileRead(position, count);
                    }

                    if (protocol.Length != 10)
                        return ErrorResponse();
                    if (command == 10)
                        return table.DataFileFlush();
                    if (command == 11)
                        return table.RollbackFileFlush();
                    return table.RollbackFileRecreate();
                }
                finally
                {
                    _sync.ExitReadLock();
                }
            }
            catch
            {
                return ErrorResponse();
            }
        }

        byte[] OpenTable(byte[] protocol)
        {
            if (protocol.Length < 6)
                return ErrorResponse();
            int nameLength = BitConverter.ToInt32(protocol, 2);
            if (nameLength < 0 || nameLength != protocol.Length - 6)
                return ErrorResponse();

            string tableName = System.Text.Encoding.UTF8.GetString(protocol, 6, nameLength);
            string fileName = System.IO.Path.Combine(configuration.DBreezeDataFolderName, tableName);

            _sync.EnterWriteLock();
            try
            {
                if (_disposed)
                    return ErrorResponse();

                ulong id;
                RemoteTable table;
                bool created = false;
                if (!_tableIds.TryGetValue(fileName, out id))
                {
                    if (_lastTableId == UInt64.MaxValue)
                        return ErrorResponse();
                    id = ++_lastTableId;
                    table = new RemoteTable(this, fileName, id);
                    _tables.Add(id, table);
                    _tableIds.Add(fileName, id);
                    _openCounts.Add(id, 0);
                    created = true;
                }
                else if (!_tables.TryGetValue(id, out table))
                {
                    return ErrorResponse();
                }

                try
                {
                    byte[] response = table.OpenRemoteTable();
                    _openCounts[id] = checked(_openCounts[id] + 1);
                    return response;
                }
                catch
                {
                    if (created)
                    {
                        table.Dispose();
                        _openCounts.Remove(id);
                        _tables.Remove(id);
                        _tableIds.Remove(fileName);
                    }
                    throw;
                }
            }
            finally
            {
                _sync.ExitWriteLock();
            }
        }

        byte[] CloseTable(ulong id)
        {
            _sync.EnterWriteLock();
            try
            {
                RemoteTable table;
                int count;
                if (_disposed || !_tables.TryGetValue(id, out table) || !_openCounts.TryGetValue(id, out count) || count <= 0)
                    return ErrorResponse();

                count--;
                if (count != 0)
                {
                    _openCounts[id] = count;
                    return new byte[] { 1 };
                }

                byte[] response = table.CloseRemoteTable();
                _openCounts.Remove(id);
                _tables.Remove(id);
                _tableIds.Remove(table._fileName);
                return response;
            }
            finally
            {
                _sync.ExitWriteLock();
            }
        }

        byte[] DeleteTable(ulong id)
        {
            _sync.EnterWriteLock();
            try
            {
                RemoteTable table;
                if (_disposed || !_tables.TryGetValue(id, out table))
                    return ErrorResponse();
                byte[] response = table.DeleteRemoteTable();
                _openCounts.Remove(id);
                _tables.Remove(id);
                _tableIds.Remove(table._fileName);
                return response;
            }
            finally
            {
                _sync.ExitWriteLock();
            }
        }

        static byte[] ErrorResponse() { return new byte[] { 255 }; }
    }
}
