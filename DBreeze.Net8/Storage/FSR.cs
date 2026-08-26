/*
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Win32.SafeHandles;

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
        readonly SortedList<long, RollbackRecord> _rollbackCache = new SortedList<long, RollbackRecord>();

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
        readonly ReaderWriterLockSlim lock_fs = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        FileStream _fsData = null;
        FileStream _fsRollback = null;
        FileStream _fsRollbackHelper = null;
        const int SharedReadBufferSize = 8 * 1024;
        const int SingleReaderReadAheadSize = 4 * 1024;
        const int ReadPageSize = 32 * 1024;
        const int ContendedReadWindowSize = 512;
        const int SmallReadThreshold = 256;
        readonly object _sharedReadLane = new object();
        byte[] _sharedReadBuffer;
        long _sharedReadBufferOffset;
        int _sharedReadBufferLength;
        long _sharedReadBufferMutationVersion;
        static long _nextInstanceId;
        readonly long _instanceId = Interlocked.Increment(ref _nextInstanceId);
        long _mutationVersion = 1;
        long _physicalDataLength;

        [ThreadStatic]
        static ReadPageCache _threadReadPageCache;
        [ThreadStatic]
        static ReadPageCache _threadContendedReadWindow;

        sealed class ReadPageCache
        {
            public byte[] Buffer;
            public long OwnerId;
            public long MutationVersion;
            public long PageOffset;
            public int PageLength;
            public byte CandidateAccessCount;
            public bool IsPopulated;
        }

        /// <summary>
        /// Pointer to the end of file, before current commit
        /// </summary>
        long eofData = 0;
        long eofRollback = 0;

        TrieSettings _trieSettings = null;
        ushort DefaultPointerLen = 0;
        DBreezeConfiguration _configuration = null;

        bool _backupIsActive = false;

        /// <summary>
        /// DateTime when file was initialized. Is remembered by LTrieRow, based on this file.
        /// If file is change after RestoreTableFromTheOtherTable or RecreateFiles,
        /// LTrieRow will have different version and will return exception.
        /// </summary>
        // LTrieRow uses this generation stamp to reject stale links after a table
        // restore/recreate. It is read on every point lookup, so it must not acquire
        // the storage I/O lock. InitFiles publishes the new generation atomically.
        long _storageFixTimeTicks = DateTime.UtcNow.Ticks;

        #endregion

        private readonly struct StorageLockScope : IDisposable
        {
            private readonly ReaderWriterLockSlim _lock;
            private readonly object _writeGate;
            private readonly bool _write;

            public StorageLockScope(ReaderWriterLockSlim sync, object writeGate, bool write)
            {
                _lock = sync;
                _writeGate = writeGate;
                _write = write;
                if (write)
                {
                    Monitor.Enter(writeGate);
                    try
                    {
                        sync.EnterWriteLock();
                    }
                    catch
                    {
                        Monitor.Exit(writeGate);
                        throw;
                    }
                }
                else
                    sync.EnterReadLock();
            }

            public void Dispose()
            {
                if (_lock == null)
                    return;

                if (_write)
                {
                    _lock.ExitWriteLock();
                    Monitor.Exit(_writeGate);
                }
                else
                    _lock.ExitReadLock();
            }
        }

        private StorageLockScope AcquireReadLock() => new StorageLockScope(lock_fs, null, false);
        private StorageLockScope AcquireWriteLock() => new StorageLockScope(lock_fs, _sharedReadLane, true);

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
            get { using (AcquireReadLock()) { return this.eofData; } }
        }

        /// <summary>
        /// Returns time of file initiation, ead remarks on
        /// </summary>
        public DateTime StorageFixTime
        {
            get { return new DateTime(Volatile.Read(ref _storageFixTimeTicks), DateTimeKind.Utc); }
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

                _seqBuf.Dispose();
                _randBuf.Clear();
                _rollbackCache.Clear();
                usedBufferSize = 0;
                eofData = 0;
                eofRollback = 0;
                _physicalDataLength = 0;
                InvalidateReadCache();
                _sharedReadBuffer = null;
                TransactionalCommitIsStarted = false;
            }

        }

        #region Initialization

        private void InitFiles()
        {
            //Creates filestreams and rollbacks, restores rollback to the initial file, if necessary

            try
            {
                this._fsData = OpenFile(this._fileName);
                this._fsRollback = OpenFile(this._fileName + ".rol");
                this._fsRollbackHelper = OpenFile(this._fileName + ".rhp");
                _physicalDataLength = GetLength(_fsData);

                //!!!!We dont have this value in root yet, could have and economize tail of the file in case if rollback occured

                if (_physicalDataLength == 0)
                {
                    //Writing initial root data

                    byte[] root = new byte[this._trieSettings.ROOT_SIZE];
                    WriteDataAt(root, 0, root.Length, 0);


                    if (_backupIsActive)
                    {
                        this._configuration.Backup.WriteBackupElement(ulFileName, 0, 0, new byte[this._trieSettings.ROOT_SIZE]);
                    }

                    //no flush here
                }

                eofData = _physicalDataLength;

                //Check is .rhp is empty add 0 pointer
                if (GetLength(_fsRollbackHelper) == 0)
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


                Volatile.Write(ref _storageFixTimeTicks, DateTime.UtcNow.Ticks);
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
            int markerLength = (int)Math.Min(GetLength(_fsRollbackHelper), btWork.Length);
            ReadExactlyAt(_fsRollbackHelper, btWork, 0, markerLength, 0);
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
            ClearRollbackMarker(true);
            RecreateRollbackFileIfNeeded();
        }

        private void ClearRollbackMarker(bool synchronizeBackup)
        {
            eofRollback = 0;
            byte[] marker = eofRollback.To_8_bytes_array_BigEndian();
            WriteAt(_fsRollbackHelper, marker, 0, marker.Length, 0);
            DurabilityTestHooks.Hit("storage.zero-marker.written");
            NET_Flush(_fsRollbackHelper);
            DurabilityTestHooks.Hit("storage.zero-marker.flushed");
            DurabilityTestHooks.Hit("recovery.marker.flushed");

            if (synchronizeBackup && _backupIsActive)
            {
                _configuration.Backup.WriteBackupElement(ulFileName, 2, 0, marker);
                _configuration.Backup.Flush();
            }
        }

        private void RecreateRollbackFileIfNeeded()
        {
            if (GetLength(_fsRollback) < MaxRollbackFileSize)
                return;

            _fsRollback.Dispose();
            File.Delete(_fileName + ".rol");
            _fsRollback = OpenFile(_fileName + ".rol");
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

            byte[] copyBuffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                for (int i = records.Count - 1; i >= 0; i--)
                {
                    StartupRollbackRecord record = records[i];
                    long remaining = record.Length;
                    long sourceOffset = record.RollbackOffset;
                    long destinationOffset = record.DataOffset;
                    while (remaining > 0)
                    {
                        int chunk = remaining > 64 * 1024 ? 64 * 1024 : (int)remaining;
                        ReadExactlyAt(_fsRollback, copyBuffer, 0, chunk, sourceOffset);
                        WriteDataAt(copyBuffer, 0, chunk, destinationOffset);
                        remaining -= chunk;
                        sourceOffset += chunk;
                        destinationOffset += chunk;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(copyBuffer);
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

                ReadExactlyAt(_fsRollback, header, 0, header.Length, rollbackPosition);
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

                long payloadOffset = rollbackPosition + headerLength;
                long recordEnd = payloadOffset + dataLength;
                if (recordEnd < rollbackPosition || recordEnd > eofRollback)
                    throw new InvalidDataException("Incomplete rollback record payload.");
                if ((long)targetOffset > Int64.MaxValue - dataLength)
                    throw new InvalidDataException("Rollback target range is too large.");

                records.Add(new StartupRollbackRecord
                {
                    DataOffset = (long)targetOffset,
                    RollbackOffset = payloadOffset,
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
            byte[] copyBuffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            long rollbackPosition = 0;
            byte[] pendingBufferedWrite = null;
            long pendingBufferedWriteOffset = 0;
            if (eofRollback < 0)
            {
                if (GetLength(_fsRollback) == 0 && unchecked((int)eofRollback) == 0)
                {
                    NET_Flush(_fsData);
                    DurabilityTestHooks.Hit("recovery.data.flushed");
                    return;
                }
                throw new InvalidDataException("Negative rollback marker.");
            }
            long rollbackLength = GetLength(_fsRollback);
            long readableEnd = eofRollback < rollbackLength ? eofRollback : rollbackLength;

            try
            {
                while (rollbackPosition < readableEnd)
                {
                    if (readableEnd - rollbackPosition < headerLength)
                    {
                        ReadExactlyAt(_fsRollback, header, 0, 1, rollbackPosition);
                        if (header[0] != 1)
                            throw new InvalidDataException("Unknown rollback protocol.");
                        break;
                    }

                    ReadExactlyAt(_fsRollback, header, 0, header.Length, rollbackPosition);
                    if (header[0] != 1)
                        throw new InvalidDataException("Unknown rollback protocol.");

                    ulong targetOffset = 0;
                    for (int i = 0; i < DefaultPointerLen; i++)
                        targetOffset = (targetOffset << 8) | header[1 + i];

                    uint dataLength = 0;
                    int lengthOffset = 1 + DefaultPointerLen;
                    for (int i = 0; i < 4; i++)
                        dataLength = (dataLength << 8) | header[lengthOffset + i];

                    long payloadOffset = rollbackPosition + headerLength;
                    long recordEnd = payloadOffset + dataLength;
                    if (recordEnd < rollbackPosition || recordEnd > readableEnd)
                        break;
                    if (targetOffset > Int64.MaxValue || (long)targetOffset > Int64.MaxValue - dataLength)
                        throw new ArgumentOutOfRangeException("targetOffset");

                    long sourceOffset = payloadOffset;
                    if (pendingBufferedWrite != null)
                    {
                        WriteDataAt(pendingBufferedWrite, 0, pendingBufferedWrite.Length, pendingBufferedWriteOffset);
                        NET_Flush(_fsData);
                        pendingBufferedWrite = null;
                    }

                    // The baseline FileStream retained the final sub-buffer write when a later
                    // protocol error escaped the constructor. Preserve that observable legacy
                    // corruption behaviour even though this implementation uses RandomAccess.
                    if (dataLength < 8192)
                    {
                        pendingBufferedWrite = new byte[(int)dataLength];
                        pendingBufferedWriteOffset = (long)targetOffset;
                        ReadExactlyAt(_fsRollback, pendingBufferedWrite, 0, pendingBufferedWrite.Length, sourceOffset);
                    }
                    else
                    {
                        long remaining = dataLength;
                        long destinationOffset = (long)targetOffset;
                        while (remaining > 0)
                        {
                            int chunk = remaining > 64 * 1024 ? 64 * 1024 : (int)remaining;
                            ReadExactlyAt(_fsRollback, copyBuffer, 0, chunk, sourceOffset);
                            WriteDataAt(copyBuffer, 0, chunk, destinationOffset);
                            remaining -= chunk;
                            sourceOffset += chunk;
                            destinationOffset += chunk;
                        }
                        NET_Flush(_fsData);
                    }

                    rollbackPosition = recordEnd;
                }

                if (pendingBufferedWrite != null)
                    WriteDataAt(pendingBufferedWrite, 0, pendingBufferedWrite.Length, pendingBufferedWriteOffset);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(copyBuffer);
            }

            NET_Flush(_fsData);
            DurabilityTestHooks.Hit("recovery.data.flushed");

        }
        #endregion

        #region "NET FLUSH"
        public static void NET_Flush(FileStream mfs)
        {
            mfs.Flush();
            RandomAccess.FlushToDisk(mfs.SafeFileHandle);
        }

        private static FileStream OpenFile(string path)
        {
            return new FileStream(path, new FileStreamOptions
            {
                Access = FileAccess.ReadWrite,
                Mode = FileMode.OpenOrCreate,
                Share = FileShare.None,
                BufferSize = 1,
                // The shared read lane supplies the historical 8 KiB locality. Keeping the OS
                // random-access hint here disables useful cache-manager read-ahead for trie walks.
                Options = FileOptions.WriteThrough
            });
        }

        private static long GetLength(FileStream stream) => RandomAccess.GetLength(stream.SafeFileHandle);

        private static void WriteAt(FileStream stream, byte[] buffer, int bufferOffset, int count, long fileOffset)
        {
            RandomAccess.Write(stream.SafeFileHandle, new ReadOnlySpan<byte>(buffer, bufferOffset, count), fileOffset);
        }

        private void WriteDataAt(byte[] buffer, int bufferOffset, int count, long fileOffset)
        {
            WriteAt(_fsData, buffer, bufferOffset, count, fileOffset);
            long end = checked(fileOffset + count);
            if (end > _physicalDataLength)
                _physicalDataLength = end;

            // A positioned write may replace bytes already held by another reader thread.
            // Versioning invalidates those pages without retaining references to this FSR.
            InvalidateReadCache();
        }

        private void InvalidateReadCache()
        {
            unchecked
            {
                _mutationVersion++;
                if (_mutationVersion == 0)
                    _mutationVersion = 1;
            }

            _sharedReadBufferLength = 0;
            _sharedReadBufferMutationVersion = 0;
        }

        private static void ReadExactlyAt(FileStream stream, byte[] buffer, int bufferOffset, int count, long fileOffset)
        {
            Span<byte> destination = new Span<byte>(buffer, bufferOffset, count);
            while (!destination.IsEmpty)
            {
                int read = RandomAccess.Read(stream.SafeFileHandle, destination, fileOffset);
                if (read == 0)
                    throw new EndOfStreamException("Unexpected end of storage stream.");
                destination = destination.Slice(read);
                fileOffset += read;
            }
        }

        private static void ReadExactlySequential(FileStream stream, byte[] buffer, int bufferOffset, int count,
            long fileOffset)
        {
            stream.Position = fileOffset;
            Span<byte> destination = new Span<byte>(buffer, bufferOffset, count);
            while (!destination.IsEmpty)
            {
                int read = stream.Read(destination);
                if (read == 0)
                    throw new EndOfStreamException("Unexpected end of storage stream.");
                destination = destination.Slice(read);
            }
        }
        #endregion

        #region "RestoreTableFromTheOtherTable"

        /// <summary>
        ///
        /// </summary>
        /// <param name="newTableFullPath"></param>
        public void RestoreTableFromTheOtherTable(string newTableFullPath)
        {
            if (String.IsNullOrEmpty(newTableFullPath))
                throw new ArgumentNullException(nameof(newTableFullPath));

            using (AcquireWriteLock())
            {
                string source = Path.GetFullPath(newTableFullPath);
                string destination = Path.GetFullPath(_fileName);
                StringComparison comparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;

                if (String.Equals(source, destination, comparison))
                    throw new ArgumentException("Source and destination table paths must differ.", nameof(newTableFullPath));
                if (!File.Exists(source))
                    throw new FileNotFoundException("Source table does not exist.", source);

                string suffix = ".restore-backup-" + Guid.NewGuid().ToString("N");
                string[] destinationFiles = { destination, destination + ".rol", destination + ".rhp" };
                string[] sourceFiles = { source, source + ".rol", source + ".rhp" };
                string[] backupFiles = { destination + suffix, destination + ".rol" + suffix, destination + ".rhp" + suffix };
                bool[] destinationMoved = new bool[3];
                bool[] sourceMoved = new bool[3];

                CloseStorageStreams();

                try
                {
                    for (int i = 0; i < destinationFiles.Length; i++)
                    {
                        if (!File.Exists(destinationFiles[i]))
                            continue;
                        File.Move(destinationFiles[i], backupFiles[i]);
                        destinationMoved[i] = true;
                    }

                    for (int i = 0; i < sourceFiles.Length; i++)
                    {
                        if (!File.Exists(sourceFiles[i]))
                            continue;
                        File.Move(sourceFiles[i], destinationFiles[i]);
                        sourceMoved[i] = true;
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
                        if (!destinationMoved[i] || !File.Exists(backupFiles[i]))
                            continue;
                        if (File.Exists(destinationFiles[i]))
                            File.Delete(destinationFiles[i]);
                        File.Move(backupFiles[i], destinationFiles[i]);
                    }

                    ResetBuffers();
                    InitFiles();
                    throw;
                }

            }
        }

        private void CloseStorageStreams()
        {
            _fsData?.Dispose();
            _fsData = null;
            _fsRollback?.Dispose();
            _fsRollback = null;
            _fsRollbackHelper?.Dispose();
            _fsRollbackHelper = null;
        }

        private void ResetBuffers()
        {
            _randBuf.Clear();
            _rollbackCache.Clear();
            usedBufferSize = 0;
            eofRollback = 0;
            eofData = 0;
            _physicalDataLength = 0;
            InvalidateReadCache();
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
                eofData = 0;
                _physicalDataLength = 0;
                InvalidateReadCache();
                TransactionalCommitIsStarted = false;
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

            long pos = _physicalDataLength;
            WriteDataAt(_seqBuf.RawBuffer, 0, _seqBuf.EOF, pos);

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
            ArgumentNullException.ThrowIfNull(data);

            long position = 0;
            byte[] encodedPosition = null;

            /* Emulation of the direct write to the disk without sequential cache */

            //_fsData.Position = position = _fsData.Length;
            //_fsData.Write(data, 0, data.Length);

            //return ((ulong)position).To_8_bytes_array_BigEndian().Substring(8 - DefaultPointerLen, DefaultPointerLen);

            /**************************************************************/



            using (AcquireWriteLock())
            {
                //case when incoming data bigger then buffer, we clean buffer and write data directly to the disk

                if (data.Length > _seqBufCapacity)
                {
                    FlushSequentialBuffer();
                    position = _physicalDataLength;
                    encodedPosition = EncodePointer(position);
                    checked { _ = position + data.Length; }
                    WriteDataAt(data, 0, data.Length, position);

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

                position = checked(_physicalDataLength + _seqBuf.EOF);
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

            using (AcquireWriteLock())
            {
                if (offset < 0 || offset > Int64.MaxValue - data.Length)
                    throw new ArgumentOutOfRangeException(nameof(offset));
                long writeEnd = offset + data.Length;
                long dataLength = _physicalDataLength;

                if (offset >= dataLength)
                {
                    if (writeEnd > checked(dataLength + _seqBuf.EOF))
                        throw new ArgumentOutOfRangeException(nameof(offset));
                    //Overwriting sequential buffer
                    _seqBuf.Write_ByOffset(Convert.ToInt32(offset - dataLength), data);
                    return;
                }

                if (offset < dataLength && writeEnd > dataLength)
                {
                    throw new Exception("FSR.WriteByOffset: offset < _fsData.Length && offset + data.Length > _fsData.Length");
                }

                if (writeEnd > checked(dataLength + _seqBuf.EOF))
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
                ref readonly BufferedWriteSet.Segment segment = ref _randBuf.GetSegment(i);
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
                WriteAt(_fsRollbackHelper, marker, 0, marker.Length, 0);
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
                ref readonly BufferedWriteSet.Segment segment = ref _randBuf.GetSegment(i);
                WriteDataAt(segment.Buffer, segment.BufferOffset, segment.Length, segment.Offset);

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

        private bool PreserveRollbackRange(long dataOffset, int length)
        {
            long end = checked(dataOffset + length);
            long cursor = dataOffset;
            bool appended = false;

            while (cursor < end)
            {
                IList<long> keys = _rollbackCache.Keys;
                int low = 0;
                int high = keys.Count;
                while (low < high)
                {
                    int middle = low + ((high - low) >> 1);
                    if (keys[middle] < cursor)
                        low = middle + 1;
                    else
                        high = middle;
                }

                int index = low;
                if (index > 0)
                {
                    long precedingStart = keys[index - 1];
                    long precedingEnd = checked(precedingStart + _rollbackCache.Values[index - 1].l);
                    if (precedingEnd > cursor)
                    {
                        cursor = precedingEnd < end ? precedingEnd : end;
                        continue;
                    }
                }

                if (index < keys.Count && keys[index] == cursor)
                {
                    long coveredEnd = checked(cursor + _rollbackCache.Values[index].l);
                    cursor = coveredEnd < end ? coveredEnd : end;
                    continue;
                }

                long nextCoveredStart = index < keys.Count && keys[index] < end ? keys[index] : end;
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

            ReadExactlyAt(_fsData, record, headerLength, length, dataOffset);

            long recordOffset = eofRollback;
            WriteAt(_fsRollback, record, 0, record.Length, recordOffset);
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
            // The historical FSR used one buffered FileStream cursor guarded by a table-wide
            // monitor. Keep that locality and admission behavior for small committed reads while
            // retaining positioned IO for writes, large reads, and reads of different tables.
            if (useCache && count > 0 && count < SharedReadBufferSize)
            {
                if (Monitor.TryEnter(_sharedReadLane))
                {
                    try
                    {
                        return Table_ReadCore(useCache, offset, count,
                            useSharedReadLane: true, useContendedReadWindow: false);
                    }
                    finally
                    {
                        Monitor.Exit(_sharedReadLane);
                    }
                }

                return Table_ReadCore(useCache, offset, count,
                    useSharedReadLane: false, useContendedReadWindow: true);
            }

            return Table_ReadCore(useCache, offset, count,
                useSharedReadLane: false, useContendedReadWindow: false);
        }

        private byte[] Table_ReadCore(bool useCache, long offset, int count,
            bool useSharedReadLane, bool useContendedReadWindow)
        {
            // Small committed reads already own _sharedReadLane. Writers take the same gate before
            // their write lock, so a second per-read ReaderWriterLockSlim acquisition is redundant.
            using (useSharedReadLane ? default : AcquireReadLock())
            {
                if (offset < 0)
                    throw new ArgumentOutOfRangeException(nameof(offset));
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));

                long dataLength = _physicalDataLength;
                if (!useCache)
                {
                    int resultLength = GetReadLength(offset, count, dataLength + _seqBuf.EOF);
                    if (resultLength == 0)
                        return Array.Empty<byte>();

                    byte[] result = GC.AllocateUninitializedArray<byte>(resultLength);
                    if (offset < dataLength)
                    {
                        int diskPart = (int)Math.Min((long)resultLength, dataLength - offset);
                        bool readFromPage = diskPart == resultLength &&
                            TryReadCommittedPage(offset, result, dataLength);
                        if (!readFromPage)
                            ReadExactlyAt(_fsData, result, 0, diskPart, offset);
                        if (diskPart < resultLength)
                            Buffer.BlockCopy(_seqBuf.RawBuffer, 0, result, diskPart, resultLength - diskPart);
                    }
                    else
                    {
                        Buffer.BlockCopy(_seqBuf.RawBuffer, checked((int)(offset - dataLength)), result, 0, result.Length);
                    }

                    OverlayRandomBuffer(offset, result);

                    return result;
                }

                // Physical length can already include an uncommitted append; eofData remains the
                // committed visibility boundary until the transaction is finished.
                long visibleLength = offset > eofData && TransactionalCommitIsStarted ? dataLength : eofData;
                int committedLength = GetReadLength(offset, count, visibleLength);
                if (committedLength == 0)
                    return Array.Empty<byte>();

                byte[] committed = GC.AllocateUninitializedArray<byte>(committedLength);
                bool canUseCommittedCache = _rollbackCache.Count == 0 && !TransactionalCommitIsStarted;
                if (useSharedReadLane)
                {
                    ReadFromSharedBuffer(offset, committed, visibleLength);
                    if (canUseCommittedCache)
                        return committed;
                }
                else if (canUseCommittedCache &&
                    (useContendedReadWindow
                        ? TryReadContendedWindow(offset, committed, visibleLength)
                        : TryReadCommittedPage(offset, committed, visibleLength)))
                {
                    return committed;
                }
                else
                {
                    ReadExactlyAt(_fsData, committed, 0, committed.Length, offset);
                }

                foreach (KeyValuePair<long, RollbackRecord> rollback in _rollbackCache)
                {
                    long rollbackEnd = rollback.Key + rollback.Value.l;
                    long resultEnd = offset + committed.Length;
                    if (rollback.Key >= resultEnd || rollbackEnd <= offset)
                        continue;

                    byte[] rollbackData = GC.AllocateUninitializedArray<byte>(rollback.Value.l);
                    ReadExactlyAt(_fsRollback, rollbackData, 0, rollbackData.Length, rollback.Value.o);
                    CopyIntersection(rollback.Key, rollbackData, offset, committed);
                }

                return committed;
            }
        }

        private void ReadFromSharedBuffer(long offset, byte[] result, long visibleLength)
        {
            long relativeOffset = offset - _sharedReadBufferOffset;
            bool cacheHit = _sharedReadBuffer != null &&
                _sharedReadBufferMutationVersion == _mutationVersion &&
                relativeOffset >= 0 &&
                relativeOffset <= _sharedReadBufferLength &&
                result.Length <= _sharedReadBufferLength - relativeOffset;

            if (!cacheHit)
            {
                _sharedReadBuffer ??= GC.AllocateUninitializedArray<byte>(SharedReadBufferSize);
                long previousBufferEnd = _sharedReadBufferOffset + _sharedReadBufferLength;
                bool continuesSequentialRead = _sharedReadBufferMutationVersion == _mutationVersion &&
                    offset >= previousBufferEnd - result.Length &&
                    offset <= previousBufferEnd + SharedReadBufferSize;
                int readAheadLength = continuesSequentialRead
                    ? SharedReadBufferSize
                    : Math.Max(result.Length, SingleReaderReadAheadSize);
                int bufferLength = (int)Math.Min(readAheadLength, visibleLength - offset);
                ReadExactlySequential(_fsData, _sharedReadBuffer, 0, bufferLength, offset);
                _sharedReadBufferOffset = offset;
                _sharedReadBufferLength = bufferLength;
                _sharedReadBufferMutationVersion = _mutationVersion;
                relativeOffset = 0;
            }
            new ReadOnlySpan<byte>(_sharedReadBuffer, (int)relativeOffset, result.Length).CopyTo(result);
        }

        private bool TryReadCommittedPage(long offset, byte[] result, long visibleLength)
        {
            int offsetInPage = (int)(offset & (ReadPageSize - 1));
            int count = result.Length;
            if (count > ReadPageSize - offsetInPage)
                return false;

            long pageOffset = offset - offsetInPage;
            int pageLength = (int)Math.Min(ReadPageSize, visibleLength - pageOffset);
            ReadPageCache cache = _threadReadPageCache ??= new ReadPageCache();
            long mutationVersion = _mutationVersion;

            // A hit is safe only for the same storage generation and committed page extent.
            // Rollback-backed and cross-page reads deliberately stay on the exact positioned path.
            if (cache.OwnerId != _instanceId ||
                cache.MutationVersion != mutationVersion ||
                cache.PageOffset != pageOffset ||
                cache.PageLength != pageLength)
            {
                cache.OwnerId = _instanceId;
                cache.MutationVersion = mutationVersion;
                cache.PageOffset = pageOffset;
                cache.PageLength = pageLength;
                cache.CandidateAccessCount = 1;
                cache.IsPopulated = false;
                return false;
            }

            if (!cache.IsPopulated)
            {
                // Small reads need two exact-read samples before admission. Larger reads retain
                // the previous second-access policy because a page fill amortizes much sooner.
                if (cache.CandidateAccessCount < byte.MaxValue)
                    cache.CandidateAccessCount++;
                int requiredAccessCount = count <= SmallReadThreshold ? 3 : 2;
                if (cache.CandidateAccessCount < requiredAccessCount)
                    return false;

                cache.Buffer ??= GC.AllocateUninitializedArray<byte>(ReadPageSize);
                ReadExactlyAt(_fsData, cache.Buffer, 0, pageLength, pageOffset);
                cache.IsPopulated = true;
            }

            Buffer.BlockCopy(cache.Buffer, offsetInPage, result, 0, count);
            return true;
        }

        private bool TryReadContendedWindow(long offset, byte[] result, long visibleLength)
        {
            ReadPageCache cache = _threadContendedReadWindow ??= new ReadPageCache();
            long mutationVersion = _mutationVersion;
            long relativeOffset = offset - cache.PageOffset;
            bool hit = cache.OwnerId == _instanceId && cache.MutationVersion == mutationVersion &&
                cache.IsPopulated && relativeOffset >= 0 &&
                relativeOffset <= cache.PageLength && result.Length <= cache.PageLength - relativeOffset;
            if (!hit)
            {
                int windowLength = (int)Math.Min(
                    Math.Max(result.Length, ContendedReadWindowSize), visibleLength - offset);
                cache.Buffer ??= GC.AllocateUninitializedArray<byte>(ContendedReadWindowSize);
                if (cache.Buffer.Length < windowLength)
                    cache.Buffer = GC.AllocateUninitializedArray<byte>(windowLength);
                ReadExactlyAt(_fsData, cache.Buffer, 0, windowLength, offset);
                cache.OwnerId = _instanceId;
                cache.MutationVersion = mutationVersion;
                cache.PageOffset = offset;
                cache.PageLength = windowLength;
                cache.IsPopulated = true;
                relativeOffset = 0;
            }

            Buffer.BlockCopy(cache.Buffer, (int)relativeOffset, result, 0, result.Length);
            return true;
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

        private void OverlayRandomBuffer(long offset, byte[] destination)
        {
            if (_randBuf.Count != 0)
                _randBuf.Overlay(offset, destination);
        }

#if false
        private byte[] Table_ReadLegacy(bool useCache, long offset, int count)
        {
            byte[] res = null;

            using (AcquireReadLock())
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
                    if (offset + count > _fsData.Length + _seqBuf.EOF)
                        res = new byte[_fsData.Length + _seqBuf.EOF - offset];
                    else
                        res = new byte[count];

                    byte[] btWork = null;

                    if (offset < _fsData.Length)
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
                            int v1 = Convert.ToInt32(_fsData.Length - offset);
                            _fsData.Read(res, 0, v1);
                            //Console.WriteLine("4;{0};{1}", offset, ((res == null) ? -1 : res.Length));
                            Buffer.BlockCopy(_seqBuf.RawBuffer, 0, res, v1, res.Length - v1);
                        }
                    }
                    else
                    {
                        //!!! threat if seqBuf is empty, should not happen thou

                        //completely taken from seqbuf
                        Buffer.BlockCopy(_seqBuf.RawBuffer, Convert.ToInt32(offset - _fsData.Length), res, 0, res.Length);
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

                            if (offset + count > this._fsData.Length)
                            {
                                res = new byte[this._fsData.Length - offset];
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


        /// <summary>
        /// Cleans all buffers and flushes data to the disk
        /// </summary>
        public void Commit()
        {
            using (AcquireWriteLock())
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
                    byte[] marker = eofRollback.To_8_bytes_array_BigEndian();
                    WriteAt(_fsRollbackHelper, marker, 0, marker.Length, 0);
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

                eofData = _physicalDataLength;

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
            using (AcquireWriteLock())
            {
                if (eofRollback != 0)
                {
                    //Finalizing rollback helper

                    eofRollback = 0;
                    byte[] marker = eofRollback.To_8_bytes_array_BigEndian();
                    WriteAt(_fsRollbackHelper, marker, 0, marker.Length, 0);
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

                eofData = _physicalDataLength;

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
                byte[] rollbackData = GC.AllocateUninitializedArray<byte>(rollback.Value.l);
                ReadExactlyAt(_fsRollback, rollbackData, 0, rollbackData.Length, rollback.Value.o);
                WriteDataAt(rollbackData, 0, rollbackData.Length, rollback.Key);

                if (_backupIsActive)
                    _configuration.Backup.WriteBackupElement(ulFileName, 0, rollback.Key, rollbackData);
            }

            NET_Flush(_fsData);
            if (_backupIsActive)
                _configuration.Backup.Flush();

            eofRollback = 0;
            byte[] marker = eofRollback.To_8_bytes_array_BigEndian();
            WriteAt(_fsRollbackHelper, marker, 0, marker.Length, 0);
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
