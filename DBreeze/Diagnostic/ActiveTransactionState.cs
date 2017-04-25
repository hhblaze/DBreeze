using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DBreeze.Diagnostic
{
    /// <summary>
    /// Diagnostic class showing how state of the transaction
    /// </summary>
    public class ActiveTransactionState
    {
        public ActiveTransactionState()
        {
            TablesToBeSynced = new List<string>();
            AwaitingReservataion = true;
        }

        /// <summary>
        /// ManagedThreadId
        /// </summary>
        public int ManagedThreadId { get; set; }
        /// <summary>
        /// SyncTableTime null if there is no tables to be synced or 
        /// </summary>
        public TimeSpan SyncTableTime { get; set; }
        /// <summary>
        /// ActiveTime
        /// </summary>
        public TimeSpan ActiveTime { get; set; }
        /// <summary>
        /// Tables to be modified
        /// </summary>
        public List<string> TablesToBeSynced { get; set; }
        /// <summary>
        /// From Awaiting reservation list
        /// </summary>
        public bool AwaitingReservataion { get; set; }
    }
}
