/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.IO;
#if !NET35 && !NETr40
using System.Runtime.ExceptionServices;
#endif
using System.Threading;

using DBreeze.Transactions;
using DBreeze.Exceptions;
using DBreeze.TextSearch;

//under DBreeze main namespace we hold Schema and Engine.

namespace DBreeze
{
    /// <summary>
    /// Main DBreeze Database class.
    /// </summary>
    public class DBreezeEngine:IDisposable
    {
        #region "Version Number"
        /// <summary>
        /// DBreeze version number
        /// </summary>        
        //public static string Version = "01.061.20131120";
        //public static string Version = "01.068.20141205";
        //public static string Version = "01.072.20150522"; //Get it from assembly
        #endregion

   
        //later can be swapped on Configuration.DBreezeDataFolderName;
        internal string MainFolder = String.Empty;
        internal Scheme DBreezeSchema = null;
        internal TextDeferredIndexer DeferredIndexer = null;
        internal TransactionsCoordinator _transactionsCoordinator = null;
        /// <summary>
        /// DBreeze resources represents an In-Memory dictionary synchronized with an internal DBreeze table. 
        /// Key is a string, Value any standard DBreeze.DataType (or serialized object by supplied serializer).
        /// Can be called from anywhere, even from other transactions. There is no need to add into sync table
        /// </summary>
        public DBreezeResources Resources = null;
        //internal bool DBisOperable = true;
        /// <summary>
        /// Db is not operable any more by DBisOperableReason reason 
        /// </summary>
        private int _dbIsOperable = 1;
        public bool DBisOperable
        {
            get => Interlocked.CompareExchange(ref _dbIsOperable, 0, 0) == 1;
            internal set => Interlocked.Exchange(ref _dbIsOperable, value ? 1 : 0);
        }
        /// <summary>
        /// Is filled with a text note who brought to DBisOperable = false
        /// </summary>
        private string _dbIsOperableReason = String.Empty;
        public string DBisOperableReason
        {
            get => Interlocked.CompareExchange(ref _dbIsOperableReason, null, null);
            internal set => Interlocked.Exchange(ref _dbIsOperableReason, value ?? String.Empty);
        }
        internal TransactionsJournal _transactionsJournal = null;
        internal TransactionTablesLocker _transactionTablesLocker = null;
        /// <summary>
        /// Whether engine is disposed
        /// </summary>
        public bool Disposed => Interlocked.CompareExchange(ref disposed, 0, 0) == 1;
        private int disposed = 0;

        private static readonly object DataTypesInitializationLock = new object();
        private readonly object _lifecycleLock = new object();

        /// <summary>
        /// Initialized from DBreezeRemoteEngine
        /// </summary>
        internal bool RemoteEngine = false;
        /// <summary>
        /// DBreeze may execute some tasks in the background (like deffered text indexing). 
        /// External delegate can receive notifications about that.
        /// </summary>
        public Action<string, object> BackgroundTasksExternalNotifier = null;
        /// <summary>
        /// Dbreeze Configuration.
        /// For now BackupPlan is included.
        /// Later can be added special settings for each entity defined by string pattern.
        /// </summary>
        internal DBreezeConfiguration Configuration = null;

        /// <summary>
        /// For DbreezeRemoteEngine wrapper
        /// </summary>
        internal DBreezeEngine() { }

        /// <summary>
        /// Dbreeze instantiator
        /// </summary>
        /// <param name="dbreezeConfiguration"></param>
        public DBreezeEngine(DBreezeConfiguration dbreezeConfiguration)
        {
            ConstructFromConfiguration(dbreezeConfiguration);
        }

        /// <summary>
        /// Constructing Dbreeze from dbreezeConfiguration
        /// </summary>
        /// <param name="dbreezeConfiguration"></param>
        internal void ConstructFromConfiguration(DBreezeConfiguration dbreezeConfiguration)
        {
            if (dbreezeConfiguration == null)
                throw new ArgumentNullException(nameof(dbreezeConfiguration));

            lock (_lifecycleLock)
            {
                ThrowIfDisposed();

                Configuration = dbreezeConfiguration;

                // There must be at least a transaction journal and a schema in the backup folder.
                if (Configuration.Backup.IsActive)
                    Configuration.Backup.DBreezeFolderName = Configuration.DBreezeDataFolderName;

                if (Configuration.Storage == DBreezeConfiguration.eStorage.RemoteInstance && !RemoteEngine)
                    throw new InvalidOperationException(
                        "DBreeze.DBreezeEngine: a remote instance must be initialized via DBreezeRemoteEngine.");

                MainFolder = Configuration.DBreezeDataFolderName;
                InitDb();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="DBreezeDataFolderName"></param>
        public DBreezeEngine(string DBreezeDataFolderName)
        {
            if (String.IsNullOrEmpty(DBreezeDataFolderName) || DBreezeDataFolderName.Trim().Length == 0)
                throw new ArgumentException("A database folder must be supplied.", nameof(DBreezeDataFolderName));

            ConstructFromConfiguration(new DBreezeConfiguration
            {
                DBreezeDataFolderName = DBreezeDataFolderName
            });
        }

        /// <summary>
        /// InitDb
        /// </summary>
        private void InitDb()
        {
            try
            {
                // InitDict mutates several process-wide dictionaries and is not internally synchronized.
                lock (DataTypesInitializationLock)
                    DataTypes.DataTypesConvertor.InitDict();

                if (Configuration.Storage == DBreezeConfiguration.eStorage.DISK)
                {
                    try
                    {
                        Directory.CreateDirectory(MainFolder);
                    }
                    catch (Exception ex)
                    {
                        throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.CREATE_DB_FOLDER_FAILED, ex);
                    }
                }

                DBreezeSchema = new Scheme(this);
                _transactionsCoordinator = new TransactionsCoordinator(this);
                _transactionsJournal = new TransactionsJournal(this);
                _transactionTablesLocker = new TransactionTablesLocker();
                DeferredIndexer = new TextDeferredIndexer(this);
                Resources = new DBreezeResources(this);
                DeferredIndexer.StartDefferedIndexing();
            }
            catch (Exception ex)
            {
                DBisOperableReason = "InitDb";
                DBisOperable = false;
                CleanupAfterFailedInitialization();

                if (ex is DBreezeException)
                    throw;

                throw DBreezeException.Throw(
                    DBreezeException.eDBreezeExceptions.GENERAL_EXCEPTION_DB_NOT_OPERABLE,
                    DBisOperableReason,
                    ex);
            }
        }

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref disposed, 1, 0) != 0)
                return;

            DBisOperableReason = "DBreezeEngine.Dispose";
            DBisOperable = false;

            List<Exception> errors = null;
            lock (_lifecycleLock)
            {
                CaptureDisposeException(ref errors, () => DeferredIndexer?.RequestStop());
                CaptureDisposeException(ref errors, () => _transactionsCoordinator?.StopEngine());
                CaptureDisposeException(ref errors, () => DeferredIndexer?.Dispose());
                CaptureDisposeException(ref errors, () => Resources?.Dispose());
                CaptureDisposeException(ref errors, () => DBreezeSchema?.Dispose());
                CaptureDisposeException(ref errors, () => _transactionsJournal?.Dispose());
                CaptureDisposeException(ref errors, () => _transactionTablesLocker?.Dispose());

                // Configuration owns the backup subsystem and must outlive all storages.
                CaptureDisposeException(ref errors, () => Configuration?.Dispose());
            }

            DBisOperableReason = "DBreezeEngine.Dispose";

            if (errors == null)
                return;

#if NET35 || NETr40
            throw errors[0];
#else
            if (errors.Count == 1)
                ExceptionDispatchInfo.Capture(errors[0]).Throw();

            throw new AggregateException("One or more DBreeze components failed to dispose.", errors);
#endif
        }

        private static void CaptureDisposeException(ref List<Exception> errors, Action disposeAction)
        {
            try
            {
                disposeAction();
            }
            catch (Exception ex)
            {
                if (errors == null)
                    errors = new List<Exception>();
                errors.Add(ex);
            }
        }

        private void CleanupAfterFailedInitialization()
        {
            try { DeferredIndexer?.RequestStop(); } catch { }
            try { _transactionsCoordinator?.StopEngine(); } catch { }
            try { DeferredIndexer?.Dispose(); } catch { }
            try { Resources?.Dispose(); } catch { }
            try { DBreezeSchema?.Dispose(); } catch { }
            try { _transactionsJournal?.Dispose(); } catch { }
            try { _transactionTablesLocker?.Dispose(); } catch { }
            try { Configuration?.Dispose(); } catch { }

            DeferredIndexer = null;
            Resources = null;
            DBreezeSchema = null;
            _transactionsCoordinator = null;
            _transactionsJournal = null;
            _transactionTablesLocker = null;
        }

        private void ThrowIfDisposed()
        {
            if (Disposed)
                throw new ObjectDisposedException(nameof(DBreezeEngine));
        }

        internal virtual void EnsureInitialized()
        {
        }


        /// <summary>
        /// Returns transaction object.
        /// </summary>
        /// <returns></returns>
        public Transaction GetTransaction()
        {
            EnsureInitialized();

            if (!DBisOperable)
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DB_IS_NOT_OPERABLE,DBisOperableReason,new Exception());

            //User receives new transaction from the engine
            return this._transactionsCoordinator.GetTransaction(0, eTransactionTablesLockTypes.SHARED);

        }

        /// <summary>
        /// Returns transaction object.
        /// </summary>
        /// <param name="tablesLockType">
        /// <para>SHARED: threads can use listed tables in parallel. Must be used together with tran.SynchronizeTables command, if necessary.</para>
        /// <para>EXCLUSIVE: if other threads use listed tables for reading or writing, current thread will be in a waiting queue.</para>
        /// </param>
        /// <param name="tables"></param>
        /// <returns>Returns transaction object</returns>
        public Transaction GetTransaction(eTransactionTablesLockTypes tablesLockType, params string[] tables)
        {
            EnsureInitialized();

            if (!DBisOperable)
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DB_IS_NOT_OPERABLE, DBisOperableReason, new Exception());

            //User receives new transaction from the engine
            return this._transactionsCoordinator.GetTransaction(1, tablesLockType, tables);

        }

       
        /// <summary>
        /// Returns DBreeze schema object
        /// </summary>
        public Scheme Scheme
        {
            get
            {
                EnsureInitialized();
                return this.DBreezeSchema;
            }
        }

        /// <summary>
        /// Notifier about background events.
        /// </summary>
        /// <param name="noti"></param>
        /// <param name="obj"></param>
        internal void BackgroundNotify(string noti, object obj)
        {
            Action<string, object> notifier = BackgroundTasksExternalNotifier;
            if (notifier == null)
                return;

            var notification = new BackgroundNotification(notifier, noti, obj);
#if NETSTANDARD1_6 && !NETSTANDARD2_0 && !NETSTANDARD2_1
            System.Threading.Tasks.Task.Factory.StartNew(
                BackgroundNotificationCallback,
                notification);
#else
            ThreadPool.QueueUserWorkItem(
                BackgroundNotificationCallback,
                notification);
#endif
        }

        private static void BackgroundNotificationCallback(object state)
        {
            BackgroundNotification notification = (BackgroundNotification)state;
            try
            {
                notification.Notifier(notification.Notification, notification.Payload);
            }
            catch
            {
            }
        }

        private sealed class BackgroundNotification
        {
            internal readonly Action<string, object> Notifier;
            internal readonly string Notification;
            internal readonly object Payload;

            internal BackgroundNotification(Action<string, object> notifier, string notification, object payload)
            {
                Notifier = notifier;
                Notification = notification;
                Payload = payload;
            }
        }

    }//end of class

}
