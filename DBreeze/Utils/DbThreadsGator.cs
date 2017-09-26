/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

#if ASYNC
using System.Threading.Tasks;
using System.Threading;
#endif

namespace DBreeze.Utils
{
    public class DbThreadsGator:IDisposable
    {

#if ASYNC
        public class AsyncManualResetEvent
        {
            private volatile TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>();

            public Task WaitAsync() { return _tcs.Task; }
            public void Set() { _tcs.TrySetResult(true); }
            public void Reset()
            {
                while (true)
                {
                    var tcs = _tcs;
                    if (!tcs.Task.IsCompleted ||
                        Interlocked.CompareExchange(ref _tcs, new TaskCompletionSource<bool>(), tcs) == tcs)
                        return;
                }
            }

        }

        AsyncManualResetEvent gate = null;

        /// <summary>
        /// Creates open Gate
        /// </summary>
        public DbThreadsGator()
        {
            gate = new AsyncManualResetEvent();
            gate.Set();
        }

        public DbThreadsGator(bool gateIsOpen)
        {
            gate = new AsyncManualResetEvent();

            if (gateIsOpen)
                gate.Set();
            else
                gate.Reset();
        }

        /// <summary>
        /// Sets Gate in the code
        /// </summary>
        /// <returns></returns>
        public async Task PutGateHere()
        {
                await gate.WaitAsync();
        }

        /// <summary>
        /// If gate is closed then it will be closed timeout time in milliseconds
        /// </summary>
        /// <param name="milliseconds"></param>
        /// <returns></returns>
        public async Task PutGateHere(int milliseconds)
        {
            await Task.WhenAny(gate.WaitAsync(), Task.Delay(milliseconds));            
        }
        
        public void OpenGate()
        {
            gate.Set();
        }

        public void CloseGate()
        {
            gate.Reset();
        }

        public void Dispose()
        {
            gate.Set();
            gate = null;
        }
#else
         System.Threading.ManualResetEvent gate = null;

        /// <summary>
        /// Creates open Gate
        /// </summary>
        public DbThreadsGator()
        {
            gate = new System.Threading.ManualResetEvent(true);
        }

        public DbThreadsGator(bool gateIsOpen)
        {
            if (gateIsOpen)
                gate = new System.Threading.ManualResetEvent(true);
            else
                gate = new System.Threading.ManualResetEvent(false);
        }

        /// <summary>
        /// Sets Gate in the code
        /// </summary>
        /// <returns></returns>
        public bool PutGateHere()
        {
            return gate.WaitOne();
        }

        /// <summary>
        /// If gate is closed then it will be closed timeout time in milliseconds
        /// </summary>
        /// <param name="milliseconds"></param>
        /// <returns></returns>
        public bool PutGateHere(int milliseconds)
        {
            return gate.WaitOne(milliseconds);
        }

        /// <summary>
        /// If gate is closed then it will be closed timeout time in milliseconds
        /// </summary>
        /// <param name="milliseconds">exitContext</param>
        /// <returns></returns>
        public bool PutGateHere(int milliseconds, bool exitContext)
        {
            return gate.WaitOne(milliseconds, exitContext);
        }

        public bool OpenGate()
        {
            return gate.Set();
        }

        public bool CloseGate()
        {
            return gate.Reset();
        }

        public void Dispose()
        {
            gate.Close();            
        }
#endif


    }
}
