﻿/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/
#if !NET35
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DBreeze.Utils
{



    /// <summary>
    /// Once added dimension must stay. 
    /// mkd.Add("dsfs", 17L, "key2");
    /// mkd.Add(new List&lt;string&gt;{ "w1", "w2" }, 11L, "key22");
    /// var mkdRes = mkd.Get&lt;string&gt;(17L, "key2");
    /// var mkdResBool1 = mkd.TryGetValue&lt;List&lt;string&gt;&gt;(out var dder1, 11L, "key22");
    /// </summary>
    public class MultiKeySortedDictionary
    {
        #region "tests"
        /*
         
          MultiKeySortedDictionary mkd = new MultiKeySortedDictionary();


        //Checking Add, Get, TryGetValue

            //mkd.Add("dsfs", "1", 2, 34L, 3m);
            mkd.Add("dsfs", 17L, 3L);

            //mkd.Add("dsfs", 17L); //Wrong add dimension is checked

            mkd.Add("dsfs3", 17L, 5L);
            mkd.Add("dsfs32", 17L, 5L);

            mkd.Add("apa", 10L, 7L);

            mkd.Add(new List<string> { "w1", "w2" }, 11L, 4L);


            var mkdRes = mkd.Get<string>(10L, 7L);

            var mkdRes1 = mkd.Get<List<string>>(11L, 4L);

            var mkdResBool = mkd.TryGetValue<List<string>>(out var dder, 11L, 4L);

            //var mkdResBool1 = mkd.TryGetValue<List<string>>(out var dder1, 11L, 6L);

            //var mkdResBool1 = mkd.TryGetValue<List<string>>(out var dder1, 11L);  //Wrong get dimension is checked

        
        //Checking Ulongs to longs


            mkd.Add("w1", ulong.MaxValue, ulong.MinValue);
            mkd.Add("w2", (ulong)1, (ulong)0);
            mkd.Add("w3", (ulong)100, (ulong)500);

            var v4 = mkd.Get<string>(ulong.MaxValue, ulong.MinValue);
            var v41 = mkd.Get<string>((ulong)1, (ulong)0);
            var v42 = mkd.Get<string>((ulong)100, (ulong)500);

            foreach (var el in mkd.GetAll())
            {
                ulong k1 = MultiKeySortedDictionary.MapLongToUlong((long)el[0]);
                ulong k2 = MultiKeySortedDictionary.MapLongToUlong((long)el[1]);
                string val = (string)el[2];
            }

        //Checking GetAll

         //mkd.Add("w1", int.MaxValue);
            //mkd.Add("w2", (int)1);
            //mkd.Add("w3", (int)100);

            //mkd.Add("w1", int.MaxValue, int.MinValue);
            //mkd.Add("w2", (int)1, (int)0);
            //mkd.Add("w3", (int)100, (int)500);

            //mkd.Add("w1", int.MaxValue, int.MinValue);
            //mkd.Add("w2", (int)1, (int)0);
            //mkd.Add("w21", (int)1, (int)1);
            //mkd.Add("w21", (int)1, (int)2);
            //mkd.Add("w3", (int)100, (int)500);


            mkd.Add("w1", int.MaxValue, int.MinValue, 12);
            mkd.Add("w1", int.MaxValue, int.MinValue, 14);
            mkd.Add("w2", (int)1, (int)0, 44);
            mkd.Add("w21", (int)1, (int)1, 12);
            mkd.Add("w21", (int)1, (int)1, 13);
            mkd.Add("w21", (int)1, (int)2, 22);
            mkd.Add("w3", (int)100, (int)500, 15);
            mkd.Add("w3", (int)100, (int)500, 16);


            //mkd.Add("w1", ulong.MaxValue, ulong.MinValue);
            //mkd.Add("w2", (ulong)1, (ulong)0);
            //mkd.Add("w3", (ulong)100, (ulong)500);

            //var v4 = mkd.Get<string>(ulong.MaxValue, ulong.MinValue);
            //var v41 = mkd.Get<string>((ulong)1, (ulong)0);
            //var v42 = mkd.Get<string>((ulong)100, (ulong)500);

            foreach (var el in mkd.GetAll())
            {
                //ulong k1 = MultiKeySortedDictionary.MapLongToUlong((long)el[0]);
                //ulong k2 = MultiKeySortedDictionary.MapLongToUlong((long)el[1]);
                //string val = (string)el[2];
            }

         */
        #endregion

        SortedDictionary<object, object> d = new SortedDictionary<object, object>();

        /// <summary>
        /// Sync object. Not instantiated
        /// </summary>
        public ReaderWriterLockSlim Sync = null;


        int dimension = -1;

        /// <summary>
        /// Total count of elements in MKD
        /// </summary>
        public long Count = 0;

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <param name="keys"></param>
        public void Add<T>(T value, params object[] keys)
        {
            // bool firstAdd
            int tp = keys.Length;

            if (dimension > -1 && tp != dimension)
                throw new Exception("Key dimension is " + dimension);

            if (dimension == -1 && tp == 0)
                throw new Exception("Keys are not supplied");

            SortedDictionary<object, object> cd = d;
            int p = 0;
            object skt = null;

            foreach (var kt in keys)
            {
                if (kt == null)
                    throw new Exception("Unsupported key type NULL");

                //skt = GetKey(kt, true);
                skt = kt;

                if (!cd.TryGetValue(skt, out var obj))
                {
                    if (p == tp - 1)
                    {//last
                        cd[skt] = value;
                        Count++;
                    }
                    else
                    {
                        cd[skt] = new SortedDictionary<object, object>();
                        cd = (SortedDictionary<object, object>)cd[skt];
                    }
                }
                else
                {
                    if (p == tp - 1)
                    {//last
                        cd[skt] = value;
                    }
                    else
                        cd = (SortedDictionary<object, object>)obj;

                }

                p++;

            }

            if (dimension == -1)
                dimension = p;
        }

        /// <summary>
        /// Returns subSortedDictionary content starting from key (also checked border values, null keys, key that equals to dimension etc...)
        /// <para>mkd.Add("a1", 1, 1 ,1);</para>
        /// <para>mkd.Add("a3", 1, 2, 3);</para>
        /// <para>mkd.Add("a4", 1, 2, 4);</para>
        /// <para>mkd.Add("a4", 1, 2, 5);</para>
        /// <para>mkd.Add("a9", 1, 3, 9);</para>
        /// <para>mkd.Add("a10", 1, 3, 10);</para>
        /// <para>mkd.Add("a7", 2, 1, 7);</para>
        /// <para>mkd.Add("a8", 2, 2, 8);</para>
        /// <para>foreach (var el in mkd.GetByKeyStart(2)){} returns 2 values</para>
        /// <para>foreach (var el in mkd.GetByKeyStart(1,2)){} returns 3 values</para>        
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        public IEnumerable<List<object>> GetByKeyStart(params object[] keys)
        {
            /*
             WarehouseId    PlaceId    ArticleId  ValueArticle
             1              1           1
             1              2           3
             1              2           4
             2              1           7
             2              1           3

            Possible questions:
                - GiveMeAll that belong to WarehouseId 1 -> results to 3 returns
                - GiveMeAll that belong to WarehouseId 1 and PlaceId 2 -> results to 2 returns
             */

            if (keys == null || keys.Length == 0)
            {
                foreach (var el in GetAll())
                {
                    yield return el;
                }
            }
            else if (dimension == (keys.Length))
            {
                List<object> l = new List<object>();
                foreach (var el in keys)
                    l.Add(el);

                if (this.TryGetValue<object>(out var getRes, keys))
                {
                    l.Add(getRes);
                    yield return l;
                }
            }
            else if (dimension > -1)
            {
                List<object> l = new List<object>();

                foreach (var el in GetRecursByKeyStart(d, 1, l, keys))
                {
                    l.Add(el);
                    yield return l;
                    l.RemoveAt(l.Count - 1);
                }

            }

        }

        IEnumerable<object> GetRecursByKeyStart(SortedDictionary<object, object> di, int dim, List<object> l, object[] keys)
        {
            object skt = null;
            SortedDictionary<object, object> subDi = di;
            bool skip = false;

            while (true)
            {
                if (dim <= keys.Length)
                {
                    if (dimension != dim)
                    {
                        //here we can reduce or di.
                        skt = keys[dim - 1];
                        if (!subDi.TryGetValue(skt, out var objSubDi))
                        {
                            skip = true;
                            break;
                        }
                        else
                        {
                            l.Add(keys[dim - 1]);
                            dim++;
                            subDi = (SortedDictionary<object, object>)objSubDi;
                        }
                    }
                    else
                        break;
                }
                else
                    break;
            }

            if (!skip)
            {
                foreach (var el in subDi)
                {
                    l.Add(el.Key);

                    if (dimension == dim)
                    {
                        yield return el.Value;
                        l.RemoveAt(l.Count - 1);
                    }
                    else
                    {
                        foreach (var el1 in GetRecursByKeyStart((SortedDictionary<object, object>)el.Value, (1 + dim), l, keys))
                            yield return el1;

                        l.RemoveAt(l.Count - 1);
                    }
                }
            }
        }



        /// <summary>
        /// <para>Returns the complete content of MultiKeySortedDictionary.</para>
        /// <para>Each returned row is a list of objects, where the last item is a value, the others are keys.</para>       
        /// </summary>
        /// <returns></returns>
        public IEnumerable<List<object>> GetAll()
        {
            List<object> l = null;

            if (dimension > -1)
            {
                l = new List<object>();

                foreach (var el in GetRecurs(d, 1, l))
                {
                    l.Add(el);
                    yield return l;
                    l.RemoveAt(l.Count - 1);
                }

            }
        }

        IEnumerable<object> GetRecurs(SortedDictionary<object, object> di, int dim, List<object> l)
        {
            foreach (var el in di)
            {
                l.Add(el.Key);

                if (dimension == dim)
                {
                    yield return el.Value;
                    l.RemoveAt(l.Count - 1);
                }
                else
                {
                    foreach (var el1 in GetRecurs((SortedDictionary<object, object>)el.Value, (1 + dim), l))
                        yield return el1;

                    l.RemoveAt(l.Count - 1);
                }
            }
        }



        /// <summary>
        /// 
        /// </summary>
        public void Clear()
        {
            d.Clear();

            dimension = -1;
            Count = 0;
        }

        /// <summary>
        /// Contains keys
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        public bool Contains(params object[] keys)
        {
            if (dimension == -1)
                return false;

            if (keys.Length != dimension)
                throw new Exception("Key dimension is " + dimension);

            SortedDictionary<object, object> cd = d;
            int p = 0;
            int tp = keys.Length;
            object skt = null;


            foreach (var kt in keys)
            {
                if (kt == null)
                    throw new Exception("Unsupported key type NULL");

                skt = kt;

                if (p == tp - 1)
                    return cd.ContainsKey(skt);

                if (!cd.TryGetValue(skt, out var obj))
                    return false;
                else
                    cd = (SortedDictionary<object, object>)obj;

                p++;

            }

            return false;
        }


        /// <summary>
        /// Remove support deleting starting from key (if dimension is 3 then 3, 2 or 1 key can be supplied). To remove all use Clear()
        /// </summary>
        /// <param name="keys"></param>
        public void Remove(params object[] keys)
        {
            if (dimension == -1)
                return;

            if (keys.Length > dimension)
                throw new Exception("Key dimension is " + dimension);

            //It is possible to have lower dimension for Remove

            SortedDictionary<object, object> cd = d;
            int p = 0;
            int tp = keys.Length;
            object skt = null;
            int cdLen = 0;
            int removedQuantity = 1;

            foreach (var kt in keys)
            {
                if (kt == null)
                    throw new Exception("Unsupported key type NULL");

                skt = kt;

                if (p == tp - 1)
                {

                    if (dimension != keys.Length)
                    {//Iterating deep to find out how many in deep elements we delete
                        removedQuantity = 0;
                        foreach (var el in this.GetByKeyStart(keys))
                        {
                            removedQuantity++;
                        }
                    }

                    cdLen = cd.Count;
                    cd.Remove(skt);
                    if (cdLen != cd.Count)
                        Count -= removedQuantity;
                }

                if (!cd.TryGetValue(skt, out var obj))
                    return;
                else
                    cd = (SortedDictionary<object, object>)obj;

                p++;

            }

            if (d.Count == 0)
                dimension = -1;

            return;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="keys"></param>
        /// <returns></returns>
        public T Get<T>(params object[] keys)
        {
            if (dimension == -1)
                return default(T);

            if (keys.Length != dimension)
                throw new Exception("Key dimension is " + dimension);

            SortedDictionary<object, object> cd = d;
            int p = 0;
            int tp = keys.Length;
            object skt = null;

            foreach (var kt in keys)
            {
                if (kt == null)
                    throw new Exception("Unsupported key type NULL");

                skt = kt;

                if (!cd.TryGetValue(skt, out var obj))
                {
                    return default(T);
                }
                else
                {
                    if (p == tp - 1)
                    {
                        return (T)obj;
                    }
                    else
                        cd = (SortedDictionary<object, object>)obj;
                }

                p++;

            }

            return default(T);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="result"></param>
        /// <param name="keys"></param>
        /// <returns></returns>
        public bool TryGetValue<T>(out T result, params object[] keys)
        {
            if (dimension == -1)
            {
                result = default(T);
                return false;
            }

            if (keys.Length != dimension)
            {
                throw new Exception("Key dimension is " + dimension);
            }

            SortedDictionary<object, object> cd = d;
            int p = 0;
            int tp = keys.Length;
            object skt = null;


            foreach (var kt in keys)
            {
                if (kt == null)
                    throw new Exception("Unsupported key type NULL");

                skt = kt;

                if (!cd.TryGetValue(skt, out var obj))
                {
                    result = default(T);
                    return false;
                }
                else
                {
                    if (p == tp - 1)
                    {
                        result = (T)obj;
                        return true;
                    }
                    else
                        cd = (SortedDictionary<object, object>)obj;
                }

                p++;
            }

            result = default(T);
            return false;
        }


    }//eoc



}
#else
#endif
