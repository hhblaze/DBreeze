/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.IO;


namespace DBreeze.Storage.RemoteInstance
{
    /// <summary>
    /// Represents one table, is managed by RemoteTablesHandler, server data, rollback and rollback helper files.
    /// </summary>
    internal class RemoteTable:IDisposable
    {
        ulong tableId = 0;
        readonly object lock_fs = new object();

        FileStream _fsData = null;
        FileStream _fsRollback = null;
        FileStream _fsRollbackHelper = null;

        int _fileStreamBufferSize = 8192;
        public string _fileName = String.Empty;
        byte ProtocolVersion = 1;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="_fileName"></param>
        /// <param name="tableId"></param>
        public RemoteTable(string _fileName, ulong tableId)
        {
            this._fileName = _fileName;
            this.tableId = tableId;

            string directory = Path.GetDirectoryName(_fileName);
            if (!String.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
        }

        /// <summary>
        /// OpenRemoteTable
        /// </summary>
        /// <returns></returns>
        public byte[] OpenRemoteTable()
        {
            lock (lock_fs)
            {
                if(_fsData == null)
                    this._fsData = new FileStream(this._fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _fileStreamBufferSize, FileOptions.WriteThrough);
                if (_fsRollback == null)
                    this._fsRollback = new FileStream(this._fileName + ".rol", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _fileStreamBufferSize, FileOptions.WriteThrough);
                if (_fsRollbackHelper == null)
                    this._fsRollbackHelper = new FileStream(this._fileName + ".rhp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _fileStreamBufferSize, FileOptions.WriteThrough);

                byte[] protocol = new byte[33];
                protocol[0] = ProtocolVersion;
                Buffer.BlockCopy(BitConverter.GetBytes(tableId), 0, protocol, 1, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(_fsData.Length), 0, protocol, 9, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(_fsRollback.Length), 0, protocol, 17, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(_fsRollbackHelper.Length), 0, protocol, 25, 8);
                return protocol;
            }
        }

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            lock (lock_fs)
            {
                CloseFiles();
            }
        }

        /// <summary>
        /// CloseRemoteTable
        /// </summary>
        public byte[] CloseRemoteTable()
        {
            lock (lock_fs)
            {
                CloseFiles();
            }

            return new byte[] { ProtocolVersion };
        }

        /// <summary>
        /// DeleteRemoteTable
        /// </summary>
        /// <returns></returns>
        public byte[] DeleteRemoteTable()
        {
            lock (lock_fs)
            {
                CloseFiles();

                File.Delete(this._fileName);
                File.Delete(this._fileName + ".rol");
                File.Delete(this._fileName + ".rhp");
            }

            return new byte[] { ProtocolVersion };
        }

        /// <summary>
        /// DataFileWrite
        /// </summary>
        /// <param name="position"></param>
        /// <param name="withFlush"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public byte[] DataFileWrite(long position, bool withFlush, byte[] data, int offset, int count)
        {
            lock (lock_fs)
            {
                _fsData.Position = position;
                _fsData.Write(data, offset, count);
                DurabilityTestHooks.Hit("remote.data.written");

                if (withFlush)
                {
                    FSR.NET_Flush(_fsData);
                    DurabilityTestHooks.Hit("remote.data.flushed");
                }

                return CreateLengthResponse(_fsData.Length);
            }            
        }

        /// <summary>
        /// RollbackFileWrite
        /// </summary>
        /// <param name="position"></param>
        /// <param name="withFlush"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public byte[] RollbackFileWrite(long position, bool withFlush, byte[] data, int offset, int count)
        {
            lock (lock_fs)
            {
                _fsRollback.Position = position;
                _fsRollback.Write(data, offset, count);
                DurabilityTestHooks.Hit("remote.rollback.written");

                if (withFlush)
                {
                    FSR.NET_Flush(_fsRollback);
                    DurabilityTestHooks.Hit("remote.rollback.flushed");
                }

                return CreateLengthResponse(_fsRollback.Length);
            }
        }


        /// <summary>
        /// RollbackFileWrite
        /// </summary>
        /// <param name="position"></param>
        /// <param name="withFlush"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public byte[] RollbackHelperFileWrite(long position, bool withFlush, byte[] data, int offset, int count)
        {
            lock (lock_fs)
            {
                _fsRollbackHelper.Position = position;
                _fsRollbackHelper.Write(data, offset, count);
                DurabilityTestHooks.Hit("remote.rollback-helper.written");

                if (withFlush)
                {
                    FSR.NET_Flush(_fsRollbackHelper);
                    DurabilityTestHooks.Hit("remote.rollback-helper.flushed");
                }

                return CreateLengthResponse(_fsRollbackHelper.Length);
            }
        }

        /// <summary>
        /// DataFileRead
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        public byte[] DataFileRead(long position, int count)
        {
            lock (lock_fs)
            {
                return Read(_fsData, position, count);
            }
        }

        /// <summary>
        /// RollbackFileRead
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        public byte[] RollbackFileRead(long position, int count)
        {
            lock (lock_fs)
            {
                return Read(_fsRollback, position, count);
            }
        }

        /// <summary>
        /// RollbackHelperFileRead
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        public byte[] RollbackHelperFileRead(long position, int count)
        {
            lock (lock_fs)
            {
                return Read(_fsRollbackHelper, position, count);
            }
        }

        /// <summary>
        /// DataFileFlush
        /// </summary>
        /// <returns></returns>
        public byte[] DataFileFlush()
        {
            lock (lock_fs)
            {
                FSR.NET_Flush(_fsData);                
            }

            return new byte[] { ProtocolVersion };
        }

        /// <summary>
        /// RollbackFileFlush
        /// </summary>
        /// <returns></returns>
        public byte[] RollbackFileFlush()
        {
            lock (lock_fs)
            {
                FSR.NET_Flush(_fsRollback);                
            }

            return new byte[] { ProtocolVersion };
        }

        /// <summary>
        /// RollbackFileRecreate
        /// </summary>
        /// <returns></returns>
        public byte[] RollbackFileRecreate()
        {
            lock (lock_fs)
            {
                this._fsRollback.Dispose();
                File.Delete(this._fileName + ".rol");
                this._fsRollback = new FileStream(this._fileName + ".rol", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _fileStreamBufferSize, FileOptions.WriteThrough);
            }
            return new byte[] { ProtocolVersion };
        }

        private void CloseFiles()
        {
            if (_fsData != null)
            {
                _fsData.Dispose();
                _fsData = null;
            }
            if (_fsRollback != null)
            {
                _fsRollback.Dispose();
                _fsRollback = null;
            }
            if (_fsRollbackHelper != null)
            {
                _fsRollbackHelper.Dispose();
                _fsRollbackHelper = null;
            }
        }

        private byte[] CreateLengthResponse(long length)
        {
            byte[] response = new byte[9];
            response[0] = ProtocolVersion;
            Buffer.BlockCopy(BitConverter.GetBytes(length), 0, response, 1, 8);
            return response;
        }

        private byte[] Read(FileStream stream, long position, int count)
        {
            long available = position < stream.Length ? stream.Length - position : 0;
            int payloadLength = available < count ? (int)available : count;
            byte[] response = new byte[payloadLength + 1];
            response[0] = ProtocolVersion;
            if (payloadLength == 0)
                return response;

            stream.Position = position;
            int offset = 1;
            int remaining = payloadLength;
            while (remaining > 0)
            {
                int read = stream.Read(response, offset, remaining);
                if (read == 0)
                    throw new EndOfStreamException("Unexpected end of remote table file.");
                offset += read;
                remaining -= read;
            }
            return response;
        }


       
    }//eoc
}
