/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.IO;

namespace DBreeze.Storage.RemoteInstance
{
    internal class RemoteInstanceCommander// : IRemoteInstanceCommander
    {
        long _DataFileLength = 0;
        long _RollbackFileLength = 0;
        long _RollbackHelperFileLength = 0;

        long _DataFilePosition = 0;
        long _RollbackFilePosition = 0;
        long _RollbackHelperFilePosition = 0;

        string tableName = String.Empty;
        ulong RemoteTableId = 0;

        IRemoteInstanceCommunicator Com = null;
        byte ProtocolVersion = 1;
        bool _isOpen = false;

        public RemoteInstanceCommander(IRemoteInstanceCommunicator communicator)
        {
            if (communicator == null)
                throw new Exception("DBreeze.Storage.RemoteInstance.RemoteInstanceCommander supplied IRemoteInstanceCommunicator is null");

            this.Com = communicator;
        }

        #region "Submission protocol"
        /*
         * 1byte - protocol version
         * 
         * For protocol version 1
         * 1byte - type of transmission (256 possible)
         *      FirstNyteValue;Command
         *              SubProtocol bytes sequence and explanation
         *              
         *      1 - OpenRemoteTable
         *              N bytes - FullPath to the file as System.Text.Encoding.UTF8.GetBytes
         *      2 - CloseRemoteTable
         *              8 bytes - RemoteTableId
         *      3 - DeleteRemoteTable
         *              8 bytes - RemoteTableId
         *      4 - DataFileWrite
         *      5 - RollbackFileWrite
         *      6 - RollbackHelperFileWrite
         *      7 - DataFileRead
         *      8 - RollbackFileRead
         *      9 - RollbackHelperFileRead
         *      10 - Data file flush
         *      11 - Rollback file flush
         *      12 - Rollback file recreate
         */
        #endregion

        #region "Local 2 Remote"

        /// <summary>
        /// Always first command, which send table name and receives back RemoteTableId
        /// Opens remote table (data, rollback and rollback helper files), if it doesn't exists, then creates it.
        /// All other operations are based on this RemoteTableId.
        /// </summary>
        /// <param name="fileName"></param>
        public void OpenRemoteTable(string tableName)
        {
            if (tableName == null)
                throw new ArgumentNullException("tableName");
            if (_isOpen)
            {
                if (String.Equals(this.tableName, tableName, StringComparison.Ordinal))
                    return;
                throw new InvalidOperationException("A remote table is already open by this commander.");
            }

            this.tableName = tableName;
            byte[] btTblName = System.Text.Encoding.UTF8.GetBytes(tableName);
            byte[] protocol = new byte[6 + btTblName.Length];
            protocol[0] = ProtocolVersion;
            protocol[1] = 1;
            Buffer.BlockCopy(BitConverter.GetBytes(btTblName.Length), 0, protocol, 2, 4);
            Buffer.BlockCopy(btTblName, 0, protocol, 6, btTblName.Length);

            byte[] ret = SendExact(protocol, "OpenRemoteTable", 33);
            RemoteTableId = BitConverter.ToUInt64(ret, 1);
            _DataFileLength = BitConverter.ToInt64(ret, 9);
            _RollbackFileLength = BitConverter.ToInt64(ret, 17);
            _RollbackHelperFileLength = BitConverter.ToInt64(ret, 25);
            _DataFilePosition = 0;
            _RollbackFilePosition = 0;
            _RollbackHelperFilePosition = 0;
            _isOpen = true;

        }

        /// <summary>
        /// CloseRemoteTable, returns nothing
        /// </summary>
        public void CloseRemoteTable()
        {
            if (!_isOpen)
                return;

            SendExact(CreateTableCommand(2), "CloseRemoteTable", 1);
            _isOpen = false;
        }

        /// <summary>
        /// DeleteRemoteTable
        /// </summary>
        public void DeleteRemoteTable()
        {
            if (!_isOpen)
                return;

            SendExact(CreateTableCommand(3), "DeleteRemoteTable", 1);
            _isOpen = false;
            _DataFileLength = 0;
            _RollbackFileLength = 0;
            _RollbackHelperFileLength = 0;
        }
               

        #region "Lengthes and positions"
        public long DataFileLength
        {
            get
            {
                return this._DataFileLength;
            }
        }

        public long RollbackFileLength
        {
            get
            {
                return this._RollbackFileLength;
            }
        }

        public long RollbackHelperFileLength
        {
            get
            {
                return this._RollbackHelperFileLength;
            }
        }
        
        public long DataFilePosition
        {
            get
            {
                return this._DataFilePosition;
            }
            set
            {
                this._DataFilePosition = value;
            }
        }

        public long RollbackFilePosition
        {
            get
            {
                return this._RollbackFilePosition;
            }
            set
            {
                this._RollbackFilePosition = value;
            }
        }

        public long RollbackHelperFilePosition
        {
            get
            {
                return this._RollbackHelperFilePosition;
            }
            set
            {
                this._RollbackHelperFilePosition = value;
            }
        }
        #endregion

        #region "Writes"
        /// <summary>
        /// Writes to remote data file, return sets DataFileLength
        /// </summary>
        /// <param name="array"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <param name="withFlush"></param>
        public void DataFileWrite(byte[] array, int offset, int count, bool withFlush)
        {
            byte[] ret = Write(4, _DataFilePosition, array, offset, count, withFlush, "DataFileWrite");
            _DataFileLength = BitConverter.ToInt64(ret, 1);
            _DataFilePosition += count;
        }

        /// <summary>
        /// Writes to remote rollback file, return sets RollbackFileLength
        /// </summary>
        /// <param name="array"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <param name="withFlush"></param>
        public void RollbackFileWrite(byte[] array, int offset, int count, bool withFlush)
        {
            byte[] ret = Write(5, _RollbackFilePosition, array, offset, count, withFlush, "RollbackFileWrite");
            _RollbackFileLength = BitConverter.ToInt64(ret, 1);
            _RollbackFilePosition += count;
        }

        /// <summary>
        /// Writes to remote rollback helper file, return sets RollbackFileHelperLength
        /// </summary>
        /// <param name="array"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <param name="withFlush"></param>
        public void RollbackHelperFileWrite(byte[] array, int offset, int count, bool withFlush)
        {
            byte[] ret = Write(6, _RollbackHelperFilePosition, array, offset, count, withFlush, "RollbackHelperFileWrite");
            _RollbackHelperFileLength = BitConverter.ToInt64(ret, 1);
            _RollbackHelperFilePosition += count;
        }

        #endregion

        #region Reads

        /// <summary>
        /// Reads Datafile
        /// </summary>
        /// <param name="array"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public int DataFileRead(byte[] array, int offset, int count)
        {
            int read = Read(7, _DataFilePosition, array, offset, count, "DataFileRead");
            _DataFilePosition += read;
            return read;
        }

        /// <summary>
        /// RollbackFileRead
        /// </summary>
        /// <param name="array"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public int RollbackFileRead(byte[] array, int offset, int count)
        {
            int read = Read(8, _RollbackFilePosition, array, offset, count, "RollbackFileRead");
            _RollbackFilePosition += read;
            return read;
        }

        /// <summary>
        /// RollbackHelperFileRead
        /// </summary>
        /// <param name="array"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public int RollbackHelperFileRead(byte[] array, int offset, int count)
        {
            int read = Read(9, _RollbackHelperFilePosition, array, offset, count, "RollbackHelperFileRead");
            _RollbackHelperFilePosition += read;
            return read;
        }
        #endregion
        
        #region Flush

        /// <summary>
        /// Data file Flush
        /// </summary>
        public void DataFileFlush()
        {
            EnsureOpen();
            SendExact(CreateTableCommand(10), "DataFileFlush", 1);
        }

        /// <summary>
        /// Rollback file flush
        /// </summary>
        public void RollbackFileFlush()
        {
            EnsureOpen();
            SendExact(CreateTableCommand(11), "RollbackFileFlush", 1);
        }
        #endregion

        /// <summary>
        /// RollbackFileRecreate
        /// </summary>
        public void RollbackFileRecreate()
        {
            EnsureOpen();
            SendExact(CreateTableCommand(12), "RollbackFileRecreate", 1);

            _RollbackFileLength = 0;
            _RollbackFilePosition = 0;
        }

        private byte[] CreateTableCommand(byte command)
        {
            EnsureOpen();
            byte[] protocol = new byte[10];
            protocol[0] = ProtocolVersion;
            protocol[1] = command;
            Buffer.BlockCopy(BitConverter.GetBytes(RemoteTableId), 0, protocol, 2, 8);
            return protocol;
        }

        private byte[] Write(byte command, long position, byte[] array, int offset, int count, bool withFlush, string operation)
        {
            EnsureOpen();
            ValidateBuffer(array, offset, count);
            if (position < 0)
                throw new ArgumentOutOfRangeException("position");
            if (position > Int64.MaxValue - count)
                throw new ArgumentOutOfRangeException("position/count");

            byte[] protocol = new byte[19 + count];
            protocol[0] = ProtocolVersion;
            protocol[1] = command;
            Buffer.BlockCopy(BitConverter.GetBytes(RemoteTableId), 0, protocol, 2, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(position), 0, protocol, 10, 8);
            protocol[18] = withFlush ? (byte)1 : (byte)0;
            if (count != 0)
                Buffer.BlockCopy(array, offset, protocol, 19, count);
            return SendExact(protocol, operation, 9);
        }

        private int Read(byte command, long position, byte[] array, int offset, int count, string operation)
        {
            EnsureOpen();
            ValidateBuffer(array, offset, count);
            if (position < 0)
                throw new ArgumentOutOfRangeException("position");

            byte[] protocol = new byte[22];
            protocol[0] = ProtocolVersion;
            protocol[1] = command;
            Buffer.BlockCopy(BitConverter.GetBytes(RemoteTableId), 0, protocol, 2, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(position), 0, protocol, 10, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(count), 0, protocol, 18, 4);

            byte[] response = Send(protocol, operation, 1);
            int read = response.Length - 1;
            if (read > count)
                throw new InvalidOperationException(operation + ": remote response is larger than requested.");
            if (read != 0)
                Buffer.BlockCopy(response, 1, array, offset, read);
            return read;
        }

        private byte[] Send(byte[] protocol, string operation, int minimumLength)
        {
            byte[] response = Com.Send(protocol);
            if (response == null || response.Length == 0 || response[0] == 255)
                throw new InvalidOperationException("DBreeze remote operation failed: " + operation + ".");
            if (response[0] != ProtocolVersion || response.Length < minimumLength)
                throw new InvalidDataException("Invalid DBreeze remote response: " + operation + ".");
            return response;
        }

        private byte[] SendExact(byte[] protocol, string operation, int expectedLength)
        {
            byte[] response = Send(protocol, operation, expectedLength);
            if (response.Length != expectedLength)
                throw new InvalidDataException("Invalid DBreeze remote response length: " + operation + ".");
            return response;
        }

        private static void ValidateBuffer(byte[] array, int offset, int count)
        {
            if (array == null)
                throw new ArgumentNullException("array");
            if (offset < 0 || count < 0 || offset > array.Length - count)
                throw new ArgumentOutOfRangeException("offset/count");
        }

        private void EnsureOpen()
        {
            if (!_isOpen)
                throw new InvalidOperationException("The remote table is not open.");
        }

        #endregion


    }
}
