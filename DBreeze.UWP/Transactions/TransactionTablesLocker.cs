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

namespace DBreeze
{
    public enum eTransactionTablesLockTypes
    {
        EXCLUSIVE,
        SHARED
    }
    
    internal class TransactionTablesLocker
    {   
        Dictionary<int, internSession> _waitingSessions = new Dictionary<int, internSession>();
        Dictionary<int, internSession> _acceptedSessions = new Dictionary<int, internSession>();
        List<int> _waitingSessionSequence = new List<int>();

        DbReaderWriterLock _sync = new DbReaderWriterLock();

        object lock_disposed = new object();
        bool disposed = false;


        class internSession
        {
            public string[] tables;
            public eTransactionTablesLockTypes lockType= eTransactionTablesLockTypes.EXCLUSIVE;
            public DbThreadsGator gator = null;          
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="lockType"></param>
        /// <param name="tables"></param>
        /// <returns>false if thread grants access, false if thread is in a queue</returns>
        public bool AddSession(eTransactionTablesLockTypes lockType, string[] tables)
        {            
            lock (lock_disposed)
            {
                if (disposed)
                    return true;
            }

            internSession iSession = null;
            bool ret = true;

            _sync.EnterWriteLock();
            try
            {
                foreach (var ses in _acceptedSessions)
                {
                    if (DbUserTables.TableNamesIntersect(ses.Value.tables.ToList(), tables.ToList()))
                    {
                       if (ses.Value.lockType == eTransactionTablesLockTypes.EXCLUSIVE || lockType == eTransactionTablesLockTypes.EXCLUSIVE)
                        {
                            //Lock
                            ret = false;
                            break;
                        }
                    }
                }

                if (!ret)
                {
                    internSession xSes = null;
                    foreach (var ses in _waitingSessionSequence)
                    {

                        if (ses == Environment.CurrentManagedThreadId)
                            break;

                        _waitingSessions.TryGetValue(ses, out xSes);

                        if (DbUserTables.TableNamesIntersect(xSes.tables.ToList(), tables.ToList()))
                        {
                            if (xSes.lockType == eTransactionTablesLockTypes.EXCLUSIVE || lockType == eTransactionTablesLockTypes.EXCLUSIVE)
                            {
                                //Lock
                                ret = false;
                                break;
                            }
                        }
                    }
                }

                if (_waitingSessions.TryGetValue(Environment.CurrentManagedThreadId, out iSession))
                {
                    //This session was in the waiting list once
                    if (ret)
                    {
                        //We have to take away session from waiting list
                        iSession.gator.Dispose();
                        iSession.gator = null;
                        _waitingSessions.Remove(Environment.CurrentManagedThreadId);
                        _waitingSessionSequence.Remove(Environment.CurrentManagedThreadId);
                    }
                    else
                    {
                        iSession.gator.CloseGate();
                    }
                }
                else
                {
                    //Creating new session
                    iSession = new internSession()
                    {
                        lockType = lockType,
                        tables = tables
                    };

                    if (!ret)
                    {
                        iSession.gator = new DbThreadsGator(false);
                        _waitingSessions.Add(Environment.CurrentManagedThreadId, iSession);
                        _waitingSessionSequence.Add(Environment.CurrentManagedThreadId);
                    }
                }

                if (ret)
                {
                    //Adding into accepted sessions                    
                    _acceptedSessions.Add(Environment.CurrentManagedThreadId, iSession);
                }
            }
            finally
            {
                _sync.ExitWriteLock();
            }

            if (!ret)
            {
                //putting gate
                iSession.gator.PutGateHere();
            }

            return ret;

        }

        /// <summary>
        /// 
        /// </summary>
        public void RemoveSession()
        {
            lock (lock_disposed)
            {
                if (disposed)
                    return;
            }

            internSession iSession = null;
            List<int> ws = null;

            _sync.EnterWriteLock();
            try
            {
                if (!_acceptedSessions.TryGetValue(Environment.CurrentManagedThreadId, out iSession))
                    return; //Should not happen

                if (iSession.gator != null)
                {
                    iSession.gator.Dispose();
                    iSession.gator = null;
                }

                _acceptedSessions.Remove(Environment.CurrentManagedThreadId);

                ws = _waitingSessionSequence.ToList();
            }
            finally
            {
                _sync.ExitWriteLock();
            }


            if (ws != null && ws.Count() > 0)
            {
                foreach (int wsId in ws)
                {

                    _sync.EnterReadLock();
                    try
                    {

                        if (!_waitingSessions.TryGetValue(wsId, out iSession))
                            continue;

                    }
                    finally
                    {
                        _sync.ExitReadLock();
                    }

                    try
                    {
                        if (iSession.gator != null)
                            iSession.gator.OpenGate();
                    }
                    catch
                    {
                    }

                }
            }


        }


        /// <summary>
        /// MUST BE CALLED BY ENGINE DISPOSE (After all other DBreeze disposes)
        /// </summary>
        public void Dispose()
        { 
            lock (lock_disposed)
            {
                if (disposed)
                    return;
                disposed = true;
            }

            foreach (var ses in _waitingSessions)
            {
                if (ses.Value.gator != null)
                {
                    ses.Value.gator.OpenGate();
                    ses.Value.gator.Dispose();
                    ses.Value.gator = null;
                }
            }

             foreach (var ses in _acceptedSessions)
            {
                if (ses.Value.gator != null)
                {
                    ses.Value.gator.OpenGate();
                    ses.Value.gator.Dispose();
                    ses.Value.gator = null;
                }
            }

            _acceptedSessions.Clear();
            _waitingSessions.Clear();
            _waitingSessionSequence.Clear();

        }


    }
}
