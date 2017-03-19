using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DBreeze.Utils;
using DBreeze.DataTypes;

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
        /// Value indicating when content should be cleared. Default is 10000 (inserts and removes)
        /// </summary>
        public int AutomaticFlushLimitQuantityPerTable = 10000;

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

            if (_cnt[tableName] >= AutomaticFlushLimitQuantityPerTable)
                Flush(tableName);
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

            if (!_dRemove.ContainsKey(tableName))
                _dRemove[tableName] = new Dictionary<string, byte[]>();

            _dRemove[tableName][keyH] = btKey;

            _cnt[tableName]++;

            if (_cnt[tableName] >= AutomaticFlushLimitQuantityPerTable)
                Flush(tableName);
        }

        public void Flush(string tableName)
        {
            if (_dRemove.ContainsKey(tableName))
            {
                foreach (var el2 in _dRemove[tableName].OrderBy(r => r.Key))
                {                    
                    _t.RemoveKey<byte[]>(tableName, el2.Value);
                }

                _dRemove[tableName].Clear();
                _dRemove.Remove(tableName);
            }

            if (_dInsert.ContainsKey(tableName))
            {
                foreach (var el2 in _dInsert[tableName].OrderBy(r => r.Key))
                {
                    _t.Insert<byte[],byte[]>(tableName, el2.Value.Key, el2.Value.Value);
                }
                _dInsert[tableName].Clear();
                _dInsert.Remove(tableName);
            }

            _cnt[tableName] = 0;
        }

        /// <summary>
        /// Flushing all 
        /// </summary>
        public void Flush()
        {
            if (!isUsed)
                return;

            foreach (var el1 in _dRemove.OrderBy(r => r.Key))
            {
                foreach (var el2 in el1.Value.OrderBy(r => r.Key))
                {
                    _t.RemoveKey<byte[]>(el1.Key, el2.Value);
                }
            }

            _dRemove.Clear();

            foreach (var el1 in _dInsert.OrderBy(r => r.Key))
            {
                foreach (var el2 in el1.Value.OrderBy(r => r.Key))
                {
                    _t.Insert<byte[], byte[]>(el1.Key, el2.Value.Key, el2.Value.Value);
                }
            }

            _dInsert.Clear();
            _cnt.Clear();

            isUsed = false;
        }

    }
}
