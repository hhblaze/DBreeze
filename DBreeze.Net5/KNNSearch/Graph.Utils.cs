// <copyright file="Graph.Utils.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// </copyright>

#if KNNSearch
namespace DBreeze.HNSW
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    internal partial class Graph<TItem, TDistance>
    {
        /// <summary>
        /// Runs breadth first search.
        /// </summary>
        /// <param name="core">The graph core.</param>
        /// <param name="entryPoint">The entry point.</param>
        /// <param name="layer">The layer of the graph where to run BFS.</param>
        /// <param name="visitAction">The action to perform on each node.</param>
        internal static void BFS(Core core, Node entryPoint, int layer, Action<Node> visitAction)
        {
            var visitedIds = new HashSet<int>();
            var expansionQueue = new Queue<int>(new[] { entryPoint.Id });

            while (expansionQueue.Any())
            {
                var currentNode = core.Storage.Nodes[expansionQueue.Dequeue()];
                if (!visitedIds.Contains(currentNode.Id))
                {
                    visitAction(currentNode);
                    visitedIds.Add(currentNode.Id);
                    foreach (var neighbourId in currentNode[layer])
                    {
                        expansionQueue.Enqueue(neighbourId);
                    }
                }
            }
        }

        internal class VisitedBitSet
        {
            private int[] Buffer;

            internal VisitedBitSet(int nodesCount)
            {
                Buffer = new int[(nodesCount >> 5) + 1];
            }

            internal bool Contains(int nodeId)
            {
                int carrier = Buffer[nodeId >> 5];
                return ((1 << (nodeId & 31)) & carrier) != 0;
            }

            internal void Add(int nodeId)
            {
                int mask = 1 << (nodeId & 31);
                Buffer[nodeId >> 5] |= mask;
            }

            internal void Clear()
            {
                Array.Clear(Buffer, 0, Buffer.Length);
            }
        }
    }
}
#endif