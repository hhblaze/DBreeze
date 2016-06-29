/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those, who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DBreeze.Utils.Async
{
    public static class AsyncOperations
    {
        public static void DoAsync<TInput, TResult>(this Func<TInput, TResult> f, TInput arg, Action<TResult> callback)
        {
            //f.BeginInvoke(arg, x => callback(f.EndInvoke(x)), null);
            f.BeginInvoke(arg, x =>
            {
                if (callback != null)
                    callback(f.EndInvoke(x));
                else
                    f.EndInvoke(x);
            }
                , null);
        }

        public static void DoAsync<TInput, TResult>(this Func<TInput, TResult> f, TInput arg)
        {
            //f.BeginInvoke(arg, x => callback(f.EndInvoke(x)), null);
            f.BeginInvoke(arg, x =>
            {
                f.EndInvoke(x);
            }
                , null);
        }

        public static void DoAsync<TInput, TInput1, TResult>(this Func<TInput, TInput1, TResult> f, TInput arg, TInput1 arg1)
        {
            //f.BeginInvoke(arg, x => callback(f.EndInvoke(x)), null);
            f.BeginInvoke(arg, arg1, x =>
            {
                f.EndInvoke(x);
            }
                , null);
        }



        public static void DoAsync<TResult>(this Func<TResult> f, Action<TResult> callback)
        {
            f.BeginInvoke(x =>
            {
                if (callback != null)
                    callback(f.EndInvoke(x));
                else
                    f.EndInvoke(x);
            }
                , null);
        }



        public static void DoAsync<TResult>(this Func<TResult> f)
        {
            f.BeginInvoke(x =>
            {
                f.EndInvoke(x);
            }
                , null);
        }



        public static void DoAsync(this Action f, Action callback)
        {
            f.BeginInvoke(x => { f.EndInvoke(x); if (callback != null) callback(); }, null);
        }



        public static void DoAsync(this Action f)
        {
            IAsyncResult ar = f.BeginInvoke(x =>
            {
                //if (x.IsCompleted)
                //{

                //}
                f.EndInvoke(x);
            }, null);

        }


        public static void DoAsync<TInput>(this Action<TInput> f, TInput arg)
        {
            f.BeginInvoke(arg, x =>
            {
                f.EndInvoke(x);

            }, null);
        }


        /// <summary>
        /// Executes async, then calls Callback function
        /// </summary>
        /// <typeparam name="TInput"></typeparam>
        /// <param name="f"></param>
        /// <param name="arg"></param>
        /// <param name="callback"></param>
        public static void DoAsync<TInput>(this Action<TInput> f, TInput arg, Action callback)
        {
            f.BeginInvoke(arg, x => { f.EndInvoke(x); if (callback != null) callback(); }, null);
        }

        public static void DoAsync<TInput, TInput1>(this Action<TInput, TInput1> f, TInput arg, TInput1 arg1)
        {
            f.BeginInvoke(arg, arg1, x => { f.EndInvoke(x); }, null);
        }

        public static void DoAsync<TInput, TInput1, TInput2>(this Action<TInput, TInput1, TInput2> f, TInput arg, TInput1 arg1, TInput2 arg2)
        {
            f.BeginInvoke(arg, arg1, arg2, x => { f.EndInvoke(x); }, null);
        }

        public static void DoAsync<TInput, TInput1, TInput2, TInput3>(this Action<TInput, TInput1, TInput2, TInput3> f, TInput arg, TInput1 arg1, TInput2 arg2, TInput3 arg3)
        {
            f.BeginInvoke(arg, arg1, arg2, arg3, x => { f.EndInvoke(x); }, null);
        }
    }
}
