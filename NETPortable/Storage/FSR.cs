/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
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
    internal class FSR : IStorage
    {        
        //!!try catches can be taken away from reads and writes, when procs are fully balanced
        
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

        /// <summary>
        /// Random buffer maximal size before flush
        /// </summary>
       int maxRandomBufferSize = 3000000; //Random buffer size before flush
       int maxRandomElementsCount = 500; //Random buffer maximal quantity of elements in buffer before flush

       int _seqBufCapacity = 1048576;
        MemoryStorage _seqBuf = new MemoryStorage(1024, 1024 * 100, MemoryStorage.eMemoryExpandStartegy.FIXED_LENGTH_INCREASE);     
       
        int usedBufferSize = 0; //Used buffer size before flush
        /// <summary>
        /// Rollback file re-creation after initialization
        /// </summary>
        public int MaxRollbackFileSize = 131072;

        string _fileName = String.Empty;
        ulong ulFileName = 0;   //ulong file name, for backup purposes
        object lock_fs = new object();
        int _fileStreamBufferSize = 8192;

        IFileStream _fsData = null;
        IFileStream _fsRollback = null;
        IFileStream _fsRollbackHelper = null;
        /// <summary>
        /// Pointer to the end of file, before current commit
        /// </summary>
        long eofData = 0;
        long eofRollback = 0;
        long fsLength = 0;

        TrieSettings _trieSettings = null;
        ushort DefaultPointerLen = 0;
        DBreezeConfiguration _configuration = null;

        bool _backupIsActive = false;

        /// <summary>
        /// DateTime when file was initialized. Is remembered by LTrieRow, based on this file.
        /// If file is change after RestoreTableFromTheOtherTable or RecreateFiles,
        /// LTrieRow will have different version and will return exception.
        /// </summary>
        DateTime _storageFixTime = DateTime.UtcNow;

        #endregion

        public FSR(string fileName, TrieSettings trieSettings, DBreezeConfiguration configuration)
        {
            this._fileName = fileName;
            this._configuration = configuration;
            this._trieSettings = trieSettings;
            DefaultPointerLen = this._trieSettings.POINTER_LENGTH;

            _backupIsActive = this._configuration.Backup.IsActive;

            //Transforms fileName into ulong digit
            ulFileName = this._configuration.Backup.BackupFNP.ParseFilename(Path.GetFileNameWithoutExtension(this._fileName));

            InitFiles();
        }

        /// <summary>
        /// Physical length of the storage file
        /// </summary>
        public long Length
        {
            get { lock (lock_fs) { return this.eofData; } }
        }

        /// <summary>
        /// Returns time of file initiation, ead remarks on 
        /// </summary>
        public DateTime StorageFixTime
        {
            get { lock (lock_fs) { return _storageFixTime; } }
        }

        /// <summary>
        /// 
        /// </summary>
        public TrieSettings TrieSettings
        {
            get { return _trieSettings; }
        }

        /// <summary>
        /// 
        /// </summary>
        public DBreezeConfiguration DbreezeConfiguration
        {
            get { return this._configuration; }
        }

        /// <summary>
        /// 
        /// </summary>
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

                _seqBuf.Dispose();
                _randBuf.Clear();
                _rollbackCache.Clear();
                usedBufferSize = 0;
                eofRollback = 0;
                eofData = 0;
                fsLength = 0;
                TransactionalCommitIsStarted = false;
            }
                       
        }

        #region Initialization

        private void InitFiles()
        {
            //Creates filestreams and rollbacks, restores rollback to the initial file, if necessary
          
            try
            {
                //this._fsData = new FileStream(this._fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _fileStreamBufferSize, FileOptions.WriteThrough);
                //this._fsRollback = new FileStream(this._fileName + ".rol", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _fileStreamBufferSize, FileOptions.WriteThrough);
                //this._fsRollbackHelper = new FileStream(this._fileName + ".rhp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _fileStreamBufferSize, FileOptions.WriteThrough);

                this._fsData = this._configuration.FSFactory.CreateType1(this._fileName, _fileStreamBufferSize);
                this._fsRollback = this._configuration.FSFactory.CreateType1(this._fileName + ".rol", _fileStreamBufferSize);
                this._fsRollbackHelper = this._configuration.FSFactory.CreateType1(this._fileName + ".rhp", _fileStreamBufferSize);                

                //!!!!We dont have this value in root yet, could have and economize tail of the file in case if rollback occured

                if (this._fsData.Length == 0)
                {
                    //Writing initial root data

                    _fsData.Position = 0;
                    _fsData.Write(new byte[this._trieSettings.ROOT_SIZE], 0, this._trieSettings.ROOT_SIZE);


                    if (_backupIsActive)
                    {
                        this._configuration.Backup.WriteBackupElement(ulFileName, 0, 0, new byte[this._trieSettings.ROOT_SIZE]);
                    }

                    //no flush here
                }

                eofData = this._fsData.Length;
                fsLength = this._fsData.Length;

                //Check is .rhp is empty add 0 pointer
                if (this._fsRollbackHelper.Length == 0)
                {
                    //no sense to write here

                    //_fsRollbackHelper.Position = 0;
                    //_fsRollbackHelper.Write(eofRollback.To_8_bytes_array_BigEndian(), 0, 8);

                    //NET_Flush(_fsRollbackHelper);
                }
                else
                {
                    InitRollback();
                }


                _storageFixTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                IsOperable = false;
                try { CloseStorageStreams(); }
                catch { }
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DB_IS_NOT_OPERABLE, "FSR INIT FAILED: " + this._fileName, ex);
            }

        }


        #endregion

        #region InitRollback

        private void InitRollback()
        {
            byte[] btWork = new byte[8];
            _fsRollbackHelper.Position = 0;
            ReadExactly(_fsRollbackHelper, btWork, 0, btWork.Length);
            eofRollback = btWork.To_Int64_BigEndian();

            if (eofRollback < 0 || eofRollback > _fsRollback.Length)
                throw new InvalidDataException("Rollback marker points outside of the rollback file.");

            if (eofRollback == 0)
            {
                if (this._fsRollback.Length >= MaxRollbackFileSize)
                {
                    this._fsRollback.Dispose();
                    this._configuration.FSFactory.Delete(this._fileName + ".rol");
                    //this._fsRollback = new FileStream(this._fileName + ".rol", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _fileStreamBufferSize, FileOptions.WriteThrough);
                    this._fsRollback = this._configuration.FSFactory.CreateType1(this._fileName + ".rol", _fileStreamBufferSize);

                    //no sense to do anything with backup
                }

                return;
            }

            //!!!Check if data file is empty write first root 64 bytes, ??? Where it must stay after rollback restoration???
             

            //Restoring rollback
            RestoreInitRollback();

            //Checking if we can recreate rollback file
            if (this._fsRollback.Length >= MaxRollbackFileSize)
            {                
                this._fsRollback.Dispose();

                this._configuration.FSFactory.Delete(this._fileName + ".rol");                
                this._fsRollback = this._configuration.FSFactory.CreateType1(this._fileName + ".rol", _fileStreamBufferSize);
                //File.Delete(this._fileName + ".rol");
                //this._fsRollback = new FileStream(this._fileName + ".rol", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _fileStreamBufferSize, FileOptions.WriteThrough);

                //no sense to do anything with backup
            }

            eofRollback = 0;
            _fsRollbackHelper.Position = 0;
            _fsRollbackHelper.Write(eofRollback.To_8_bytes_array_BigEndian(), 0, 8);

            NET_Flush(_fsRollbackHelper);

            //try
            //{

            //}
            //catch (Exception ex)
            //{
            //    IsOperable = false;
            //    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.RESTORE_ROLLBACK_DATA_FAILED, this._fileName, ex);
            //}

        }
        /// <summary>
        /// 
        /// </summary>
        void RestoreInitRollback()
        {
            int headerLength = 1 + DefaultPointerLen + 4;
            byte[] header = new byte[headerLength];
            byte[] copyBuffer = new byte[64 * 1024];
            long rollbackPosition = 0;

            while (rollbackPosition < eofRollback)
            {
                if (eofRollback - rollbackPosition < headerLength)
                    throw new InvalidDataException("Incomplete rollback record header.");

                _fsRollback.Position = rollbackPosition;
                ReadExactly(_fsRollback, header, 0, header.Length);
                if (header[0] != 1)
                    throw new InvalidDataException("Unknown rollback protocol.");

                ulong targetOffset = 0;
                for (int i = 0; i < DefaultPointerLen; i++)
                    targetOffset = (targetOffset << 8) | header[1 + i];

                uint dataLength = 0;
                int lengthOffset = 1 + DefaultPointerLen;
                for (int i = 0; i < 4; i++)
                    dataLength = (dataLength << 8) | header[lengthOffset + i];

                if (targetOffset > Int64.MaxValue)
                    throw new InvalidDataException("Rollback target offset is too large.");
                long recordEnd = rollbackPosition + headerLength + (long)dataLength;
                if (recordEnd < rollbackPosition || recordEnd > eofRollback)
                    throw new InvalidDataException("Incomplete rollback record payload.");
                if ((long)targetOffset > Int64.MaxValue - dataLength)
                    throw new InvalidDataException("Rollback target range is too large.");

                _fsData.Position = (long)targetOffset;
                long remaining = dataLength;
                while (remaining > 0)
                {
                    int chunk = remaining > copyBuffer.Length ? copyBuffer.Length : (int)remaining;
                    ReadExactly(_fsRollback, copyBuffer, 0, chunk);
                    _fsData.Write(copyBuffer, 0, chunk);
                    remaining -= chunk;
                }
                rollbackPosition = recordEnd;
            }

            NET_Flush(_fsData);
        }

        static void ReadExactly(IFileStream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, offset, count);
                if (read == 0)
                    throw new EndOfStreamException("Unexpected end of storage stream.");
                offset += read;
                count -= read;
            }
        }
        #endregion

        #region "NET FLUSH"
        public static void NET_Flush(IFileStream mfs)
        {
            mfs.Flush(true);
        }
        #endregion

        #region "RestoreTableFromTheOtherTable"

        /// <summary>
        /// 
        /// </summary>
        /// <param name="newTableFullPath"></param>
        public void RestoreTableFromTheOtherTable(string newTableFullPath)
        {
            lock (lock_fs)
            {
                if (String.IsNullOrEmpty(newTableFullPath))
                    throw new ArgumentNullException("newTableFullPath");
                if (String.Equals(newTableFullPath, _fileName, StringComparison.Ordinal))
                    throw new ArgumentException("Source and destination table paths must differ.", "newTableFullPath");
                if (!_configuration.FSFactory.Exists(newTableFullPath))
                    throw new FileNotFoundException("Source table was not found.", newTableFullPath);

                string[] sourceFiles = { newTableFullPath, newTableFullPath + ".rol", newTableFullPath + ".rhp" };
                string[] destinationFiles = { _fileName, _fileName + ".rol", _fileName + ".rhp" };
                string backupPrefix = _fileName + ".dbreeze.restore." + Guid.NewGuid().ToString("N");
                string[] backupFiles = { backupPrefix, backupPrefix + ".rol", backupPrefix + ".rhp" };
                bool[] destinationMoved = new bool[3];
                bool[] sourceMoved = new bool[3];

                CloseStorageStreams();
                try
                {
                    for (int i = 0; i < destinationFiles.Length; i++)
                    {
                        if (_configuration.FSFactory.Exists(destinationFiles[i]))
                        {
                            _configuration.FSFactory.Move(destinationFiles[i], backupFiles[i]);
                            destinationMoved[i] = true;
                        }
                    }
                    for (int i = 0; i < sourceFiles.Length; i++)
                    {
                        if (_configuration.FSFactory.Exists(sourceFiles[i]))
                        {
                            _configuration.FSFactory.Move(sourceFiles[i], destinationFiles[i]);
                            sourceMoved[i] = true;
                        }
                    }

                    ResetBuffers();
                    InitFiles();
                    for (int i = 0; i < backupFiles.Length; i++)
                        if (_configuration.FSFactory.Exists(backupFiles[i]))
                            _configuration.FSFactory.Delete(backupFiles[i]);
                }
                catch
                {
                    CloseStorageStreams();
                    for (int i = sourceFiles.Length - 1; i >= 0; i--)
                    {
                        if (sourceMoved[i] && _configuration.FSFactory.Exists(destinationFiles[i]) &&
                            !_configuration.FSFactory.Exists(sourceFiles[i]))
                            _configuration.FSFactory.Move(destinationFiles[i], sourceFiles[i]);
                    }
                    for (int i = destinationFiles.Length - 1; i >= 0; i--)
                    {
                        if (destinationMoved[i] && _configuration.FSFactory.Exists(backupFiles[i]))
                        {
                            if (_configuration.FSFactory.Exists(destinationFiles[i]))
                                _configuration.FSFactory.Delete(destinationFiles[i]);
                            _configuration.FSFactory.Move(backupFiles[i], destinationFiles[i]);
                        }
                        else if (!sourceMoved[i] && _configuration.FSFactory.Exists(destinationFiles[i]))
                        {
                            _configuration.FSFactory.Delete(destinationFiles[i]);
                        }
                    }
                    ResetBuffers();
                    InitFiles();
                    throw;
                }

            }
        }

        void CloseStorageStreams()
        {
            if (_fsData != null) { _fsData.Dispose(); _fsData = null; }
            if (_fsRollback != null) { _fsRollback.Dispose(); _fsRollback = null; }
            if (_fsRollbackHelper != null) { _fsRollbackHelper.Dispose(); _fsRollbackHelper = null; }
        }

        void ResetBuffers()
        {
            _randBuf.Clear();
            _rollbackCache.Clear();
            usedBufferSize = 0;
            eofRollback = 0;
            eofData = 0;
            fsLength = 0;
            TransactionalCommitIsStarted = false;
            _seqBuf.Clear(false);
        }
        #endregion

        #region "Recreate Files"

        /// <summary>
        /// 
        /// </summary>
        public void RecreateFiles()
        {
            lock (lock_fs)
            {
                CloseStorageStreams();
                ResetBuffers();

                this._configuration.FSFactory.Delete(this._fileName);
                this._configuration.FSFactory.Delete(this._fileName + ".rol");
                this._configuration.FSFactory.Delete(this._fileName + ".rhp");
                
                InitFiles();

            }
        }
        #endregion



        /// <summary>
        /// Must be called from lock_fs
        /// </summary>
        void FlushSequentialBuffer()
        {

            if (_seqBuf.EOF == 0)
                return;

            //long pos = _fsData.Length;
            long pos = fsLength;
            _fsData.Position = pos;
            _fsData.Write(_seqBuf.RawBuffer, 0, _seqBuf.EOF);

            fsLength += _seqBuf.EOF;

            if (_backupIsActive)
                this._configuration.Backup.WriteBackupElement(ulFileName, 0, pos, _seqBuf.RawBuffer, 0, _seqBuf.EOF);

            _seqBuf.Clear(false);
        }

        
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

            /* Emulation of the direct write to the disk without sequential cache */

            //_fsData.Position = position = _fsData.Length;
            //_fsData.Write(data, 0, data.Length);

            //return ((ulong)position).To_8_bytes_array_BigEndian().Substring(8 - DefaultPointerLen, DefaultPointerLen);

            /**************************************************************/

           

            lock (lock_fs)
            {
                //case when incoming data bigger then buffer, we clean buffer and write data directly to the disk

                if (data.Length > _seqBufCapacity)
                {
                    FlushSequentialBuffer();
                    //_fsData.Position = position = _fsData.Length;
                    _fsData.Position = position = fsLength;
                    encodedPosition = EncodePointer(position);
                    long newLength = checked(fsLength + data.Length);
                    _fsData.Write(data, 0, data.Length);

                    fsLength = newLength;

                    if (_backupIsActive)
                        _configuration.Backup.WriteBackupElement(ulFileName, 0, position, data);

                    return encodedPosition;
                }
                
                //Time to clean buffer
                if (data.Length > _seqBufCapacity - _seqBuf.EOF)
                {
                    FlushSequentialBuffer();
                }

                //Writing into buffer

                //position = _fsData.Length + _seqBuf.EOF;
                position = checked(fsLength + _seqBuf.EOF);
                encodedPosition = EncodePointer(position);

                _seqBuf.Write_ToTheEnd(data);
                
                //eofData (ptr to the end of file before current commit) will be increased only after flush

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

            /* Emulation of the direct save without random cache */
            //lock (lock_fs)
            //{
            //    FlushSequentialBuffer();
            //    _fsData.Position = offset;
            //    _fsData.Write(data, 0, data.Length);
            //}

            //Console.WriteLine("yeah");
            //return;
            /******************************************************/


            //DB RULE1. We cant update and go out of the end of file
            //!! ALL throw new Exception must be taken away after testS
            //!! This is a cutted implementation for DBreeze we dont take care buffer elements overlapping (start+len U some elements -> should be not possible)
            //overwriting partly file and partly sequential buffer is not allowed

            if (data == null || data.Length == 0)
            {
                throw new Exception("FSR.WriteByOffset: data == null || data.Length == 0");
            }

            lock (lock_fs)
            {
                if (offset < 0 || offset > Int64.MaxValue - data.Length)
                    throw new ArgumentOutOfRangeException("offset");
                long writeEnd = offset + data.Length;

                //if (offset >= _fsData.Length)
                if (offset >= fsLength)
                {
                    if (writeEnd > checked(fsLength + _seqBuf.EOF))
                        throw new ArgumentOutOfRangeException("offset");
                    //Overwriting sequential buffer
                    //_seqBuf.Write_ByOffset(Convert.ToInt32(offset - _fsData.Length), data);                    
                    _seqBuf.Write_ByOffset(Convert.ToInt32(offset - fsLength), data);
                    return;
                }

                //if (offset < _fsData.Length && offset + data.Length > _fsData.Length)
                if (offset < fsLength && writeEnd > fsLength)
                {
                    throw new Exception("FSR.WriteByOffset: offset < _fsData.Length && offset + data.Length > _fsData.Length");
                }

                //if (offset + data.Length > (_fsData.Length + _seqBuf.EOF))
                if (writeEnd > checked(fsLength + _seqBuf.EOF))
                {
                    //DB RULE1. We cant update and go out of the end of file. Only if we write into empty file root in the beginning
                    throw new Exception("FSR.WriteByOffset: offset + data.Length > (_fsData.Length + seqEOF)");
                }

                byte[] inBuf = null;
                if (_randBuf.TryGetValue(offset, out inBuf))
                {
                    if (inBuf.Length != data.Length)
                    {
                        //OLD solution
                        //it means we overwrite second time the same position with different length of data - what is not allowed
                        //throw new Exception("FSR.WriteByOffset: inBuf.Length != data.Length");

                        //Solution from 20140425
                        //we just overwrite offset value with the new data
                    }

                    usedBufferSize += data.Length - inBuf.Length;
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
                if (usedBufferSize >= maxRandomBufferSize || _randBuf.Count > maxRandomElementsCount)
                    FlushRandomBuffer();
            }
        }
        
        /// <summary>
        /// Is called only from lock_fs and must be finished by calling NET_Flush
        /// </summary>     
        void FlushRandomBuffer()
        {
            if (_randBuf.Count == 0)
                return;

            bool flushRollback = false;
            List<long> keys = new List<long>(_randBuf.Keys);
            keys.Sort();

            foreach (long key in keys)
                if (PreserveRollbackRange(key, _randBuf[key].Length))
                    flushRollback = true;

            if (flushRollback)
            {

                //Flushing rollback
                NET_Flush(_fsRollback);

                byte[] marker = eofRollback.To_8_bytes_array_BigEndian();
                _fsRollbackHelper.Position = 0;
                _fsRollbackHelper.Write(marker, 0, marker.Length);

                //Flushing rollback helper
                NET_Flush(_fsRollbackHelper);


                if (_backupIsActive)
                {
                    this._configuration.Backup.WriteBackupElement(ulFileName, 2, 0, marker);
                    this._configuration.Backup.Flush();
                }
            }

            //second loop for saving data
            foreach (long key in keys)
            {
                byte[] value = _randBuf[key];
                _fsData.Position = key;
                _fsData.Write(value, 0, value.Length);

                if (key + value.Length > fsLength)
                    fsLength = key + value.Length;

                if (_backupIsActive)
                    this._configuration.Backup.WriteBackupElement(ulFileName, 0, key, value);
            }

            //No flush of data file, it will be done on Flush()                        

            _randBuf.Clear();
            usedBufferSize = 0;
        }

        byte[] EncodePointer(long position)
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

        bool PreserveRollbackRange(long dataOffset, int length)
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

        void AppendRollbackRecord(long dataOffset, int length)
        {
            int headerLength = 1 + DefaultPointerLen + 4;
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

            _fsData.Position = dataOffset;
            ReadExactly(_fsData, record, headerLength, length);
            long recordOffset = eofRollback;
            _fsRollback.Position = recordOffset;
            _fsRollback.Write(record, 0, record.Length);
            if (_backupIsActive)
                _configuration.Backup.WriteBackupElement(ulFileName, 1, recordOffset, record);

            _rollbackCache.Add(dataOffset, new RollbackRecord { o = checked(recordOffset + headerLength), l = length });
            eofRollback = checked(recordOffset + record.Length);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="useCache"></param>
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
        /// <param name="useCache"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        #if false
        public byte[] Table_Read(bool useCache, long offset, int count)
        {
            byte[] res = null;

            lock (lock_fs)
            {
                if (!useCache)
                {
                    //WRITER

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
                    //if (offset + count > _fsData.Length + _seqBuf.EOF)
                    //    res = new byte[_fsData.Length + _seqBuf.EOF - offset];
                    if (offset + count > fsLength + _seqBuf.EOF)
                        res = new byte[fsLength + _seqBuf.EOF - offset];
                    else
                        res = new byte[count];

                    byte[] btWork = null;

                    //if (offset < _fsData.Length)
                    if (offset < fsLength)
                    {
                        //Starting reading from file
                        _fsData.Position = offset;

                        if (offset + res.Length <= _fsData.Length)
                        {
                            //must be taken completely from file
                            _fsData.Read(res, 0, res.Length);
                            //Console.WriteLine("3;{0};{1}", offset, ((res == null) ? -1 : res.Length));
                        }
                        else
                        {
                            //partly from file, partly from sequential cache
                            //int v1 = Convert.ToInt32(_fsData.Length - offset);
                            int v1 = Convert.ToInt32(fsLength - offset);
                            _fsData.Read(res, 0, v1);
                            //Console.WriteLine("4;{0};{1}", offset, ((res == null) ? -1 : res.Length));
                            Buffer.BlockCopy(_seqBuf.RawBuffer, 0, res, v1, res.Length - v1);
                        }
                    }
                    else
                    {
                        //!!! threat if seqBuf is empty, should not happen thou

                        //completely taken from seqbuf
                        //Buffer.BlockCopy(_seqBuf.RawBuffer, Convert.ToInt32(offset - _fsData.Length), res, 0, res.Length);
                        Buffer.BlockCopy(_seqBuf.RawBuffer, Convert.ToInt32(offset - fsLength), res, 0, res.Length);
                    }


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
                    //READER

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
                    ///////

                    //NOW
                    if (offset + count > eofData)
                    {
                        if (eofData < offset && TransactionalCommitIsStarted)   //NOT FINISHED multi-table COMMIT. SelectDirect case
                        {
                            //Probably not finished transaction and SelectDirect case. We return value,
                            //because at this momont all transaction table have successfully gone through TransactionalCommit() procedure.

                            //if (offset + count > this._fsData.Length)
                            if (offset + count > fsLength)
                            {
                                //res = new byte[this._fsData.Length - offset];
                                res = new byte[fsLength - offset];
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

                    _fsData.Position = offset;
                    _fsData.Read(res, 0, res.Length);
                    //Console.WriteLine("1;{0};{1}", offset, ((res == null) ? -1 : res.Length));

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

                        _fsRollback.Position = rb.o;
                        _fsRollback.Read(btWork, 0, btWork.Length);

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
                    int resultLength = GetReadLength(offset, count, checked(fsLength + _seqBuf.EOF));
                    if (resultLength == 0)
                        return new byte[0];

                    byte[] result = new byte[resultLength];
                    if (offset < fsLength)
                    {
                        _fsData.Position = offset;
                        int diskPart = (int)Math.Min((long)resultLength, fsLength - offset);
                        ReadExactly(_fsData, result, 0, diskPart);
                        if (diskPart < resultLength)
                            Buffer.BlockCopy(_seqBuf.RawBuffer, 0, result, diskPart, resultLength - diskPart);
                    }
                    else
                    {
                        Buffer.BlockCopy(_seqBuf.RawBuffer, checked((int)(offset - fsLength)), result, 0, result.Length);
                    }

                    foreach (KeyValuePair<long, byte[]> buffered in _randBuf)
                        CopyIntersection(buffered.Key, buffered.Value, offset, result);
                    return result;
                }

                long visibleLength = eofData;
                if (offset > eofData && TransactionalCommitIsStarted)
                    visibleLength = fsLength;
                int committedLength = GetReadLength(offset, count, visibleLength);
                if (committedLength == 0)
                    return new byte[0];

                byte[] committed = new byte[committedLength];
                _fsData.Position = offset;
                ReadExactly(_fsData, committed, 0, committed.Length);
                foreach (KeyValuePair<long, RollbackRecord> rollback in _rollbackCache)
                {
                    long rollbackEnd = rollback.Key + rollback.Value.l;
                    long resultEnd = offset + committed.Length;
                    if (rollback.Key >= resultEnd || rollbackEnd <= offset)
                        continue;

                    byte[] rollbackData = new byte[rollback.Value.l];
                    _fsRollback.Position = rollback.Value.o;
                    ReadExactly(_fsRollback, rollbackData, 0, rollbackData.Length);
                    CopyIntersection(rollback.Key, rollbackData, offset, committed);
                }
                return committed;
            }
        }

        static int GetReadLength(long offset, int count, long length)
        {
            if (count == 0 || offset >= length)
                return 0;
            long available = length - offset;
            return available < count ? (int)available : count;
        }

        static void CopyIntersection(long sourceOffset, byte[] source, long destinationOffset, byte[] destination)
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
                FlushSequentialBuffer();
                FlushRandomBuffer();

                NET_Flush(_fsData);

                if (_backupIsActive)
                {
                    this._configuration.Backup.Flush();
                }

                if (eofRollback != 0)
                {
                    //Finalizing rollback helper

                    eofRollback = 0;
                    _fsRollbackHelper.Position = 0;
                    _fsRollbackHelper.Write(eofRollback.To_8_bytes_array_BigEndian(), 0, 8);

                    NET_Flush(_fsRollbackHelper);

                    if (_backupIsActive)
                    {
                        this._configuration.Backup.WriteBackupElement(ulFileName, 2, 0, eofRollback.To_8_bytes_array_BigEndian());
                        this._configuration.Backup.Flush();
                    }
                }

                _rollbackCache.Clear();

                //eofData = this._fsData.Length;
                eofData = fsLength;

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
                FlushSequentialBuffer();
                FlushRandomBuffer();

                NET_Flush(_fsData);

                TransactionalCommitIsStarted = true;
            }

            if (_backupIsActive)
            {
                this._configuration.Backup.Flush();
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
                    _fsRollbackHelper.Position = 0;
                    _fsRollbackHelper.Write(eofRollback.To_8_bytes_array_BigEndian(), 0, 8);

                    NET_Flush(_fsRollbackHelper);

                    if (_backupIsActive)
                    {
                        this._configuration.Backup.WriteBackupElement(ulFileName, 2, 0, eofRollback.To_8_bytes_array_BigEndian());
                        this._configuration.Backup.Flush();
                    }
                }

                _rollbackCache.Clear();

                //eofData = this._fsData.Length;
                eofData = fsLength;

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
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.RESTORE_ROLLBACK_DATA_FAILED, _fileName, ex);
            }
        }

        /// <summary>
        /// Standard and transactional rollback
        /// </summary>
        #if false
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
                            _fsRollback.Position = rb.Value.o;
                            _fsRollback.Read(btWork, 0, btWork.Length);

                            _fsData.Position = rb.Key;
                            _fsData.Write(btWork, 0, btWork.Length);

                            if (rb.Key + btWork.Length > fsLength)
                                fsLength = rb.Key + btWork.Length;

                            if (_backupIsActive)
                            {
                                this._configuration.Backup.WriteBackupElement(ulFileName, 0, rb.Key, btWork);
                            }
                        }

                        NET_Flush(_fsData);

                         if (_backupIsActive)
                        {
                            this._configuration.Backup.Flush();
                        }

                        //Restoring rhp
                        eofRollback = 0;
                        _fsRollbackHelper.Position = 0;
                        _fsRollbackHelper.Write(eofRollback.To_8_bytes_array_BigEndian(), 0, 8);

                        NET_Flush(_fsRollbackHelper);

                        if (_backupIsActive)
                        {
                            this._configuration.Backup.WriteBackupElement(ulFileName, 2, 0, eofRollback.To_8_bytes_array_BigEndian());
                            this._configuration.Backup.Flush();
                        }

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
        #endif

        public void Rollback()
        {
            try
            {
                lock (lock_fs)
                    RollbackCore();
            }
            catch (Exception ex)
            {
                IsOperable = false;
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.RESTORE_ROLLBACK_DATA_FAILED, _fileName, ex);
            }
        }

        void RollbackCore()
        {
            _seqBuf.Clear(false);
            if (_randBuf.Count != 0)
            {
                usedBufferSize = 0;
                _randBuf.Clear();
            }

            if (_rollbackCache.Count == 0)
                return;

            foreach (KeyValuePair<long, RollbackRecord> rollback in _rollbackCache)
            {
                byte[] rollbackData = new byte[rollback.Value.l];
                _fsRollback.Position = rollback.Value.o;
                ReadExactly(_fsRollback, rollbackData, 0, rollbackData.Length);
                _fsData.Position = rollback.Key;
                _fsData.Write(rollbackData, 0, rollbackData.Length);
                if (rollback.Key + rollbackData.Length > fsLength)
                    fsLength = rollback.Key + rollbackData.Length;
                if (_backupIsActive)
                    _configuration.Backup.WriteBackupElement(ulFileName, 0, rollback.Key, rollbackData);
            }

            NET_Flush(_fsData);
            if (_backupIsActive)
                _configuration.Backup.Flush();

            eofRollback = 0;
            byte[] marker = eofRollback.To_8_bytes_array_BigEndian();
            _fsRollbackHelper.Position = 0;
            _fsRollbackHelper.Write(marker, 0, marker.Length);
            NET_Flush(_fsRollbackHelper);
            if (_backupIsActive)
            {
                _configuration.Backup.WriteBackupElement(ulFileName, 2, 0, marker);
                _configuration.Backup.Flush();
            }
            _rollbackCache.Clear();
        }











        
    }
}
