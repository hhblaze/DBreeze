/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using DBreeze.Utils;
using DBreeze.DataTypes;
using DBreeze.LianaTrie;

namespace DBreeze.Transactions
{
    /// <summary>
    /// Speeding up, space economy. Represents a mechanism helping to store entites into the memory, before insert or remove.
    /// Operations are flushed explicitly or by Commit (first removed then inserted),
    /// sorted by key ascending.
    /// </summary>
    public class RandomKeySorter
    {
        //Key is a table name, Value is Inserting/removing Key as Hex, Value is insert candidate
        Dictionary<string, Dictionary<string, KeyValuePair<byte[], byte[]>>> _dInsert = new Dictionary<string, Dictionary<string, KeyValuePair<byte[], byte[]>>>();
        Dictionary<string, Dictionary<string, byte[]>> _dRemove = new Dictionary<string, Dictionary<string, byte[]>>();

        Dictionary<string, int> _cnt = new Dictionary<string, int>();

        internal Transaction _t = null;
        
        bool isUsed = false;
        /// <summary>
        /// NOT USED ANYMORE. Preserved for public API compatibility.
        /// Automatic flush makes TryGetValueByKey ambiguous after flushing: a miss can mean
        /// either "not buffered" or "already flushed", forcing an LTrie/storage lookup and
        /// breaking the object-layer fast path.
        /// </summary>
        public int AutomaticFlushLimitQuantityPerTable = 1000000;
               
        HashSet<string> _tablesWithOverwriteIsNotAllowed = new HashSet<string>();

        /// <summary>
        /// Internal regulator telling, that specified tables should work via fast update
        /// </summary>
        internal void TablesWithOverwriteIsNotAllowed(string tableName)
        {
            _t.EnsureTransactionOwner();

            if (!_tablesWithOverwriteIsNotAllowed.Contains(tableName))
            {
                _tablesWithOverwriteIsNotAllowed.Add(tableName);
                _t.Technical_SetTable_OverwriteIsNotAllowed(tableName);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        internal byte[] TryGetValueByKey(string tableName, string key)
        {
            if (!_dInsert.ContainsKey(tableName))
                return null;

            KeyValuePair<byte[], byte[]> kvp;

            if (!_dInsert[tableName].TryGetValue(key, out kvp))
                return null;

            return kvp.Value;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>        
        public void Insert<TKey,TValue>(string tableName, TKey key, TValue value)
        {
            _t.EnsureTransactionOwner();

            if (key == null)
                throw new Exception("RandomKeySorter, key can't be null");

            byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);
            byte[] btValue = DataTypesConvertor.ConvertValue<TValue>(value);

            var keyH = btKey.ToBytesString();

            isUsed = true;
                     
            int count;
            if (!_cnt.TryGetValue(tableName, out count))
                count = 0;

            Dictionary<string, byte[]> removeTable;
            bool replacedRemove = _dRemove.TryGetValue(tableName, out removeTable) && removeTable.Remove(keyH);
            if (removeTable != null && removeTable.Count == 0)
                _dRemove.Remove(tableName);

            Dictionary<string, KeyValuePair<byte[], byte[]>> insertTable;
            if (!_dInsert.TryGetValue(tableName, out insertTable))
            {
                insertTable = new Dictionary<string, KeyValuePair<byte[], byte[]>>();
                _dInsert[tableName] = insertTable;
            }

            bool replacedInsert = insertTable.ContainsKey(keyH);
            insertTable[keyH] = new KeyValuePair<byte[], byte[]>(btKey, btValue);

            if (!replacedRemove && !replacedInsert)
                count++;
            _cnt[tableName] = count;

        }

        /// <summary>
        /// Removes from the table key
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        public void Remove<TKey>(string tableName, TKey key)
        {
            _t.EnsureTransactionOwner();

            if (key == null)
                throw new Exception("RandomKeySorter, key can't be null");

            byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

            var keyH = btKey.ToBytesString();

            isUsed = true;

            int count;
            if (!_cnt.TryGetValue(tableName, out count))
                count = 0;

            Dictionary<string, KeyValuePair<byte[], byte[]>> dInsertTable = null;
            bool replacedInsert = false;
            if (_dInsert.TryGetValue(tableName, out dInsertTable))
            {
                replacedInsert = dInsertTable.Remove(keyH);
                if (dInsertTable.Count == 0)
                    _dInsert.Remove(tableName);
            }

            Dictionary<string, byte[]> removeTable;
            if (!_dRemove.TryGetValue(tableName, out removeTable))
            {
                removeTable = new Dictionary<string, byte[]>();
                _dRemove[tableName] = removeTable;
            }

            bool replacedRemove = removeTable.ContainsKey(keyH);
            removeTable[keyH] = btKey;

            if (!replacedInsert && !replacedRemove)
                count++;
            _cnt[tableName] = count;

        }



        /// <summary>
        /// Contains writing LTrie tables
        /// </summary>
        Dictionary<string, LTrie> tbls = new Dictionary<string, LTrie>();

        public void Flush(string tableName)
        {
            _t.EnsureTransactionOwner();

            LTrie table = null;
            bool WasOperated = false;
            byte[] deletedValue = null;
            byte[] btKey = null;
            byte[] btVal = null;

            if (_dRemove.ContainsKey(tableName))
            {
                if (!tbls.TryGetValue(tableName, out table))
                {
                    table = _t.GetWriteTableFromBuffer(tableName);
                    tbls[tableName] = table;
                }

                foreach (var el2 in _dRemove[tableName].OrderBy(r => r.Key, StringComparer.Ordinal))
                {
                    // _t.RemoveKey<byte[]>(tableName, el2.Value);
                    btKey = el2.Value;
                    table.Remove(ref btKey, out WasOperated, false, out deletedValue);
                }

                _dRemove[tableName].Clear();
                _dRemove.Remove(tableName);
            }

            if (_dInsert.ContainsKey(tableName))
            {
                if (!tbls.TryGetValue(tableName, out table))
                {
                    table = _t.GetWriteTableFromBuffer(tableName);
                    tbls[tableName] = table;
                }

                foreach (var el2 in _dInsert[tableName].OrderBy(r => r.Key, StringComparer.Ordinal))
                {
                    //_t.Insert<byte[],byte[]>(tableName, el2.Value.Key, el2.Value.Value);
                    btKey = el2.Value.Key;
                    btVal = el2.Value.Value;
                    table.Add(ref btKey, ref btVal, out WasOperated, false);
                }
                _dInsert[tableName].Clear();
                _dInsert.Remove(tableName);
            }

            _cnt.Remove(tableName);
        }

      
        /// <summary>
        /// Flushing all 
        /// </summary>
        public void Flush()
        {
            _t.EnsureTransactionOwner();

            if (!isUsed)
                return;

            LTrie table = null;
            bool WasOperated = false;
            byte[] deletedValue = null;
            byte[] btKey = null;
            byte[] btVal = null;

            foreach (var el1 in _dRemove.OrderBy(r => r.Key, StringComparer.Ordinal))
            {
                if (!tbls.TryGetValue(el1.Key, out table))
                {
                    table = _t.GetWriteTableFromBuffer(el1.Key);
                    tbls[el1.Key] = table;
                }

                foreach (var el2 in el1.Value.OrderBy(r => r.Key, StringComparer.Ordinal))
                {
                    //_t.RemoveKey<byte[]>(el1.Key, el2.Value);
                    btKey = el2.Value;
                    table.Remove(ref btKey, out WasOperated, false, out deletedValue);
                }
            }

            _dRemove.Clear();

            foreach (var el1 in _dInsert.OrderBy(r => r.Key, StringComparer.Ordinal))
            {
                if (!tbls.TryGetValue(el1.Key, out table))
                {
                    table = _t.GetWriteTableFromBuffer(el1.Key);
                    tbls[el1.Key] = table;
                }

                //List<string> tt = el1.Value.OrderBy(r => r.Key).Select(r => r.Key).ToList();
                foreach (var el2 in el1.Value.OrderBy(r => r.Key, StringComparer.Ordinal))
                {
                    //_t.Insert<byte[], byte[]>(el1.Key, el2.Value.Key, el2.Value.Value);
                    btKey = el2.Value.Key;
                    btVal = el2.Value.Value;
                    table.Add(ref btKey, ref btVal, out WasOperated, false);
                    
                }
            }

            _dInsert.Clear();
            _cnt.Clear();

            isUsed = false;
        }

        /// <summary>
        /// Drops all operations which have not been committed. Called by Transaction.Rollback.
        /// </summary>
        internal void Reset()
        {
            _dInsert.Clear();
            _dRemove.Clear();
            _cnt.Clear();
            tbls.Clear();
            _tablesWithOverwriteIsNotAllowed.Clear();
            isUsed = false;
        }

    }
}
