/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

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
        private long qOpen;
        private int disposed;


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
            while (true)
            {
                long current = Volatile.Read(ref qOpen);
                if (current == Int64.MaxValue)
                    throw new InvalidOperationException("OpenTable usage counter overflowed.");

                if (Interlocked.CompareExchange(ref qOpen, current + 1, current) == current)
                    return;
            }
        }

        /// <summary>
        /// Decreases quantity of usage by one and returns true if table can be automatically closed
        /// </summary>
        /// <returns></returns>
        public bool Remove(ulong cnt)
        {
            if (cnt > Int64.MaxValue)
                throw new InvalidOperationException("OpenTable usage decrement is too large.");

            long decrement = (long)cnt;
            while (true)
            {
                long current = Volatile.Read(ref qOpen);
                if (current < decrement)
                    throw new InvalidOperationException("OpenTable usage counter underflowed.");

                long remaining = current - decrement;
                if (Interlocked.CompareExchange(ref qOpen, remaining, current) == current)
                    return remaining == 0;
            }
        }

        
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                Trie?.Dispose();
        }
    }
}
