/*
  Copyright https://github.com/wlou/HNSW.Net MIT License
  Copyright (C) 2012 dbreeze.tiesky.com / Oleksiy Solovyov / Ivars Sudmalis.
  It's a free software for those who think that it should be free.
*/
#if NET6FUNC || NET472

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
            /// <param name="parameters">The parameters of the algorithm.</param>
            
            public Graph( Parameters parameters)
            {
                this.Parameters = parameters;
               
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

                Node entryPoint = this.entryPoint;
                int ii = 0;
                var qIter = (items.Count + this.Count);

                if (entryPoint == null)
                {
                    var lItem = items[this.Count];
                    lItem.item = this._composer._normalize(lItem.item);
                      
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