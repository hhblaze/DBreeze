// <copyright file="SmallWorld.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace DBreeze.HNSW
{
    /// <summary>
    /// Type of heuristic to select best neighbours for a node.
    /// </summary>
    public enum NeighbourSelectionHeuristic
    {
        /// <summary>
        /// Marker for the Algorithm 3 (SELECT-NEIGHBORS-SIMPLE) from the article. Implemented in <see cref="Algorithms.Algorithm3{TItem, TDistance}"/>
        /// </summary>
        SelectSimple,

        /// <summary>
        /// Marker for the Algorithm 4 (SELECT-NEIGHBORS-HEURISTIC) from the article. Implemented in <see cref="Algorithms.Algorithm4{TItem, TDistance}"/>
        /// </summary>
        SelectHeuristic
    }
}
