// <copyright file="BinaryHeap.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// </copyright>

#if NET6FUNC
namespace DBreeze.HNSW
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Binary heap wrapper around the <see cref="IList{T}"/> It's a max-heap implementation i.e. the maximum element is always on top. But the order of elements can be customized by providing <see cref="IComparer{T}"/> instance.
    /// </summary>
    /// <typeparam name="T">The type of the items in the source list.</typeparam>
    [SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "By design")]
    internal struct BinaryHeap<T>
    {
        internal IComparer<T> Comparer;
        internal List<T> Buffer;
        internal bool Any => Buffer.Count > 0;
        internal BinaryHeap(List<T> buffer) : this(buffer, Comparer<T>.Default) { }
        internal BinaryHeap(List<T> buffer, IComparer<T> comparer)
        {
            Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            Comparer = comparer;
            for (int i = 1; i < Buffer.Count; ++i) { SiftUp(i); }
        }

        internal void Push(T item)
        {
            Buffer.Add(item);
            SiftUp(Buffer.Count - 1);
        }

        internal T Pop()
        {
            if (Buffer.Count > 0)
            {
                var result = Buffer[0];

                Buffer[0] = Buffer[Buffer.Count - 1];
                Buffer.RemoveAt(Buffer.Count - 1);
                SiftDown(0);

                return result;
            }

            throw new InvalidOperationException("Heap is empty");
        }

        /// <summary>
        /// Restores the heap property starting from i'th position down to the bottom given that the downstream items fulfill the rule.
        /// </summary>
        /// <param name="i">The position of item where heap property is violated.</param>
        private void SiftDown(int i)
        {
            while (i < Buffer.Count)
            {
                int l = (i << 1) + 1;
                int r = l + 1;
                if (l >= Buffer.Count)
                {
                    break;
                }

                int m = r < Buffer.Count && Comparer.Compare(Buffer[l], Buffer[r]) < 0 ? r : l;
                if (Comparer.Compare(Buffer[m], Buffer[i]) <= 0)
                {
                    break;
                }

                Swap(i, m);
                i = m;
            }
        }

        /// <summary>
        /// Restores the heap property starting from i'th position up to the head given that the upstream items fulfill the rule.
        /// </summary>
        /// <param name="i">The position of item where heap property is violated.</param>
        private void SiftUp(int i)
        {
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (Comparer.Compare(Buffer[i], Buffer[p]) <= 0)
                {
                    break;
                }

                Swap(i, p);
                i = p;
            }
        }

        private void Swap(int i, int j)
        {
            var temp = Buffer[i];
            Buffer[i] = Buffer[j];
            Buffer[j] = temp;
        }
    }
}
#endif