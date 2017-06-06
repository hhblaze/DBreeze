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
using DBreeze.Utils;

namespace DBreeze.DataTypes
{
    /// <summary>
    /// NestedTable
    /// </summary>
    public class NestedTable:IDisposable
    {
        private NestedTableInternal _tbl = null;
        internal bool _insertAllowed = false;
        private bool _tableExists = false;
        
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="nt"></param>
        /// <param name="insertTablesAllowed"></param>
        /// <param name="tableExists"></param>
        internal NestedTable(NestedTableInternal nt, bool insertTablesAllowed, bool tableExists)
        {
            _tbl = nt;
            _insertAllowed = insertTablesAllowed;
            _tableExists = tableExists;
        }

        bool _valuesLazyLoadingIsOn = true;

        /// <summary>
        /// When it's on iterators return Row with the key and a pointer to the value.
        /// <par>Value will be read out when we call it Row.Value.</par>
        /// <pa>When it's off we read value together with the key in one round</pa>
        /// </summary>
        public bool ValuesLazyLoadingIsOn
        {
            get { return this._valuesLazyLoadingIsOn; }
            set { this._valuesLazyLoadingIsOn = value; }
            //get { return (_tbl == null) ? true : _tbl.ValuesLazyLoadingIsOn; }
            //set
            //{
            //    if (_tbl != null)
            //    {
            //        _tbl.ValuesLazyLoadingIsOn = value;
            //    }
            //}
        }

        /// <summary>
        /// You are already in the table
        /// <para>This function will help to access another table by parent table key and its value index</para>
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="key"></param>
        /// <param name="tableIndex"></param>
        /// <returns></returns>
        public NestedTable GetTable<TKey>(TKey key, uint tableIndex)
        {
            if (!this._tableExists)
                return new NestedTable(null, false, false);

            return _tbl.GetTable(key, tableIndex, this._insertAllowed);
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            this.CloseTable();
        }
       

        /// <summary>
        /// Tries to close the table if no other threads are using it.
        /// </summary>
        public void CloseTable()
        {
            if (!this._tableExists)
                return;

            this._tbl.CloseTable();
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
        /// <param name="initialPointer">if null creates new data block, if not null tries to overwrite existing data block</param>
        /// <param name="data"></param>
        /// <returns>returns created data block parameters of fixed 16 bytes length, which can be stored in the row value
        /// <para>and later reused for getting data block back</para>
        /// </returns>
        public byte[] InsertDataBlock(byte[] initialPointer, byte[] data)
        {
            return _tbl.table.InsertDataBlock(ref initialPointer, ref data);
        }

        /// <summary>
        /// Another way (second is via row by index where pointer is stored) to get stored data block
        /// </summary>
        /// <param name="initialPointer"></param>
        /// <returns></returns>
        public byte[] SelectDataBlock(byte[] initialPointer)
        {
#if NET35 || NETr40
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
#else
            bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != Environment.CurrentManagedThreadId);
#endif 

            return _tbl.table.SelectDataBlock(ref initialPointer, useCache);
        }


        /// <summary>
        /// Inserts or updates the key
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public NestedTable Insert<TKey, TValue>(TKey key, TValue value)
        {
            byte[] refToInsertedValue = null;
            bool WasUpdated = false;

            return this.Insert<TKey, TValue>(key, value, out refToInsertedValue, out WasUpdated, false);
        }

        /// <summary>
        /// Inserts or updates the key
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="refToInsertedValue">returns ptr in the file to the value and key (always 8 bytes)</param>
        /// <returns></returns>
        public NestedTable Insert<TKey, TValue>(TKey key, TValue value,out byte[] refToInsertedValue)
        {
            refToInsertedValue = null;
            bool WasUpdated = false;

            return this.Insert<TKey, TValue>(key, value, out refToInsertedValue, out WasUpdated, false); 

        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="refToInsertedValue">returns ptr in the file to the value and key (always 8 bytes)</param>
        /// <param name="WasUpdated">indicates that key we insert, already existed in the system and was updated</param>
        /// <returns></returns>
        public NestedTable Insert<TKey, TValue>(TKey key, TValue value, out byte[] refToInsertedValue, out bool WasUpdated)
        {
            return this.Insert<TKey, TValue>(key, value, out refToInsertedValue, out WasUpdated,false); 
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="refToInsertedValue">returns ptr in the file to the value and key (always 8 bytes)</param>
        /// <param name="WasUpdated">indicates that key we insert, already existed in the system and was updated</param>
        /// <param name="dontUpdateIfExists">When true - if value exists, we dont update it. If WasUpdated = true then value exists, if false - we have inserted new one</param>
        /// <returns></returns>
        public NestedTable Insert<TKey, TValue>(TKey key, TValue value, out byte[] refToInsertedValue, out bool WasUpdated, bool dontUpdateIfExists)
        {
            WasUpdated = false;
            refToInsertedValue = null;

            if (!this._tableExists)
                return new NestedTable(null, false, false);

            if (!_insertAllowed)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DBINTABLE_CHANGEDATA_FROMSELECTVIEW);
            }


            //Special check of null of nulls is integrated inside of the convertor
            //For keys and values different convertors.

            byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);
            byte[] btValue = DataTypesConvertor.ConvertValue<TValue>(value);

            refToInsertedValue = _tbl.table.Add(ref btKey, ref btValue, out WasUpdated, dontUpdateIfExists);

            if (refToInsertedValue != null)
                refToInsertedValue = refToInsertedValue.EnlargeByteArray_BigEndian(8);

            return this;
        }

        /// <summary>
        /// <para>After the end of transaction overwrite will be allowed again.</para>
        /// <para>Concerns overwriting of values, trie search nodes and dataBlocks.</para>
        /// <para>ref. documentation from [20130412]</para>
        /// </summary>
        /// <param name="isAllowed"></param>
        public void Technical_SetTable_OverwriteIsNotAllowed()
        {
            if (!this._tableExists)
                return;

            _tbl.table.OverWriteIsAllowed = false;
        }

        


        #region "Specific structures"
        /// <summary>
        /// Inserts a dictionary into nested-table row.
        /// <para></para>
        /// Designed for simple dictionary data types.
        /// <para></para>
        /// Actually creates a new table inside of nested table row and handles it like dictionary.
        /// <para></para>
        /// If new Dictionary is supplied then non-existing keys in supplied DB will be removed from db
        /// <para>new values will be inserted, changed values will be updated</para>
        /// <para>To get dictionary use SelectDictionary</para>
        /// </summary>
        /// <typeparam name="TTableKey"></typeparam>
        /// <typeparam name="TDictionaryKey"></typeparam>
        /// <typeparam name="TDictionaryValue"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="tableIndex"></param>
        /// <param name="withValuesRemove">if true, then values which are not in supplied dictionary will be removed from db, otherwise only appended and updated</param>
        /// <returns>Returns Nested Table where insert was made</returns>
        public NestedTable InsertDictionary<TTableKey, TDictionaryKey, TDictionaryValue>(TTableKey key, Dictionary<TDictionaryKey, TDictionaryValue> value, uint tableIndex,bool withValuesRemove)
        {
            var subTable = this.GetTable(key, tableIndex);

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

            return this;
        }

        /// <summary>
        /// Inserts dictionary into current nested table.
        /// </summary>
        /// <typeparam name="TDictionaryKey"></typeparam>
        /// <typeparam name="TDictionaryValue"></typeparam>
        /// <param name="value"></param>
        /// <param name="withValuesRemove">if true, then values which are not in supplied dictionary will be removed from db, otherwise only appended and updated</param>
        /// <returns>current nested table</returns>
        public NestedTable InsertDictionary<TDictionaryKey, TDictionaryValue>(Dictionary<TDictionaryKey, TDictionaryValue> value, bool withValuesRemove)
        {
            // var subTable = this.GetTable(key, tableIndex);

            if (withValuesRemove)
            {
                foreach
                    (var row in (
                        from c in this.SelectForward<TDictionaryKey, TDictionaryValue>()
                        where !(from v in value select v.Key).Contains(c.Key)
                        select c.Key)
                    )
                {
                    this.RemoveKey<TDictionaryKey>(row);
                }
            }

            foreach (var row in value)
            {
                this.Insert(row.Key, row.Value);
            }

            return this;
        }

        /// <summary>
        /// Selects complete table from nested-table row nested table, by row nested-table index as Dictionary.
        /// </summary>
        /// <typeparam name="TTableKey"></typeparam>
        /// <typeparam name="TDictionaryKey"></typeparam>
        /// <typeparam name="TDictionaryValue"></typeparam>
        /// <param name="key"></param>
        /// <param name="tableIndex"></param>
        /// <returns></returns>
        public Dictionary<TDictionaryKey, TDictionaryValue> SelectDictionary<TTableKey, TDictionaryKey, TDictionaryValue>(TTableKey key, uint tableIndex)
        {
            Dictionary<TDictionaryKey, TDictionaryValue> output = new Dictionary<TDictionaryKey, TDictionaryValue>();
            foreach (var row in this.GetTable<TTableKey>(key, tableIndex).SelectForward<TDictionaryKey, TDictionaryValue>())
            {
                output.Add(row.Key, row.Value);
            }
            return output;
        }

        /// <summary>
        /// Selects completely current nested-table as a Dictionary.
        /// </summary>
        /// <typeparam name="TDictionaryKey"></typeparam>
        /// <typeparam name="TDictionaryValue"></typeparam>
        /// <returns></returns>
        public Dictionary<TDictionaryKey, TDictionaryValue> SelectDictionary<TDictionaryKey, TDictionaryValue>()
        {
            Dictionary<TDictionaryKey, TDictionaryValue> output = new Dictionary<TDictionaryKey, TDictionaryValue>();
            foreach (var row in this.SelectForward<TDictionaryKey, TDictionaryValue>())
            {
                output.Add(row.Key, row.Value);
            }
            return output;
        }

        /// <summary>
        /// Inserts a HashSet (unique list of Keys) into nested-table row.
        /// <para></para>
        /// Actually creates a new table inside of nested table row and handles it like table with THashSetKey key any byte[] == null value.
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
        /// <returns>Returns Nested Table where insert was made</returns>
        public NestedTable InsertHashSet<TTableKey, THashSetKey>(TTableKey key, HashSet<THashSetKey> value, uint tableIndex, bool withValuesRemove)
        {
            var subTable = this.GetTable(key, tableIndex);

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

            return this;
        }

        /// <summary>
        /// Inserts HashSet into current nested-table
        /// </summary>
        /// <typeparam name="THashSetKey"></typeparam>
        /// <param name="value"></param>
        /// <param name="withValuesRemove">if true, then values which are not in supplied HashSet will be removed from db, otherwise only appended and updated</param>
        /// <returns>current nested table</returns>
        public NestedTable InsertHashSet<THashSetKey>(HashSet<THashSetKey> value, bool withValuesRemove)
        {
            
            if (withValuesRemove)
            {
                foreach
                    (var row in (
                        from c in this.SelectForward<THashSetKey, byte[]>()
                        where !(from v in value select v).Contains(c.Key)
                        select c.Key)
                    )
                {
                    this.RemoveKey<THashSetKey>(row);
                }
            }

            foreach (var row in value)
            {
                this.Insert<THashSetKey, byte[]>(row, null);
            }

            return this;
        }

        /// <summary>
        /// Selects complete table from nested-table row nested table, by row nested-table index as HashSet (unique list of Keys).
        /// </summary>
        /// <typeparam name="TTableKey"></typeparam>
        /// <typeparam name="THashSetKey"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="tableIndex"></param>
        /// <returns>HashSet</returns>
        public HashSet<THashSetKey> SelectHashSet<TTableKey, THashSetKey>(TTableKey key, uint tableIndex)
        {
            HashSet<THashSetKey> output = new HashSet<THashSetKey>();
            foreach (var row in this.GetTable<TTableKey>(key, tableIndex).SelectForward<THashSetKey, byte[]>())
            {
                output.Add(row.Key);
            }
            return output;
        }

        /// <summary>
        /// Returns completely current nested-table as a HashSet (unique list of Keys).
        /// </summary>
        /// <typeparam name="THashSetKey"></typeparam>
        /// <returns></returns>
        public HashSet<THashSetKey> SelectHashSet<THashSetKey>()
        {
            HashSet<THashSetKey> output = new HashSet<THashSetKey>();
            foreach (var row in this.SelectForward<THashSetKey, byte[]>())
            {
                output.Add(row.Key);
            }
            return output;
        }


        #endregion

        /// <summary>
        /// Inserts or updates the key value starting from startIndex.
        /// <para>If there were no value before, value byte[] array till startindex wll be filled with byte[] {0}</para>
        /// <para>If value is smaller then startIndex, value will be expanded.</para> 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="startIndex"></param>
        /// <returns></returns>
        public NestedTable InsertPart<TKey, TValue>(TKey key, TValue value, uint startIndex)
        {
            byte[] refToInsertedValue = null;
            bool WasUpdated = false;
            return this.InsertPart(key, value, startIndex,out refToInsertedValue, out WasUpdated);
        }

        /// <summary>
        /// Inserts or updates the key value starting from startIndex.
        /// <para>If there were no value before, value byte[] array till startindex wll be filled with byte[] {0}</para>
        /// <para>If value is smaller then startIndex, value will be expanded.</para> 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="startIndex"></param>
        /// <param name="refToInsertedValue">returns ptr in the file to the value and key (always 8 bytes)</param>
        /// <returns></returns>
        public NestedTable InsertPart<TKey, TValue>(TKey key, TValue value, uint startIndex, out byte[] refToInsertedValue)
        {
            refToInsertedValue = null;
            bool WasUpdated = false;

            return this.InsertPart(key, value, startIndex, out refToInsertedValue, out WasUpdated);

        }

        /// <summary>
        /// Inserts or updates the key value starting from startIndex.
        /// <para>If there were no value before, value byte[] array till startindex wll be filled with byte[] {0}</para>
        /// <para>If value is smaller then startIndex, value will be expanded.</para> 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="startIndex"></param>
        /// <param name="refToInsertedValue">returns ptr in the file to the value and key (always 8 bytes)</param>
        /// <param name="WasUpdated">indicates that key we insert, already existed in the system and was updated</param>
        /// <returns></returns>
        public NestedTable InsertPart<TKey, TValue>(TKey key, TValue value, uint startIndex, out byte[] refToInsertedValue, out bool WasUpdated)
        {
            WasUpdated = false;
            refToInsertedValue = null;

            if (!this._tableExists)
                return new NestedTable(null, false, false);

            if (!_insertAllowed)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DBINTABLE_CHANGEDATA_FROMSELECTVIEW);
            }


            //Special check of null of nulls is integrated inside of the convertor
            //For keys and values different convertors.

            byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);
            byte[] btValue = DataTypesConvertor.ConvertValue<TValue>(value);

            long valueStartPtr = 0;
            refToInsertedValue = _tbl.table.AddPartially(ref btKey, ref btValue, startIndex, out valueStartPtr);

            if (refToInsertedValue != null)
                refToInsertedValue = refToInsertedValue.EnlargeByteArray_BigEndian(8);

            return this;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="key"></param>
        /// <returns></returns>
        public Row<TKey, TValue> Select<TKey, TValue>(TKey key)
        {
            return Select<TKey,TValue>(key, false);

        }

        /// <summary>
        /// TO be used in Select with ReadVisibilityScope as True. Is created only once per instantiated nested table. 
        /// </summary>
        LTrieRootNode readRootNode = null;

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="key"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, select will return key/value,</para>
        /// <para>like it was before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public Row<TKey, TValue> Select<TKey, TValue>(TKey key, bool AsReadVisibilityScope)
        {

            if (!this._tableExists)   //Returning default value
                return new Row<TKey, TValue>(null, null, false);
            //return new Row<TKey, TValue>(null, null, false, null, false);

            byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

#if NET35 || NETr40
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
#else
            bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != Environment.CurrentManagedThreadId);
#endif 

            if (AsReadVisibilityScope)
                useCache = true;

            LTrieRow row = null;
            if (useCache)
            {
                if (readRootNode == null)
                    readRootNode = new LTrieRootNode(_tbl.table);

                row = readRootNode.GetKey(btKey, true, this._valuesLazyLoadingIsOn);
            }
            else
                row = _tbl.table.GetKey(btKey, useCache, this._valuesLazyLoadingIsOn);

            //LTrieRow row = _tbl.table.GetKey(btKey, useCache);
            Row<TKey, TValue> rw = new Row<TKey, TValue>(row, _tbl._masterTrie, useCache);
            //Row<TKey, TValue> rw = new Row<TKey, TValue>(row._root, row.LinkToValue, row.Exists, row.Key, useCache);

            rw.nestedTable = this;

            return rw;

            //return new Row<TKey, TValue>(row._root, row.LinkToValue, row.Exists, row.Key, useCache);

        }



        /// <summary>
        /// <para>EXPERIMENTAL</para>
        /// Returns Row by supplying direct pointer to key/value in the file.
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="refToInsertedValue"></param>
        /// <returns></returns>
        public Row<TKey, TValue> SelectDirect<TKey, TValue>(byte[] refToInsertedValue)
        {

            if (!this._tableExists)   //Returning default value
                return new Row<TKey, TValue>(null, null, false);

            if (refToInsertedValue == null)
            {
                return new Row<TKey, TValue>(null, null, false);
            }
            else
            {
                //Bringing refToInsertedValue to the pointer size of the table
                refToInsertedValue = refToInsertedValue.RemoveLeadingElement(0).EnlargeByteArray_BigEndian(_tbl._masterTrie.Storage.TrieSettings.POINTER_LENGTH);
            }

#if NET35 || NETr40
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
#else
            bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != Environment.CurrentManagedThreadId);
#endif


            LTrieRow ltr = new LTrieRow(((useCache) ? _tbl.table.rn : new LTrieRootNode(_tbl.table)));
            ltr.Key = _tbl.table.Cache.ReadKey(useCache, refToInsertedValue);
            ltr.LinkToValue = refToInsertedValue;

            Row<TKey, TValue> rw =new Row<TKey, TValue>(ltr, _tbl._masterTrie, useCache);
            
            rw.nestedTable = this;

            return rw;
            
        }



        /// <summary>
        /// 
        /// </summary>
        public NestedTable RemoveAllKeys()
        {
            if (!this._tableExists)
                return new NestedTable(null, false, false);

            if (!_insertAllowed)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DBINTABLE_CHANGEDATA_FROMSELECTVIEW);
            }

            _tbl.RemoveAll();

            return this;
        }

        /// <summary>
        /// Removes a key
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="key"></param>
        public NestedTable RemoveKey<TKey>(TKey key)
        {
            bool WasRemoved = false;
            byte[] deletedValue = null;
            return RemoveKey(key, out WasRemoved, false, out deletedValue);

        }

        /// <summary>
        /// Removes a key
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="key"></param>
        /// <param name="WasRemoved">indicates that key existed in the system</param>
        public NestedTable RemoveKey<TKey>(TKey key, out bool WasRemoved)
        {
            byte[] deletedValue = null;
            return RemoveKey(key, out WasRemoved, false, out deletedValue);
        }

        /// <summary>
        /// Removes a key
        /// </summary>
        /// <typeparam name="TKey">type of the key</typeparam>
        /// <param name="key">key to delete</param>
        /// <param name="WasRemoved">indicates that key existed in the system</param>
        /// <param name="deletedValue">Will hold deleted value if WasRemoved is true</param>
        public NestedTable RemoveKey<TKey>(TKey key, out bool WasRemoved, out byte[] deletedValue)
        {
            return RemoveKey(key, out WasRemoved, true, out deletedValue);
        }

        /// <summary>
        /// Removes a key
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="key"></param>
        /// <param name="WasRemoved">indicates that key existed in the system</param>
        /// <param name="retrieveDeletedValue">indicates if system should retrieve deleted value</param>
        /// <param name="deletedValue"></param>
        private NestedTable RemoveKey<TKey>(TKey key, out bool WasRemoved, bool retrieveDeletedValue, out byte[] deletedValue)
        {
            WasRemoved = false;
            deletedValue = null;

            if (!this._tableExists)
                return new NestedTable(null, false, false);

            if (!_insertAllowed)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DBINTABLE_CHANGEDATA_FROMSELECTVIEW);
            }

            byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

            _tbl.table.Remove(ref btKey, out WasRemoved, retrieveDeletedValue, out deletedValue);

            return this;
        }




        /// <summary>
        /// Renames old key on the new one
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="oldKey"></param>
        /// <param name="newKey"></param>
        public NestedTable ChangeKey<TKey>(TKey oldKey, TKey newKey)
        {
            byte[] ptrToNewKey = null;
            bool WasChanged = false;
            return ChangeKey(oldKey, newKey, out ptrToNewKey, out WasChanged);
            
        }

        /// <summary>
        /// Renames old key on the new one
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="oldKey"></param>
        /// <param name="newKey"></param>
        /// <param name="ptrToNewKey">return pointer to the new value in the file (always 8 bytes)</param>
        /// <returns></returns>
        public NestedTable ChangeKey<TKey>(TKey oldKey, TKey newKey, out byte[] ptrToNewKey)
        {
            ptrToNewKey = null;
            bool WasChanged = false;
            return ChangeKey(oldKey, newKey, out ptrToNewKey, out WasChanged);
            
        }


        /// <summary>
        /// Renames old key on the new one
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="oldKey"></param>
        /// <param name="newKey"></param>
        /// <param name="ptrToNewKey">return pointer to the new value in the file (always 8 bytes)</param>
        /// <param name="WasChanged">indicates that oldKey existed and was succesfully changed</param>
        /// <returns></returns>
        public NestedTable ChangeKey<TKey>(TKey oldKey, TKey newKey, out byte[] ptrToNewKey, out bool WasChanged)
        {
            WasChanged = false;
            ptrToNewKey = null;

            if (!this._tableExists)
                return new NestedTable(null, false, false);

            if (!_insertAllowed)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DBINTABLE_CHANGEDATA_FROMSELECTVIEW);
            }

            byte[] btOldKey = DataTypesConvertor.ConvertKey<TKey>(oldKey);
            byte[] btNewKey = DataTypesConvertor.ConvertKey<TKey>(newKey);

            _tbl.table.ChangeKey(ref btOldKey, ref btNewKey, out ptrToNewKey,out WasChanged);

            return this;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public ulong Count()
        {
            if (!this._tableExists)
                return 0;

#if NET35 || NETr40
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
#else
            bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != Environment.CurrentManagedThreadId);
#endif 

            return _tbl.table.Count(useCache);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <returns></returns>
        public Row<TKey, TValue> Max<TKey, TValue>()
        {
            if (!this._tableExists)   //Returning default value
                return new Row<TKey, TValue>(null, null, false);
            //return new Row<TKey, TValue>(null, null, false, null, false);

#if NET35 || NETr40
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
#else
            bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != Environment.CurrentManagedThreadId);
#endif 

            LTrieRow row = _tbl.table.IterateBackwardForMaximal(useCache, false);

            Row<TKey, TValue> rw = new Row<TKey, TValue>(row, _tbl._masterTrie, useCache);
            //Row<TKey, TValue> rw = new Row<TKey, TValue>(row._root, row.LinkToValue, row.Exists, row.Key, useCache);

            rw.nestedTable = this;

            return rw;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <returns></returns>
        public Row<TKey, TValue> Min<TKey, TValue>()
        {
            if (!this._tableExists)   //Returning default value
                return new Row<TKey, TValue>(null, null, false);
            //return new Row<TKey, TValue>(null, null, false, null, false);

#if NET35 || NETr40
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
#else
            bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != Environment.CurrentManagedThreadId);
#endif 
            LTrieRow row = _tbl.table.IterateForwardForMinimal(useCache, false);

            Row<TKey, TValue> rw = new Row<TKey, TValue>(row, _tbl._masterTrie, useCache);
            //Row<TKey, TValue> rw = new Row<TKey, TValue>(row._root, row.LinkToValue, row.Exists, row.Key, useCache);

            rw.nestedTable = this;

            return rw;
        }


        #region "Fetch"

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForward<TKey, TValue>()
        {
            return SelectForward<TKey, TValue>(false);

        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForward<TKey, TValue>(bool AsReadVisibilityScope)
        {
            if (this._tableExists)
            {
#if NET35 || NETr40
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
#else
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != Environment.CurrentManagedThreadId);
#endif 

                if (AsReadVisibilityScope)
                    useCache = true;

                Row<TKey, TValue> rw = null;

                foreach (var xrow in _tbl.table.IterateForward(useCache, this._valuesLazyLoadingIsOn))
                {
                    rw = new Row<TKey, TValue>(xrow, _tbl._masterTrie, useCache);
                    //rw = new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, useCache);
                    rw.nestedTable = this;
                    yield return rw;
                }
            }
        }

       
        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackward<TKey, TValue>()
        {
            return SelectBackward<TKey, TValue>(false);

        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackward<TKey, TValue>(bool AsReadVisibilityScope)
        {
            if (this._tableExists)
            {
#if NET35 || NETr40
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
#else
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != Environment.CurrentManagedThreadId);
#endif 

                if (AsReadVisibilityScope)
                    useCache = true;

                Row<TKey, TValue> rw = null;

                foreach (var xrow in _tbl.table.IterateBackward(useCache, this._valuesLazyLoadingIsOn))
                {
                    rw = new Row<TKey, TValue>(xrow, _tbl._masterTrie, useCache);
                    //rw = new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, useCache);
                    rw.nestedTable = this;
                    yield return rw;
                }
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="key"></param>
        /// <param name="includeStartFromKey"></param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardStartFrom<TKey, TValue>(TKey key, bool includeStartFromKey)
        {
            return SelectForwardStartFrom<TKey, TValue>(key, includeStartFromKey, false);

        }


        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="key"></param>
        /// <param name="includeStartFromKey"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardStartFrom<TKey, TValue>(TKey key, bool includeStartFromKey, bool AsReadVisibilityScope)
        {
            if (this._tableExists)
            {
#if NET35 || NETr40
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
#else
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != Environment.CurrentManagedThreadId);
#endif 

                if (AsReadVisibilityScope)
                    useCache = true;
                
                Row<TKey, TValue> rw = null;
                byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

                foreach (var xrow in _tbl.table.IterateForwardStartFrom(btKey, includeStartFromKey, useCache, this._valuesLazyLoadingIsOn))
                {
                    rw = new Row<TKey, TValue>(xrow, _tbl._masterTrie, useCache);
                    //rw = new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, useCache);
                    rw.nestedTable = this;
                    yield return rw;
                }
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="key"></param>
        /// <param name="includeStartFromKey"></param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardStartFrom<TKey, TValue>(TKey key, bool includeStartFromKey)
        {
            return SelectBackwardStartFrom<TKey, TValue>(key, includeStartFromKey, false);
            
        }


        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="key"></param>
        /// <param name="includeStartFromKey"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardStartFrom<TKey, TValue>(TKey key, bool includeStartFromKey, bool AsReadVisibilityScope)
        {
            if (this._tableExists)
            {
#if NET35 || NETr40
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
#else
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != Environment.CurrentManagedThreadId);
#endif 

                if (AsReadVisibilityScope)
                    useCache = true;

                Row<TKey, TValue> rw = null;
                byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

                foreach (var xrow in _tbl.table.IterateBackwardStartFrom(btKey, includeStartFromKey, useCache, this._valuesLazyLoadingIsOn))
                {
                    rw = new Row<TKey, TValue>(xrow, _tbl._masterTrie, useCache);
                    //rw = new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, useCache);
                    rw.nestedTable = this;
                    yield return rw;
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="startKey"></param>
        /// <param name="includeStartKey"></param>
        /// <param name="stopKey"></param>
        /// <param name="includeStopKey"></param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardFromTo<TKey, TValue>(TKey startKey, bool includeStartKey, TKey stopKey, bool includeStopKey)
        {
            return SelectForwardFromTo<TKey, TValue>(startKey, includeStartKey, stopKey, includeStopKey, false);

        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="startKey"></param>
        /// <param name="includeStartKey"></param>
        /// <param name="stopKey"></param>
        /// <param name="includeStopKey"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardFromTo<TKey, TValue>(TKey startKey, bool includeStartKey, TKey stopKey, bool includeStopKey, bool AsReadVisibilityScope)
        {
            if (this._tableExists)
            {
#if NET35 || NETr40
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
#else
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != Environment.CurrentManagedThreadId);
#endif 

                if (AsReadVisibilityScope)
                    useCache = true;


                Row<TKey, TValue> rw = null;

                byte[] btStartKey = DataTypesConvertor.ConvertKey<TKey>(startKey);
                byte[] btStopKey = DataTypesConvertor.ConvertKey<TKey>(stopKey);

                foreach (var xrow in _tbl.table.IterateForwardFromTo(btStartKey, btStopKey, includeStartKey, includeStopKey, useCache, this._valuesLazyLoadingIsOn))
                {
                    rw = new Row<TKey, TValue>(xrow, _tbl._masterTrie, useCache);
                    //rw = new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, useCache);
                    rw.nestedTable = this;
                    yield return rw;
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="startKey"></param>
        /// <param name="includeStartKey"></param>
        /// <param name="stopKey"></param>
        /// <param name="includeStopKey"></param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardFromTo<TKey, TValue>(TKey startKey, bool includeStartKey, TKey stopKey, bool includeStopKey)
        {
            return SelectBackwardFromTo<TKey, TValue>(startKey, includeStartKey, stopKey, includeStopKey, false);
           
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="startKey"></param>
        /// <param name="includeStartKey"></param>
        /// <param name="stopKey"></param>
        /// <param name="includeStopKey"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardFromTo<TKey, TValue>(TKey startKey, bool includeStartKey, TKey stopKey, bool includeStopKey, bool AsReadVisibilityScope)
        {
            if (this._tableExists)
            {
#if NET35 || NETr40
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
#else
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != Environment.CurrentManagedThreadId);
#endif 

                if (AsReadVisibilityScope)
                    useCache = true;

                Row<TKey, TValue> rw = null;

                byte[] btStartKey = DataTypesConvertor.ConvertKey<TKey>(startKey);
                byte[] btStopKey = DataTypesConvertor.ConvertKey<TKey>(stopKey);

                foreach (var xrow in _tbl.table.IterateBackwardFromTo(btStartKey, btStopKey, includeStartKey, includeStopKey, useCache, this._valuesLazyLoadingIsOn))
                {
                    rw = new Row<TKey, TValue>(xrow, _tbl._masterTrie, useCache);
                    //rw = new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, useCache);
                    rw.nestedTable = this;
                    yield return rw;
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="startWithKeyPart"></param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardStartsWith<TKey, TValue>(TKey startWithKeyPart)
        {
            return SelectForwardStartsWith<TKey, TValue>(startWithKeyPart, false);
            
        }


        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="startWithKeyPart"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardStartsWith<TKey, TValue>(TKey startWithKeyPart, bool AsReadVisibilityScope)
        {
            if (this._tableExists)
            {
#if NET35 || NETr40
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
#else
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != Environment.CurrentManagedThreadId);
#endif 

                if (AsReadVisibilityScope)
                    useCache = true;

                Row<TKey, TValue> rw = null;

                byte[] btStartKey = DataTypesConvertor.ConvertKey<TKey>(startWithKeyPart);

                foreach (var xrow in _tbl.table.IterateForwardStartsWith(btStartKey, useCache, this._valuesLazyLoadingIsOn))
                {
                    rw = new Row<TKey, TValue>(xrow, _tbl._masterTrie, useCache);
                    //rw = new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, useCache);
                    rw.nestedTable = this;
                    yield return rw;
                }
            }
        }

        #region "SelectForwardStartsWithClosestToPrefix"

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
        /// <param name="startWithClosestPrefix"></param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardStartsWithClosestToPrefix<TKey, TValue>(TKey startWithClosestPrefix)
        {
            return SelectForwardStartsWithClosestToPrefix<TKey, TValue>(startWithClosestPrefix, false);
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
        /// <param name="startWithClosestPrefix"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardStartsWithClosestToPrefix<TKey, TValue>(TKey startWithClosestPrefix, bool AsReadVisibilityScope)
        {
            if (this._tableExists)
            {
#if NET35 || NETr40
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
#else 
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != Environment.CurrentManagedThreadId);
#endif 

                if (AsReadVisibilityScope)
                    useCache = true;

                Row<TKey, TValue> rw = null;

                byte[] btStartKey = DataTypesConvertor.ConvertKey<TKey>(startWithClosestPrefix);

                foreach (var xrow in _tbl.table.IterateForwardStartsWithClosestToPrefix(btStartKey, useCache, this._valuesLazyLoadingIsOn))
                {
                    rw = new Row<TKey, TValue>(xrow, _tbl._masterTrie, useCache);
                    //rw = new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, useCache);
                    rw.nestedTable = this;
                    yield return rw;
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
        /// <param name="startWithClosestPrefix"></param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardStartsWithClosestToPrefix<TKey, TValue>(TKey startWithClosestPrefix)
        {
            return SelectBackwardStartsWithClosestToPrefix<TKey, TValue>(startWithClosestPrefix, false);
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
        /// <param name="startWithClosestPrefix"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardStartsWithClosestToPrefix<TKey, TValue>(TKey startWithClosestPrefix, bool AsReadVisibilityScope)
        {
            if (this._tableExists)
            {
#if NET35 || NETr40
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
#else 
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != Environment.CurrentManagedThreadId);
#endif 

                if (AsReadVisibilityScope)
                    useCache = true;

                Row<TKey, TValue> rw = null;

                byte[] btStartKey = DataTypesConvertor.ConvertKey<TKey>(startWithClosestPrefix);

                foreach (var xrow in _tbl.table.IterateBackwardStartsWithClosestToPrefix(btStartKey, useCache, this._valuesLazyLoadingIsOn))
                {
                    rw = new Row<TKey, TValue>(xrow, _tbl._masterTrie, useCache);
                    //rw = new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, useCache);
                    rw.nestedTable = this;
                    yield return rw;
                }
            }
        }
        #endregion


        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="startWithKeyPart"></param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardStartsWith<TKey, TValue>(TKey startWithKeyPart)
        {

            return  SelectBackwardStartsWith<TKey, TValue>(startWithKeyPart, false);
            
        }


        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="startWithKeyPart"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardStartsWith<TKey, TValue>(TKey startWithKeyPart, bool AsReadVisibilityScope)
        {
            if (this._tableExists)
            {
#if NET35 || NETr40
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
#else
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != Environment.CurrentManagedThreadId);
#endif 

                if (AsReadVisibilityScope)
                    useCache = true;

                Row<TKey, TValue> rw = null;

                byte[] btStartKey = DataTypesConvertor.ConvertKey<TKey>(startWithKeyPart);

                foreach (var xrow in _tbl.table.IterateBackwardStartsWith(btStartKey, useCache, this._valuesLazyLoadingIsOn))
                {
                    rw = new Row<TKey, TValue>(xrow, _tbl._masterTrie, useCache);
                    //rw = new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, useCache);
                    rw.nestedTable = this;
                    yield return rw;
                }
            }
        }


        #endregion


        #region "Skip"

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="skippingQuantity"></param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardSkip<TKey, TValue>(ulong skippingQuantity)
        {
            return SelectForwardSkip<TKey, TValue>(skippingQuantity, false);
          
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="skippingQuantity"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardSkip<TKey, TValue>(ulong skippingQuantity, bool AsReadVisibilityScope)
        {
            if (this._tableExists)
            {
#if NET35 || NETr40
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
#else
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != Environment.CurrentManagedThreadId);
#endif 

                if (AsReadVisibilityScope)
                    useCache = true;

                Row<TKey, TValue> rw = null;


                foreach (var xrow in _tbl.table.IterateForwardSkip(skippingQuantity, useCache, this._valuesLazyLoadingIsOn))
                {
                    rw = new Row<TKey, TValue>(xrow, _tbl._masterTrie, useCache);
                    //rw = new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, useCache);
                    rw.nestedTable = this;
                    yield return rw;
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="skippingQuantity"></param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardSkip<TKey, TValue>(ulong skippingQuantity)
        {
            return SelectBackwardSkip<TKey, TValue>(skippingQuantity, false);          
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="skippingQuantity"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardSkip<TKey, TValue>(ulong skippingQuantity, bool AsReadVisibilityScope)
        {
            if (this._tableExists)
            {
#if NET35 || NETr40
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
#else
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != Environment.CurrentManagedThreadId);
#endif 

                if (AsReadVisibilityScope)
                    useCache = true;

                Row<TKey, TValue> rw = null;


                foreach (var xrow in _tbl.table.IterateBackwardSkip(skippingQuantity, useCache, this._valuesLazyLoadingIsOn))
                {
                    rw = new Row<TKey, TValue>(xrow, _tbl._masterTrie, useCache);
                    //rw = new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, useCache);
                    rw.nestedTable = this;
                    yield return rw;
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="key"></param>
        /// <param name="skippingQuantity"></param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardSkipFrom<TKey, TValue>(TKey key, ulong skippingQuantity)
        {
            return SelectForwardSkipFrom<TKey, TValue>(key, skippingQuantity, false);
            //if (this._tableExists)
            //{
            //    bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
            //    Row<TKey, TValue> rw = null;

            //    byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

            //    foreach (var xrow in _tbl.table.IterateForwardSkipFrom(btKey,skippingQuantity, useCache))
            //    {
            //        rw = new Row<TKey, TValue>(xrow, _tbl._masterTrie, useCache);
            //        //rw = new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, useCache);
            //        rw.nestedTable = this;
            //        yield return rw;
            //    }
            //}
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="key"></param>
        /// <param name="skippingQuantity"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectForwardSkipFrom<TKey, TValue>(TKey key, ulong skippingQuantity, bool AsReadVisibilityScope)
        {
            if (this._tableExists)
            {
#if NET35 || NETr40
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
#else
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != Environment.CurrentManagedThreadId);
#endif 

                if (AsReadVisibilityScope)
                    useCache = true;

                Row<TKey, TValue> rw = null;

                byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

                foreach (var xrow in _tbl.table.IterateForwardSkipFrom(btKey, skippingQuantity, useCache, this._valuesLazyLoadingIsOn))
                {
                    rw = new Row<TKey, TValue>(xrow, _tbl._masterTrie, useCache);
                    //rw = new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, useCache);
                    rw.nestedTable = this;
                    yield return rw;
                }
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="key"></param>
        /// <param name="skippingQuantity"></param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardSkipFrom<TKey, TValue>(TKey key, ulong skippingQuantity)
        {
            return SelectBackwardSkipFrom<TKey, TValue>(key, skippingQuantity, false);
            //if (this._tableExists)
            //{
            //    bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
            //    Row<TKey, TValue> rw = null;

            //    byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

            //    foreach (var xrow in _tbl.table.IterateBackwardSkipFrom(btKey, skippingQuantity, useCache))
            //    {
            //        rw = new Row<TKey, TValue>(xrow, _tbl._masterTrie, useCache);
            //        //rw = new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, useCache);
            //        rw.nestedTable = this;
            //        yield return rw;
            //    }
            //}
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="key"></param>
        /// <param name="skippingQuantity"></param>
        /// <param name="AsReadVisibilityScope">Metters only for transactions where this table is in modification list
        /// <para>(by SynchronizeTables or just insert, remove.. any key modification command).</para>
        /// <para>If this parameter set to true, enumerator will return key/value,</para>
        /// <para>like they were, before transaction started (and parallel reading threds can see it).</para>
        /// </param>
        /// <returns></returns>
        public IEnumerable<Row<TKey, TValue>> SelectBackwardSkipFrom<TKey, TValue>(TKey key, ulong skippingQuantity, bool AsReadVisibilityScope)
        {
            if (this._tableExists)
            {
#if NET35 || NETr40
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId);
#else 
                bool useCache = (_tbl._masterTrie.NestedTablesCoordinator.ModificationThreadId != Environment.CurrentManagedThreadId);
#endif 

                if (AsReadVisibilityScope)
                    useCache = true;

                Row<TKey, TValue> rw = null;

                byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

                foreach (var xrow in _tbl.table.IterateBackwardSkipFrom(btKey, skippingQuantity, useCache, this._valuesLazyLoadingIsOn))
                {
                    rw = new Row<TKey, TValue>(xrow, _tbl._masterTrie, useCache);
                    //rw = new Row<TKey, TValue>(xrow._root, xrow.LinkToValue, xrow.Exists, xrow.Key, useCache);
                    rw.nestedTable = this;
                    yield return rw;
                }
            }
        }
#endregion




       
    }
}
