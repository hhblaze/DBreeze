/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.Threading;

namespace DBreeze.Utils
{
    /*
     .NET 8 optimized
     */

    internal class DbThreadsGator : IDisposable
    {
        // Replaced ManualResetEvent with ManualResetEventSlim for significantly better performance
        private readonly ManualResetEventSlim _gate;

        /// <summary>
        /// Creates open Gate
        /// </summary>
        public DbThreadsGator()
        {
            _gate = new ManualResetEventSlim(true);
        }

        public DbThreadsGator(bool gateIsOpen)
        {
            _gate = new ManualResetEventSlim(gateIsOpen);
        }

        /// <summary>
        /// Sets Gate in the code
        /// </summary>
        /// <returns></returns>
        public bool PutGateHere()
        {
            _gate.Wait();
            return true; // WaitOne() without timeout always returns true when it completes
        }

        /// <summary>
        /// If gate is closed then it will be closed timeout time in milliseconds
        /// </summary>
        /// <param name="milliseconds"></param>
        /// <returns></returns>
        public bool PutGateHere(int milliseconds)
        {
            return _gate.Wait(milliseconds);
        }

        public bool OpenGate()
        {
            _gate.Set();
            return true; // EventWaitHandle.Set() for unnamed/local events always returns true
        }

        public bool CloseGate()
        {
            _gate.Reset();
            return true; // EventWaitHandle.Reset() for unnamed/local events always returns true
        }

        public void Dispose()
        {
            _gate.Dispose();
        }
    }
}