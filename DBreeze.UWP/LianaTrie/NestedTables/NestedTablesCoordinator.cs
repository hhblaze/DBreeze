/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze.Utils;
using DBreeze.DataTypes;

namespace DBreeze.LianaTrie
{
    /// <summary>
    /// Represents a bound to the LTrie nested tables coordinator.
    /// 1. Gets ability to remember Inserted Tables to perform cascade commit.
    /// 2. Rebind internal root-start if 2 or more horizontal tables are inserted into 1 value during one transaction (value expand case)
    /// </summary>
    internal class NestedTablesCoordinator:IDisposable
    {
        /// <summary>
        /// Key is a pointer to the full value, then in the value new Dictionary
        /// where key is root_start
        /// </summary>
        Dictionary<ulong, Dictionary<long, NestedTableInternal>> _nestedTables = new Dictionary<ulong, Dictionary<long, NestedTableInternal>>();
        public DbReaderWriterLock Sync_NestedTables = new DbReaderWriterLock();

        Dictionary<string, ulong> _nestedTblsViaKeys = new Dictionary<string, ulong>();

        private int countNested = 0;

        /// <summary>
        /// Will be taken into consideration only from MasterTrie.
        /// Set up to -1 after Commit and Rollback, Set To Thread id when tran.InsertTable is called
        /// using this flag we will be able to regulate returns (based on useCache or not for nested Tables)
        /// </summary>
        internal volatile int ModificationThreadId = -1;

        internal object lock_nestedTblAccess = new object();

        /// <summary>
        /// LTrie makes in case of InsertTable call
        /// </summary>
        /// <param name="nestedTable"></param>
        internal void AddNestedTable(ref byte[] key, ulong fullValueStart, long rootStart, NestedTableInternal nestedTable)
        {
            Sync_NestedTables.EnterWriteLock();
            try
            {

                string hash = key.ToBase64String();

                ulong ptr = 0;

                _nestedTblsViaKeys.TryGetValue(hash, out ptr);

                if (ptr == 0)
                    _nestedTblsViaKeys.Add(hash, fullValueStart);

                Dictionary<long, NestedTableInternal> dict = null;

                _nestedTables.TryGetValue(fullValueStart, out dict);

                if (dict == null)
                {
                    Dictionary<long, NestedTableInternal> d = new Dictionary<long, NestedTableInternal>();
                    d.Add(rootStart, nestedTable);
                    _nestedTables.Add(fullValueStart, d);
                    countNested++;
                }
                else
                {
                    NestedTableInternal dit = null;
                    dict.TryGetValue(rootStart, out dit);
                    if (dit == null)
                    {
                        _nestedTables[fullValueStart].Add(rootStart, nestedTable);
                    }
                    //else all ok, skip
                }


            }
            catch (System.Exception ex)
            {
                //CASCADE
                throw ex;
            }
            finally
            {
                Sync_NestedTables.ExitWriteLock();
            }
                     
           
        }

        //NESTED TABLES ARE NOT DELETED AFTER COMMIT OR ROLLBACK etc. Their lifecycle is finished together with Master Trie.
        //It's necessary for the parallel threads which could start to read before commit and go on to read after commit, 
        //in case, if after new thread creates a write into the same table (new nested table will be create and parallel read will have reference to the "old" one
        //- in total 2 tables with different cache can collide)

        /// <summary>
        /// Committing nested tables
        /// </summary>
        internal void TransactionalCommitFinished()
        {            

            Sync_NestedTables.EnterReadLock();
            try
            {                
                foreach (var nt in _nestedTables)
                {
                    foreach (var dit in nt.Value)
                    {
                        dit.Value.TransactionalCommitFinished();
                    }
                }

            }
            catch (System.Exception ex)
            {
                //CASCADE
                throw ex;
            }
            finally
            {
                Sync_NestedTables.ExitReadLock();
            }

        }

        /// <summary>
        /// Committing nested tables
        /// </summary>
        internal void Commit()
        {

            Sync_NestedTables.EnterReadLock();         
            try
            {
                foreach (var nt in _nestedTables)
                {
                    foreach (var dit in nt.Value)
                    {
                        dit.Value.Commit();
                    }
                }

                //_nestedTables.Clear();
                //countNested = 0;
            }
            catch (System.Exception ex)
            {
                //CASCADE
                throw ex;
            }
            finally
            {
                Sync_NestedTables.ExitReadLock();
            }

        }

        /// <summary>
        /// Transactional Commit Nested
        /// </summary>
        internal void TransactionalCommit()
        {
            Sync_NestedTables.EnterReadLock(); 
            try
            {
                foreach (var nt in _nestedTables)
                {
                    foreach (var dit in nt.Value)
                    {
                        dit.Value.TransactionalCommit();
                    }
                }

                //_nestedTables.Clear();
                //countNested = 0;
            }
            catch (System.Exception ex)
            {
                //CASCADE
                throw ex;
            }
            finally
            {
                Sync_NestedTables.ExitReadLock();
            }

        }

        internal void Rollback()
        {
            Sync_NestedTables.EnterReadLock();
            try
            {
                foreach (var nt in _nestedTables)
                {
                    foreach (var dit in nt.Value)
                    {
                        dit.Value.Rollback();
                    }
                }

                //_nestedTables.Clear();
                //countNested = 0;
            }
            catch (System.Exception ex)
            {
                //CASCADE
                throw ex;
            }
            finally
            {
                Sync_NestedTables.ExitReadLock();
            }
        }

        /// <summary>
        /// Transactional Rollback nested
        /// </summary>
        internal void TransactionalRollback()
        {
            Sync_NestedTables.EnterReadLock();
            try
            {
                foreach (var nt in _nestedTables)
                {
                    foreach (var dit in nt.Value)
                    {
                        dit.Value.TransactionalRollback();
                    }
                }

            }
            catch (System.Exception ex)
            {
                //CASCADE
                throw ex;
            }
            finally
            {
                Sync_NestedTables.ExitReadLock();
            }
        }


        internal bool IfKeyIsInNestedList(ref byte[] key)
        {

            Sync_NestedTables.EnterReadLock();
            try
            {
                string hash = key.ToBase64String();

                ulong ptr = 0; 

                _nestedTblsViaKeys.TryGetValue(hash, out ptr);

                if (ptr == 0)
                    return false;

                return true;
            }
            finally
            {
                Sync_NestedTables.ExitReadLock();
            }

        }


        internal void ChangeKeyAndMoveNestedTablesRootStart(ref byte[] oldKey, ref byte[] newKey, ulong idNewFullValueStart, long valueStart)
        {
            Sync_NestedTables.EnterWriteLock();
            try
            {
                string hash = oldKey.ToBase64String();

                ulong ptr = 0; 

                _nestedTblsViaKeys.TryGetValue(hash, out ptr);

                if (ptr == 0)
                    return;    

                Dictionary<long, NestedTableInternal> dict = null;

                _nestedTables.TryGetValue(ptr, out dict);

                if (!_nestedTables.ContainsKey(idNewFullValueStart))
                {
                    _nestedTables.Add(idNewFullValueStart, new Dictionary<long, NestedTableInternal>());
                }

                long rootStart = 0;
                foreach (var d in dict)
                {
                    rootStart = d.Value.SetNewRootStart(valueStart);
                    _nestedTables[idNewFullValueStart].Add(rootStart, d.Value);
                }

                _nestedTables.Remove(ptr);

                _nestedTblsViaKeys.Remove(hash);

                hash = newKey.ToBase64String();

                _nestedTblsViaKeys.Add(hash, idNewFullValueStart);               
                
            }
            catch (Exception ex)
            {
                //CASCADE
                throw ex;
            }
            finally
            {
                Sync_NestedTables.ExitWriteLock();
            }
        }


        internal void MoveNestedTablesRootStart(ref byte[] key ,ulong idNewFullValueStart,long valueStart)
        {
            Sync_NestedTables.EnterWriteLock();
            try
            {
                string hash = key.ToBase64String();

                ulong ptr = 0;  //old fullValueStart

                _nestedTblsViaKeys.TryGetValue(hash, out ptr);

                if (ptr == 0)
                    return;     //didn't find such row in manipulations

                //settign up new link
                _nestedTblsViaKeys[hash] = idNewFullValueStart;


                if (!_nestedTables.ContainsKey(idNewFullValueStart))
                {
                    _nestedTables.Add(idNewFullValueStart, new Dictionary<long, NestedTableInternal>());
                }
                else
                    return;

                Dictionary<long, NestedTableInternal> dict = null;

                _nestedTables.TryGetValue(ptr, out dict);
                                               

                long rootStart = 0;

                foreach (var d in dict)
                {
                    rootStart = d.Value.SetNewRootStart(valueStart);
                    _nestedTables[idNewFullValueStart].Add(rootStart, d.Value);
                }
                              

                _nestedTables.Remove(ptr);
            }
            catch (Exception ex)
            {
                //CASCADE
                throw ex;
            }
            finally
            {
                Sync_NestedTables.ExitWriteLock();
            }
        }

        



        /// <summary>
        /// Returns null if table is not in the coordinator, otherwise returns reference to the table
        /// </summary>
        /// <param name="fullValueStart"></param>
        /// <param name="rootStart"></param>
        /// <returns></returns>      
        internal NestedTableInternal GetTable(ref byte[] key,  long rootStart)
        {
            Sync_NestedTables.EnterReadLock();
            try
            {
                string hash = key.ToBase64String();

                ulong ptr = 0;

                _nestedTblsViaKeys.TryGetValue(hash, out ptr);

                if (ptr == 0)
                    return null;

                Dictionary<long, NestedTableInternal> dict = null;

                _nestedTables.TryGetValue(ptr, out dict);

                if (dict == null)
                    return null;

                NestedTableInternal dit = null;

                dict.TryGetValue(rootStart, out dit);
                return dit;
            }
            catch (Exception ex)
            {
                //CASCADE
                throw ex;
            }
            finally
            {
                Sync_NestedTables.ExitReadLock();
            }
            
           
        }


        internal void CloseAll()
        {
            //quantity open reads
            uint qor = 0;
            lock (this.lock_nestedTblAccess)
            {
                Sync_NestedTables.EnterWriteLock();
                try
                {
                    foreach (var ntByKey in _nestedTblsViaKeys)
                    {
                        foreach (var ntByRoot in _nestedTables[ntByKey.Value])
                        {
                            qor = --ntByRoot.Value.quantityOpenReads;

                            if (qor > 0)
                                continue;

                            System.Diagnostics.Debug.WriteLine("Closing");
                        }
                    }
                }
                finally
                {
                    Sync_NestedTables.ExitWriteLock();
                }
            }
        }

        public void CloseTable(ref byte[] key, ref long rootStart)
        {
            //Must close refered nested table and in cascade include tables, for memory efficiency while reading master table with nested tables
            //Full remove must also clean dictionary entries

            //Close must calculate, OpenWritesAndReads

            //We count up/down only reads, CloseTable
            lock (this.lock_nestedTblAccess)
            {
                Sync_NestedTables.EnterWriteLock();
                try
                {
                    string hash = key.ToBase64String();

                    ulong ptr = 0;

                    _nestedTblsViaKeys.TryGetValue(hash, out ptr);

                    if (ptr == 0)
                        return;

                    Dictionary<long, NestedTableInternal> dict = null;

                    _nestedTables.TryGetValue(ptr, out dict);

                    if (dict == null)
                        return;

                    if (!dict.ContainsKey(rootStart))
                        return;

                    //decreasing quantity of open reads

                    uint qor = --dict[rootStart].quantityOpenReads;

                    //if (this.ModificationThreadId != -1)
                    //    return;

                    if (qor > 0)
                        return;


                    dict[rootStart].Dispose();

                    dict.Remove(rootStart);

                    if (dict.Count() == 0)
                    {
                        _nestedTblsViaKeys.Remove(hash);
                    }

                }
                finally
                {
                    Sync_NestedTables.ExitWriteLock();
                }
            }
        }

        /// <summary>
        /// Cascade remove all of all nested and sub-nested tables under the key
        /// </summary>
        /// <param name="key"></param>
        public void Remove(ref byte[] key)
        {
            Sync_NestedTables.EnterWriteLock();
            try
            {
                string hash = key.ToBase64String();

                ulong ptr = 0;

                _nestedTblsViaKeys.TryGetValue(hash, out ptr);

                if (ptr == 0)
                    return;

                Dictionary<long, NestedTableInternal> dict = null;

                _nestedTables.TryGetValue(ptr, out dict);

                if (dict == null)
                    return;

                foreach (var di in dict)
                {
                    //this will call in every nested table RemoveAll keys function, who will call nested table Dispose
                    di.Value.RemoveAll();
                }
            }
            finally
            {
                Sync_NestedTables.ExitWriteLock();
            }
        }

        public void RemoveAll()
        {
            Sync_NestedTables.EnterWriteLock();
            try
            {
                foreach (var nt in _nestedTables)
                {
                    foreach (var dit in nt.Value)
                    {
                        dit.Value.RemoveAll();
                    }
                }               
            }
            finally
            {
                Sync_NestedTables.ExitWriteLock();
            }
        }


        public void Dispose()
        {
            Sync_NestedTables.EnterWriteLock();
            try
            {
                foreach (var nt in _nestedTables)
                {
                    foreach (var dit in nt.Value)
                    {
                        dit.Value.Dispose();
                    }
                }

                _nestedTables.Clear();
                //_nestedTables = null;

                _nestedTblsViaKeys.Clear();
                //_nestedTblsViaKeys = null;

                countNested = 0;
            }
            finally
            {
                Sync_NestedTables.ExitWriteLock();
            }
        }
    }
}
