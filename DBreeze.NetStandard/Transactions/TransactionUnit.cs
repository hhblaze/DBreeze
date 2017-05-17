/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze.Utils;
using DBreeze.SchemeInternal;

namespace DBreeze.Transactions
{   
    /// <summary>
    /// This object includes class Transaction (visible for the user) and holds internally technical transaction information.
    /// </summary>
    internal class TransactionUnit:IDisposable
    {
        TransactionsCoordinator _transactionsCoordinator = null;
        /// <summary>
        /// Represents object which will be used by user, there we have all query methods, depending upon query we now if the table has to be locked or not.
        /// </summary>
        Transaction _transaction = null;

        //public TransactionUnit(TransactionsCoordinator transactionsCoordinator)
        //{  
        //    this._transactionsCoordinator = transactionsCoordinator;            
        //    this._transaction = new Transaction(this);           
        //}

        public TransactionUnit(int transactionType, TransactionsCoordinator transactionsCoordinator, eTransactionTablesLockTypes lockType, params string[] tables)            
        {
            this._transactionsCoordinator = transactionsCoordinator;
            this._transaction = new Transaction(transactionType, this, lockType, tables);        
        }


        /// <summary>
        /// Easy access to transactin coordinator for the Transaction which is visible for the user
        /// </summary>
        public TransactionsCoordinator TransactionsCoordinator
        {
            get { return this._transactionsCoordinator; }
        }

        /// <summary>
        /// Transaction visible for the user
        /// </summary>
        public Transaction Transaction
        {
            get { return this._transaction; }
        }
        
        /// <summary>
        /// 
        /// </summary>
        public int TransactionThreadId
        {
            get { return this._transaction.ManagedThreadId; }
        }

        
        /// <summary>
        /// Lock for all tables definitions inside current transaction
        /// </summary>
        //DbReaderWriterLock _sync_transactionWriteTables = new DbReaderWriterLock();
        DbReaderWriterLock _sync_transactionWriteTables = new DbReaderWriterLock();
        
        /// <summary>
        /// It holds all tables marked for possible mutations
        /// </summary>
        Dictionary<string, ITransactable> _transactionWriteTables = new Dictionary<string, ITransactable>();

        /// <summary>
        /// List of tables which are waiting for reservation for writing, we need it to predict deadlock situations.
        /// </summary>
        List<string> _transactionWriteTablesAwaitingReservation = new List<string>();


        public void Dispose()
        {
            
            _sync_transactionWriteTables.EnterReadLock();
            try
            {
                if (this._transactionsCoordinator.GetSchema.Engine.DBisOperable)
                {
                    foreach (var tt in _transactionWriteTables.Where(r => r.Value != null))
                    {
                        tt.Value.TransactionIsFinished(this._transaction.ManagedThreadId);
                    }
                }
            }
            catch (System.Exception ex)
            {
                throw ex;
            }
            finally
            {
                _sync_transactionWriteTables.ExitReadLock();
            }

            //clearing all transaction tables

            _sync_transactionWriteTables.EnterWriteLock();
            try
            {
                _transactionWriteTables.Clear();    //holds table name + ITransactable interface                
                _transactionWriteTablesAwaitingReservation.Clear(); //holds tables with awaiting reservation, means that transaction thread is blocked
            }
            catch (System.Exception ex)
            {
                throw ex;
            }
            finally
            {
                _sync_transactionWriteTables.ExitWriteLock();
            }
        }

        #region "Tables Awaiting "WRITE" reservation "

        /// <summary>
        /// Used by TransactionCoordinator.RegisterWriteTablesForTransaction
        /// </summary>
        /// <param name="tablesNames"></param>
        public void AddTransactionWriteTablesAwaitingReservation(List<string> tablesNames)
        {
            _sync_transactionWriteTables.EnterWriteLock();
            try
            {
                _transactionWriteTablesAwaitingReservation.AddRange(tablesNames.Except(_transactionWriteTablesAwaitingReservation));
            }
            catch (System.Exception ex)
            {
                //CASCADE
                throw ex;
            }
            finally
            {
                _sync_transactionWriteTables.ExitWriteLock();
            }
        }

        public void ClearTransactionWriteTablesAwaitingReservation(List<string> tablesNames)
        {
            _sync_transactionWriteTables.EnterWriteLock();
            try
            {
                _transactionWriteTablesAwaitingReservation.RemoveAll(r => tablesNames.Contains(r));
            }
            finally
            {
                _sync_transactionWriteTables.ExitWriteLock();
            }
        }

        public List<string> GetTransactionWriteTablesAwaitingReservation()
        {
            _sync_transactionWriteTables.EnterReadLock();
            try
            {
                return _transactionWriteTablesAwaitingReservation;
            }
            finally
            {
                _sync_transactionWriteTables.ExitReadLock();
            }
        }
        #endregion

        int transactionWriteTablesCount = 0;
        /// <summary>
        /// Adds a table which will take place in transaction operations.
        /// Reserved has value null, Real (which are acquired by Transaction for Write) has ITransactable filled.
        /// ITransactable = null, gives to differ from toched and reserved.
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="table">if null - will be added to Reservation table</param>
        public void AddTransactionWriteTable(string tableName, ITransactable table)
        {

            _sync_transactionWriteTables.EnterWriteLock();
            try
            {

                if (!_transactionWriteTables.ContainsKey(tableName))
                {
                    this._transactionWriteTables.Add(tableName, table);

                    transactionWriteTablesCount++;
                }
                else
                {
                    if (_transactionWriteTables[tableName] == null)
                        _transactionWriteTables[tableName] = table;
                }

            }
            catch (System.Exception ex)
            {
                //CIRCULAR
                throw ex;
            }
            finally
            {
                _sync_transactionWriteTables.ExitWriteLock();
            }
        }


        /// <summary>
        /// Doesn't need pattern check
        /// Returns all tables which took place in write operation for the current transaction
        /// Without reserved as Text tables only which have real ITransactable inside
        /// </summary>
        /// <returns></returns>
        public List<ITransactable> GetTransactionWriteTables()
        {
            _sync_transactionWriteTables.EnterReadLock();
            try
            {
                return _transactionWriteTables.Where(r => r.Value != null).Select(r => r.Value).ToList();
            }
            catch (System.Exception ex)
            {
                throw ex;
            }
            finally
            {
                _sync_transactionWriteTables.ExitReadLock();
            }

        }

        /// <summary>
        /// returns count of reserved tables, used by transaction just to find out if reservation (or first table modification) was done or not.
        /// reservation can be done only once.
        /// </summary>
        /// <returns></returns>
        public int TransactionWriteTablesCount
        {
            get
            {
                return transactionWriteTablesCount;
            }
        }


        /// <summary>
        /// Used inside of Transaction, we can choose fot READ or READ_SYNCHRO for READ FUNCs
        /// </summary>
        /// <param name="tableName"></param>
        /// <returns></returns>
        public bool If_TableIsReservedForWrite(string tableName)
        {
            _sync_transactionWriteTables.EnterReadLock();
            try
            {
                //MUST NOT BE USED PATTERN CHECK, look FastTest TEST_TABLE_RESERVED_FOR_WRITE and TEST_TABLE_RESERVED_FOR_WRITE_1
                //////return DbUserTables.TableNamesContains(_transactionWriteTables.Keys.ToList(), tableName);

                ITransactable val;
                return _transactionWriteTables.TryGetValue(tableName,out val);


                //foreach (var tin in _transactionWriteTables)
                //{
                //    if (tin.Key.Equals(tableName, StringComparison.OrdinalIgnoreCase))
                //        return true;
                //}

                //return false;

                //var kvp = _transactionWriteTables.Where(r => r.Key.Equals(tableName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

                //returns true if tableName is found in the list and it means table is reserved.
                //In new scheme it will check RegexCompliant, together with TransactionsCoordinator.RegisterWriteTablesForTransaction
                //return !(kvp.Key == null);
            }
            catch (System.Exception ex)
            {
                throw ex;
            }
            finally
            {
                _sync_transactionWriteTables.ExitReadLock();
            }
        }


        ///// <summary>
        ///// NOT USED ANYWHERE
        ///// 
        ///// Get ITransactable by tableName.
        ///// Can return NULL, if table is not in a list of WRITE tables.
        ///// </summary>
        ///// <param name="tableName"></param>
        ///// <returns></returns>
        //public ITransactable GetTransactionWriteTable(string tableName)
        //{
        //    _sync_transactionWriteTables.EnterReadLock();
        //    try
        //    {
        //       r.KEy is tablename
        //        return _transactionWriteTables.Where(r => r.Value != null && r.Value.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase)).Select(r => r.Value).FirstOrDefault();
        //    }
        //    catch (System.Exception ex)
        //    {
        //        throw ex;
        //    }
        //    finally
        //    {
        //        _sync_transactionWriteTables.ExitReadLock();
        //    }

        //}


        /// <summary>
        /// Returns only table names for reservation
        /// </summary>
        /// <returns></returns>
        public List<string> GetTransactionWriteTablesNames()
        {
            _sync_transactionWriteTables.EnterReadLock();
            try
            {
                return _transactionWriteTables.Keys.ToList();
            }
            catch (System.Exception ex)
            {
                throw ex;
            }
            finally
            {
                _sync_transactionWriteTables.ExitReadLock();
            }

        }


        /// <summary>
        /// 
        /// </summary>
        public void Commit()
        {
            //calling for each table write table commit
            _sync_transactionWriteTables.EnterReadLock();
            try
            {
                foreach (var tt in _transactionWriteTables.Where(r => r.Value != null))
                {
                    tt.Value.ITRCommit();
                }
            }
            catch (System.Exception ex)
            {
                //CASCADE
                throw ex;
            }
            finally
            {
                _sync_transactionWriteTables.ExitReadLock();
            }
        }


        /// <summary>
        /// 
        /// </summary>
        public void RollBack()
        {
            //calling for each table write table rollback
            _sync_transactionWriteTables.EnterReadLock();
            try
            {
                foreach (var tt in _transactionWriteTables.Where(r => r.Value != null))
                {
                    tt.Value.ITRRollBack();
                }
            }
            catch (System.Exception ex)
            {
                //CASCADE
                throw ex;
            }
            finally
            {
                _sync_transactionWriteTables.ExitReadLock();
            }
        }

    }
}
