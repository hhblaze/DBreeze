/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
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

            TransactionUnit transactionUnit = new TransactionUnit(transactionType, this, lockType, tables);
            

            //Checking if the same transaction already exists in the list of Transactions. 
            //It could happen in case of abnormal termination of parallel thread, without disposing of the transaction.
            //So we delete pending transaction first, then create new one.
            bool reRun = false;
            _sync_transactions.EnterReadLock();
            try
            {
                if (this._transactions.ContainsKey(transactionUnit.TransactionThreadId))
                {
                    reRun = true;
                }
            }
            finally
            {
                _sync_transactions.ExitReadLock();
            }

            if (reRun)
            {
                UnregisterTransaction(transactionUnit.TransactionThreadId);
                return GetTransaction(transactionType, lockType, tables);
            }

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
            TransactionUnit transactionUnit = this.GetTransactionUnit(transactionThreadId);
            Exception exc = null;

            if (transactionUnit != null)
            {
                _sync_transactions.EnterWriteLock();
                try
                {
                    this._transactions.Remove(transactionUnit.Transaction.ManagedThreadId);
                    transactionUnit.Dispose();
                }
                catch (System.Exception ex)
                {
                    exc = ex;
                }
                finally
                {
                    _sync_transactions.ExitWriteLock();
                }

                
            }

            //letting other threads, which tried to register tables for modification and were blocked, to re-try the operation.
            //mreWriteTransactionLock.Set();
            ThreadsGator.OpenGate();

            if (exc != null)
                throw exc;
            
        }

        /// <summary>
        /// Is called by the engine on Dispose.
        /// </summary>
        public void UnregisterAllTransactions()
        {
            Exception exc = null;

            _sync_transactions.EnterWriteLock();
            try
            {
                foreach (var transactionUnit in _transactions.Values)
                {
                    try
                    {
                        transactionUnit.Dispose();
                    }
                    catch (Exception ex)
                    {
                        if(exc == null)
                            exc = ex;
                    }
                    
                }

                this._transactions.Clear();
            }
            catch (System.Exception ex)
            {
                if (exc == null)
                    exc = ex;
            }
            finally
            {
                _sync_transactions.ExitWriteLock();

                //lettign other threads, which tried to register tables for modification and were blocked, to re-try the operation.
                //mreWriteTransactionLock.Set();
                ThreadsGator.OpenGate();

                //No need here
                //////if (exc != null)
                //////    throw exc;
            }

            
        }



        #region "Registering Tables for Writing or Read-Commited before making operations, for avoiding deadLocks"

        //System.Threading.ManualResetEvent mreWriteTransactionLock = new System.Threading.ManualResetEvent(true);
        DbThreadsGator ThreadsGator = new DbThreadsGator();
        object _sync_dl = new object();

        /// <summary>
        /// Access synchronizer.
        /// All calls of the WRITE LOCATOR come over this function.
        /// </summary>
        /// <param name="transactionThreadId"></param>
        /// <param name="tablesNames"></param>
        public void RegisterWriteTablesForTransaction(int transactionThreadId, List<string> tablesNames,bool calledBySynchronizer)
        {
            //in every transaction unit we got a list of reserved for WRITING tables

            //if we have in tablesNames one of the tables which is in this list we have to stop the thread with mre
            bool toWaitTillTransactionIsFinished = false;
            bool breakOuterLoop = false;
            TransactionUnit transactionUnit = null;
            bool deadlock = false;

            while (true)    //loop till thread will get full access to write tables
            {
                toWaitTillTransactionIsFinished = false;
                breakOuterLoop = false;
                deadlock = false;

                //only tables required for writing or read-commited will have to go over this fast bottleneck
                lock (_sync_dl)
                {
                    _sync_transactions.EnterReadLock();
                    try
                    {

                        this._transactions.TryGetValue(transactionThreadId, out transactionUnit);

                        if (transactionUnit == null)
                            return; //transaction doesn't exist anymore, gracefully goes out

                       
                        if (!calledBySynchronizer)
                        {
                            //Here we are in case if Registrator is called by WriteTableCall, so we check intersections
                            //Between reserved Write tables and current table using patterns intersections technique.
                            //If they intersect we let the thread to proceed
                            if (DbUserTables.TableNamesIntersect(transactionUnit.GetTransactionWriteTablesNames(), tablesNames))
                            {
                                return;                       
                            }
                        }


                        //iterating over all open transactions except self, finding out if desired tables are locked by other threads.
                        foreach (var tu in this._transactions.Where(r => r.Value.TransactionThreadId != transactionThreadId))
                        {
                            foreach (string tableName in tu.Value.GetTransactionWriteTablesNames())
                            {
                                //if (tablesNames.Contains(tableName))
                                if (DbUserTables.TableNamesContains(tablesNames,tableName))
                                {
                                    //
                                    //++++++++++++++ here we can register all tables which are waiting for write lock release
                                    transactionUnit.AddTransactionWriteTablesAwaitingReservation(tablesNames);

                                    //++++++++++++++ if thread, who has locked this table has another table in a "waiting for reservation" blocked by this thread - it's a deadlock                                    
                                    //if (transactionUnit.GetTransactionWriteTablesNames().Intersect(tu.Value.GetTransactionWriteTablesAwaitingReservation()).Count() > 0)
                                    if (DbUserTables.TableNamesIntersect(transactionUnit.GetTransactionWriteTablesNames(),tu.Value.GetTransactionWriteTablesAwaitingReservation()))
                                    {
                                        //we got deadlock, we will stop this transaction with an exception
                                        deadlock = true;
                                    }

                                    //other thread has reserved table for the transaction we have to wait
                                    toWaitTillTransactionIsFinished = true;
                                    breakOuterLoop = true;

                                    if (!deadlock)
                                    {
                                        ThreadsGator.CloseGate();  //closing gate only if no deadlock situation  
                                        //mreWriteTransactionLock.Reset();   //setting to signalled only in non-deadlock case
                                    }

                                    break;
                                }
                            }

                            if (breakOuterLoop)
                                break;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        this.UnregisterTransaction(transactionThreadId);

                        throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTION_TABLE_WRITE_REGISTRATION_FAILED,ex);                        
                        
                    }
                    finally
                    {
                        _sync_transactions.ExitReadLock();
                    }

                    //if(true) this thread owns all table for modification lock
                    if (!toWaitTillTransactionIsFinished)
                    {
                        //+++++++++++ Here we can clear all table names in the waiting reservation queue
                        transactionUnit.ClearTransactionWriteTablesAwaitingReservation(tablesNames);

                        //we have to reserve for our transaction all tables
                        foreach (var tbn in tablesNames)
                            transactionUnit.AddTransactionWriteTable(tbn, null);

                        return;
                    }

                }//end of lock


                if (deadlock)
                {
                    this.UnregisterTransaction(transactionThreadId);

                    throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.TRANSACTION_IN_DEADLOCK);                    
                }

                if (toWaitTillTransactionIsFinished)
                {
                    //blocking thread which requires busy tables for writing, till they are released
                    //ThreadsGator.PutGateHere(20000);    //every 20 second (or by Gate open we give a chance to re-try, for safety reasons of hanged threads, if programmer didn't dispose DBreeze process after the programm end)
                    ThreadsGator.PutGateHere();
                    //mreWriteTransactionLock.WaitOne();
                }
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
                if (Environment.CurrentManagedThreadId != transactionThreadId)
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
        public LTrie GetTable_READ(string tableName, int transactionThreadId, bool ignoreThreadIdCheck = false)
        {           
            if (!this._engine.DBisOperable)
                return null;

            TransactionUnit transactionUnit = this.GetTransactionUnit(transactionThreadId);

            if (transactionUnit != null)
            {
                if (!ignoreThreadIdCheck && Environment.CurrentManagedThreadId != transactionThreadId)
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
                    catch (OperationCanceledException ex)
                    {
                        throw ex;
                    }
                    //catch (System.Threading.ThreadAbortException ex)
                    // {
                    //     //We don'T make DBisOperable = false;                         
                    //     throw ex;
                    // }
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
                            catch (OperationCanceledException ex1)
                            {
                                throw ex1;
                            }
                            //catch (System.Threading.ThreadAbortException ex1)
                            // {
                            //     //We don'T make DBisOperable = false;                         
                            //     throw ex1;
                            // }
                             catch (Exception ex1)
                             {
                                 //CASCADE, WHICH MUST BRING TO DB is not opearatbale state
                                 this._engine.DBisOperable = false;
                                 this._engine.DBisOperableReason = "TransactionsCoordinator.Commit tablesForTransaction.Count > 1";
                                 throw new Exception(ex.ToString() +" --> " + ex1.ToString());
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
                    catch (OperationCanceledException ex)
                    {
                        throw ex;
                    }
                    //catch (System.Threading.ThreadAbortException ex)
                    // {
                    //     //We don'T make DBisOperable = false;                         
                    //     throw ex;
                    // }
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
                    catch (OperationCanceledException ex)
                    {
                        throw ex;
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
                    catch (OperationCanceledException ex)
                    {
                        throw ex;
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
