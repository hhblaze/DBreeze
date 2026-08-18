/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Threading;

namespace DBreeze.Storage.RemoteInstance
{
    /// <summary>
    /// ServerSide. Servs one local database.
    /// </summary>
    public class RemoteTablesHandler:IDisposable
    {
        readonly ReaderWriterLockSlim _sync = new ReaderWriterLockSlim();
        readonly Dictionary<ulong, RemoteTable> _t = new Dictionary<ulong, RemoteTable>();
        /// <summary>
        /// fileName to id binding
        /// </summary>
        readonly Dictionary<string, ulong> _tIds = new Dictionary<string, ulong>(
            System.IO.Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        readonly Dictionary<ulong, int> _openCounts = new Dictionary<ulong, int>();
        ulong tableId = 0;
        string databasePreFolderPath = String.Empty;
        bool _disposed = false;
        const int MaxReadResponseSize = 64 * 1024 * 1024;

        /// <summary>
        /// RemoteTablesHandler
        /// </summary>
        /// <param name="databasePreFolderPath"></param>
        public RemoteTablesHandler(string databasePreFolderPath)
        {
            this.databasePreFolderPath = databasePreFolderPath;
        }

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            _sync.EnterWriteLock();
            try
            {
                if (_disposed)
                    return;

                _disposed = true;
                foreach (var rt in _t)
                    rt.Value.Dispose();

                _t.Clear();
                _tIds.Clear();
                _openCounts.Clear();
            }
            finally
            {
                _sync.ExitWriteLock();
            }
        }

        /// <summary>
        /// ParseProtocol
        /// </summary>
        /// <param name="protocol"></param>
        /// <returns></returns>
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

                ulong callTableId = BitConverter.ToUInt64(protocol, 2);
                if (command == 2)
                {
                    if (protocol.Length != 10)
                        return ErrorResponse();
                    return CloseTable(callTableId);
                }
                if (command == 3)
                {
                    if (protocol.Length != 10)
                        return ErrorResponse();
                    return DeleteTable(callTableId);
                }

                _sync.EnterReadLock();
                try
                {
                    RemoteTable table;
                    if (!_t.TryGetValue(callTableId, out table))
                        return ErrorResponse();

                    switch (command)
                    {
                        case 4:
                        case 5:
                        case 6:
                            if (protocol.Length < 19)
                                return ErrorResponse();
                            long writePosition = BitConverter.ToInt64(protocol, 10);
                            if (writePosition < 0 || protocol[18] > 1)
                                return ErrorResponse();
                            int payloadLength = protocol.Length - 19;
                            if (writePosition > Int64.MaxValue - payloadLength)
                                return ErrorResponse();
                            if (command == 4)
                                return table.DataFileWrite(writePosition, protocol[18] == 1, protocol, 19, payloadLength);
                            if (command == 5)
                                return table.RollbackFileWrite(writePosition, protocol[18] == 1, protocol, 19, payloadLength);
                            return table.RollbackHelperFileWrite(writePosition, protocol[18] == 1, protocol, 19, payloadLength);

                        case 7:
                        case 8:
                        case 9:
                            if (protocol.Length != 22)
                                return ErrorResponse();
                            long readPosition = BitConverter.ToInt64(protocol, 10);
                            int count = BitConverter.ToInt32(protocol, 18);
                            if (readPosition < 0 || count < 0 || count > MaxReadResponseSize)
                                return ErrorResponse();
                            if (command == 7)
                                return table.DataFileRead(readPosition, count);
                            if (command == 8)
                                return table.RollbackFileRead(readPosition, count);
                            return table.RollbackHelperFileRead(readPosition, count);

                        case 10:
                            if (protocol.Length != 10)
                                return ErrorResponse();
                            return table.DataFileFlush();
                        case 11:
                            if (protocol.Length != 10)
                                return ErrorResponse();
                            return table.RollbackFileFlush();
                        case 12:
                            if (protocol.Length != 10)
                                return ErrorResponse();
                            return table.RollbackFileRecreate();
                    }
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
            return ErrorResponse();
        }

        private byte[] OpenTable(byte[] protocol)
        {
            if (protocol.Length < 6)
                return ErrorResponse();

            int tableNameLength = BitConverter.ToInt32(protocol, 2);
            if (tableNameLength < 0 || tableNameLength != protocol.Length - 6)
                return ErrorResponse();

            string tableName = System.Text.Encoding.UTF8.GetString(protocol, 6, tableNameLength);
            string fileName = System.IO.Path.GetFullPath(System.IO.Path.Combine(databasePreFolderPath, tableName));

            _sync.EnterWriteLock();
            try
            {
                if (_disposed)
                    return ErrorResponse();

                ulong id;
                RemoteTable table;
                bool created = false;
                if (!_tIds.TryGetValue(fileName, out id))
                {
                    if (tableId == UInt64.MaxValue)
                        return ErrorResponse();
                    id = ++tableId;
                    table = new RemoteTable(fileName, id);
                    _t.Add(id, table);
                    _tIds.Add(fileName, id);
                    _openCounts.Add(id, 0);
                    created = true;
                }
                else if (!_t.TryGetValue(id, out table))
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
                        _t.Remove(id);
                        _tIds.Remove(fileName);
                    }
                    throw;
                }
            }
            finally
            {
                _sync.ExitWriteLock();
            }
        }

        private byte[] CloseTable(ulong id)
        {
            _sync.EnterWriteLock();
            try
            {
                RemoteTable table;
                int count;
                if (!_t.TryGetValue(id, out table) || !_openCounts.TryGetValue(id, out count) || count <= 0)
                    return ErrorResponse();

                count--;
                if (count != 0)
                {
                    _openCounts[id] = count;
                    return new byte[] { 1 };
                }

                byte[] response = table.CloseRemoteTable();
                _openCounts.Remove(id);
                _t.Remove(id);
                _tIds.Remove(table._fileName);
                return response;
            }
            finally
            {
                _sync.ExitWriteLock();
            }
        }

        private byte[] DeleteTable(ulong id)
        {
            _sync.EnterWriteLock();
            try
            {
                RemoteTable table;
                if (!_t.TryGetValue(id, out table))
                    return ErrorResponse();

                byte[] response = table.DeleteRemoteTable();
                _openCounts.Remove(id);
                _t.Remove(id);
                _tIds.Remove(table._fileName);
                return response;
            }
            finally
            {
                _sync.ExitWriteLock();
            }
        }

        private static byte[] ErrorResponse()
        {
            return new byte[] { 255 };
        }

      
    }
}
