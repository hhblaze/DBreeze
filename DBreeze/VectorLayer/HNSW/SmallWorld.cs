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
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Linq;
    using System.Runtime.Serialization.Formatters.Binary;

    /// <summary>
    /// <see href="https://arxiv.org/abs/1603.09320">Hierarchical Navigable Small World Graphs</see>.
    /// </summary>
    /// <typeparam name="TItem">The type of items to connect into small world.</typeparam>
    /// <typeparam name="TDistance">The type of distance between items (expect any numeric type: float, double, decimal, int, ...).</typeparam>
    internal partial class SmallWorld<TItem, TDistance>
        //where TDistance : IComparable<TDistance>
    {
        /// <summary>
        /// The distance function in the items space.
        /// </summary>
        private readonly Func<TItem, TItem, TDistance> distance;

        /// <summary>
        /// The hierarchical small world graph instance.
        /// </summary>
        private Graph graph;

        /// <summary>
        /// Type of heuristic to select best neighbours for a node.
        /// </summary>
        public enum NeighbourSelectionHeuristic
        {
            /// <summary>
            /// Marker for the Algorithm 3 (SELECT-NEIGHBORS-SIMPLE) from the article.
            /// Implemented in <see cref="SmallWorld{TItem, TDistance}.NodeAlg3"/>
            /// </summary>
            SelectSimple,

            /// <summary>
            /// Marker for the Algorithm 4 (SELECT-NEIGHBORS-HEURISTIC) from the article.
            /// Implemented in <see cref="SmallWorld{TItem, TDistance}.NodeAlg4"/>
            /// </summary>
            SelectHeuristic,
        }


        /// <summary>
        /// Parameters of the algorithm.
        /// </summary>
        [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "By Design")]
        [Serializable]
        public class Parameters
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="Parameters"/> class.
            /// </summary>
            public Parameters()
            {
                this.M = 10;
                this.LevelLambda = 1 / Math.Log(this.M);
                this.NeighbourHeuristic = NeighbourSelectionHeuristic.SelectSimple;
                this.ConstructionPruning = 200;
                this.ExpandBestSelection = false;
                this.KeepPrunedConnections = true;
            }

            /// <summary>
            /// Gets or sets the parameter which defines the maximum number of neighbors in the zero and above-zero layers.
            /// The maximum number of neighbors for the zero layer is 2 * M.
            /// The maximum number of neighbors for higher layers is M.
            /// </summary>
            public int M { get; set; }

            /// <summary>
            /// Gets or sets the max level decay parameter.
            /// https://en.wikipedia.org/wiki/Exponential_distribution
            /// See 'mL' parameter in the HNSW article.
            /// </summary>
            public double LevelLambda { get; set; }

            /// <summary>
            /// Gets or sets parameter which specifies the type of heuristic to use for best neighbours selection.
            /// </summary>
            public NeighbourSelectionHeuristic NeighbourHeuristic { get; set; }

            /// <summary>
            /// Gets or sets the number of candidates to consider as neighbousr for a given node at the graph construction phase.
            /// See 'efConstruction' parameter in the article.
            /// </summary>
            public int ConstructionPruning { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether to expand candidates if <see cref="NeighbourSelectionHeuristic.SelectHeuristic"/> is used.
            /// See 'extendCandidates' parameter in the article.
            /// </summary>
            public bool ExpandBestSelection { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether to keep pruned candidates if <see cref="NeighbourSelectionHeuristic.SelectHeuristic"/> is used.
            /// See 'keepPrunedConnections' parameter in the article.
            /// </summary>
            public bool KeepPrunedConnections { get; set; }

            [NonSerialized]
            internal IStorage<TItem, TDistance> Storage;            
        }

        /// <summary>
        /// Representation of knn search result.
        /// </summary>
        [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "By Design")]
        public class KNNSearchResult
        {
            /// <summary>
            /// Gets or sets the id of the item = rank of the item in source collection.
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// Gets or sets the item itself.
            /// </summary>
            public TItem Item { get; set; }

            /// <summary>
            /// Gets or sets the distance between the item and the knn search query.
            /// </summary>
            public TDistance Distance { get; set; }

            public long ExternalId { get; set; }
        }
    }
}
#endif