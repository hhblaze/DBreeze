/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze;
using DBreeze.Utils;
using DBreeze.LianaTrie;
using DBreeze.Exceptions;
using DBreeze.SchemeInternal;

namespace DBreeze.Transactions
{
    internal class TransactionsCoordinator
    {
        DbReaderWriterLock _sync_transactions = new DbReaderWriterLock();
        /// <summary>
        /// Dictionary of all active transactions. Key is ManagedThreadId
        /// </summary>
        Dictionary<int, TransactionUnit> _transactions = new Dictionary<int, TransactionUnit>();

        internal DBreezeEngine _engine = null;

        public TransactionsCoordinator(DBreezeEngine engine)
        {
            this._engine = engine;            
        }

        /// <summary>
        /// Fast access to the Schema object.
        /// Used by Transaction class
        /// </summary>
        public Scheme GetSchema
        {
            get { return this._engine.DBreezeSchema; }
        }



        /// <summary>
        /// 
        /// </summary>
        /// <param name="transactionType">0 = standard transaction, 1 - locked transaction</param>
        /// <param name="lockType"></param>
        /// <param name="tables"></param>
        /// <returns></returns>
        public Transaction GetTransaction(int transactionType, eTransactionTablesLockTypes lockType, params string[] tables)
        {
            //this check is done on upper level
            //if (!this.DbIsOperatable)
            //    return null;

            //Transaction must have 2 classes one class is for the user, with appropriate methods, second for technical purposes TransactionDetails, where we store different transaction information
            //both classes must be bound into one class TransactionUnit

#if NET35 || NETr40
            int transactionThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
#else
            int transactionThreadId = Environment.CurrentManagedThreadId;
#endif
            bool existingTransaction = false;
            _sync_transactions.EnterReadLock();
            try
            {
                existingTransaction = this._transactions.ContainsKey(transactionThreadId);
            }
            finally
            {
                _sync_transactions.ExitReadLock();
            }

            if (existingTransaction)
                UnregisterTransaction(transactionThreadId);

            TransactionUnit transactionUnit = new TransactionUnit(transactionType, this, lockType, tables);

            //Adding transaction to the list
            _sync_transactions.EnterWriteLock();
            try
            {
                this._transactions.Add(transactionUnit.TransactionThreadId, transactionUnit);
            }
            catch (System.Exception ex)
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTION_GETTING_TRANSACTION_FAILED, ex);
            }
            finally
            {
                _sync_transactions.ExitWriteLock();
            }

            return transactionUnit.Transaction;
        }

        ///// <summary>
        ///// 
        ///// </summary>
        ///// <returns></returns>
        //public Transaction GetTransaction()
        //{
        //    //this check is done on upper level
        //    //if (!this.DbIsOperatable)
        //    //    return null;

        //    //Transaction must have 2 classes one class is for the user, with appropriate methods, second for technical purposes TransactionDetails, where we store different transaction information
        //    //both classes must be bound into one class TransactionUnit
            
        //    TransactionUnit transactionUnit = new TransactionUnit(this);
           
            

        //    //Checking if the same transaction already exists in the list of Transactions. 
        //    //It could happen in case of abnormal termination of parallel thread, without disposing of the transaction.
        //    //So we delete pending transaction first, then create new one.
        //    bool reRun = false;
        //    _sync_transactions.EnterReadLock();
        //    try
        //    {
        //        if (this._transactions.ContainsKey(transactionUnit.TransactionThreadId))
        //        {
        //            reRun = true;
        //        }
        //    }
        //    finally
        //    {
        //        _sync_transactions.ExitReadLock();
        //    }

        //    if (reRun)
        //    {
        //        UnregisterTransaction(transactionUnit.TransactionThreadId);
        //        return GetTransaction();
        //    }

        //    //Adding transaction to the list
        //    _sync_transactions.EnterWriteLock();
        //    try
        //    {
        //        this._transactions.Add(transactionUnit.TransactionThreadId, transactionUnit);
        //    }
        //    catch (System.Exception ex)
        //    {
        //        throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTION_GETTING_TRANSACTION_FAILED,ex);                
        //    }
        //    finally
        //    {
        //        _sync_transactions.ExitWriteLock();
        //    }

        //    return transactionUnit.Transaction;
        //}

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transactionThreadId"></param>
        /// <returns></returns>
        private TransactionUnit GetTransactionUnit(int transactionThreadId)
        {
            TransactionUnit transactionUnit = null;

            _sync_transactions.EnterReadLock();
            try
            {
                this._transactions.TryGetValue(transactionThreadId, out transactionUnit);
            }
            catch (System.Exception ex)
            {
                throw ex;
            }
            finally
            {
                _sync_transactions.ExitReadLock();
            }

            return transactionUnit;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transactionThreadId"></param>
        public void UnregisterTransaction(int transactionThreadId)
        {
            TransactionUnit transactionUnit = null;
            Exception exc = null;

            _sync_transactions.EnterWriteLock();
            try
            {
                if (this._transactions.TryGetValue(transactionThreadId, out transactionUnit))
                    this._transactions.Remove(transactionThreadId);
            }
            finally
            {
                _sync_transactions.ExitWriteLock();
            }

            if (transactionUnit != null)
            {
                try
                {
                    transactionUnit.Dispose();
                }
                catch (System.Exception ex)
                {
                    exc = ex;
                }
            }

            SignalWriteWaiters(transactionThreadId);

            if (exc != null)
                throw exc;
            
        }

        /// <summary>
        /// Is called by the engine on Dispose.
        /// </summary>
        public void UnregisterAllTransactions()
        {
            Exception exc = null;
            List<TransactionUnit> transactionUnits;

            _sync_transactions.EnterWriteLock();
            try
            {
                transactionUnits = _transactions.Values.ToList();
                this._transactions.Clear();
            }
            finally
            {
                _sync_transactions.ExitWriteLock();
            }

            foreach (TransactionUnit transactionUnit in transactionUnits)
            {
                try
                {
                    transactionUnit.Dispose();
                }
                catch (Exception ex)
                {
                    if (exc == null)
                        exc = ex;
                }
            }

            SignalWriteWaiters(Int32.MinValue);
        }



        #region "Registering Tables for Writing or Read-Commited before making operations, for avoiding deadLocks"

        /// <summary>
        /// Gets ActiveTransactionsState
        /// </summary>
        /// <returns</returns>
        public List<Diagnostic.ActiveTransactionState> Diagnostic_GetActiveTransactionsState()
        {
            List<Diagnostic.ActiveTransactionState> ret = new List<Diagnostic.ActiveTransactionState>();
            Diagnostic.ActiveTransactionState s = null;
            _sync_transactions.EnterReadLock();
            try
            {
                foreach (var t in this._transactions)
                {
                    s = new Diagnostic.ActiveTransactionState()
                    {
                      ManagedThreadId = t.Key
                    };

                    s.TablesToBeSynced.AddRange(t.Value.GetTransactionWriteTablesAwaitingReservation());

                    if (s.TablesToBeSynced.Count < 1)
                    {
                        s.TablesToBeSynced.AddRange(t.Value.GetTransactionWriteTablesNames());
                        s.AwaitingReservataion = false;

                    }

                    s.ActiveTime = DateTime.UtcNow.Subtract(t.Value.udtStart);

                    if (s.TablesToBeSynced.Count > 0)
                    {
                        s.SyncTableTime = t.Value.udtSyncStop != DateTime.MinValue ? t.Value.udtSyncStop.Subtract(t.Value.udtStart) 
                            : DateTime.UtcNow.Subtract(t.Value.udtStart);
                    }

                    ret.Add(s);                   
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
                _sync_transactions.ExitReadLock();
            }

            return ret;
        }

        object _sync_dl = new object();

        sealed class WriteReservationWaiter
        {
            internal readonly int TransactionThreadId;
            internal readonly List<string> Tables;
            internal readonly DbThreadsGator Gate = new DbThreadsGator(false);
            internal List<int> Blockers = new List<int>();

            internal WriteReservationWaiter(int transactionThreadId, List<string> tables)
            {
                TransactionThreadId = transactionThreadId;
                Tables = new List<string>(tables);
            }
        }

        readonly Dictionary<int, WriteReservationWaiter> _writeWaiters = new Dictionary<int, WriteReservationWaiter>();
        readonly List<int> _writeWaiterSequence = new List<int>();

        void RemoveWriteWaiterUnderLock(int transactionThreadId)
        {
            _writeWaiters.Remove(transactionThreadId);
            _writeWaiterSequence.Remove(transactionThreadId);
        }

        void SignalWriteWaiters(int removedTransactionThreadId)
        {
            lock (_sync_dl)
            {
                if (removedTransactionThreadId == Int32.MinValue)
                {
                    foreach (WriteReservationWaiter waiter in _writeWaiters.Values)
                        waiter.Gate.OpenGate();
                    return;
                }

                WriteReservationWaiter removedWaiter;
                if (_writeWaiters.TryGetValue(removedTransactionThreadId, out removedWaiter))
                {
                    RemoveWriteWaiterUnderLock(removedTransactionThreadId);
                    removedWaiter.Gate.OpenGate();
                }

                foreach (WriteReservationWaiter waiter in _writeWaiters.Values)
                {
                    if (waiter.Blockers.Remove(removedTransactionThreadId) && waiter.Blockers.Count == 0)
                        waiter.Gate.OpenGate();
                }
            }
        }

        bool WaitPathExists(int fromTransactionThreadId, int targetTransactionThreadId, HashSet<int> visited)
        {
            Stack<int> pending = new Stack<int>();
            pending.Push(fromTransactionThreadId);
            while (pending.Count > 0)
            {
                int current = pending.Pop();
                if (current == targetTransactionThreadId)
                    return true;
                if (!visited.Add(current))
                    continue;

                WriteReservationWaiter waiter;
                if (!_writeWaiters.TryGetValue(current, out waiter))
                    continue;
                foreach (int blocker in waiter.Blockers)
                    pending.Push(blocker);
            }
            return false;
        }


        /// <summary>
        /// Access synchronizer.
        /// All calls of the WRITE LOCATOR come over this function.
        /// </summary>
        /// <param name="transactionThreadId"></param>
        /// <param name="tablesNames"></param>
        /// <param name="calledBySynchronizer"></param>
        public void RegisterWriteTablesForTransaction(int transactionThreadId, List<string> tablesNames,bool calledBySynchronizer)
        {
            WriteReservationWaiter waiter = null;
            bool terminatingForDeadlock = false;

            try
            {
                while (true)
                {
                    TransactionUnit transactionUnit = null;
                    bool deadlock = false;
                    bool transactionMissing = false;
                    bool reservationGranted = false;

                    lock (_sync_dl)
                    {
                        if (waiter != null)
                            waiter.Gate.CloseGate();

                        _sync_transactions.EnterReadLock();
                        try
                        {
                            this._transactions.TryGetValue(transactionThreadId, out transactionUnit);
                            if (transactionUnit == null)
                            {
                                transactionMissing = true;
                            }
                            else
                            {
                                if (waiter == null)
                                {
                                    bool requiresDeadlockDetection = false;
                                    if (!calledBySynchronizer)
                                    {
                                        if (DbUserTables.TableNamesIntersect(transactionUnit.GetTransactionWriteTablesNames(), tablesNames))
                                            return;

                                        int transactionWriteTablesCount = transactionUnit.TransactionWriteTablesCount;
                                        if (_engine.Configuration.NotifyAhead_WhenWriteTablePossibleDeadlock && transactionWriteTablesCount > 0)
                                            throw new Exception("Put table \"" + tablesNames.FirstOrDefault() + "\" into tran.SynchronizeTables statement, because it will be modified");

                                        // A transaction holding no write table cannot close a wait cycle.
                                        // SynchronizeTables also reserves its complete set atomically before any write.
                                        requiresDeadlockDetection = transactionWriteTablesCount > 0;
                                    }

                                    waiter = new WriteReservationWaiter(transactionThreadId, tablesNames);
                                    _writeWaiters[transactionThreadId] = waiter;
                                    _writeWaiterSequence.Add(transactionThreadId);
                                    transactionUnit.AddTransactionWriteTablesAwaitingReservation(tablesNames);

                                    List<int> blockers = new List<int>();
                                    foreach (var other in this._transactions)
                                    {
                                        if (other.Key != transactionThreadId &&
                                            DbUserTables.TableNamesIntersect(waiter.Tables, other.Value.GetTransactionWriteTablesNames()))
                                            blockers.Add(other.Key);
                                    }

                                    // A later request may pass unrelated requests, but never an earlier
                                    // conflicting one. This prevents an exclusive writer from starving.
                                    foreach (int queuedId in _writeWaiterSequence)
                                    {
                                        if (queuedId == transactionThreadId)
                                            break;
                                        WriteReservationWaiter earlier;
                                        if (_writeWaiters.TryGetValue(queuedId, out earlier) &&
                                            DbUserTables.TableNamesIntersect(waiter.Tables, earlier.Tables) &&
                                            !blockers.Contains(queuedId))
                                            blockers.Add(queuedId);
                                    }

                                    // Blockers are monotonic while the waiter is queued: a later conflicting
                                    // request cannot pass it, and an earlier waiter keeps the same transaction id
                                    // after its reservation is granted. Only transaction removal can unblock it.
                                    waiter.Blockers = blockers;
                                    if (requiresDeadlockDetection)
                                    {
                                        foreach (int blocker in blockers)
                                        {
                                            if (WaitPathExists(blocker, transactionThreadId, new HashSet<int>()))
                                            {
                                                deadlock = true;
                                                break;
                                            }
                                        }
                                    }
                                }

                                if (!deadlock && waiter.Blockers.Count == 0)
                                {
                                    RemoveWriteWaiterUnderLock(transactionThreadId);
                                    transactionUnit.ClearTransactionWriteTablesAwaitingReservation(tablesNames);
                                    foreach (string tableName in tablesNames)
                                        transactionUnit.AddTransactionWriteTable(tableName, null);
                                }
                            }
                        }
                        finally
                        {
                            _sync_transactions.ExitReadLock();
                        }

                        if (transactionMissing)
                        {
                            if (waiter != null)
                                RemoveWriteWaiterUnderLock(transactionThreadId);
                        }
                        else if (deadlock)
                        {
                            RemoveWriteWaiterUnderLock(transactionThreadId);
                        }
                        else if (waiter != null && waiter.Blockers.Count == 0)
                        {
                            reservationGranted = true;
                        }
                    }

                    if (reservationGranted)
                    {
                        waiter.Gate.Dispose();
                        return;
                    }

                    if (transactionMissing)
                    {
                        if (waiter != null)
                            waiter.Gate.Dispose();
                        return;
                    }
                    if (deadlock)
                    {
                        terminatingForDeadlock = true;
                        Exception cleanupFailure = null;
                        try
                        {
                            waiter.Gate.Dispose();
                        }
                        catch (Exception cleanupException)
                        {
                            cleanupFailure = cleanupException;
                        }

                        try
                        {
                            this.UnregisterTransaction(transactionThreadId);
                        }
                        catch (Exception cleanupException)
                        {
                            cleanupFailure = cleanupFailure == null
                                ? cleanupException
                                : new Exception(cleanupFailure.ToString() + " --> " + cleanupException.ToString(), cleanupFailure);
                        }

                        throw cleanupFailure == null
                            ? DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTION_IN_DEADLOCK)
                            : DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTION_IN_DEADLOCK, cleanupFailure);
                    }

                    waiter.Gate.PutGateHere();
                }
            }
            catch (Exception ex)
            {
                if (terminatingForDeadlock)
                    throw;

                Exception failure = ex;
                lock (_sync_dl)
                {
                    WriteReservationWaiter registeredWaiter;
                    if (waiter != null &&
                        _writeWaiters.TryGetValue(transactionThreadId, out registeredWaiter) &&
                        Object.ReferenceEquals(waiter, registeredWaiter))
                        RemoveWriteWaiterUnderLock(transactionThreadId);
                }

                if (waiter != null)
                {
                    try
                    {
                        waiter.Gate.Dispose();
                    }
                    catch (Exception cleanupException)
                    {
                        failure = new Exception(failure.ToString() + " --> " + cleanupException.ToString(), failure);
                    }
                }

                try
                {
                    this.UnregisterTransaction(transactionThreadId);
                }
                catch (Exception cleanupException)
                {
                    failure = new Exception(failure.ToString() + " --> " + cleanupException.ToString(), failure);
                }

                throw DBreezeException.Throw(
                    DBreezeException.eDBreezeExceptions.TRANSACTION_TABLE_WRITE_REGISTRATION_FAILED,
                    failure);
            }
        }

#endregion //Eliminating Deadlocks. Registering tables for write before starting transaction operations


        /// <summary>
        /// Can return NULL (if DbIsNotOperatable)
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="transactionThreadId"></param>
        /// <returns></returns>
        public LTrie GetTable_WRITE(string tableName, int transactionThreadId)
        {
            if (!this._engine.DBisOperable)
                return null;

            TransactionUnit transactionUnit = this.GetTransactionUnit(transactionThreadId);

            if (transactionUnit != null)
            {
#if NET35 || NETr40
                if (System.Threading.Thread.CurrentThread.ManagedThreadId != transactionThreadId)
#else           
                if (Environment.CurrentManagedThreadId != transactionThreadId)
#endif     
                {
                    this.UnregisterTransaction(transactionThreadId);

                    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTION_CANBEUSED_FROM_ONE_THREAD);
                }


                //We must put Get_Table_Write through the same bottleneck as RegisterWriteTablesForTransaction
                this.RegisterWriteTablesForTransaction(transactionThreadId, new List<string> { tableName },false);
                //it will wait here till table for writing, reserved by other thread is released

                LTrie tbl = null;

                try
                {
                    tbl = this._engine.DBreezeSchema.GetTable(tableName);

                    //Adding table to transaction unit with the ITransactable interface
                    transactionUnit.AddTransactionWriteTable(tableName, tbl);    //added together with ITransactable

                    //TODO  -   THIS TABLE LTrie must be Interfaced
                    //Telling to the table that transactionThreadId Thread will modify it
                    tbl.ModificationThreadId(transactionThreadId);

                }
                catch (Exception ex)
                {
                    //Exception must come from Schema, by in-ability to get the table
                    this.UnregisterTransaction(transactionThreadId);

                    //CIRCULAR PARTLY
                    throw ex;
                }

                return tbl;
            }
            else
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTION_DOESNT_EXIST);
            }
        }


        /// <summary>
        /// Can return NULL if table doesn't exist
        /// Can return NULL (if DbIsNotOperatable)
        /// 
        /// Differs from GetTable_Write:
        /// 1. table is not registered for Write;
        /// 2. Table is not created, if doesn't exist.
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="transactionThreadId"></param>
        /// <param name="ignoreThreadIdCheck"></param>
        /// <returns></returns>
        public LTrie GetTable_READ(string tableName, int transactionThreadId, bool ignoreThreadIdCheck=false)
        {           
            if (!this._engine.DBisOperable)
                return null;

            TransactionUnit transactionUnit = this.GetTransactionUnit(transactionThreadId);

            if (transactionUnit != null)
            {
#if NET35 || NETr40
            if (!ignoreThreadIdCheck && System.Threading.Thread.CurrentThread.ManagedThreadId != transactionThreadId)
#else                
                if (!ignoreThreadIdCheck && Environment.CurrentManagedThreadId != transactionThreadId)
#endif                    
                {
                    this.UnregisterTransaction(transactionThreadId);
                    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTION_CANBEUSED_FROM_ONE_THREAD);
                }

                LTrie tbl = null;

                try
                {
                    if (!this._engine.DBreezeSchema.IfUserTableExists(tableName))
                        return null;

                    tbl = this._engine.DBreezeSchema.GetTable(tableName);
                }
                catch (Exception ex)
                {
                    //Exception must come from Schema, by in-ability to get the table
                    this.UnregisterTransaction(transactionThreadId);

                    //CIRCULAR PARTLY
                    throw ex;
                }

                return tbl;
            }
            else
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTION_DOESNT_EXIST);
            }
        }



    
        /// <summary>
        /// Commit
        /// </summary>
        /// <param name="transactionThreadId"></param>
        public void Commit(int transactionThreadId)
        {
            if (!this._engine.DBisOperable)
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.DB_IS_NOT_OPERABLE,this._engine.DBisOperableReason,new Exception());

            TransactionUnit transactionUnit = this.GetTransactionUnit(transactionThreadId);

            if (transactionUnit != null)
            {
                List<ITransactable> tablesForTransaction = transactionUnit.GetTransactionWriteTables();

                 if (tablesForTransaction.Count() == 0)
                 {
                     //DO NOTHING
                 }
                 else if (tablesForTransaction.Count() == 1)
                 {
                     try
                     {                         
                         tablesForTransaction[0].SingleCommit();
                     }
                     catch (System.Threading.ThreadAbortException ex)
                     {
                        //Rollback was ok, so we just return mistake, why commit failed
                        //We don'T make DBisOperable = false;                         
                        throw ex;
                     }
                     catch (TableNotOperableException ex1)
                     {
                         this._engine.DBisOperable = false;
                         this._engine.DBisOperableReason = "TransactionsCoordinator.Commit tablesForTransaction.Count = 1";
                         //CASCADE, WHICH MUST BRING TO DB is not opearatbale state
                         throw ex1;
                     }
                     catch (System.Exception ex)
                     {
                         //Rollback was ok, so we just return mistake, why commit failed
                         //CASCADE
                         throw ex;
                     }
                     
                 }
                 else
                 {

                    //Gettign new TransactionJournalId
                    ulong tranNumber = this._engine._transactionsJournal.GetTransactionNumber();

                    foreach (var tt in tablesForTransaction)
                    {
                        try
                        {
                            //Adding table
                            this._engine._transactionsJournal.AddTableForTransaction(tranNumber, tt);
                            tt.ITRCommit();

                        }
                        //catch (System.Threading.ThreadAbortException ex)
                        //{
                        //    //We don'T make DBisOperable = false;                         
                        //    throw ex;
                        //}
                        catch (Exception ex)
                        {
                            //SMTH HAPPENED INSIDE OF COMMIT Trying to rollBack tables
                            try
                            {
                                foreach (var tt1 in tablesForTransaction)
                                {
                                    tt1.ITRRollBack();
                                }

                                this._engine._transactionsJournal.RemoveTransactionFromDictionary(tranNumber);
                            }
                            //catch (System.Threading.ThreadAbortException ex1)
                            //{
                            //    //We don'T make DBisOperable = false;                         
                            //    throw ex1;
                            //}
                            catch (Exception ex1)
                            {
                                //CASCADE, WHICH MUST BRING TO DB is not opearable state
                                this._engine.DBisOperable = false;
                                this._engine.DBisOperableReason = "TransactionsCoordinator.Commit tablesForTransaction.Count > 1";
                                throw new Exception(ex.ToString() + " --> " + ex1.ToString());
                            }

                            //In case if rollback succeeded we throw exception brough by bad commit

                            //CASCADE from LTrieRootNode.TransactionalCommit
                            throw ex;
                        }

                    }//end of foreach

                    //Here we appear if all tables were succesfully commited (but it's not visible still for READING THREDS and all tables still have their rollback files active)

                    //We have to finish the transaction
                    try
                    {
                        this._engine._transactionsJournal.FinishTransaction(tranNumber);
                    }
                    //catch (System.Threading.ThreadAbortException ex)
                    //{
                    //    //We don'T make DBisOperable = false;                         
                    //    throw ex;
                    //}
                    catch (Exception ex)
                    {
                        this._engine.DBisOperable = false;
                        this._engine.DBisOperableReason = "TransactionsCoordinator.Commit FinishTransaction";
                        throw ex;
                    }

                }
            }
            else
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTION_DOESNT_EXIST);
            }
        }

        public void Rollback(int transactionThreadId)
        {
            if (!this._engine.DBisOperable)
                return;

            TransactionUnit transactionUnit = this.GetTransactionUnit(transactionThreadId);

            if (transactionUnit != null)
            {
                List<ITransactable> tablesForTransaction = transactionUnit.GetTransactionWriteTables();

                if (tablesForTransaction.Count() == 0)
                {
                    //DO NOTHING
                }
                else if (tablesForTransaction.Count() == 1)
                {
                    try
                    {
                        tablesForTransaction[0].SingleRollback();
                    }
                    //catch (System.Threading.ThreadAbortException ex)
                    //{
                    //    //We don'T make DBisOperable = false;                         
                    //    throw ex;
                    //}
                    catch (Exception ex)
                    {
                        this._engine.DBisOperable = false;
                        this._engine.DBisOperableReason = "TransactionsCoordinator.Rollback tablesForTransaction.Count = 1";
                        //CASCADE, WHICH MUST BRING TO DB is not opearatbale state
                        throw ex;
                    }
                    
                }
                else
                {                   
                    //Rollback MANY AT ONCE
                    try
                    {
                        foreach (var tt1 in tablesForTransaction)
                        {
                            tt1.SingleRollback();
                        }
                    }
                    //catch (System.Threading.ThreadAbortException ex1)
                    //{
                    //    //We don'T make DBisOperable = false;                         
                    //    throw ex1;
                    //}
                    catch (Exception ex1)
                    {
                        //CASCADE, WHICH MUST BRING TO DB is not opearatbale state
                        this._engine.DBisOperable = false;
                        this._engine.DBisOperableReason = "TransactionsCoordinator.Rollback tablesForTransaction.Count > 1";
                        throw ex1;
                    }
                }
            }
            else
            {
                throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTION_DOESNT_EXIST);
            }
        }


        /// <summary>
        /// Normal Engine Stop, usually in case of Main Thread or DLL disposing
        /// </summary>
        public void StopEngine()
        {
            this._engine.DBisOperable = false;
            this._engine.DBisOperableReason = "TransactionsCoordinator.StopEngine";

            this.UnregisterAllTransactions();

            //No need to Dispose Gator
            //ThreadsGator.Dispose();
        }



    }
}
