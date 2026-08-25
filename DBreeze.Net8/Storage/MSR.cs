/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Threading;

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

        readonly ReaderWriterLockSlim lock_fs = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);


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
        // Read by every LTrieRow freshness check. Taking lock_fs here adds two
        // ReaderWriterLockSlim round-trips to a typical missing point lookup. This
        // generation stamp is published atomically by recreate/init operations.
        long _storageFixTimeTicks = DateTime.UtcNow.Ticks;
        #endregion

        private readonly struct StorageLockScope : IDisposable
        {
            private readonly ReaderWriterLockSlim _lock;
            private readonly bool _write;

            public StorageLockScope(ReaderWriterLockSlim sync, bool write)
            {
                _lock = sync;
                _write = write;
                if (write) sync.EnterWriteLock(); else sync.EnterReadLock();
            }

            public void Dispose()
            {
                if (_write) _lock.ExitWriteLock(); else _lock.ExitReadLock();
            }
        }

        private StorageLockScope AcquireReadLock() => new StorageLockScope(lock_fs, false);
        private StorageLockScope AcquireWriteLock() => new StorageLockScope(lock_fs, true);

        public MSR(string fileName, TrieSettings trieSettings, DBreezeConfiguration configuration)
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
            get { using (AcquireReadLock()) { return eofData; } }
        }

        /// <summary>
        /// Returns time of file initiation, ead remarks on 
        /// </summary>
        public DateTime StorageFixTime
        {
            get { return new DateTime(Volatile.Read(ref _storageFixTimeTicks), DateTimeKind.Utc); }
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
            using (AcquireWriteLock())
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

                Volatile.Write(ref _storageFixTimeTicks, DateTime.UtcNow.Ticks);
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
            using (AcquireWriteLock())
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
                Volatile.Write(ref _storageFixTimeTicks, DateTime.UtcNow.Ticks);
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
            ArgumentNullException.ThrowIfNull(data);

            long position = 0;
            byte[] encodedPosition = null;

            using (AcquireWriteLock())
            {
                position = _fsData.EOF;
                encodedPosition = EncodePointer(position);
                position = _fsData.Write_ToTheEnd(data);
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

            using (AcquireWriteLock())
            {
                if (offset < 0 || offset > Int32.MaxValue - data.Length)
                    throw new ArgumentOutOfRangeException(nameof(offset));
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
                ref readonly BufferedWriteSet.Segment segment = ref _randBuf.GetSegment(i);
                if (PreserveRollbackRange(segment.Offset, segment.Length))
                    flushRollback = true;
            }

            if (flushRollback)
            {
                //Writing into helper
                byte[] marker = eofRollback.To_8_bytes_array_BigEndian();
                _fsRollbackHelper.Write_ByOffset(0, marker);

                //Flushing rollback and rollback helper
            }


            //second loop for saving data
            for (int i = 0; i < _randBuf.Count; i++)
            {
                ref readonly BufferedWriteSet.Segment segment = ref _randBuf.GetSegment(i);
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
            byte[] record = GC.AllocateUninitializedArray<byte>(checked(headerLength + length));
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
            _fsRollback.Write_ByOffset(recordOffset, record);
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
            using (AcquireReadLock())
            {
                if (offset < 0)
                    throw new ArgumentOutOfRangeException(nameof(offset));
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));

                if (!useCache)
                {
                    int resultLength = GetReadLength(offset, count, _fsData.EOF);
                    if (resultLength == 0)
                        return Array.Empty<byte>();

                    byte[] result = _fsData.Read((int)offset, resultLength);
                    if (_randBuf.Count != 0)
                        _randBuf.Overlay(offset, result);
                    return result;
                }

                long visibleLength = offset > eofData && TransactionalCommitIsStarted ? _fsData.EOF : eofData;
                int committedLength = GetReadLength(offset, count, visibleLength);
                if (committedLength == 0)
                    return Array.Empty<byte>();

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
                throw new ArgumentOutOfRangeException(nameof(position));

            byte[] pointer = GC.AllocateUninitializedArray<byte>(DefaultPointerLen);
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
            long copyStart = Math.Max(sourceOffset, destinationOffset);
            long copyEnd = Math.Min(sourceEnd, destinationEnd);
            if (copyStart >= copyEnd)
                return;

            Buffer.BlockCopy(source, (int)(copyStart - sourceOffset), destination,
                (int)(copyStart - destinationOffset), (int)(copyEnd - copyStart));
        }

        #if false
        private byte[] Table_ReadLegacy(bool useCache, long offset, int count)
        {

            byte[] res = null;

            //if (count == 0)     //!!! also not necessary, but can be while testing period under exception
            //    return null;

            using (AcquireReadLock())
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
                    RollbackRecord rb = null;
                    //putting on top
                    foreach (var bk in bufKeys)
                    {
                        if (offset + res.Length <= bk)
                            continue;

                        rb = _rollbackCache[bk];
                        //reading from rollback
                        btWork = new byte[rb.l];

                        btWork = _fsRollback.Read((int)rb.o, btWork.Length);

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

        #endif

        /// <summary>
        /// Cleans all buffers and flushes data to the disk
        /// </summary>
        public void Commit()
        {
            using (AcquireWriteLock())
            {
                FlushRandomBuffer();


                if (eofRollback != 0)
                {
                    //Finalizing rollback helper

                    eofRollback = 0;
                    byte[] btWork = eofRollback.To_8_bytes_array_BigEndian();
                    _fsRollbackHelper.Write_ByOffset(0, btWork);
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
            using (AcquireWriteLock())
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
            using (AcquireWriteLock())
            {
                if (eofRollback != 0)
                {
                    //Finalizing rollback helper

                    eofRollback = 0;
                    byte[] btWork = eofRollback.To_8_bytes_array_BigEndian();
                    _fsRollbackHelper.Write_ByOffset(0, btWork);
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
                using (AcquireWriteLock())
                {
                    RollbackCore();
                    TransactionalCommitIsStarted = false;
                }
            }
            catch (Exception ex)
            {
                IsOperable = false;
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.RESTORE_ROLLBACK_DATA_FAILED, _fileName, ex);
            }
        }

        /// <summary>
        /// Standard and transactional rollback
        /// </summary>
        public void Rollback()
        {
            try
            {
                using (AcquireWriteLock())
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
            _fsRollbackHelper.Write_ByOffset(0, eofRollback.To_8_bytes_array_BigEndian());
            _rollbackCache.Clear();
        }









    }
}
