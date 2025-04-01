/*
  Copyright https://github.com/wlou/HNSW.Net MIT License
  Copyright (C) 2012 dbreeze.tiesky.com / Oleksiy Solovyov / Ivars Sudmalis.
  It's a free software for those who think that it should be free.
*/

#if NET6FUNC || NET472
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DBreeze.Utils;


namespace DBreeze.HNSW
{
    internal partial class SmallWorld<TItem, TDistance>
    {       
        public class Composer
        {
            internal SmallWorld<TItem, TDistance>.Parameters _parameters;
            internal Func<TItem, TItem, TDistance> _distance;
            internal Func<TItem, TItem> _normalize;
            
            internal InstanceManager InstanceManager = null;
            internal BucketManager BucketManager = null;

            ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

            int InsertBatchSize = 1000; //1000 items at once
            internal float AutomaticProcessorCountChoiceInProcent = 0.7f;
            internal int MaxItemsInBucket = 100000;

            /// <summary>
            /// Function to get embeddings from outer table by ExternalId.
            /// In case if it is NULL, we also Adding vectors inside Storage.TableVectors
            /// </summary>
            internal Func<long, TItem> GetVectorbyExternalId;



            public Composer(Parameters parameters, int instanceQuantity = 0, Func<long, TItem> GetVectorbyExternalId = null)
            {
                if (parameters.Storage == null)
                    throw new Exception("HNSW. Storage is null"); //Or create default Storage

                this.GetVectorbyExternalId = GetVectorbyExternalId;

                this._parameters = parameters;
                //this._distance = distance;
                this._distance = parameters.Storage.GetDistanceFunction();
                this._normalize = parameters.Storage.GetNormalizeFunction();                
                this.InstanceManager = new InstanceManager(this, instanceQuantity);
                this.BucketManager = new BucketManager(this);
                
            }

            IEnumerable<List<(long externalId, TItem item)>> Batcher(IList<(long externalId, TItem item)> items)
            {
                List<(long externalId, TItem item)> currentBatch = new List<(long externalId, TItem item)>();
                int curBatch = 0;
                foreach(var item in items)
                {
                    if (curBatch >= this.InsertBatchSize)
                    {
                        yield return currentBatch;
                        currentBatch = new List<(long externalId, TItem item)>();                        
                        curBatch = 0;
                    }
                    currentBatch.Add(item);
                    curBatch++;
                }
                if(currentBatch.Count>0)
                    yield return currentBatch;
            }

            ///// <summary>
            ///// 
            ///// </summary>
            ///// <param name="items"></param>
            ///// <param name="clearDistanceCache"></param>
            //public void AddItems(IList<(uint externalId, TItem item)> items, bool clearDistanceCache = true)
            //{
            //    if (items == null || items.Count == 0)
            //    {
            //        return; // Nothing to add
            //    }
               
            //    foreach (var iitems in Batcher(items))
            //    {
            //        _lock.EnterWriteLock();
            //        try
            //        {
            //            int totalItems = iitems.Count;

            //            // Determine the number of instances to actually distribute work across.
            //            // If there are fewer items than instances, don't try to give work to more instances than there are items.
            //            int effectiveInstanceQuantity = Math.Min(this.InstanceManager.InstanceQuantity, totalItems);

            //            // If after the Min, we have no instances to work with (e.g., if InstanceQuantity was 0, though constructor prevents this), exit.
            //            // This also implicitly handles totalItems == 0 case again, though already checked.
            //            if (effectiveInstanceQuantity <= 0) return;

            //            // Calculate base chunk size and the number of instances that will get one extra item
            //            int chunkSize = totalItems / effectiveInstanceQuantity;
            //            int remainder = totalItems % effectiveInstanceQuantity;

            //            // Use Parallel.For to iterate through the indices of the instances that will receive items
            //            Parallel.For(0, effectiveInstanceQuantity, i =>
            //            {
            //                // Calculate the starting index in the original list for this instance's chunk
            //                // Instances with index less than 'remainder' get an extra item, affecting subsequent start indices.
            //                int startIndex = i * chunkSize + Math.Min(i, remainder);

            //                // Calculate the number of items this instance should process
            //                int count = chunkSize + (i < remainder ? 1 : 0);

            //                // This check is technically redundant given the effectiveInstanceQuantity logic,
            //                // as 'count' should always be at least 1 here, but it doesn't hurt.
            //                if (count > 0)
            //                {
            //                    // Extract the sub-list (chunk) for the current instance.
            //                    // .Skip().Take() works on IEnumerable, .ToList() materializes the chunk.
            //                    // This creates a new List for each chunk, involving some overhead,
            //                    // but is clear and works generally with IList.
            //                    var chunkItems = iitems.Skip(startIndex).Take(count).ToList();

            //                    // Call AddItems on the specific graph instance.
            //                    // It's crucial that _instances[i].AddItems is safe to call in parallel *across different instances*.
            //                    // It should primarily modify the state associated with _instances[i].

            //                    this.InstanceManager.CInstances[i].GetInsertBucket().AddItems(chunkItems, clearDistanceCache);
            //                }
            //            });

            //            // Note: If totalItems < InstanceQuantity initially, the instances with index >= totalItems
            //            // (i.e., index >= effectiveInstanceQuantity) will not be touched in this operation, which is expected.
            //        }
            //        finally
            //        {
            //            _lock.ExitWriteLock();
            //        }
            //    }


               
            //}

            public void AddItems(IList<(long externalId, TItem item)> items, bool clearDistanceCache = true)
            {
                if (items == null || items.Count == 0) return;

                foreach (var batch in Batcher(items))
                {
                    _lock.EnterWriteLock();
                    try
                    {
                        int totalItems = batch.Count;
                        int effectiveInstanceQuantity = Math.Min(InstanceManager.InstanceQuantity, totalItems);
                        if (effectiveInstanceQuantity <= 0) return;
                                              
                        var partitioner = Partitioner.Create(0, totalItems, (totalItems + effectiveInstanceQuantity - 1) / effectiveInstanceQuantity);

                        Parallel.ForEach(partitioner, range =>
                        {
                            int instanceIndex = range.Item1 / ((totalItems + effectiveInstanceQuantity - 1) / effectiveInstanceQuantity);
                            var instance = InstanceManager.CInstances[instanceIndex];
                            var chunkItems = batch.Skip(range.Item1).Take(range.Item2 - range.Item1).ToList();
                            instance.GetInsertBucket().AddItems(chunkItems, clearDistanceCache);
                        });

                        //Flushing items
                        this._parameters.Storage.FlushAddItems(this.GetVectorbyExternalId != null);
                    }
                    finally
                    {
                        _lock.ExitWriteLock();
                    }
                }
            }


            /// <summary>
            /// 
            /// </summary>
            /// <param name="item"></param>
            /// <param name="k"></param>
            /// <param name="clearDistanceCache"></param>
            /// <returns></returns>
            public IEnumerable<SmallWorld<TItem, TDistance>.KNNSearchResult> KNNSearch(TItem item, int k, bool clearDistanceCache = true)
            {                
                item = this._normalize(item);

                _lock.EnterReadLock();
                try
                {
                    if (k <= 0)
                        k = 1;

                    ReaderWriterLockSlim _sync = new ReaderWriterLockSlim();
                    var priorityQueue = new PriorityQueue<KNNSearchResult, TDistance>();

                    var nonVisited = this.BucketManager.GetSearchBucketIDs();

                    Parallel.ForEach(this.InstanceManager.CInstances, instance =>
                    {
                        var localQueue = new PriorityQueue<KNNSearchResult, TDistance>();

                        while (true)
                        {
                            int bucketId = -1;
                            _sync.EnterWriteLock();
                            if (nonVisited.Count > 0)
                            {
                                bucketId = nonVisited.First();
                                nonVisited.Remove(bucketId);
                            }
                            _sync.ExitWriteLock();
                            if (bucketId != -1)
                            {
                                var graph = instance.GetSearchBucket(bucketId);

                                var destination = graph.NewNode(-1, uint.MaxValue, item, 0, false);
                                var neighbourhood = graph.KNearest(destination, k);
                                foreach (var n in neighbourhood)
                                {
                                    var result = new KNNSearchResult
                                    {
                                        Id = n.Id,
                                        ExternalId = n.ExternalId,
                                        Item = n.Item,
                                        Distance = destination.From(n),
                                    };
                                    localQueue.Enqueue(result, result.Distance);                                    
                                }

                                if (clearDistanceCache)
                                    graph.DistanceCache.Clear();

                            }
                            else
                                break;
                        }

                        _sync.EnterWriteLock();
                        while (localQueue.Count>0)
                        {
                            var el = localQueue.Dequeue();
                            priorityQueue.Enqueue(el, el.Distance);
                        }
                        _sync.ExitWriteLock();

                    });

                    int u = 0;
                    while (priorityQueue.Count > 0 && u < k)
                    {
                        yield return priorityQueue.Dequeue();
                        u++;
                    }
                }
                finally
                {
                    _lock.ExitReadLock();
                }
                               
            }

            /// <summary>
            /// 
            /// </summary>
            /// <returns></returns>
            public long Count()
            {
                long counter = 0;

                _lock.EnterReadLock();
                try
                {
                    ReaderWriterLockSlim _sync = new ReaderWriterLockSlim();                   

                    var nonVisited = this.BucketManager.GetSearchBucketIDs();

                    Parallel.ForEach(this.InstanceManager.CInstances, instance =>
                    {
                        while (true)
                        {
                            int bucketId = -1;
                            _sync.EnterWriteLock();
                            if (nonVisited.Count > 0)
                            {
                                bucketId = nonVisited.First();
                                nonVisited.Remove(bucketId);
                            }
                            _sync.ExitWriteLock();
                            if (bucketId != -1)
                            {
                                var graph = instance.GetSearchBucket(bucketId);
                                Interlocked.Add(ref counter, graph.Count);

                            }
                            else
                                break;
                        }
                    });
                }
                finally
                {
                    _lock.ExitReadLock();
                }

                return counter;

            }

            //public IEnumerable<SmallWorld<TItem, TDistance>.KNNSearchResult> KNNSearch(TItem item, int k, bool clearDistanceCache = true)
            //{
            //    _lock.EnterReadLock();
            //    try
            //    {
            //        if (k <= 0)
            //            k = 1;

            //        ReaderWriterLockSlim _sync = new ReaderWriterLockSlim();
            //        PriorityQueue<SmallWorld<TItem, TDistance>.KNNSearchResult, TDistance> priorityQueue = new();

            //        var nonVisited = this.BucketManager.GetSearchBucketIDs();

            //        Parallel.ForEach(this.InstanceManager.CInstances, instance =>
            //        {
            //            while (true)
            //            {
            //                int bucketId = -1;
            //                _sync.EnterWriteLock();
            //                if (nonVisited.Count > 0)
            //                {
            //                    bucketId = nonVisited.First();
            //                    nonVisited.Remove(bucketId);
            //                }
            //                _sync.ExitWriteLock();
            //                if (bucketId != -1)
            //                {
            //                    var graph = instance.GetSearchBucket(bucketId);

            //                    var destination = graph.NewNode(-1, uint.MaxValue, item, 0);
            //                    var neighbourhood = graph.KNearest(destination, k);
            //                    var ret = neighbourhood.Select(
            //                       n => new KNNSearchResult
            //                       {
            //                           Id = n.Id,
            //                           ExternalId = n.ExternalId,
            //                           Item = n.Item,
            //                           Distance = destination.From(n),
            //                       }).ToList();

            //                    if (clearDistanceCache)
            //                        graph.DistanceCache.Clear();

            //                    _sync.EnterWriteLock();
            //                    foreach (var el in ret)
            //                    {
            //                        priorityQueue.Enqueue(el, el.Distance);
            //                    }
            //                    _sync.ExitWriteLock();
            //                }
            //                else
            //                    break;
            //            }



            //        });

            //        int u = 0;
            //        while (priorityQueue.Count > 0 && u < k)
            //        {
            //            yield return priorityQueue.Dequeue();
            //            u++;
            //        }
            //    }
            //    finally
            //    {
            //        _lock.ExitReadLock();
            //    }

            //}


            public void Flush()
            {
                this.BucketManager.Flush();
            }

        } //eoc Composer

        /// <summary>
        /// 
        /// </summary>
        internal class InstanceManager
        {
            public int InstanceQuantity = 1;
            public List<CInstance> CInstances = new List<CInstance>();
            public Composer _composer;

            public InstanceManager(Composer composer, int desiredInstanceQuantity = -1)
            {
                this._composer = composer;

                if (desiredInstanceQuantity > 0)
                {//manual choice
                    if (Environment.ProcessorCount < desiredInstanceQuantity)
                        InstanceQuantity = Environment.ProcessorCount;
                    else
                        InstanceQuantity = desiredInstanceQuantity;
                }
                else
                {//automatic choice - desiredInstanceQuantity<1 -                     
                    //automatic choice, taking about 70% (this._composer.AutomaticProcessorCountChoiceInProcent)
                    InstanceQuantity = Convert.ToInt32(Math.Ceiling((float)Environment.ProcessorCount * this._composer.AutomaticProcessorCountChoiceInProcent));
                }

                for (int i = 0; i < InstanceQuantity; i++)
                {
                    CInstances.Add(new CInstance(_composer));
                }

            }



        }//eoc InstanceManager

        /// <summary>
        /// 
        /// </summary>
        internal class CInstance
        {
            Bucket _bucket;
            public Composer _composer;

            public CInstance(Composer composer)
            {
                this._composer = composer;
            }

            public Graph GetInsertBucket()
            {
                var bucket = this._composer.BucketManager.GetInsertBucket(_bucket == null ? -1 : _bucket.BucketId);

                if((_bucket == null ? -1 : _bucket.BucketId) == bucket.BucketId) //same bucket remains
                    return _bucket.Graph;

                _bucket = bucket;
                return _bucket.Graph;
            }

            public Graph GetSearchBucket(int bucketId)
            {
                var bucket = this._composer.BucketManager.GetSearchBucket(bucketId);
                _bucket = bucket;
                return _bucket.Graph;
            }


        }//eoc CInstance

      

        internal class Bucket
        {
            public Graph Graph = null;
            //public int BucketId = 0;
            public bool Changed = false;
            public bool InInsertUse=false;
            public int BucketId = 0;

            public int Count { get {
                    return this.Graph.Count;
                } }            

        }//eoc Bucket


        /// <summary>
        /// 
        /// </summary>
        internal class BucketManager
        {
            Composer _composer;
            List<Bucket> _buckets = new List<Bucket>();            
            int initialBucketId = -1; //set it up on load
            ReaderWriterLockSlim _sync=new ReaderWriterLockSlim();

            public BucketManager(Composer composer)
            {
                this._composer = composer;               
                LoadBuckets();
            }

            void LoadBuckets()
            {
               
                _sync.EnterWriteLock();

                foreach (var b in _composer._parameters.Storage.GetBuckets())
                {
                    
                    var bucket = new Bucket()
                    {
                        BucketId = b.BucketId,
                    };
                    
                    bucket.Graph = new Graph(_composer, bucket, b.EntryPointId);
                    bucket.Graph.Count = b.Count;

                    _buckets.Add(bucket);
                }

                if(_buckets.Count>0)
                {
                    initialBucketId = _buckets.Count - 1;
                }
                else
                {
                    for (int inst = 0; inst < _composer.InstanceManager.InstanceQuantity; inst++)
                    {
                        NewBucket();
                    }
                }

                _sync.ExitWriteLock();
            }

            Bucket NewBucket()
            {//must be called inside the lock

                ++initialBucketId;
                var bucket = new Bucket()
                {
                    BucketId = initialBucketId,
                };

                bucket.Graph = new Graph(_composer, bucket, -1);

                _buckets.Add(bucket);

                return bucket;
            }

            public void Flush()
            {
                foreach (var b in _buckets)
                {
                    //Flushing bucket nodes
                    b.Graph.Flush();
                    if(b.Graph.Changed)
                    {  
                        //Flushing bucket info
                        this._composer._parameters.Storage.FlushBucket(b);
                    }

                }

                //Clearing Items (vectors) cache
                this._composer._parameters.Storage.ClearItemsCache();
            }

            public HashSet<int> GetSearchBucketIDs()
            {
                HashSet<int> ret;
                _sync.EnterReadLock();
                ret = _buckets.Where(r=>r.Count > 0).Select(r => r.BucketId).ToHashSet();
                _sync.ExitReadLock();
                return ret;
            }

            public Bucket GetSearchBucket(int bucketId)
            {
                Bucket bucket;
                _sync.EnterReadLock();
                bucket = _buckets[bucketId];
                _sync.ExitReadLock();

                return bucket;
            }

          

            Bucket GetFreeInsertBucketOrCreate()
            {//must be called inside the lock

                foreach(var b in _buckets)
                {
                    if (b.InInsertUse || b.Count >= this._composer.MaxItemsInBucket)
                        continue;
                    return b;
                }
                return NewBucket();               
            }

            public Bucket GetInsertBucket(int currentBucketId)
            {
                //Finding appropriate bucket
                if(currentBucketId == -1)
                {
                    _sync.EnterWriteLock();
                    var newBucket = GetFreeInsertBucketOrCreate();
                    newBucket.InInsertUse = true;
                    _sync.ExitWriteLock();
                    return newBucket;
                }
                else
                {
                    //check existing bucket count
                    _sync.EnterWriteLock();
                    var existingBucket = _buckets[currentBucketId];
                    if (_buckets[currentBucketId].Count >= this._composer.MaxItemsInBucket)
                    {
                        var newBucket = GetFreeInsertBucketOrCreate();
                        newBucket.InInsertUse= true;
                        existingBucket.InInsertUse = false;
                        _sync.ExitWriteLock();
                        return newBucket;
                    }
                    _sync.ExitWriteLock();
                    return existingBucket;
                }
            }



        }//eoc BucketManager


    }//eoc SmallWorld

}//eon
#endif