/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who thinks that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DBreeze.Utils
{
#if NET40

    /// <summary>
    /// ManualResetEventSlim wrapper
    /// </summary>
    public class DbThreadsGator : IDisposable
    {

        System.Threading.ManualResetEventSlim gate = null;

        /// <summary>
        /// Creates open Gate
        /// </summary>
        public DbThreadsGator()
        {
            gate = new System.Threading.ManualResetEventSlim(true);
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="gateIsOpen"></param>
        public DbThreadsGator(bool gateIsOpen)
        {
            if (gateIsOpen)
                gate = new System.Threading.ManualResetEventSlim(true);
            else
                gate = new System.Threading.ManualResetEventSlim(false);
        }

        /// <summary>
        /// Sets Gate in the code
        /// </summary>
        /// <returns></returns>
        public void PutGateHere()
        {
            gate.Wait();
        }

        /// <summary>
        /// If gate is closed then it will be closed timeout time in milliseconds
        /// </summary>
        /// <param name="milliseconds"></param>
        /// <returns></returns>
        public void PutGateHere(int milliseconds)
        {
            gate.Wait(milliseconds);
        }
        
        /// <summary>
        /// OpenGate
        /// </summary>
        public void OpenGate()
        {
            gate.Set();
        }

        /// <summary>
        /// CloseGate
        /// </summary>
        public void CloseGate()
        {
            gate.Reset();
        }

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            gate.Set();
            gate.Dispose();
            gate = null;
        }
    }

    #else

    public class DbThreadsGator:IDisposable
    {

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
        /// <param name="milliseconds"></param>
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
            gate.Dispose();
            gate = null;
        }
    }
#endif
}
