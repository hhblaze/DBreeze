#if NET6FUNC || NET472
using DBreeze.DataTypes;
using DBreeze.Tries;
using DBreeze.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace DBreeze.HNSW
{
    internal partial class SmallWorld<TItem, TDistance>
    {
        internal interface IStorage<TItem, TDistance>
        {
            //TItem GetItem1(uint externalId);
            TItem GetItem(long externalId, Func<long, TItem> f);
            void AddItem(long externalId, int bucketId, int id, TItem item);
            string TableName { get; set; }
            DBreeze.Transactions.Transaction tran { get; set; }
            bool FlushNodes(int bucketId, Dictionary<int, Node> nodes);
            void FlushBucket(Bucket bucket);
            NodeDB GetDBNode(int bucketId, int nodeId);
            List<BucketDB> GetBuckets();
            void ClearItemsCache();
            //TItem NormalizeVector(TItem vector);
            void FlushAddItems(bool externalTableForVectorsAvailable);

            Func<TItem, TItem, TDistance> GetDistanceFunction();
            Func<TItem, TItem> GetNormalizeFunction();
        }

        internal class DBStorage
        {
            /*
             DBreeze scheme
             2- Key: new byte[] {2, (int)bucketId, (int)nodeId}
                Value: NodeDB (maxLevel, externalId, connections on all levels)
             3- Key: new byte[] {3, (int)bucketId}
                Value: BucketDB (entryPoint info, MaxLevel)
             4- Key: new byte[] {4, (long)externalId }
                Value: (int)bucketId, (int)nodeId
                    //var bucketId = Value.Substring(0, 4).To_Int32_BigEndian();
                    //var nodeId = Value.Substring(4, 4).To_Int32_BigEndian();
             5- Key: new byte[] {5, (long)externalId}
                Value: brotli compressed TItem (vector self)
             */


            public DBreeze.Transactions.Transaction tran { get; set; }

            protected ReaderWriterLockSlim _sync = new ReaderWriterLockSlim();
            
            /// <summary>
            /// 
            /// </summary>
            public string TableName { get; set; } = string.Empty;

            /// <summary>
            /// true, if something was saved
            /// </summary>
            /// <param name="bucketId"></param>
            /// <param name="nodes"></param>
            /// <returns></returns>
            public bool FlushNodes(int bucketId, Dictionary<int, Node> nodes)
            {
                
                bool changed = false;
                foreach(var n in nodes.Where(r=>r.Value.Changed).OrderBy(r=>r.Key))
                {
                    var dbNode = new NodeDB
                    {
                        Connections = n.Value.Connections.CloneByExpressionTree(),
                        ExternalId = n.Value.ExternalId,
                        Id = n.Value.Id,
                        MaxLevel = n.Value.MaxLevel
                    };
                    byte[] btNode = dbNode.BiserEncoder().Encode();

                    tran.Insert<byte[], byte[]>(this.TableName, 2.ToIndex(bucketId, n.Key), btNode);

                    changed = true; 
                }

                return changed;
            }
             
            public NodeDB GetDBNode(int bucketId, int nodeId)
            {
                var row = tran.Select<byte[], byte[]>(this.TableName, 2.ToIndex(bucketId, nodeId));
                return NodeDB.BiserDecode(row.Value);
            }

            public void FlushBucket(Bucket bucket)
            {
                var dbBucket = new BucketDB()
                {
                    BucketId = bucket.BucketId,
                    EntryPointId = bucket.Graph.entryPoint.Id,
                    Count = bucket.Graph.Count
                };

                byte[] btBucket = dbBucket.BiserEncoder().Encode();

                tran.Insert<byte[], byte[]>(this.TableName, 3.ToIndex(bucket.BucketId), btBucket);
            }

            public List<BucketDB> GetBuckets()
            {
                List<BucketDB> ret = new List<BucketDB>();
                foreach (var row in tran.SelectForwardFromTo<byte[], byte[]> (this.TableName, 3.ToIndex((int)0),true,3.ToIndex(int.MaxValue),false))
                {
                    ret.Add(BucketDB.BiserDecode(row.Value));
                }
                return ret;
            }
        }

        internal class SmallWorldStorageF : DBStorage, IStorage<float[], float>
        {
            //TODO: Put here F/D distance functions (with without "Vector" support) use one time check if "Vector" supported from SIMDForUnits
            public Func<float[], float[], float> GetDistanceFunction()
            {
                if (CosineDistance.IsHardwareAccelerated())
                    return CosineDistance.DistanceForUnits;
                else
                    return CosineDistance.DistanceForUnitsSimple;

            }

            public Func<float[], float[]> GetNormalizeFunction()
            {
                if (CosineDistance.IsHardwareAccelerated())
                    return CosineDistance.NormalizeVector;
                else
                    return CosineDistance.NormalizeSimple;
            }


            ConcurrentDictionary<long, float[]> itemsCache = new ConcurrentDictionary<long, float[]>();
            List<(long externalId, int bucketId, int id)> addedItems = new List<(long externalId, int bucketId, int id)>();

            float[] SmallWorld<TItem, TDistance>.IStorage<float[], float>.GetItem(long externalId, Func<long, float[]> f=null)
            {
                if(f != null)
                    return f(externalId);

                if (!itemsCache.TryGetValue(externalId, out var item))
                {   
                    if (f != null)
                    {
                        item = f(externalId);
                        itemsCache[externalId] = item;
                    }
                    else
                    {   
                        var row = tran.Select<byte[], byte[]>(this.TableName, 5.ToIndex(externalId));
                        var bt = DecompressF(row.Value);
                        itemsCache[externalId] = bt;
                        return bt;
                    }
                }

                return item;
            }

            //float[] SmallWorld<TItem, TDistance>.IStorage<float[]>.GetItem1(uint externalId)
            //{

            //    //if (this.GetVectorByExternalId != null)
            //    //    return GetVectorByExternalId(externalId);

            //    if (!itemsCache.TryGetValue(externalId, out var item))
            //    {
            //        var ll = tran.ValuesLazyLoadingIsOn;
            //        tran.ValuesLazyLoadingIsOn = false;
            //        var row = tran.Select<uint, byte[]>(TableVectorsStorage, externalId);
            //        tran.ValuesLazyLoadingIsOn = ll;
            //        if (row.Exists)
            //        {
            //            var dec = SmallWorld<TItem, TDistance>.Decompress(row.Value);
            //            itemsCache[row.Key] = dec;
            //            return dec;
            //        }
            //        else
            //        {
            //            throw new Exception($"HNSW. SmallWorldStorageF. GetItem {externalId} is not found");
            //        }

            //    }

            //    return item;
            //}

            public void ClearItemsCache()
            {
                itemsCache.Clear();
            }

           
            

            public void FlushAddItems(bool externalTableForVectorsAvailable)
            {
                /*
                   DBreeze scheme         
                   4- Key: new byte[] {4, (long)externalId }
                      Value: (int)bucketId, (int)nodeId
                   5- Key: new byte[] {5, (long)externalId}
                      Value: brotli compressed TItem (vector self)
                */

                foreach(var el in addedItems)
                {
                    var item = itemsCache[el.externalId];
                  
                    if (!externalTableForVectorsAvailable)
                    {
                        tran.Insert<byte[], byte[]>(this.TableName, 5.ToIndex(el.externalId), CompressF(item));
                    }                
                    tran.Insert<byte[], byte[]>(this.TableName, 4.ToIndex(el.externalId), el.bucketId.To_4_bytes_array_BigEndian().Concat(el.id.To_4_bytes_array_BigEndian()));
                }
                addedItems.Clear();
            }


            /// <summary>
            /// item should be already normalized
            /// </summary>
            /// <param name="externalId"></param>
            /// <param name="bucketId"></param>
            /// <param name="id"></param>
            /// <param name="item"></param>           
            public void AddItem(long externalId, int bucketId, int id, float[] item)
            {
                itemsCache[externalId] = item;

                this._sync.EnterWriteLock();                
                addedItems.Add((externalId, bucketId, id));
                this._sync.ExitWriteLock();               
            }

           
            //public float[] NormalizeVector(float[] vector)
            //{
            //    return CosineDistance.NormalizeVector(vector);
            //}


            ///// <summary>
            ///// TEST
            ///// </summary>
            ///// <param name="batchSize"></param>
            ///// <returns></returns>
            //public IEnumerable<List<(uint, float[])>> ByBatch(int batchSize, int take=int.MaxValue)
            //{
            //    if (tran.Count(TEMPTableVectorsStorage) == 0)
            //        yield break;

            //    int i = 0;
            //    List<(uint, float[])> l = new();
            //    foreach (var el in tran.SelectForward<uint, byte[]>(TEMPTableVectorsStorage).Take(take))
            //    {
            //        if (i == batchSize)
            //        {
            //            i = 0;
            //            yield return l;
            //            l.Clear();
            //        }
            //        var dec = SmallWorld<TItem, TDistance>.Decompress(el.Value);
            //        l.Add((el.Key, dec));
            //        i++;
            //    }

            //    if (l.Count > 0)
            //        yield return l;
            //}


        }



        /// <summary>
        /// 
        /// </summary>
        internal class SmallWorldStorageD : DBStorage, IStorage<double[], double>
        {
            public Func<double[], double[], double> GetDistanceFunction()
            {
                if (CosineDistance.IsHardwareAccelerated())
                    return CosineDistance.DistanceForUnits;
                else
                    return CosineDistance.DistanceForUnitsSimple;
            }

            public Func<double[], double[]> GetNormalizeFunction()
            {
                if (CosineDistance.IsHardwareAccelerated())
                    return CosineDistance.NormalizeVector;
                else
                    return CosineDistance.NormalizeSimple;
            }

            ConcurrentDictionary<long, double[]> itemsCache = new ConcurrentDictionary<long, double[]>();
            List<(long externalId, int bucketId, int id)> addedItems = new List<(long externalId, int bucketId, int id)>();

            double[] SmallWorld<TItem, TDistance>.IStorage<double[], double>.GetItem(long externalId, Func<long, double[]> f)
            {
                if (f != null)
                    return f(externalId);

                if (!itemsCache.TryGetValue(externalId, out var item))
                {
                    if (f != null)
                    {
                        item = f(externalId);
                        itemsCache[externalId] = item;
                    }
                    else
                    {
                        var row = tran.Select<byte[], byte[]>(this.TableName, 5.ToIndex(externalId));
                        var bt = DecompressD(row.Value);
                        itemsCache[externalId] = bt;
                        return bt;
                    }
                }

                return item;
            }

            //double[] SmallWorld<TItem, TDistance>.IStorage<double[]>.GetItem1(uint externalId)
            //{
            //    if (!itemsCache.TryGetValue(externalId, out var item))
            //    {
            //        var ll = tran.ValuesLazyLoadingIsOn;
            //        tran.ValuesLazyLoadingIsOn = false;
            //        var row = tran.Select<uint, byte[]>(TableVectorsStorage, externalId);
            //        tran.ValuesLazyLoadingIsOn = ll;
            //        if (row.Exists)
            //        {
            //            var dec = SmallWorld<TItem, TDistance>.DecompressD(row.Value);
            //            itemsCache[row.Key] = dec;
            //            return dec;
            //        }

            //    }

            //    return item;
            //}

            public void ClearItemsCache()
            {
                itemsCache.Clear();
            }

            public double[] NormalizeVector(double[] vector)
            {
                return CosineDistance.NormalizeVector(vector);
            }

            

            public void FlushAddItems(bool externalTableForVectorsAvailable)
            {
                /*
                   DBreeze scheme         
                   4- Key: new byte[] {4, (long)externalId }
                      Value: (int)bucketId, (int)nodeId
                   5- Key: new byte[] {5, (long)externalId}
                      Value: brotli compressed TItem (vector self)
                */
                foreach (var el in addedItems)
                {
                    var item = itemsCache[el.externalId];

                    if (!externalTableForVectorsAvailable)
                    {
                        tran.Insert<byte[], byte[]>(this.TableName, 5.ToIndex(el.externalId), CompressD(item));
                    }
                
                    tran.Insert<byte[], byte[]>(this.TableName, 4.ToIndex(el.externalId), el.bucketId.To_4_bytes_array_BigEndian().Concat(el.id.To_4_bytes_array_BigEndian()));
                }

                addedItems.Clear();
            }


            /// <summary>
            /// item should be already normalized
            /// </summary>
            /// <param name="externalId"></param>
            /// <param name="bucketId"></param>
            /// <param name="id"></param>
            /// <param name="item"></param>           
            public void AddItem(long externalId, int bucketId, int id, double[] item)
            {
                itemsCache[externalId] = item;

                this._sync.EnterWriteLock();
                addedItems.Add((externalId, bucketId, id));
                this._sync.ExitWriteLock();
            }

            ///// <summary>
            ///// 
            ///// </summary>
            ///// <param name="batchSize"></param>
            ///// <param name="take"></param>
            ///// <returns></returns>
            //public IEnumerable<List<(uint, double[])>> ByBatch(int batchSize, int take = int.MaxValue)
            //{
            //    if (tran.Count(TEMPTableVectorsStorage) == 0)
            //        yield break;

            //    int i = 0;
            //    List<(uint, double[])> l = new();
            //    foreach (var el in tran.SelectForward<uint, byte[]>(TEMPTableVectorsStorage).Take(take))
            //    {
            //        if (i == batchSize)
            //        {
            //            i = 0;
            //            yield return l;
            //            l.Clear();
            //        }
            //        var dec = SmallWorld<TItem, TDistance>.DecompressD(el.Value);
            //        l.Add((el.Key, dec));
            //        i++;
            //    }

            //    if (l.Count > 0)
            //        yield return l;
            //}


        }

        internal partial class NodeDB
        {
            public List<List<int>> Connections { get; set; } = new List<List<int>>();
            public int MaxLevel { get; set; } = 0;
            public int Id { get; set; } = 0;
            public long ExternalId { get; set; } = 0;
            public bool Deleted { get; set; } = false;
        }

        internal partial class NodeDB : Biser.IEncoder
        {
            public Biser.Encoder BiserEncoder(Biser.Encoder existingEncoder = null)
            {
                Biser.Encoder encoder = new Biser.Encoder(existingEncoder);


                encoder.Add(Connections, (r1) => {
                    encoder.Add(r1, (r2) => {
                        encoder.Add(r2);
                    });
                });
                encoder.Add(MaxLevel);
                encoder.Add(Id);
                encoder.Add(ExternalId);
                encoder.Add(Deleted);

                return encoder;
            }


            public static NodeDB BiserDecode(byte[] enc = null, Biser.Decoder extDecoder = null)
            {
                Biser.Decoder decoder = null;
                if (extDecoder == null)
                {
                    if (enc == null || enc.Length == 0)
                        return null;
                    decoder = new Biser.Decoder(enc);
                }
                else
                {
                    if (extDecoder.CheckNull())
                        return null;
                    else
                        decoder = extDecoder;
                }

                NodeDB m = new NodeDB();

                m.Connections = decoder.CheckNull() ? null : new System.Collections.Generic.List<System.Collections.Generic.List<System.Int32>>();
                if (m.Connections != null)
                {
                    decoder.GetCollection(() => {
                        var pvar1 = decoder.CheckNull() ? null : new System.Collections.Generic.List<System.Int32>();
                        if (pvar1 != null)
                        {
                            decoder.GetCollection(() => {
                                var pvar2 = decoder.GetInt();
                                return pvar2;
                            }, pvar1, true);
                        }
                        return pvar1;
                    }, m.Connections, true);
                }
                m.MaxLevel = decoder.GetInt();
                m.Id = decoder.GetInt();
                m.ExternalId = decoder.GetLong();
                m.Deleted = decoder.GetBool();


                return m;
            }
        }


        internal partial class BucketDB {

            public int BucketId { get; set; }
            public int EntryPointId { get; set; }
            public int Count { get; set; }
        }

        internal partial class BucketDB : Biser.IEncoder
        {


            public Biser.Encoder BiserEncoder(Biser.Encoder existingEncoder = null)
            {
                Biser.Encoder encoder = new Biser.Encoder(existingEncoder);


                encoder.Add(BucketId);
                encoder.Add(EntryPointId);
                encoder.Add(Count);

                return encoder;
            }


            public static BucketDB BiserDecode(byte[] enc = null, Biser.Decoder extDecoder = null)
            {
                Biser.Decoder decoder = null;
                if (extDecoder == null)
                {
                    if (enc == null || enc.Length == 0)
                        return null;
                    decoder = new Biser.Decoder(enc);
                }
                else
                {
                    if (extDecoder.CheckNull())
                        return null;
                    else
                        decoder = extDecoder;
                }

                BucketDB m = new BucketDB();



                m.BucketId = decoder.GetInt();
                m.EntryPointId = decoder.GetInt();
                m.Count = decoder.GetInt();


                return m;
            }


        }
    }
    
}
#endif