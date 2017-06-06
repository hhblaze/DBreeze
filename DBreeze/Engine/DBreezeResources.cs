/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
//using System.Threading.Tasks;
using System.IO;

using DBreeze.LianaTrie;
using DBreeze.Storage;
using DBreeze.Utils;
using DBreeze.DataTypes;
using DBreeze.Exceptions;

using System.Threading;

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
        long init = DateTime.UtcNow.Ticks;        
        int disposed = 0;

        Dictionary<string, byte[]> _d = new Dictionary<string, byte[]>();
        ReaderWriterLockSlim _sync = new ReaderWriterLockSlim();

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
            if (System.Threading.Interlocked.CompareExchange(ref disposed, 1, 0) != 0)
                return;

            if (LTrie != null)
            {
                LTrie.Dispose();
            }
        }

        /// <summary>
        /// Settings regulating resources behaviour
        /// </summary>
        public class Settings
        {
            /// <summary>
            /// Constructor
            /// </summary>
            public Settings()
            {
                HoldInMemory = true;
                HoldOnDisk = true;
                FastUpdates = false;
                InsertWithVerification = true;
                SortingAscending = true;
            }
            /// <summary>
            /// Resource will be stored in-memory, for the fast access.
            /// Default is true
            /// </summary>
            public bool HoldInMemory { get; set; }

            /// <summary>
            /// Resource will be stored on-disk
            /// Default is true
            /// </summary>
            public bool HoldOnDisk { get; set; }

            /// <summary>            
            /// Sets OverWriteIsAllowed = false.
            /// Toggle only if it's not enough the speed of the update.
            /// Default is false.
            /// </summary>
            public bool FastUpdates { get; set; }

            /// <summary>
            /// Prevents disk insert of the identical value of the existing key.
            /// Is interesting in case of intensive writes.
            /// Default is true.
            /// </summary>
            public bool InsertWithVerification { get; set; }

            /// <summary>
            /// Needed for getting resources via SelectStartsWith.
            /// Default is true.
            /// </summary>
            public bool SortingAscending { get; set; }
        }

        /// <summary>
        /// Insert resource
        /// </summary>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="resourceName"></param>
        /// <param name="resourceObject"></param>
        /// <param name="resourceSettings">resource extra behaviour</param>        
        public void Insert<TValue>(string resourceName, TValue resourceObject, Settings resourceSettings = null)
        {
            if (String.IsNullOrEmpty(resourceName))
                return;

            if (resourceSettings == null)
                resourceSettings = _defaultSetting;

            string rn = _urp + resourceName;

            byte[] btKey = DataTypesConvertor.ConvertKey<string>(rn);
            byte[] btValue = DataTypesConvertor.ConvertValue<TValue>(resourceObject);            

            _sync.EnterWriteLock();
            try
            {
                //------- Verification, to prevent storing of the identical value
                if (resourceSettings.InsertWithVerification)
                {
                    byte[] btExVal = null;
                    if (_d.TryGetValue(rn, out btExVal))
                    {
                        if (btExVal._ByteArrayEquals(btValue))
                            return;
                    }
                    else
                    {
                        //Grabbing from disk
                        if (resourceSettings.HoldOnDisk)
                        {
                            var row = LTrie.GetKey(btKey, false, false);
                            if (row.Exists)
                            {
                                btExVal = row.GetFullValue(false);
                                if (btExVal._ByteArrayEquals(btValue))
                                {
                                    if (resourceSettings.HoldInMemory)
                                        _d[rn] = btValue;

                                    return;
                                }
                            }
                        }

                    }
                }
                //------- 


                if (resourceSettings.HoldOnDisk)
                {
                    bool cov = LTrie.OverWriteIsAllowed;
                    if (resourceSettings.FastUpdates)
                        LTrie.OverWriteIsAllowed = false;

                    LTrie.Add(btKey, btValue);
                    LTrie.Commit();

                    if (resourceSettings.FastUpdates)
                        LTrie.OverWriteIsAllowed = cov;
                }

                if (resourceSettings.HoldInMemory)
                    _d[rn] = btValue;
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DBREEZE_RESOURCES_CONCERNING, "in Insert", ex);
            }
            finally
            {
                _sync.ExitWriteLock();
            }
            

            #region "remark"
            //            if (holdInMemory)
            //            {
            //                _sync.EnterWriteLock();
            //                try
            //                {
            //                    _d[resourceName] = btValue;
            //                }
            //                catch (Exception ex)
            //                {
            //                    throw ex;
            //                }
            //                finally
            //                {
            //                    _sync.ExitWriteLock();
            //                }
            //            }

            //            Action a = () => 
            //            {

            //                _sync.EnterWriteLock();
            //                try
            //                {
            //                    LTrie.Add(btKey, btValue);
            //                    LTrie.Commit();
            //                }
            //                catch (Exception ex)
            //                {
            //                    throw ex;
            //                }
            //                finally
            //                {
            //                    _sync.ExitWriteLock();
            //                }
            //            };

            //#if NET35 || NETr40   //The same must be use for .NET 4.0

            //            new System.Threading.Thread(new System.Threading.ThreadStart(() =>
            //            {
            //                a();
            //            })).Start();
            //#else
            //            System.Threading.Tasks.Task.Run(() => {
            //                a();
            //            });
            //#endif
            #endregion

        }

        /// <summary>
        /// Batch insert of resources where value is a defined DBreeze or DBreeze.CustomSerializer type
        /// </summary>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="resources"></param>
        /// <param name="resourceSettings"></param>
        public void Insert<TValue>(IDictionary<string, TValue> resources, Settings resourceSettings = null)
        {
            this.Insert(resources.ToDictionary(r => r.Key, r=> DataTypesConvertor.ConvertValue<TValue>(r.Value)),resourceSettings);
        }

        /// <summary>
        /// Batch insert of resources where value is a byte[]
        /// </summary>
        /// <param name="resources"></param>
        /// <param name="resourceSettings">resource extra behaviour</param>
        public void Insert(IDictionary<string, byte[]> resources, Settings resourceSettings = null)
        {
            if (resources == null || resources.Count < 1)
                return;

            if (resourceSettings == null)
                resourceSettings = _defaultSetting;

            byte[] btKey = null;
            byte[] btExVal = null;
            string rn = String.Empty;

            _sync.EnterWriteLock();
            try
            {
                bool cov = LTrie.OverWriteIsAllowed;
                if (resourceSettings.HoldOnDisk && resourceSettings.FastUpdates)
                    LTrie.OverWriteIsAllowed = false;

                foreach (var rs in resources.OrderBy(r=>r.Key))
                {
                    if (String.IsNullOrEmpty(rs.Key))
                        continue;

                    rn = _urp + rs.Key;

                    //------- Verification, to prevent storing of the identical value
                    if (resourceSettings.InsertWithVerification)
                    { 
                        if (_d.TryGetValue(rn, out btExVal))
                        {
                            if (btExVal._ByteArrayEquals(rs.Value))
                                continue;
                        }
                        else
                        {
                            //Grabbing from disk
                            if (resourceSettings.HoldOnDisk)
                            {
                                var row = LTrie.GetKey(btKey, false, false);
                                if (row.Exists)
                                {
                                    btExVal = row.GetFullValue(false);
                                    if (btExVal._ByteArrayEquals(rs.Value))
                                    {
                                        if (resourceSettings.HoldInMemory)
                                            _d[rn] = rs.Value;

                                        continue;
                                    }
                                }
                            }
                          
                        }
                    }
                    //------- 

                    if (resourceSettings.HoldInMemory)
                        _d[rn] = rs.Value;

                    if (resourceSettings.HoldOnDisk)
                    {
                        btKey = DataTypesConvertor.ConvertKey<string>(rn);
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
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DBREEZE_RESOURCES_CONCERNING, "in Insert batch", ex);
            }
            finally
            {
                _sync.ExitWriteLock();
            }

        }

        /// <summary>
        /// Removes resources from database and In-Memory dictionary 
        /// </summary>
        public void Remove(IList<string> resourcesNames)
        {
            if (resourcesNames == null || resourcesNames.Count == 0)
                return;

            byte[] btKey;
            string rn = String.Empty;

            _sync.EnterWriteLock();
            try
            {
                foreach (var rs in resourcesNames)
                {
                    if (String.IsNullOrEmpty(rs))
                        continue;

                    rn = _urp + rs;
                    _d.Remove(rn);

                    btKey = DataTypesConvertor.ConvertKey<string>(rn);
                    LTrie.Remove(ref btKey);
                }

                LTrie.Commit();
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DBREEZE_RESOURCES_CONCERNING, "in Remove batch", ex);
            }
            finally
            {
                _sync.ExitWriteLock();
            }
      
        }

        /// <summary>
        /// Removes resource from database and 
        /// </summary>        
        public void Remove(string resourceName)
        {
            if (String.IsNullOrEmpty(resourceName))
                return;

            string rn = _urp + resourceName;            
            byte[] btKey = DataTypesConvertor.ConvertKey<string>(rn);

            _sync.EnterWriteLock();
            try
            {
                _d.Remove(rn);
                LTrie.Remove(ref btKey);
                LTrie.Commit();                
            }
            catch (Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DBREEZE_RESOURCES_CONCERNING, "in Remove", ex);
            }
            finally
            {
                _sync.ExitWriteLock();
            }           

        }


        /// <summary>
        /// SelectStartsWith.
        /// Value instance, when byte[], must stay immutable, please use Dbreeze.Utils.CloneArray
        /// </summary>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="resourceNameStartsWith"></param>
        /// <param name="resourceSettings"></param>
        /// <returns></returns>
        public IEnumerable<KeyValuePair<string, TValue>> SelectStartsWith<TValue>(string resourceNameStartsWith, Settings resourceSettings = null)
        {
            if (!String.IsNullOrEmpty(resourceNameStartsWith))
            {
                if (resourceSettings == null)
                    resourceSettings = _defaultSetting;

                byte[] val = null;
                string rn = String.Empty;
                byte[] btKey = null;



                _sync.EnterUpgradeableReadLock();

                btKey = DataTypesConvertor.ConvertKey<string>(_urp + resourceNameStartsWith);
                var q = LTrie.IterateForwardStartsWith(btKey, true, false);
                if(!resourceSettings.SortingAscending)
                    q = LTrie.IterateBackwardStartsWith(btKey, true, false);

                foreach (var el in q)
                {
                    rn = el.Key.UTF8_GetString();

                    if (!_d.TryGetValue(rn, out val))
                    {
                        val = el.GetFullValue(false);

                        if (resourceSettings.HoldInMemory)
                        {
                            _sync.EnterWriteLock();
                            try
                            {
                                _d[rn] = val;
                            }
                            catch (Exception)
                            { }
                            finally
                            {
                                _sync.ExitWriteLock();
                            }
                        }
                    }

                    //no try..catch for yield return
                    yield return new KeyValuePair<string, TValue>(rn.Substring(1), val == null ? default(TValue) : DataTypesConvertor.ConvertBack<TValue>(val));
                }

                _sync.ExitUpgradeableReadLock();

            }//if is null or


        }




        /// <summary>
        /// Gets resources of the same type as a batch from memory or database (if not yet loaded).
        /// Value instance, when byte[], must stay immutable, please use Dbreeze.Utils.CloneArray
        /// </summary>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="resourcesNames"></param>
        /// <param name="resourceSettings">resource extra behaviour</param>
        /// <returns></returns>
        public IDictionary<string,TValue> Select<TValue>(IList<string> resourcesNames, Settings resourceSettings = null)
        {
            Dictionary<string, TValue> ret = new Dictionary<string, TValue>();
            if (resourcesNames == null || resourcesNames.Count < 1)
                return ret;
            if (resourceSettings == null)
                resourceSettings = _defaultSetting;

            byte[] val = null;
            string rn = String.Empty;

            //bool ba = typeof(TValue) == typeof(byte[]);

            _sync.EnterUpgradeableReadLock();
            try
            {

                foreach (var rsn in resourcesNames.OrderBy(r => r))
                {
                    if (String.IsNullOrEmpty(rsn))
                        continue;
                    rn = _urp + rsn;

                    if (!_d.TryGetValue(rn, out val))
                    {
                        //Value is not found
                        _sync.EnterWriteLock();
                        try
                        {
                            //At this moment value appeared
                            if (_d.TryGetValue(rn, out val))
                            {
                                if (val != null)
                                    ret[rsn] = DataTypesConvertor.ConvertBack<TValue>(val);
                                else
                                    ret[rsn] = default(TValue);

                                continue;
                            }

                            //trying to get from database
                            byte[] btKey = DataTypesConvertor.ConvertKey<string>(rn);
                            var row = LTrie.GetKey(btKey, false, false);
                            if (row.Exists)
                            {
                                val = row.GetFullValue(false);
                                if (val == null)
                                {
                                    if (resourceSettings.HoldInMemory)
                                        _d[rn] = null;

                                    ret[rsn] = default(TValue);
                                }
                                else
                                {
                                    if (resourceSettings.HoldInMemory)
                                        _d[rn] = val;
                                    ret[rsn] = DataTypesConvertor.ConvertBack<TValue>(val);
                                }
                            }
                            else
                            {
                                if (resourceSettings.HoldInMemory)
                                    _d[rn] = null;

                                ret[rsn] = default(TValue);
                            }
                        }
                        catch (Exception ex)
                        {
                            throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DBREEZE_RESOURCES_CONCERNING, "in Select 1", ex);
                        }
                        finally
                        {
                            _sync.ExitWriteLock();
                        }

                    }
                    else
                    {                        
                        if (val == null)
                            ret[rsn] = default(TValue);
                        else
                            ret[rsn] = DataTypesConvertor.ConvertBack<TValue>(val);

                    }
                }//eo foreach

            }
            catch (System.Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DBREEZE_RESOURCES_CONCERNING, "in Select 2", ex);
            }
            finally
            {
                _sync.ExitUpgradeableReadLock();
            }

            return ret;
        }





        /// <summary>
        /// Gets resource from memory or database (if not yet loaded)
        /// Value instance, when byte[], must stay immutable, please use Dbreeze.Utils.CloneArray
        /// </summary>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="resourceName"></param>
        /// <param name="resourceSettings">resource extra behaviour</param>
        /// <returns></returns>
        public TValue Select<TValue>(string resourceName, Settings resourceSettings = null)
        {
            if (String.IsNullOrEmpty(resourceName))
                return default(TValue);
            if (resourceSettings == null)
                resourceSettings = _defaultSetting;

            byte[] val = null;
            string rn = _urp + resourceName;

            _sync.EnterUpgradeableReadLock();
            try
            {
                if (!_d.TryGetValue(rn, out val))
                {
                    //Value is not found
                    _sync.EnterWriteLock();
                    try
                    {
                        //At this moment value appeared
                        if (_d.TryGetValue(rn, out val))
                        {
                            return val == null ? default(TValue) : DataTypesConvertor.ConvertBack<TValue>(val);                        
                        }

                        //trying to get from database
                        byte[] btKey = DataTypesConvertor.ConvertKey<string>(rn);
                        var row = LTrie.GetKey(btKey, false, false);
                        if (row.Exists)
                        {
                            val = row.GetFullValue(false);
                            if (val == null)
                            {
                                if (resourceSettings.HoldInMemory)
                                    _d[rn] = null;

                                return default(TValue);
                            }
                            else
                            {
                                if (resourceSettings.HoldInMemory)
                                    _d[rn] = val;

                                return DataTypesConvertor.ConvertBack<TValue>(val);
                            }
                        }
                        else
                        {
                            if (resourceSettings.HoldInMemory)
                                _d[rn] = null;

                            return default(TValue);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DBREEZE_RESOURCES_CONCERNING, "in Select 1", ex);
                    }
                    finally
                    {
                        _sync.ExitWriteLock();
                    }

                }
                else
                {
                    return val == null ? default(TValue) : DataTypesConvertor.ConvertBack<TValue>(val);                 
                }
            }
            catch (System.Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DBREEZE_RESOURCES_CONCERNING, "in Select 2", ex);
            }
            finally
            {
                _sync.ExitUpgradeableReadLock();
            }
        }


    }//eo class
}
