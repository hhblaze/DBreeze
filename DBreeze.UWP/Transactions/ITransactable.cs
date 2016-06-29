/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DBreeze.Transactions
{
    public interface ITransactable
    {
        void SingleCommit();
        void SingleRollback();
        void ITRCommit();
        /// <summary>
        /// Is called by Transaction Journal, to make root available for all and delete rollback file
        /// </summary>
        void ITRCommitFinished();
        void ITRRollBack();
        /// <summary>
        /// Transaction Coordinator notifies table that transaction is finished
        /// and table can clear ModificationThreadId and run RollBackProcedure (if transaction was finised without Commit)
        /// </summary>
        /// <param name="transactionThreadId"></param>
        void TransactionIsFinished(int transactionThreadId);
        string TableName { get; set; }
        //string RollBackFileName { get; }

        /// <summary>
        /// TransactionsCoordinator via this function will explain to the table that transactionThreadId thread will make modificatons.
        /// later when calling fetch functions, table will be able to return different RootNodes depending upon the thread
        /// </summary>
        /// <param name="transactionThreadId"></param>
        void ModificationThreadId(int transactionThreadId);

        //string ITRTableName { get; }

        //////void TransactionIsFinished(int transactionThreadId);

        //////void SnapshotReadLocator(long transactionId);
        //////void RestoreLocatorsFromSnapshot(long transactionId);
        ////////void InitCreateLocators();

        ///////// <summary>
        ///////// Must return WRITE TABLE Locator
        ///////// </summary>
        //////STSdb.Data.ITransaction STSdbITransaction { get; }
        
    }
}
