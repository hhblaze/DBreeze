/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using DBreeze.Utils;
using DBreeze.Exceptions;

namespace DBreeze.Storage
{
    /// <summary>
    /// DBreeze random and sequential disk IO buffers implementation.
    /// Specially designed for DBreeze specific storage format.
    /// Not for common usage.
    /// </summary>
    internal class MSR : IStorage
    {      

        #region "Variables"

        /// <summary>
        /// Indicates subsystem vitality
        /// </summary>
        public bool IsOperable = true;
        /// <summary>
        /// Random buffer
        /// </summary>
        Dictionary<long, byte[]> _randBuf = new Dictionary<long, byte[]>();

        /// <summary>
        /// Record in rollback is characterized with 
        /// </summary>
        class r
        {
            /// <summary>
            /// offset in rollback file
            /// </summary>
            public long o { get; set; }
            /// <summary>
            /// Length in rollback file
            /// </summary>
            public int l { get; set; }
        }

        /// <summary>
        /// Rollback cache
        /// Key is offset in data file, value is corresponding offset and length in rollback file
        /// </summary>
        Dictionary<long, r> _rollbackCache = new Dictionary<long, r>();

        string _fileName = String.Empty;
       
        /// <summary>
        /// Random buffer maximal size before flush
        /// </summary>
        public int maxRandomBufferSize = 3000000; //Random buffer size before flush
        public int maxRandomElementsCount = 500; //Random buffer maximal quantity of elements before flush

        int usedBufferSize = 0; //Used buffer size before flush
        /// <summary>
        /// Rollback file re-creation after initialization
        /// </summary>
        public int MaxRollbackFileSize = 1048576;

        object lock_fs = new object();
        

        MemoryStorage _fsData = null;
        MemoryStorage _fsRollback = null;
        MemoryStorage _fsRollbackHelper = null;
        /// <summary>
        /// Pointer to the end of file
        /// </summary>
        long eofData = 0;
        long eofRollback = 0;

        TrieSettings _trieSettings = null;
        ushort DefaultPointerLen = 0;
        DBreezeConfiguration _configuration = null;

        /// <summary>
        /// DateTime when file was initialized. Is remembered by LTrieRow, based on this file.
        /// If file is change after RestoreTableFromTheOtherTable or RecreateFiles,
        /// LTrieRow will have different version and will return exception.
        /// </summary>
        DateTime _storageFixTime = DateTime.UtcNow;
        #endregion

        public MSR(string fileName, TrieSettings trieSettings,DBreezeConfiguration configuration)
        {
            this._fileName = fileName;
            this._configuration = configuration;
            this._trieSettings = trieSettings;
            DefaultPointerLen = this._trieSettings.POINTER_LENGTH;

            InitFiles();
        }

        /// <summary>
        /// Physical length of the storage file
        /// </summary>
        public long Length
        {
            get { return eofData; }
        }
        
        /// <summary>
        /// Returns time of file initiation, ead remarks on 
        /// </summary>
        public DateTime StorageFixTime
        {
            get { return _storageFixTime; }
        }

        public TrieSettings TrieSettings
        {
            get { return _trieSettings; }
        }

        public DBreezeConfiguration DbreezeConfiguration
        {
            get { return this._configuration; }
        }

        public string Table_FileName
        {
            get { return this._fileName; }
        }

        /// <summary>
        /// 
        /// </summary>
        public void Table_Dispose()
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

        #region Initialization

        private void InitFiles()
        {
            //Creates filestreams and rollbacks, restores rollback to the initial file, if necessary

            try
            {
                this._fsData = new MemoryStorage(1024 * 16, 1024 * 500, MemoryStorage.eMemoryExpandStartegy.FIXED_LENGTH_INCREASE);
                this._fsRollback = new MemoryStorage(1024 * 16, 1024 * 128, MemoryStorage.eMemoryExpandStartegy.FIXED_LENGTH_INCREASE);
                this._fsRollbackHelper = new MemoryStorage(8, 10, MemoryStorage.eMemoryExpandStartegy.FIXED_LENGTH_INCREASE);

                //Writing root
                this._fsData.Write_ToTheEnd(new byte[64]);

                eofData = this._fsData.EOF;

                _storageFixTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                IsOperable = false;
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DB_IS_NOT_OPERABLE, "MSR INIT FAILED: " + this._fileName, ex);
            }

        }




        #endregion

        public void RestoreTableFromTheOtherTable(string newTableFullPath)
        {
            //DO NOTHING
        }

        #region "Recreate Files"
        /// <summary>
        /// 
        /// </summary>
        public void RecreateFiles()
        {
            lock (lock_fs)
            {
                _fsData.Clear(true);
                _fsRollback.Clear(true);
                _fsRollbackHelper.Clear(true);
              
                _randBuf.Clear();
                _rollbackCache.Clear();
                usedBufferSize = 0;
                eofRollback = 0;
                eofData = 0;

                InitFiles();

            }
        }
        #endregion

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public byte[] Table_WriteToTheEnd(byte[] data)
        {
            long position = 0;

            lock (lock_fs)
            {
                position = _fsData.Write_ToTheEnd(ref data);                
            }
                        
            return ((ulong)position).To_8_bytes_array_BigEndian().Substring(8 - DefaultPointerLen, DefaultPointerLen);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="data"></param>
        public void Table_WriteByOffset(byte[] offset, byte[] data)
        {
            Table_WriteByOffset((long)offset.DynamicLength_To_UInt64_BigEndian(), data);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="data"></param>
        public void Table_WriteByOffset(long offset, byte[] data)
        {
            //DB RULE1. We cant update and go out of the end of file
            //!! both throw new Exception must be taken away after test
            //!! This is a cutted implementation for DBreeze we dont take care buffer elements overlapping (start+len U some elements -> should be not possible)

            if (data == null || data.Length == 0)
                return;     //!!!may be exception

            lock (lock_fs)
            {

                if (offset < _fsData.EOF && offset + data.Length > _fsData.EOF)
                {
                    throw new Exception("FSR.WriteByOffset: offset < _fsData.EOF && offset + data.Length > _fsData.EOF");
                }

                if (offset + data.Length > _fsData.EOF)
                {
                    //DB RULE1. We cant update and go out of the end of file. Only if we write into empty file root in the beginning
                    throw new Exception("FSR.WriteByOffset: offset + data.Length > _fsData.EOF");
                }


                byte[] inBuf = null;
                if (_randBuf.TryGetValue(offset, out inBuf))
                {
                    if (inBuf.Length != data.Length)
                    {
                        //OLD solution
                        //it means we overwrite second time the same position with different length of data - what is not allowed
                        //throw new Exception("MSR.WriteByOffset: inBuf.Length != data.Length");

                        //Solution from 20140425
                        //we just overwrite offset value with the new data
                    }

                    //setting new value for such offset
                    _randBuf[offset] = data;
                }
                else
                {
                    //We put data to the buffer first and flush it if buffer > allowed space. We dont take care if data is bigger then buffer.
                    //In any case first we put it to the buffer 
                    _randBuf.Add(offset, data);
                    usedBufferSize += data.Length;
                }

                //if we are able to store data into buffer lets do it
                if (usedBufferSize >= maxRandomBufferSize || _randBuf.Count() > maxRandomElementsCount)
                    FlushRandomBuffer();
            }
        }


        /// <summary>
        /// Is called only from lock_fs and must be finished by calling NET_Flush
        /// </summary>
        /// <param name="commit"></param>
        void FlushRandomBuffer()
        {
            if (_randBuf.Count() == 0)
            {
                return;
            }

            //First we write all data into rollback file and helper, calling flush on rollback
            //then updating data of data file but dont call update
            //clearing random buffer

            //Creating rollback header           
            byte[] offset = null;
            byte[] btRoll = null;

            bool flushRollback = false;

            //first loop for saving rollback data
            foreach (var de in _randBuf.OrderBy(r => r.Key))      //sorting can mean nothing here, only takes extra time
            {
                offset = ((ulong)de.Key).To_8_bytes_array_BigEndian().Substring(8 - DefaultPointerLen, DefaultPointerLen);

                if (_rollbackCache.ContainsKey(de.Key))
                    continue;

                //Reading from dataFile values which must be rolled back
                btRoll = new byte[de.Value.Length];

                _fsData.Write_ByOffset((int)de.Key, ref btRoll);

                //Forming protocol for rollback
                btRoll = new byte[] { 1 }
                           .ConcatMany(
                           offset,
                           ((uint)btRoll.Length).To_4_bytes_array_BigEndian(),
                           btRoll
                           );

                //Writing rollback
                _fsRollback.Write_ByOffset((int)eofRollback, ref btRoll);

                _rollbackCache.Add(de.Key, new r { o = eofRollback + 1 + offset.Length + 4, l = de.Value.Length });  //10 is size of protocol data

                //increasing eof rollback file
                eofRollback += btRoll.Length;

                flushRollback = true;
            }

            if (flushRollback)
            {
                //Writing into helper
                btRoll = eofRollback.To_8_bytes_array_BigEndian();
                _fsRollbackHelper.Write_ByOffset(0, ref btRoll);

                //Flushing rollback and rollback helper
            }


            //second loop for saving data
            foreach (var de in _randBuf.OrderBy(r => r.Key))      //sorting can mean nothing here, only takes extra time
            {
                btRoll = de.Value;
                _fsData.Write_ByOffset((int)de.Key, ref btRoll);
            }

            //No flush of data file, it will be done on Flush()                        

            _randBuf.Clear();
            usedBufferSize = 0;
            
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="useCache">if actual overwritten data must be used</param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public byte[] Table_Read(bool useCache, byte[] offset, int count)
        {
            return Table_Read(useCache, (long)offset.DynamicLength_To_UInt64_BigEndian(), count);
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="readActual">if actual overwritten data must be used</param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public byte[] Table_Read(bool useCache, long offset, int count)
        {

            byte[] res = null;

            //if (count == 0)     //!!! also not necessary, but can be while testing period under exception
            //    return null;

            lock (lock_fs)
            {
                if (!useCache)
                {
                    //We read exactly what is already saved, without rollback.
                    //all data must be either in file or in buffer
                    //we must form resulting byte[] 

                    //Buffer
                    //Finding first element which is smaller or equal then offset
                    List<long> bufKeys = new List<long>();

                    if (_randBuf.Count() > 0)
                    {
                        var qkvp = _randBuf.OrderByDescending(r => r.Key).Where(r => r.Key < offset).Take(1).Where(r => (r.Key + r.Value.Length - 1) >= offset);


                        if (qkvp.Count() > 0)
                        {
                            bufKeys.Add(qkvp.FirstOrDefault().Key);
                        }

                        foreach (var kvp in _randBuf.OrderBy(r => r.Key).Where(r => r.Key >= offset && r.Key < (offset + count)))
                        {
                            bufKeys.Add(kvp.Key);
                        }
                    }

                    //reading full byte[] from original file and putting on top keys
                    //We use full length of the file
                    if (offset + count > _fsData.EOF)
                        res = new byte[_fsData.EOF - offset];
                    else
                        res = new byte[count];

                    res = _fsData.Read((int)offset, res.Length);

                    byte[] btWork = null;
                    //putting on top
                    foreach (var bk in bufKeys)
                    {
                        if (offset + res.Length <= bk)
                            continue;

                        btWork = _randBuf[bk];

                        bool cut = false;
                        int start = 0;
                        int stop = btWork.Length;

                        if (bk < offset)
                        {
                            cut = true;
                            start = Convert.ToInt32(offset - bk);
                        }

                        if ((offset + count) < (bk + btWork.Length))
                        {
                            cut = true;
                            stop = Convert.ToInt32(offset + count - bk);
                        }

                        if (cut)
                        {
                            byte[] tmp = new byte[stop - start];
                            Buffer.BlockCopy(btWork, start, tmp, 0, tmp.Length);
                            btWork = tmp;
                        }

                        Buffer.BlockCopy(btWork, 0, res, (start > 0) ? 0 : Convert.ToInt32(bk - offset), btWork.Length);
                    }


                }
                else
                {
                    //we must use rollback file.
                    //We can read only up to commited file lengh eofData

                    List<long> bufKeys = new List<long>();

                    if (_rollbackCache.Count() > 0)
                    {
                        var qkvp = _rollbackCache.OrderByDescending(r => r.Key).Where(r => r.Key < offset).Take(1).Where(r => (r.Key + r.Value.l - 1) >= offset);


                        if (qkvp.Count() > 0)
                        {
                            bufKeys.Add(qkvp.FirstOrDefault().Key);
                        }

                        foreach (var kvp in _rollbackCache.OrderBy(r => r.Key).Where(r => r.Key >= offset && r.Key < (offset + count)))
                        {
                            bufKeys.Add(kvp.Key);
                        }
                    }

                    //reading full byte[] from original file and putting on top keys

                    /*
                         * Transaction with minimum 2 tables. T2 is inserted, reference to T2 KVP is taken, then this reference is saved into T1.
                         * Commit().
                         * Commit calls TransactionalCommit for every table sequentially. First it meets table T1, then T2.
                         * In both tables TransactionalCommit procedures are successfull.
                         * then Commit procedure for each table calls TransactionalCommitIsFinished (this proc will clear rollback refs and moves eofData for every table).
                         * First encounters T1 and only then T2. 
                         * ....Somewhere here (between calling T1 and T2 TransactionalCommitIsFinished) starts a parallel thread. 
                         * After T1 TransactionalCommitIsFinished our parallel thread P1 reads data from T1, 
                         * and gets SelectDirect reference to T2 KVP. Then tries to read from not yet TransactionalCommitIsFinished T2.
                         * and for T2 happens: eofData < offset
                         * 
                         * To avoid such specific case we use for calculation this._fsData.Length instead of eofData in case if (eofData < offset && TransactionalCommitIsStarted)            
                         * 19.07.2013 10:25
                        */

                    //WAS
                    //if (offset + count > eofData)
                    //    res = new byte[eofData - offset];
                    //else
                    //    res = new byte[count];
                    //////

                    //NOW
                    if (offset + count > eofData)
                    {
                        if (eofData < offset && TransactionalCommitIsStarted)   //NOT FINISHED multi-table COMMIT. SelectDirect case
                        {
                            //Probably not finished transaction and SelectDirect case. We return value,
                            //because at this momont all transaction table have successfully gone through TransactionalCommit() procedure.

                            if (offset + count > this._fsData.EOF)
                            {
                                res = new byte[this._fsData.EOF - offset];
                            }
                            else
                            {
                                res = new byte[count];
                            }
                        }
                        else
                        {
                            res = new byte[eofData - offset];
                        }
                    }
                    else
                        res = new byte[count];
                    ///////


                    res = _fsData.Read((int)offset, res.Length);


                    byte[] btWork = null;
                    r rb = null;
                    //putting on top
                    foreach (var bk in bufKeys)
                    {
                        if (offset + res.Length <= bk)
                            continue;

                        rb = _rollbackCache[bk];
                        //reading from rollback
                        btWork = new byte[rb.l];

                        btWork = _fsData.Read((int)rb.o, btWork.Length);

                        bool cut = false;
                        int start = 0;
                        int stop = btWork.Length;

                        if (bk < offset)
                        {
                            cut = true;
                            start = Convert.ToInt32(offset - bk);
                        }

                        if ((offset + count) < (bk + btWork.Length))
                        {
                            cut = true;
                            stop = Convert.ToInt32(offset + count - bk);
                        }

                        if (cut)
                        {
                            byte[] tmp = new byte[stop - start];
                            Buffer.BlockCopy(btWork, start, tmp, 0, tmp.Length);
                            btWork = tmp;
                        }

                        Buffer.BlockCopy(btWork, 0, res, (start > 0) ? 0 : Convert.ToInt32(bk - offset), btWork.Length);
                    }
                }
            }


            return res;
           
        }

       

        /// <summary>
        /// Cleans all buffers and flushes data to the disk
        /// </summary>
        public void Commit()
        {
            lock (lock_fs)
            {
                FlushRandomBuffer();
                                

                if (eofRollback != 0)
                {
                    //Finalizing rollback helper

                    eofRollback = 0;
                    byte[] btWork = eofRollback.To_8_bytes_array_BigEndian();
                    _fsRollbackHelper.Write_ByOffset(0, ref btWork);    
                    //_fsRollbackHelper.Position = 0;
                    //_fsRollbackHelper.Write(eofRollback.To_8_bytes_array_BigEndian(), 0, 8);

                  //  NET_Flush(_fsRollbackHelper);
                }

                _rollbackCache.Clear();

                eofData = this._fsData.EOF;
            }
        }

        /// <summary>
        /// Transactional Commit is started
        /// </summary>
        bool TransactionalCommitIsStarted = false;

        /// <summary>
        /// 
        /// </summary>
        public void TransactionalCommit()
        {
            lock (lock_fs)
            {
                FlushRandomBuffer();

               // NET_Flush(_fsData);

                TransactionalCommitIsStarted = true;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void TransactionalCommitIsFinished()
        {
            lock (lock_fs)
            {
                if (eofRollback != 0)
                {
                    //Finalizing rollback helper

                    eofRollback = 0;
                    byte[] btWork = eofRollback.To_8_bytes_array_BigEndian();
                    _fsRollbackHelper.Write_ByOffset(0, ref btWork);    
                    //_fsRollbackHelper.Position = 0;
                    //_fsRollbackHelper.Write(eofRollback.To_8_bytes_array_BigEndian(), 0, 8);

                    //NET_Flush(_fsRollbackHelper);
                }

                _rollbackCache.Clear();

                eofData = this._fsData.EOF;

                TransactionalCommitIsStarted = false;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void TransactionalRollback()
        {
            Rollback();
            TransactionalCommitIsStarted = false;
        }

        /// <summary>
        /// Standard and transactional rollback
        /// </summary>
        public void Rollback()
        {
            try
            {
                lock (lock_fs)
                {
                    //Clearing random buffer
                    if (_randBuf.Count() != 0)
                    {
                        usedBufferSize = 0;
                        _randBuf.Clear();
                    }

                    //Restoring Rollback records
                    byte[] btWork = null;

                    if (_rollbackCache.Count() > 0)
                    {

                        foreach (var rb in _rollbackCache)
                        {
                            btWork = new byte[rb.Value.l];

                            btWork = _fsRollback.Read((int)rb.Value.o, btWork.Length);

                            //_fsRollback.Position = rb.Value.o;
                            //_fsRollback.Read(btWork, 0, btWork.Length);

                            _fsData.Write_ByOffset((int)rb.Key, btWork);
                            //_fsData.Position = rb.Key;
                            //_fsData.Write(btWork, 0, btWork.Length);
                        }

                        //NET_Flush(_fsData);

                        //Restoring rhp
                        eofRollback = 0;
                        btWork = eofRollback.To_8_bytes_array_BigEndian();
                        _fsRollbackHelper.Write_ByOffset(0, btWork);
                        //_fsRollbackHelper.Position = 0;
                        //_fsRollbackHelper.Write(eofRollback.To_8_bytes_array_BigEndian(), 0, 8);

                        //NET_Flush(_fsRollbackHelper);

                        //Clearing rollbackCache
                        _rollbackCache.Clear();

                    }

                    //we dont move eofData, space can be re-used up to next restart (may be root can have this info in next protocols)
                    //eofData = this._fsData.Length;
                }
            }
            catch (Exception ex)
            {
                IsOperable = false;
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.RESTORE_ROLLBACK_DATA_FAILED, this._fileName, ex);
            }


        }








       
    }
}
