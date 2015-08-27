/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who thinks that it should be free.
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

namespace DBreeze.Transactions
{
    public class Transaction:IDisposable
    {
        /// <summary>
        /// Managed threadId of the transaction
        /// </summary>
        public int ManagedThreadId = 0;
        private TransactionUnit _transactionUnit = null;

        bool disposed = false;

        /// <summary>
        /// 0 - standard transaction, 1 - locked transaction (Shared Exclusive)
        /// </summary>
        int _transactionType = 0;

        public Transaction(int transactionType, TransactionUnit transactionUnit, eTransactionTablesLockTypes lockType, params string[] tables)
        {
            _transactionType = transactionType;
            ManagedThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
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

            _openTable.Clear();

            //Transaction of type 1 is with Shared and Exclusive locks of the table. Can block even reading threads.
            if (_transactionType == 1)
            {
                _transactionUnit.TransactionsCoordinator.GetSchema.Engine._transactionTablesLocker.RemoveSession();
            }
        }

        private bool _valuesLazyLoadingIsOn = true;
        /// <summary>
        /// When it's on iterators return Row with the key and a pointer to the value.
        /// <par>Value will be read out when we call it Row.Value.</par>
        /// <pa>When it's off we read value together with the key in one round</pa>
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

        /// <summary>
        /// This must be called only if we use not cached opening
        /// </summary>
        private void AddOpenTable(string tableName)
        {
            ulong? cnt=null;
            _openTable.TryGetValue(tableName, out cnt);

            if (cnt == null)
            {
                _openTable.Add(tableName, 1);
            }
            else
            {                
                _openTable[tableName] =  cnt + 1;
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
        private LTrie GetWriteTableFromBuffer(string tableName)
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
        /// Retuns LTrie (or later by necessity ITrie) and as out variable ReadRootNode which must be used for READ FUNC data acquiring.
        /// READ FUNCs calling this proc will receive table and as out var. root, this root will be supplied to the trie.
        /// If root is null, then write root will be used.
        /// If table is null - table doesn't exist.
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="root"></param>
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
            this._transactionUnit.TransactionsCoordinator.Commit(this.ManagedThreadId);
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
        /// Insert a dynamic size data block in the table storage, returns fixed 16 bytes length identifier
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
        /// <param name="tableName"></param>
        /// <param name="tableNumber"></param>
        public NestedTable InsertTable<TKey>(string tableName, TKey key, uint tableIndex)
        {

            LTrie table = GetWriteTableFromBuffer(tableName);

            byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

            LTrieRow row = table.GetKey(ref btKey, null);
                        
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

                LTrieRow row = table.GetKey(ref btKey, readRoot);

                /*
                 * !(readRoot == null) means 
                 * (readRoot == null) ? WRITING TABLE TRANSACTION, doesn't use cache for getting value : READING TABLE TRANSACTION, always uses value via cache
                 */
                var nt = table.GetTable(row,ref btKey, tableIndex, null, false, !(readRoot == null));
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
                table.ValuesLazyLoadingIsOn = this._valuesLazyLoadingIsOn;
                LTrieRow row = table.IterateBackwardForMaximal(readRoot);
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
                table.ValuesLazyLoadingIsOn = this._valuesLazyLoadingIsOn;

                LTrieRow row = table.IterateForwardForMinimal(readRoot);
                /*
                 * !(readRoot == null) means 
                 * (readRoot == null) ? WRITING TABLE TRANSACTION, doesn't use cache for getting value : READING TABLE TRANSACTION, always uses value via cache
                 */

                return new Row<TKey, TValue>(row, null, !(readRoot == null));
                //return new Row<TKey, TValue>(row._root, row.LinkToValue, row.Exists, row.Key, !(readRoot == null));

            }
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
        /// <returns></returns>
        public Row<TKey, TValue> Select<TKey, TValue>(string tableName, TKey key)
        {
            return Select<TKey, TValue>(tableName, key, false);

            //ITrieRootNode readRoot = null;
            //LTrie table = GetReadTableFromBuffer(tableName, out readRoot);

            //if (table == null)
            //{
            //    return new Row<TKey, TValue>(null, null, false);
            //    //return new Row<TKey, TValue>(null, null, false, null, true);
            //}
            //else
            //{
            //    byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);
                
            //    LTrieRow row = table.GetKey(ref btKey, readRoot);
            //    /*
            //     * !(readRoot == null) means 
            //     * (readRoot == null) ? WRITING TABLE TRANSACTION, doesn't use cache for getting value : READING TABLE TRANSACTION, always uses value via cache
            //     */

            //    return new Row<TKey, TValue>(row, null, !(readRoot == null));
            //    //return new Row<TKey, TValue>(row._root, row.LinkToValue, row.Exists, row.Key, !(readRoot == null));

            //}
        }


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
        public Row<TKey, TValue> Select<TKey, TValue>(string tableName, TKey key, bool AsReadVisibilityScope)
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
                table.ValuesLazyLoadingIsOn = this._valuesLazyLoadingIsOn;

                byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

                LTrieRow row = table.GetKey(ref btKey, readRoot);
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
                refToInsertedValue = refToInsertedValue.RemoveLeadingElement(0).EnlargeByteArray_BigEndian(table.Storage.TrieSettings.POINTER_LENGHT);
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
        /// Iterates table forward (ordered by key ascending).
        /// <para>Always check row.Exists property</para>
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="tableName"></param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForward<TKey, TValue>(string tableName)
        {
            return SelectForward<TKey, TValue>(tableName, false);
                        
            //ITrieRootNode readRoot = null;
            //LTrie table = GetReadTableFromBuffer(tableName, out readRoot);
            
            //if (table == null)
            //{
            //    //do nothing end of iteration                
            //}
            //else
            //{
            //    //readRoot can be either filled or null
            //    //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
            //    foreach (var xrow in table.IterateForward(readRoot))
            //    {
            //        yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
            //        //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
            //    }
            //}
        }

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
        public IEnumerable<Row<TKey, TValue>> SelectForward<TKey, TValue>(string tableName, bool AsReadVisibilityScope)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                table.ValuesLazyLoadingIsOn = this._valuesLazyLoadingIsOn;

                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateForward(readRoot))
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
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackward<TKey, TValue>(string tableName)
        {
            return SelectBackward<TKey, TValue>(tableName, false);

            //ITrieRootNode readRoot = null;
            //LTrie table = GetReadTableFromBuffer(tableName, out readRoot);

            //if (table == null)
            //{
            //    //do nothing end of iteration                
            //}
            //else
            //{
            //    //readRoot can be either filled or null
            //    //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
            //    foreach (var xrow in table.IterateBackward(readRoot))
            //    {
            //        yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
            //        //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
            //    }
            //}
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
        public IEnumerable<Row<TKey, TValue>> SelectBackward<TKey, TValue>(string tableName, bool AsReadVisibilityScope)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                table.ValuesLazyLoadingIsOn = this._valuesLazyLoadingIsOn;
                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateBackward(readRoot))
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
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardStartFrom<TKey, TValue>(string tableName,TKey key, bool includeStartFromKey)
        {
            return SelectForwardStartFrom<TKey, TValue>(tableName,key, includeStartFromKey, false);
            //ITrieRootNode readRoot = null;
            //LTrie table = GetReadTableFromBuffer(tableName, out readRoot);

            //if (table == null)
            //{
            //    //do nothing end of iteration                
            //}
            //else
            //{
            //    byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

            //    //readRoot can be either filled or null
            //    //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
            //    foreach (var xrow in table.IterateForwardStartFrom(btKey, includeStartFromKey,readRoot))
            //    {
            //        yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
            //        //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
            //    }
            //}
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
        public IEnumerable<Row<TKey, TValue>> SelectForwardStartFrom<TKey, TValue>(string tableName, TKey key, bool includeStartFromKey, bool AsReadVisibilityScope)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                table.ValuesLazyLoadingIsOn = this._valuesLazyLoadingIsOn;
                byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateForwardStartFrom(btKey, includeStartFromKey, readRoot))
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
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardStartFrom<TKey, TValue>(string tableName, TKey key, bool includeStartFromKey)
        {
            return SelectBackwardStartFrom<TKey, TValue>(tableName, key, includeStartFromKey, false);

            //ITrieRootNode readRoot = null;
            //LTrie table = GetReadTableFromBuffer(tableName, out readRoot);

            //if (table == null)
            //{
            //    //do nothing end of iteration                
            //}
            //else
            //{
            //    byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

            //    //readRoot can be either filled or null
            //    //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
            //    foreach (var xrow in table.IterateBackwardStartFrom(btKey, includeStartFromKey, readRoot))
            //    {
            //        yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
            //        //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
            //    }
            //}
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
        public IEnumerable<Row<TKey, TValue>> SelectBackwardStartFrom<TKey, TValue>(string tableName, TKey key, bool includeStartFromKey, bool AsReadVisibilityScope)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                table.ValuesLazyLoadingIsOn = this._valuesLazyLoadingIsOn;
                byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateBackwardStartFrom(btKey, includeStartFromKey, readRoot))
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
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardFromTo<TKey, TValue>(string tableName, TKey startKey, bool includeStartKey, TKey stopKey, bool includeStopKey)
        {
            return SelectForwardFromTo<TKey, TValue>(tableName, startKey, includeStartKey, stopKey, includeStopKey, false);
            //ITrieRootNode readRoot = null;
            //LTrie table = GetReadTableFromBuffer(tableName, out readRoot);

            //if (table == null)
            //{
            //    //do nothing end of iteration                
            //}
            //else
            //{
            //    byte[] btStartKey = DataTypesConvertor.ConvertKey<TKey>(startKey);
            //    byte[] btStopKey = DataTypesConvertor.ConvertKey<TKey>(stopKey);

            //    //readRoot can be either filled or null
            //    //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
            //    foreach (var xrow in table.IterateForwardFromTo(btStartKey, btStopKey, includeStartKey, includeStopKey, readRoot))
            //    {
            //        yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
            //        //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
            //    }
            //}
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
        public IEnumerable<Row<TKey, TValue>> SelectForwardFromTo<TKey, TValue>(string tableName, TKey startKey, bool includeStartKey, TKey stopKey, bool includeStopKey, bool AsReadVisibilityScope)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                table.ValuesLazyLoadingIsOn = this._valuesLazyLoadingIsOn;

                byte[] btStartKey = DataTypesConvertor.ConvertKey<TKey>(startKey);
                byte[] btStopKey = DataTypesConvertor.ConvertKey<TKey>(stopKey);

                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateForwardFromTo(btStartKey, btStopKey, includeStartKey, includeStopKey, readRoot))
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
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardFromTo<TKey, TValue>(string tableName, TKey startKey, bool includeStartKey, TKey stopKey, bool includeStopKey)
        {
            return SelectBackwardFromTo<TKey, TValue>(tableName, startKey, includeStartKey, stopKey, includeStopKey, false);
            //ITrieRootNode readRoot = null;
            //LTrie table = GetReadTableFromBuffer(tableName, out readRoot);

            //if (table == null)
            //{
            //    //do nothing end of iteration                
            //}
            //else
            //{
            //    byte[] btStartKey = DataTypesConvertor.ConvertKey<TKey>(startKey);
            //    byte[] btStopKey = DataTypesConvertor.ConvertKey<TKey>(stopKey);

            //    //readRoot can be either filled or null
            //    //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
            //    foreach (var xrow in table.IterateBackwardFromTo(btStartKey, btStopKey, includeStartKey, includeStopKey, readRoot))
            //    {
            //        yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
            //        //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
            //    }
            //}
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
        public IEnumerable<Row<TKey, TValue>> SelectBackwardFromTo<TKey, TValue>(string tableName, TKey startKey, bool includeStartKey, TKey stopKey, bool includeStopKey, bool AsReadVisibilityScope)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                table.ValuesLazyLoadingIsOn = this._valuesLazyLoadingIsOn;

                byte[] btStartKey = DataTypesConvertor.ConvertKey<TKey>(startKey);
                byte[] btStopKey = DataTypesConvertor.ConvertKey<TKey>(stopKey);

                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateBackwardFromTo(btStartKey, btStopKey, includeStartKey, includeStopKey, readRoot))
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
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardStartsWith<TKey, TValue>(string tableName, TKey startWithKeyPart)
        {
            return SelectForwardStartsWith<TKey, TValue>(tableName, startWithKeyPart, false);
            //ITrieRootNode readRoot = null;
            //LTrie table = GetReadTableFromBuffer(tableName, out readRoot);

            //if (table == null)
            //{
            //    //do nothing end of iteration                
            //}
            //else
            //{
            //    byte[] btStartWithKeyPart = DataTypesConvertor.ConvertKey<TKey>(startWithKeyPart);

            //    //readRoot can be either filled or null
            //    //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
            //    foreach (var xrow in table.IterateForwardStartsWith(btStartWithKeyPart, readRoot))
            //    {
            //        yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
            //        //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
            //    }
            //}
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
        public IEnumerable<Row<TKey, TValue>> SelectForwardStartsWith<TKey, TValue>(string tableName, TKey startWithKeyPart, bool AsReadVisibilityScope)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                table.ValuesLazyLoadingIsOn = this._valuesLazyLoadingIsOn;
                byte[] btStartWithKeyPart = DataTypesConvertor.ConvertKey<TKey>(startWithKeyPart);

                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateForwardStartsWith(btStartWithKeyPart, readRoot))
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
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardStartsWithClosestToPrefix<TKey, TValue>(string tableName, TKey startWithClosestPrefix)
        {
            return SelectForwardStartsWithClosestToPrefix<TKey, TValue>(tableName, startWithClosestPrefix, false);          
        }

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
        public IEnumerable<Row<TKey, TValue>> SelectForwardStartsWithClosestToPrefix<TKey, TValue>(string tableName, TKey startWithClosestPrefix, bool AsReadVisibilityScope)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                table.ValuesLazyLoadingIsOn = this._valuesLazyLoadingIsOn;
                byte[] btStartWithKeyPart = DataTypesConvertor.ConvertKey<TKey>(startWithClosestPrefix);

                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateForwardStartsWithClosestToPrefix(btStartWithKeyPart, readRoot))
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
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardStartsWithClosestToPrefix<TKey, TValue>(string tableName, TKey startWithClosestPrefix)
        {
            return SelectBackwardStartsWithClosestToPrefix<TKey, TValue>(tableName, startWithClosestPrefix, false);
        }

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
        public IEnumerable<Row<TKey, TValue>> SelectBackwardStartsWithClosestToPrefix<TKey, TValue>(string tableName, TKey startWithClosestPrefix, bool AsReadVisibilityScope)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                table.ValuesLazyLoadingIsOn = this._valuesLazyLoadingIsOn;
                byte[] btStartWithKeyPart = DataTypesConvertor.ConvertKey<TKey>(startWithClosestPrefix);

                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateBackwardStartsWithClosestToPrefix(btStartWithKeyPart, readRoot))
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
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardStartsWith<TKey, TValue>(string tableName, TKey startWithKeyPart)
        {
            return SelectBackwardStartsWith<TKey, TValue>(tableName, startWithKeyPart, false);

            //ITrieRootNode readRoot = null;
            //LTrie table = GetReadTableFromBuffer(tableName, out readRoot);

            //if (table == null)
            //{
            //    //do nothing end of iteration                
            //}
            //else
            //{
            //    byte[] btStartWithKeyPart = DataTypesConvertor.ConvertKey<TKey>(startWithKeyPart);

            //    //readRoot can be either filled or null
            //    //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
            //    foreach (var xrow in table.IterateBackwardStartsWith(btStartWithKeyPart, readRoot))
            //    {
            //        yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
            //        //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
            //    }
            //}
        }

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
        public IEnumerable<Row<TKey, TValue>> SelectBackwardStartsWith<TKey, TValue>(string tableName, TKey startWithKeyPart, bool AsReadVisibilityScope)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                table.ValuesLazyLoadingIsOn = this._valuesLazyLoadingIsOn;
                byte[] btStartWithKeyPart = DataTypesConvertor.ConvertKey<TKey>(startWithKeyPart);

                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateBackwardStartsWith(btStartWithKeyPart, readRoot))
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
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardSkip<TKey, TValue>(string tableName, ulong skippingQuantity)
        {

            return SelectForwardSkip<TKey, TValue>(tableName, skippingQuantity, false);
            //ITrieRootNode readRoot = null;
            //LTrie table = GetReadTableFromBuffer(tableName, out readRoot);

            //if (table == null)
            //{
            //    //do nothing end of iteration                
            //}
            //else
            //{
            //    //readRoot can be either filled or null
            //    //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
            //    foreach (var xrow in table.IterateForwardSkip(skippingQuantity,readRoot))
            //    {
            //        yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
            //        //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
            //    }
            //}
        }

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
        public IEnumerable<Row<TKey, TValue>> SelectForwardSkip<TKey, TValue>(string tableName, ulong skippingQuantity, bool AsReadVisibilityScope)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                table.ValuesLazyLoadingIsOn = this._valuesLazyLoadingIsOn;
                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateForwardSkip(skippingQuantity, readRoot))
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
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardSkip<TKey, TValue>(string tableName,ulong skippingQuantity)
        {
            return SelectBackwardSkip<TKey, TValue>(tableName,skippingQuantity, false);

            //ITrieRootNode readRoot = null;
            //LTrie table = GetReadTableFromBuffer(tableName, out readRoot);

            //if (table == null)
            //{
            //    //do nothing end of iteration                
            //}
            //else
            //{
            //    //readRoot can be either filled or null
            //    //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
            //    foreach (var xrow in table.IterateBackwardSkip(skippingQuantity, readRoot))
            //    {
            //        yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
            //        //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
            //    }
            //}
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
        public IEnumerable<Row<TKey, TValue>> SelectBackwardSkip<TKey, TValue>(string tableName, ulong skippingQuantity, bool AsReadVisibilityScope)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                table.ValuesLazyLoadingIsOn = this._valuesLazyLoadingIsOn;
                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateBackwardSkip(skippingQuantity, readRoot))
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
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardSkipFrom<TKey, TValue>(string tableName, TKey key, ulong skippingQuantity)
        {
            return SelectForwardSkipFrom<TKey, TValue>(tableName, key, skippingQuantity, false);

            //ITrieRootNode readRoot = null;
            //LTrie table = GetReadTableFromBuffer(tableName, out readRoot);

            //if (table == null)
            //{
            //    //do nothing end of iteration                
            //}
            //else
            //{
            //    byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

            //    //readRoot can be either filled or null
            //    //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
            //    foreach (var xrow in table.IterateForwardSkipFrom(btKey, skippingQuantity, readRoot))
            //    {
            //        yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
            //        //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
            //    }
            //}
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
        public IEnumerable<Row<TKey, TValue>> SelectForwardSkipFrom<TKey, TValue>(string tableName, TKey key, ulong skippingQuantity, bool AsReadVisibilityScope)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                table.ValuesLazyLoadingIsOn = this._valuesLazyLoadingIsOn;
                byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateForwardSkipFrom(btKey, skippingQuantity, readRoot))
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
        /// <param name="skippingQuantity"></param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardSkipFrom<TKey, TValue>(string tableName, TKey key, ulong skippingQuantity)
        {
            return SelectBackwardSkipFrom<TKey, TValue>(tableName, key, skippingQuantity,false);
            //ITrieRootNode readRoot = null;
            //LTrie table = GetReadTableFromBuffer(tableName, out readRoot);

            //if (table == null)
            //{
            //    //do nothing end of iteration                
            //}
            //else
            //{
            //    byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

            //    //readRoot can be either filled or null
            //    //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
            //    foreach (var xrow in table.IterateBackwardSkipFrom(btKey, skippingQuantity, readRoot))
            //    {
            //        yield return new Row<TKey, TValue>(xrow,null, !(readRoot == null));
            //        //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
            //    }
            //}
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
        public IEnumerable<Row<TKey, TValue>> SelectBackwardSkipFrom<TKey, TValue>(string tableName, TKey key, ulong skippingQuantity, bool AsReadVisibilityScope)
        {

            ITrieRootNode readRoot = null;
            LTrie table = GetReadTableFromBuffer(tableName, out readRoot, AsReadVisibilityScope);

            if (table == null)
            {
                //do nothing end of iteration                
            }
            else
            {
                table.ValuesLazyLoadingIsOn = this._valuesLazyLoadingIsOn;
                byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

                //readRoot can be either filled or null
                //if null it means that write root will be used (READ_SYNCHRO) if filled - this root will be used
                foreach (var xrow in table.IterateBackwardSkipFrom(btKey, skippingQuantity, readRoot))
                {
                    yield return new Row<TKey, TValue>(xrow, null, !(readRoot == null));
                    //yield return new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, !(readRoot == null));
                }
            }
        }
        #endregion

        /// <summary>
        /// Experimental. Replaces existing table with the other table, created out of this engine.
        /// Reading threads will wait till this operation occurs.
        /// Note that prototype table will be deleted.
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="newTableFullPath"></param>
        public void RestoreTableFromTheOtherFile(string tableName, string newTableFullPath)
        {
            LTrie table = GetWriteTableFromBuffer(tableName);
            table.Storage.RestoreTableFromTheOtherTable(newTableFullPath);
        }
    }
}
