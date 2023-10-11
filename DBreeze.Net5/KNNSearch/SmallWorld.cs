// <copyright file="SmallWorld.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// </copyright>

#if KNNSearch
namespace DBreeze.HNSW
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Linq;
    using System.Threading;
    //using MessagePack;
    //using MessagePackCompat;

    /// <summary>
    /// The Hierarchical Navigable Small World Graphs. https://arxiv.org/abs/1603.09320
    /// </summary>
    /// <typeparam name="TItem">The type of items to connect into small world.</typeparam>
    /// <typeparam name="TDistance">The type of distance between items (expect any numeric type: float, double, decimal, int, ...).</typeparam>
    internal partial class SmallWorld<TItem, TDistance> where TDistance : struct, IComparable<TDistance>
    {
        //private const string SERIALIZATION_HEADER = "HNSW";
        private readonly Func<TItem, TItem, TDistance> Distance;

        private Graph<TItem, TDistance> Graph;
        private IProvideRandomValues Generator;

        private ReaderWriterLockSlim _rwLock;

        ///// <summary>
        ///// Gets the list of items currently held by the SmallWorld graph. 
        ///// The list is not protected by any locks, and should only be used when it is known the graph won't change
        ///// </summary>
        //public IReadOnlyList<TItem> UnsafeItems => Graph?.GraphCore?.Items;

        ///// <summary>
        ///// Gets a copy of the list of items currently held by the SmallWorld graph. 
        ///// This call is protected by a read-lock and is safe to be called from multiple threads.
        ///// </summary>
        //public IReadOnlyList<TItem> Items
        //{
        //    get
        //    {
        //        if (_rwLock is object)
        //        {
        //            _rwLock.EnterReadLock();
        //            try
        //            {
        //                return Graph.GraphCore.Storage.Items.ToList();
        //            }
        //            finally
        //            {
        //                _rwLock.ExitReadLock();
        //            }
        //        }
        //        else
        //        {
        //            return Graph?.GraphCore?.Storage.Items;
        //        }
        //    }
        //}


        /// <summary>
        /// Initializes a new instance of the <see cref="SmallWorld{TItem, TDistance}"/> class.
        /// </summary>
        /// <param name="distance">The distance function to use in the small world.</param>
        /// <param name="generator">The random number generator for building graph.</param>
        /// <param name="parameters">Parameters of the algorithm.</param>
        public SmallWorld(Func<TItem, TItem, TDistance> distance, IProvideRandomValues generator, Parameters parameters,
            DBreeze.Transactions.Transaction tran, string tableName,
            bool threadSafe = true)
        {
            Distance = distance;
            Graph = new Graph<TItem, TDistance>(Distance, parameters, tran, tableName);
            Generator = generator;
            _rwLock = threadSafe ? new ReaderWriterLockSlim() : null;
        }

        /// <summary>
        /// Clusterization.KMeans
        /// </summary>
        /// <param name="k">quantity of desired clusters</param>
        /// <returns>Key: integer that represents clusterID (just 0,1,2 etc up to externalIDsAsCentroids.Count-1 or k-1)</returns>
        public Dictionary<int, List<byte[]>> KMeans(int k, List<byte[]> externalIDsAsCentroids = null)
        {
            _rwLock?.EnterWriteLock();
            try
            {
                return Graph.KMeans(k);
            }
            finally
            {
                _rwLock?.ExitWriteLock();
            }
            
        }

        ///// <summary>
        ///// Builds hnsw graph from the items.
        ///// </summary>
        ///// <param name="items">The items to connect into the graph.</param>

        //public IReadOnlyList<int> AddItems(IReadOnlyList<TItem> items, IProgressReporter progressReporter = null)
        //{
        //    _rwLock?.EnterWriteLock();
        //    try
        //    {
        //       return Graph.AddItems(items, Generator, progressReporter);
        //    }
        //    finally
        //    {
        //        _rwLock?.ExitWriteLock();
        //    }
        //}

        /// <summary>
        /// Builds hnsw graph from the items.
        /// </summary>
        /// <param name="items"></param>
        /// <param name="progressReporter"></param>
        /// <returns>IDs of added items</returns>
        public IReadOnlyList<int> AddItems(IReadOnlyDictionary<byte[], TItem> items, IProgressReporter progressReporter = null, bool deferredIndexing = false)
        {
            _rwLock?.EnterWriteLock();
            try
            {
                return Graph.AddItems(items, Generator, progressReporter, deferredIndexing: deferredIndexing);
            }
            finally
            {
                _rwLock?.ExitWriteLock();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="externalItemId"></param>
        public void ActivateItems(List<byte[]> externalItemIds, bool activate)
        {
            _rwLock?.EnterWriteLock();
            try
            {
                Graph.ActivateItems(externalItemIds, activate);
            }
            finally
            {
                _rwLock?.ExitWriteLock();
            }
        }

        /// <summary>
        /// Run knn search for a given item.
        /// </summary>
        /// <param name="item">The item to search nearest neighbours.</param>
        /// <param name="k">The number of nearest neighbours.</param>
        /// <param name="excludingDocuments">External Documents IDs to be excluded from the search</param>
        /// <returns>The list of found nearest neighbours.</returns>
        public IList<KNNSearchResult> KNNSearch(TItem item, int k, List<byte[]> excludingDocuments=null)
        {
            _rwLock?.EnterReadLock();
            try
            {
                return Graph.KNearest(item, k, excludingDocuments);
            }
            finally
            {
                _rwLock?.ExitReadLock();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="internalIDs"></param>
        public void IndexIDs(List<int> internalIDs, IProgressReporter progressReporter = null)
        {
            _rwLock?.EnterReadLock();
            try
            {
                Graph.IndexIDs(internalIDs,Generator);
            }
            finally
            {
                _rwLock?.ExitReadLock();
            }
        }

        /// <summary>
        /// Get the item with the index
        /// </summary>
        /// <param name="index">The index of the item</param>
        public TItem GetItem(int index)
        {
            _rwLock?.EnterReadLock();
            try
            {
                return Graph.GraphCore.Storage.Items[index];
                //return Items[index];
            }
            finally
            {
                _rwLock?.ExitReadLock();
            }
        }


        /// <summary>
        /// Prints edges of the graph. Mostly for debug and test purposes.
        /// </summary>
        /// <returns>String representation of the graph's edges.</returns>
        public string Print()
        {
            return Graph.Print();
        }

        /// <summary>
        /// Frees the memory used by the Distance Cache
        /// </summary>
        public void ResizeDistanceCache(int newSize)
        {
            Graph.GraphCore.ResizeDistanceCache(newSize);
        }

        //[MessagePackObject(keyAsPropertyName:true)]
        public class Parameters
        {
            public Parameters()
            {
                M = 10;
                LevelLambda = 1 / Math.Log(M);
                NeighbourHeuristic = NeighbourSelectionHeuristic.SelectSimple;
                ConstructionPruning = 200;
                ExpandBestSelection = false;
                KeepPrunedConnections = false;
                EnableDistanceCacheForConstruction = true;
                InitialDistanceCacheSize = 1024 * 1024;
                InitialItemsSize = 1024;
            }

            /// <summary>
            /// Gets or sets the parameter which defines the maximum number of neighbors in the zero and above-zero layers.
            /// The maximum number of neighbors for the zero layer is 2 * M.
            /// The maximum number of neighbors for higher layers is M.
            /// </summary>
            public int M { get; set; }

            /// <summary>
            /// Gets or sets the max level decay parameter. https://en.wikipedia.org/wiki/Exponential_distribution See 'mL' parameter in the HNSW article.
            /// </summary>
            public double LevelLambda { get; set; }

            /// <summary>
            /// Gets or sets parameter which specifies the type of heuristic to use for best neighbours selection.
            /// </summary>
            public NeighbourSelectionHeuristic NeighbourHeuristic { get; set; }

            /// <summary>
            /// Gets or sets the number of candidates to consider as neighbours for a given node at the graph construction phase. See 'efConstruction' parameter in the article.
            /// </summary>
            public int ConstructionPruning { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether to expand candidates if <see cref="NeighbourSelectionHeuristic.SelectHeuristic"/> is used. See 'extendCandidates' parameter in the article.
            /// </summary>
            public bool ExpandBestSelection { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether to keep pruned candidates if <see cref="NeighbourSelectionHeuristic.SelectHeuristic"/> is used. See 'keepPrunedConnections' parameter in the article.
            /// </summary>
            public bool KeepPrunedConnections { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether to cache calculated distances at graph construction time.
            /// </summary>
            public bool EnableDistanceCacheForConstruction { get; set; }

            /// <summary>
            /// Gets or sets a the initial distance cache size. 
            /// Note: This value is reset to 0 on deserialization to avoid allocating the distance cache for pre-built graphs.
            /// </summary>
            public int InitialDistanceCacheSize { get; set; }

            /// <summary>
            /// Gets or sets a the initial size of the Items list
            /// </summary>
            public int InitialItemsSize { get; set; }
        }

        public class KNNSearchResult
        {
            internal KNNSearchResult((TItem item, int id, byte[] externalId) itemEx, TDistance distance):this(itemEx.id, itemEx.item, distance)
            {
              ExternalId = itemEx.externalId;
            }

            internal KNNSearchResult(int id, TItem item, TDistance distance)
            {
                Id = id;
                Item = item;
                Distance = distance;
            }

            public int Id { get; }

            public TItem Item { get; }

            public TDistance Distance { get; }

            public byte[] ExternalId { get; }

            public override string ToString()
            {
                return $"I:{Id} Dist:{Distance:n2} [{Item}]";
            }
        }
    }
}
#endif