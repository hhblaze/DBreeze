#if NET6FUNC || NET472
// <copyright file="SmallWorld.Graph.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace DBreeze.HNSW
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.Serialization.Formatters.Binary; //TODO: remove serializers
    using System.Text;

    /// <content>
    /// The part with the implemenation of a hierarchical small world graph.
    /// </content>
    internal partial class SmallWorld<TItem, TDistance>
    {
        /// <summary>
        /// The layered graph implementation.
        /// </summary>
        internal class Graph
        {
            public Node entryPoint;

            public DistanceCache<TDistance> DistanceCache;
            public NodeCache NodeCache;

            //public Func<TItem, TItem, TDistance> Distance;

            /// <summary>
            /// Gets parameters of the algorithm.
            /// </summary>
            public Parameters Parameters { get; private set; }

            public Bucket _bucket;

            public Composer _composer;


            public Graph(Composer composer, Bucket bucket, int entryPointId=-1)
                :this(composer._parameters)
            {
                this._composer = composer;


                this._bucket = bucket;

                //if (composer._generator != null)
                //    this.RandomGenerator = composer._generator;

                if (entryPointId > -1)
                    entryPoint = this.NodeCache.GetNode(entryPointId);

            }

            internal TItem GetItem(long externalID)
            {
                return this._composer._parameters.Storage.GetItem(externalID, this._composer.GetVectorbyExternalId);
                
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="Graph"/> class.
            /// </summary>
            /// <param name="distance">The distance funtion to use in the small world.</param>
            /// <param name="parameters">The parameters of the algorithm.</param>
            //public Graph(Func<TItem, TItem, TDistance> distance, Parameters parameters)
            public Graph( Parameters parameters)
            {
                this.Parameters = parameters;
                //this.Distance = distance;

                this.DistanceCache=new DistanceCache<TDistance>();
                this.NodeCache = new NodeCache(this);

                switch (this.Parameters.NeighbourHeuristic)
                {
                    case NeighbourSelectionHeuristic.SelectHeuristic:
                        this.NewNode = (id, externalId, item, level, fromDB) => new NodeAlg4(id, externalId, item, level, this, fromDB);
                        break;

                    case NeighbourSelectionHeuristic.SelectSimple:
                    default:
                        this.NewNode = (id, externalId, item, level, fromDB) => new NodeAlg3(id, externalId, item, level, this, fromDB);
                        break;
                }

               
            }

            

            /// <summary>
            /// Gets the node factory associated with the graph.
            /// The node construction arguments are:
            /// 1st: int -> the id of the new node;
            /// 2nd: TItem -> the item to attach to the node;
            /// 3rd: int -> the level of the node.
            /// </summary>
            public Func<int, long, TItem, int, bool, Node> NewNode { get; private set; }

            public int Count { get; set; } = 0;

            Utils.FastRandom RandomGenerator = new Utils.FastRandom(42);
            //public Random RandomGenerator = new Random(42);

            //public Metrics Metrics { get; private set; }=new Metrics();

            public bool Changed = false;

            public void Flush()
            {
                if(_bucket!=null)
                {
                    if (this.NodeCache.Flush())
                        this.Changed = true;

                    this.NodeCache.Clear();
                }
            }
            
            /// <summary>
            /// 
            /// </summary>
            /// <param name="items"></param>
            /// <param name="clearDistanceCache"></param>
            public void AddItems(IList<(long externalId, TItem item)> items, bool clearDistanceCache = true)
            {
                if (!items?.Any() ?? false)
                    return;

                //Changed = true;

                Node entryPoint = this.entryPoint;
                int ii = 0;
                var qIter = (items.Count + this.Count);

                if (entryPoint == null)
                {
                    var lItem = items[this.Count];
                    lItem.item = this._composer._normalize(lItem.item);
                    //lItem.item = this.Parameters.Storage.NormalizeVector(lItem.item);   
                    entryPoint = this.NewNode(this.Count, lItem.externalId, lItem.item, RandomLevel(this.RandomGenerator, this.Parameters.LevelLambda), false);
                    //Adding to DB
                    this.Parameters.Storage.AddItem(lItem.externalId, this._bucket.BucketId, this.Count, lItem.item);

                    this.Count++;
                    ii++;
                   
                }

                
                for (int id = Count; id < qIter; ++id)
                {
                    /*
                     * W ← ∅ // list for the currently found nearest elements
                     * ep ← get enter point for hnsw
                     * L ← level of ep // top layer for hnsw
                     * l ← ⌊-ln(unif(0..1))∙mL⌋ // new element’s level
                     * for lc ← L … l+1
                     *   W ← SEARCH-LAYER(q, ep, ef=1, lc)
                     *   ep ← get the nearest element from W to q
                     * for lc ← min(L, l) … 0
                     *   W ← SEARCH-LAYER(q, ep, efConstruction, lc)
                     *   neighbors ← SELECT-NEIGHBORS(q, W, M, lc) // alg. 3 or alg. 4
                     *     for each e ∈ neighbors // shrink connections if needed
                     *       eConn ← neighbourhood(e) at layer lc
                     *       if │eConn│ > Mmax // shrink connections of e if lc = 0 then Mmax = Mmax0
                     *         eNewConn ← SELECT-NEIGHBORS(e, eConn, Mmax, lc) // alg. 3 or alg. 4
                     *         set neighbourhood(e) at layer lc to eNewConn
                     *   ep ← W
                     * if l > L
                     *   set enter point for hnsw to q
                     */

                    // zoom in and find the best peer on the same level as newNode
                    var bestPeer = entryPoint;
                    var lItem = items[ii];
                    lItem.item = this._composer._normalize(lItem.item);
                    //lItem.item = this.Parameters.Storage.NormalizeVector(lItem.item);
                    var newNode = this.NewNode(id, lItem.externalId, lItem.item, RandomLevel(this.RandomGenerator, this.Parameters.LevelLambda), false);

                    //Adding to DB
                    this.Parameters.Storage.AddItem(lItem.externalId, this._bucket.BucketId, id, lItem.item);

                    for (int level = bestPeer.MaxLevel; level > newNode.MaxLevel; --level)
                    {
                        bestPeer = KNearestAtLevel(bestPeer, newNode, 1, level).Single();
                    }

                    // connecting new node to the small world
                    for (int level = Math.Min(newNode.MaxLevel, entryPoint.MaxLevel); level >= 0; --level)
                    {
                        var potentialNeighbours = KNearestAtLevel(bestPeer, newNode, this.Parameters.ConstructionPruning, level);
                        var bestNeighbours = newNode.SelectBestForConnecting(potentialNeighbours);

                        foreach (var newNeighbour in bestNeighbours)
                        {
                            newNode.AddConnection(newNeighbour, level);
                            newNeighbour.AddConnection(newNode, level);

                            // if distance from newNode to newNeighbour is better than to bestPeer => update bestPeer
                            if (DLt(newNode.From(newNeighbour), newNode.From(bestPeer)))
                            {
                                bestPeer = newNeighbour;
                            }
                        }
                    }

                    // zoom out to the highest level
                    if (newNode.MaxLevel > entryPoint.MaxLevel)
                    {
                        entryPoint = newNode;
                    }

                    Count++;
                    ii++;
                }

                // construction is done
                this.entryPoint = entryPoint;

                if(clearDistanceCache)
                    this.DistanceCache.Clear();
            }

            /// <summary>
            /// Get k nearest items for a given one.
            /// Contains implementation of K-NN-SEARCH(hnsw, q, K, ef) algorithm.
            /// Article: Section 4. Algorithm 5.
            /// </summary>
            /// <param name="destination">The given node to get the nearest neighbourhood for.</param>
            /// <param name="k">The size of the neighbourhood.</param>
            /// <returns>The list of the nearest neighbours.</returns>
            public IList<Node> KNearest(Node destination, int k)
            {
                var bestPeer = this.entryPoint;
                for (int level = this.entryPoint.MaxLevel; level > 0; --level)
                {
                    bestPeer = KNearestAtLevel(bestPeer, destination, 1, level).Single();
                }

                return KNearestAtLevel(bestPeer, destination, k, 0);
            }

            ///// <summary>
            ///// Serializes edges of the graph.
            ///// </summary>
            ///// <returns>Bytes representing edges.</returns>
            //public byte[] Serialize()
            //{
            //    using (var stream = new MemoryStream())
            //    {
            //        var formatter = new BinaryFormatter();
            //        formatter.Serialize(stream, this.entryPoint.Id);
            //        formatter.Serialize(stream, this.entryPoint.ExternalId);
            //        formatter.Serialize(stream, this.entryPoint.MaxLevel);                    
            //        formatter.Serialize(stream, this.Count);

            //        for (int level = this.entryPoint.MaxLevel; level >= 0; --level)
            //        {
            //            var edges = new Dictionary<(int, long), List<(int, long)>>();
            //            BFS(this.entryPoint, level, (node) =>
            //            {
            //                edges[(node.Id, node.ExternalId)] = node.GetConnections(level).Select(x => (x.Id, x.ExternalId)).ToList();
            //            });

            //            formatter.Serialize(stream, edges);
            //        }

            //        return stream.ToArray();
            //    }
            //}

            /* new serializ
             
              public byte[] Serialize()
    {
        var graphData = new GraphSerializationData
        {
            EntryPointId = this.entryPoint.Id,
            EntryPointExternalId = this.entryPoint.ExternalId,
            EntryPointMaxLevel = this.entryPoint.MaxLevel,
            Count = this.Count,
            EdgesByLevel = new List<Dictionary<(int, long), List<(int, long)>>>()
        };

        for (int level = this.entryPoint.MaxLevel; level >= 0; --level)
        {
            var edges = new Dictionary<(int, long), List<(int, long)>>();
            BFS(this.entryPoint, level, (node) =>
            {
                edges[(node.Id, node.ExternalId)] = node.GetConnections(level).Select(x => (x.Id, x.ExternalId)).ToList();
            });
            graphData.EdgesByLevel.Add(edges);
        }

        return Encoder.Serialize(graphData);  // Use Biser's Encoder
    }


            public void Deserialize(byte[] bytes)
    {
        GraphSerializationData graphData = Decoder.Decode<GraphSerializationData>(bytes);  // Use Biser's Decoder

        Dictionary<int, Node> nodeList = new Dictionary<int, Node>();

        Func<(int id, long externalId), int, Node> getOrAdd = (idPair, level) =>
        {
            var item = this.Parameters.Storage.GetItem(idPair.externalId);

            if (!nodeList.TryGetValue(idPair.id, out var foundNode))
            {
                foundNode = this.NewNode(idPair.id, idPair.externalId, item, level);
                nodeList[idPair.id] = foundNode;
            }
            return foundNode;
        };

        this.Count = graphData.Count;

        // Restoring root
        var rootItem = this.Parameters.Storage.GetItem(graphData.EntryPointExternalId);
        nodeList[graphData.EntryPointId] = this.NewNode(graphData.EntryPointId, graphData.EntryPointExternalId, rootItem, graphData.EntryPointMaxLevel);

        // Restoring the rest
        for (int level = graphData.EntryPointMaxLevel; level >= 0; --level)
        {
            var edges = graphData.EdgesByLevel[graphData.EntryPointMaxLevel - level]; // Access in reverse order
            foreach (var pair in edges)
            {
                var currentNode = getOrAdd(pair.Key, level);
                foreach (var adjacentId in pair.Value)
                {
                    var neighbour = getOrAdd(adjacentId, level);
                    currentNode.AddConnection(neighbour, level);
                }
            }
        }

        this.entryPoint = nodeList[graphData.EntryPointId];
    }


    [Biser.Serialization.BiserObject]  // Mark for Biser serialization
    private class GraphSerializationData
    {
        [Biser.Serialization.BiserMember(1)]
        public int EntryPointId { get; set; }

        [Biser.Serialization.BiserMember(2)]
        public long EntryPointExternalId { get; set; }

        [Biser.Serialization.BiserMember(3)]
        public int EntryPointMaxLevel { get; set; }

        [Biser.Serialization.BiserMember(4)]
        public int Count { get; set; }

        [Biser.Serialization.BiserMember(5)]
        public List<Dictionary<(int, long), List<(int, long)>>> EdgesByLevel { get; set; }
    }
             */


            ///// <summary>
            ///// Deserilaizes graph edges and assigns nodes to the items.
            ///// </summary>            
            ///// <param name="bytes">The serialized edges.</param>            
            //public void Deserialize(byte[] bytes)
            //{
            //    //public void Deserialize(IList<(long externalId, TItem item)> items, byte[] bytes)
            //    //var nodeList = Enumerable.Repeat<Node>(null, items.Count).ToList();
            //    Dictionary<int, Node> nodeList = new Dictionary<int, Node>();

            //    Func<(int id, uint externalId), int, Node> getOrAdd = (idPair, level) =>
            //    {
            //        var item = this.GetItem(idPair.externalId);
            //        //var item = this.Parameters.Storage.GetItem(idPair.externalId);
            //        //nodeList[idPair.id] = nodeList[idPair.id] ?? this.NewNode(idPair.id, idPair.externalId, item, level);

            //        if (nodeList.TryGetValue(idPair.id, out var founNode))
            //            nodeList[idPair.id]=founNode;
            //        else
            //            nodeList[idPair.id] = this.NewNode(idPair.id, idPair.externalId, item, level, false);

            //        return nodeList[idPair.id];
            //    };

            //    using (var stream = new MemoryStream(bytes))
            //    {
            //        var formatter = new BinaryFormatter();
            //        int entryId = (int)formatter.Deserialize(stream);
            //        uint entryExternalId = (uint)formatter.Deserialize(stream);
            //        int maxLevel = (int)formatter.Deserialize(stream);
                    
            //        this.Count = (int)formatter.Deserialize(stream);

            //        //Restoring root
            //        //var rootItem = this.Parameters.Storage.GetItem(entryExternalId);// items[entryId];                    
            //        var rootItem = this.GetItem(entryExternalId);// items[entryId];                    
            //        nodeList[entryId] = this.NewNode(entryId, entryExternalId, rootItem, maxLevel, false);
            //        //Restoring the rest
            //        for (int level = maxLevel; level >= 0; --level)
            //        {
            //            var edges = (Dictionary<(int id, uint extId), List<(int id, uint extId)>>)formatter.Deserialize(stream);
            //            foreach (var pair in edges)
            //            {
            //                var currentNode = getOrAdd(pair.Key, level);
            //                foreach (var adjacentId in pair.Value)
            //                {
            //                    var neighbour = getOrAdd(adjacentId, level);
            //                    currentNode.AddConnection(neighbour, level);
            //                }
            //            }
            //        }

            //        this.entryPoint = nodeList[entryId];
            //    }
            //}

            ///// <summary>
            ///// Prints edges of the graph.
            ///// </summary>
            ///// <returns>String representation of the graph's edges.</returns>
            //internal string Print()
            //{
            //    var buffer = new StringBuilder();
            //    for (int level = this.entryPoint.MaxLevel; level >= 0; --level)
            //    {
            //        buffer.AppendLine($"[LEVEL {level}]");
            //        BFS(this.entryPoint, level, (node) =>
            //        {
            //            var neighbours = string.Join(", ", node.GetConnections(level).Select(x => x.Id));
            //            buffer.AppendLine($"({node.Id}) -> {{{neighbours}}}");
            //        });

            //        buffer.AppendLine();
            //    }

            //    return buffer.ToString();
            //}

            /// <summary>
            /// The implementaiton of SEARCH-LAYER(q, ep, ef, lc) algorithm.
            /// Article: Section 4. Algorithm 2.
            /// </summary>
            /// <param name="entryPoint">The entry point for the search.</param>
            /// <param name="destination">The search target.</param>
            /// <param name="k">The number of the nearest neighbours to get from the layer.</param>
            /// <param name="level">Level of the layer.</param>
            /// <returns>The list of the nearest neighbours at the level.</returns>
            private static List<Node> KNearestAtLevel(Node entryPoint, Node destination, int k, int level)
            {
                /*
                 * v ← ep // set of visited elements
                 * C ← ep // set of candidates
                 * W ← ep // dynamic list of found nearest neighbors
                 * while │C│ > 0
                 *   c ← extract nearest element from C to q
                 *   f ← get furthest element from W to q
                 *   if distance(c, q) > distance(f, q)
                 *     break // all elements in W are evaluated
                 *   for each e ∈ neighbourhood(c) at layer lc // update C and W
                 *     if e ∉ v
                 *       v ← v ⋃ e
                 *       f ← get furthest element from W to q
                 *       if distance(e, q) < distance(f, q) or │W│ < ef
                 *         C ← C ⋃ e
                 *         W ← W ⋃ e
                 *         if │W│ > ef
                 *           remove furthest element from W to q
                 * return W
                 */

                // prepare tools
                IComparer<Node> closerIsLess = destination;//.TravelingCosts;
                IComparer<Node> fartherIsLess = closerIsLess.Reverse();

                // prepare heaps
                var resultHeap = new BinaryHeap<Node>(new List<Node>(k + 1) { entryPoint }, closerIsLess);
                var expansionHeap = new BinaryHeap<Node>(new List<Node>() { entryPoint }, fartherIsLess);

                // run bfs
                var visited = new HashSet<int>() { entryPoint.Id };
                while (expansionHeap.Buffer.Any())
                {
                    // get next candidate to check and expand
                    var toExpand = expansionHeap.Pop();
                    var farthestResult = resultHeap.Buffer.First();
                    if (DGt(destination.From(toExpand), destination.From(farthestResult)))
                    {
                        // the closest candidate is farther than farthest result
                        break;
                    }

                    // expand candidate
                    foreach (var neighbour in toExpand.GetConnections(level))
                    {
                        if (!visited.Contains(neighbour.Id))
                        {
                            // enque perspective neighbours to expansion list
                            farthestResult = resultHeap.Buffer.First();
                            if (resultHeap.Buffer.Count < k
                            || DLt(destination.From(neighbour), destination.From(farthestResult)))
                            {
                                expansionHeap.Push(neighbour);
                                resultHeap.Push(neighbour);
                                if (resultHeap.Buffer.Count > k)
                                {
                                    resultHeap.Pop();
                                }
                            }

                            // update visited list
                            visited.Add(neighbour.Id);
                        }
                    }
                }

                return resultHeap.Buffer;
            }

            /// <summary>
            /// Gets the level for the layer.
            /// </summary>
            /// <param name="generator">The random numbers generator.</param>
            /// <param name="lambda">Poisson lambda.</param>
            /// <returns>The level value.</returns>
            private static int RandomLevel(Utils.FastRandom generator, double lambda)
            {   
                var r = -Math.Log(generator.NextDouble()) * lambda;
                return (int)r;
            }
        }
    }
}
#endif