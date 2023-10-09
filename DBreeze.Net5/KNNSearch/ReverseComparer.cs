// <copyright file="ReverseComparer.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// </copyright>

#if KNNSearch
namespace DBreeze.HNSW
{
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Reverses the order of the nested comparer.
    /// </summary>
    /// <typeparam name="T">The types of items to comapre.</typeparam>
    public class ReverseComparer<T> : IComparer<T>
    {
        private readonly IComparer<T> Comparer;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReverseComparer{T}"/> class.
        /// </summary>
        /// <param name="comparer">The comparer to invert.</param>
        public ReverseComparer(IComparer<T> comparer)
        {
            Comparer = comparer;
        }

        /// <inheritdoc />
        public int Compare(T x, T y)
        {
            return Comparer.Compare(y, x);
        }
    }

    /// <summary>
    /// Extension methods to shortcut <see cref="ReverseComparer{T}"/> usage.
    /// </summary>
    [SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "By Design")]
    [SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1204:Static elements must appear before instance elements", Justification = "By Design")]
    public static class ReverseComparerExtensions
    {
        /// <summary>
        /// Creates new <see cref="ReverseComparer{T}"/> wrapper for the given comparer.
        /// </summary>
        /// <typeparam name="T">The types of items to comapre.</typeparam>
        /// <param name="comparer">The source comparer.</param>
        /// <returns>The inverted to source comparer.</returns>
        public static ReverseComparer<T> Reverse<T>(this IComparer<T> comparer)
        {
            return new ReverseComparer<T>(comparer);
        }
    }
}
#endif