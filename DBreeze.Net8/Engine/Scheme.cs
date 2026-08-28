/*
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.IO;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

using DBreeze.Storage;
using DBreeze.LianaTrie;
using DBreeze.Utils;

using DBreeze.Exceptions;

using DBreeze.SchemeInternal;
using System.Threading;

namespace DBreeze
{
    public class Scheme : IDisposable
    {
        internal DBreezeEngine Engine = null;

        readonly CachedTableNames cachedTableNames = new CachedTableNames();

        /// <summary>
        /// Flag that closes file of the table if threads don't use it for reading or writing.
        /// </summary>
        internal bool AutoCloseOpenTables = true;

        const string SchemaFileName = "_DBreezeSchema";

        //For System Tables or Records we reserve "@@@@" sequence
        const string LastFileNumberKeyName = "@@@@LastFileNumber";

        TrieSettings LTrieSettings = null;
        IStorage Storage = null;
        LTrie LTrie = null;

        //User files counter
        ulong LastFileNumber = 10000000;

        readonly DbReaderWriterLock _sync_openTablesHolder = new DbReaderWriterLock();
        readonly Dictionary<string, OpenTable> _openTablesHolder =
            new Dictionary<string, OpenTable>(StringComparer.Ordinal);

        const int IdleDiskTableLimit = 8;
        const int IdleDiskTableMilliseconds = 250;

        sealed class IdleDiskTable
        {
            internal OpenTable Table;
            internal long IdleAt;
            internal LinkedListNode<string> OrderNode;
        }

        enum DiskTableCloseState
        {
            Closing,
            Failed
        }

        sealed class DiskTableClose
        {
            internal string TableName;
            internal OpenTable Table;
            internal string Reason;
            internal DiskTableCloseState State;
            internal Exception Failure;
        }

        readonly Dictionary<string, IdleDiskTable> _idleDiskTables =
            new Dictionary<string, IdleDiskTable>(StringComparer.Ordinal);
        readonly LinkedList<string> _idleDiskTableOrder = new LinkedList<string>();
        readonly Dictionary<string, DiskTableClose> _closingDiskTables =
            new Dictionary<string, DiskTableClose>(StringComparer.Ordinal);
        Timer _idleDiskTableTimer;

        int _disposed;
        readonly object _tableUsageChanged = new object();
        long _tableUsageVersion;

        public Scheme(DBreezeEngine DBreezeEngine)
        {
            Engine = DBreezeEngine;

            this.OpenSchema();
            _idleDiskTableTimer = new Timer(SweepIdleDiskTables, null,
                Timeout.Infinite, Timeout.Infinite);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            Timer idleTimer = Interlocked.Exchange(ref _idleDiskTableTimer, null);
            SignalTableUsageChanged();

            if (idleTimer != null)
            {
                using (var timerDisposed = new ManualResetEvent(false))
                {
                    if (idleTimer.Dispose(timerDisposed))
                        timerDisposed.WaitOne();
                }
            }

            WaitForClosingDiskTables();

            List<Exception> errors = null;
            List<OpenTable> tablesToDispose = new List<OpenTable>();
            LTrie schemaToDispose = null;
            _sync_openTablesHolder.EnterWriteLock();
            try
            {
                foreach (var row in _openTablesHolder)
                    tablesToDispose.Add(row.Value);

                _openTablesHolder.Clear();
                _idleDiskTables.Clear();
                _idleDiskTableOrder.Clear();
                foreach (DiskTableClose close in _closingDiskTables.Values)
                {
                    if (close.State == DiskTableCloseState.Failed && close.Failure != null)
                        (errors ??= new List<Exception>()).Add(close.Failure);
                }
                _closingDiskTables.Clear();
                schemaToDispose = LTrie;
                LTrie = null;
            }
            finally
            {
                _sync_openTablesHolder.ExitWriteLock();
            }

            foreach (OpenTable table in tablesToDispose)
            {
                try { table.Dispose(); }
                catch (Exception ex) { (errors ??= new List<Exception>()).Add(ex); }
            }

            if (schemaToDispose != null)
            {
                try { schemaToDispose.Dispose(); }
                catch (Exception ex) { (errors ??= new List<Exception>()).Add(ex); }
            }

            if (errors == null)
                return;

            if (errors.Count == 1)
                ExceptionDispatchInfo.Capture(errors[0]).Throw();

            throw new AggregateException("One or more DBreeze schema storages failed to dispose.", errors);
        }

        private static long IdleNow() => Stopwatch.GetTimestamp();

        private static bool IdleExpired(long now, long idleAt)
        {
            return now - idleAt >= (Stopwatch.Frequency * IdleDiskTableMilliseconds) / 1000;
        }

        private void RemoveIdleTracking(string tableName)
        {
            if (!_idleDiskTables.TryGetValue(tableName, out IdleDiskTable idle))
                return;

            _idleDiskTables.Remove(tableName);
            _idleDiskTableOrder.Remove(idle.OrderNode);
        }

        private void ScheduleIdleDiskTableSweep()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            _idleDiskTableTimer?.Change(IdleDiskTableMilliseconds, Timeout.Infinite);
        }

        private void SweepIdleDiskTables(object state)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            List<DiskTableClose> tablesToDispose = null;
            bool scheduleAgain = false;
            long now = IdleNow();

            _sync_openTablesHolder.EnterWriteLock();
            try
            {
                LinkedListNode<string> node = _idleDiskTableOrder.First;
                while (node != null)
                {
                    LinkedListNode<string> next = node.Next;
                    string tableName = node.Value;
                    if (!_idleDiskTables.TryGetValue(tableName, out IdleDiskTable idle))
                    {
                        _idleDiskTableOrder.Remove(node);
                        node = next;
                        continue;
                    }

                    if (idle.Table.UsageCount != 0)
                    {
                        RemoveIdleTracking(tableName);
                        node = next;
                        continue;
                    }

                    if (!IdleExpired(now, idle.IdleAt))
                    {
                        scheduleAgain = true;
                        node = next;
                        continue;
                    }

                    RemoveIdleTracking(tableName);
                    if (_openTablesHolder.TryGetValue(tableName, out OpenTable current) &&
                        ReferenceEquals(current, idle.Table) && current.UsageCount == 0)
                    {
                        BeginDiskTableClose(tableName, current, "timer", ref tablesToDispose);
                    }
                    node = next;
                }
            }
            finally
            {
                _sync_openTablesHolder.ExitWriteLock();
            }

            if (tablesToDispose != null)
                CloseDiskTables(tablesToDispose, false);

            if (scheduleAgain && Volatile.Read(ref _disposed) == 0)
                _idleDiskTableTimer?.Change(IdleDiskTableMilliseconds, Timeout.Infinite);
        }

        private void SignalTableUsageChanged()
        {
            Interlocked.Increment(ref _tableUsageVersion);
            lock (_tableUsageChanged)
                Monitor.PulseAll(_tableUsageChanged);
        }

        private void WaitForTableUsageChange(long observedVersion)
        {
            lock (_tableUsageChanged)
            {
                if (Volatile.Read(ref _tableUsageVersion) == observedVersion)
                    Monitor.Wait(_tableUsageChanged);
            }
        }

        private void WaitForClosingDiskTables()
        {
            for (;;)
            {
                long observedVersion = Volatile.Read(ref _tableUsageVersion);
                bool hasClosing = false;

                _sync_openTablesHolder.EnterReadLock();
                try
                {
                    foreach (DiskTableClose close in _closingDiskTables.Values)
                    {
                        if (close.State == DiskTableCloseState.Closing)
                        {
                            hasClosing = true;
                            break;
                        }
                    }
                }
                finally
                {
                    _sync_openTablesHolder.ExitReadLock();
                }

                if (!hasClosing)
                    return;

                WaitForTableUsageChange(observedVersion);
            }
        }

        private enum OpenTableLookup
        {
            Missing,
            Acquired,
            Closing,
            Failed
        }

        private sealed class RetryDiskTableCloseException : Exception
        {
        }

        private OpenTableLookup TryAcquireOpenTable(string tableName, out OpenTable table,
            out Exception failure)
        {
            failure = null;
            if (!_openTablesHolder.TryGetValue(tableName, out table))
                return OpenTableLookup.Missing;

            if (_closingDiskTables.TryGetValue(tableName, out DiskTableClose close) &&
                ReferenceEquals(close.Table, table))
            {
                if (close.State == DiskTableCloseState.Failed)
                {
                    failure = close.Failure;
                    return OpenTableLookup.Failed;
                }

                return OpenTableLookup.Closing;
            }

            table.Add();
            return OpenTableLookup.Acquired;
        }

        private void BeginDiskTableClose(string tableName, OpenTable table, string reason,
            ref List<DiskTableClose> tablesToDispose)
        {
            if (_closingDiskTables.ContainsKey(tableName))
                return;

            var close = new DiskTableClose
            {
                TableName = tableName,
                Table = table,
                Reason = reason,
                State = DiskTableCloseState.Closing
            };
            _closingDiskTables.Add(tableName, close);
            (tablesToDispose ??= new List<DiskTableClose>()).Add(close);
        }

        private void CloseDiskTables(List<DiskTableClose> tablesToDispose, bool throwOnFailure)
        {
            List<Exception> failures = null;
            foreach (DiskTableClose close in tablesToDispose)
            {
                Exception failure = null;
                try
                {
                    DurabilityTestHooks.Hit("scheme.idle-close.before-dispose|" + close.Reason + "|" + close.TableName);
                    close.Table.Dispose();
                    DurabilityTestHooks.Hit("scheme.idle-close.after-dispose|" + close.Reason + "|" + close.TableName);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                if (failure != null)
                {
                    Engine.DBisOperableReason = "Scheme.CloseDiskTable: " + close.TableName;
                    Engine.DBisOperable = false;
                }

                _sync_openTablesHolder.EnterWriteLock();
                try
                {
                    if (!_closingDiskTables.TryGetValue(close.TableName, out DiskTableClose currentClose) ||
                        !ReferenceEquals(currentClose, close))
                    {
                        failure ??= new InvalidOperationException(
                            "The disk-table close tombstone disappeared before close completion.");
                    }

                    if (failure == null)
                    {
                        if (_openTablesHolder.TryGetValue(close.TableName, out OpenTable currentTable) &&
                            ReferenceEquals(currentTable, close.Table))
                        {
                            _openTablesHolder.Remove(close.TableName);
                        }
                        _closingDiskTables.Remove(close.TableName);
                    }
                    else
                    {
                        close.Failure = failure;
                        close.State = DiskTableCloseState.Failed;
                    }
                }
                finally
                {
                    _sync_openTablesHolder.ExitWriteLock();
                }

                if (failure != null)
                {
                    Engine.BackgroundNotify("SchemeIdleTableDisposeFailed", failure);
                    (failures ??= new List<Exception>()).Add(failure);
                }

                SignalTableUsageChanged();
            }

            if (!throwOnFailure || failures == null)
                return;

            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();

            throw new AggregateException("One or more idle DBreeze tables failed to close.", failures);
        }

        /*          TODO

         *  1. HERE we will add TableNames as RegEx with settings
         *  2. Checking Reserverd TableNames prefixes
         *  3. User TableName must start from @ut
         *  4. GetPhysicalPathToTheUserTable - File with DIrectory Settings for different tables parser (to make reside different tables in different HDDs or even network drives)
         */

        private void OpenSchema()
        {
            LTrieSettings = new TrieSettings()
            {
                InternalTable = true,
                //SkipStorageBuffer = true
            };

            Storage = new StorageLayer(Path.Combine(Engine.MainFolder, SchemaFileName), LTrieSettings, Engine.Configuration);

            LTrie = new LTrie(Storage);

            LTrie.TableName = "DBreeze.Scheme";

            //Reading lastFileNumber
            ReadUserLastFileNumber();
        }


        private void ReadUserLastFileNumber()
        {
            byte[] btKeyName = Encoding.UTF8.GetBytes(LastFileNumberKeyName);
            LTrieRow row = LTrie.GetKey(btKeyName, false, false);

            if (row.Exists)
            {
                byte[] fullValue = row.GetFullValue(true);
                LastFileNumber = fullValue.To_UInt64_BigEndian();
            }
        }

        private static ulong ReadTableFileNumber(byte[] schemaValue)
        {
            if (schemaValue == null || schemaValue.Length < 10)
                throw new InvalidDataException("The DBreeze schema record is truncated.");

            ushort protocol = BinaryPrimitives.ReadUInt16BigEndian(schemaValue.AsSpan(0, 2));
            if (protocol != 1)
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.SCHEME_FILE_PROTOCOL_IS_UNKNOWN);

            return BinaryPrimitives.ReadUInt64BigEndian(schemaValue.AsSpan(2, 8));
        }

        private static byte[] CreateTableSchemaValue(ulong fileNumber)
        {
            byte[] value = new byte[10];
            BinaryPrimitives.WriteUInt16BigEndian(value.AsSpan(0, 2), 1);
            BinaryPrimitives.WriteUInt64BigEndian(value.AsSpan(2, 8), fileNumber);
            return value;
        }

        /// <summary>
        /// ONLY FOR INTERNAL NEEDS, lock must be handeled by outer procedure.
        /// Users must use GetTablePathFromTableName.
        /// Transactions Journal after start will try to delete RollbackFiles of the finished transactions.
        /// For this it needs to know exact pathes.
        /// For now all tables stored in one folder. Later we will have extra config file which lets to reside
        /// some of tables in the other folders.
        /// This function is an access globalizer to physical file locations by userTableName.
        /// !!!!TRAnJRNL, WHEN RESTORES ROLLBACK, MUST REFER TO Scheme trie settings in the future, FOR NOW DEFAULT
        /// </summary>
        /// <param name="userTableName"></param>
        /// <returns></returns>
        internal string GetPhysicalPathToTheUserTable(string userTableName)
        {
            try
            {
                byte[] btTableName = GetUserTableNameAsByte(userTableName);
                ulong fileName = 0;


                //Getting file name
                LTrieRow row = LTrie.GetKey(btTableName, false, false);

                if (row.Exists)
                {
                    byte[] fullValue = row.GetFullValue(true);
                    fileName = ReadTableFileNumber(fullValue);
                }
                else
                    return String.Empty;


                //Getting folder

                //For now returns path inside working folder, later re-make, take into consideration mapping of DB to tother folders.

                string alternativeTableLocation = String.Empty;

                if (CheckAlternativeTableLocationsIntersections(userTableName, out alternativeTableLocation))
                {
                    if (alternativeTableLocation == String.Empty)
                    {
                        //In memory table
                        //return Path.Combine(Engine.MainFolder, fileName.ToString());
                        return "MEMORY";
                    }
                    else
                    {
                        //returning alternative folder + fileName
                        return Path.Combine(alternativeTableLocation, fileName.ToString());
                    }
                }
                else
                {
                    //Standard path (Dbreeze mainFolder + fileName)
                    return Path.Combine(Engine.MainFolder, fileName.ToString());
                }
            }
            //catch (System.Threading.ThreadAbortException ex)
            //{
            //    //We don'T make DBisOperable = false;
            //    throw ex;
            //}
            catch (Exception ex)
            {
                this.Engine.DBisOperable = false;
                this.Engine.DBisOperableReason = "GetPhysicalPathToTheUserTable";
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.GENERAL_EXCEPTION_DB_NOT_OPERABLE, this.Engine.DBisOperableReason, ex);

            }
        }

        /// <summary>
        /// Opens an existing user table only to let its storage restore a committed rollback.
        /// Uses the same storage override decision as the normal table-opening path.
        /// Returns null for removed and in-memory tables.
        /// </summary>
        internal LTrie OpenTableForCommittedRecovery(string userTableName)
        {
            string physicalPath = GetPhysicalPathToTheUserTable(userTableName);
            if (physicalPath == String.Empty || physicalPath == "MEMORY")
                return null;

            TrieSettings settings = new TrieSettings
            {
                RollbackRecovery = RollbackRecoveryIntent.FinalizeJournalCommitted
            };
            string alternativeTableLocation;
            if (CheckAlternativeTableLocationsIntersections(userTableName, out alternativeTableLocation))
            {
                if (alternativeTableLocation == String.Empty)
                    return null;

                settings.StorageWasOverriden = true;
                settings.AlternativeTableStorageType = DBreezeConfiguration.eStorage.DISK;
                settings.AlternativeTableStorageFolder = alternativeTableLocation;
            }

            IStorage storage = new StorageLayer(physicalPath, settings, Engine.Configuration);
            LTrie trie = new LTrie(storage);
            trie.TableName = userTableName;
            return trie;
        }


        /// <summary>
        /// Returns physical path to the table file, if table doesn't exists in the Scheme returns String.Empty
        /// </summary>
        /// <param name="userTableName"></param>
        /// <returns></returns>
        public string GetTablePathFromTableName(string userTableName)
        {
            //For user usage

            _sync_openTablesHolder.EnterReadLock();
            try
            {
                byte[] btTableName = GetUserTableNameAsByte(userTableName);

                LTrieRow row = LTrie.GetKey(btTableName, true, false);

                if (!row.Exists)
                {
                    return String.Empty;
                }

                byte[] fullValue = row.GetFullValue(true);
                ulong fileName = ReadTableFileNumber(fullValue);

                string alternativeTableLocation = String.Empty;

                if (CheckAlternativeTableLocationsIntersections(userTableName, out alternativeTableLocation))
                {
                    if (alternativeTableLocation == String.Empty)
                        return "MEMORY";
                    else
                        return Path.Combine(alternativeTableLocation, fileName.ToString());
                }
                else
                {
                    return Path.Combine(Engine.MainFolder, fileName.ToString());
                }
            }
            finally
            {
                _sync_openTablesHolder.ExitReadLock();
            }

        }



        /// <summary>
        /// Adds static prefix to all user table names, to
        /// make selection of tables for different purposes easier with StartsWith function
        /// </summary>
        /// <param name="tableName"></param>
        /// <returns></returns>
        private byte[] GetUserTableNameAsByte(string tableName)
        {
            tableName ??= String.Empty;
            byte[] result = new byte[3 + Encoding.UTF8.GetByteCount(tableName)];
            result[0] = (byte)'@';
            result[1] = (byte)'u';
            result[2] = (byte)'t';
            Encoding.UTF8.GetBytes(tableName.AsSpan(), result.AsSpan(3));
            return result;
        }

        private string GetUserTableNameAsString(string tableName)
        {
            return "@ut" + tableName;
        }

        /// <summary>
        /// Returns table for READ, WRITE FUNC
        /// </summary>
        /// <param name="userTableName"></param>
        /// <returns></returns>
        internal LTrie GetTable(string userTableName)
        {
            for (;;)
            {
                if (Volatile.Read(ref _disposed) != 0)
                    throw new ObjectDisposedException(nameof(Scheme));

                long observedVersion = Volatile.Read(ref _tableUsageVersion);
                try
                {
                    return GetTableOnce(userTableName);
                }
                catch (RetryDiskTableCloseException)
                {
                    WaitForTableUsageChange(observedVersion);
                }
            }
        }

        private LTrie GetTableOnce(string userTableName)
        {
            string tableName = GetUserTableNameAsString(userTableName);

            //TODO pattern based mapping If table doesn't exist we create it with properties which could be supplied after db init as regex theme.



            //Schema protocol: 2 bytes - protocol version, other data
            //For protocol 1: first 8 bytes will be TheFileName, starting from db10000-dbN (0-N ulong). up to 10000 are reserved for dbreeze.

            //Table names are UTF-8 based, no limits

            ulong fileName = 0;
            OpenTable otl = null;

            // The overwhelmingly common path must allow concurrent readers. ReaderWriterLockSlim
            // permits only one upgradeable reader, which previously serialized every table lookup.
            _sync_openTablesHolder.EnterReadLock();
            try
            {
                OpenTableLookup lookup = TryAcquireOpenTable(tableName, out otl, out Exception closeFailure);
                if (lookup == OpenTableLookup.Acquired)
                    return otl.Trie;
                if (lookup == OpenTableLookup.Closing)
                    throw new RetryDiskTableCloseException();
                if (lookup == OpenTableLookup.Failed)
                {
                    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DB_IS_NOT_OPERABLE,
                        "Closing table " + tableName + " failed.", closeFailure);
                }
            }
            finally
            {
                _sync_openTablesHolder.ExitReadLock();
            }

            // Keep the legacy slow path for creation, including its write-lock recheck.
            _sync_openTablesHolder.EnterUpgradeableReadLock();
            try
            {

                OpenTableLookup lookup = TryAcquireOpenTable(tableName, out otl, out Exception closeFailure);
                if (lookup == OpenTableLookup.Acquired)
                    return otl.Trie;
                if (lookup == OpenTableLookup.Closing)
                    throw new RetryDiskTableCloseException();
                if (lookup == OpenTableLookup.Failed)
                {
                    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DB_IS_NOT_OPERABLE,
                        "Closing table " + tableName + " failed.", closeFailure);
                }


                //Probably table Exists in db but not in openTablesHolder

                _sync_openTablesHolder.EnterWriteLock();
                try
                {
                    //UpgradeableRead recheck
                    lookup = TryAcquireOpenTable(tableName, out otl, out closeFailure);
                    if (lookup == OpenTableLookup.Acquired)
                        return otl.Trie;
                    if (lookup == OpenTableLookup.Closing)
                        throw new RetryDiskTableCloseException();
                    if (lookup == OpenTableLookup.Failed)
                    {
                        throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DB_IS_NOT_OPERABLE,
                            "Closing table " + tableName + " failed.", closeFailure);
                    }



                    byte[] btTableName = GetUserTableNameAsByte(userTableName);

                    //Trying to get fileName from cache
                    fileName = this.cachedTableNames.GetFileName(tableName);
                    // LTrieRow row = null;
                    bool tableExists = false;

                    if (fileName == 0)
                    {
                        LTrieRow row = LTrie.GetKey(btTableName, false, false);


                        if (row.Exists)
                        {
                            tableExists = true;

                            byte[] fullValue = row.GetFullValue(false);
                            fileName = ReadTableFileNumber(fullValue);
                        }
                        else
                        {
                            tableExists = false;
                            //Creating new table.

                            //Checking table name validity

                            //this will throw exception, if not valid
                            DbUserTables.UserTableNameIsOk(userTableName);


                            //Creating such table and renewing LastFileNumber counter

                            //Adding to LastFileNumber
                            LastFileNumber++;


                            ////Deleting physical files related to the table, if they existed - normally they should not
                            //DeleteAllReleatedTableFiles(Path.Combine(Engine.MainFolder, LastFileNumber.ToString()));

                            byte[] lft = LastFileNumber.To_8_bytes_array_BigEndian();

                            //Writing this number to Schema file
                            LTrie.Add(Encoding.UTF8.GetBytes(LastFileNumberKeyName), lft);

                            //Creating table self and writing to Schema file

                            LTrie.Add(btTableName, CreateTableSchemaValue(LastFileNumber));

                            //Committing both records
                            LTrie.Commit();

                            fileName = LastFileNumber;

                            this.cachedTableNames.Add(tableName, fileName);
                        }
                    }
                    else
                        tableExists = true;

                    //Creating LTrie, adding it to _openTablesHolder

                    //Seeting up Trie TableName, OTHER SETTINGS

                    TrieSettings ts = new TrieSettings();
                    IStorage storage = null;


                    ////Checking if default Flusg Disk behaviour was overriden
                    //ts.DiskFlushBehaviour = Engine.Configuration.DiskFlushBehaviour;
                    ////Checking if we have alternative DiskFlush behaviour
                    //foreach (var pattern in Engine.Configuration.AlternativeDiskFlushBehaviour)
                    //{
                    //    //pattern.Key
                    //    if (DbUserTables.PatternsIntersect(pattern.Key, userTableName))
                    //    {

                    //        ts.DiskFlushBehaviour = pattern.Value;
                    //        break;
                    //    }
                    //}

                    string alternativeTableLocation = String.Empty;

                    if (CheckAlternativeTableLocationsIntersections(userTableName, out alternativeTableLocation))
                    {
                        ts.StorageWasOverriden = true;

                        if (alternativeTableLocation == String.Empty)
                        {
                            ts.AlternativeTableStorageType = DBreezeConfiguration.eStorage.MEMORY;

                            storage = new StorageLayer(Path.Combine(Engine.MainFolder, fileName.ToString()), ts, Engine.Configuration);
                        }
                        else
                        {
                            ts.AlternativeTableStorageType = DBreezeConfiguration.eStorage.DISK;
                            ts.AlternativeTableStorageFolder = alternativeTableLocation;

                            DirectoryInfo diAlt = new DirectoryInfo(alternativeTableLocation);
                            if (!diAlt.Exists)
                                diAlt.Create();

                            if (!tableExists)
                            {
                                //Deleting physical files related to the table, if they existed - normally they should not
                                DeleteAllReleatedTableFiles(Path.Combine(ts.AlternativeTableStorageFolder, LastFileNumber.ToString()));
                            }

                            storage = new StorageLayer(Path.Combine(ts.AlternativeTableStorageFolder, fileName.ToString()), ts, Engine.Configuration);
                        }
                    }
                    else
                    {
                        if (!tableExists)
                        {
                            //Deleting physical files related to the table, if they existed - normally they should not
                            DeleteAllReleatedTableFiles(Path.Combine(Engine.MainFolder, LastFileNumber.ToString()));
                        }

                        storage = new StorageLayer(Path.Combine(Engine.MainFolder, fileName.ToString()), ts, Engine.Configuration);
                    }

                    //storage = new StorageLayer(Path.Combine(Engine.MainFolder, fileName.ToString()), ts, Engine.Configuration);

                    LTrie trie = new LTrie(storage);

                    //Setting LTrie user table name
                    trie.TableName = userTableName;

                    //_openTablesHolder.Add(tableName, trie);

                    //Automatically increased usage in OpenTable constructor
                    _openTablesHolder.Add(tableName, new OpenTable(trie));

                    return trie;
                }
                finally
                {
                    _sync_openTablesHolder.ExitWriteLock();
                }
            }
            catch (RetryDiskTableCloseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.SCHEME_GET_TABLE_WRITE_FAILED, tableName, ex);
            }
            finally
            {
                _sync_openTablesHolder.ExitUpgradeableReadLock();
            }
        }

        /// <summary>
        /// Checks if in configuration was supplied alternative path for table location.
        /// Returns true if intersection was found.
        /// Alternative Path equals to String.Empty - locate in Memory
        /// </summary>
        /// <param name="userTableName"></param>
        /// <param name="alternativePath"></param>
        /// <returns></returns>
        internal bool CheckAlternativeTableLocationsIntersections(string userTableName, out string alternativePath)
        {
            alternativePath = String.Empty;

            foreach (var pattern in Engine.Configuration.AlternativeTablesLocations)
            {
                //pattern.Key
                if (DbUserTables.PatternsIntersect(pattern.Key, userTableName))
                {
                    alternativePath = pattern.Value;
                    return true;
                }
            }

            return false;
        }


        /// <summary>
        /// Called by Transaction, when it's time to be Disposed and close tables.
        /// Tables will be closed only in case of other threads don't use it.
        /// </summary>
        /// <param name="closeOpenTables"></param>
        private void KeepIdleDiskTable(string tableName, OpenTable table,
            ref List<DiskTableClose> tablesToDispose)
        {
            long now = IdleNow();
            if (_idleDiskTables.TryGetValue(tableName, out IdleDiskTable existing))
            {
                existing.Table = table;
                existing.IdleAt = now;
                _idleDiskTableOrder.Remove(existing.OrderNode);
                existing.OrderNode = _idleDiskTableOrder.AddLast(tableName);
            }
            else
            {
                _idleDiskTables.Add(tableName, new IdleDiskTable
                {
                    Table = table,
                    IdleAt = now,
                    OrderNode = _idleDiskTableOrder.AddLast(tableName)
                });
            }

            while (_idleDiskTables.Count > IdleDiskTableLimit)
            {
                string oldestName = _idleDiskTableOrder.First.Value;
                IdleDiskTable oldest = _idleDiskTables[oldestName];
                RemoveIdleTracking(oldestName);

                // A table can have been reacquired through the lock-free read path before its
                // stale idle entry was swept. Active tables are never evicted.
                if (oldest.Table.UsageCount != 0)
                    continue;

                if (_openTablesHolder.TryGetValue(oldestName, out OpenTable current) &&
                    ReferenceEquals(current, oldest.Table) && current.UsageCount == 0)
                {
                    BeginDiskTableClose(oldestName, current, "limit", ref tablesToDispose);
                }
            }
        }

        internal void CloseTables(Dictionary<string, ulong?> closeOpenTables)
        {
            //if (Engine.Configuration.Storage == DBreezeConfiguration.eStorage.MEMORY)
            //    return;

            string tableName = String.Empty;
            OpenTable ot = null;
            bool toClose = false;
            bool hasIdleTables = false;
            List<DiskTableClose> tablesToDispose = null;

            string alternativeTableLocation = String.Empty;

            _sync_openTablesHolder.EnterWriteLock();
            try
            {
                //utn - user table name
                foreach (var utn in closeOpenTables)
                {

                    if (CheckAlternativeTableLocationsIntersections(utn.Key, out alternativeTableLocation))
                    {
                        if (alternativeTableLocation == String.Empty)
                        {
                            //Memory table, we don't close
                            continue;
                        }
                        else
                        {
                            //Physical table...going on
                        }
                    }
                    else
                    {
                        //Table location is not overridden, working further based on main DBreeze configuration
                        if (Engine.Configuration.Storage == DBreezeConfiguration.eStorage.MEMORY)
                            continue;   //we don't close memory tables
                    }

                    tableName = GetUserTableNameAsString(utn.Key);

                    _openTablesHolder.TryGetValue(tableName, out ot);

                    if (ot != null)
                    {
                        toClose = ot.Remove((ulong)utn.Value);

                        if (AutoCloseOpenTables)    //If AutoCloseIsEnabled, we dispose LTrie and closing physical file.
                        {
                            if (toClose)
                            {
                                // Keep a small disk-only working set for the next transaction.
                                // Ownership is already released (UsageCount == 0); the timer only
                                // retains physical handles and never an active transaction table.
                                KeepIdleDiskTable(tableName, ot, ref tablesToDispose);
                                hasIdleTables = true;
                            }
                        }
                    }
                    //else
                    //{
                    //}
                }


            }
            finally
            {
                _sync_openTablesHolder.ExitWriteLock();
            }

            try
            {
                if (tablesToDispose != null)
                    CloseDiskTables(tablesToDispose, true);
            }
            finally
            {
                if (hasIdleTables)
                    ScheduleIdleDiskTableSweep();

                SignalTableUsageChanged();
            }
        }


        /// <summary>
        /// Used by GetTableFor Write, if table is newly created and we have such file name
        /// </summary>
        /// <param name="fullTableFilePath"></param>
        private void DeleteAllReleatedTableFiles(string fullTableFilePath)
        {
            //This call can be done only for physical files, it's controlled on the upper level

            //if (this.Engine.Configuration.Storage == DBreezeConfiguration.eStorage.MEMORY)
            //    return;

            try
            {
                //Deleting DB File
                if (File.Exists(fullTableFilePath))
                    File.Delete(fullTableFilePath);

                //Deleting Rollback File
                if (File.Exists(fullTableFilePath + ".rol"))
                    File.Delete(fullTableFilePath + ".rol");

                //Deleting Rollback Help File
                if (File.Exists(fullTableFilePath + ".rhp"))
                    File.Delete(fullTableFilePath + ".rhp");

                /* Handling backup*/
                if (this.Engine.Configuration.Backup.IsActive)
                {
                    string exactFileName = Path.GetFileName(fullTableFilePath);
                    ulong ulFileName = this.Engine.Configuration.Backup.BackupFNP.ParseFilename(exactFileName);
                    long backup_filePosition = 0;
                    byte[] data = null;
                    this.Engine.Configuration.Backup.WriteBackupElement(ulFileName, 5, backup_filePosition, data);
                }
                /*****************/

            }
            catch
            {
                throw;
            }
        }

        /// <summary>
        /// Returns if user table exists
        /// </summary>
        /// <param name="userTableName"></param>
        /// <returns></returns>
        public bool IfUserTableExists(string userTableName)
        {
            string tableName = GetUserTableNameAsString(userTableName);

            _sync_openTablesHolder.EnterReadLock();
            try
            {
                if (_openTablesHolder.ContainsKey(tableName))
                    return true;

                ////Searching on the disk
                byte[] btTableName = this.GetUserTableNameAsByte(userTableName);
                var row = LTrie.GetKey(btTableName, true, true);
                return row.Exists;
            }
            finally
            {
                _sync_openTablesHolder.ExitReadLock();
            }

            ////First trying to acquire memory storage, without lock but inside of "ignoring" try-catch, if answer is failed refer to the disk

            //_sync_openTablesHolder.EnterReadLock();
            //try
            //{
            //    if (_openTablesHolder.ContainsKey(tableName))
            //        return true;

            //}
            //finally
            //{
            //    _sync_openTablesHolder.ExitReadLock();
            //}



            //////Searching on the disk
            //byte[] btTableName = this.GetUserTableNameAsByte(userTableName);
            //var row = LTrie.GetKey(btTableName, true);
            //return row.Exists;
        }

        /// <summary>
        /// Returns List of user tables starting from specified mask.
        /// If mask is String.Empty returns all user tables
        /// </summary>
        /// <param name="mask"></param>
        /// <returns></returns>
        public List<string> GetUserTableNamesStartingWith(string mask)
        {
            List<string> ret = new List<string>();

            //No lock here, while IterateForwardStartsWith of the LTrie is safe (new root is created), and we don't acquire value from the key (which could be delete).
            //_sync_openTablesHolder.EnterReadLock();
            //try
            //{
            byte[] btKeyName = Encoding.UTF8.GetBytes("@ut" + mask);

            foreach (var row in LTrie.IterateForwardStartsWith(btKeyName, true, false))
            {
                //try       //try-catch could be necessary in case if we acquire value, which was deleted by other thread. Here we don't acquire value.
                //{
                ret.Add(System.Text.Encoding.UTF8.GetString(row.Key).Substring(3));
                //}
                //catch
                //{}

            }
            //}
            //finally
            //{
            //    _sync_openTablesHolder.ExitReadLock();
            //}

            return ret;
        }


        /// <summary>
        /// Deletes user table
        /// </summary>
        /// <param name="userTableName"></param>
        public void DeleteTable(string userTableName)
        {
            for (;;)
            {
                if (Volatile.Read(ref _disposed) != 0)
                    return;

                long observedVersion = Volatile.Read(ref _tableUsageVersion);
                if (DeleteTableInternal(userTableName))
                    return;

                WaitForTableUsageChange(observedVersion);
            }
        }

        private bool DeleteTableInternal(string userTableName)
        {
            string tableName = GetUserTableNameAsString(userTableName);
            bool completed = false;
            _sync_openTablesHolder.EnterWriteLock();
            try
            {
                if (_closingDiskTables.TryGetValue(tableName, out DiskTableClose close))
                {
                    if (close.State == DiskTableCloseState.Failed)
                    {
                        throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DB_IS_NOT_OPERABLE,
                            "Closing table " + tableName + " failed.", close.Failure);
                    }

                    return false;
                }

                cachedTableNames.Remove(tableName);

                if (_openTablesHolder.TryGetValue(tableName, out OpenTable openTable))
                {
                    RemoveIdleTracking(tableName);
                    openTable.Dispose();
                    _openTablesHolder.Remove(tableName);
                }

                string physicalDbFileName = GetPhysicalPathToTheUserTable(userTableName);
                if (physicalDbFileName != String.Empty)
                {
                    byte[] btTableName = GetUserTableNameAsByte(userTableName);
                    LTrie.Remove(ref btTableName);
                    LTrie.Commit();

                    if (physicalDbFileName != "MEMORY")
                        DeleteAllReleatedTableFiles(physicalDbFileName);
                }

                completed = true;
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(
                    DBreezeException.eDBreezeExceptions.SCHEME_TABLE_DELETE_FAILED,
                    userTableName,
                    ex);
            }
            finally
            {
                _sync_openTablesHolder.ExitWriteLock();
            }

            if (completed)
                SignalTableUsageChanged();
            return completed;
        }


        /// <summary>
        /// Renames user table, if it existed.
        /// <para>Safe, will make operation only when other threads stop to work with the oldTable</para>
        /// </summary>
        /// <param name="oldUserTableName"></param>
        /// <param name="newUserTableName"></param>
        public void RenameTable(string oldUserTableName, string newUserTableName)
        {
            if (String.Equals(oldUserTableName, newUserTableName, StringComparison.Ordinal))
                return;

            DbUserTables.UserTableNameIsOk(oldUserTableName);
            DbUserTables.UserTableNameIsOk(newUserTableName);

            for (; ; )
            {
                if (Volatile.Read(ref _disposed) != 0)
                    return;

                long observedVersion = Volatile.Read(ref _tableUsageVersion);
                if (RenameTableInternal(oldUserTableName, newUserTableName))
                    return;

                WaitForTableUsageChange(observedVersion);
            }
        }

        /// <summary>
        /// Renames user table, if it existed.
        /// <para>If there are threads which are working with this table, rename will not be finished and will return false</para>
        /// </summary>
        /// <param name="oldUserTableName"></param>
        /// <param name="newUserTableName"></param>
        /// <returns>true if successfully renamed, otherwise false</returns>
        private bool RenameTableInternal(string oldUserTableName, string newUserTableName)
        {
            _sync_openTablesHolder.EnterWriteLock();
            try
            {
                string oldTableName = GetUserTableNameAsString(oldUserTableName);
                string newTableName = GetUserTableNameAsString(newUserTableName);
                byte[] btOldTableName = GetUserTableNameAsByte(oldUserTableName);
                byte[] btNewTableName = GetUserTableNameAsByte(newUserTableName);

                if (_closingDiskTables.TryGetValue(oldTableName, out DiskTableClose sourceClose))
                {
                    if (sourceClose.State == DiskTableCloseState.Failed)
                    {
                        throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DB_IS_NOT_OPERABLE,
                            "Closing table " + oldTableName + " failed.", sourceClose.Failure);
                    }
                    return false;
                }

                if (_closingDiskTables.TryGetValue(newTableName, out DiskTableClose destinationClose))
                {
                    if (destinationClose.State == DiskTableCloseState.Failed)
                    {
                        throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DB_IS_NOT_OPERABLE,
                            "Closing table " + newTableName + " failed.", destinationClose.Failure);
                    }
                    return false;
                }

                LTrieRow sourceRow = LTrie.GetKey(btOldTableName, false, false);
                if (!sourceRow.Exists)
                    return true;

                StorageRoute oldRoute = ResolveStorageRoute(oldUserTableName);
                StorageRoute newRoute = ResolveStorageRoute(newUserTableName);
                if (!oldRoute.Equals(newRoute))
                {
                    throw new InvalidOperationException(
                        "Renaming a table across different storage locations is not supported.");
                }

                _openTablesHolder.TryGetValue(oldTableName, out OpenTable sourceOpenTable);
                bool inMemory = oldRoute.Storage == DBreezeConfiguration.eStorage.MEMORY;
                if (!inMemory && sourceOpenTable != null)
                {
                    if (sourceOpenTable.UsageCount != 0)
                        return false;

                    RemoveIdleTracking(oldTableName);
                    _openTablesHolder.Remove(oldTableName);
                    sourceOpenTable.Dispose();
                    sourceOpenTable = null;
                }

                ulong sourceFileNumber = ReadTableFileNumber(sourceRow.GetFullValue(false));

                LTrieRow destinationRow = LTrie.GetKey(btNewTableName, false, false);
                string destinationPhysicalPath = String.Empty;
                if (destinationRow.Exists)
                {
                    ulong destinationFileNumber = ReadTableFileNumber(destinationRow.GetFullValue(false));
                    destinationPhysicalPath = GetPhysicalPath(newRoute, destinationFileNumber);
                }

                if (_openTablesHolder.TryGetValue(newTableName, out OpenTable destinationOpenTable))
                {
                    RemoveIdleTracking(newTableName);
                    destinationOpenTable.Dispose();
                    _openTablesHolder.Remove(newTableName);
                }

                LTrie.ChangeKey(ref btOldTableName, ref btNewTableName);
                LTrie.Commit();

                cachedTableNames.Remove(oldTableName);
                cachedTableNames.Remove(newTableName);
                cachedTableNames.Add(newTableName, sourceFileNumber);

                if (inMemory && sourceOpenTable != null)
                {
                    RemoveIdleTracking(oldTableName);
                    _openTablesHolder.Remove(oldTableName);
                    sourceOpenTable.Trie.TableName = newUserTableName;
                    _openTablesHolder.Add(newTableName, sourceOpenTable);
                }

                if (destinationPhysicalPath.Length != 0 && destinationPhysicalPath != "MEMORY")
                    DeleteAllReleatedTableFiles(destinationPhysicalPath);
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(
                    DBreezeException.eDBreezeExceptions.SCHEME_TABLE_RENAME_FAILED,
                    oldUserTableName,
                    ex);
            }
            finally
            {
                _sync_openTablesHolder.ExitWriteLock();
            }

            return true;
        }

        private StorageRoute ResolveStorageRoute(string userTableName)
        {
            if (CheckAlternativeTableLocationsIntersections(userTableName, out string alternativePath))
            {
                return alternativePath.Length == 0
                    ? new StorageRoute(DBreezeConfiguration.eStorage.MEMORY, String.Empty)
                    : new StorageRoute(DBreezeConfiguration.eStorage.DISK, NormalizeDirectory(alternativePath));
            }

            return Engine.Configuration.Storage == DBreezeConfiguration.eStorage.DISK
                ? new StorageRoute(DBreezeConfiguration.eStorage.DISK, NormalizeDirectory(Engine.MainFolder))
                : new StorageRoute(Engine.Configuration.Storage, String.Empty);
        }

        private static string NormalizeDirectory(string path)
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }

        private static string GetPhysicalPath(StorageRoute route, ulong fileNumber)
        {
            if (route.Storage == DBreezeConfiguration.eStorage.MEMORY)
                return "MEMORY";

            return route.Storage == DBreezeConfiguration.eStorage.DISK
                ? Path.Combine(route.Directory, fileNumber.ToString())
                : String.Empty;
        }

        private readonly struct StorageRoute : IEquatable<StorageRoute>
        {
            internal readonly DBreezeConfiguration.eStorage Storage;
            internal readonly string Directory;

            internal StorageRoute(DBreezeConfiguration.eStorage storage, string directory)
            {
                Storage = storage;
                Directory = directory;
            }

            public bool Equals(StorageRoute other)
            {
                if (Storage != other.Storage)
                    return false;

                StringComparison comparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                return String.Equals(Directory, other.Directory, comparison);
            }

            public override bool Equals(object obj) => obj is StorageRoute other && Equals(other);

            public override int GetHashCode()
            {
                StringComparer comparer = OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal;
                return HashCode.Combine(Storage, comparer.GetHashCode(Directory));
            }
        }


    }
}
