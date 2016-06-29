/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
//using System.Threading.Tasks;
using System.Threading;

using DBreeze.Utils;

namespace DBreeze.Storage.RemoteInstance
{
    /// <summary>
    /// ServerSide. Servs one local database.
    /// </summary>
    public class RemoteTablesHandler:IDisposable
    {
        ReaderWriterLockSlim _sync = new ReaderWriterLockSlim();
        Dictionary<ulong, RemoteTable> _t = new Dictionary<ulong, RemoteTable>();
        /// <summary>
        /// fileName to id binding
        /// </summary>
        Dictionary<string, ulong> _tIds = new Dictionary<string, ulong>();
        ulong tableId = 0;
        string databasePreFolderPath = String.Empty;
        bool directoryIsNotCreated = true;

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
                foreach (var rt in _t)
                    rt.Value.Dispose();

                _t.Clear();
                _tIds.Clear();
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
                ulong callTableId = 0;
                RemoteTable rt = null;
                byte[] ret = null;

                if (protocol[0] == 1)   //Protocol 1
                {

                    if (protocol[1] != 1)
                    {
                        callTableId = BitConverter.ToUInt64(protocol, 2);

                        _sync.EnterReadLock();
                        try
                        {
                            if (!_t.TryGetValue(callTableId, out rt))
                            {
                                //throw new Exception("table can't be find by id");
                                return new byte[] { 255 };  //Protocol 255 means error of operation and must raise an exception
                            }
                        }
                        finally
                        {
                            _sync.ExitReadLock();
                        }
                    }

                    switch (protocol[1])
                    {
                        case 1:
                            #region "OpenRemoteTable"
                            //Special parsing
                            int tblLen = BitConverter.ToInt32(protocol, 2);
                            string tblName = System.Text.Encoding.UTF8.GetString(protocol.Substring(6,tblLen));
                            string _fileName = System.IO.Path.Combine(databasePreFolderPath, tblName);
                         
                            _sync.EnterUpgradeableReadLock();
                            try
                            {
                                if (!_tIds.TryGetValue(_fileName, out callTableId))
                                {
                                    _sync.EnterWriteLock();
                                    try
                                    {
                                        if (!_tIds.TryGetValue(_fileName, out callTableId))
                                        {
                                            tableId++;

                                            //Creating directory, if necessary
                                            if (directoryIsNotCreated)
                                            {
                                                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_fileName));
                                                directoryIsNotCreated = false;
                                            }

                                            rt = new RemoteTable(_fileName, tableId);
                                            _t[tableId] = rt;
                                            _tIds[_fileName] = tableId;
                                        }
                                    }
                                    finally
                                    {
                                        _sync.ExitWriteLock();
                                    }
                                }
                                else
                                    _t.TryGetValue(callTableId, out rt);
                            }
                            finally
                            {
                                _sync.ExitUpgradeableReadLock();
                            }

                            return rt.OpenRemoteTable();
                            #endregion
                        case 2:
                            #region "CloseRemoteTable"

                            return rt.CloseRemoteTable();

                            #endregion
                        case 3:
                            #region "DeleteRemoteTable"
                            ret = rt.DeleteRemoteTable();

                            _sync.EnterWriteLock();
                            try
                            {
                                _tIds.Remove(rt._fileName);                                
                                _t.Remove(callTableId);                                
                            }
                            finally
                            {
                                _sync.ExitWriteLock();
                            }

                            return ret;
                            #endregion
                        case 4:
                            #region DataFileWrite
                            return rt.DataFileWrite(BitConverter.ToInt64(protocol, 10), (protocol[18] == 1), protocol.Substring(19));
                            #endregion                           
                        case 5:
                            #region "RollbackFileWrite"
                            return rt.RollbackFileWrite(BitConverter.ToInt64(protocol, 10), (protocol[18] == 1), protocol.Substring(19));
                        #endregion
                        case 6:
                            #region "RollbackHelperFileWrite"
                            return rt.RollbackHelperFileWrite(BitConverter.ToInt64(protocol, 10), (protocol[18] == 1), protocol.Substring(19));
                            #endregion
                        case 7:
                            #region "DataFileRead"
                            return rt.DataFileRead(BitConverter.ToInt64(protocol, 10), BitConverter.ToInt32(protocol, 18));
                        #endregion
                        case 8:
                            #region "RollbackFileRead"
                            return rt.RollbackFileRead(BitConverter.ToInt64(protocol, 10), BitConverter.ToInt32(protocol, 18));
                            #endregion
                        case 9:
                            #region "RollbackHelperFileRead"
                            return rt.RollbackHelperFileRead(BitConverter.ToInt64(protocol, 10), BitConverter.ToInt32(protocol, 18));
                            #endregion
                        case 10:
                            #region "DataFileFlush"
                            return rt.DataFileFlush();
                            #endregion
                        case 11:
                            #region "RollbackFileFlush"
                            return rt.RollbackFileFlush();
                            #endregion
                        case 12:
                            #region "RollbackFileRecreate"
                            return rt.RollbackFileRecreate();
                            #endregion

                    }
                }
            }
            catch// (Exception ex)
            {
                return new byte[] { 255 };
                //throw ex;       //Connector must be disconnected and error must be logged
            }
            return null;
        }

      
    }
}
