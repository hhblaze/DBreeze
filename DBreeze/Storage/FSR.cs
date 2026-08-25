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
    internal class FSR : IStorage
    {
        //!!try catches can be taken away from reads and writes, when procs are fully balanced
        //!!15.02.2019 was implemented everywhere: fsLength instead of this._fsData.Length was added on 23.03.2017 for now only in DBreeze .NET4.5, later integrate everywhere

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

        FileStream _fsData = null;
        FileStream _fsRollback = null;
        FileStream _fsRollbackHelper = null;
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
                TransactionalCommitIsStarted = false;
            }

        }

        #region Initialization

        private void InitFiles()
        {
            //Creates filestreams and rollbacks, restores rollback to the initial file, if necessary

            try
            {
                this._fsData = new FileStream(this._fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _fileStreamBufferSize, FileOptions.WriteThrough);
                this._fsRollback = new FileStream(this._fileName + ".rol", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _fileStreamBufferSize, FileOptions.WriteThrough);
                this._fsRollbackHelper = new FileStream(this._fileName + ".rhp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, _fileStreamBufferSize, FileOptions.WriteThrough);

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

        private struct StartupRollbackRecord
        {
            public long DataOffset;
            public long RollbackOffset;
            public long Length;
        }

        private void InitRollback()
        {
            byte[] btWork = new byte[8];
            _fsRollbackHelper.Position = 0;
            int markerLength = (int)Math.Min(_fsRollbackHelper.Length, btWork.Length);
            ReadExactly(_fsRollbackHelper, btWork, 0, markerLength);
            eofRollback = btWork.To_Int64_BigEndian();

            if (_trieSettings.RollbackRecovery == RollbackRecoveryIntent.FinalizeJournalCommitted)
            {
                FinalizeJournalCommittedRollback();
                return;
            }

            if (eofRollback == 0)
            {
                RecreateRollbackFileIfNeeded();
                return;
            }

            RestoreInitRollback();
            ClearRollbackMarker(false);
            RecreateRollbackFileIfNeeded();
        }

        private void FinalizeJournalCommittedRollback()
        {
            // The durable transaction-journal row is the commit point. Data was
            // flushed by TransactionalCommit, so replaying .rol here would split a
            // multi-table transaction after a crash between participant finalizers.
            ClearRollbackMarker(true);
            RecreateRollbackFileIfNeeded();
        }

        private void ClearRollbackMarker(bool synchronizeBackup)
        {
            eofRollback = 0;
            byte[] marker = eofRollback.To_8_bytes_array_BigEndian();
            _fsRollbackHelper.Position = 0;
            _fsRollbackHelper.Write(marker, 0, marker.Length);
            DurabilityTestHooks.Hit("storage.zero-marker.written");
            NET_Flush(_fsRollbackHelper);
            DurabilityTestHooks.Hit("storage.zero-marker.flushed");
            DurabilityTestHooks.Hit("recovery.marker.flushed");

            // This is deliberately unconditional for committed recovery. A prior
            // process may have flushed the local zero marker and died before the
            // corresponding backup marker became durable.
            if (synchronizeBackup && _backupIsActive)
            {
                _configuration.Backup.WriteBackupElement(ulFileName, 2, 0, marker);
                _configuration.Backup.Flush();
            }
        }

        private void RecreateRollbackFileIfNeeded()
        {
            if (_fsRollback.Length < MaxRollbackFileSize)
                return;

            _fsRollback.Dispose();
            File.Delete(_fileName + ".rol");
            _fsRollback = new FileStream(_fileName + ".rol", FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None, _fileStreamBufferSize, FileOptions.WriteThrough);
            DurabilityTestHooks.Hit("recovery.rollback.recycled");
        }

        void RestoreInitRollback()
        {
            List<StartupRollbackRecord> records;
            try
            {
                records = ReadStartupRollbackRecords();
            }
            catch (InvalidDataException)
            {
                RestoreInitRollbackLegacyCompatible();
                return;
            }
            catch (EndOfStreamException)
            {
                RestoreInitRollbackLegacyCompatible();
                return;
            }

            byte[] copyBuffer = new byte[64 * 1024];
            for (int i = records.Count - 1; i >= 0; i--)
            {
                StartupRollbackRecord record = records[i];
                _fsRollback.Position = record.RollbackOffset;
                _fsData.Position = record.DataOffset;
                long remaining = record.Length;
                while (remaining > 0)
                {
                    int chunk = remaining > copyBuffer.Length ? copyBuffer.Length : (int)remaining;
                    ReadExactly(_fsRollback, copyBuffer, 0, chunk);
                    _fsData.Write(copyBuffer, 0, chunk);
                    remaining -= chunk;
                }
            }

            NET_Flush(_fsData);
            DurabilityTestHooks.Hit("recovery.data.flushed");
        }

        private List<StartupRollbackRecord> ReadStartupRollbackRecords()
        {
            int headerLength = 1 + DefaultPointerLen + 4;
            byte[] header = new byte[headerLength];
            List<StartupRollbackRecord> records = new List<StartupRollbackRecord>();
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

                records.Add(new StartupRollbackRecord
                {
                    DataOffset = (long)targetOffset,
                    RollbackOffset = rollbackPosition + headerLength,
                    Length = dataLength
                });

                rollbackPosition = recordEnd;
            }

            return records;
        }

        private void RestoreInitRollbackLegacyCompatible()
        {
            int headerLength = 1 + DefaultPointerLen + 4;
            byte[] header = new byte[headerLength];
            byte[] copyBuffer = new byte[64 * 1024];
            long rollbackPosition = 0;
            byte[] pendingBufferedWrite = null;
            long pendingBufferedWriteOffset = 0;

            if (eofRollback < 0)
            {
                if (_fsRollback.Length == 0 && unchecked((int)eofRollback) == 0)
                {
                    NET_Flush(_fsData);
                    DurabilityTestHooks.Hit("recovery.data.flushed");
                    return;
                }
                throw new InvalidDataException("Negative rollback marker.");
            }

            long readableEnd = eofRollback < _fsRollback.Length ? eofRollback : _fsRollback.Length;

            while (rollbackPosition < readableEnd)
            {
                // a83424e treated an incomplete final record as not-yet-written and
                // completed recovery using only preceding full records.
                if (readableEnd - rollbackPosition < headerLength)
                {
                    _fsRollback.Position = rollbackPosition;
                    if (_fsRollback.ReadByte() != 1)
                        throw new InvalidDataException("Unknown rollback protocol.");
                    break;
                }

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

                long recordEnd = rollbackPosition + headerLength + (long)dataLength;
                if (recordEnd < rollbackPosition || recordEnd > readableEnd)
                    break;
                if (targetOffset > Int64.MaxValue || (long)targetOffset > Int64.MaxValue - dataLength)
                    throw new ArgumentOutOfRangeException("targetOffset");

                if (pendingBufferedWrite != null)
                {
                    _fsData.Position = pendingBufferedWriteOffset;
                    _fsData.Write(pendingBufferedWrite, 0, pendingBufferedWrite.Length);
                    NET_Flush(_fsData);
                    pendingBufferedWrite = null;
                }

                if (dataLength < _fileStreamBufferSize)
                {
                    pendingBufferedWrite = new byte[(int)dataLength];
                    pendingBufferedWriteOffset = (long)targetOffset;
                    ReadExactly(_fsRollback, pendingBufferedWrite, 0, pendingBufferedWrite.Length);
                }
                else
                {
                    _fsData.Position = (long)targetOffset;
                    long remaining = dataLength;
                    while (remaining > 0)
                    {
                        int chunk = remaining > copyBuffer.Length ? copyBuffer.Length : (int)remaining;
                        ReadExactly(_fsRollback, copyBuffer, 0, chunk);
                        _fsData.Write(copyBuffer, 0, chunk);
                        remaining -= chunk;
                    }
                    NET_Flush(_fsData);
                }

                rollbackPosition = recordEnd;
            }

            if (pendingBufferedWrite != null)
            {
                _fsData.Position = pendingBufferedWriteOffset;
                _fsData.Write(pendingBufferedWrite, 0, pendingBufferedWrite.Length);
            }

            NET_Flush(_fsData);
            DurabilityTestHooks.Hit("recovery.data.flushed");
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
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
#if NET40
        public static void NET_Flush(FileStream mfs)
        {
            mfs.Flush(true);

            //VolumeInfo.GetVolumes()[0].FlushAll();  
        }
#else

        [System.Runtime.InteropServices.DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool FlushFileBuffers(IntPtr hFile);

        public static void NET_Flush(FileStream mfs)
        {
            mfs.Flush();
            IntPtr handle = mfs.SafeFileHandle.DangerousGetHandle();

            if (!FlushFileBuffers(handle))
                throw new System.ComponentModel.Win32Exception();
        }
#endif
        #endregion

        #region "RestoreTableFromTheOtherTable"

        /// <summary>
        /// 
        /// </summary>
        /// <param name="newTableFullPath"></param>
        public void RestoreTableFromTheOtherTable(string newTableFullPath)
        {
            if (String.IsNullOrEmpty(newTableFullPath))
                throw new ArgumentNullException("newTableFullPath");

            lock (lock_fs)
            {
                string source = Path.GetFullPath(newTableFullPath);
                string destination = Path.GetFullPath(_fileName);
                StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;

                if (String.Equals(source, destination, comparison))
                    throw new ArgumentException("Source and destination table paths must differ.", "newTableFullPath");
                if (!File.Exists(source))
                    throw new FileNotFoundException("Source table does not exist.", source);

                string suffix = ".restore-backup-" + Guid.NewGuid().ToString("N");
                string[] destinationFiles = new string[] { destination, destination + ".rol", destination + ".rhp" };
                string[] sourceFiles = new string[] { source, source + ".rol", source + ".rhp" };
                string[] backupFiles = new string[] { destination + suffix, destination + ".rol" + suffix, destination + ".rhp" + suffix };
                bool[] destinationMoved = new bool[3];
                bool[] sourceMoved = new bool[3];

                CloseStorageStreams();

                try
                {
                    for (int i = 0; i < destinationFiles.Length; i++)
                    {
                        if (File.Exists(destinationFiles[i]))
                        {
                            File.Move(destinationFiles[i], backupFiles[i]);
                            destinationMoved[i] = true;
                        }
                    }

                    for (int i = 0; i < sourceFiles.Length; i++)
                    {
                        if (File.Exists(sourceFiles[i]))
                        {
                            File.Move(sourceFiles[i], destinationFiles[i]);
                            sourceMoved[i] = true;
                        }
                    }

                    ResetBuffers();
                    InitFiles();

                    for (int i = 0; i < backupFiles.Length; i++)
                    {
                        if (File.Exists(backupFiles[i]))
                            File.Delete(backupFiles[i]);
                    }
                }
                catch
                {
                    CloseStorageStreams();

                    for (int i = sourceFiles.Length - 1; i >= 0; i--)
                    {
                        if (sourceMoved[i] && File.Exists(destinationFiles[i]) && !File.Exists(sourceFiles[i]))
                            File.Move(destinationFiles[i], sourceFiles[i]);
                    }

                    for (int i = destinationFiles.Length - 1; i >= 0; i--)
                    {
                        if (destinationMoved[i] && File.Exists(backupFiles[i]))
                        {
                            if (File.Exists(destinationFiles[i]))
                                File.Delete(destinationFiles[i]);
                            File.Move(backupFiles[i], destinationFiles[i]);
                        }
                    }

                    ResetBuffers();
                    InitFiles();
                    throw;
                }

            }
        }

        private void CloseStorageStreams()
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

        private void ResetBuffers()
        {
            _randBuf.Clear();
            _rollbackCache.Clear();
            usedBufferSize = 0;
            eofRollback = 0;
            eofData = 0;
            fsLength = 0;
            TransactionalCommitIsStarted = false;
            _seqBuf.Clear(true);
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
                eofData = 0;
                fsLength = 0;
                _seqBuf.Clear(true);

                File.Delete(this._fileName);
                File.Delete(this._fileName + ".rol");
                File.Delete(this._fileName + ".rhp");

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
            {
                this._configuration.Backup.WriteBackupElement(ulFileName, 0, pos, _seqBuf.RawBuffer, 0, _seqBuf.EOF);
            }

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
                    {
                        this._configuration.Backup.WriteBackupElement(ulFileName, 0, position, data);
                    }

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
                DurabilityTestHooks.Hit("storage.rollback.written");

                //Flushing rollback
                NET_Flush(_fsRollback);
                DurabilityTestHooks.Hit("storage.rollback.flushed");

                //Writing into helper
                byte[] marker = eofRollback.To_8_bytes_array_BigEndian();
                _fsRollbackHelper.Position = 0;
                _fsRollbackHelper.Write(marker, 0, marker.Length);
                DurabilityTestHooks.Hit("storage.active-marker.written");

                //Flushing rollback helper
                NET_Flush(_fsRollbackHelper);
                DurabilityTestHooks.Hit("storage.active-marker.flushed");


                if (_backupIsActive)
                {
                    this._configuration.Backup.WriteBackupElement(ulFileName, 2, 0, marker);
                    this._configuration.Backup.Flush();
                }
            }

            //second loop for saving data
            for (int i = 0; i < _randBuf.Count; i++)
            {
                BufferedWriteSet.Segment segment = _randBuf[i];
                _fsData.Position = segment.Offset;
                _fsData.Write(segment.Buffer, segment.BufferOffset, segment.Length);

                if (segment.End > fsLength)
                    fsLength = segment.End;

                if (_backupIsActive)
                {
                    _configuration.Backup.WriteBackupElement(ulFileName, 0, segment.Offset,
                        segment.Buffer, segment.BufferOffset, segment.Length);
                }
            }
            DurabilityTestHooks.Hit("storage.data.written");

            //No flush of data file, it will be done on Flush()                        

            _randBuf.Clear();
            usedBufferSize = 0;
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

            _rollbackCache.Add(dataOffset, new RollbackRecord { o = recordOffset + headerLength, l = length });
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
                    int resultLength = GetReadLength(offset, count, fsLength + _seqBuf.EOF);
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
                        Buffer.BlockCopy(_seqBuf.RawBuffer, Convert.ToInt32(offset - fsLength), result, 0, result.Length);
                    }

                    if (_randBuf.Count != 0)
                        _randBuf.Overlay(offset, result);

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
                FlushSequentialBuffer();
                FlushRandomBuffer();

                NET_Flush(_fsData);
                DurabilityTestHooks.Hit("storage.data.flushed");

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
                    DurabilityTestHooks.Hit("storage.zero-marker.written");

                    NET_Flush(_fsRollbackHelper);
                    DurabilityTestHooks.Hit("storage.zero-marker.flushed");

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
                DurabilityTestHooks.Hit("storage.data.flushed");

                TransactionalCommitIsStarted = true;
            }

            if (_backupIsActive)
            {
                this._configuration.Backup.Flush();
            }
            DurabilityTestHooks.Hit("transaction.participant-prepared");
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
                    DurabilityTestHooks.Hit("storage.zero-marker.written");

                    NET_Flush(_fsRollbackHelper);
                    DurabilityTestHooks.Hit("storage.zero-marker.flushed");

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
            _fsRollbackHelper.Position = 0;
            _fsRollbackHelper.Write(eofRollback.To_8_bytes_array_BigEndian(), 0, 8);
            NET_Flush(_fsRollbackHelper);

            if (_backupIsActive)
            {
                _configuration.Backup.WriteBackupElement(ulFileName, 2, 0, eofRollback.To_8_bytes_array_BigEndian());
                _configuration.Backup.Flush();
            }

            _rollbackCache.Clear();
        }












    }
}
