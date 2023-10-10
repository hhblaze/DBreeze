// <copyright file="EventSources.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// </copyright>

#if KNNSearch
namespace DBreeze.HNSW
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Diagnostics.Tracing;
    using System.Runtime.InteropServices;

    internal static class EventSources
    {
        /// <summary>
        /// Writes specific metric if the source is enabled.
        /// </summary>
        /// <param name="source">The event source to check.</param>
        /// <param name="counter">The counter to write metric.</param>
        /// <param name="value">The value to write.</param>
        internal static void WriteMetricIfEnabled(EventSource source, EventCounter counter, float value)
        {
            if (source.IsEnabled())
            {
                counter.WriteMetric(value);
            }
        }

        /// <summary>
        /// Source of events occuring at graph construction phase.
        /// </summary>
        [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "By Design")]
        [EventSource(Name = "HNSW.Net.Graph.Build")]
        [ComVisible(false)]
        public class GraphBuildEventSource : EventSource
        {
            /// <summary>
            /// The singleton instance of the source.
            /// </summary>
            public static readonly GraphBuildEventSource Instance = new GraphBuildEventSource();

            /// <summary>
            /// Initializes a new instance of the <see cref="GraphBuildEventSource"/> class.
            /// </summary>
            private GraphBuildEventSource() : base(EventSourceSettings.EtwSelfDescribingEventFormat)
            {
                var coreGetDistanceCacheHitRate = new EventCounter("GetDistance.CacheHitRate", this);
                CoreGetDistanceCacheHitRateReporter = (float value) => WriteMetricIfEnabled(this, coreGetDistanceCacheHitRate, value);

                var graphInsertNodeLatency = new EventCounter("InsertNode.Latency", this);
                GraphInsertNodeLatencyReporter = (float value) => WriteMetricIfEnabled(this, graphInsertNodeLatency, value);
            }

            /// <summary>
            /// Gets the delegate to report the hit rate of the distance cache.
            /// <see cref="Graph{TItem, TDistance}.Core.GetDistance(int, int)"/>
            /// </summary>
            internal Action<float> CoreGetDistanceCacheHitRateReporter { get; }

            /// <summary>
            /// Gets the delegate to report the node insertion latency.
            /// <see cref="Graph{TItem, TDistance}.Build(System.Collections.Generic.IReadOnlyList{TItem}, System.Random)"/>
            /// </summary>
            internal Action<float> GraphInsertNodeLatencyReporter { get; }
        }

        /// <summary>
        /// Source of events occuring at graph construction phase.
        /// </summary>
        [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "By Design")]
        [EventSource(Name = "HNSW.Net.Graph.Search")]
        [ComVisible(false)]
        public class GraphSearchEventSource : EventSource
        {
            /// <summary>
            /// The singleton instance of the source.
            /// </summary>
            public static readonly GraphSearchEventSource Instance = new GraphSearchEventSource();

            /// <summary>
            /// Initializes a new instance of the <see cref="GraphSearchEventSource"/> class.
            /// </summary>
            private GraphSearchEventSource() : base(EventSourceSettings.EtwSelfDescribingEventFormat)
            {
                var graphKNearestLatency = new EventCounter("KNearest.Latency", this);
                GraphKNearestLatencyReporter = (float value) => WriteMetricIfEnabled(this, graphKNearestLatency, value);

                var graphKNearestVisitedNodes = new EventCounter("KNearest.VisitedNodes", this);
                GraphKNearestVisitedNodesReporter = (float value) => WriteMetricIfEnabled(this, graphKNearestVisitedNodes, value);
            }

            /// <summary>
            /// Gets the delegate to report <see cref="Graph{TItem, TDistance}.KNearest(TItem, int)" /> latency.
            /// </summary>
            internal Action<float> GraphKNearestLatencyReporter { get; }

            /// <summary>
            /// Gets the counter to report the number of expanded nodes at runtime.
            /// <see cref="Graph{TItem, TDistance}.KNearest(TItem, int)"/>
            /// </summary>
            internal Action<float> GraphKNearestVisitedNodesReporter { get; }
        }
    }
}
#endif