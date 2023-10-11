// <copyright file="TravelingCosts.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// </copyright>

#if NET6FUNC
namespace DBreeze.HNSW
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Implementation of distance calculation from an arbitrary point to the given destination.
    /// </summary>
    /// <typeparam name="TItem">Type of the points.</typeparam>
    /// <typeparam name="TDistance">Type of the distance.</typeparam>
    internal class TravelingCosts<TItem, TDistance> : IComparer<TItem>
    {
        private static readonly Comparer<TDistance> DistanceComparer = Comparer<TDistance>.Default;

        private readonly Func<TItem, TItem, TDistance> Distance;

        public TravelingCosts(Func<TItem, TItem, TDistance> distance, TItem destination)
        {
            Distance = distance;
            Destination = destination;
        }

        public TItem Destination { get; }

        public TDistance From(TItem departure)
        {
            return Distance(departure, Destination);
        }

        /// <summary>
        /// Compares 2 points by the distance from the destination.
        /// </summary>
        /// <param name="x">Left point.</param>
        /// <param name="y">Right point.</param>
        /// <returns>
        /// -1 if x is closer to the destination than y;
        /// 0 if x and y are equally far from the destination;
        /// 1 if x is farther from the destination than y.
        /// </returns>
        public int Compare(TItem x, TItem y)
        {
            var fromX = From(x);
            var fromY = From(y);
            return DistanceComparer.Compare(fromX, fromY);
        }
    }
}
#endif