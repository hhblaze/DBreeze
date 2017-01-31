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
        /// 
        /// </summary>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="resourceName"></param>
        /// <param name="resourceObject"></param>
        /// <param name="holdInMemory"></param>
        public void Insert<TValue>(string resourceName, TValue resourceObject, bool holdInMemory=true)
        {
            if (String.IsNullOrEmpty(resourceName))
                return;

            byte[] btKey = DataTypesConvertor.ConvertKey<string>(resourceName);
            byte[] btValue = DataTypesConvertor.ConvertValue<TValue>(resourceObject);

            if (holdInMemory)
            {
                _sync.EnterWriteLock();
                try
                {
                    _d[resourceName] = btValue;
                }
                catch (Exception ex)
                {
                    throw ex;
                }
                finally
                {
                    _sync.ExitWriteLock();
                }
            }

            Action a = () => 
            {
               
                _sync.EnterWriteLock();
                try
                {
                    LTrie.Add(btKey, btValue);
                    LTrie.Commit();
                }
                catch (Exception ex)
                {
                    throw ex;
                }
                finally
                {
                    _sync.ExitWriteLock();
                }
            };
            
#if NET35 || NETr40   //The same must be use for .NET 4.0

            new System.Threading.Thread(new System.Threading.ThreadStart(() =>
            {
                a();
            })).Start();
#else
            System.Threading.Tasks.Task.Run(() => {
                a();
            });
#endif
           
        }

        /// <summary>
        /// Batch insert of resources
        /// </summary>
        /// <param name="resources"></param>
        /// <param name="holdInMemory"></param>
        public void Insert(IDictionary<string, byte[]> resources, bool holdInMemory = true)
        {
            if (resources == null || resources.Count < 1)
                return;

            byte[] btKey = null;

            if (holdInMemory)
            {
                _sync.EnterWriteLock();
                try
                {
                    foreach (var rs in resources)
                    {
                        if (String.IsNullOrEmpty(rs.Key))
                            continue;
                        _d[rs.Key] = rs.Value;
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
                finally
                {
                    _sync.ExitWriteLock();
                }
            }

            Action a = () =>
            {
                          
                
                _sync.EnterWriteLock();
                try
                {
                    foreach (var rs in resources)
                    {
                        if (String.IsNullOrEmpty(rs.Key))
                            continue;

                        btKey = DataTypesConvertor.ConvertKey<string>(rs.Key);
                        LTrie.Add(btKey, rs.Value);                       
                    }

                    LTrie.Commit();

                }
                catch (Exception ex)
                {
                    throw ex;
                }
                finally
                {
                    _sync.ExitWriteLock();
                }
            };

#if NET35 || NETr40   //The same must be use for .NET 4.0

            new System.Threading.Thread(new System.Threading.ThreadStart(() =>
            {
                a();
            })).Start();
#else
            System.Threading.Tasks.Task.Run(() => {
                a();
            });
#endif

        }

        /// <summary>
        /// Removes resources from database and In-Memory dictionary 
        /// </summary>
        public void Remove(IList<string> resourcesNames)
        {
            if (resourcesNames == null || resourcesNames.Count == 0)
                return;

            _sync.EnterWriteLock();
            try
            {
                foreach (var rs in resourcesNames)
                {
                    if (String.IsNullOrEmpty(rs))
                        continue;
                    _d.Remove(rs);
                }                
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
                _sync.ExitWriteLock();
            }

            Action a = () =>
            {
                byte[] btKey;

                _sync.EnterWriteLock();
                try
                {
                    foreach (var rs in resourcesNames)
                    {
                        if (String.IsNullOrEmpty(rs))
                            continue;

                        btKey = DataTypesConvertor.ConvertKey<string>(rs);
                      
                        LTrie.Remove(ref btKey);                       
                    }

                    LTrie.Commit();
                }
                catch (Exception ex)
                {
                    throw ex;
                }
                finally
                {
                    _sync.ExitWriteLock();
                }
            };

#if NET35 || NETr40   //The same must be use for .NET 4.0

            new System.Threading.Thread(new System.Threading.ThreadStart(() =>
            {
                a();
            })).Start();
#else
            System.Threading.Tasks.Task.Run(() => {
                a();
            });
#endif
      
        }

        /// <summary>
        /// Removes resource from database and 
        /// </summary>        
        public void Remove(string resourceName)
        {
            if (String.IsNullOrEmpty(resourceName))
                return;

            _sync.EnterWriteLock();
            try
            {             
                _d.Remove(resourceName);
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
                _sync.ExitWriteLock();
            }

            Action a = () =>
            {
                byte[] btKey = DataTypesConvertor.ConvertKey<string>(resourceName);

                _sync.EnterWriteLock();
                try
                {
                    LTrie.Remove(ref btKey);
                    LTrie.Commit();                    
                }
                catch (Exception ex)
                {
                    throw ex;
                }
                finally
                {
                    _sync.ExitWriteLock();
                }
            };

#if NET35 || NETr40   //The same must be use for .NET 4.0

            new System.Threading.Thread(new System.Threading.ThreadStart(() =>
            {
                a();
            })).Start();
#else
            System.Threading.Tasks.Task.Run(() =>
            {
                a();
            });
#endif

        }



        /// <summary>
        /// Gets resource from memory or database (if not yet loaded)
        /// </summary>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="resourceName"></param>
        /// <param name="holdInMemory">first time will grab fro database and will leave in-memory</param>
        /// <returns></returns>
        public TValue Select<TValue>(string resourceName, bool holdInMemory = true)
        {
            if (String.IsNullOrEmpty(resourceName))
                return default(TValue);

            byte[] val = null;            

            _sync.EnterUpgradeableReadLock();
            try
            {
                if (!_d.TryGetValue(resourceName, out val))
                {
                    //Value is not found
                    _sync.EnterWriteLock();
                    try
                    {
                        //At this moment value appeared
                        if (_d.TryGetValue(resourceName, out val))
                        {
                            if (val != null)
                                return DataTypesConvertor.ConvertBack<TValue>(val);
                            else
                                return default(TValue);
                        }

                        //trying to get from database
                        byte[] btKey = DataTypesConvertor.ConvertKey<string>(resourceName);
                        var row = LTrie.GetKey(btKey, true);
                        if (row.Exists)
                        {
                            val = row.GetFullValue(true);
                            if (val == null)
                            {
                                if (holdInMemory)
                                    _d[resourceName] = null;

                                return default(TValue);
                            }
                            else
                            {
                                if (holdInMemory)
                                    _d[resourceName] = val;
                                return DataTypesConvertor.ConvertBack<TValue>(val);
                            }
                        }
                        else
                        {
                            if (holdInMemory)
                                _d[resourceName] = null;

                            return default(TValue);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }
                    finally
                    {
                        _sync.ExitWriteLock();
                    }

                }
                else
                {
                    if (val == null)
                        return default(TValue);
                    else
                        return DataTypesConvertor.ConvertBack<TValue>(val);

                }
            }
            catch (System.Exception ex)
            {                    
                throw ex;
            }
            finally
            {
                _sync.ExitUpgradeableReadLock();
            }
        }


    }//eo class
}
