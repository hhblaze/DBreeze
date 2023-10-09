// <copyright file="Graph.Core.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// </copyright>

#if KNNSearch
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

    internal partial class Graph<TItem, TDistance>
    {
        internal class Core
        {
            private readonly Func<TItem, TItem, TDistance> Distance;

            private readonly DistanceCache<TDistance> DistanceCache;

            private long DistanceCalculationsCount;

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
            /// <param name="items">with external embedding ID as a key</param>
            /// <param name="generator"></param>
            /// <returns></returns>
            internal IReadOnlyList<int> AddItems(IReadOnlyDictionary<byte[], TItem> items, IProvideRandomValues generator, bool deferredIndexing = false)
            {
                var xnewIDs = Storage.AddItems(items, generator, NewNodeFunc, deferredIndexing: deferredIndexing);

                if (!deferredIndexing)
                {
                    DistanceCache?.Resize(xnewIDs.Count, false);
                }
                
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
                var r = -Math.Log(generator.NextFloat()) * lambda;
                return (int)r;
            }
        }
    }
}
#endif