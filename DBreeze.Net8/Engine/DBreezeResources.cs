/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.  
*/
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.IO;

using DBreeze.LianaTrie;
using DBreeze.Storage;
using DBreeze.Utils;
using DBreeze.DataTypes;
using DBreeze.Exceptions;

namespace DBreeze
{
    /// <summary>
    /// DBreeze resources represents an In-Memory dictionary synchronized with an internal DBreeze table. 
    /// Key is a string, Value any standard DBreeze.DataType (or serialized object, when custom serializer is supplied).
    /// Can be called from anywhere, even from other transactions. There is no need to add into sync table
    /// </summary>
    public class DBreezeResources : IDisposable
    {
        DBreezeEngine DBreezeEngine = null;
        TrieSettings LTrieSettings = null;
        IStorage Storage = null;
        LTrie LTrie = null;
        static string TableFileName = "_DBreezeResources";
        int disposed = 0;

        // ConcurrentDictionary replaces Dictionary + ReaderWriterLockSlim for high-concurrency performance
        ConcurrentDictionary<string, byte[]> _d = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);

        // ConcurrentDictionary does not accept null values, so we use a sentinel array to represent a cached 'null'
        private static readonly byte[] NullSentinel = [];

        // Dedicated lock only used to serialize DB writes and protect LTrie property mutations
        private readonly object _dbWriteLock = new object();

        Settings _defaultSetting = new Settings();

        /// <summary>
        /// UserResourcePrefix. Having prefixes gives us ability to reuse the table for smth. else
        /// </summary>
        const string _urp = "u";

        /// <summary>
        /// constructor
        /// </summary>
        /// <param name="engine"></param>
        internal DBreezeResources(DBreezeEngine engine)
        {
            this.DBreezeEngine = engine;
            LTrieSettings = new TrieSettings()
            {
                InternalTable = true
            };
            Storage = new StorageLayer(Path.Combine(engine.MainFolder, TableFileName), LTrieSettings, engine.Configuration);
            LTrie = new LTrie(Storage);
            LTrie.TableName = "DBreezeResources";
        }

        /// <summary>
        /// Disposing
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref disposed, 1, 0) != 0)
                return;

            LTrie?.Dispose();
        }

        /// <summary>
        /// Settings regulating resources behaviour
        /// </summary>
        public class Settings
        {
            public Settings()
            {
                HoldInMemory = true;
                HoldOnDisk = true;
                FastUpdates = false;
                InsertWithVerification = true;
                SortingAscending = true;
            }

            /// <summary>
            /// Resource will be stored in-memory, for the fast access. Default is true
            /// </summary>
            public bool HoldInMemory { get; set; }

            /// <summary>
            /// Resource will be stored on-disk. Default is true
            /// </summary>
            public bool HoldOnDisk { get; set; }

            /// <summary>            
            /// Sets OverWriteIsAllowed = false. Toggle only if it's not enough the speed of the update. Default is false.
            /// </summary>
            public bool FastUpdates { get; set; }

            /// <summary>
            /// Prevents disk insert of the identical value of the existing key. Default is true.
            /// </summary>
            public bool InsertWithVerification { get; set; }

            /// <summary>
            /// Needed for getting resources via SelectStartsWith. Default is true.
            /// </summary>
            public bool SortingAscending { get; set; }
        }

        /// <summary>
        /// Insert resource
        /// </summary>
        public void Insert<TValue>(string resourceName, TValue resourceObject, Settings resourceSettings = null)
        {
            if (string.IsNullOrEmpty(resourceName))
                return;

            resourceSettings ??= _defaultSetting;

            string rn = _urp + resourceName;

            byte[] btKey = DataTypesConvertor.ConvertKey<string>(rn);
            byte[] btValue = DataTypesConvertor.ConvertValue<TValue>(resourceObject);

            try
            {
                // ------- Verification, to prevent storing of the identical value
                if (resourceSettings.InsertWithVerification)
                {
                    if (_d.TryGetValue(rn, out byte[] cachedVal))
                    {
                        byte[] btExVal = ReferenceEquals(cachedVal, NullSentinel) ? null : cachedVal;

                        if (btExVal == null && btValue == null) return;
                        if (btExVal != null && btExVal._ByteArrayEquals(btValue)) return;
                    }
                    else
                    {
                        // Grabbing from disk
                        if (resourceSettings.HoldOnDisk)
                        {
                            var row = LTrie.GetKey(btKey, false, false);
                            if (row.Exists)
                            {
                                byte[] btExVal = row.GetFullValue(false);

                                bool isEqual = (btExVal == null && btValue == null) ||
                                               (btExVal != null && btExVal._ByteArrayEquals(btValue));

                                if (isEqual)
                                {
                                    if (resourceSettings.HoldInMemory)
                                        _d[rn] = btValue ?? NullSentinel;

                                    return;
                                }
                            }
                        }
                    }
                }
                // ------- 

                if (resourceSettings.HoldOnDisk)
                {
                    // Narrow lock to serialize only disk modifications & protect DBreeze engine properties
                    lock (_dbWriteLock)
                    {
                        bool cov = LTrie.OverWriteIsAllowed;
                        if (resourceSettings.FastUpdates)
                            LTrie.OverWriteIsAllowed = false;

                        LTrie.Add(btKey, btValue);
                        LTrie.Commit();

                        if (resourceSettings.FastUpdates)
                            LTrie.OverWriteIsAllowed = cov;
                    }
                }

                if (resourceSettings.HoldInMemory)
                    _d[rn] = btValue ?? NullSentinel;
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DBREEZE_RESOURCES_CONCERNING, "in Insert", ex);
            }
        }

        /// <summary>
        /// Batch insert of resources where value is a defined DBreeze or DBreeze.CustomSerializer type
        /// </summary>
        public void Insert<TValue>(IDictionary<string, TValue> resources, Settings resourceSettings = null)
        {
            this.Insert(resources.ToDictionary(r => r.Key, r => DataTypesConvertor.ConvertValue<TValue>(r.Value)), resourceSettings);
        }

        /// <summary>
        /// Batch insert of resources where value is a byte[]
        /// </summary>
        public void Insert(IDictionary<string, byte[]> resources, Settings resourceSettings = null)
        {
            if (resources == null || resources.Count < 1)
                return;

            resourceSettings ??= _defaultSetting;

            try
            {
                lock (_dbWriteLock)
                {
                    bool cov = LTrie.OverWriteIsAllowed;
                    if (resourceSettings.HoldOnDisk && resourceSettings.FastUpdates)
                        LTrie.OverWriteIsAllowed = false;

                    foreach (var rs in resources.OrderBy(r => r.Key))
                    {
                        if (string.IsNullOrEmpty(rs.Key))
                            continue;

                        string rn = _urp + rs.Key;

                        // ------- Verification, to prevent storing of the identical value
                        if (resourceSettings.InsertWithVerification)
                        {
                            if (_d.TryGetValue(rn, out byte[] cachedVal))
                            {
                                byte[] btExVal = ReferenceEquals(cachedVal, NullSentinel) ? null : cachedVal;

                                if (btExVal == null && rs.Value == null) continue;
                                if (btExVal != null && btExVal._ByteArrayEquals(rs.Value)) continue;
                            }
                            else if (resourceSettings.HoldOnDisk)
                            {
                                // Safe to read from disk concurrently, but since we are writing batches, 
                                // doing it inside the lock is standard for transactional scope.
                                byte[] tempBtKey = DataTypesConvertor.ConvertKey<string>(rn);
                                var row = LTrie.GetKey(tempBtKey, false, false);
                                if (row.Exists)
                                {
                                    byte[] btExVal = row.GetFullValue(false);

                                    bool isEqual = (btExVal == null && rs.Value == null) ||
                                                   (btExVal != null && btExVal._ByteArrayEquals(rs.Value));

                                    if (isEqual)
                                    {
                                        if (resourceSettings.HoldInMemory)
                                            _d[rn] = rs.Value ?? NullSentinel;

                                        continue;
                                    }
                                }
                            }
                        }
                        // ------- 

                        if (resourceSettings.HoldInMemory)
                            _d[rn] = rs.Value ?? NullSentinel;

                        if (resourceSettings.HoldOnDisk)
                        {
                            byte[] btKey = DataTypesConvertor.ConvertKey<string>(rn);
                            LTrie.Add(btKey, rs.Value);
                        }
                    }

                    if (resourceSettings.HoldOnDisk)
                    {
                        if (resourceSettings.FastUpdates)
                            LTrie.OverWriteIsAllowed = cov;

                        LTrie.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DBREEZE_RESOURCES_CONCERNING, "in Insert batch", ex);
            }
        }

        /// <summary>
        /// Removes resources from database and In-Memory dictionary 
        /// </summary>
        public void Remove(IList<string> resourcesNames)
        {
            if (resourcesNames == null || resourcesNames.Count == 0)
                return;

            try
            {
                lock (_dbWriteLock)
                {
                    foreach (var rs in resourcesNames)
                    {
                        if (string.IsNullOrEmpty(rs))
                            continue;

                        string rn = _urp + rs;
                        _d.TryRemove(rn, out _);

                        byte[] btKey = DataTypesConvertor.ConvertKey<string>(rn);
                        LTrie.Remove(ref btKey);
                    }

                    LTrie.Commit();
                }
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DBREEZE_RESOURCES_CONCERNING, "in Remove batch", ex);
            }
        }

        /// <summary>
        /// Removes resource from database and 
        /// </summary>        
        public void Remove(string resourceName)
        {
            if (string.IsNullOrEmpty(resourceName))
                return;

            string rn = _urp + resourceName;
            byte[] btKey = DataTypesConvertor.ConvertKey<string>(rn);

            try
            {
                _d.TryRemove(rn, out _);

                lock (_dbWriteLock)
                {
                    LTrie.Remove(ref btKey);
                    LTrie.Commit();
                }
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DBREEZE_RESOURCES_CONCERNING, "in Remove", ex);
            }
        }

        /// <summary>
        /// SelectStartsWith.
        /// Value instance, when byte[], must stay immutable, please use Dbreeze.Utils.CloneArray
        /// </summary>
        public IEnumerable<KeyValuePair<string, TValue>> SelectStartsWith<TValue>(string resourceNameStartsWith, Settings resourceSettings = null)
        {
            if (string.IsNullOrEmpty(resourceNameStartsWith))
                yield break;

            resourceSettings ??= _defaultSetting;
            byte[] btKey = DataTypesConvertor.ConvertKey<string>(_urp + resourceNameStartsWith);

            // Fetch iterator
            var q = LTrie.IterateForwardStartsWith(btKey, true, false);
            if (!resourceSettings.SortingAscending)
                q = LTrie.IterateBackwardStartsWith(btKey, true, false);

            // Materialize here to instantly release DBreeze iterators/handlers and get rid of the yield anti-pattern 
            var materialized = q.Select(el => (Key: el.Key.UTF8_GetString(), Val: el.GetFullValue(false))).ToList();

            foreach (var el in materialized)
            {
                string rn = el.Key;
                byte[] val = el.Val;

                if (!_d.TryGetValue(rn, out byte[] cachedVal))
                {
                    if (resourceSettings.HoldInMemory)
                    {
                        _d[rn] = val ?? NullSentinel;
                    }
                }
                else
                {
                    val = ReferenceEquals(cachedVal, NullSentinel) ? null : cachedVal;
                }

                yield return new KeyValuePair<string, TValue>(
                    rn.Substring(1),
                    val == null ? default : DataTypesConvertor.ConvertBack<TValue>(val)
                );
            }
        }

        /// <summary>
        /// Gets resources of the same type as a batch from memory or database (if not yet loaded).
        /// Value instance, when byte[], must stay immutable, please use Dbreeze.Utils.CloneArray
        /// </summary>
        public IDictionary<string, TValue> Select<TValue>(IList<string> resourcesNames, Settings resourceSettings = null)
        {
            var ret = new Dictionary<string, TValue>();
            if (resourcesNames == null || resourcesNames.Count < 1)
                return ret;

            resourceSettings ??= _defaultSetting;

            try
            {
                foreach (var rsn in resourcesNames.OrderBy(r => r))
                {
                    if (string.IsNullOrEmpty(rsn))
                        continue;

                    string rn = _urp + rsn;

                    if (_d.TryGetValue(rn, out byte[] cachedVal))
                    {
                        byte[] val = ReferenceEquals(cachedVal, NullSentinel) ? null : cachedVal;
                        ret[rsn] = val == null ? default : DataTypesConvertor.ConvertBack<TValue>(val);
                        continue;
                    }

                    // Value is not found in memory, try DB
                    byte[] btKey = DataTypesConvertor.ConvertKey<string>(rn);
                    var row = LTrie.GetKey(btKey, false, false);

                    if (row.Exists)
                    {
                        byte[] val = row.GetFullValue(false);
                        if (resourceSettings.HoldInMemory)
                            _d[rn] = val ?? NullSentinel;

                        ret[rsn] = val == null ? default : DataTypesConvertor.ConvertBack<TValue>(val);
                    }
                    else
                    {
                        if (resourceSettings.HoldInMemory)
                            _d[rn] = NullSentinel;

                        ret[rsn] = default;
                    }
                }
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DBREEZE_RESOURCES_CONCERNING, "in Select 2", ex);
            }

            return ret;
        }

        /// <summary>
        /// Gets resource from memory or database (if not yet loaded)
        /// Value instance, when byte[], must stay immutable, please use Dbreeze.Utils.CloneArray
        /// </summary>
        public TValue Select<TValue>(string resourceName, Settings resourceSettings = null)
        {
            if (string.IsNullOrEmpty(resourceName))
                return default;

            resourceSettings ??= _defaultSetting;

            string rn = _urp + resourceName;

            try
            {
                if (_d.TryGetValue(rn, out byte[] cachedVal))
                {
                    byte[] val = ReferenceEquals(cachedVal, NullSentinel) ? null : cachedVal;
                    return val == null ? default : DataTypesConvertor.ConvertBack<TValue>(val);
                }

                // Value is not found in memory, try DB
                byte[] btKey = DataTypesConvertor.ConvertKey<string>(rn);
                var row = LTrie.GetKey(btKey, false, false);

                if (row.Exists)
                {
                    byte[] val = row.GetFullValue(false);
                    if (resourceSettings.HoldInMemory)
                        _d[rn] = val ?? NullSentinel;

                    return val == null ? default : DataTypesConvertor.ConvertBack<TValue>(val);
                }
                else
                {
                    if (resourceSettings.HoldInMemory)
                        _d[rn] = NullSentinel;

                    return default;
                }
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DBREEZE_RESOURCES_CONCERNING, "in Select 2", ex);
            }
        }

    } // eo class
}