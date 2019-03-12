/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze.LianaTrie;
using DBreeze.Exceptions;
using DBreeze.DataTypes;
using DBreeze.Tries;
using DBreeze.SchemeInternal;
using DBreeze.Utils;
using DBreeze.TextSearch;
//using System.Threading.Tasks;

namespace DBreeze.Transactions
{
    /// <summary>
    /// Transaction
    /// </summary>
    public class Transaction:IDisposable
    {
        /// <summary>
        /// Managed threadId of the transaction
        /// </summary>
        public int ManagedThreadId = 0;
        internal TransactionUnit _transactionUnit = null;

        bool disposed = false;

        /// <summary>
        /// DateTime.UtcNow.Ticks - time of transaction creation
        /// Combination of ManagedThreadId and CreatedUdt, gives us unique transaction identifier
        /// </summary>
        public long CreatedUdt = DateTime.UtcNow.Ticks;

        /// <summary>
        /// 0 - standard transaction, 1 - locked transaction (Shared Exclusive)
        /// </summary>
        int _transactionType = 0;

        /// <summary>
        /// TextSearchHandler instance
        /// </summary>
        internal TextSearchHandler tsh = null;

        /// <summary>
        /// Speeding up, space economy. Represents a mechanism helping to store entites into the memory, before insert or remove.
        /// When AutomaticFlushLimitQuantityPerTable per table (default 10000) is exceed or 
        /// within Commit command, all entites will be flushed (first removed then inserted) on the disk 
        /// sorted by key ascending
        /// </summary>
        public RandomKeySorter RandomKeySorter = new RandomKeySorter();   

        /// <summary>
        /// Transaction
        /// </summary>
        /// <param name="transactionType"></param>
        /// <param name="transactionUnit"></param>
        /// <param name="lockType"></param>
        /// <param name="tables"></param>
        internal Transaction(int transactionType, TransactionUnit transactionUnit, eTransactionTablesLockTypes lockType, params string[] tables)
        {
            _transactionType = transactionType;
#if NET35 || NETr40
            ManagedThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
#else
            ManagedThreadId = Environment.CurrentManagedThreadId;
            
#endif            
            _transactionUnit = transactionUnit;

            switch(transactionType)
            {
                case 0:
                    break;
                case 1:
                    while (true)
                    {
                        if (_transactionUnit.TransactionsCoordinator.GetSchema.Engine._transactionTablesLocker.AddSession(lockType, tables))
                            break;

                    }
                    break;
            }

            this.RandomKeySorter._t = this;
           
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {            
            if (disposed)
                return;

            disposed = true;

            
            this._transactionUnit.TransactionsCoordinator.UnregisterTransaction(this.ManagedThreadId);
            
            
            //Clearing Write Tables buffer
            transactionWriteTables.Clear();
            //Clearing Read Tables buffer
            transactionReadTables.Clear();


            //foreach (var tb in _openTable)
            //{
            //    Console.WriteLine("Thread: {0}; TN: {1}; CNT: {2}", this.ManagedThreadId, tb.Key,tb.Value);
            //}

            this._transactionUnit.TransactionsCoordinator.GetSchema.CloseTables(_openTable);

            lock (sync_openTable)
            {
                _openTable.Clear();
            }

            //Transaction of type 1 is with Shared and Exclusive locks of the table. Can block even reading threads.
            if (_transactionType == 1)
            {
                _transactionUnit.TransactionsCoordinator.GetSchema.Engine._transactionTablesLocker.RemoveSession();
            }
        }

        private bool _valuesLazyLoadingIsOn = true;
        /// <summary>
        /// When it's on iterators return Row with the key and a pointer to the value.
        /// <para>Value will be read out when we call it Row.Value.</para>
        /// <para>When it's off we read value together with the key in one round</para>
        /// <para>Default is true</para>
        /// </summary>
        public bool ValuesLazyLoadingIsOn
        {
            get { return _valuesLazyLoadingIsOn; }
            set { _valuesLazyLoadingIsOn = value; }
        }


        #region "Reserving tables for write, to avoid Deadlocks"

        bool syncroTablesIsDone = false;

        /// <summary>
        /// Use before any table modification command inside of the transaction.
        /// <para>In case if transaction is going to modify only 1 table, reservation is not necessary, there is no danger of the deadlock.</para>
        /// <para></para>
        /// <para>Table Names available patterns:</para>
        /// <para>$ * #</para>
        /// <para>* - 1 or more of any symbol kind (every symbol after * will be cutted): Items* U Items123/Pictures</para>
        /// <para># - symbols (except slash) followed by slash and minimum another symbol: Items#/Picture U Items123/Picture</para>
        /// <para>$ - 1 or more symbols except slash (every symbol after $ will be cutted): Items$ U Items123;  Items$ !U Items123/Pictures </para>
        /// </summary>
        /// <param name="tablesNamesPatterns">can be either tableName or pattern like Articles#/Items*</param>
        public void SynchronizeTables(IList<string> tablesNamesPatterns)
        {

            this.CheckIfTransactionHasTablesRegisteredForWrite(tablesNamesPatterns);

            List<string> correctedPatterns = new List<string>();

            //Checking every of the supplied patter for consistency and getting changed patterns in case of *
            foreach (var tnp in tablesNamesPatterns)
            {
                correctedPatterns.Add(DbUserTables.UserTablePatternIsOk(tnp));
            }

            this._transactionUnit.TransactionsCoordinator.RegisterWriteTablesForTransaction(this.ManagedThreadId, correctedPatterns,true);
        }

        /// <summary>
        ///  Use before any table modification command inside of transaction
        /// <para>In case if transaction is going to modify only 1 table, reservation is not necessary, there is no danger of the deadlock.</para>
        /// <para></para>
        /// <para>Table Names available patterns:</para>
        /// <para>$ * #</para>
        /// <para>* - 1 or more of any symbol kind (every symbol after * will be cutted): Items* U Items123/Pictures</para>
        /// <para># - symbols (except slash) followed by slash and minimum another symbol: Items#/Picture U Items123/Picture</para>
        /// <para>$ - 1 or more symbols except slash (every symbol after $ will be cutted): Items$ U Items123;  Items$ !U Items123/Pictures </para>
        /// </summary>
        /// <param name="tablesNamesPatterns"></param>
        public void SynchronizeTables(params string[] tablesNamesPatterns)
        {
            this.SynchronizeTables(tablesNamesPatterns.Select(r => r).ToList());
        }

        private void CheckIfTransactionHasTablesRegisteredForWrite(IList<string> tablesNames)
        {
            if (syncroTablesIsDone)
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTION_TABLES_RESERVATION_CANBEDONE_ONCE, new Exception());

            syncroTablesIsDone = false;
            //Reservation can be done only once, before calls of any write function.

            if (tablesNames.Count()<1)
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTION_TABLES_RESERVATION_LIST_MUSTBEFILLED, new Exception());

            if (this._transactionUnit.TransactionWriteTablesCount > 0)
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTION_TABLES_RESERVATION_FAILED, new Exception());
        }

        #endregion


        #region "Get Table Counter"
        //To achieve effect of correct table close We have extra dictionary where we calculate how many times
        //table was exclusively open for read and write using GetTable from Schema
        Dictionary<string, ulong?> _openTable = new Dictionary<string, ulong?>();
        object sync_openTable = new object();

        /// <summary>
        /// This must be called only if we use not cached opening
        /// </summary>
        private void AddOpenTable(string tableName)
        {
            lock (sync_openTable)
            {
                ulong? cnt = null;
                _openTable.TryGetValue(tableName, out cnt);

                if (cnt == null)
                {
                    _openTable.Add(tableName, 1);
                    //System.Diagnostics.Debug.WriteLine("OTN " + tableName);
                }
                else
                {
                    _openTable[tableName] = cnt + 1;
                    //System.Diagnostics.Debug.WriteLine("OT " + tableName + "  cnt:" + (cnt + 1));
                }
            }
        }

        #endregion


        #region "Write Tables Cache, Performance Booster For Bulk Inserts, Updates and Removes"
        /// <summary>
        /// Small buffer for the tables where we are going to write in.
        /// It will boost performance in case of Bulk inserts or updates
        /// </summary>
        Dictionary<string, LTrie> transactionWriteTables = new Dictionary<string, LTrie>();

        /// <summary>
        /// Automatically tries to take Write Table from buffer or from system, throws exception if smth. happens
        /// </summary>
        /// <param name="tableName"></param>
        /// <returns></returns>
        internal LTrie GetWriteTableFromBuffer(string tableName)
        {
            LTrie table = null;

            transactionWriteTables.TryGetValue(tableName, out table);

            if (table == null)
            {
                table = this._transactionUnit.TransactionsCoordinator.GetTable_WRITE(tableName, this.ManagedThreadId);
                
                if (table == null)
                    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DB_IS_NOT_OPERABLE, this._transactionUnit.TransactionsCoordinator.GetSchema.Engine.DBisOperableReason,new Exception());

                //Adding to Open Table Counter
                AddOpenTable(tableName);

                transactionWriteTables.Add(tableName, table);

            }

            return table;
        }
        #endregion

        #region "Read Tables Cache, Performance Booster"

        /// <summary>
        /// Small buffer for the tables, we are going to read from.
        /// </summary>
        Dictionary<string, Rtbe> transactionReadTables = new Dictionary<string, Rtbe>();        

        /// <summary>
        /// Technical class, who holds reference to the table and its last modification dts
        /// ReadTableElement
        /// </summary>
        private class Rtbe
        {
            public LTrie Ltrie { get; set; }
            public long dtTableFixed { get; set; }
            public ITrieRootNode ReadRoot { get; set; }
        }

        /// <summary>
        /// IS USED BY NON-RANGE SELECTS OPERATORS
        /// Retuns LTrie (or later by necessity ITrie) and as out variable ReadRootNode which must be used for READ FUNC data acquiring.
        /// READ FUNCs calling this proc will receive table and as out var. root, this root will be supplied to the trie.
        /// If root is null, then write root will be used.
        /// If table is null - table doesn't exist.
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="root"></param>
        /// <returns></returns>
        private LTrie GetReadTableFromBuffer(string tableName, out ITrieRootNode root)
        {
            return GetReadTableFromBuffer(tableName, out root, false);

            ////Can be changed on interface ITrie by necessity.

            ////In discussion, if we need to compare threadsIds, normally transaction can be used only from one thread, but it will be checked
            ////by GetTable_READ. In case if we take table from cache, may be it's not necessary to check.
            ////if (System.Threading.Thread.CurrentThread.ManagedThreadId != transactionThreadId)
            ////{
            ////    this.UnregisterTransaction(transactionThreadId);

            ////    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTION_CANBEUSED_FROM_ONE_THREAD);
            ////}

            //LTrie table = null;
            //Rtbe rtbe = null;
            //long dtTableFixed = 0;
            //root = null;

            //transactionReadTables.TryGetValue(tableName, out rtbe);

            //if (rtbe == null)
            //{
            //    table = this._transactionUnit.TransactionsCoordinator.GetTable_READ(tableName, this.ManagedThreadId);

            //    if (table == null)
            //        return null;    //returns null (may be table doesn't exist it's ok for READ FUNCs)

            //    //Adding to Open Table Counter
            //    AddOpenTable(tableName);

            //    //Checking READ_SYNCHRO
            //    if (this._transactionUnit.If_TableIsReservedForWrite(tableName))
            //    {
            //        //YES, we want to read from writeRoot, so transaction who is writing will fetch always changed data
            //        //root stays null and will be handeld by LTrie
            //        return table;
            //    }

            //    //NO, we want to read, getting new read node, remembering when table was fixed (Commited or Rollbacked)
            //    root = table.GetTrieReadNode(out dtTableFixed);

            //    transactionReadTables.Add(tableName, new Rtbe() { Ltrie = table, dtTableFixed = dtTableFixed, ReadRoot = root });

            //    return table;
            //}
            //else
            //{
            //    //Table was found in the cache

            //    //Checking READ_SYNCHRO
            //    if (this._transactionUnit.If_TableIsReservedForWrite(tableName))
            //    {
            //        //YES, we want to read from writeRoot, so transaction who is writing will fetch always changed data
            //        //root stays null and will be handeld by LTrie
            //        return rtbe.Ltrie;
            //        //return table;
            //    }

            //    //NO, we want to read, getting new read node

            //    //Checking rtbe modification date
            //    if (rtbe.dtTableFixed != rtbe.Ltrie.DtTableFixed)
            //    {


            //        //Our table cached read-node not actual anymore, data was changed

            //        //after last acquiring of the read node, data was changed, so we need new root node
            //        //root = table.GetTrieReadNode(out dtTableFixed);
            //        root = rtbe.Ltrie.GetTrieReadNode(out dtTableFixed);

            //        //Refreshing cache data.
            //        transactionReadTables[tableName].dtTableFixed = dtTableFixed;
            //        transactionReadTables[tableName].ReadRoot = root;

            //        if (root == null)
            //            return null;    //Table is not operable
            //    }
            //    else
            //    {
            //        //old read-root can be used
            //        root = rtbe.ReadRoot;
            //    }

            //    return rtbe.Ltrie;
            //}


        }

        /// <summary>
        /// <para>Will be used when getting new table for ReadVisibilityScope=true in this transaction.</para>
        /// If true, will create new read-table, if false, will try to reuse cached (once created) existing read-table (or create new cached READ-TABLE).
        /// <para>Switch ReadVisibilityScopeModifier_GenerateNewTableForRead can be changed many times during transaction.</para>
        /// May be switched to true, before call Select...,AsReadVisibilityScope = true, then can be switched back.
        /// </summary>
        public bool ReadVisibilityScopeModifier_GenerateNewTableForRead=false;
        /// <summary>
        /// <para>Works in case if ReadVisibilityScopeModifier_GenerateNewTableForRead = true.</para>
        /// <para>When true, will show also updated, but not committed changes, to the moment of request.</para>
        /// <para>Default is false</para>
        /// </summary>
        public bool ReadVisibilityScopeModifier_DirtyRead = false;

        /// <summary>
        /// Retuns LTrie (or later by necessity ITrie) and as out variable ReadRootNode which must be used for READ FUNC data acquiring.
        /// READ FUNCs calling this proc will receive table and as out var. root, this root will be supplied to the trie.
        /// If root is null, then write root will be used.
        /// If table is null - table doesn't exist.
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="root"></param>
        /// <param name="AsForRead"></param>
        /// <returns></returns>
        private LTrie GetReadTableFromBuffer(string tableName, out ITrieRootNode root, bool AsForRead)
        {
            //Can be changed on interface ITrie by necessity.

            //In discussion, if we need to compare threadsIds, normally transaction can be used only from one thread, but it will be checked
            //by GetTable_READ. In case if we take table from cache, may be it's not necessary to check.
            //if (System.Threading.Thread.CurrentThread.ManagedThreadId != transactionThreadId)
            //{
            //    this.UnregisterTransaction(transactionThreadId);

            //    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTION_CANBEUSED_FROM_ONE_THREAD);
            //}

            LTrie table = null;
            Rtbe rtbe = null;
            long dtTableFixed = 0;
            root = null;

            //Parallel read queries inside of one transaction, to use in TPL in manner Task.WaitAll(Task.Run(() => tran.Select...,Task.Run(() => tran.Select...,...);
            //all reads become automatically with AsForRead flag
            bool ignoreThreadIdCheck = false;
#if NET35 || NETr40
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != this.ManagedThreadId)
#else            
            if (Environment.CurrentManagedThreadId != this.ManagedThreadId)
#endif           
            {
                ignoreThreadIdCheck = true;
                AsForRead = true;
            }

            //READ VISIBILITY SCOPE MODIFIER
            //When we don't want to reuse cached READ_TABLE, but create the new one 
            //Switch ReadVisibilityScopeModifier_GenerateNewTableForRead can be changed many times during transaction.
            //May be switched on before call Select...,AsReadVisibilityScope = true, then can be switched back
            if (ignoreThreadIdCheck || (ReadVisibilityScopeModifier_GenerateNewTableForRead && AsForRead))
            {
                table = this._transactionUnit.TransactionsCoordinator.GetTable_READ(tableName, this.ManagedThreadId, ignoreThreadIdCheck: ignoreThreadIdCheck);

                if (table == null)
                    return null;    //returns null (may be table doesn't exist it's ok for READ FUNCs)

                //Adding to Open Table Counter
                AddOpenTable(tableName);

                //if ReadVisibilityScopeModifier_DirtyRead true, will return also not committed changes
                if (!ReadVisibilityScopeModifier_DirtyRead)
                {
                    root = table.GetTrieReadNode(out dtTableFixed);                    
                }

                return table;
            }
            //////////////////////////////////////////////////////////////////////////// END READ VISIBILITY SCOPE MODIFIER            

            transactionReadTables.TryGetValue(tableName, out rtbe);

            if (rtbe == null)
            {
                table = this._transactionUnit.TransactionsCoordinator.GetTable_READ(tableName, this.ManagedThreadId);

                if (table == null)
                    return null;    //returns null (may be table doesn't exist it's ok for READ FUNCs)

                //Adding to Open Table Counter
                AddOpenTable(tableName);

                //Checking READ_SYNCHRO
                if (this._transactionUnit.If_TableIsReservedForWrite(tableName))
                {
                    if (!AsForRead)
                    {
                        //YES, we want to read from writeRoot, so transaction who is writing will fetch always changed data
                        //root stays null and will be handeld by LTrie
                        return table;
                    }
                }

                //NO, we want to read, getting new read node, remembering when table was fixed (Committed or Rollbacked)
                root = table.GetTrieReadNode(out dtTableFixed);               

                transactionReadTables.Add(tableName, new Rtbe() { Ltrie = table, dtTableFixed = dtTableFixed, ReadRoot = root });

                return table;
            }
            else
            {
                //Table was found in the cache

                //Checking READ_SYNCHRO
                if (this._transactionUnit.If_TableIsReservedForWrite(tableName))
                {
                    if (!AsForRead)
                    {
                        //YES, we want to read from writeRoot, so transaction who is writing will fetch always changed data
                        //root stays null and will be handeld by LTrie
                        return rtbe.Ltrie;
                        //return table;
                    }
                }

                //NO, we want to read, getting new read node

                //Checking rtbe modification date
                if (rtbe.dtTableFixed != rtbe.Ltrie.DtTableFixed)
                {


                    //Our table cached read-node not actual anymore, data was changed

                    //after last acquiring of the read node, data was changed, so we need new root node
                    //root = table.GetTrieReadNode(out dtTableFixed);
                    root = rtbe.Ltrie.GetTrieReadNode(out dtTableFixed);                   

                    //Refreshing cache data.
                    transactionReadTables[tableName].dtTableFixed = dtTableFixed;
                    transactionReadTables[tableName].ReadRoot = root;

                    if (root == null)
                        return null;    //Table is not operable
                }
                else
                {
                    //old read-root can be used
                    root = rtbe.ReadRoot;                   
                }

                return rtbe.Ltrie;
            }

        }
        #endregion

        #region "Commit Rollback"

        /// <summary>
        /// Commits all changes made inside of the current transaction.
        /// </summary>
        public void Commit()
        {
            RandomKeySorter.Flush();
            TextSearchHandlerCommit();
            this._transactionUnit.TransactionsCoordinator.Commit(this.ManagedThreadId);
            TextSearchHandlerAfterCommit();

        }

        /// <summary>
        /// Rollsback all changes made by current transaction before last Commit.
        /// </summary>
        public void Rollback()
        {
            this._transactionUnit.TransactionsCoordinator.Rollback(this.ManagedThreadId);
        }
        #endregion

        #region "Table Add Remove RenameKey"

        /// <summary>
        /// Syntax-sugar for  this.RandomKeySorter.Remove(tableName, key, value);
        /// 
        /// Speeding up, space economy. Represents a mechanism helping to store entites into the memory, before insert or remove.
        /// When AutomaticFlushLimitQuantityPerTable per table (default 10000) is exceed or 
        /// within Commit command, all entites will be flushed (first removed then inserted) on the disk 
        /// sorted by key ascending
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public void RemoveRandomKeySorter<TKey>(string tableName, TKey key)
        {
            this.RandomKeySorter.Remove(tableName, key);
        }

        /// <summary>
        /// Removes specified key, if it existed
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        public void RemoveKey<TKey>(string tableName, TKey key)
        {
            bool WasRemoved = false;
            byte[] deletedValue = null;
            RemoveKey(tableName, key, out WasRemoved, false, out deletedValue);            
        }

        /// <summary>
        /// Removes specified key, if it exists.
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="WasRemoved">indicates that key existed in the system, before removing</param>
        public void RemoveKey<TKey>(string tableName, TKey key, out bool WasRemoved)
        {
            byte[] deletedValue = null;
            RemoveKey(tableName, key, out WasRemoved,false,out deletedValue);
        }

        /// <summary>
        /// Removes specified key, if it exists. Return value which was deleted if WasRemoved is true.
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="WasRemoved">indicates that key existed in the system, before removing</param>
        /// <param name="deletedValue">Will hold deleted value if WasRemoved is true</param>
        public void RemoveKey<TKey>(string tableName, TKey key, out bool WasRemoved, out byte[] deletedValue)
        {
            RemoveKey(tableName, key, out WasRemoved, true, out deletedValue);
        }

        /// <summary>
        /// Internal function.
        /// Removes specified key, if it exists. Can return value which was deleted if WasRemoved is true
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="WasRemoved">indicates that key existed in the system, before removing</param>
        /// <param name="retrieveDeletedValue">indicates if system should retrieve deleted value</param>
        /// <param name="deletedValue"></param>
        private void RemoveKey<TKey>(string tableName, TKey key, out bool WasRemoved, bool retrieveDeletedValue, out byte[] deletedValue)
        {
            WasRemoved = false;
            LTrie table = GetWriteTableFromBuffer(tableName);

            byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);
            table.Remove(ref btKey, out WasRemoved, retrieveDeletedValue, out deletedValue);
        }

        /// <summary>
        /// Removes all records in the table
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="withFileRecreation">also recreates table file, if true</param>
        public void RemoveAllKeys(string tableName, bool withFileRecreation)
        {
            LTrie table = GetWriteTableFromBuffer(tableName);

            table.RemoveAll(withFileRecreation);
        }

        /// <summary>
        /// Renames key old its value on the new one
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="oldKey"></param>
        /// <param name="newKey"></param>
        public void ChangeKey<TKey>(string tableName, TKey oldKey,TKey newKey)
        {
            byte[] ptrToNewKey = null;
            bool WasChanged = false;
            this.ChangeKey(tableName, oldKey, newKey, out ptrToNewKey, out WasChanged);

            //LTrie table = GetWriteTableFromBuffer(tableName);

            //byte[] btOldKey = DataTypesConvertor.ConvertKey<TKey>(oldKey);
            //byte[] btNewKey = DataTypesConvertor.ConvertKey<TKey>(newKey);

            //table.ChangeKey(ref btOldKey,ref btNewKey);
        }

        /// <summary>
        /// Renames key on the new one
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="oldKey"></param>
        /// <param name="newKey"></param>
        /// <param name="ptrToNewKey">return pointer to the new value in the file (always 8 bytes)</param>
        public void ChangeKey<TKey>(string tableName, TKey oldKey, TKey newKey,out byte[] ptrToNewKey)
        {
            ptrToNewKey = null;
            bool WasChanged = false;
            this.ChangeKey(tableName, oldKey, newKey, out ptrToNewKey, out WasChanged);

            //ptrToNewKey = null;
            //LTrie table = GetWriteTableFromBuffer(tableName);

            //byte[] btOldKey = DataTypesConvertor.ConvertKey<TKey>(oldKey);
            //byte[] btNewKey = DataTypesConvertor.ConvertKey<TKey>(newKey);

            //table.ChangeKey(ref btOldKey, ref btNewKey,out ptrToNewKey);
        }

        /// <summary>
        /// Renames key on the new one
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="oldKey"></param>
        /// <param name="newKey"></param>
        /// <param name="ptrToNewKey">return pointer to the new value in the file (always 8 bytes)</param>
        /// <param name="WasChanged">indicates that oldKey existed and was succesfully changed</param>
        public void ChangeKey<TKey>(string tableName, TKey oldKey, TKey newKey, out byte[] ptrToNewKey, out bool WasChanged)
        {
            WasChanged = false;
            ptrToNewKey = null;
            LTrie table = GetWriteTableFromBuffer(tableName);

            byte[] btOldKey = DataTypesConvertor.ConvertKey<TKey>(oldKey);
            byte[] btNewKey = DataTypesConvertor.ConvertKey<TKey>(newKey);

            table.ChangeKey(ref btOldKey, ref btNewKey, out ptrToNewKey, out WasChanged);
        }


        /// <summary>
        /// Insert a dynamic size data block in the table storage, returns 16 bytes length identifier
        /// <para></para>
        /// which can be stored in a table value from specified index.
        /// <para></para>
        /// Retrieve such DataBlock we can using Row.GetDataBlock.
        /// <para>The same statement is used to update datablock, received value must update row value who holds reference to it.</para>
        /// <para>Must be used as row column with dynamic length</para>
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="initialPointer">if null creates new data block, if not null tries to overwrite existing data block</param>
        /// <param name="data"></param>
        /// <returns>returns created data block parameters of fixed 16 bytes length, which can be stored in the row value
        /// <para>and later reused for getting data block back</para>
        /// </returns>
        public byte[] InsertDataBlock(string tableName, byte[] initialPointer, byte[] data)
        {
            LTrie table = GetWriteTableFromBuffer(tableName);

            return table.InsertDataBlock(ref initialPointer, ref data);
        }

        /// <summary>
        /// Modification of InsertDataBlock.
        /// Insert a dynamic size data block in the table storage, returns a fixed 16 bytes length identifier -
        /// it never changes even the value is updated.
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="initialPointer">if null creates new data block, if not null tries to overwrite existing data block</param>
        /// <param name="data"></param>        
        /// <returns></returns>        
        public byte[] InsertDataBlockWithFixedAddress<TValue>(string tableName, byte[] initialPointer, TValue data)
        {
            byte[] refToDataBlock = null;
            LTrie table = GetWriteTableFromBuffer(tableName);
            bool state = table.OverWriteIsAllowed;
            byte[] dt = null;           
            
            dt = DataTypesConvertor.ConvertValue<TValue>(data);

            if (initialPointer == null)
            {
                refToDataBlock = table.InsertDataBlock(ref initialPointer, ref dt);
                refToDataBlock = table.InsertDataBlock(ref initialPointer, ref refToDataBlock);              
                return refToDataBlock;                
            }
            else
            {
                refToDataBlock = this.SelectDataBlock(tableName, initialPointer);

                refToDataBlock = table.InsertDataBlock(ref refToDataBlock, ref dt);

                table.OverWriteIsAllowed = true;
                table.InsertDataBlock(ref initialPointer, ref refToDataBlock);                
                table.OverWriteIsAllowed = state;
                return initialPointer;                
            }
        }

        /// <summary>
        /// Another way (second is via row by index where pointer is stored) to get stored data block
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="ptrToDataBlock">16 byte pointer identifier, received after insertDataBlock</param>
        /// <returns></returns>
        public byte[] SelectDataBlock(string tableName, byte[] ptrToDataBlock)
        {
            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot);

            
            if (table == null)
            {
                return null;
            }
            else
            {
                return table.SelectDataBlock(ref ptrToDataBlock, !(readRoot == null));
            }
        }

        /// <summary>
        /// Gets data block with fixed identifier saved via InsertDataBlockWithFixedAddress
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="ptrToDataBlock"></param>
        /// <returns></returns>
        public TValue SelectDataBlockWithFixedAddress<TValue>(string tableName, byte[] ptrToDataBlock)
        {            
            byte[] refToDataBlock = this.SelectDataBlock(tableName, ptrToDataBlock);
            return DataTypesConvertor.ConvertBack<TValue>(this.SelectDataBlock(tableName, refToDataBlock));
        }



        /// <summary>
        /// Syntax-sugar for  this.RandomKeySorter.Insert(tableName, key, value);
        /// 
        /// Speeding up, space economy. Represents a mechanism helping to store entites into the memory, before insert or remove.
        /// When AutomaticFlushLimitQuantityPerTable per table (default 10000) is exceed or 
        /// within Commit command, all entites will be flushed (first removed then inserted) on the disk 
        /// sorted by key ascending
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public void InsertRandomKeySorter<TKey, TValue>(string tableName, TKey key, TValue value)
        {
            this.RandomKeySorter.Insert(tableName, key, value);
        }


        /// <summary>
        /// Concept of the objects storage (read docu from 20170321)
        /// Automatically gets monotonically grown entity ID (automatically will be stored in table after commit).
        /// Table can contain and handle many monotonically grown identities, 
        /// but for that must be explicitely set addressOfIdentity
        /// </summary>
        /// <typeparam name="TIdentity">type of identity (long,ulong,int,uint,short,ushort)</typeparam>
        /// <param name="tableName">Table name</param>
        /// <param name="addressOfIdentity">by default is stored to th key address byte[]{0}</param>
        /// <param name="seed">Step of growth</param>
        /// <returns></returns>
        public TIdentity ObjectGetNewIdentity<TIdentity>(string tableName, byte[] addressOfIdentity = null, uint seed = 1)
        {
            if (seed < 1)
                seed = 1;

            LTrie table = GetWriteTableFromBuffer(tableName); //Reserving table for write

            addressOfIdentity = addressOfIdentity ?? new byte[] { 0 };

            Type td = typeof(TIdentity);

            byte[] btcnt = this.RandomKeySorter.TryGetValueByKey(tableName, addressOfIdentity.ToBytesString());

            if (btcnt == null)
                btcnt = this.Select<byte[], byte[]>(tableName, addressOfIdentity).Value;

            if (td == DataTypesConvertor.TYPE_LONG)
            {
                var ci = (btcnt == null) ? (long)seed : btcnt.To_Int64_BigEndian() + (long)seed;
                this.RandomKeySorter.Insert<byte[], byte[]>(tableName, addressOfIdentity, ci.ToBytes());
                return (TIdentity)((object)ci);
            }
            else if (td == DataTypesConvertor.TYPE_INT)
            {
                var ci = (btcnt == null) ? (int)seed : btcnt.To_Int32_BigEndian() + (int)seed;
                this.RandomKeySorter.Insert<byte[], byte[]>(tableName, addressOfIdentity, ci.ToBytes());
                return (TIdentity)((object)ci);
            }
            else if (td == DataTypesConvertor.TYPE_ULONG)
            {
                var ci = (btcnt == null) ? (ulong)seed : btcnt.To_UInt64_BigEndian() + (ulong)seed;
                this.RandomKeySorter.Insert<byte[], byte[]>(tableName, addressOfIdentity, ci.ToBytes());
                return (TIdentity)((object)ci);
            }
            else if (td == DataTypesConvertor.TYPE_UINT)
            {
                var ci = (btcnt == null) ? (uint)seed : btcnt.To_UInt32_BigEndian() + (uint)seed;
                this.RandomKeySorter.Insert<byte[], byte[]>(tableName, addressOfIdentity, ci.ToBytes());
                return (TIdentity)((object)ci);
            }
            else if (td == DataTypesConvertor.TYPE_SHORT)
            {
                var ci = (short)((btcnt == null) ? (short)seed : (btcnt.To_Int16_BigEndian() + (short)seed));
                this.RandomKeySorter.Insert<byte[], byte[]>(tableName, addressOfIdentity, ci.ToBytes());
                return (TIdentity)((object)ci);
            }
            else if (td == DataTypesConvertor.TYPE_USHORT)
            {
                var ci = (ushort)((btcnt == null) ? (ushort)seed : (btcnt.To_UInt16_BigEndian() + (ushort)seed));
                this.RandomKeySorter.Insert<byte[], byte[]>(tableName, addressOfIdentity, ci.ToBytes());
                return (TIdentity)((object)ci);
            }
            else
                throw new Exception("DBreeze.Transaction.ObjectGetNewIdentity: not acceptable identity type. (Only (long,ulong,int,uint,short,ushort))");
        }

        /// <summary>
        /// Concept of the objects storage (read docu from 20170321)
        /// Insert/Updates entity and its secondary keys
        /// </summary>
        /// <typeparam name="TObject"></typeparam>
        /// <param name="tableName">Table name</param>
        /// <param name="toInsert">Configuration for the inserting object</param>
        /// <param name="speedUpdate">Set to true to increase update speed (table can consume more physical space)</param>
        /// <returns></returns>
        public DBreeze.Objects.DBreezeObjectInsertResult<TObject> ObjectInsert<TObject>(string tableName, DBreeze.Objects.DBreezeObject<TObject> toInsert, bool speedUpdate = false)
        {
           
            DBreeze.Objects.DBreezeObjectInsertResult<TObject> res = new Objects.DBreezeObjectInsertResult<TObject>();

            if (toInsert == null || toInsert.Indexes == null || toInsert.Indexes.Count < 1)
                throw new Exception("DBreeze.Transaction.InsertObject: indexes are not supplied");

            DBreeze.Objects.DBreezeIndex primary = null;

            //Newly supplied indexes, their count may not correspond to the stored indexes
            Dictionary<byte, byte[]> nidx = new Dictionary<byte, byte[]>();
            foreach (var idx in toInsert.Indexes.OrderByDescending(r=>r.PrimaryIndex).ThenByDescending(r=>r.AddPrimaryToTheEnd))
            {
                if (idx.PrimaryIndex)
                {
                    if (primary != null)
                        throw new Exception("DBreeze.Transaction.ObjectInsert: primary index can be defined only once");
                    primary = idx;
                    primary.FormIndex(null);
                }
                else if (idx.AddPrimaryToTheEnd)
                {
                    if (primary == null)
                        throw new Exception("DBreeze.Transaction.ObjectInsert: primary index has to be supplied");
                    idx.FormIndex(primary.IndexNoPrefix);
                }
                else
                {
                    idx.FormIndex(null);

                    //Possible candidate to grab existing entity
                    if (!toInsert.NewEntity && toInsert.ptrToExisingEntity == null && primary == null && idx.IndexNoPrefix != null)
                    {   
                        primary = idx;
                    }
                }
                if (nidx.ContainsKey(idx.IndexNumber))
                    throw new Exception("DBreeze.Transaction.ObjectInsert: index definition is duplicated");

                nidx[idx.IndexNumber] = idx.IndexFull;
            }

            if (!toInsert.NewEntity && toInsert.ptrToExisingEntity == null && primary == null)
                throw new Exception("DBreeze.Transaction.ObjectInsert: Not supplied index helping to grab entity");


            byte[] ptr = null;
            bool newptr = true;

            //First element (index 0) holds value itself
            //Other elements are existing indexes
            Dictionary<uint, byte[]> d = new Dictionary<uint, byte[]>();            

            //New indexes to be added
            List<byte[]> newIdx = new List<byte[]>();
            byte[] encodedVal = null;
            byte[] val = null;

            if (!toInsert.NewEntity)
            {
                ITrieRootNode readRoot = null;
                LTrie table = null;

                if (toInsert.ptrToExisingEntity == null)
                {                   
                    table = GetReadTableFromBuffer(tableName, out readRoot, true);
                   

                    if (table != null)
                    {                        
                        var row = table.GetKey(ref primary.IndexFull, readRoot, false);
                        if (row.Exists)
                            ptr = row.Value.Substring(0, 16);
                    }
                }
                else
                {
                    ptr = toInsert.ptrToExisingEntity;
                    val = toInsert.ExisingEntity;
                }

                newptr = ptr == null;

                if (!newptr)
                {
                    //Getting existing value
                    if (toInsert.ptrToExisingEntity == null)
                    {
                        val = this.SelectDataBlock(tableName, ptr);
                        val = table.SelectDataBlock(ref val, !(readRoot == null));
                    }                  

                    Biser.Decode_DICT_PROTO_UINT_BYTEARRAY(val, d);

                    if(toInsert.IncludeOldEntityIntoResult)
                        res.OldEntity = DataTypesConvertor.ConvertBack<TObject>(d[0]);
                    res.OldEntityWasFound = true;
                    
                    d[0] = DataTypesConvertor.ConvertValue<TObject>(toInsert.Entity);
                    
                    if (speedUpdate)
                    {
                        //We must in any case delete all indexes and then insert new
                        foreach (var idx in d.Skip(1))
                        {
                            if (nidx.ContainsKey((byte)idx.Key))
                            {
                                if (nidx[(byte)idx.Key] == null)
                                    this.RandomKeySorter.Remove<byte[]>(tableName, idx.Value);
                                else if (!nidx[(byte)idx.Key]._ByteArrayEquals(idx.Value))
                                    this.RandomKeySorter.Remove<byte[]>(tableName, idx.Value);
                            }
                            else
                                this.RandomKeySorter.Remove<byte[]>(tableName, idx.Value);
                        }

                        foreach (var idx in nidx)
                        {
                            if (idx.Value == null)
                                d.Remove(idx.Key);
                            else
                                d[idx.Key] = idx.Value;
                        }

                        ptr = null;
                        newptr = true;
                    }
                    else
                    {
                        //In first element we got value itself
                        //In other - stored indexes
                        foreach (var idx in nidx)
                        {
                            if (d.ContainsKey(idx.Key))
                            {
                                if (idx.Value == null)
                                {
                                    this.RandomKeySorter.Remove<byte[]>(tableName, d[idx.Key]);
                                    d.Remove(idx.Key);
                                    continue;
                                }else if (!idx.Value._ByteArrayEquals(d[idx.Key]))
                                {
                                    this.RandomKeySorter.Remove<byte[]>(tableName, d[idx.Key]);
                                    d[idx.Key] = idx.Value;
                                    newIdx.Add(idx.Value);
                                }
                            }
                            else
                            {
                                if (idx.Value == null)
                                    continue;
                                //new index must be added
                                d[idx.Key] = idx.Value;
                                newIdx.Add(idx.Value);
                            }
                        }

                        //Checking may be we don't need to update anything                       
                        encodedVal = d.Encode_DICT_PROTO_UINT_BYTEARRAY();
                        if (newIdx.Count == 0 && val._ByteArrayEquals(encodedVal))
                        {
                            res.EntityWasInserted = false;
                            return res;
                        }
                    }

                }
                else
                {
                    d[0] = DataTypesConvertor.ConvertValue<TObject>(toInsert.Entity);
                    foreach (var idx in nidx)
                    {
                        if (idx.Value == null)
                            continue;
                        d[idx.Key] = idx.Value;
                    }
                }               
            }
            else
            {
                d[0] = DataTypesConvertor.ConvertValue<TObject>(toInsert.Entity);
                foreach (var idx in nidx)
                {
                    if (idx.Value == null)
                        continue;
                    d[idx.Key] = idx.Value;
                }
            }
            
            if(encodedVal == null)
                encodedVal = d.Encode_DICT_PROTO_UINT_BYTEARRAY();
       
            //Inserting enhanced object with current indexes                       
            ptr = this.InsertDataBlockWithFixedAddress(tableName, ptr, encodedVal);
            res.PtrToObject = ptr;

            //Updating real primary/secondary keys references. Via RandomKeySorter
            if (speedUpdate || newptr)
            {
                //Massive update will come always via RandomKeySorter
                foreach (var idx in d.Skip(1))
                {
                    this.RandomKeySorter.Insert<byte[], byte[]>(tableName, idx.Value, ptr);
                }

            }
            else
            {
                //Only new index
                foreach (var idx in newIdx)
                {
                    this.RandomKeySorter.Insert<byte[], byte[]>(tableName, idx, ptr);
                }
            }

            if (speedUpdate)
                this.RandomKeySorter.TablesWithOverwriteIsNotAllowed(tableName);

            return res;
        }

        /// <summary>
        /// Concept of the objects storage (read docu from 20170321)
        /// Removes entity and its keys
        /// </summary>
        /// <param name="tableName">Table name</param>
        /// <param name="index">Any of formed index to lookup the entity for deletion</param>
        /// <param name="speedUpdate">Aet to true to increase update speed (table can consume more physical space)</param>
        public void ObjectRemove(string tableName, byte[] index, bool speedUpdate = false)
        {
            if (index == null)
                return;
            
            ITrieRootNode readRoot = null;

            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, true);

            
            byte[] ptr = null;

            if (table != null)
            {                
                var row = table.GetKey(ref index, readRoot,false);
                if (row.Exists)
                    ptr = row.Value.Substring(0, 16);
            }
            
            if (ptr != null)
            {
                Dictionary<uint, byte[]> d = new Dictionary<uint, byte[]>();

                //Getting existing value
                byte[] val = this.SelectDataBlock(tableName, ptr);
                val = table.SelectDataBlock(ref val, !(readRoot == null));
                Biser.Decode_DICT_PROTO_UINT_BYTEARRAY(val, d);

                foreach (var idx in d.Skip(1))
                {
                    this.RandomKeySorter.Remove<byte[]>(tableName, idx.Value);
                }

                if (speedUpdate)
                    this.RandomKeySorter.TablesWithOverwriteIsNotAllowed(tableName);
            }
            
        }

        /// <summary>
        /// Concept of the objects storage (read docu from 20170321).
        /// Gets object directly if available its fixed address in the file, can be useful for the indexes stored in different from the object table.
        /// Returns DBreezeObject, to get entity use property.Entity. If returns null, then such entity doesn't exist.
        /// </summary>
        /// <typeparam name="TVal"></typeparam>
        /// <param name="tableName">name of the table</param>
        /// <param name="address">fixed address in the file</param>
        /// <returns></returns>
        public DBreeze.Objects.DBreezeObject<TVal> ObjectGetByFixedAddress<TVal>(string tableName, byte[] address)
        {
            if (address == null)
                return null;

            var ret = new Objects.DBreezeObject<TVal>();

            ret.ptrToExisingEntity = address;
            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot);

            if (table == null)
                return null;

            address = table.SelectDataBlock(ref address, !(readRoot == null));
            Dictionary<uint, byte[]> d = new Dictionary<uint, byte[]>();
            ret.ExisingEntity = table.SelectDataBlock(ref address, !(readRoot == null));
            Biser.Decode_DICT_PROTO_UINT_BYTEARRAY(ret.ExisingEntity, d);
            if (d == null || d.Count < 1)
                return null;
            ret.Entity = DataTypesConvertor.ConvertBack<TVal>(d[0]);
            return ret;
        }


        /// <summary>
        /// Inserts or updates the key
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public void Insert<TKey, TValue>(string tableName, TKey key, TValue value)
        {
            byte[] refToInsertedValue = null;
            bool WasUpdated = false;
            this.Insert(tableName, key, value, out refToInsertedValue, out WasUpdated, false);
        }

        /// <summary>
        /// Inserts or updates the key
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="refToInsertedValue">returns ptr in the file to the value and key (always 8 bytes)</param>
        public void Insert<TKey, TValue>(string tableName, TKey key, TValue value, out byte[] refToInsertedValue)
        {
            refToInsertedValue = null;
            bool WasUpdated = false;
            this.Insert(tableName, key, value, out refToInsertedValue, out WasUpdated, false);

            //refToInsertedValue = null;
            //LTrie table = GetWriteTableFromBuffer(tableName);
            
            ////Special check of null of nulls is integrated inside of the convertor
            ////For keys and values different convertors.


            ////DBreeze.Test.TestStatic.StartCounter("KEYS COMPUTE");

            //byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);
            //byte[] btValue = DataTypesConvertor.ConvertValue<TValue>(value);

            ////DBreeze.Test.TestStatic.StopCounter("KEYS COMPUTE");

            ////DBreeze.Test.TestStatic.StartCounter("VALUE ADD");
            //refToInsertedValue = table.Add(ref btKey, ref btValue);
            ////DBreeze.Test.TestStatic.StopCounter("VALUE ADD");
            //if (refToInsertedValue != null)
            //    refToInsertedValue = refToInsertedValue.EnlargeByteArray_BigEndian(8);
        }

        /// <summary>
        /// Inserts or updates the key
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="refToInsertedValue">returns ptr in the file to the value and key (always 8 bytes)</param>
        /// <param name="WasUpdated">indicates that key we insert, already existed in the system and was updated</param>
        public void Insert<TKey, TValue>(string tableName, TKey key, TValue value, out byte[] refToInsertedValue, out bool WasUpdated)
        {
            this.Insert(tableName, key, value, out refToInsertedValue, out WasUpdated,false);
        }

        /// <summary>
        /// Inserts or updates the key
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="refToInsertedValue">returns ptr in the file to the value and key (always 8 bytes)</param>
        /// <param name="WasUpdated">indicates that key we insert, already existed in the system and was updated</param>
        /// <param name="dontUpdateIfExists">When true - if value exists, we dont update it. If WasUpdated = true then value exists, if false - we have inserted new one</param>
        public void Insert<TKey, TValue>(string tableName, TKey key, TValue value, out byte[] refToInsertedValue, out bool WasUpdated, bool dontUpdateIfExists)
        {
            refToInsertedValue = null;
            WasUpdated = false;
            
            LTrie table = GetWriteTableFromBuffer(tableName);

            //Special check of null of nulls is integrated inside of the convertor
            //For keys and values different convertors.


            //DBreeze.Test.TestStatic.StartCounter("KEYS COMPUTE");

            byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);
            byte[] btValue = DataTypesConvertor.ConvertValue<TValue>(value);

            //DBreeze.Test.TestStatic.StopCounter("KEYS COMPUTE");

            //DBreeze.Test.TestStatic.StartCounter("VALUE ADD");
            refToInsertedValue = table.Add(ref btKey, ref btValue, out WasUpdated, dontUpdateIfExists);
            //DBreeze.Test.TestStatic.StopCounter("VALUE ADD");
            if (refToInsertedValue != null)
                refToInsertedValue = refToInsertedValue.EnlargeByteArray_BigEndian(8);
        }


        /// <summary>
        /// <para>After the end of transaction overwrite will be allowed again.</para>
        /// <para>Concerns overwriting of values, trie search nodes and dataBlocks.</para>
        /// <para>ref. documentation from [20130412]</para>
        /// </summary>
        /// <param name="tableName"></param>      
        public void Technical_SetTable_OverwriteIsNotAllowed(string tableName)
        {
            LTrie table = GetWriteTableFromBuffer(tableName);
            table.OverWriteIsAllowed = false;
        }

        ///// <summary>
        ///// <para>Default value is taken from database global configuration and may be overriden by AlternativeDiskFlushBehaviour.</para>
        ///// <para>This method gives us ability to override it again.</para>
        ///// <para>Use it before modification operations.</para>
        ///// <para>Available only for master table. Has sense only if table will be modified inside of transaction</para>
        ///// </summary>
        ///// <param name="tableName"></param>
        ///// <param name="diskFlushBehaviour"></param>
        //public void Technical_SetTable_DiskFlushBehaviour(string tableName, DBreezeConfiguration.eDiskFlush diskFlushBehaviour)
        //{            
        //    LTrie table = GetWriteTableFromBuffer(tableName);
        //    table.Storage.TrieSettings.DiskFlushBehaviour = diskFlushBehaviour;
        //}


        /// <summary>
        /// Inserts or updates the key value starting from startIndex.
        /// <para>If there were no value before, value byte[] array till startindex wll be filled with byte[] {0}</para>
        /// <para>If value is smaller then startIndex, value will be expanded.</para>
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="startIndex"></param>
        public void InsertPart<TKey, TValue>(string tableName, TKey key, TValue value, uint startIndex)
        {
            byte[] refToInsertedValue = null;
            this.InsertPart(tableName, key, value, startIndex, out refToInsertedValue);
        }


        /// <summary>
        /// Inserts or updates the key value starting from startIndex.
        /// <para>If there were no value before, value byte[] array till startindex wll be filled with byte[] {0}</para>
        /// <para>If value is smaller then startIndex, value will be expanded.</para>
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="startIndex"></param>
        /// <param name="refToInsertedValue">returns ptr in the file to the value and key (always 8 bytes)</param> 
        public void InsertPart<TKey, TValue>(string tableName, TKey key, TValue value, uint startIndex, out byte[] refToInsertedValue)
        {
            refToInsertedValue = null;
            bool WasUpdated = false;
            this.InsertPart(tableName, key, value, startIndex, out refToInsertedValue, out WasUpdated);
            //refToInsertedValue = null;
            //LTrie table = GetWriteTableFromBuffer(tableName);

            ////Special check of null of nulls is integrated inside of the convertor
            ////For keys and values different convertors.

            //byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);
            //byte[] btValue = DataTypesConvertor.ConvertValue<TValue>(value);

            //long valueStartPtr = -1;
            //refToInsertedValue = table.AddPartially(ref btKey, ref btValue, startIndex, out valueStartPtr);

            //if (refToInsertedValue != null)
            //    refToInsertedValue = refToInsertedValue.EnlargeByteArray_BigEndian(8);
        }


        /// <summary>
        /// Inserts or updates the key value starting from startIndex.
        /// <para>If there were no value before, value byte[] array till startindex wll be filled with byte[] {0}</para>
        /// <para>If value is smaller then startIndex, value will be expanded.</para>
        /// <para>Second generic parameter represents datatype of the inserting parameter in the middle of the value</para>
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="startIndex"></param>
        /// <param name="refToInsertedValue">returns ptr in the file to the value and key (always 8 bytes)</param> 
        /// <param name="WasUpdated">indicates that key we insert, already existed in the system and was updated</param> 
        public void InsertPart<TKey, TValue>(string tableName, TKey key, TValue value, uint startIndex, out byte[] refToInsertedValue, out bool WasUpdated)
        {
            refToInsertedValue = null;
            LTrie table = GetWriteTableFromBuffer(tableName);

            //Special check of null of nulls is integrated inside of the convertor
            //For keys and values different convertors.

            byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);
            byte[] btValue = DataTypesConvertor.ConvertValue<TValue>(value);

            long valueStartPtr = -1;
            refToInsertedValue = table.AddPartially(ref btKey, ref btValue, startIndex, out valueStartPtr,out WasUpdated);

            if (refToInsertedValue != null)
                refToInsertedValue = refToInsertedValue.EnlargeByteArray_BigEndian(8);
        }



        /// <summary>
        /// Will create internal table if it doesn't exist and return it.
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="tableIndex"></param>
        /// <returns></returns>
        public NestedTable InsertTable<TKey>(string tableName, TKey key, uint tableIndex)
        {

            LTrie table = GetWriteTableFromBuffer(tableName);

            byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

            LTrieRow row = table.GetKey(ref btKey, null, true);
                        
            var nt = table.GetTable(row,ref btKey, tableIndex, null, true, false); //<-masterTrie argument equals to null in case if it is a first level of nested tables
            nt.ValuesLazyLoadingIsOn = this._valuesLazyLoadingIsOn;
            
            return nt;

            //return table.InsertTable(btKey, tableIndex,null); //<-masterTrie argument equals to null in case if it is a first level of nested tables

        }

        /// <summary>
        /// If internal table doesn't exist will not create it but must always return 
        /// NestedTable, which will be internally marked as absent. 
        /// In this case all Add/Remove operations will have to throw exception and
        /// Select operations will return their default values
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="tableIndex"></param>
        /// <returns></returns>
        public NestedTable SelectTable<TKey>(string tableName, TKey key, uint tableIndex)
        {
            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot);

            if (table == null)
            {
                return new NestedTable(null, false, false);
                //return new NestedTableInternal(false, null, 0, false, 0, false); 
            }
            else
            {
                byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

                LTrieRow row = table.GetKey(ref btKey, readRoot, true);

                /*
                 * !(readRoot == null) means 
                 * (readRoot == null) ? WRITING TABLE TRANSACTION, doesn't use cache for getting value : READING TABLE TRANSACTION, always uses value via cache
                 */
                var nt = table.GetTable(row, ref btKey, tableIndex, null, false, !(readRoot == null));
                nt.ValuesLazyLoadingIsOn = this._valuesLazyLoadingIsOn;
                return nt;

            }

            //return null;
        }


        #endregion

        #region "Insert Specific Structures"

        /// <summary>
        /// Inserts a dictionary into master-table row.
        /// <para></para>
        /// Actually creates a new table inside of master table row and handles it like table with TDictionaryKey key any TDictionaryValue value.
        /// <para></para>
        /// If new Dictionary is supplied then non-existing keys in supplied DB will be removed from db
        /// <para>new values will be inserted, changed values will be updated</para>
        /// <para>To get dictionary use SelectDictionary</para>
        /// </summary>
        /// <typeparam name="TTableKey"></typeparam>
        /// <typeparam name="TDictionaryKey"></typeparam>
        /// <typeparam name="TDictionaryValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="tableIndex"></param>
        /// <param name="withValuesRemove">if true, then values which are not in supplied dictionary will be removed from db, otherwise only appended and updated</param>
        public void InsertDictionary<TTableKey, TDictionaryKey, TDictionaryValue>(string tableName, TTableKey key, Dictionary<TDictionaryKey, TDictionaryValue> value, uint tableIndex,bool withValuesRemove)
        {
            var subTable = this.InsertTable(tableName, key, tableIndex);

            if (withValuesRemove)
            {
                foreach
                    (var row in (
                        from c in subTable.SelectForward<TDictionaryKey, TDictionaryValue>()
                        where !(from v in value select v.Key).Contains(c.Key)
                        select c.Key)
                    )
                {
                    subTable.RemoveKey<TDictionaryKey>(row);
                }
            }

            foreach (var row in value)
            {
                subTable.Insert(row.Key, row.Value);
            }
        }

        /// <summary>
        /// Inserts a dictionary into master-table
        /// </summary>
        /// <typeparam name="TDictionaryKey"></typeparam>
        /// <typeparam name="TDictionaryValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="value"></param>
        /// <param name="withValuesRemove">if true, then values which are not in supplied dictionary will be removed from db, otherwise only appended and updated</param>
        public void InsertDictionary<TDictionaryKey, TDictionaryValue>(string tableName, Dictionary<TDictionaryKey, TDictionaryValue> value, bool withValuesRemove)
        {
            //var subTable = this.InsertTable(tableName, key, tableIndex);

            if (withValuesRemove)
            {
                foreach
                    (var row in (
                        from c in this.SelectForward<TDictionaryKey, TDictionaryValue>(tableName)
                        where !(from v in value select v.Key).Contains(c.Key)
                        select c.Key)
                    )
                {
                    this.RemoveKey<TDictionaryKey>(tableName, row);
                }
            }

            foreach (var row in value)
            {
                this.Insert(tableName, row.Key, row.Value);
                // subTable.Insert(row.Key, row.Value);
            }
        }

        /// <summary>
        /// Selects complete table from master-table row nested table, by row nested-table index as Dictionary.
        /// </summary>
        /// <typeparam name="TTableKey"></typeparam>
        /// <typeparam name="TDictionaryKey"></typeparam>
        /// <typeparam name="TDictionaryValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="tableIndex"></param>
        /// <returns></returns>
        public Dictionary<TDictionaryKey, TDictionaryValue> SelectDictionary<TTableKey, TDictionaryKey, TDictionaryValue>(string tableName, TTableKey key, uint tableIndex)
        {
            Dictionary<TDictionaryKey, TDictionaryValue> output = new Dictionary<TDictionaryKey, TDictionaryValue>();
            foreach (var row in this.SelectTable<TTableKey>(tableName, key, tableIndex).SelectForward<TDictionaryKey, TDictionaryValue>())
            {
                output.Add(row.Key, row.Value);
            }
            return output;
        }


        /// <summary>
        /// Selects complete master-table as Dictionary
        /// </summary>
        /// <typeparam name="TDictionaryKey"></typeparam>
        /// <typeparam name="TDictionaryValue"></typeparam>
        /// <param name="tableName"></param>
        /// <returns></returns>
        public Dictionary<TDictionaryKey, TDictionaryValue> SelectDictionary<TDictionaryKey, TDictionaryValue>(string tableName)
        {
            Dictionary<TDictionaryKey, TDictionaryValue> output = new Dictionary<TDictionaryKey, TDictionaryValue>();
            foreach (var row in this.SelectForward<TDictionaryKey, TDictionaryValue>(tableName))
            {
                output.Add(row.Key, row.Value);
            }
            return output;
        }

        /// <summary>
        /// Inserts a HashSet (unique list of Keys) into master-table row.
        /// <para></para>
        /// Actually creates a new table inside of master table row and handles it like table with THashSetKey key any byte[] == null value.
        /// <para></para>
        /// If new HashSet is supplied then non-existing keys in supplied DB will be removed from db (withValuesRemove=true)
        /// <para>new values will be inserted, changed values will be updated</para>
        /// <para>To get HashSet use SelectHashSet</para>
        /// </summary>
        /// <typeparam name="TTableKey"></typeparam>
        /// <typeparam name="THashSetKey"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="tableIndex"></param>
        /// <param name="withValuesRemove">if true, then values which are not in supplied HashSet will be removed from db, otherwise only appended and updated</param>
        public void InsertHashSet<TTableKey, THashSetKey>(string tableName, TTableKey key, HashSet<THashSetKey> value, uint tableIndex, bool withValuesRemove)
        {
            var subTable = this.InsertTable(tableName, key, tableIndex);

            if (withValuesRemove)
            {
                foreach
                    (var row in (
                        from c in subTable.SelectForward<THashSetKey, byte[]>()
                        where !(from v in value select v).Contains(c.Key)
                        select c.Key)
                    )
                {
                    subTable.RemoveKey<THashSetKey>(row);
                }
            }

            foreach (var row in value)
            {
                subTable.Insert<THashSetKey, byte[]>(row, null);
            }
        }

        /// <summary>
        /// Inserts a HashSet (unique list of Keys) into master-table itself
        /// </summary>
        /// <typeparam name="TTableKey"></typeparam>
        /// <typeparam name="THashSetKey"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="value"></param>
        /// <param name="withValuesRemove">if true, then values which are not in supplied HashSet will be removed from db, otherwise only appended and updated</param>
        public void InsertHashSet<THashSetKey>(string tableName, HashSet<THashSetKey> value, bool withValuesRemove)
        {
           
            if (withValuesRemove)
            {
                foreach
                    (var row in (
                        from c in this.SelectForward<THashSetKey, byte[]>(tableName)
                        where !(from v in value select v).Contains(c.Key)
                        select c.Key)
                    )
                {
                    this.RemoveKey<THashSetKey>(tableName,row);
                }
            }

            foreach (var row in value)
            {
                this.Insert<THashSetKey, byte[]>(tableName,row, null);
            }
        }

        /// <summary>
        /// Selects complete table from master-table row nested table, by row nested-table index as HashSet (unique list of Keys).
        /// </summary>
        /// <typeparam name="TTableKey"></typeparam>
        /// <typeparam name="THashSetKey"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="tableIndex"></param>
        /// <returns></returns>
        public HashSet<THashSetKey> SelectHashSet<TTableKey, THashSetKey>(string tableName, TTableKey key, uint tableIndex)
        {
            HashSet<THashSetKey> output = new HashSet<THashSetKey>();
            foreach (var row in this.SelectTable<TTableKey>(tableName, key, tableIndex).SelectForward<THashSetKey, byte[]>())
            {
                output.Add(row.Key);
            }
            return output;
        }

        /// <summary>
        /// Selects complete master-table as a HashSet into memory
        /// </summary>
        /// <typeparam name="THashSetKey"></typeparam>
        /// <param name="tableName"></param>
        /// <returns></returns>
        public HashSet<THashSetKey> SelectHashSet<THashSetKey>(string tableName)
        {
            HashSet<THashSetKey> output = new HashSet<THashSetKey>();
            foreach (var row in this.SelectForward<THashSetKey, byte[]>(tableName))
            {
                output.Add(row.Key);
            }
            return output;
        }

        #endregion

        #region "Count Max Min"

        /// <summary>
        /// Returns records quantity inside of the the table
        /// </summary>
        /// <param name="tableName"></param>
        /// <returns></returns>
        public ulong Count(string tableName)
        {
            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot);

            if (table == null)
            {
                return 0;
            }
            else
            {
                return table.Count(readRoot);
            }
        }

        /// <summary>
        /// Returns row with the maximal key.
        /// <para>Always check row.Exists property</para>
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <returns></returns>
        public Row<TKey, TValue> Max<TKey, TValue>(string tableName)
        {
            ITrieRootNode readRoot = null;

            LTrie table = GetReadTableFromBuffer(tableName, out readRoot);

            if (table == null)
            {
                return new Row<TKey, TValue>(null, null, false);
                //return new Row<TKey, TValue>(null, null, false, null,true);
            }
            else
            {                
                LTrieRow row = table.IterateBackwardForMaximal(readRoot, false);
                /*
                 * !(readRoot == null) means 
                 * (readRoot == null) ? WRITING TABLE TRANSACTION, doesn't use cache for getting value : READING TABLE TRANSACTION, always uses value via cache
                 */

                return new Row<TKey, TValue>(row, null, !(readRoot == null));
                //return new Row<TKey, TValue>(row._root, row.LinkToValue, row.Exists, row.Key, !(readRoot == null));
                
            }
        }

        /// <summary>
        /// Returns row with the minimal key.
        /// <para>Always check row.Exists property</para>
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <returns></returns>
        public Row<TKey, TValue> Min<TKey, TValue>(string tableName)
        {
            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot);

            if (table == null)
            {
                return new Row<TKey, TValue>(null, null, false);
                //return new Row<TKey, TValue>(null, null, false, null, true);
            }
            else
            {
                LTrieRow row = table.IterateForwardForMinimal(readRoot, false);
                /*
                 * !(readRoot == null) means 
                 * (readRoot == null) ? WRITING TABLE TRANSACTION, doesn't use cache for getting value : READING TABLE TRANSACTION, always uses value via cache
                 */

                return new Row<TKey, TValue>(row, null, !(readRoot == null));
                //return new Row<TKey, TValue>(row._root, row.LinkToValue, row.Exists, row.Key, !(readRoot == null));

            }
        }

        #endregion

        #region "Insert for text search"

        /// <summary>
        /// Inserts/Updates searchable words per external documentID
        /// </summary>
        /// <param name="tableName">Real DBreeze table name, used to store text index for the group of documents. Must be added to tran.SynchronizeTables by programmer.</param>
        /// <param name="documentId">External document id, it will be returned after executing TextSearch.block.GetDocumentIDs</param>        
        /// <param name="containsWords">Space separated words, which will be stored using "contains" logic.</param>
        /// <param name="fullMatchWords">Space separated words, which will be stored using "full-match" logic (they can be also search via contains words by StartsWith logic)</param>
        /// <param name="deferredIndexing"> Means that document will be indexed in parallel thread and possibly search functionality for the document
        /// will be available a bit later after commit. 
        /// It's good for the fast Commits while inserting relatively large searchables-set .
        /// Default value is false, means that searchables will be indexed together with Commit and will be available at the same time.</param>
        /// <param name="containsMinimalLength"> Minimal lenght of the word to be searched using "contains" logic. Default is 3. </param>
        public void TextInsert(string tableName, byte[] documentId, string containsWords="", string fullMatchWords="", bool deferredIndexing=false, int containsMinimalLength=3)
        {            
            if (tsh == null)
                tsh = new TextSearchHandler(this);
            
            tsh.InsertDocumentText(this, tableName, documentId, containsWords, fullMatchWords, deferredIndexing, containsMinimalLength, TextSearchHandler.eInsertMode.Insert);
        }

        /// <summary>
        /// Appends words to the searchable set of the external documentID
        /// </summary>
        /// <param name="tableName">Real DBreeze table name, used to store text index for the group of documents. Must be added to tran.SynchronizeTables by programmer.</param>
        /// <param name="documentId">External document id, it will be returned after executing TextSearch.block.GetDocumentIDs</param>      
        /// <param name="containsWords">Space separated words, which will be stored using "contains" logic.</param>
        /// <param name="fullMatchWords">Space separated words, which will be stored using "full-match" logic</param>
        /// <param name="deferredIndexing"> Means that document will be indexed in parallel thread and possible search will be available a bit later after commit. 
        /// It's good for the fast Commits while inserting relatively large searchables-set .
        /// Default value is false, means that searchables will be indexed together with Commit and will be available at the same time.</param>
        /// <param name="containsMinimalLength"> Minimal lenght of the word to be searched using "contains" logic. Default is 3. </param>
        public void TextAppend(string tableName, byte[] documentId, string containsWords="", string fullMatchWords="", bool deferredIndexing = false, int containsMinimalLength = 3)
        {          
            if (tsh == null)
                tsh = new TextSearchHandler(this);

            tsh.InsertDocumentText(this, tableName, documentId, containsWords, fullMatchWords, deferredIndexing, containsMinimalLength, TextSearchHandler.eInsertMode.Append);
        }

        /// <summary>
        /// Removes words from the searchable set of the external documentID
        /// </summary>
        /// <param name="tableName">Real DBreeze table name, used to store text index for the group of documents. Must be added to tran.SynchronizeTables by programmer.</param>
        /// <param name="documentId">External document id, it will be returned after executing TextSearch.block.GetDocumentIDs</param>        
        /// <param name="fullMatchWords">Space separated words, which will be stored using "full-match" logic</param>
        /// <param name="deferredIndexing"> Means that document will be indexed in parallel thread and possible search will be available a bit later after commit. 
        /// It's good for the fast Commits while inserting relatively large searchables-set .
        /// Default value is false, means that searchables will be indexed together with Commit and will be available at the same time.</param>
        /// <param name="containsMinimalLength"> Minimal lenght of the word to be searched using "contains" logic. Default is 3. </param>
        public void TextRemove(string tableName, byte[] documentId, string fullMatchWords, bool deferredIndexing = false, int containsMinimalLength = 3)
        {
            if (tsh == null)
                tsh = new TextSearchHandler(this);

            tsh.InsertDocumentText(this, tableName, documentId, "", fullMatchWords, deferredIndexing, containsMinimalLength, TextSearchHandler.eInsertMode.Remove);
        }

        /// <summary>
        /// Removes external documentID from the search index
        /// </summary>
        /// <param name="tableName">Real DBreeze table name, used to store text index for the group of documents. Must be added to tran.SynchronizeTables by programmer.</param>
        /// <param name="documentId">External document id, it will be returned after executing TextSearch.block.GetDocumentIDs</param>  
        /// <param name="deferredIndexing"> Means that document will be indexed in parallel thread and possible search will be available a bit later after commit. 
        /// It's good for the fast Commits while inserting relatively large searchables-set .
        /// Default value is false, means that searchables will be indexed together with Commit and will be available at the same time.</param>
        public void TextRemoveAll(string tableName, byte[] documentId, bool deferredIndexing = false)
        {         
            if (tsh == null)
                tsh = new TextSearchHandler(this);

            tsh.InsertDocumentText(this, tableName, documentId, String.Empty,String.Empty, deferredIndexing,3, TextSearchHandler.eInsertMode.Insert);
        }

        /// <summary>
        /// Returns existng searchables for the given documents external IDs
        /// </summary>
        /// <param name="tableName">Real DBreeze table name, used to store text index for the group of documents. Must be added to tran.SynchronizeTables by programmer.</param>
        /// <param name="documentIds"></param>
        /// <returns></returns>
        public Dictionary<byte[],HashSet<string>> TextGetDocumentsSearchables(string tableName, HashSet<byte[]> documentIds)
        {
            if (tsh == null)
                tsh = new TextSearchHandler(this);

            return tsh.GetDocumentsSearchables(this, tableName, documentIds);
        }


        /// <summary>
        /// Returns TextSearchTable (word aligned bitmap index manager for the search-index table). 
        /// Allows to make logical block based comparative operations.
        /// </summary>
        /// <param name="tableName">Real DBreeze table name, used to store text index for the group of documents. Must be added to tran.SynchronizeTables by programmer.</param>
        /// <returns></returns>
        public TextSearchTable TextSearch(string tableName)
        {
            if (tsh == null)
                tsh = new TextSearchHandler(this);

            TextSearchTable w = new TextSearchTable(this,tableName);            
            
            return w;
        }

        /// <summary>
        /// Is called before COMMIT only
        /// </summary>
        void TextSearchHandlerCommit()
        {
            if (tsh != null && tsh.InsertWasPerformed)
                tsh.BeforeCommit();
        }

        /// <summary>
        /// Is called after COMMIT
        /// </summary>
        void TextSearchHandlerAfterCommit()
        {
            if(tsh != null && tsh.InsertWasPerformed)
                tsh.AfterCommit();
        }
        #endregion

        #region "Select one key"


        /// <summary>
        /// Selects specified key from the table
        /// <para>Always check row.Exists property</para>
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, select will return key/value,</para>
        /// <para>like it was before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public Row<TKey, TValue> Select<TKey, TValue>(string tableName, TKey key, bool AsReadVisibilityScope = false)
        {
            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                return new Row<TKey, TValue>(null, null, false);
                //return new Row<TKey, TValue>(null, null, false, null, true);
            }
            else
            {               
                byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

                LTrieRow row = table.GetKey(ref btKey, readRoot, this._valuesLazyLoadingIsOn);
                /*
                 * !(readRoot == null) means 
                 * (readRoot == null) ? WRITING TABLE TRANSACTION, doesn't use cache for getting value : READING TABLE TRANSACTION, always uses value via cache
                 */

                return new Row<TKey, TValue>(row, null, !(readRoot == null));
                //return new Row<TKey, TValue>(row._root, row.LinkToValue, row.Exists, row.Key, !(readRoot == null));

            }
        }

        /// <summary>
        /// Returns Row by supplying direct pointer to key/value in the file.
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="refToInsertedValue"></param>
        /// <returns></returns>
        public Row<TKey, TValue> SelectDirect<TKey, TValue>(string tableName, byte[] refToInsertedValue)
        {
            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot);
                       
            if (refToInsertedValue == null)
            {
                return new Row<TKey, TValue>(null, null, false);
            }
            else
            {
                //Bringing refToInsertedValue to the pointer size of the table
                refToInsertedValue = refToInsertedValue.RemoveLeadingElement(0).EnlargeByteArray_BigEndian(table.Storage.TrieSettings.POINTER_LENGTH);
            }


            if (table == null)
            {               
                
                return new Row<TKey, TValue>(null, null, false);
            }
            else
            {
                
                LTrieRow ltr=new LTrieRow(((readRoot == null) ? table.rn : (LTrieRootNode)readRoot));

                byte[] xKey=null;
                byte[] xValue=null;
                long valueStartPtr=0;
                uint ValueLength = 0;

                //new
                table.Cache.ReadKeyValue(!(readRoot == null), refToInsertedValue, out valueStartPtr, out ValueLength, out xKey, out xValue);
                ltr.ValueStartPointer = valueStartPtr;
                ltr.ValueFullLength = ValueLength;
                ltr.LinkToValue = refToInsertedValue;
                ltr.Value = xValue;
                ltr.ValueIsReadOut = true;
                ltr.Key = xKey;

                //was before
                //ltr.Key = table.Cache.ReadKey(!(readRoot == null), refToInsertedValue);                
                //ltr.LinkToValue = refToInsertedValue;

                return new Row<TKey, TValue>(ltr, null, !(readRoot == null));
                
            }
        }


        #endregion

        #region "Fetch variations"

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForward<TKey, TValue>(string tableName, bool AsReadVisibilityScope = false)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateForward(readRoot, this._valuesLazyLoadingIsOn))
                {
                    yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
                    //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
                }
            }
        }


        /// <summary>
        /// Iterates table backward (ordered by key descending).
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackward<TKey, TValue>(string tableName, bool AsReadVisibilityScope = false)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {   
                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateBackward(readRoot, this._valuesLazyLoadingIsOn))
                {
                    yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
                    //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
                }
            }
        }


        /// <summary>
        /// Iterates table forward (ordered by key ascending). Starting from specified key
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="includeStartFromKey">if start key will be included in the final result</param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardStartFrom<TKey, TValue>(string tableName, TKey key, bool includeStartFromKey, bool AsReadVisibilityScope = false)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {                
                byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateForwardStartFrom(btKey, includeStartFromKey, readRoot, this._valuesLazyLoadingIsOn))
                {
                    yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
                    //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
                }
            }
        }

        /// <summary>
        /// Iterates table backward (ordered by key descending). Starting from specified key
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="includeStartFromKey">if start key will be included in the final result</param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardStartFrom<TKey, TValue>(string tableName, TKey key, bool includeStartFromKey, bool AsReadVisibilityScope = false)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {                
                byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateBackwardStartFrom(btKey, includeStartFromKey, readRoot, this._valuesLazyLoadingIsOn))
                {
                    yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
                    //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
                }
            }
        }


        /// <summary>
        /// Iterates table forward (ordered by key ascending). Starting from specified StartKey up to specified StopKey
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="startKey"></param>
        /// <param name="includeStartKey">if start key will be included in the final result</param>
        /// <param name="stopKey"></param>
        /// <param name="includeStopKey">if stop key will be included in the final result</param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardFromTo<TKey, TValue>(string tableName, TKey startKey, bool includeStartKey, TKey stopKey, bool includeStopKey, bool AsReadVisibilityScope = false)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                byte[] btStartKey = DataTypesConvertor.ConvertKey<TKey>(startKey);
                byte[] btStopKey = DataTypesConvertor.ConvertKey<TKey>(stopKey);

                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateForwardFromTo(btStartKey, btStopKey, includeStartKey, includeStopKey, readRoot, this._valuesLazyLoadingIsOn))
                {
                    yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
                    //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
                }
            }
        }


        /// <summary>
        /// Iterates table backward (ordered by key descending). Starting from specified StartKey down to specified StopKey.
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="startKey"></param>
        /// <param name="includeStartKey">if start key will be included in the final result</param>
        /// <param name="stopKey"></param>
        /// <param name="includeStopKey">if stop key will be included in the final result</param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardFromTo<TKey, TValue>(string tableName, TKey startKey, bool includeStartKey, TKey stopKey, bool includeStopKey, bool AsReadVisibilityScope = false)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                byte[] btStartKey = DataTypesConvertor.ConvertKey<TKey>(startKey);
                byte[] btStopKey = DataTypesConvertor.ConvertKey<TKey>(stopKey);

                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateBackwardFromTo(btStartKey, btStopKey, includeStartKey, includeStopKey, readRoot, this._valuesLazyLoadingIsOn))
                {
                    yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
                    //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
                }
            }
        }
             
        /// <summary>
        /// <para>Mostly can be used for string or byte[] keys</para>
        /// <para>Iterates table forward (ordered by key ascending). Starting and including specified key part (big-endian from byte[] point of view)</para>
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="startWithKeyPart"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardStartsWith<TKey, TValue>(string tableName, TKey startWithKeyPart, bool AsReadVisibilityScope = false)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                byte[] btStartWithKeyPart = DataTypesConvertor.ConvertKey<TKey>(startWithKeyPart);

                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateForwardStartsWith(btStartWithKeyPart, readRoot, this._valuesLazyLoadingIsOn))
                {
                    yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
                    //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
                }
            }
        }





        #region "Select Forward StartsWith ClosestToPrefix"
        
        /// <summary>
        /// <para>Mostly can be used for string or byte[] keys</para>
        /// <para>Iterates table forward (ordered by key ascending). Starting from the prefix or closest to the prefix part (big-endian from byte[] point of view)</para>
        /// <para>If we have in a table keys:</para>
        /// <para>"check"</para>
        /// <para>"sam"</para>
        /// <para>"slam"</para>
        /// <para>"slash"</para>
        /// <para>"what"</para>
        /// <para>our search prefix is "slap", we will get:</para>
        /// <para>"slam"</para>
        /// <para>"slash"</para>
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="startWithClosestPrefix"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardStartsWithClosestToPrefix<TKey, TValue>(string tableName, TKey startWithClosestPrefix, bool AsReadVisibilityScope = false)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                byte[] btStartWithKeyPart = DataTypesConvertor.ConvertKey<TKey>(startWithClosestPrefix);

                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateForwardStartsWithClosestToPrefix(btStartWithKeyPart, readRoot, this._valuesLazyLoadingIsOn))
                {
                    yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
                    //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
                }
            }
        }
        #endregion




        #region "Select Backward StartsWith ClosestToPrefix"
      
        /// <summary>
        /// <para>Mostly can be used for string or byte[] keys</para>
        /// <para>Iterates table backward (ordered by key descending). Starting from the prefix or closest to the prefix part (big-endian from byte[] point of view)</para>
        /// <para>If we have in a table keys:</para>
        /// <para>"check"</para>
        /// <para>"sam"</para>
        /// <para>"slam"</para>
        /// <para>"slash"</para>
        /// <para>"what"</para>
        /// <para>our search prefix is "slap", we will get:</para>
        /// <para>"slash"</para>
        /// <para>"slam"</para>
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="startWithClosestPrefix"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardStartsWithClosestToPrefix<TKey, TValue>(string tableName, TKey startWithClosestPrefix, bool AsReadVisibilityScope = false)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                byte[] btStartWithKeyPart = DataTypesConvertor.ConvertKey<TKey>(startWithClosestPrefix);

                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateBackwardStartsWithClosestToPrefix(btStartWithKeyPart, readRoot, this._valuesLazyLoadingIsOn))
                {
                    yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
                    //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
                }
            }
        }
        #endregion



        

        /// <summary>
        /// <para>Mostly can be used for string or byte[] keys</para>
        /// <para>Iterates table backward (ordered by key descending). Starting and including specified key part (big-endian from byte[] point of view)</para>
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="startWithKeyPart"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardStartsWith<TKey, TValue>(string tableName, TKey startWithKeyPart, bool AsReadVisibilityScope = false)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                byte[] btStartWithKeyPart = DataTypesConvertor.ConvertKey<TKey>(startWithKeyPart);

                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateBackwardStartsWith(btStartWithKeyPart, readRoot, this._valuesLazyLoadingIsOn))
                {
                    yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
                    //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
                }
            }
        }

        #endregion

        #region "Skip variations"


        /// <summary>
        /// Iterates table forward (ordered by key ascending), skipping from the first key specified quantity of records
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="skippingQuantity"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardSkip<TKey, TValue>(string tableName, ulong skippingQuantity, bool AsReadVisibilityScope = false)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateForwardSkip(skippingQuantity, readRoot, this._valuesLazyLoadingIsOn))
                {
                    yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
                    //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
                }
            }
        }

        /// <summary>
        /// Iterates table backward (ordered by key descending), skipping from the last key back specified quantity of records
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="skippingQuantity"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardSkip<TKey, TValue>(string tableName, ulong skippingQuantity, bool AsReadVisibilityScope = false)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateBackwardSkip(skippingQuantity, readRoot, this._valuesLazyLoadingIsOn))
                {
                    yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
                    //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
                }
            }
        }


        /// <summary>
        /// Iterates table forward (ordered by key ascending), skipping from specified key specified quantity of records
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="skippingQuantity"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardSkipFrom<TKey, TValue>(string tableName, TKey key, ulong skippingQuantity, bool AsReadVisibilityScope = false)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateForwardSkipFrom(btKey, skippingQuantity, readRoot, this._valuesLazyLoadingIsOn))
                {
                    yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
                    //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
                }
            }
        }


        /// <summary>
        /// Iterates table backward (ordered by key descending), skipping from specified key back specified quantity of records
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="skippingQuantity">if start key will be included in the final result</param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardSkipFrom<TKey, TValue>(string tableName, TKey key, ulong skippingQuantity, bool AsReadVisibilityScope = false)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateBackwardSkipFrom(btKey, skippingQuantity, readRoot, this._valuesLazyLoadingIsOn))
                {
                    yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
                    //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
                }
            }
        }
        #endregion

        /// <summary>
        /// <para>Experimenting. Replaces existing table with the other table, created out of this engine or from the table that belongs to the engine.</para>
        /// <para>Reading threads of tableName will wait till this operation occurs.</para>
        /// <para>Note that source table files will be deleted. </para>
        /// <para>Use when source table is a temporary table.</para>
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="newTableFullPath">Depends on thisEngineTable argument, if thisEngineTable == true then full path to the file is supplied, otherwise only source-tableName</param>
        /// <param name="thisEngineTable">default false, indicates that table belongs to the engine and all open file pointers on it will be closed, use with temporary tables.</param>
        public void RestoreTableFromTheOtherFile(string tableName, string newTableFullPath, bool sourceTableBelongsToEngine = false)
        {
            //LTrie table = GetWriteTableFromBuffer(tableName);
            //table.Storage.RestoreTableFromTheOtherTable(newTableFullPath);

            LTrie table = GetWriteTableFromBuffer(tableName);
            LTrie tableSource = null;

            if (sourceTableBelongsToEngine)
            {
                tableSource = GetWriteTableFromBuffer(newTableFullPath);
                tableSource.Storage.Table_Dispose();
                newTableFullPath = _transactionUnit.TransactionsCoordinator.GetSchema.GetTablePathFromTableName(newTableFullPath);
            }
            table.Storage.RestoreTableFromTheOtherTable(newTableFullPath);
        }
    }
}
