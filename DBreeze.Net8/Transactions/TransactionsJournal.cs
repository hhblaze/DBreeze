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
        readonly object _sync_journalIo = new object();

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
            LTrie journalToDispose = null;
            _sync_transactionsTables.EnterWriteLock();
            try
            {
                _transactionsTables.Clear();
                journalToDispose = LTrie;
                LTrie = null;
            }
            finally
            {
                _sync_transactionsTables.ExitWriteLock();
            }

            lock (_sync_journalIo)
                journalToDispose?.Dispose();
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
            catch (OperationCanceledException)
            {
                throw;
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

        public void FinishTransaction(ulong tranNumber)
        {
            Dictionary<string, ITransactable> transactionTables = null;
            _sync_transactionsTables.EnterReadLock();
            try
            {
                Dictionary<string, ITransactable> tables;
                if (_transactionsTables.TryGetValue(tranNumber, out tables))
                    transactionTables = new Dictionary<string, ITransactable>(tables, StringComparer.Ordinal);
            }
            finally
            {
                _sync_transactionsTables.ExitReadLock();
            }

            if (transactionTables == null)
                return;

            // Preserve participant enumeration order in the canonical cross-version payload.
            List<string> committedTablesNames = new List<string>(transactionTables.Keys);
            string serTbls = TransactionJournalPayloadCodec.Serialize(committedTablesNames);
            byte[] btSerTbls = Encoding.UTF8.GetBytes(serTbls);
            byte[] key = tranNumber.To_8_bytes_array_BigEndian();

            // 1. Persist the recovery marker before finalizing any table.
            lock (_sync_journalIo)
            {
                    LTrie.Add(ref key, ref btSerTbls);
                    DurabilityTestHooks.Hit("journal.before-commit-marker");
                    LTrie.Commit();
                    DurabilityTestHooks.Hit("journal.committed");
            }

            // 2. Potentially slow per-table I/O does not hold the journal state lock.
            // If this throws, the durable marker intentionally remains for startup recovery.
            foreach (var table in transactionTables.Values)
            {
                table.ITRCommitFinished();
                DurabilityTestHooks.Hit("journal.participant-finalized");
            }

            // 3. All tables are finalized; remove the marker durably.
            lock (_sync_journalIo)
            {
                    LTrie.Remove(ref key);
                    LTrie.Commit();
                    DurabilityTestHooks.Hit("journal.removed");
            }

            bool journalCanBeCompacted;
            _sync_transactionsTables.EnterWriteLock();
            try
            {
                _transactionsTables.Remove(tranNumber);
                journalCanBeCompacted = _transactionsTables.Count == 0;
            }
            finally
            {
                _sync_transactionsTables.ExitWriteLock();
            }

            if (journalCanBeCompacted)
            {
                lock (_sync_journalIo)
                {
                    // A transaction may have been registered after the first count check and
                    // may already be waiting to persist its recovery marker. Recheck while the
                    // journal itself is locked so RecreateFiles cannot erase that marker.
                    _sync_transactionsTables.EnterReadLock();
                    try
                    {
                        journalCanBeCompacted = _transactionsTables.Count == 0;
                    }
                    finally
                    {
                        _sync_transactionsTables.ExitReadLock();
                    }

                    if (journalCanBeCompacted && LTrie.Storage.Length > MaxlengthOfTransactionFile)
                    {
                        LTrie.Storage.RecreateFiles();
                        LTrie.Dispose();
                        Storage = new StorageLayer(Path.Combine(Engine.MainFolder, JournalFileName), LTrieSettings, Engine.Configuration);
                        LTrie = new LTrie(Storage) { TableName = "DBreeze.TranJournal" };
                    }
                }
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
