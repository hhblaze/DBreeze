/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBreeze.Storage.RemoteInstance
{
    /// <summary>
    /// NSR works via this interface
    /// </summary>
    public interface IRemoteInstanceCommander
    { 
        //!!!!!!!!!!!!!!!!!!   BACKUP HANDLING. MUST BE HANDELED REMOTELY

        /// <summary>
        /// Opens remote file (data, rollback and rollback helper), if it doesn't exists, then creates it.
        /// In background returns current rollback, rollback helper, data file length and RemoteTableId.
        /// All other operations are done using remote table id
        /// </summary>
        /// <param name="fileName"></param>        
        void OpenRemoteTable(string fileName);

        /// <summary>
        /// Closes remote table (data, rollback and rollback helper)
        /// </summary>        
        void CloseRemoteTable();

        /// <summary>
        /// Deletes remote table (data, rollback and rollback helper).
        /// </summary>
        /// <param name="fileName"></param>        
        void DeleteRemoteTable();

        /// <summary>
        /// Operations like OpenRemoteFile, Read etc always return back the length of data and rollback file, which must be set to this variable by interface implementer
        /// </summary>
        long DataFileLength { get; }
        long RollbackFileLength { get; }
        long RollbackHelperFileLength { get; }

        /// <summary>
        /// Sets up data file position for reading or writing
        /// </summary>
        long DataFilePosition { get; set;  }
        /// <summary>
        /// Sets up rollback file position for reading or writing
        /// </summary>
        long RollbackFilePosition { get; set; }
        /// <summary>
        /// Sets up rollback file position for reading or writing
        /// </summary>
        long RollbackHelperFilePosition { get; set; }

        /// <summary>
        /// Writes to remote data file, return sets DataFileLength
        /// </summary>
        /// <param name="array"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        void DataFileWrite(byte[] array, int offset, int count, bool withFlush);
        /// <summary>
        /// Writes to remote rollback file, return sets RollbackFileLength
        /// </summary>
        /// <param name="array"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        void RollbackFileWrite(byte[] array, int offset, int count, bool withFlush);
        /// <summary>
        /// Writes to remote rollback file, return sets RollbackHelperFileLength
        /// </summary>
        /// <param name="array"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        void RollbackHelperFileWrite(byte[] array, int offset, int count, bool withFlush);

        /// <summary>
        /// DataFileRead
        /// </summary>
        /// <param name="array"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        int DataFileRead(byte[] array, int offset, int count);

        /// <summary>
        /// RollbackFileRead
        /// </summary>
        /// <param name="array"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        int RollbackFileRead(byte[] array, int offset, int count);

        /// <summary>
        /// RollbackHelperFileRead
        /// </summary>
        /// <param name="array"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        int RollbackHelperFileRead(byte[] array, int offset, int count);

        /// <summary>
        /// RollbackFileRecreate
        /// </summary>
        void RollbackFileRecreate();

        /// <summary>
        /// DataFileFlush
        /// </summary>
        void DataFileFlush();
        /// <summary>
        /// RollbackFileFlush
        /// </summary>
        void RollbackFileFlush();

        ///// <summary>
        ///// Not necessary
        ///// </summary>
        //void RollbackHelperFileFlush();
    }
}
