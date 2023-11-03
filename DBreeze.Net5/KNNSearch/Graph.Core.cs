// <copyright file="Graph.Core.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// </copyright>

#if NET6FUNC
namespace DBreeze.HNSW
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Threading;
    //using MessagePack;
    using DBreeze;

    using static DBreeze.HNSW.EventSources;
    using System.Net.Http.Headers;
    using System.Linq;

    internal partial class Graph<TItem, TDistance>
    {
        internal class Core
        {
            private readonly Func<TItem, TItem, TDistance> Distance;
            //-
            public readonly DistanceCache<TDistance> DistanceCache;
            //-
            public long DistanceCalculationsCount;

            internal DBStorage<TItem> Storage { get; private set; }

            ////internal List<Node> Nodes { get; private set; }
            //internal NodeList Nodes { get; private set; }

            ////internal List<TItem> Items { get; private set; }
            //internal ItemList<TItem> Items { get; private set; }

            public Algorithms.Algorithm<TItem, TDistance> Algorithm { get; private set; }

            public SmallWorld<TItem, TDistance>.Parameters Parameters { get; private set; }

            internal float DistanceCacheHitRate => (float)(DistanceCache?.HitCount ?? 0) / DistanceCalculationsCount;

            internal Core(Func<TItem, TItem, TDistance> distance, SmallWorld<TItem, TDistance>.Parameters parameters, DBreeze.Transactions.Transaction tran, string tableName)
            {
                Distance = distance;
                Parameters = parameters;
                
                //var initialSize = Math.Max(1024, parameters.InitialItemsSize);

                Storage = new DBStorage<TItem>(tran, tableName);

                //-origcode
                //Nodes = new List<Node>(initialSize);
                //Items = new List<TItem>(initialSize);

                switch (Parameters.NeighbourHeuristic)
                {
                    case NeighbourSelectionHeuristic.SelectSimple:
                    {
                        Algorithm = new Algorithms.Algorithm3<TItem, TDistance>(this);
                        break;
                    }
                    case NeighbourSelectionHeuristic.SelectHeuristic:
                    {
                        Algorithm = new Algorithms.Algorithm4<TItem, TDistance>(this);
                        break;
                    }
                }

                if (Parameters.EnableDistanceCacheForConstruction)
                {
                    DistanceCache = new DistanceCache<TDistance>();
                    
                    DistanceCache.Resize(parameters.InitialDistanceCacheSize, false);
                }

                DistanceCalculationsCount = 0;
            }

           
            /// <summary>
            /// 
            /// </summary>
            /// <param name="k"></param>
            /// <param name="externalIDsToFormCentroids"></param>
            /// <returns></returns>
            internal Dictionary<int, List<byte[]>> KMeans(int k, List<byte[]> externalIDsAsCentroids = null)
            {
                Storage.CacheIsActive = true;

                List<int> initialCentroids = null;
                if ((externalIDsAsCentroids?.Count ?? 0) > 0)
                {
                    initialCentroids = new List<int>(externalIDsAsCentroids.Count);

                    foreach (var el in externalIDsAsCentroids)
                        initialCentroids.Add(Storage.Items.GetItemByExternalID(el).Item2);
                }

                var res = Clustering.KMeansCluster((ItemList<double[]>)(object)Storage.Items, k, (Func<double[], double[], double>)(object)Distance, initialCentroids: initialCentroids);
               
                Dictionary<int, List<byte[]>> d = res
                .Select((pair, index) => new { Index = index, Items = pair.Value })
                .ToDictionary(
                    entry => entry.Index,
                    entry => entry.Items.Select(intId => Storage.Items.GetItem(intId).ItemInDB.ExternalID).ToList()
                );

                Storage.CacheIsActive = false;
                return d;
            }

            ///// <summary>
            ///// 
            ///// </summary>
            ///// <param name="clusterPrototypes"></param>
            ///// <param name="itemsToBeClustered"></param>
            ///// <returns></returns>
            //internal Dictionary<int, List<int>> KMeans(List<float[]> clusterPrototypes, List<float[]> itemsToBeClustered)
            //{
            //    return Clustering.KMeansCluster(clusterPrototypes, itemsToBeClustered, (Func<float[], float[], float>)(object)Distance);                
            //}

            /// <summary>
            /// 
            /// </summary>
            /// <param name="clusterPrototypes"></param>
            /// <param name="itemsToBeClustered"></param>
            /// <returns></returns>
            internal Dictionary<int, List<int>> KMeans(List<double[]> clusterPrototypes, List<double[]> itemsToBeClustered)
            {
                return Clustering.KMeansCluster(clusterPrototypes, itemsToBeClustered, (Func<double[], double[], double>)(object)Distance);
            }

            /// <summary>
            /// 
            /// </summary>
            /// <param name="externalDocumentIDs"></param>
            /// <returns></returns>
            internal List<TItem> GetVectorsByExternalDocumentIDs(List<byte[]> externalDocumentIDs)
            {
                return externalDocumentIDs.Select(r=> (TItem)(object)Storage.Items.GetItemByExternalID(r).Item1.VectorDouble).ToList();              
            }

            /// <summary>
            /// 
            /// </summary>
            /// <param name="items">with external embedding ID as a key</param>
            /// <param name="generator"></param>
            /// <returns></returns>
            internal IReadOnlyList<int> AddItems(IReadOnlyDictionary<byte[], TItem> items, IProvideRandomValues generator, bool deferredIndexing = false)
            {
                var xnewIDs = Storage.AddItems(items, generator, NewNodeFunc, deferredIndexing: deferredIndexing);
               
                return xnewIDs;

                //int newCount = items.Count;

                //var newIDs = new List<int>();
                //Items.AddRange(items);
                //DistanceCache?.Resize(newCount, false);

                //int id0 = Nodes.Count;

                //for (int id = 0; id < newCount; ++id)
                //{
                //    Nodes.Add(Algorithm.NewNode(id0 + id, RandomLayer(generator, Parameters.LevelLambda)));
                //    newIDs.Add(id0 + id);
                //}
                //return newIDs;
            }

            ///// <summary>
            ///// 
            ///// </summary>
            ///// <param name="items"></param>
            ///// <param name="generator"></param>
            ///// <returns></returns>
            //internal IReadOnlyList<int> AddItems(IReadOnlyList<TItem> items, IProvideRandomValues generator)
            //{
            //    var xnewIDs = Storage.AddItems(items, generator, NewNodeFunc);

            //    DistanceCache?.Resize(xnewIDs.Count, false);

            //    return xnewIDs;

            //    //int newCount = items.Count;

            //    //var newIDs = new List<int>();
            //    //Items.AddRange(items);
            //    //DistanceCache?.Resize(newCount, false);

            //    //int id0 = Nodes.Count;

            //    //for (int id = 0; id < newCount; ++id)
            //    //{
            //    //    Nodes.Add(Algorithm.NewNode(id0 + id, RandomLayer(generator, Parameters.LevelLambda)));
            //    //    newIDs.Add(id0 + id);
            //    //}
            //    //return newIDs;
            //}

            /// <summary>
            /// 
            /// </summary>
            /// <param name="externalItemIds"></param>
            internal void ActivateItems(List<byte[]> externalItemIds, bool activate)
            {
                Storage.Items.ActivateItems(externalItemIds, activate);
            }

            /// <summary>
            /// 
            /// </summary>
            /// <param name="nodeId"></param>
            /// <param name="generator"></param>
            /// <returns></returns>
            public Node NewNodeFunc(int nodeId, IProvideRandomValues generator)
            {
                return Algorithm.NewNode(nodeId, RandomLayer(generator, Parameters.LevelLambda));
            }


            internal void ResizeDistanceCache(int newSize)
            {
                DistanceCache?.Resize(newSize, true);
            }

            //internal void Serialize(Stream stream)
            //{
            //    MessagePackSerializer.Serialize(stream, Nodes);
            //}

            //internal void Deserialize(IReadOnlyList<TItem> items, Stream stream)
            //{
            //    // readStrict: true -> removed, as not available anymore on MessagePack 2.0 - also probably not necessary anymore
            //    //                     see https://github.com/neuecc/MessagePack-CSharp/pull/663

            //    Nodes = MessagePackSerializer.Deserialize<List<Node>>(stream);
            //    Items.AddRange(items);
            //}

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal TDistance GetDistance(int fromId, int toId)
            {
                DistanceCalculationsCount++;
                if (DistanceCache is object)
                {
                    return DistanceCache.GetOrCacheValue(fromId, toId, GetDistanceSkipCache);
                }
                else
                {
                    return Distance(Storage.Items[fromId], Storage.Items[toId]);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private TDistance GetDistanceSkipCache(int fromId, int toId)
            {
                return Distance(Storage.Items[fromId], Storage.Items[toId]);
            }

            public static int RandomLayer(IProvideRandomValues generator, double lambda)
            {
                //var r = -Math.Log(generator.NextFloat()) * lambda;
                var r = -Math.Log(generator.NextDouble()) * lambda;
                return (int)r;
            }
        }
    }
}
#endif