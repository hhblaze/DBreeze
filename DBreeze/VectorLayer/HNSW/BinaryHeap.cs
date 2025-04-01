/*
  Copyright https://github.com/wlou/HNSW.Net MIT License  
  It's a free software for those who think that it should be free.
*/

#if NET6FUNC || NET472

namespace DBreeze.HNSW
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Binary heap wrapper around the <see cref="IList{T}"/>
    /// It's a max-heap implementation i.e. the maximum element is always on top.
    /// But the order of elements can be customized by providing <see cref="IComparer{T}"/> instance.
    /// </summary>
    /// <typeparam name="T">The type of the items in the source list.</typeparam>
    internal class BinaryHeap<T>
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
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            this.Buffer = buffer;
            this.Comparer = comparer;
            for (int i = 1; i < this.Buffer.Count; ++i)
            {
                this.SiftUp(i);
            }
        }

        /// <summary>
        /// Gets the heap comparer.
        /// </summary>
        public IComparer<T> Comparer { get; private set; }

        /// <summary>
        /// Gets the buffer of the heap.
        /// </summary>
        public List<T> Buffer { get; private set; }

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
            if (this.Buffer.Any())
            {
                var result = this.Buffer.First();

                this.Buffer[0] = this.Buffer.Last();
                this.Buffer.RemoveAt(this.Buffer.Count - 1);
                this.SiftDown(0);

                return result;
            }

            throw new InvalidOperationException("Heap is empty");
        }

        /// <summary>
        /// Restores the heap property starting from i'th position down to the bottom
        /// given that the downstream items fulfill the rule.
        /// </summary>
        /// <param name="i">The position of item where heap property is violated.</param>
        private void SiftDown(int i)
        {
            while (i < this.Buffer.Count)
            {
                int l = (2 * i) + 1;
                int r = l + 1;
                if (l >= this.Buffer.Count)
                {
                    break;
                }

                int m = r < this.Buffer.Count && this.Comparer.Compare(this.Buffer[l], this.Buffer[r]) < 0 ? r : l;
                if (this.Comparer.Compare(this.Buffer[m], this.Buffer[i]) <= 0)
                {
                    break;
                }

                this.Swap(i, m);
                i = m;
            }
        }

        /// <summary>
        /// Restores the heap property starting from i'th position up to the head
        /// given that the upstream items fulfill the rule.
        /// </summary>
        /// <param name="i">The position of item where heap property is violated.</param>
        private void SiftUp(int i)
        {
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (this.Comparer.Compare(this.Buffer[i], this.Buffer[p]) <= 0)
                {
                    break;
                }

                this.Swap(i, p);
                i = p;
            }
        }

        /// <summary>
        /// Swaps items with the specified indicies.
        /// </summary>
        /// <param name="i">The first index.</param>
        /// <param name="j">The second index.</param>
        private void Swap(int i, int j)
        {
            var temp = this.Buffer[i];
            this.Buffer[i] = this.Buffer[j];
            this.Buffer[j] = temp;
        }
    }
}
#endif