#if NET6FUNC || NET472
namespace DBreeze.HNSW
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Runtime.Serialization.Formatters.Binary;
    /// <summary>
    /// <see href="https://arxiv.org/abs/1603.09320">Hierarchical Navigable Small World Graphs</see>.
    /// </summary>
    /// <typeparam name="TItem">The type of items to connect into small world.</typeparam>
    /// <typeparam name="TDistance">The type of distance between items (expect any numeric type: float, double, decimal, int, ...).</typeparam>
    internal partial class SmallWorld<TItem, TDistance>
        where TDistance : IComparable<TDistance>
    {
        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TDistance"></typeparam>
        internal class DistanceCache<TDistance> 
        {
            /// <summary>
            /// Gets or sets Mkd.
            /// </summary>
            Dictionary<int, Dictionary<int, TDistance>> Mkd { get; set; } = new Dictionary<int, Dictionary<int, TDistance>>(); //much faster, than one long dictionary

            //public Dictionary<long, TDistance> Mkd { get; set; } = new();

            /// <summary>
            /// Gets or Sets Count.
            /// </summary>
            public int Count { get; private set; } = 0;

            public void Clear()
            {
                Count = 0;
                this.Mkd.Clear();
            }

            ///// <summary>
            ///// destination is a primary key.
            ///// </summary>
            ///// <param name="destinationId">destination.</param>
            ///// <param name="departureId">departure.</param>
            ///// <param name="distance">distance.</param>
            //public void Add(int destinationId, int departureId, TDistance distance)
            //{
            //    var key = ((long)destinationId << 32) | departureId;
            //    //var key = CombineIntsToLong(destinationId, departureId);
            //    Mkd[key] = distance;

            //    this.Count++;
            //}

            ///// <summary>
            ///// TryGetValue.
            ///// </summary>
            ///// <param name="destinationId">destination.</param>
            ///// <param name="departureId">departure.</param>
            ///// <param name="result">result.</param>
            ///// <returns>boolean value.</returns>
            //public bool TryGetValue(int destinationId, int departureId, out TDistance result)
            //{
            //    var key = ((long)destinationId << 32) | departureId;
            //    //var key = CombineIntsToLong(destinationId, departureId);

            //    if (this.Mkd.TryGetValue(key, out result))
            //        return true;

            //    result = default;
            //    return false;
            //}



            public long Key = 0;

            public void Add(int destinationId, int departureId, TDistance distance)
            {             

                if (!this.Mkd.TryGetValue(destinationId, out var inner))
                {
                    inner = new Dictionary<int, TDistance>();
                    this.Mkd[destinationId] = inner;
                }

                inner[departureId] = distance;


                this.Count++;
            }

            public bool TryGetValue(int destinationId, int departureId, out TDistance result)
            {
                if (this.Mkd.TryGetValue(destinationId, out var inner))
                {
                    return inner.TryGetValue(departureId, out result);
                }

                result = default;
                return false;
            }

            //[MethodImpl(MethodImplOptions.AggressiveInlining)]
            //public static long CombineIntsToLong(int high, int low)
            //{
            //    // Method 1: Using bitwise operations (fastest on most systems)
            //    // return ((long)high << 32) | (uint)low;  // Cast low to uint to avoid sign extension issues
            //    return ((long)high << 32) | low;  // Cast low to uint to avoid sign extension issues

            //    // Method 2: Using Unsafe code (Potentially even faster, but requires unsafe context and can be platform-dependent)
            //    //unsafe
            //    //{
            //    //    long result;
            //    //    int* ptr = (int*)&result;
            //    //    ptr[0] = low;   //Little endian
            //    //    ptr[1] = high;
            //    //    return result;
            //    //}


            //}

            //[MethodImpl(MethodImplOptions.AggressiveInlining)]
            //public static (int high, int low) SplitLongToInts(long value)
            //{
            //    // Method 1: Using bitwise operations
            //    int low = (int)value;           // The low 32 bits are just a cast
            //    int high = (int)(value >> 32);  // Shift right by 32 to get the high 32 bits

            //    return (high, low);

            //    // Method 2: Using unsafe code
            //    // unsafe
            //    // {
            //    //     int* ptr = (int*)&value;
            //    //     return (ptr[1], ptr[0]); // Little Endian
            //    // }
            //}


            ///// <summary>
            ///// destination is a primary key.
            ///// </summary>
            ///// <param name="destination">destination.</param>
            ///// <param name="departure">departure.</param>
            ///// <param name="distance">distance.</param>
            //public void Add(Node destination, Node departure, TDistance distance)
            //{
            //    if (!this.Mkd.TryGetValue(destination.Id, out var inner))
            //    {
            //        inner = new Dictionary<int, TDistance>();
            //        this.Mkd[destination.Id] = inner;
            //    }

            //    inner[departure.Id] = distance;
            //    this.Count++;
            //}

            ///// <summary>
            ///// TryGetValue.
            ///// </summary>
            ///// <param name="destination">destination.</param>
            ///// <param name="departure">departure.</param>
            ///// <param name="result">result.</param>
            ///// <returns>boolean value.</returns>
            //public bool TryGetValue(Node destination, Node departure, out TDistance result)
            //{
            //    if (this.Mkd.TryGetValue(destination.Id, out var inner))
            //    {
            //        return inner.TryGetValue(departure.Id, out result);
            //    }

            //    result = default;
            //    return false;
            //}


        }

        /// <summary>
        /// 
        /// </summary>
        internal class NodeCache
        {
            Graph _graph;
            public NodeCache(Graph graph)
            {
                _graph = graph;
            }

            Dictionary<int, Node> _nodes=new Dictionary<int, Node>();

            internal Node GetNode(int nodeId)
            {                
                if(_nodes.TryGetValue(nodeId, out var node))
                    return node;

                var dbnode = this._graph.Parameters.Storage.GetDBNode(this._graph._bucket.BucketId, nodeId);

                node = this._graph.NewNode(dbnode.Id, dbnode.ExternalId, default(TItem), dbnode.MaxLevel, true);
                node.Connections = dbnode.Connections;

                _nodes.Add(node.Id, node);  

                return node;
            }

            internal void AddNode(Node node)
            {
                _nodes[node.Id] = node;
            }

            /// <summary>
            /// 
            /// </summary>
            /// <returns>true if something was changed</returns>
            public bool Flush()
            {
                if (_nodes.Count > 0)
                {
                    return this._graph.Parameters.Storage.FlushNodes(_graph._bucket.BucketId, _nodes);
                }
                return false;
            }

            public void Clear()
            {
                _nodes.Clear();
            }
        }
        
    }
}
#endif