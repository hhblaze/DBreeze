#if NET472 || NETSTANDARD2_1 || NETCOREAPP2_0 || NETCOREAPP
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBreeze.Utils
{
    public class PriorityQueue<TElement, TPriority>
    {
        private readonly List<PriorityQueueNode> _nodes;
        private readonly IComparer<TPriority> _comparer;

        public PriorityQueue()
            : this(Comparer<TPriority>.Default)
        {
        }

        public PriorityQueue(IComparer<TPriority> comparer)
            : this(4, comparer)
        {
        }

        public PriorityQueue(int initialCapacity)
            : this(initialCapacity, Comparer<TPriority>.Default)
        {
        }

        public PriorityQueue(int initialCapacity, IComparer<TPriority> comparer)
        {
            _comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
            _nodes = new List<PriorityQueueNode>(initialCapacity);
        }

        public int Count => _nodes.Count;

        public void Enqueue(TElement element, TPriority priority)
        {
            _nodes.Add(new PriorityQueueNode(element, priority));
            BubbleUp(_nodes.Count - 1);
        }

        public TElement Dequeue()
        {
            if (_nodes.Count == 0)
                throw new InvalidOperationException("Queue is empty.");

            var result = _nodes[0].Element;
            MoveLastNodeToRoot();
            SinkDown(0);
            return result;
        }

        public TElement Peek()
        {
            if (_nodes.Count == 0)
                throw new InvalidOperationException("Queue is empty.");
            return _nodes[0].Element;
        }

        public void Clear()
        {
            _nodes.Clear();
        }

        private void BubbleUp(int index)
        {
            while (index > 0)
            {
                int parentIndex = (index - 1) / 2;
                if (_comparer.Compare(_nodes[index].Priority, _nodes[parentIndex].Priority) >= 0)
                    break;

                Swap(index, parentIndex);
                index = parentIndex;
            }
        }

        private void SinkDown(int index)
        {
            int lastIndex = _nodes.Count - 1;
            while (true)
            {
                int leftChildIndex = 2 * index + 1;
                int rightChildIndex = 2 * index + 2;
                int smallestIndex = index;

                if (leftChildIndex <= lastIndex &&
                    _comparer.Compare(_nodes[leftChildIndex].Priority, _nodes[smallestIndex].Priority) < 0)
                {
                    smallestIndex = leftChildIndex;
                }

                if (rightChildIndex <= lastIndex &&
                    _comparer.Compare(_nodes[rightChildIndex].Priority, _nodes[smallestIndex].Priority) < 0)
                {
                    smallestIndex = rightChildIndex;
                }

                if (smallestIndex == index)
                    break;

                Swap(index, smallestIndex);
                index = smallestIndex;
            }
        }

        private void MoveLastNodeToRoot()
        {
            int lastIndex = _nodes.Count - 1;
            _nodes[0] = _nodes[lastIndex];
            _nodes.RemoveAt(lastIndex);
        }

        private void Swap(int indexA, int indexB)
        {
            var temp = _nodes[indexA];
            _nodes[indexA] = _nodes[indexB];
            _nodes[indexB] = temp;
        }

        private struct PriorityQueueNode
        {
            public TElement Element { get; }
            public TPriority Priority { get; }

            public PriorityQueueNode(TElement element, TPriority priority)
            {
                Element = element;
                Priority = priority;
            }
        }
    }

}
#endif
