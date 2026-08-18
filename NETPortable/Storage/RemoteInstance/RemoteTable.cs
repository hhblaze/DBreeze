/*
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.IO;

namespace DBreeze.Storage.RemoteInstance
{
    internal class RemoteTable : IDisposable
    {
        readonly ulong tableId;
        readonly object lock_fs = new object();
        readonly RemoteTablesHandler rth;
        IFileStream _fsData;
        IFileStream _fsRollback;
        IFileStream _fsRollbackHelper;
        readonly int _fileStreamBufferSize = 8192;
        public readonly string _fileName;
        readonly byte ProtocolVersion = 1;

        public RemoteTable(RemoteTablesHandler handler, string fileName, ulong id)
        {
            rth = handler;
            _fileName = fileName;
            tableId = id;

            string directory = Path.GetDirectoryName(fileName);
            if (!String.IsNullOrEmpty(directory))
            {
                IDirectoryInfo directoryInfo = rth.configuration.FSFactory.CreateDirectoryInfo(directory);
                if (!directoryInfo.Exists)
                    directoryInfo.Create();
            }
        }

        public byte[] OpenRemoteTable()
        {
            lock (lock_fs)
            {
                if (_fsData == null)
                    _fsData = rth.configuration.FSFactory.CreateType1(_fileName, _fileStreamBufferSize);
                if (_fsRollback == null)
                    _fsRollback = rth.configuration.FSFactory.CreateType1(_fileName + ".rol", _fileStreamBufferSize);
                if (_fsRollbackHelper == null)
                    _fsRollbackHelper = rth.configuration.FSFactory.CreateType1(_fileName + ".rhp", _fileStreamBufferSize);

                byte[] response = new byte[33];
                response[0] = ProtocolVersion;
                Buffer.BlockCopy(BitConverter.GetBytes(tableId), 0, response, 1, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(_fsData.Length), 0, response, 9, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(_fsRollback.Length), 0, response, 17, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(_fsRollbackHelper.Length), 0, response, 25, 8);
                return response;
            }
        }

        public void Dispose()
        {
            lock (lock_fs)
                CloseFiles();
        }

        public byte[] CloseRemoteTable()
        {
            lock (lock_fs)
                CloseFiles();
            return Success();
        }

        public byte[] DeleteRemoteTable()
        {
            lock (lock_fs)
            {
                CloseFiles();
                rth.configuration.FSFactory.Delete(_fileName);
                rth.configuration.FSFactory.Delete(_fileName + ".rol");
                rth.configuration.FSFactory.Delete(_fileName + ".rhp");
            }
            return Success();
        }

        public byte[] DataFileWrite(long position, bool withFlush, byte[] data, int offset, int count)
        {
            lock (lock_fs)
                return Write(_fsData, position, withFlush, data, offset, count);
        }

        public byte[] RollbackFileWrite(long position, bool withFlush, byte[] data, int offset, int count)
        {
            lock (lock_fs)
                return Write(_fsRollback, position, withFlush, data, offset, count);
        }

        public byte[] RollbackHelperFileWrite(long position, bool withFlush, byte[] data, int offset, int count)
        {
            lock (lock_fs)
                return Write(_fsRollbackHelper, position, withFlush, data, offset, count);
        }

        public byte[] DataFileRead(long position, int count)
        {
            lock (lock_fs)
                return Read(_fsData, position, count);
        }

        public byte[] RollbackFileRead(long position, int count)
        {
            lock (lock_fs)
                return Read(_fsRollback, position, count);
        }

        public byte[] RollbackHelperFileRead(long position, int count)
        {
            lock (lock_fs)
                return Read(_fsRollbackHelper, position, count);
        }

        public byte[] DataFileFlush()
        {
            lock (lock_fs)
                FSR.NET_Flush(_fsData);
            return Success();
        }

        public byte[] RollbackFileFlush()
        {
            lock (lock_fs)
                FSR.NET_Flush(_fsRollback);
            return Success();
        }

        public byte[] RollbackFileRecreate()
        {
            lock (lock_fs)
            {
                if (_fsRollback != null)
                    _fsRollback.Dispose();
                rth.configuration.FSFactory.Delete(_fileName + ".rol");
                _fsRollback = rth.configuration.FSFactory.CreateType1(_fileName + ".rol", _fileStreamBufferSize);
            }
            return Success();
        }

        byte[] Write(IFileStream stream, long position, bool withFlush, byte[] data, int offset, int count)
        {
            if (stream == null)
                throw new InvalidOperationException("The remote table is not open.");
            stream.Position = position;
            stream.Write(data, offset, count);
            if (withFlush)
                FSR.NET_Flush(stream);
            return CreateLengthResponse(stream.Length);
        }

        byte[] Read(IFileStream stream, long position, int count)
        {
            if (stream == null)
                throw new InvalidOperationException("The remote table is not open.");
            long available = position < stream.Length ? stream.Length - position : 0;
            int payloadLength = available < count ? (int)available : count;
            byte[] response = new byte[checked(payloadLength + 1)];
            response[0] = ProtocolVersion;
            if (payloadLength == 0)
                return response;

            stream.Position = position;
            int responseOffset = 1;
            int remaining = payloadLength;
            while (remaining > 0)
            {
                int read = stream.Read(response, responseOffset, remaining);
                if (read == 0)
                    throw new EndOfStreamException("Unexpected end of remote table file.");
                responseOffset += read;
                remaining -= read;
            }
            return response;
        }

        void CloseFiles()
        {
            if (_fsData != null) { _fsData.Dispose(); _fsData = null; }
            if (_fsRollback != null) { _fsRollback.Dispose(); _fsRollback = null; }
            if (_fsRollbackHelper != null) { _fsRollbackHelper.Dispose(); _fsRollbackHelper = null; }
        }

        byte[] CreateLengthResponse(long length)
        {
            byte[] response = new byte[9];
            response[0] = ProtocolVersion;
            Buffer.BlockCopy(BitConverter.GetBytes(length), 0, response, 1, 8);
            return response;
        }

        byte[] Success() { return new byte[] { ProtocolVersion }; }
    }
}
