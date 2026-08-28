/*
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.Diagnostics;

namespace DBreeze.Storage
{
    /// <summary>
    /// Compile-time-only durability checkpoints. Calls to Hit are removed by the
    /// compiler unless DBREEZE_DURABILITY_TEST_HOOKS is defined.
    /// </summary>
    internal static class DurabilityTestHooks
    {
#if DBREEZE_DURABILITY_TEST_HOOKS
        internal static Action<string> Handler = null;
        internal static Action<string, byte[]> DurableFileHandler = null;
#endif

        [Conditional("DBREEZE_DURABILITY_TEST_HOOKS")]
        internal static void Hit(string checkpoint)
        {
#if DBREEZE_DURABILITY_TEST_HOOKS
            Action<string> handler = Handler;
            if (handler != null)
                handler(checkpoint);
#endif
        }
    }
}
