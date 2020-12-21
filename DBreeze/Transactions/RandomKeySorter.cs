/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DBreeze.Utils;
using DBreeze.DataTypes;
using DBreeze.LianaTrie;

namespace DBreeze.Transactions
{
    /// <summary>
    /// Speeding up, space economy. Represents a mechanism helping to store entites into the memory, before insert or remove.
    /// When AutomaticFlushLimitQuantityPerTable per table (default 10000) is exceed or 
    /// within Commit command, all entites will be flushed (first removed then inserted) on the disk 
    /// sorted by key ascending
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
        /// <para>NOT USED ANYMORE</para>
        /// Value indicating when content should be cleared. Default is 1000000 (inserts and removes)
        /// <para>Getting rid of Automatic flush, that makes inefficient TryGetValueByKey after flushing (will need always to check HD if not found in sorter)</para> 
        /// </summary>
        public int AutomaticFlushLimitQuantityPerTable = 1000000;
               
        HashSet<string> _tablesWithOverwriteIsNotAllowed = new HashSet<string>();

        /// <summary>
        /// Internal regulator telling, that specified tables should work via fast update
        /// </summary>
        internal void TablesWithOverwriteIsNotAllowed(string tableName)
        {
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
            if (key == null)
                throw new Exception("RandomKeySorter, key can't be null");

            byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);
            byte[] btValue = DataTypesConvertor.ConvertKey<TValue>(value);

            var keyH = btKey.ToBytesString();

            isUsed = true;
                     
            if (!_cnt.ContainsKey(tableName))
                _cnt[tableName] = 0;

                if (!_dInsert.ContainsKey(tableName))
                _dInsert[tableName] = new Dictionary<string, KeyValuePair<byte[], byte[]>>();            

            _dInsert[tableName][keyH] = new KeyValuePair<byte[], byte[]>(btKey, btValue);

            _cnt[tableName]++;

            //Getting rid of Automatic flush, that makes inefficient TryGetValueByKey after flushing (will need always to check HD if not found in sorter) 
            //if (_cnt[tableName] >= AutomaticFlushLimitQuantityPerTable)
            //    Flush(tableName);
        }

        /// <summary>
        /// Removes from the table key
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="tableName"></param>
        /// <param name="key"></param>
        public void Remove<TKey>(string tableName, TKey key)
        {
            if (key == null)
                throw new Exception("RandomKeySorter, key can't be null");

            byte[] btKey = DataTypesConvertor.ConvertKey<TKey>(key);

            var keyH = btKey.ToBytesString();

            isUsed = true;

            if (!_cnt.ContainsKey(tableName))
                _cnt[tableName] = 0;

            Dictionary<string, KeyValuePair<byte[], byte[]>> dInsertTable = null;
            if (_dInsert.TryGetValue(tableName, out dInsertTable))
            {
                //Trying already to remove these values
                //NOTE
                if (dInsertTable.ContainsKey(keyH))
                {
                    dInsertTable.Remove(keyH);
                    _cnt[tableName]--;

                    if(!WasAutomaticallyFlushed)
                        return; //we can go out and don't put this to _remove list if in this session we entites were not flushed due to AutomaticFlushLimitQuantityPerTable, unless we must leave them in _dRemove table, so they are deleted when using Flush()
                }
            }


            if (!_dRemove.ContainsKey(tableName))
                _dRemove[tableName] = new Dictionary<string, byte[]>();

            _dRemove[tableName][keyH] = btKey;

           

            _cnt[tableName]++;

            //Getting rid of Automatic flush, that makes inefficient TryGetValueByKey after flushing (will need always to check HD if not found in sorter) 
            //if (_cnt[tableName] >= AutomaticFlushLimitQuantityPerTable)
            //    Flush(tableName);
        }



        /// <summary>
        /// Contains writing LTrie tables
        /// </summary>
        Dictionary<string, LTrie> tbls = new Dictionary<string, LTrie>();
        bool WasAutomaticallyFlushed = false;
        
        public void Flush(string tableName)
        {
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

                foreach (var el2 in _dRemove[tableName].OrderBy(r => r.Key))
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

                foreach (var el2 in _dInsert[tableName].OrderBy(r => r.Key))
                {
                    //_t.Insert<byte[],byte[]>(tableName, el2.Value.Key, el2.Value.Value);
                    btKey = el2.Value.Key;
                    btVal = el2.Value.Value;
                    table.Add(ref btKey, ref btVal, out WasOperated, false);
                }
                _dInsert[tableName].Clear();
                _dInsert.Remove(tableName);
            }

            _cnt[tableName] = 0;
            WasAutomaticallyFlushed = true;
        }

      
        /// <summary>
        /// Flushing all 
        /// </summary>
        public void Flush()
        {
            if (!isUsed)
                return;

            LTrie table = null;
            bool WasOperated = false;
            byte[] deletedValue = null;
            byte[] btKey = null;
            byte[] btVal = null;

            foreach (var el1 in _dRemove.OrderBy(r => r.Key))
            {
                if (!tbls.TryGetValue(el1.Key, out table))
                {
                    table = _t.GetWriteTableFromBuffer(el1.Key);
                    tbls[el1.Key] = table;
                }

                foreach (var el2 in el1.Value.OrderBy(r => r.Key))
                {
                    //_t.RemoveKey<byte[]>(el1.Key, el2.Value);
                    btKey = el2.Value;
                    table.Remove(ref btKey, out WasOperated, false, out deletedValue);
                }
            }

            _dRemove.Clear();

            foreach (var el1 in _dInsert.OrderBy(r => r.Key))
            {
                if (!tbls.TryGetValue(el1.Key, out table))
                {
                    table = _t.GetWriteTableFromBuffer(el1.Key);
                    tbls[el1.Key] = table;
                }

                //List<string> tt = el1.Value.OrderBy(r => r.Key).Select(r => r.Key).ToList();
                foreach (var el2 in el1.Value.OrderBy(r => r.Key))
                {
                    //_t.Insert<byte[], byte[]>(el1.Key, el2.Value.Key, el2.Value.Value);
                    btKey = el2.Value.Key;
                    btVal = el2.Value.Value;
                    table.Add(ref btKey, ref btVal, out WasOperated, false);
                    
                }
            }

            _dInsert.Clear();
            _cnt.Clear();

            WasAutomaticallyFlushed = false;

            isUsed = false;
        }

    }
}
