/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/
using System;
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
        private int inited;
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
            if (Volatile.Read(ref inited) == 0)
            {
                lock (lock_init)
                {
                    if (inited == 0)
                    {
                        this.ConstructFromConfiguration(conf);
                        Volatile.Write(ref inited, 1);
                    }
                }
            }
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
