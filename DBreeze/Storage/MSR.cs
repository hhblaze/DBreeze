/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.Collections.Generic;
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
        readonly BufferedWriteSet _randBuf = new BufferedWriteSet();

        /// <summary>
        /// Record in rollback is characterized with 
        /// </summary>
        class RollbackRecord
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
        Dictionary<long, RollbackRecord> _rollbackCache = new Dictionary<long, RollbackRecord>();

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
            get { lock (lock_fs) { return eofData; } }
        }
        
        /// <summary>
        /// Returns time of file initiation, ead remarks on 
        /// </summary>
        public DateTime StorageFixTime
        {
            get { lock (lock_fs) { return _storageFixTime; } }
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

                _randBuf.Clear();
                _rollbackCache.Clear();
                usedBufferSize = 0;
                eofRollback = 0;
                TransactionalCommitIsStarted = false;
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
            throw new NotSupportedException("RestoreTableFromTheOtherTable is not available for memory storage.");
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
                TransactionalCommitIsStarted = false;

                _fsData.Write_ToTheEnd(new byte[64]);
                eofData = _fsData.EOF;
                _storageFixTime = DateTime.UtcNow;
                IsOperable = true;

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
            if (data == null)
                throw new ArgumentNullException("data");

            long position = 0;
            byte[] encodedPosition = null;

            lock (lock_fs)
            {
                position = _fsData.EOF;
                encodedPosition = EncodePointer(position);
                position = _fsData.Write_ToTheEnd(ref data);
            }

            return encodedPosition;
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
                if (offset < 0 || offset > Int32.MaxValue - data.Length)
                    throw new ArgumentOutOfRangeException("offset");
                long writeEnd = offset + data.Length;

                if (offset < _fsData.EOF && writeEnd > _fsData.EOF)
                {
                    throw new Exception("FSR.WriteByOffset: offset < _fsData.EOF && offset + data.Length > _fsData.EOF");
                }

                if (writeEnd > _fsData.EOF)
                {
                    //DB RULE1. We cant update and go out of the end of file. Only if we write into empty file root in the beginning
                    throw new Exception("FSR.WriteByOffset: offset + data.Length > _fsData.EOF");
                }


                _randBuf.Add(offset, data);
                usedBufferSize = checked(usedBufferSize + data.Length);

                //if we are able to store data into buffer lets do it
                if (usedBufferSize >= maxRandomBufferSize || _randBuf.WriteOperations > maxRandomElementsCount)
                    FlushRandomBuffer();
            }
        }


        /// <summary>
        /// Is called only from lock_fs and must be finished by calling NET_Flush
        /// </summary>
        /// <param name="commit"></param>
        void FlushRandomBuffer()
        {
            if (_randBuf.Count == 0)
            {
                return;
            }

            //First we write all data into rollback file and helper, calling flush on rollback
            //then updating data of data file but dont call update
            //clearing random buffer

            bool flushRollback = false;

            //first loop for saving rollback data
            for (int i = 0; i < _randBuf.Count; i++)
            {
                BufferedWriteSet.Segment segment = _randBuf[i];
                if (PreserveRollbackRange(segment.Offset, segment.Length))
                    flushRollback = true;
            }

            if (flushRollback)
            {
                //Writing into helper
                byte[] marker = eofRollback.To_8_bytes_array_BigEndian();
                _fsRollbackHelper.Write_ByOffset(0, ref marker);

                //Flushing rollback and rollback helper
            }


            //second loop for saving data
            for (int i = 0; i < _randBuf.Count; i++)
            {
                BufferedWriteSet.Segment segment = _randBuf[i];
                _fsData.Write_ByOffset(checked((int)segment.Offset), segment.Buffer,
                    segment.BufferOffset, segment.Length);
            }

            //No flush of data file, it will be done on Flush()                        

            _randBuf.Clear();
            usedBufferSize = 0;

        }

        private bool PreserveRollbackRange(long dataOffset, int length)
        {
            long end = checked(dataOffset + length);
            long cursor = dataOffset;
            bool appended = false;

            while (cursor < end)
            {
                long coveredEnd = cursor;
                long nextCoveredStart = end;
                foreach (KeyValuePair<long, RollbackRecord> rollback in _rollbackCache)
                {
                    long rollbackEnd = rollback.Key + rollback.Value.l;
                    if (rollback.Key <= cursor && rollbackEnd > coveredEnd)
                        coveredEnd = rollbackEnd;
                    else if (rollback.Key > cursor && rollback.Key < nextCoveredStart)
                        nextCoveredStart = rollback.Key;
                }

                if (coveredEnd > cursor)
                {
                    cursor = coveredEnd < end ? coveredEnd : end;
                    continue;
                }

                int uncoveredLength = checked((int)(nextCoveredStart - cursor));
                AppendRollbackRecord(cursor, uncoveredLength);
                appended = true;
                cursor = nextCoveredStart;
            }

            return appended;
        }

        private void AppendRollbackRecord(long dataOffset, int length)
        {
            int headerLength = 1 + DefaultPointerLen + 4;
            byte[] original = _fsData.Read(checked((int)dataOffset), length);
            byte[] record = new byte[checked(headerLength + length)];
            record[0] = 1;

            ulong encodedOffset = (ulong)dataOffset;
            for (int i = DefaultPointerLen; i > 0; i--)
            {
                record[i] = (byte)encodedOffset;
                encodedOffset >>= 8;
            }
            if (encodedOffset != 0)
                throw new InvalidOperationException("Storage offset does not fit configured pointer length.");

            uint encodedLength = (uint)length;
            int lengthOffset = 1 + DefaultPointerLen;
            record[lengthOffset] = (byte)(encodedLength >> 24);
            record[lengthOffset + 1] = (byte)(encodedLength >> 16);
            record[lengthOffset + 2] = (byte)(encodedLength >> 8);
            record[lengthOffset + 3] = (byte)encodedLength;
            Buffer.BlockCopy(original, 0, record, headerLength, length);

            int recordOffset = checked((int)eofRollback);
            _fsRollback.Write_ByOffset(recordOffset, ref record);
            _rollbackCache.Add(dataOffset, new RollbackRecord { o = recordOffset + headerLength, l = length });
            eofRollback = checked((long)recordOffset + record.Length);
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
            lock (lock_fs)
            {
                if (offset < 0)
                    throw new ArgumentOutOfRangeException("offset");
                if (count < 0)
                    throw new ArgumentOutOfRangeException("count");

                if (!useCache)
                {
                    int resultLength = GetReadLength(offset, count, _fsData.EOF);
                    if (resultLength == 0)
                        return new byte[0];

                    byte[] result = _fsData.Read((int)offset, resultLength);

                    if (_randBuf.Count != 0)
                        _randBuf.Overlay(offset, result);
                    return result;
                }

                long visibleLength = offset > eofData && TransactionalCommitIsStarted ? _fsData.EOF : eofData;
                int committedLength = GetReadLength(offset, count, visibleLength);
                if (committedLength == 0)
                    return new byte[0];

                byte[] committed = _fsData.Read((int)offset, committedLength);
                foreach (KeyValuePair<long, RollbackRecord> rollback in _rollbackCache)
                {
                    long rollbackEnd = rollback.Key + rollback.Value.l;
                    long resultEnd = offset + committed.Length;
                    if (rollback.Key >= resultEnd || rollbackEnd <= offset)
                        continue;

                    byte[] rollbackData = _fsRollback.Read((int)rollback.Value.o, rollback.Value.l);
                    CopyIntersection(rollback.Key, rollbackData, offset, committed);
                }
                return committed;
            }
        }

        private byte[] EncodePointer(long position)
        {
            if (position < 0)
                throw new ArgumentOutOfRangeException("position");

            byte[] pointer = new byte[DefaultPointerLen];
            ulong value = (ulong)position;
            for (int i = pointer.Length - 1; i >= 0; i--)
            {
                pointer[i] = (byte)value;
                value >>= 8;
            }
            if (value != 0)
                throw new InvalidOperationException("Storage position does not fit configured pointer length.");
            return pointer;
        }

        private static int GetReadLength(long offset, int count, long length)
        {
            if (count == 0 || offset >= length)
                return 0;

            long available = length - offset;
            return available < count ? (int)available : count;
        }

        private static void CopyIntersection(long sourceOffset, byte[] source, long destinationOffset, byte[] destination)
        {
            long sourceEnd = sourceOffset + source.Length;
            long destinationEnd = destinationOffset + destination.Length;
            long copyStart = sourceOffset > destinationOffset ? sourceOffset : destinationOffset;
            long copyEnd = sourceEnd < destinationEnd ? sourceEnd : destinationEnd;

            if (copyStart >= copyEnd)
                return;

            Buffer.BlockCopy(source, (int)(copyStart - sourceOffset), destination,
                (int)(copyStart - destinationOffset), (int)(copyEnd - copyStart));
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
            try
            {
                lock (lock_fs)
                {
                    RollbackCore();
                    TransactionalCommitIsStarted = false;
                }
            }
            catch (Exception ex)
            {
                IsOperable = false;
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.RESTORE_ROLLBACK_DATA_FAILED, this._fileName, ex);
            }
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
                    RollbackCore();
                }
            }
            catch (Exception ex)
            {
                IsOperable = false;
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.RESTORE_ROLLBACK_DATA_FAILED, this._fileName, ex);
            }


        }

        private void RollbackCore()
        {
            if (_randBuf.Count != 0)
            {
                usedBufferSize = 0;
                _randBuf.Clear();
            }

            if (_rollbackCache.Count == 0)
                return;

            foreach (KeyValuePair<long, RollbackRecord> rollback in _rollbackCache)
            {
                byte[] rollbackData = _fsRollback.Read((int)rollback.Value.o, rollback.Value.l);
                _fsData.Write_ByOffset((int)rollback.Key, rollbackData);
            }

            eofRollback = 0;
            byte[] marker = eofRollback.To_8_bytes_array_BigEndian();
            _fsRollbackHelper.Write_ByOffset(0, marker);
            _rollbackCache.Clear();
        }








       
    }
}
