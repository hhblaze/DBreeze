/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.IO;

using DBreeze;
using DBreeze.Storage;
using DBreeze.LianaTrie;
using DBreeze.Utils;

using DBreeze.Exceptions;

namespace DBreeze.Transactions
{
    public class TransactionsJournal : IDisposable
    {
        internal DBreezeEngine Engine=null;
        static string JournalFileName = "_DBreezeTranJrnl";

        TrieSettings LTrieSettings = null;
        IStorage Storage = null;
        LTrie LTrie = null;

        object lock_transactionNumber = new object();
        ulong _transactionNumber = 0;

        DbReaderWriterLock _sync_transactionsTables = new DbReaderWriterLock();

        /// <summary>
        /// We try to clear tranasction file, when its length is more then 10MB and if it's possible
        /// </summary>
        const long MaxlengthOfTransactionFile = 1024 * 1024 * 10;
      
        

        /// <summary>
        /// Key: transaction number, counting up from the engine start
        /// Value: Dictionary containing as a Key usertableName, as value link to the table
        /// </summary>
        Dictionary<ulong, Dictionary<string, ITransactable>> _transactionsTables = new Dictionary<ulong, Dictionary<string, ITransactable>>();

        public TransactionsJournal(DBreezeEngine DBreezeEngine)
        {
            Engine = DBreezeEngine;

            this.Init();
        }

        private void Init()
        {
            try
            {
                LTrieSettings = new TrieSettings()
                {
                     InternalTable = true,
                     //SkipStorageBuffer = true
                };

                Storage = new StorageLayer(Path.Combine(Engine.MainFolder, JournalFileName), LTrieSettings, Engine.Configuration);
                 //Storage = new TrieDiskStorage(Path.Combine(Engine.MainFolder, JournalFileName), LTrieSettings, Engine.Configuration);
                 LTrie = new LTrie(Storage);

                 LTrie.TableName = "DBreeze.TranJournal";

                 this.RestoreNotFinishedTransactions();
            }
            catch (Exception)
            {
                DisposeStorageAfterFailedInitialization();
                throw;
            }
        }

        private void DisposeStorageAfterFailedInitialization()
        {
            try
            {
                if (LTrie != null)
                    LTrie.Dispose();
                else if (Storage != null)
                    Storage.Table_Dispose();
            }
            catch
            {
                // Preserve the startup/recovery exception.
            }

            LTrie = null;
            Storage = null;
        }



        private void RecreateJournalStorage()
        {
            LTrie.RemoveAll(true);
        }

        public void Dispose()
        {
            _sync_transactionsTables.EnterWriteLock();
            try
            {
                _transactionsTables.Clear();
                
                //Disposing self trie
                if (LTrie != null)
                {
                    //LTrieStorage.Dispose();
                    LTrie.Dispose();
                }
            }
            finally
            {
                _sync_transactionsTables.ExitWriteLock();
            }
        }



        private void RestoreNotFinishedTransactions()
        {
            //TODO Trie settings from the table must be taken from schema (when they will differ)

            //STORE FILE NAME of rollback not table name
            try
            {
                byte[] btCommittedTablesNames =null;
                List<string> committedTablesNames = new List<string>();

                if (LTrie.Count(false) == 0)     //All ok
                {
                    RecreateJournalStorage();
                    return;
                }

                DBreeze.LianaTrie.LTrie ltrie = null;
                                

                foreach (var row in LTrie.IterateForward(true, false))
                {
                    btCommittedTablesNames = row.GetFullValue(true);

                    committedTablesNames = TransactionJournalPayloadCodec.Deserialize(
                        System.Text.Encoding.UTF8.GetString(btCommittedTablesNames));

                    foreach (var fn in committedTablesNames)
                    {                       
                        ltrie = Engine.DBreezeSchema.OpenTableForCommittedRecovery(fn);
                        if (ltrie != null)
                            ltrie.Dispose();
                        DurabilityTestHooks.Hit("journal.recovery-participant-finalized");
                    }

                    committedTablesNames.Clear();
                }

                //If all ok, recreate file
                RecreateJournalStorage();
                DurabilityTestHooks.Hit("journal.removed");
            }
            //catch (System.Threading.ThreadAbortException ex)
            //{
            //    //We don'T make DBisOperable = false;                         
            //    throw ex;
            //}
            catch (Exception ex)
            {
                //BRINGS TO DB NOT OPERATABLE
                this.Engine.DBisOperable = false;
                this.Engine.DBisOperableReason = "TransactionsCoordinator.RestoreNotFinishedTransaction";
                //NOT CASCADE ADD EXCEPTION
                throw DBreezeException.Throw(
                    DBreezeException.eDBreezeExceptions.CLEAN_ROLLBACK_FILES_FOR_FINISHED_TRANSACTIONS_FAILED, ex);
            }
            
        }


        /// <summary>
        /// Every table inside of the transaction before calling Transaction Commit, goes to this in-memory dictionary
        /// </summary>
        /// <param name="tranNumber"></param>
        /// <param name="table"></param>
        public void AddTableForTransaction(ulong tranNumber, ITransactable table)
        {

            _sync_transactionsTables.EnterWriteLock();
            try
            {
                Dictionary<string, ITransactable> tbls = null;
                _transactionsTables.TryGetValue(tranNumber, out tbls);

                if (tbls == null)
                {
                    tbls = new Dictionary<string, ITransactable>();
                    tbls.Add(table.TableName, table);
                    _transactionsTables.Add(tranNumber, tbls);
                }
                else
                {
                    if (!tbls.ContainsKey(table.TableName))
                        tbls.Add(table.TableName, table);
                }
            }
            catch (System.Exception)
            {
                //Called from TransactionCoordinator.Commit
                throw;
            }
            finally
            {
                _sync_transactionsTables.ExitWriteLock();
            }

        }

        /// <summary>
        /// FinishTransaction
        /// </summary>
        /// <param name="tranNumber"></param>
        public void FinishTransaction(ulong tranNumber)
        {

            //_sync_transactionsTables.EnterReadLock();
            _sync_transactionsTables.EnterWriteLock();
            try
            {
                Dictionary<string, ITransactable> tbls = null;
                _transactionsTables.TryGetValue(tranNumber, out tbls);

                if (tbls != null)
                {
                    //Starting procedure

                    //1. Saving all table names into db - needed in case if something happens (Power loss or whatever).
                    //   Then restarted TransactionalJournal will delete rollback files for these tables (they are all committed)
                   
                    List<string> committedTablesNames = new List<string>();
                    foreach (var tt in tbls)
                    {
                        committedTablesNames.Add(tt.Key);                   
                    }


                    string serTbls = TransactionJournalPayloadCodec.Serialize(committedTablesNames);
                    byte[] btSerTbls = System.Text.Encoding.UTF8.GetBytes(serTbls);

                    byte[] key = tranNumber.To_8_bytes_array_BigEndian();

                    LTrie.Add(ref key, ref btSerTbls);
                    DurabilityTestHooks.Hit("journal.before-commit-marker");
                    LTrie.Commit();
                    DurabilityTestHooks.Hit("journal.committed");

                    //2. Calling transaction End for all tables
                    try
                    {
                        foreach (var tt in tbls)
                        {
                            tt.Value.ITRCommitFinished();
                            DurabilityTestHooks.Hit("journal.participant-finalized");
                        }
                    }
                    catch (System.Threading.ThreadAbortException)
                    {
                        //CASCADE from ITRCommitFinished, brings to NON-OPERATABL
                        //CASCADE 
                        throw;
                    }
                    catch (Exception)
                    {
                        //CASCADE from ITRCommitFinished, brings to NON-OPERATABLE
                        throw;
                    }

                    //3. Deleting Record in Journal
                    LTrie.Remove(ref key);
                    LTrie.Commit();
                    DurabilityTestHooks.Hit("journal.removed");

                    //Clearing transaction number
                    tbls.Clear();
                    _transactionsTables.Remove(tranNumber);


                    //When Transaction File becomes big we try to clean it.
                    if (LTrie.Storage.Length > MaxlengthOfTransactionFile && _transactionsTables.Count() == 0)
                    {
                        LTrie.Storage.RecreateFiles();
                        LTrie.Dispose();
                       
                        Storage = new StorageLayer(Path.Combine(Engine.MainFolder, JournalFileName), LTrieSettings, Engine.Configuration);                        
                        LTrie = new LTrie(Storage);
                        LTrie.TableName = "DBreeze.TranJournal";
                    }
                }

            }
            catch (System.Threading.ThreadAbortException)
            {
                //CASCADE
                throw;
            }
            catch (System.Exception)
            {
                //CASCADE
                throw;
            }
            finally
            {
                _sync_transactionsTables.ExitWriteLock();
                //_sync_transactionsTables.ExitReadLock();
            }

        }

        /// <summary>
        /// Used in case of failed transaction of multiple tables, to clean in-memory dictionary
        /// </summary>
        /// <param name="tranNumber"></param>
        public void RemoveTransactionFromDictionary(ulong tranNumber)
        {
            _sync_transactionsTables.EnterWriteLock();
            try
            {
                Dictionary<string, ITransactable> tbls = null;
                _transactionsTables.TryGetValue(tranNumber, out tbls);

                if (tbls != null)
                {
                    tbls.Clear();                   
                }

                _transactionsTables.Remove(tranNumber);
            }
            finally
            {
                _sync_transactionsTables.ExitWriteLock();               
            }
        }

        /// <summary>
        /// Returns new transaction number
        /// </summary>
        /// <returns></returns>
        public ulong GetTransactionNumber()
        {
            ulong res = 0;
            lock (lock_transactionNumber)
            {
                _transactionNumber++;
                res = _transactionNumber;
            }
            return res;
        }
    }
}
