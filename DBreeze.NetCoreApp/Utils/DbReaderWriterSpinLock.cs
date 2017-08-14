/* This part of the code belongs to Joe Duffy (c) 2009. 
   http://www.bluebytesoftware.com/blog/2009/01/30/ASinglewordReaderwriterSpinLock.aspx
   And is counted as free software. */

using System;
using System.Threading;

// Disabling compiler warning on a volatile field used in interlocked operations. Safe.
#pragma warning disable 0420

namespace DBreeze.Utils
{
    /// <summary>
    /// Temporar wrapper
    /// </summary>
    public class DbReaderWriterSpinLock
    {
        ReaderWriterLockSlim rwls = new ReaderWriterLockSlim();

        public DbReaderWriterSpinLock()
        {
        }

        public void EnterReadLock()
        {
            rwls.EnterReadLock();
        }

        public void ExitReadLock()
        {
            rwls.ExitReadLock();
        }

        public void EnterWriteLock()
        {
            rwls.EnterWriteLock();
        }

        public void ExitWriteLock()
        {
            rwls.ExitWriteLock();
        }

        //public void EnterUpgradeableReadLock()
        //{
        //    rwls.EnterUpgradeableReadLock();
        //}

        //public void ExitUpgradeableReadLock()
        //{
        //    rwls.ExitUpgradeableReadLock();
        //}
    }

    ///// <summary>
    ///// Doesn't have upgradeable locks.
    ///// Good in case when object using this lock must be initialized many times.
    ///// ReaderWriterLockSlim is very slow for init.
    ///// Final code is modified to work with .NET 3.5, added Thread.SpinWait(WAIT_ITERATIONS); instead of SpinWait.SpinOnce.
    ///// </summary>
    //public class DbReaderWriterSpinLock_Original
    //{
    //    private volatile int m_state;
    //    private const int MASK_WRITER_BIT = unchecked((int)0x80000000);
    //    private const int MASK_WRITER_WAITING_BIT = unchecked((int)0x40000000);
    //    private const int MASK_WRITER_BITS = unchecked((int)(MASK_WRITER_BIT | MASK_WRITER_WAITING_BIT));
    //    private const int MASK_READER_BITS = unchecked((int)~MASK_WRITER_BITS);

    //    //Varibale shows how many SpinWait-iterations should pass the thread before go to the next check.
    //    private int WAIT_ITERATIONS = 1000;

    //    public DbReaderWriterSpinLock()
    //    {
    //    }

    //    /// <summary>
    //    /// Default is 1000
    //    /// </summary>
    //    /// <param name="waitIterations"></param>
    //    public DbReaderWriterSpinLock(int waitIterations)
    //    {
    //        WAIT_ITERATIONS = waitIterations;
    //    }

    //    public void EnterWriteLock()
    //    {
    //        for (; ; )
    //        {
    //            int state = m_state;
    //            if ((state == 0 || state == MASK_WRITER_WAITING_BIT) &&
    //                Interlocked.CompareExchange(ref m_state, MASK_WRITER_BIT, state) == state)
    //                return;

    //            if ((state & MASK_WRITER_WAITING_BIT) == 0)
    //                Interlocked.CompareExchange(ref m_state, state | MASK_WRITER_WAITING_BIT, state);

    //            Thread.SpinWait(WAIT_ITERATIONS);

    //        }
    //    }

    //    public void ExitWriteLock()
    //    {
    //        Interlocked.Exchange(ref m_state, 0 | (m_state & MASK_WRITER_WAITING_BIT));
    //    }

    //    public void EnterReadLock()
    //    {

    //        for (; ; )
    //        {
    //            int state = m_state;
    //            if ((state & MASK_WRITER_BITS) == 0)
    //            {
    //                if (Interlocked.CompareExchange(ref m_state, state + 1, state) == state)
    //                    return;
    //            }

    //            Thread.SpinWait(WAIT_ITERATIONS);
    //        }

    //    }

    //    public void ExitReadLock()
    //    {
    //        for (; ; )
    //        {

    //            int state = m_state;
    //            if ((state & MASK_READER_BITS) == 0)
    //                throw new Exception("DbReaderWriterSpinLock: ExitReader - no readers");

    //            if (Interlocked.CompareExchange(
    //                ref m_state, ((state & MASK_READER_BITS) - 1) | (state & MASK_WRITER_WAITING_BIT), state) == state)
    //                return;

    //            Thread.SpinWait(WAIT_ITERATIONS);

    //        }

    //    }
    //}
}
