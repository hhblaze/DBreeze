/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace DBreeze.SchemeInternal
{
    /// <summary>
    /// serves, cache of physical file names and corresponding virtual user table names
    /// </summary>
    internal class CachedTableNames
    {
        ReaderWriterLockSlim _sync = new ReaderWriterLockSlim();
        Dictionary<string, ulong> cache = new Dictionary<string, ulong>();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="userTableName"></param>
        /// <param name="fileName"></param>
        public void Add(string userTableName, ulong fileName)
        {
            _sync.EnterWriteLock();
            try
            {
                cache[userTableName] = fileName;
            }
            finally
            {
                _sync.ExitWriteLock();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="userTableName"></param>
        public void Remove(string userTableName)
        {
            _sync.EnterWriteLock();
            try
            {
                cache.Remove(userTableName);
            }
            finally
            {
                _sync.ExitWriteLock();
            }
        }

        /// <summary>
        /// Returns 0, if can't find
        /// </summary>
        /// <param name="userTableName"></param>
        public ulong GetFileName(string userTableName)
        {
            _sync.EnterReadLock();
            try
            {
                ulong fn = 0;
                if (!cache.TryGetValue(userTableName, out fn))
                    return 0;
                return fn;

            }
            finally
            {
                _sync.ExitReadLock();
            }
        }


    }
}
