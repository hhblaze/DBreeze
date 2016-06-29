/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

using DBreeze.Utils;

namespace DBreeze.Diagnostic
{

    public static class SpeedStatistic
    {
        public class Counter
        {
            ulong cnt = 0;
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

            string name = "";

            public Counter(string counterName)
            {
                this.name = counterName;
            }

            public void Start()
            {
                cnt++;
                sw.Start();
            }

            public void Stop()
            {                
                sw.Stop();
            }

            public long ElapsedMs
            {
                get { return sw.ElapsedMilliseconds; }
            }

            public long ElapsedTicks
            {
                get { return sw.ElapsedTicks; }
            }

            public ulong QuantityRuns
            {
                get { return cnt; }
            }

            public void Clear()
            {
                sw.Stop();
                sw.Reset();
                cnt = 0;
            }

            public void PrintOut()
            {
                string output = String.Format("{0}: {1}; Time: {2} ms; {3} ticks ", this.name, cnt, sw.ElapsedMilliseconds, sw.ElapsedTicks);
                if (ToConsole)
                    System.Diagnostics.Debug.WriteLine(output);
                else
                    System.Diagnostics.Debug.WriteLine(output);
                
            }
        }

        static DbReaderWriterLock _sync = new DbReaderWriterLock();
        static Dictionary<string, Counter> _cntrs = new Dictionary<string, Counter>();

        /// <summary>
        /// Default is Debug.WriteLine, can be changed to Console.WriteLine
        /// </summary>
        public static bool ToConsole = false;

        /// <summary>
        /// Starts counter
        /// </summary>
        /// <param name="counterName"></param>
        public static void StartCounter(string counterName)
        {


            _sync.EnterUpgradeableReadLock();
            try
            {
                if (_cntrs.ContainsKey(counterName))
                {
                    _cntrs[counterName].Start();
                    return;
                }

                _sync.EnterWriteLock();
                try
                {
                    if (_cntrs.ContainsKey(counterName))
                    {
                        _cntrs[counterName].Start();
                        return;
                    }

                    Counter nc = new Counter(counterName);
                    _cntrs.Add(counterName, nc);

                    nc.Start();
                }
                finally
                {
                    _sync.ExitWriteLock();
                }
            }
            finally
            {
                _sync.ExitUpgradeableReadLock();
            }


        }

        /// <summary>
        /// Stops counter
        /// </summary>
        /// <param name="counterName"></param>
        public static void StopCounter(string counterName)
        {

            _sync.EnterReadLock();
            try
            {
                if (_cntrs.ContainsKey(counterName))
                {
                    _cntrs[counterName].Stop();
                }
            }
            finally
            {
                _sync.ExitReadLock();
            }

        }

        /// <summary>
        /// Returns Counter object.
        /// Can return NULL if counter not found
        /// </summary>
        /// <param name="counterName"></param>
        /// <returns></returns>
        public static Counter GetCounter(string counterName)
        {
            Counter cnt = null;
            _sync.EnterReadLock();
            try
            {
                if (_cntrs.ContainsKey(counterName))
                {
                    cnt = _cntrs[counterName];
                }
            }
            finally
            {
                _sync.ExitReadLock();
            }

            return cnt;
        }

        public static void ClearAll()
        {

                _sync.EnterWriteLock();
                try
                {
                    foreach (var cnt in _cntrs)
                    {
                        cnt.Value.Stop();
                    }

                    _cntrs.Clear();
                }
                finally
                {
	                _sync.ExitWriteLock();
                }

        }


        /// <summary>
        /// Prints out stat for all counters without clearing statistic 
        /// </summary>
        public static void PrintOut()
        {
            PrintOut(false);
        }

        /// <summary>
        /// Prints out counter without clearing statistic for this counter
        /// </summary>
        /// <param name="counterName"></param>
        public static void PrintOut(string counterName)
        {

            PrintOut(counterName, false);
            //_sync.EnterReadLock();
            //try
            //{
            //    if (_cntrs.ContainsKey(counterName))
            //    {
            //        var cnt = _cntrs[counterName];
            //        Console.WriteLine("{0}: {1}; Time: {2} ms; {3} ticks ", counterName, cnt.QuantityRuns, cnt.ElapsedMs, cnt.ElapsedTicks);
            //    }
            //}
            //finally
            //{
            //    _sync.ExitReadLock();
            //}           

        }

        /// <summary>
        /// Prints out specified counter.
        /// </summary>
        /// <param name="counterName"></param>
        /// <param name="withClearingCounter"></param>
        public static void PrintOut(string counterName,bool withClearingCounter)
        {
            _sync.EnterReadLock();
            try
            {
                if (_cntrs.ContainsKey(counterName))
                {
                    var cnt = _cntrs[counterName];

                    string output = String.Format("{0}: {1}; Time: {2} ms; {3} ticks ", counterName, cnt.QuantityRuns, cnt.ElapsedMs, cnt.ElapsedTicks);
                    if (ToConsole)
                        System.Diagnostics.Debug.WriteLine(output);
                    else
                        System.Diagnostics.Debug.WriteLine(output);

                    if (withClearingCounter)
                        cnt.Clear();
                }
            }
            finally
            {
                _sync.ExitReadLock();
            }    
        }
       
        /// <summary>
        /// Prints out stats for all counters
        /// </summary>
        /// <param name="withResetingStatistic">resets statistic</param>
        public static void PrintOut(bool withResetingStatistic)
        {

            _sync.EnterReadLock();
            try
            {
                foreach (var cnt in _cntrs)
                {
                    string output = String.Format("{0}: {1}; Time: {2} ms; {3} ticks ", cnt.Key, cnt.Value.QuantityRuns, cnt.Value.ElapsedMs, cnt.Value.ElapsedTicks);
                    if (ToConsole)
                        System.Diagnostics.Debug.WriteLine(output);
                    else
                        System.Diagnostics.Debug.WriteLine(output);
                    
                }
            }
            finally
            {
                _sync.ExitReadLock();
            }

            if (withResetingStatistic)
                ClearAll();

        }
    }


}
