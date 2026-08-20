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

        Dictionary<byte[], ulong> _nestedTblsViaKeys =
            new Dictionary<byte[], ulong>(ByteArrayEqualityComparer.Instance);

        private sealed class ByteArrayEqualityComparer : IEqualityComparer<byte[]>
        {
            internal static readonly ByteArrayEqualityComparer Instance = new ByteArrayEqualityComparer();

            public bool Equals(byte[] x, byte[] y)
            {
                if (ReferenceEquals(x, y))
                    return true;
                if (x == null || y == null || x.Length != y.Length)
                    return false;

                for (int i = 0; i < x.Length; i++)
                {
                    if (x[i] != y[i])
                        return false;
                }

                return true;
            }

            public int GetHashCode(byte[] value)
            {
                unchecked
                {
                    int hash = (int)2166136261;
                    for (int i = 0; i < value.Length; i++)
                        hash = (hash ^ value[i]) * 16777619;
                    return hash;
                }
            }
        }

        private int countNested = 0;
        private int _disposed = 0;
        private List<NestedTableInternal> _deferredClosedTables = null;
        private int _transactionCompletionPending = 0;

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

                byte[] hash = key;
                byte[] identityKey = key;

                ulong ptr = 0;

                _nestedTblsViaKeys.TryGetValue(hash, out ptr);

                if (ptr == 0)
                {
                    identityKey = (byte[])key.Clone();
                    _nestedTblsViaKeys.Add(identityKey, fullValueStart);
                }

                Dictionary<long, NestedTableInternal> dict = null;

                _nestedTables.TryGetValue(fullValueStart, out dict);

                if (ptr != 0 && dict != null)
                {
                    foreach (KeyValuePair<long, NestedTableInternal> existingTable in dict)
                    {
                        identityKey = existingTable.Value.StructuralKey;
                        break;
                    }
                }

                nestedTable.BindIdentity(fullValueStart, identityKey);

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
            catch
            {
                //CASCADE
                throw;
            }
            finally
            {
                Sync_NestedTables.ExitWriteLock();
            }
                     
           
        }

        //Nested tables with open handles remain coordinated across commit/rollback so readers can cross the
        //transaction boundary safely. A clean table is detached when its last handle closes. A dirty table whose
        //last handle closes is retained until the master transaction completes, then detached and disposed.

        /// <summary>
        /// Committing nested tables
        /// </summary>
        internal void TransactionalCommitFinished()
        {
            System.Threading.Interlocked.Exchange(ref _transactionCompletionPending, 1);

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
            catch
            {
                //CASCADE
                throw;
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
            System.Threading.Interlocked.Exchange(ref _transactionCompletionPending, 1);

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
            catch
            {
                //CASCADE
                throw;
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
            System.Threading.Interlocked.Exchange(ref _transactionCompletionPending, 1);
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
            catch
            {
                //CASCADE
                throw;
            }
            finally
            {
                Sync_NestedTables.ExitReadLock();
            }

        }

        internal void Rollback()
        {
            System.Threading.Interlocked.Exchange(ref _transactionCompletionPending, 1);
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
            catch
            {
                //CASCADE
                throw;
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
            System.Threading.Interlocked.Exchange(ref _transactionCompletionPending, 1);
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
            catch
            {
                //CASCADE
                throw;
            }
            finally
            {
                Sync_NestedTables.ExitReadLock();
            }

        }

        internal void TransactionFinished()
        {
            System.Threading.Interlocked.Exchange(ref _transactionCompletionPending, 0);
            ReleaseDeferredClosedTables();
        }


        internal bool IfKeyIsInNestedList(ref byte[] key)
        {

            Sync_NestedTables.EnterReadLock();
            try
            {
                byte[] hash = key;

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
                byte[] hash = oldKey;

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

                byte[] newIdentityKey = (byte[])newKey.Clone();
                long rootStart = 0;
                foreach (var d in dict)
                {
                    rootStart = d.Value.SetNewRootStart(idNewFullValueStart, valueStart, newIdentityKey);
                    _nestedTables[idNewFullValueStart].Add(rootStart, d.Value);
                }

                _nestedTables.Remove(ptr);

                _nestedTblsViaKeys.Remove(hash);

                hash = newIdentityKey;

                _nestedTblsViaKeys.Add(hash, idNewFullValueStart);               
                
            }
            catch
            {
                //CASCADE
                throw;
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
                byte[] hash = key;

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
                    rootStart = d.Value.SetNewRootStart(idNewFullValueStart, valueStart, null);
                    _nestedTables[idNewFullValueStart].Add(rootStart, d.Value);
                }
                              

                _nestedTables.Remove(ptr);
            }
            catch
            {
                //CASCADE
                throw;
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
                byte[] hash = key;

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
            catch
            {
                //CASCADE
                throw;
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

                            Console.WriteLine("Closing");
                        }
                    }
                }
                finally
                {
                    Sync_NestedTables.ExitWriteLock();
                }
            }
        }

        public void CloseTable(NestedTableInternal nestedTable)
        {
            NestedTableInternal tableToDispose = null;

            lock (this.lock_nestedTblAccess)
            {
                Sync_NestedTables.EnterWriteLock();
                try
                {
                    if (!nestedTable.CoordinatorOwned)
                        return;

                    if (nestedTable.quantityOpenReads == 0)
                        return;

                    uint qor = --nestedTable.quantityOpenReads;

                    if (qor > 0)
                        return;

                    if (nestedTable.IsModified ||
                        System.Threading.Interlocked.CompareExchange(ref _transactionCompletionPending, 0, 0) != 0)
                    {
                        if (!nestedTable.ClosePending)
                        {
                            nestedTable.ClosePending = true;
                            if (_deferredClosedTables == null)
                                _deferredClosedTables = new List<NestedTableInternal>();
                            _deferredClosedTables.Add(nestedTable);
                        }
                        return;
                    }

                    nestedTable.ClosePending = false;
                    if (DetachNestedTable(nestedTable))
                        tableToDispose = nestedTable;

                }
                finally
                {
                    Sync_NestedTables.ExitWriteLock();
                }
            }

            if (tableToDispose != null)
                tableToDispose.Dispose();
        }

        private void ReleaseDeferredClosedTables()
        {
            List<NestedTableInternal> tablesToDispose = null;

            lock (this.lock_nestedTblAccess)
            {
                Sync_NestedTables.EnterWriteLock();
                try
                {
                    if (_deferredClosedTables == null)
                        return;

                    List<NestedTableInternal> stillDeferred = null;
                    foreach (NestedTableInternal nestedTable in _deferredClosedTables)
                    {
                        if (!nestedTable.ClosePending)
                            continue;

                        if (nestedTable.quantityOpenReads > 0)
                        {
                            nestedTable.ClosePending = false;
                            continue;
                        }

                        if (nestedTable.IsModified)
                        {
                            if (stillDeferred == null)
                                stillDeferred = new List<NestedTableInternal>();
                            stillDeferred.Add(nestedTable);
                            continue;
                        }

                        nestedTable.ClosePending = false;
                        if (DetachNestedTable(nestedTable))
                        {
                            if (tablesToDispose == null)
                                tablesToDispose = new List<NestedTableInternal>();
                            tablesToDispose.Add(nestedTable);
                        }
                    }

                    _deferredClosedTables = stillDeferred;
                }
                finally
                {
                    Sync_NestedTables.ExitWriteLock();
                }
            }

            DisposeNestedTables(tablesToDispose);
        }

        private bool DetachNestedTable(NestedTableInternal nestedTable)
        {
            ulong fullValueStart = nestedTable.FullValueStart;
            long rootStart = nestedTable.RootStart;
            Dictionary<long, NestedTableInternal> dict = null;
            NestedTableInternal registeredTable = null;

            if (!_nestedTables.TryGetValue(fullValueStart, out dict) ||
                !dict.TryGetValue(rootStart, out registeredTable) ||
                !Object.ReferenceEquals(registeredTable, nestedTable))
            {
                dict = null;
                foreach (KeyValuePair<ulong, Dictionary<long, NestedTableInternal>> byPointer in _nestedTables)
                {
                    foreach (KeyValuePair<long, NestedTableInternal> byRoot in byPointer.Value)
                    {
                        if (Object.ReferenceEquals(byRoot.Value, nestedTable))
                        {
                            fullValueStart = byPointer.Key;
                            rootStart = byRoot.Key;
                            dict = byPointer.Value;
                            break;
                        }
                    }

                    if (dict != null)
                        break;
                }
            }

            if (dict == null)
            {
                nestedTable.DetachFromCoordinator();
                return false;
            }

            dict.Remove(rootStart);
            if (dict.Count == 0)
            {
                _nestedTables.Remove(fullValueStart);

                bool keyRemoved = nestedTable.StructuralKey != null &&
                    _nestedTblsViaKeys.Remove(nestedTable.StructuralKey);
                if (!keyRemoved)
                {
                    byte[] structuralKey = null;
                    foreach (KeyValuePair<byte[], ulong> byKey in _nestedTblsViaKeys)
                    {
                        if (byKey.Value == fullValueStart)
                        {
                            structuralKey = byKey.Key;
                            break;
                        }
                    }

                    if (structuralKey != null)
                        _nestedTblsViaKeys.Remove(structuralKey);
                }

                if (countNested > 0)
                    countNested--;
            }

            nestedTable.DetachFromCoordinator();
            return true;
        }

        private static void DisposeNestedTables(List<NestedTableInternal> nestedTables)
        {
            if (nestedTables == null)
                return;

            foreach (NestedTableInternal nestedTable in nestedTables)
                nestedTable.Dispose();
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
                byte[] hash = key;

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


        private List<NestedTableInternal> DetachAll()
        {
            System.Threading.Interlocked.Exchange(ref _transactionCompletionPending, 0);
            List<NestedTableInternal> detachedTables = null;
            foreach (var nt in _nestedTables)
            {
                foreach (var dit in nt.Value)
                {
                    dit.Value.DetachFromCoordinator();
                    if (detachedTables == null)
                        detachedTables = new List<NestedTableInternal>();
                    detachedTables.Add(dit.Value);
                }
            }

            _nestedTables.Clear();
            _nestedTblsViaKeys.Clear();
            _deferredClosedTables = null;
            countNested = 0;
            return detachedTables;
        }

        internal void Reset()
        {
            List<NestedTableInternal> detachedTables = null;
            lock (this.lock_nestedTblAccess)
            {
                Sync_NestedTables.EnterWriteLock();
                try
                {
                    detachedTables = DetachAll();
                }
                finally
                {
                    Sync_NestedTables.ExitWriteLock();
                }
            }

            DisposeNestedTables(detachedTables);
        }

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            List<NestedTableInternal> detachedTables = null;
            lock (this.lock_nestedTblAccess)
            {
                Sync_NestedTables.EnterWriteLock();
                try
                {
                    detachedTables = DetachAll();
                }
                finally
                {
                    Sync_NestedTables.ExitWriteLock();
                }
            }

            try
            {
                DisposeNestedTables(detachedTables);
            }
            finally
            {
                Sync_NestedTables.Dispose();
            }
        }
    }
}
