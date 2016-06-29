/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
//using System.Threading.Tasks;
using System.IO;

using DBreeze.Utils;


namespace DBreeze.Storage.RemoteInstance
{
    /// <summary>
    /// Represents one table, is managed by RemoteTablesHandler, server data, rollback and rollback helper files.
    /// </summary>
    internal class RemoteTable:IDisposable
    {
        ulong tableId = 0;
        string databasePreFolderPath = String.Empty;
        object lock_fs = new object();

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

                byte[] protocol = new byte[] { ProtocolVersion }   //Protocol version
                                  .ConcatMany(
                                    BitConverter.GetBytes(tableId),
                                    BitConverter.GetBytes(_fsData.Length),
                                    BitConverter.GetBytes(_fsRollback.Length),
                                    BitConverter.GetBytes(_fsRollbackHelper.Length)
                                  );
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
        }

        /// <summary>
        /// CloseRemoteTable
        /// </summary>
        public byte[] CloseRemoteTable()
        {
            lock (lock_fs)
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
        public byte[] DataFileWrite(long position, bool withFlush, byte[] data)
        {
            lock (lock_fs)
            {
                _fsData.Position = position;
                _fsData.Write(data, 0, data.Length);

                if (withFlush)
                    FSR.NET_Flush(_fsData);

                byte[] protocol = new byte[] { ProtocolVersion }   //Protocol version
                                      .Concat(BitConverter.GetBytes(_fsData.Length));
                return protocol;
            }            
        }

        /// <summary>
        /// RollbackFileWrite
        /// </summary>
        /// <param name="position"></param>
        /// <param name="withFlush"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public byte[] RollbackFileWrite(long position, bool withFlush, byte[] data)
        {
            lock (lock_fs)
            {
                _fsRollback.Position = position;
                _fsRollback.Write(data, 0, data.Length);

                if (withFlush)
                    FSR.NET_Flush(_fsRollback);

                byte[] protocol = new byte[] { ProtocolVersion }   //Protocol version
                                      .Concat(BitConverter.GetBytes(_fsRollback.Length));
                return protocol;
            }
        }


        /// <summary>
        /// RollbackFileWrite
        /// </summary>
        /// <param name="position"></param>
        /// <param name="withFlush"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public byte[] RollbackHelperFileWrite(long position, bool withFlush, byte[] data)
        {
            lock (lock_fs)
            {
                _fsRollbackHelper.Position = position;
                _fsRollbackHelper.Write(data, 0, data.Length);

                if (withFlush)
                    FSR.NET_Flush(_fsRollbackHelper);

                byte[] protocol = new byte[] { ProtocolVersion }   //Protocol version
                                      .Concat(BitConverter.GetBytes(_fsRollbackHelper.Length));
                return protocol;
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
                byte[] bt = new byte[count];
                _fsData.Position = position;
                _fsData.Read(bt, 0, bt.Length);

                byte[] protocol = new byte[] { ProtocolVersion }   //Protocol version
                                   .Concat(bt);
                return protocol;
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
                byte[] bt = new byte[count];
                _fsRollback.Position = position;
                _fsRollback.Read(bt, 0, bt.Length);

                byte[] protocol = new byte[] { ProtocolVersion }   //Protocol version
                                   .Concat(bt);
                return protocol;
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
                byte[] bt = new byte[count];
                _fsRollbackHelper.Position = position;
                _fsRollbackHelper.Read(bt, 0, bt.Length);

                byte[] protocol = new byte[] { ProtocolVersion }   //Protocol version
                                   .Concat(bt);
                return protocol;
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


       
    }//eoc
}
