/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Threading;

namespace DBreeze.Utils
{

    /// <summary>
    /// Wrapper For System.Threading.ReaderWriterLockSlim
    /// In case if better algorithm will be found
    /// </summary>
    public class DbReaderWriterLock
    {
        ReaderWriterLockSlim rwls = new ReaderWriterLockSlim();

        public DbReaderWriterLock()
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

        public void EnterUpgradeableReadLock()
        {
            rwls.EnterUpgradeableReadLock();
        }

        public void ExitUpgradeableReadLock()
        {
            rwls.ExitUpgradeableReadLock();
        }
    }
}
