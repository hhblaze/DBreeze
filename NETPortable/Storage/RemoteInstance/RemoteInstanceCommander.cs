/*
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.IO;

namespace DBreeze.Storage.RemoteInstance
{
    internal class RemoteInstanceCommander
    {
        long _DataFileLength;
        long _RollbackFileLength;
        long _RollbackHelperFileLength;
        long _DataFilePosition;
        long _RollbackFilePosition;
        long _RollbackHelperFilePosition;
        string tableName = String.Empty;
        ulong RemoteTableId;
        readonly IRemoteInstanceCommunicator Com;
        readonly byte ProtocolVersion = 1;
        bool _isOpen;

        public RemoteInstanceCommander(IRemoteInstanceCommunicator communicator)
        {
            if (communicator == null)
                throw new ArgumentNullException("communicator");
            Com = communicator;
        }

        public void OpenRemoteTable(string newTableName)
        {
            if (newTableName == null)
                throw new ArgumentNullException("newTableName");
            if (_isOpen)
            {
                if (String.Equals(tableName, newTableName, StringComparison.Ordinal))
                    return;
                throw new InvalidOperationException("A remote table is already open by this commander.");
            }

            byte[] tableNameBytes = System.Text.Encoding.UTF8.GetBytes(newTableName);
            byte[] protocol = new byte[checked(6 + tableNameBytes.Length)];
            protocol[0] = ProtocolVersion;
            protocol[1] = 1;
            Buffer.BlockCopy(BitConverter.GetBytes(tableNameBytes.Length), 0, protocol, 2, 4);
            Buffer.BlockCopy(tableNameBytes, 0, protocol, 6, tableNameBytes.Length);

            byte[] response = SendExact(protocol, "OpenRemoteTable", 33);
            RemoteTableId = BitConverter.ToUInt64(response, 1);
            _DataFileLength = BitConverter.ToInt64(response, 9);
            _RollbackFileLength = BitConverter.ToInt64(response, 17);
            _RollbackHelperFileLength = BitConverter.ToInt64(response, 25);
            _DataFilePosition = 0;
            _RollbackFilePosition = 0;
            _RollbackHelperFilePosition = 0;
            tableName = newTableName;
            _isOpen = true;
        }

        public void CloseRemoteTable()
        {
            if (!_isOpen)
                return;
            SendExact(CreateTableCommand(2), "CloseRemoteTable", 1);
            _isOpen = false;
        }

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

        public long DataFileLength { get { return _DataFileLength; } }
        public long RollbackFileLength { get { return _RollbackFileLength; } }
        public long RollbackHelperFileLength { get { return _RollbackHelperFileLength; } }
        public long DataFilePosition { get { return _DataFilePosition; } set { _DataFilePosition = value; } }
        public long RollbackFilePosition { get { return _RollbackFilePosition; } set { _RollbackFilePosition = value; } }
        public long RollbackHelperFilePosition { get { return _RollbackHelperFilePosition; } set { _RollbackHelperFilePosition = value; } }

        public void DataFileWrite(byte[] array, int offset, int count, bool withFlush)
        {
            byte[] response = Write(4, _DataFilePosition, array, offset, count, withFlush, "DataFileWrite");
            _DataFileLength = BitConverter.ToInt64(response, 1);
            _DataFilePosition = checked(_DataFilePosition + count);
        }

        public void RollbackFileWrite(byte[] array, int offset, int count, bool withFlush)
        {
            byte[] response = Write(5, _RollbackFilePosition, array, offset, count, withFlush, "RollbackFileWrite");
            _RollbackFileLength = BitConverter.ToInt64(response, 1);
            _RollbackFilePosition = checked(_RollbackFilePosition + count);
        }

        public void RollbackHelperFileWrite(byte[] array, int offset, int count, bool withFlush)
        {
            byte[] response = Write(6, _RollbackHelperFilePosition, array, offset, count, withFlush, "RollbackHelperFileWrite");
            _RollbackHelperFileLength = BitConverter.ToInt64(response, 1);
            _RollbackHelperFilePosition = checked(_RollbackHelperFilePosition + count);
        }

        public int DataFileRead(byte[] array, int offset, int count)
        {
            int read = Read(7, _DataFilePosition, array, offset, count, "DataFileRead");
            _DataFilePosition = checked(_DataFilePosition + read);
            return read;
        }

        public int RollbackFileRead(byte[] array, int offset, int count)
        {
            int read = Read(8, _RollbackFilePosition, array, offset, count, "RollbackFileRead");
            _RollbackFilePosition = checked(_RollbackFilePosition + read);
            return read;
        }

        public int RollbackHelperFileRead(byte[] array, int offset, int count)
        {
            int read = Read(9, _RollbackHelperFilePosition, array, offset, count, "RollbackHelperFileRead");
            _RollbackHelperFilePosition = checked(_RollbackHelperFilePosition + read);
            return read;
        }

        public void DataFileFlush() { SendExact(CreateTableCommand(10), "DataFileFlush", 1); }
        public void RollbackFileFlush() { SendExact(CreateTableCommand(11), "RollbackFileFlush", 1); }

        public void RollbackFileRecreate()
        {
            SendExact(CreateTableCommand(12), "RollbackFileRecreate", 1);
            _RollbackFileLength = 0;
            _RollbackFilePosition = 0;
        }

        byte[] CreateTableCommand(byte command)
        {
            EnsureOpen();
            byte[] protocol = new byte[10];
            protocol[0] = ProtocolVersion;
            protocol[1] = command;
            Buffer.BlockCopy(BitConverter.GetBytes(RemoteTableId), 0, protocol, 2, 8);
            return protocol;
        }

        byte[] Write(byte command, long position, byte[] array, int offset, int count, bool withFlush, string operation)
        {
            EnsureOpen();
            ValidateBuffer(array, offset, count);
            ValidatePosition(position, count);
            byte[] protocol = new byte[checked(19 + count)];
            protocol[0] = ProtocolVersion;
            protocol[1] = command;
            Buffer.BlockCopy(BitConverter.GetBytes(RemoteTableId), 0, protocol, 2, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(position), 0, protocol, 10, 8);
            protocol[18] = withFlush ? (byte)1 : (byte)0;
            if (count != 0)
                Buffer.BlockCopy(array, offset, protocol, 19, count);
            return SendExact(protocol, operation, 9);
        }

        int Read(byte command, long position, byte[] array, int offset, int count, string operation)
        {
            EnsureOpen();
            ValidateBuffer(array, offset, count);
            ValidatePosition(position, count);
            byte[] protocol = new byte[22];
            protocol[0] = ProtocolVersion;
            protocol[1] = command;
            Buffer.BlockCopy(BitConverter.GetBytes(RemoteTableId), 0, protocol, 2, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(position), 0, protocol, 10, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(count), 0, protocol, 18, 4);

            byte[] response = Send(protocol, operation, 1);
            int read = response.Length - 1;
            if (read > count)
                throw new InvalidDataException(operation + ": remote response is larger than requested.");
            if (read != 0)
                Buffer.BlockCopy(response, 1, array, offset, read);
            return read;
        }

        byte[] Send(byte[] protocol, string operation, int minimumLength)
        {
            byte[] response = Com.Send(protocol);
            if (response == null || response.Length == 0 || response[0] == 255)
                throw new InvalidOperationException("DBreeze remote operation failed: " + operation + ".");
            if (response[0] != ProtocolVersion || response.Length < minimumLength)
                throw new InvalidDataException("Invalid DBreeze remote response: " + operation + ".");
            return response;
        }

        byte[] SendExact(byte[] protocol, string operation, int expectedLength)
        {
            byte[] response = Send(protocol, operation, expectedLength);
            if (response.Length != expectedLength)
                throw new InvalidDataException("Invalid DBreeze remote response length: " + operation + ".");
            return response;
        }

        static void ValidateBuffer(byte[] array, int offset, int count)
        {
            if (array == null)
                throw new ArgumentNullException("array");
            if (offset < 0 || count < 0 || offset > array.Length - count)
                throw new ArgumentOutOfRangeException("offset/count");
        }

        static void ValidatePosition(long position, int count)
        {
            if (position < 0 || position > Int64.MaxValue - count)
                throw new ArgumentOutOfRangeException("position/count");
        }

        void EnsureOpen()
        {
            if (!_isOpen)
                throw new InvalidOperationException("The remote table is not open.");
        }
    }
}
