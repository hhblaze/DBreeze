/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
//using System.Threading.Tasks;

using DBreeze.Utils;

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
            this.tableName = tableName;
            byte[] btTblName = System.Text.Encoding.UTF8.GetBytes(tableName);
            byte[] protocol = new byte[] { ProtocolVersion, 1 }   //Protocol version
                              .ConcatMany(
                                BitConverter.GetBytes(btTblName.Length),
                                btTblName
                              );

            //!!!!!!!!!Check throwing exception in send
            byte[] ret = Com.Send(protocol);        

            //Parsing Answer
            if (ret[0] == 1)    //For protocol version 1
            {
                //First 8 bytes = RemoteTableId
                RemoteTableId = BitConverter.ToUInt64(ret, 1);
                //We need all remote table files lengthes
                _DataFileLength = BitConverter.ToInt64(ret, 9);
                _RollbackFileLength = BitConverter.ToInt64(ret, 17);
                _RollbackHelperFileLength = BitConverter.ToInt64(ret, 25);
            }
            else if (ret[0] == 255)
                throw new Exception("DBreeze.Storage.RemoteInstance.RemoteInstanceCommander.OpenRemoteTable: remote exception");

        }

        /// <summary>
        /// CloseRemoteTable, returns nothing
        /// </summary>
        public void CloseRemoteTable()
        {
            byte[] protocol = new byte[] { ProtocolVersion, 2 }
                              .ConcatMany(BitConverter.GetBytes(RemoteTableId));

            byte[] ret = Com.Send(protocol);

            if (ret[0] == 255)
                throw new Exception("DBreeze.Storage.RemoteInstance.RemoteInstanceCommander.CloseRemoteTable: remote exception");
        }

        /// <summary>
        /// DeleteRemoteTable
        /// </summary>
        public void DeleteRemoteTable()
        {
            byte[] protocol = new byte[] { ProtocolVersion, 3 }
                              .ConcatMany(BitConverter.GetBytes(RemoteTableId));

            byte[] ret = Com.Send(protocol);

            if (ret[0] == 255)
                throw new Exception("DBreeze.Storage.RemoteInstance.RemoteInstanceCommander.DeleteRemoteTable: remote exception");
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
            byte[] protocol = new byte[] { ProtocolVersion, 4 }
                              .ConcatMany(
                              BitConverter.GetBytes(RemoteTableId),
                              BitConverter.GetBytes(this._DataFilePosition),
                              (withFlush) ? new byte[] {1} : new byte[] {0},
                              array.Substring(offset,count)
                              );

            byte[] ret = Com.Send(protocol);

            if (ret[0] == 1)    //For protocol version 1
            {                
                _DataFileLength = BitConverter.ToInt64(ret, 1);             
            }
            else if (ret[0] == 255)
                throw new Exception("DBreeze.Storage.RemoteInstance.RemoteInstanceCommander.DataFileWrite: remote exception");
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
            byte[] protocol = new byte[] { ProtocolVersion, 5 }
                             .ConcatMany(
                             BitConverter.GetBytes(RemoteTableId),
                             BitConverter.GetBytes(this._RollbackFilePosition),
                             (withFlush) ? new byte[] { 1 } : new byte[] { 0 },
                             array.Substring(offset, count)
                             );

            byte[] ret = Com.Send(protocol);

            if (ret[0] == 1)    //For protocol version 1
            {
                _RollbackFileLength = BitConverter.ToInt64(ret, 1);
            }
            else if (ret[0] == 255)
                throw new Exception("DBreeze.Storage.RemoteInstance.RemoteInstanceCommander.RollbackFileWrite: remote exception");
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
            byte[] protocol = new byte[] { ProtocolVersion, 6 }
                             .ConcatMany(
                             BitConverter.GetBytes(RemoteTableId),
                             BitConverter.GetBytes(this._RollbackHelperFilePosition),
                             (withFlush) ? new byte[] { 1 } : new byte[] { 0 },
                             array.Substring(offset, count)
                             );

            byte[] ret = Com.Send(protocol);

            if (ret[0] == 1)    //For protocol version 1
            {
                _RollbackHelperFileLength = BitConverter.ToInt64(ret, 1);
            }
            else if (ret[0] == 255)
                throw new Exception("DBreeze.Storage.RemoteInstance.RemoteInstanceCommander.RollbackHelperFileWrite: remote exception");
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
            byte[] protocol = new byte[] { ProtocolVersion, 7 }
                              .ConcatMany(
                              BitConverter.GetBytes(RemoteTableId),
                              BitConverter.GetBytes(this._DataFilePosition),                             
                              BitConverter.GetBytes(count-offset)
                              );

            byte[] ret = Com.Send(protocol);

            if (ret[0] == 1)    //For protocol version 1
            {
                if (ret.Length == 1)
                {
                    (new byte[0]).CopyTo(array, 0);
                }
                else
                    ret.Substring(1).CopyTo(array, 0);  //not to loose ref object
            }
            //else if (ret[0] == 254)
            //{
            //    //Trying to reconnect
            //    OpenRemoteTable(this.tableName);
            //    DataFileRead(array, offset, count);
            //}
            else if (ret[0] == 255)
                throw new Exception("DBreeze.Storage.RemoteInstance.RemoteInstanceCommander.DataFileRead: remote exception");

            return (array == null) ? 0 : array.Length;
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
            byte[] protocol = new byte[] { ProtocolVersion, 8 }
                              .ConcatMany(
                              BitConverter.GetBytes(RemoteTableId),
                              BitConverter.GetBytes(this._RollbackFilePosition),
                              BitConverter.GetBytes(count - offset)
                              );

            byte[] ret = Com.Send(protocol);

            if (ret[0] == 1)    //For protocol version 1
            {
                if (ret.Length == 1)
                {
                    (new byte[0]).CopyTo(array, 0);
                }
                else
                    ret.Substring(1).CopyTo(array, 0);  //not to loose ref object                
            }
            else if (ret[0] == 255)
                throw new Exception("DBreeze.Storage.RemoteInstance.RemoteInstanceCommander.RollbackFileRead: remote exception");

            return (array == null) ? 0 : array.Length;
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
            byte[] protocol = new byte[] { ProtocolVersion, 9 }
                              .ConcatMany(
                              BitConverter.GetBytes(RemoteTableId),
                              BitConverter.GetBytes(this._RollbackHelperFilePosition),
                              BitConverter.GetBytes(count - offset)
                              );

            byte[] ret = Com.Send(protocol);

            if (ret[0] == 1)    //For protocol version 1
            {
                if (ret.Length == 1)
                {
                    (new byte[0]).CopyTo(array, 0);
                }
                else
                    ret.Substring(1).CopyTo(array, 0);  //not to loose ref object
            }
            else if (ret[0] == 255)
                throw new Exception("DBreeze.Storage.RemoteInstance.RemoteInstanceCommander.RollbackHelperFileRead: remote exception");

            return (array == null) ? 0 : array.Length;
        }
        #endregion
        
        #region Flush

        /// <summary>
        /// Data file Flush
        /// </summary>
        public void DataFileFlush()
        {
            byte[] protocol = new byte[] { ProtocolVersion, 10 }
                             .ConcatMany(
                             BitConverter.GetBytes(RemoteTableId)
                             );

            byte[] ret = Com.Send(protocol);

            if (ret[0] == 255)
                throw new Exception("DBreeze.Storage.RemoteInstance.RemoteInstanceCommander.DataFileFlush: remote exception");
        }

        /// <summary>
        /// Rollback file flush
        /// </summary>
        public void RollbackFileFlush()
        {
            byte[] protocol = new byte[] { ProtocolVersion, 11 }
                             .ConcatMany(
                             BitConverter.GetBytes(RemoteTableId)
                             );

            byte[] ret = Com.Send(protocol);

            if (ret[0] == 255)
                throw new Exception("DBreeze.Storage.RemoteInstance.RemoteInstanceCommander.RollbackFileFlush: remote exception");
        }
        #endregion

        /// <summary>
        /// RollbackFileRecreate
        /// </summary>
        public void RollbackFileRecreate()
        {
            byte[] protocol = new byte[] { ProtocolVersion, 12 }
                             .ConcatMany(
                             BitConverter.GetBytes(RemoteTableId)
                             );

            byte[] ret = Com.Send(protocol);

            if (ret[0] == 255)
                throw new Exception("DBreeze.Storage.RemoteInstance.RemoteInstanceCommander.RollbackFileRecreate: remote exception");

            _RollbackFileLength = 0;
        }

        #endregion


    }
}
