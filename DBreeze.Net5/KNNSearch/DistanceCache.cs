// <copyright file="DistanceCache.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// </copyright>

#if NET6FUNC
using DBreeze.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace DBreeze.HNSW
{
    internal static class DistanceCacheLimits
    {
        /// <summary>
        /// https://referencesource.microsoft.com/#mscorlib/system/array.cs,2d2b551eabe74985,references
        /// We use powers of 2 for efficient modulo
        /// 2^28 = 268435456
        /// 2^29 = 536870912
        /// 2^30 = 1073741824
        /// </summary>
        public static int MaxArrayLength 
        { 
            get { return _maxArrayLength; }
            set { _maxArrayLength = NextPowerOf2((uint)value); }
        }

        private static int NextPowerOf2(uint x)
        {
#if NET7FUNC
            var v = System.Numerics.BitOperations.RoundUpToPowerOf2(x);
            if (v > 0x10000000) return 0x10000000;
            return (int)v;
#else
            return (int)Math.Pow(2, Math.Ceiling(Math.Log(x) / Math.Log(2)));
#endif
        }

        private static int _maxArrayLength = 268_435_456; // 0x10000000;
    }
    internal class DistanceCache<TDistance> where TDistance : struct
    {


        public TDistance[] values;

        public long[] keys;

        //-public
        public int HitCount;
        public int HitDistanceCount;

        /// <summary>
        /// New cache distance
        /// </summary>
        public Dictionary<(int, int), TDistance> ds = new Dictionary<(int, int), TDistance>();

        internal DistanceCache()
        {
        }

        internal void Resize(int pointsCount, bool overwrite)
        {
            return;
            //if(System.IO.File.Exists(@"D:\Temp\DBVector\cache1"))
            //{
            //    var bt = System.IO.File.ReadAllBytes(@"D:\Temp\DBVector\cache1");
            //    var dcdb = DistanceCacheDB.BiserDecode(bt);
            //    ds = (Dictionary<(int, int), TDistance>)(object)dcdb.ds;
            //}
            
            //return;
            //if(pointsCount <=0) { pointsCount = 1024; }

            // long capacity = ((long)pointsCount * (pointsCount + 1)) >> 1; //orig check inti cache size
            ////long capacity = pointsCount * 100;//-test
            ////capacity = 600000;

            //capacity = capacity < DistanceCacheLimits.MaxArrayLength ? capacity : DistanceCacheLimits.MaxArrayLength;

            //if (keys is null || capacity > keys.Length || overwrite)
            //{
            //    int i0 = 0;
            //    if (keys is null || overwrite)
            //    {
            //        keys   = new long[(int)capacity];
            //        values = new TDistance[(int)capacity];
            //    }
            //    else
            //    {
            //        i0 = keys.Length;
            //        Array.Resize(ref keys,   (int)capacity);
            //        Array.Resize(ref values, (int)capacity);
            //    }

            //    // TODO: may be there is a better way to warm up cache and force OS to allocate pages
            //    keys.AsSpan().Slice(i0).Fill(-1);
            //    values.AsSpan().Slice(i0).Fill(default);
            //}
        }

        public long timeDistance = 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal TDistance GetOrCacheValue(int fromId, int toId, Func<int,int,TDistance> getter)
        {

            //Stopwatch sw = new Stopwatch();
            if (ds.TryGetValue((fromId, toId), out var dist))
            {
                HitCount++;
                return dist;
            }
            else
            {
                //sw.Start();
                HitDistanceCount++;
                dist = getter(fromId, toId);
                ds[(fromId, toId)] = dist;
                ds[(toId, fromId)] = dist;
                //sw.Stop();
                //timeDistance += sw.ElapsedTicks;
                return dist;
            }

            //long key = MakeKey(fromId, toId);
            //int hash = (int)(key & (keys.Length - 1));

            //if (keys[hash] == key)
            //{
            //    HitCount++;
            //    return values[hash];
            //}
            //else
            //{
            //    Stopwatch sw = new Stopwatch();
            //    sw.Start();

            //    HitDistanceCount++;
            //    var d = getter(fromId, toId);



            //    keys[hash] = key;
            //    values[hash] = d;
            //    sw.Stop();
            //    timeDistance += sw.ElapsedTicks;
            //    return d;
            //}
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetValue(int fromId, int toId, TDistance distance)
        {
            long key = MakeKey(fromId, toId);
            int hash = (int)(key & (keys.Length - 1));
            keys[hash] = key;
            values[hash] = distance;
        }

        /// <summary>
        /// Builds key for the pair of points.
        /// MakeKey(fromId, toId) == MakeKey(toId, fromId)
        /// </summary>
        /// <param name="fromId">The from point identifier.</param>
        /// <param name="toId">The to point identifier.</param>
        /// <returns>Key of the pair.</returns>
        private static long MakeKey(int fromId, int toId)
        {
            return fromId > toId ? (((long)fromId * (fromId + 1)) >> 1) + toId : (((long)toId * (toId + 1)) >> 1) + fromId;
        }

        //public byte[] Finilize()
        //{
        //    Console.WriteLine($"dcdbCount: {ds.Count}");
        //    return null;

        //    var dcdb = new DistanceCacheDB()
        //    {
        //        ds = (Dictionary<(int, int), double>)(object)ds
        //    };

        //    var bt = dcdb.BiserEncoder().Encode();
        //    //var bt1 = bt.CompressBytesBrotliDBreeze();
        //    //Console.WriteLine($"BT: {bt.Length}; BT1: {bt1.Length}");
        //    Console.WriteLine($"BT: {bt.Length}; BT1: {0}; dcdbCount: {ds.Count}");

        //    System.IO.File.WriteAllBytes(@"D:\Temp\DBVector\cache1", bt);

        //    return bt;
        //    //return bt1;
        //}

        //public partial class DistanceCacheDB
        //{
        //    public Dictionary<(int, int), double> ds { get; set; } = new Dictionary<(int, int), double>();         
        //    //public Dictionary<int, double> ds { get; set; } = new Dictionary<int, double>();
        //}

        //public partial class DistanceCacheDB : Biser.IEncoder
        //{


        //    public Biser.Encoder BiserEncoder(Biser.Encoder existingEncoder = null)
        //    {
        //        Biser.Encoder encoder = new Biser.Encoder(existingEncoder);


        //        encoder.Add(ds, (r1) =>
        //        {
        //            encoder.Add(r1.Key.Item1);
        //            encoder.Add(r1.Key.Item2);
        //            encoder.Add(r1.Value);
        //        });

        //        return encoder;
        //    }


        //    public static DistanceCacheDB BiserDecode(byte[] enc = null, Biser.Decoder extDecoder = null)
        //    {
        //        Biser.Decoder decoder = null;
        //        if (extDecoder == null)
        //        {
        //            if (enc == null || enc.Length == 0)
        //                return null;
        //            decoder = new Biser.Decoder(enc);
        //        }
        //        else
        //        {
        //            if (extDecoder.CheckNull())
        //                return null;
        //            else
        //                decoder = extDecoder;
        //        }

        //        DistanceCacheDB m = new DistanceCacheDB();



        //        m.ds = decoder.CheckNull() ? null : new System.Collections.Generic.Dictionary<System.ValueTuple<System.Int32, System.Int32>, System.Double>();
        //        if (m.ds != null)
        //        {
        //            decoder.GetCollection(() =>
        //            {
        //                System.Int32 pvar2 = 0;
        //                System.Int32 pvar3 = 0;
        //                pvar2 = decoder.GetInt();
        //                pvar3 = decoder.GetInt();
        //                var pvar1 = new ValueTuple<System.Int32, System.Int32>(pvar2, pvar3);
        //                return pvar1;
        //            },
        //        () =>
        //        {
        //            var pvar4 = decoder.GetDouble();
        //            return pvar4;
        //        }, m.ds, true);
        //        }


        //        return m;
        //    }


        //}


    }//eoc
}
#endif