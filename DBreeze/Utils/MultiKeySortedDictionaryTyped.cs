#if NET472 || NETSTANDARD2_1 || NETCOREAPP2_0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DBreeze.Utils
{
    /// <summary>
    /// MultiKeySortedDictionary where key is a Tuple with more than one key
    /// Second generation of the non-generic MKD, supporting cloning, serialization, count and named returns due to the ValueTuple behaviour.
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <typeparam name="TValue"></typeparam>
    public class MultiKeySortedDictionary<TKey, TValue> where TKey : System.Runtime.CompilerServices.ITuple
    {
#region "tests"
        /*
         
          MultiKeyDictionary mkd = new MultiKeyDictionary();
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
                ulong k1 = MultiKeyDictionary.MapLongToUlong((long)el[0]);
                ulong k2 = MultiKeyDictionary.MapLongToUlong((long)el[1]);
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
                //ulong k1 = MultiKeyDictionary.MapLongToUlong((long)el[0]);
                //ulong k2 = MultiKeyDictionary.MapLongToUlong((long)el[1]);
                //string val = (string)el[2];
            }
         */
#endregion

        /// <summary>
        /// MultiKeySortedDictionary
        /// </summary>
        public MultiKeySortedDictionary()
        {
            _key = default(TKey);

            MultiKeyDictionary.CreateDeconstructDelegate(_key.Length, _key.GetType(), ref this.Impl);
            //MKDHelper.CreateSerializeDelegate(_key.Length, _key.GetType(), this.serSeq);
        }



        SortedDictionary<object, object> d = new SortedDictionary<object, object>();

        /// <summary>
        /// Sync object. Not instantiated
        /// </summary>
        public ReaderWriterLockSlim Sync = null;

        int dimension = -1;

        TKey _key;
        Func<List<object>, TKey> Impl = null;
        /// <summary>
        /// Total count of elements in MKD
        /// </summary>
        public long Count = 0;
        //List<(Action<Biser.Encoder, object>, Func<Biser.Decoder, object>)> serSeq = new List<(Action<Biser.Encoder, object>, Func<Biser.Decoder, object>)>();


        /// <summary>
        /// MultiKeyDictionary.ByteArraySerializator must be set (once for all instances)
        /// </summary>
        /// <returns></returns>
        public byte[] Serialize()
        {
            if (MultiKeyDictionary.ByteArraySerializator == null)
                return null;

            return MultiKeyDictionary.ByteArraySerializator(GetAllObj().Select(r => (((TKey)Impl(r), (TValue)r[r.Count - 1]))));

            //Biser.Encoder enc = new Biser.Encoder();

            //enc.Add(this.Count);

            //foreach (var el in GetAllObj())
            //{

            //    var cnt = el.Count - 1;
            //    for (int ij = 0; ij < cnt; ij++)
            //    {
            //        serSeq[ij].Item1(enc, el[ij]);
            //    }

            //    enc.Add(MultiKeyDictionary.ByteArraySerializator((TValue)el[el.Count - 1]));
            //}

            //return enc.Encode();

        }

        /// <summary>
        /// MultiKeyDictionary.ByteArrayDeSerializator must be set (once for all instances)
        /// </summary>
        /// <param name="data"></param>
        public void Deserialize(byte[] data)
        {
            this.Clear();


            if (MultiKeyDictionary.ByteArrayDeSerializator == null)
                return;

            foreach (var el in (IEnumerable<(TKey, TValue)>)MultiKeyDictionary.ByteArrayDeSerializator(data, typeof(IEnumerable<(TKey, TValue)>)))
            {
                this.Add(el.Item1, el.Item2);
            }

            ////foreach (var el in (IEnumerable<(TKey, TValue)>)MultiKeyDictionary.ByteArrayDeSerializator(data, typeof(IEnumerable<(TKey, TValue)>)))
            ////{
            ////    d.Add(el.Item1, el.Item2);
            ////}

            //Biser.Decoder dec = new Biser.Decoder(data);

            //List<object> decres = new List<object>();

            //var totalElements = dec.GetLong();
            //for (int i = 0; i < totalElements; i++)
            //{
            //    for (int ij = 0; ij < _key.Length; ij++)
            //    {
            //        decres.Add(serSeq[ij].Item2(dec));
            //    }

            //    this.Add((TKey)Impl(decres), (TValue)MultiKeyDictionary.ByteArrayDeSerializator(dec.GetByteArray(), typeof(TValue)));

            //    decres.Clear();
            //}


        }

        /// <summary>
        /// Clones this multi-key dictionary into another instance
        /// </summary>
        /// <returns></returns>
        public MultiKeySortedDictionary<TKey, TValue> CloneMultiKeySortedDictionary()
        {
            return this.CloneByExpressionTree();
        }

        /// <summary>
        /// Adds elements
        /// </summary>
        /// <param name="keys"></param>
        /// <param name="value"></param>
        public void Add(TKey keys, TValue value)
        {
            //_key = keys;

            //if (Impl == null)
            //    CreateDeconstructDelegate();



            int tp = keys.Length;

            if (dimension > -1 && tp != dimension)
                throw new Exception("Key dimension is " + dimension);

            if (dimension == -1 && tp == 0)
                throw new Exception("Keys are not supplied");

            SortedDictionary<object, object> cd = d;
            int p = 0;
            object skt = null;

            for (int i = 0; i < keys.Length; i++)
            //foreach (var kt in keys)
            {
                var kt = keys[i];

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
        /// Iterate over all elements of the dictionary
        /// </summary>
        /// <returns></returns>
        public IEnumerable<(TKey, TValue)> GetAll()
        {
            List<object> l = null;

            if (dimension > -1)
            {
                l = new List<object>();

                foreach (var el in GetRecurs(d, 1, l))
                {
                    l.Add(el);
                    yield return ((TKey)Impl(l), (TValue)el);
                    l.RemoveAt(l.Count - 1);
                }

            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        IEnumerable<List<object>> GetAllObj()
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


        /// <summary>
        ///// Returns subdictionary content starting from key (also checked border values, null keys, key that equals to dimension etc...)
        ///// <para>mkd.Add("a1", 1, 1 ,1);</para>
        ///// <para>mkd.Add("a3", 1, 2, 3);</para>
        ///// <para>mkd.Add("a4", 1, 2, 4);</para>
        ///// <para>mkd.Add("a4", 1, 2, 5);</para>
        ///// <para>mkd.Add("a9", 1, 3, 9);</para>
        ///// <para>mkd.Add("a10", 1,3,10);</para>
        ///// <para>mkd.Add("a7", 2, 1, 7);</para>
        ///// <para>mkd.Add("a8", 2, 2, 8);</para>
        ///// <para>foreach (var el in mkd.GetByKeyStart(2)){} returns 2 values</para>
        ///// <para>foreach (var el in mkd.GetByKeyStart(1,2)){} returns 3 values</para>        
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        public IEnumerable<(TKey, TValue)> GetByKeyStart(params object[] keys) //public IEnumerable<(TKey, TValue)> GetByKeyStart(System.Runtime.CompilerServices.ITuple keys)
        {//
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

                for(int i=0;i<keys.Length;i++)
                    l.Add(keys[i]);              
                
                if (this.TryGetValue<object>(out var getRes, keys)) //if(this.TryGetValue((TKey)keys, out var getRes))
                {
                    l.Add(getRes);
                    yield return ((TKey)Impl(l), (TValue)getRes);
                    
                }
            }
            else if (dimension > -1)
            {
                List<object> l = new List<object>();

                foreach (var el in GetRecursByKeyStart(d, 1, l, keys))
                {
                    l.Add(el);
                    yield return ((TKey)Impl(l), (TValue)el);
                    //yield return l;
                    l.RemoveAt(l.Count - 1);
                }

            }

        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="di"></param>
        /// <param name="dim"></param>
        /// <param name="l"></param>
        /// <param name="keys"></param>
        /// <returns></returns>
        //IEnumerable<object> GetRecursByKeyStart(Dictionary<object, object> di, int dim, List<object> l, System.Runtime.CompilerServices.ITuple keys)
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
        /// 
        /// </summary>
        /// <param name="di"></param>
        /// <param name="dim"></param>
        /// <param name="l"></param>
        /// <returns></returns>
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
        /// 
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        public bool Contains(TKey keys)
        {
            if (dimension == -1)
                return false;

            if (keys.Length != dimension)
                throw new Exception("Key dimension is " + dimension);

            SortedDictionary<object, object> cd = d;
            int p = 0;
            int tp = keys.Length;
            object skt = null;
            object kt = null;

            for (int i = 0; i < keys.Length; i++)
            {
                kt = keys[i];

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
        /// Removes key completely
        /// </summary>
        /// <param name="keys"></param>
        public void Remove(TKey keys)
        {
            if (dimension == -1)
                return;

            if (keys.Length > dimension)
                throw new Exception("Key dimension is " + dimension);

            var obj = new object[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                obj[i] = keys[i];

            Remove(obj);
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

                    if (_key.Length != keys.Length)
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
        /// Gets/Sets the value by complete Key
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        public TValue this[TKey keys]
        {
            get => this.Get(keys);
            set => this.Add(keys, value);
        }

        /// <summary>
        /// Gets the value by complete Key
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        public TValue Get(TKey keys)
        {
            if (dimension == -1)
                return default(TValue);

            if (keys.Length != dimension)
                throw new Exception("Key dimension is " + dimension);


            var obj = new object[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                obj[i] = keys[i];

            if (!TryGetValue(out TValue result, obj))
                return default(TValue);

            return result;

            //Dictionary<object, object> cd = d;
            //int p = 0;
            //int tp = keys.Length;
            //object skt = null;
            //object kt = null;

            //for (int i = 0; i < keys.Length; i++)
            //{
            //    kt = keys[i];

            //    if (kt == null)
            //        throw new Exception("Unsupported key type NULL");

            //    skt = kt;

            //    if (!cd.TryGetValue(skt, out var obj))
            //    {
            //        return default(TValue);
            //    }
            //    else
            //    {
            //        if (p == tp - 1)
            //        {
            //            return (TValue)obj;
            //        }
            //        else
            //            cd = (Dictionary<object, object>)obj;
            //    }

            //    p++;

            //}

            //return default(TValue);
        }

        /// <summary>
        /// Tries to get the value by the complete key
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="result"></param>
        /// <param name="keys"></param>
        /// <returns></returns>
        public bool TryGetValue(TKey keys, out TValue result)
        {
            if (dimension == -1)
            {
                result = default(TValue);
                return false;
            }

            if (keys.Length != dimension)
            {
                throw new Exception("Key dimension is " + dimension);
            }

            var obj = new object[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                obj[i] = keys[i];

            return TryGetValue(out result, obj);

        }

        /// <summary>
        /// Internal dimensions are checked level up
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="result"></param>
        /// <param name="keys"></param>
        /// <returns></returns>
        bool TryGetValue<T>(out T result, params object[] keys)
        {
            //if (dimension == -1)
            //{
            //    result = default(T);
            //    return false;
            //}

            //if (keys.Length != dimension)
            //{
            //    throw new Exception("Key dimension is " + dimension);
            //}

            SortedDictionary<object, object> cd = d;
            int p = 0;
            int tp = keys.Length;
            object skt = null;


            foreach (var kt in keys)
            {
                //Not possible with ValueTuple
                //if (kt == null) 
                //    throw new Exception("Unsupported key type NULL");

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
#endif
