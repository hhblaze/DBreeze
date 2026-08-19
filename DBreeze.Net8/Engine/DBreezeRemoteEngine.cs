/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/
using System;
using System.Runtime.ExceptionServices;
using System.Threading;

using DBreeze.Transactions;

namespace DBreeze
{
    /// <summary>
    /// 
    /// </summary>
    public class DBreezeRemoteEngine : DBreezeEngine
    {
        private readonly DBreezeConfiguration conf;
        private const int NotInitialized = 0;
        private const int Initialized = 1;
        private const int Faulted = 2;
        private int initializationState = NotInitialized;
        private ExceptionDispatchInfo initializationException;
        private readonly object lock_init = new object();
        

        /// <summary>
        /// DBreezeRemoteEngine instantiator
        /// </summary>
        /// <param name="dbreezeConfiguration"></param>
        public DBreezeRemoteEngine(DBreezeConfiguration dbreezeConfiguration)
        {
            ArgumentNullException.ThrowIfNull(dbreezeConfiguration);

            conf = dbreezeConfiguration;
            this.RemoteEngine = true;
                        
        }

        /// <summary>
        /// 
        /// </summary>
        void Init()
        {
            int state = Volatile.Read(ref initializationState);
            if (state == Faulted)
                ThrowInitializationException();
            if (Disposed)
                throw new ObjectDisposedException(nameof(DBreezeRemoteEngine));
            if (state == Initialized)
                return;

            lock (lock_init)
            {
                state = initializationState;
                if (state == Faulted)
                    ThrowInitializationException();
                if (Disposed)
                    throw new ObjectDisposedException(nameof(DBreezeRemoteEngine));
                if (state == Initialized)
                    return;

                try
                {
                    this.ConstructFromConfiguration(conf);
                    if (Disposed)
                        throw new ObjectDisposedException(nameof(DBreezeRemoteEngine));

                    Volatile.Write(ref initializationState, Initialized);
                }
                catch (Exception ex)
                {
                    initializationException = ExceptionDispatchInfo.Capture(ex);
                    Volatile.Write(ref initializationState, Faulted);
                    throw;
                }
            }
        }

        private void ThrowInitializationException()
        {
            initializationException.Throw();
        }

        internal override void EnsureInitialized()
        {
            Init();
        }


        /// <summary>
        /// Returns transaction object.
        /// </summary>
        /// <returns></returns>
        public new Transaction GetTransaction()
        {
            Init();
            return base.GetTransaction();
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
        public new Transaction GetTransaction(eTransactionTablesLockTypes tablesLockType, params string[] tables)
        {
            Init();
            return base.GetTransaction(tablesLockType, tables);
        }

        /// <summary>
        /// Returns DBreeze schema object
        /// </summary>
        public new Scheme Scheme
        {
            get
            {
                Init();
                return this.DBreezeSchema;
            }
        }
        
    }


}
