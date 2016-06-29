/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze.LianaTrie;

namespace DBreeze.SchemeInternal
{
    public class OpenTable:IDisposable
    {

        //TODO CHECK Sheme RenameTable, will be other approach


        public LTrie Trie = null;

        /// <summary>
        /// Quantity of open exemplars
        /// </summary>
        private ulong qOpen = 0;
        private object lock_qOpen = new object();


        //public OpenTable()
        //{
        //    Add();
        //}

        public OpenTable(LTrie trie)
        {
            this.Trie = trie;

            Add();
        }

        /// <summary>
        /// Inceases quantity of usage by one
        /// </summary>
        public void Add()
        {
            lock (lock_qOpen)
            {
                qOpen++;

                //Console.WriteLine("Add: {0}; Left: {1}", Trie.TableName, qOpen);
            }

            
        }

        /// <summary>
        /// Decreases quantity of usage by one and returns true if table can be automatically closed
        /// </summary>
        /// <returns></returns>
        public bool Remove(ulong cnt)
        {
            bool toClose = false;
            lock (lock_qOpen)
            {
                qOpen -= cnt;
                if (qOpen == 0)
                    toClose = true;

                //Console.WriteLine("Rmv: {0}; Left: {1}", Trie.TableName, qOpen);
            }

           

            return toClose;
        }

        
        public void Dispose()
        {
            if (Trie != null)
                Trie.Dispose();
        }
    }
}
