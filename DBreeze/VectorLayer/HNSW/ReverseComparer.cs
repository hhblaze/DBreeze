/*
  Copyright https://github.com/wlou/HNSW.Net MIT License  
  It's a free software for those who think that it should be free.
*/
#if NET6FUNC || NET472

namespace DBreeze.HNSW
{
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Reverses the order of the nested comparer.
    /// </summary>
    /// <typeparam name="T">The types of items to comapre.</typeparam>
    internal class ReverseComparer<T> : IComparer<T>
    {
        /// <summary>
        /// Gets a default sort order comparer for the type specified by the generic argument.
        /// </summary>
        public static readonly ReverseComparer<T> Default = new ReverseComparer<T>(Comparer<T>.Default);

        private readonly IComparer<T> comparer = Default;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReverseComparer{T}"/> class.
        /// </summary>
        /// <param name="comparer">The comparer to invert.</param>
        public ReverseComparer(IComparer<T> comparer)
        {
            this.comparer = comparer;
        }

        /// <inheritdoc />
        public int Compare(T x, T y)
        {
            return this.comparer.Compare(y, x);
        }
    }

    /// <summary>
    /// Extension methods to shortcut <see cref="ReverseComparer{T}"/> usage.
    /// </summary>
    [SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "By Design")]
    [SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1204:Static elements must appear before instance elements", Justification = "By Design")]
    internal static class ReverseComparerExtensions
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