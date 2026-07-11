/*
  Copyright https://github.com/wlou/HNSW.Net MIT License  
  It's a free software for those who think that it should be free.
*/

#if NET6FUNC || NET472

namespace DBreeze.HNSW
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Binary heap wrapper around the <see cref="IList{T}"/>
    /// It's a max-heap implementation i.e. the maximum element is always on top.
    /// But the order of elements can be customized by providing <see cref="IComparer{T}"/> instance.
    /// </summary>
    /// <typeparam name="T">The type of the items in the source list.</typeparam>
    internal sealed class BinaryHeap<T>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BinaryHeap{T}"/> class.
        /// </summary>
        /// <param name="buffer">The buffer to store heap items.</param>
        public BinaryHeap(List<T> buffer)
            : this(buffer, Comparer<T>.Default)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BinaryHeap{T}"/> class.
        /// </summary>
        /// <param name="buffer">The buffer to store heap items.</param>
        /// <param name="comparer">The comparer which defines order of items.</param>
        public BinaryHeap(List<T> buffer, IComparer<T> comparer)
        {
            this.Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            this.Comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));

            // Floyd's bottom-up heap construction is O(n), unlike inserting every
            // existing item independently, which is O(n log n).
            for (int i = (this.Buffer.Count >> 1) - 1; i >= 0; --i)
            {
                this.SiftDown(i);
            }
        }

        /// <summary>
        /// Gets the heap comparer.
        /// </summary>
        public IComparer<T> Comparer { get; }

        /// <summary>
        /// Gets the buffer of the heap.
        /// </summary>
        public List<T> Buffer { get; }

        /// <summary>
        /// Gets the number of items in the heap.
        /// </summary>
        public int Count => this.Buffer.Count;

        /// <summary>
        /// Gets the maximum item without removing it.
        /// </summary>
        /// <returns>The maximum item.</returns>
        public T Peek()
        {
            if (this.Buffer.Count == 0)
            {
                throw new InvalidOperationException("Heap is empty");
            }

            return this.Buffer[0];
        }

        /// <summary>
        /// Pushes item to the heap.
        /// </summary>
        /// <param name="item">The item to push.</param>
        public void Push(T item)
        {
            this.Buffer.Add(item);
            this.SiftUp(this.Buffer.Count - 1);
        }

        /// <summary>
        /// Pops the item from the heap.
        /// </summary>
        /// <returns>The popped item.</returns>
        public T Pop()
        {
            int lastIndex = this.Buffer.Count - 1;
            if (lastIndex < 0)
            {
                throw new InvalidOperationException("Heap is empty");
            }

            T result = this.Buffer[0];
            if (lastIndex == 0)
            {
                this.Buffer.RemoveAt(0);
                return result;
            }

            T replacement = this.Buffer[lastIndex];
            this.Buffer.RemoveAt(lastIndex);
            this.Buffer[0] = replacement;
            this.SiftDown(0);
            return result;
        }

        /// <summary>
        /// Restores the heap property starting from i'th position down to the bottom
        /// given that the downstream items fulfill the rule.
        /// </summary>
        /// <param name="i">The position of item where heap property is violated.</param>
        private void SiftDown(int i)
        {
            Span<T> buffer = CollectionsMarshal.AsSpan(this.Buffer);
            int count = buffer.Length;

            T item = buffer[i];
            int firstLeaf = count >> 1;

            while (i < firstLeaf)
            {
                int child = (i << 1) + 1;
                int right = child + 1;
                if (right < count && this.Comparer.Compare(buffer[child], buffer[right]) < 0)
                {
                    child = right;
                }

                if (this.Comparer.Compare(buffer[child], item) <= 0)
                {
                    break;
                }

                buffer[i] = buffer[child];
                i = child;
            }

            buffer[i] = item;
        }

        /// <summary>
        /// Restores the heap property starting from i'th position up to the head
        /// given that the upstream items fulfill the rule.
        /// </summary>
        /// <param name="i">The position of item where heap property is violated.</param>
        private void SiftUp(int i)
        {
            Span<T> buffer = CollectionsMarshal.AsSpan(this.Buffer);

            T item = buffer[i];

            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (this.Comparer.Compare(item, buffer[parent]) <= 0)
                {
                    break;
                }

                buffer[i] = buffer[parent];
                i = parent;
            }

            buffer[i] = item;
        }
    }
}
#endif
