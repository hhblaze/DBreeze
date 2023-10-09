// <copyright file="Node.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace DBreeze.HNSW
{
    using System;
    using System.Collections.Generic;

    internal partial class Algorithms
    {
        /// <summary>
        /// The abstract class representing algorithm to control node capacity.
        /// </summary>
        /// <typeparam name="TItem">The typeof the items in the small world.</typeparam>
        /// <typeparam name="TDistance">The type of the distance in the small world.</typeparam>
        internal abstract class Algorithm<TItem, TDistance> where TDistance : struct, IComparable<TDistance>
        {
            protected readonly Graph<TItem, TDistance>.Core GraphCore;

            protected readonly Func<int, int, TDistance> NodeDistance;

            public Algorithm(Graph<TItem, TDistance>.Core graphCore)
            {
                GraphCore = graphCore;
                NodeDistance = graphCore.GetDistance;
            }

            /// <summary>
            /// Creates a new instance of the <see cref="Node"/> struct. Controls the exact type of connection lists.
            /// </summary>
            /// <param name="nodeId">The identifier of the node.</param>
            /// <param name="maxLayer">The max layer where the node is presented.</param>
            /// <returns>The new instance.</returns>
            internal virtual Node NewNode(int nodeId, int maxLayer)
            {
                var connections = new List<List<int>>(maxLayer + 1);
                //-test var connections = new List<List<int>>();
                for (int layer = 0; layer <= maxLayer; ++layer)
                {
                    // M + 1 neighbours to not realloc in AddConnection when the level is full
                    int layerM = GetM(layer) + 1;
                    connections.Add(new List<int>(layerM));
                    //-test connections.Add(new List<int>());
                }

                return new Node
                {
                    Id = nodeId,
                    Connections = connections
                };
            }

            /// <summary>
            /// The algorithm which selects best neighbours from the candidates for the given node.
            /// </summary>
            /// <param name="candidatesIds">The identifiers of candidates to neighbourhood.</param>
            /// <param name="travelingCosts">Traveling costs to compare candidates.</param>
            /// <param name="layer">The layer of the neighbourhood.</param>
            /// <returns>Best nodes selected from the candidates.</returns>
            internal abstract List<int> SelectBestForConnecting(List<int> candidatesIds, TravelingCosts<int, TDistance> travelingCosts, int layer);

            /// <summary>
            /// Get maximum allowed connections for the given level.
            /// </summary>
            /// <remarks>
            /// Article: Section 4.1:
            /// "Selection of the Mmax0 (the maximum number of connections that an element can have in the zero layer) also
            /// has a strong influence on the search performance, especially in case of high quality(high recall) search.
            /// Simulations show that setting Mmax0 to M(this corresponds to kNN graphs on each layer if the neighbors
            /// selection heuristic is not used) leads to a very strong performance penalty at high recall.
            /// Simulations also suggest that 2∙M is a good choice for Mmax0;
            /// setting the parameter higher leads to performance degradation and excessive memory usage."
            /// </remarks>
            /// <param name="layer">The level of the layer.</param>
            /// <returns>The maximum number of connections.</returns>
            internal int GetM(int layer)
            {
                return layer == 0 ? 2 * GraphCore.Parameters.M : GraphCore.Parameters.M;
            }

            /// <summary>
            /// Tries to connect the node with the new neighbour.
            /// </summary>
            /// <param name="node">The node to add neighbour to.</param>
            /// <param name="neighbour">The new neighbour.</param>
            /// <param name="layer">The layer to add neighbour to.</param>
            internal void Connect(Node node, Node neighbour, int layer)
            {
                var nodeLayer = node[layer];
                nodeLayer.Add(neighbour.Id);
                if (nodeLayer.Count > GetM(layer))
                {
                    var travelingCosts = new TravelingCosts<int, TDistance>(NodeDistance, node.Id);
                    node[layer] = SelectBestForConnecting(nodeLayer, travelingCosts, layer);
                }
            }
        }

    }
}
