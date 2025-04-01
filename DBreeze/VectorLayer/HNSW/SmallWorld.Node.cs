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
    using System.Linq;

    /// <content>
    /// The part with the implementaion of a node in the hnsw graph.
    /// </content>
    internal partial class SmallWorld<TItem, TDistance>
    {
        /// <summary>
        /// The abstract node implementation.
        /// The <see cref="SelectBestForConnecting(IList{Node})"/> must be implemented by the subclass.
        /// </summary>
        internal abstract class Node : IComparer<Node>
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="Node"/> class.
            /// </summary>
            /// <param name="id">The identifier of the node.</param>
            /// <param name="item">The item which is represented by the node.</param>
            /// <param name="maxLevel">The maximum level until which the node exists.</param>
            /// <param name="distance">The distance function for attached items.</param>
            /// <param name="parameters">The parameters of the algorithm.</param>
            public Node(int id, long externalId, TItem item, int maxLevel, SmallWorld<TItem, TDistance>.Graph graph, bool fromDB=false)
            {
                this.Id = id;
                this.ExternalId = externalId;               
                this.MaxLevel = maxLevel;                

                this.Graph = graph;

                if(!fromDB)
                {
                    if (externalId == uint.MaxValue) //needed for the search (virtual node is being created)
                        TempItem = item;
                    else
                    {
                        //only real nodes in the cache
                        graph.NodeCache.AddNode(this);
                                                
                        this.Changed = true;
                    }


                    this.Connections = new List<List<int>>(this.MaxLevel + 1);
                    for (int level = 0; level <= this.MaxLevel; ++level)
                    {
                        this.Connections.Add(new List<int>(GetM(this.Graph.Parameters.M, level)));
                    }
                    
                }

            }

            private static readonly Comparer<TDistance> DistanceComparer = Comparer<TDistance>.Default;

            public TDistance From(Node departure)
            {              

                TDistance result;              

                if (!this.Graph.DistanceCache.TryGetValue(this.Id, departure.Id, out result))
                {
                    //result = this.distance(departure, this.destination);
                    result = this.Graph._composer._distance(departure.Item, this.Item); //Access via graph

                    this.Graph.DistanceCache.Add(this.Id, departure.Id, result);
                }

                return result;
            }
            public int Compare(Node x, Node y)
            {
                var fromX = this.From(x);
                var fromY = this.From(y);
                return DistanceComparer.Compare(fromX, fromY);
            }

            public SmallWorld<TItem, TDistance>.Graph Graph = null;

            /// <summary>
            /// Gets the identifier of the node.
            /// </summary>
            public int Id { get; private set; }

            /// <summary>
            /// Gets the external identifier of the item.
            /// </summary>
            public long ExternalId { get; private set; }

            /// <summary>
            /// Gets the maximum level of the node.
            /// </summary>
            public int MaxLevel { get; private set; }

            /// <summary>
            /// Needed for the knn-search, for the query the Node is also created
            /// </summary>
            public TItem TempItem { get; private set; }

            /// <summary>
            /// Gets the item associated with the node.
            /// </summary>
            public TItem Item 
            { 
                get { 
                    if(TempItem == null)
                    {  
                        return this.Graph.GetItem(this.ExternalId);
                       
                    }                        
                    else
                        return TempItem;
                }
                
            }

            public bool Changed=false;

            /// <summary>
            /// Gets all connections of the node on all layers.
            /// </summary>            
            public List<List<int>> Connections { get; set; }           

            private static readonly List<Node> EmptyNodeList = new List<Node>();
            

            public List<Node> GetConnections(int level)
            {
                if (level < this.Connections.Count)
                {

                    return this.Connections[level].Select(r => this.Graph.NodeCache.GetNode(r)).ToList();
                    //return this.Connections[level];
                }

                return EmptyNodeList;
            }
          

            /// <summary>
            /// Add connections to the node on the specific layer.
            /// </summary>
            /// <param name="newNeighbour">The node to connect with.</param>
            /// <param name="level">The level of the layer.</param>           
            public void AddConnection(Node newNeighbour, int level)
            {
                var levelConnections = this.Connections[level];
                if ((levelConnections.Count + 1) > GetM(this.Graph.Parameters.M, level))
                {
                    var levelNeighbours = levelConnections.Select(r => this.Graph.NodeCache.GetNode(r)).ToList();
                    levelNeighbours.Add(newNeighbour);
                    this.Connections[level] = this.SelectBestForConnecting(levelNeighbours).Select(r => r.Id).ToList();
                }
                else
                {
                    this.Connections[level].Add(newNeighbour.Id);
                }

                this.Changed = true;
            }
           

            /// <summary>
            /// The algorithm which selects best neighbours from the candidates for this node.
            /// </summary>
            /// <param name="candidates">The candidates for connecting.</param>
            /// <returns>Best nodes selected from the candidates.</returns>
            public abstract List<Node> SelectBestForConnecting(List<Node> candidates);

            /// <summary>
            /// Get maximum allowed connections for the given layer.
            /// </summary>
            /// <remarks>
            /// Article: Section 4.1:
            /// "Selection of the Mmax0 (the maximum number of connections that an element can have in the zero layer) also
            /// has a strong influence on the search performance, especially in case of high quality(high recall) search.
            /// Simulations show that setting Mmax0 to M(this corresponds to kNN graphs on each layer if the neighbors
            /// selection heuristic is not used) leads to a very strong performance penalty at high recall.
            /// Simulations also suggest that 2∙M is a good choice for Mmax0;
            /// setting the parameter higher leads to performance degradation and excessive memory usage".
            /// </remarks>
            /// <param name="baseM">Base M parameter of the algorithm.</param>
            /// <param name="level">The level of the layer.</param>
            /// <returns>The maximum number of connections.</returns>
            protected static int GetM(int baseM, int level)
            {
                return level == 0 ? 2 * baseM : baseM;
            }
        }

        /// <summary>
        /// The implementation of the SELECT-NEIGHBORS-SIMPLE(q, C, M) algorithm.
        /// Article: Section 4. Algorithm 3.
        /// </summary>
        private class NodeAlg3 : Node
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="NodeAlg3"/> class.
            /// </summary>
            /// <param name="id">The identifier of the node.</param>
            /// <param name="item">The item which is represented by the node.</param>
            /// <param name="maxLevel">The maximum level until which the node exists.</param>
            /// <param name="distance">The distance function for attached items.</param>
            /// <param name="parameters">The parameters of the algorithm.</param>
            public NodeAlg3(int id, long externalId, TItem item, int maxLevel, SmallWorld<TItem, TDistance>.Graph graph, bool fromDB = false)
                : base(id, externalId, item, maxLevel, graph, fromDB)
            {
            }

            /// <inheritdoc />
            public override List<Node> SelectBestForConnecting(List<Node> candidates)
            {
                /*
                 * q ← this
                 * return M nearest elements from C to q
                 */

                IComparer<Node> fartherIsLess = this.Reverse();
                var candidatesHeap = new BinaryHeap<Node>(candidates, fartherIsLess);

                var result = new List<Node>(GetM(this.Graph.Parameters.M, this.MaxLevel) + 1);
                while (candidatesHeap.Buffer.Any() && result.Count < GetM(this.Graph.Parameters.M, this.MaxLevel))
                {
                    result.Add(candidatesHeap.Pop());
                }

                return result;
            }
        }

        /// <summary>
        /// The implementation of the SELECT-NEIGHBORS-HEURISTIC(q, C, M, lc, extendCandidates, keepPrunedConnections) algorithm.
        /// Article: Section 4. Algorithm 4.
        /// </summary>
        private class NodeAlg4 : Node
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="NodeAlg4"/> class.
            /// </summary>
            /// <param name="id">The identifier of the node.</param>
            /// <param name="item">The item which is represented by the node.</param>
            /// <param name="maxLevel">The maximum level until which the node exists.</param>
            /// <param name="distance">The distance function for attached items.</param>
            /// <param name="parameters">The parameters of the algorithm.</param>
            public NodeAlg4(int id, long externalId, TItem item, int maxLevel, SmallWorld<TItem, TDistance>.Graph graph, bool fromDB = false)
                : base(id, externalId, item, maxLevel, graph, fromDB)
            {
            }

            /// <inheritdoc />
            public override List<Node> SelectBestForConnecting(List<Node> candidates)
            {
                /*
                * q ← this
                * R ← ∅    // result
                * W ← C    // working queue for the candidates
                * if expandCandidates  // expand candidates
                *   for each e ∈ C
                *     for each eadj ∈ neighbourhood(e) at layer lc
                *       if eadj ∉ W
                *         W ← W ⋃ eadj
                *
                * Wd ← ∅ // queue for the discarded candidates
                * while │W│ gt 0 and │R│ lt M
                *   e ← extract nearest element from W to q
                *   if e is closer to q compared to any element from R
                *     R ← R ⋃ e
                *   else
                *     Wd ← Wd ⋃ e
                *
                * if keepPrunedConnections // add some of the discarded connections from Wd
                *   while │Wd│ gt 0 and │R│ lt M
                *   R ← R ⋃ extract nearest element from Wd to q
                *
                * return R
                */

                IComparer<Node> closerIsLess = this;
                IComparer<Node> fartherIsLess = closerIsLess.Reverse();

                var resultHeap = new BinaryHeap<Node>(new List<Node>(GetM(this.Graph.Parameters.M, this.MaxLevel) + 1), closerIsLess);
                var candidatesHeap = new BinaryHeap<Node>(candidates, fartherIsLess);

                // expand candidates option is enabled
                if (this.Graph.Parameters.ExpandBestSelection)
                {
                    var candidatesIds = new HashSet<int>(candidates.Select(c => c.Id));
                    foreach (var neighbour in this.GetConnections(this.MaxLevel))
                    {
                        if (!candidatesIds.Contains(neighbour.Id))
                        {
                            candidatesHeap.Push(neighbour);
                            candidatesIds.Add(neighbour.Id);
                        }
                    }
                }

                // main stage of moving candidates to result
                var discardedHeap = new BinaryHeap<Node>(new List<Node>(candidatesHeap.Buffer.Count), fartherIsLess);
                while (candidatesHeap.Buffer.Any() && resultHeap.Buffer.Count < GetM(this.Graph.Parameters.M, this.MaxLevel))
                {
                    var candidate = candidatesHeap.Pop();
                    var farestResult = resultHeap.Buffer.FirstOrDefault();

                    if (farestResult == null
                    || DLt(this.From(candidate), this.From(farestResult)))
                    {
                        resultHeap.Push(candidate);
                    }
                    else if (this.Graph.Parameters.KeepPrunedConnections)
                    {
                        discardedHeap.Push(candidate);
                    }
                }

                // keep pruned option is enabled
                if (this.Graph.Parameters.KeepPrunedConnections)
                {
                    while (discardedHeap.Buffer.Any() && resultHeap.Buffer.Count < GetM(this.Graph.Parameters.M, this.MaxLevel))
                    {
                        resultHeap.Push(discardedHeap.Pop());
                    }
                }

                return resultHeap.Buffer;
            }
        }
    }
}
#endif