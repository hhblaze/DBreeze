using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DBreeze.LianaTrie
{
    /// <summary>
    /// Process-local cache of immutable committed generation-node images.  It is deliberately
    /// internal: cache policy must not become part of DBreeze's public or persisted contract.
    /// </summary>
    internal static class CommittedReadNodeCacheRegistry
    {
        private static readonly ConditionalWeakTable<DBreezeConfiguration, CommittedReadNodeCacheManager>
            Managers = new ConditionalWeakTable<DBreezeConfiguration, CommittedReadNodeCacheManager>();
        private const int CounterStripes = 64;
        private const int CounterStride = 8; // one 64-byte cache line per stripe
        private static readonly long[] Hits = new long[CounterStripes * CounterStride];
        private static readonly long[] Misses = new long[CounterStripes * CounterStride];
        private static long _retainedBytes;

        internal static CommittedReadNodeTableCache Attach(LTrie tree)
        {
            if (tree == null ||
                tree.Storage.TrieSettings.AlternativeTableStorageType != DBreezeConfiguration.eStorage.DISK)
                return null;

            DBreezeConfiguration configuration = tree.Storage.DbreezeConfiguration;
            return configuration == null ? null : Managers.GetValue(configuration,
                static _ => new CommittedReadNodeCacheManager()).CreateTableCache(tree);
        }

        internal static void RecordHit() =>
            Interlocked.Increment(ref Hits[(Environment.CurrentManagedThreadId &
                (CounterStripes - 1)) * CounterStride]);
        internal static void RecordMiss() =>
            Interlocked.Increment(ref Misses[(Environment.CurrentManagedThreadId &
                (CounterStripes - 1)) * CounterStride]);
        internal static void ChangeRetained(long bytes) => Interlocked.Add(ref _retainedBytes, bytes);
        internal static long[] GetDiagnostics() => new[]
        {
            Sum(Hits),
            Sum(Misses),
            Interlocked.Read(ref _retainedBytes),
        };

        private static long Sum(long[] counters)
        {
            long sum = 0;
            for (int index = 0; index < counters.Length; index += CounterStride)
                sum += Volatile.Read(ref counters[index]);
            return sum;
        }
    }

    internal sealed class CommittedReadNodeCacheManager
    {
        internal const long ConfigurationLimitBytes = 64L * 1024 * 1024;
        internal const long TableLimitBytes = 8L * 1024 * 1024;

        private readonly object _sync = new object();
        private readonly Queue<CommittedReadNodeCacheEntry> _globalOrder =
            new Queue<CommittedReadNodeCacheEntry>();
        private long _retainedBytes;

        internal CommittedReadNodeTableCache CreateTableCache(LTrie tree)
        {
            lock (_sync)
            {
                while (_retainedBytes + CommittedReadNodeTableCache.HotFrontBytes >
                    ConfigurationLimitBytes && _globalOrder.Count != 0)
                    EvictGlobalOldest();
                if (_retainedBytes + CommittedReadNodeTableCache.HotFrontBytes >
                    ConfigurationLimitBytes)
                    return null;

                _retainedBytes += CommittedReadNodeTableCache.HotFrontBytes;
                CommittedReadNodeCacheRegistry.ChangeRetained(
                    CommittedReadNodeTableCache.HotFrontBytes);
                return new CommittedReadNodeTableCache(this, tree);
            }
        }

        internal void Admit(CommittedReadNodeTableCache table, CommittedReadNodeCacheKey key,
            CommittedReadNode node)
        {
            lock (_sync)
            {
                if (table.IsDisposed || table.TryGetExisting(key, out _))
                    return;

                var entry = new CommittedReadNodeCacheEntry(table, key, node);
                if (!table.TryAdd(entry))
                    return;

                _globalOrder.Enqueue(entry);
                _retainedBytes += entry.Weight;
                CommittedReadNodeCacheRegistry.ChangeRetained(entry.Weight);
                table.Enqueue(entry);

                while (table.RetainedBytes > TableLimitBytes)
                    table.EvictOldest();
                while (_retainedBytes > ConfigurationLimitBytes)
                    EvictGlobalOldest();
            }
        }

        internal void AdvanceEpoch(CommittedReadNodeTableCache table, long epoch)
        {
            lock (_sync)
                table.AdvanceEpochUnderLock(epoch);
        }

        internal void DisposeTable(CommittedReadNodeTableCache table)
        {
            lock (_sync)
                table.DisposeUnderLock();
        }

        internal void Released(CommittedReadNodeCacheEntry entry)
        {
            _retainedBytes -= entry.Weight;
            CommittedReadNodeCacheRegistry.ChangeRetained(-entry.Weight);
        }

        internal void ReleaseTableFront()
        {
            _retainedBytes -= CommittedReadNodeTableCache.HotFrontBytes;
            CommittedReadNodeCacheRegistry.ChangeRetained(
                -CommittedReadNodeTableCache.HotFrontBytes);
        }

        private void EvictGlobalOldest()
        {
            while (_globalOrder.Count != 0)
            {
                CommittedReadNodeCacheEntry entry = _globalOrder.Dequeue();
                if (entry.Table.Remove(entry))
                    return;
            }
        }
    }

    internal sealed class CommittedReadNodeTableCache : IDisposable
    {
        private const int AdmissionSlots = 4096;
        private const int HotFrontSlots = 256;
        internal const int HotFrontBytes = HotFrontSlots * 8;

        private readonly CommittedReadNodeCacheManager _manager;
        private readonly LTrie _tree;
        private readonly ConcurrentDictionary<CommittedReadNodeCacheKey, CommittedReadNodeCacheEntry>
            _entries = new ConcurrentDictionary<CommittedReadNodeCacheKey, CommittedReadNodeCacheEntry>();
        private readonly Queue<CommittedReadNodeCacheEntry> _order =
            new Queue<CommittedReadNodeCacheEntry>();
        private readonly long[] _admission = new long[AdmissionSlots];
        private readonly CommittedReadNodeCacheEntry[] _hotFront =
            new CommittedReadNodeCacheEntry[HotFrontSlots];
        private long _latestEpoch = Int64.MinValue;
        private long _retainedBytes;
        private int _disposed;

        internal CommittedReadNodeTableCache(CommittedReadNodeCacheManager manager, LTrie tree)
        {
            _manager = manager;
            _tree = tree;
        }

        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;
        internal long RetainedBytes => _retainedBytes + HotFrontBytes;

        internal CommittedReadNode GetOrLoad(long epoch, ulong pointer)
        {
            if (IsDisposed)
                return CommittedReadNode.Load(_tree, pointer);

            long observedEpoch = Volatile.Read(ref _latestEpoch);
            if (epoch > observedEpoch)
                _manager.AdvanceEpoch(this, epoch);

            var key = new CommittedReadNodeCacheKey(epoch, pointer);
            int hotSlot = key.HotHash & (HotFrontSlots - 1);
            CommittedReadNodeCacheEntry cached = Volatile.Read(ref _hotFront[hotSlot]);
            if ((cached == null || Volatile.Read(ref cached.Removed) != 0 ||
                 !cached.Key.Equals(key)) &&
                _entries.TryGetValue(key, out cached))
                Volatile.Write(ref _hotFront[hotSlot], cached);

            if (cached != null && Volatile.Read(ref cached.Removed) == 0 &&
                cached.Key.Equals(key))
            {
                CommittedReadNodeCacheRegistry.RecordHit();
                return cached.Node;
            }

            CommittedReadNodeCacheRegistry.RecordMiss();
            CommittedReadNode loaded = CommittedReadNode.Load(_tree, pointer);

            // A fixed-size lock-free doorkeeper implements second-hit admission without retaining
            // a dictionary entry for one-pass traversals.
            long tag = key.AdmissionTag;
            int slot = (int)((ulong)tag & (AdmissionSlots - 1));
            long previous = Interlocked.Exchange(ref _admission[slot], tag);
            if (previous == tag && epoch >= Volatile.Read(ref _latestEpoch))
                _manager.Admit(this, key, loaded);

            return loaded;
        }

        internal bool TryGetExisting(CommittedReadNodeCacheKey key,
            out CommittedReadNodeCacheEntry entry) => _entries.TryGetValue(key, out entry);

        internal bool TryAdd(CommittedReadNodeCacheEntry entry)
        {
            if (IsDisposed || !_entries.TryAdd(entry.Key, entry))
                return false;
            _retainedBytes += entry.Weight;
            Volatile.Write(ref _hotFront[entry.Key.HotHash & (HotFrontSlots - 1)], entry);
            return true;
        }

        internal void Enqueue(CommittedReadNodeCacheEntry entry) => _order.Enqueue(entry);

        internal void EvictOldest()
        {
            while (_order.Count != 0)
            {
                if (Remove(_order.Dequeue()))
                    return;
            }
        }

        internal bool Remove(CommittedReadNodeCacheEntry entry)
        {
            if (Interlocked.Exchange(ref entry.Removed, 1) != 0)
                return false;
            if (!_entries.TryRemove(entry.Key, out CommittedReadNodeCacheEntry actual) ||
                !ReferenceEquals(actual, entry))
                return false;

            _retainedBytes -= entry.Weight;
            _manager.Released(entry);
            return true;
        }

        internal void AdvanceEpochUnderLock(long epoch)
        {
            if (epoch <= _latestEpoch)
                return;

            _latestEpoch = epoch;
            while (_order.Count != 0)
                Remove(_order.Dequeue());
            Array.Clear(_admission, 0, _admission.Length);
        }

        internal void DisposeUnderLock()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            while (_order.Count != 0)
                Remove(_order.Dequeue());
            _entries.Clear();
            Array.Clear(_hotFront, 0, _hotFront.Length);
            Array.Clear(_admission, 0, _admission.Length);
            _manager.ReleaseTableFront();
        }

        public void Dispose() => _manager.DisposeTable(this);
    }

    internal readonly struct CommittedReadNodeCacheKey : IEquatable<CommittedReadNodeCacheKey>
    {
        internal CommittedReadNodeCacheKey(long epoch, ulong pointer)
        {
            Epoch = epoch;
            Pointer = pointer;
        }

        internal long Epoch { get; }
        internal ulong Pointer { get; }
        internal long AdmissionTag
        {
            get
            {
                ulong mixed = Pointer ^ unchecked((ulong)Epoch * 11400714819323198485UL);
                mixed ^= mixed >> 33;
                mixed *= 0xff51afd7ed558ccdUL;
                mixed ^= mixed >> 33;
                long tag = unchecked((long)mixed);
                return tag == 0 ? 1 : tag;
            }
        }

        internal int HotHash
        {
            get
            {
                ulong mixed = Pointer ^ unchecked((ulong)Epoch * 0x9e3779b97f4a7c15UL);
                return unchecked((int)(mixed ^ (mixed >> 32)));
            }
        }

        public bool Equals(CommittedReadNodeCacheKey other) =>
            Epoch == other.Epoch && Pointer == other.Pointer;
        public override bool Equals(object obj) => obj is CommittedReadNodeCacheKey other && Equals(other);
        public override int GetHashCode() => HotHash;
    }

    internal sealed class CommittedReadNodeCacheEntry
    {
        internal CommittedReadNodeCacheEntry(CommittedReadNodeTableCache table,
            CommittedReadNodeCacheKey key, CommittedReadNode node)
        {
            Table = table;
            Key = key;
            Node = node;
            Weight = node.RetainedBytes + 96;
        }

        internal CommittedReadNodeTableCache Table { get; }
        internal CommittedReadNodeCacheKey Key { get; }
        internal CommittedReadNode Node { get; }
        internal int Weight { get; }
        internal int Removed;
    }

    /// <summary>Dense immutable node image. High bit marks a value link; zero means absent.</summary>
    internal sealed class CommittedReadNode
    {
        private const ulong ValueLinkMask = 1UL << 63;
        private readonly ulong[] _links;

        private CommittedReadNode(ulong[] links)
        {
            _links = links;
        }

        internal int RetainedBytes => 24 + (_links.Length * sizeof(ulong));

        internal static CommittedReadNode Load(LTrie tree, ulong pointer)
        {
            ushort pointerLength = tree.Storage.TrieSettings.POINTER_LENGTH;
            byte[] pointerBytes = PointerToBytes(pointer, pointerLength);
            int maximumLength = 2 + pointerLength + (256 * (pointerLength + 2));
            byte[] compact = tree.Cache.GenerationNodeRead(true, pointerBytes, maximumLength);
            var links = new ulong[257];
            if (compact == null || compact.Length == 0)
                return new CommittedReadNode(links);
            if (compact.Length < pointerLength ||
                (compact.Length - pointerLength) % (pointerLength + 2) != 0)
                throw new InvalidDataException("Malformed committed LTrie generation node.");

            ulong valuePointer = ReadPointer(compact, 0, pointerLength);
            if (valuePointer != 0)
                links[256] = valuePointer | ValueLinkMask;

            int step = pointerLength + 2;
            for (int offset = pointerLength; offset < compact.Length; offset += step)
            {
                ulong childPointer = ReadPointer(compact, offset + 2, pointerLength);
                if (childPointer == 0)
                    continue;
                links[compact[offset]] = childPointer |
                    (compact[offset + 1] == 1 ? ValueLinkMask : 0UL);
            }
            return new CommittedReadNode(links);
        }

        internal bool TryGet(int kid, out ulong pointer, out bool linkToNode)
        {
            ulong encoded = _links[kid];
            pointer = encoded & ~ValueLinkMask;
            linkToNode = (encoded & ValueLinkMask) == 0;
            return pointer != 0;
        }

        internal static ulong ReadPointer(byte[] bytes, int offset, int length)
        {
            ulong value = 0;
            for (int index = 0; index < length; index++)
                value = (value << 8) | bytes[offset + index];
            return value;
        }

        internal static byte[] PointerToBytes(ulong pointer, int length)
        {
            byte[] bytes = GC.AllocateUninitializedArray<byte>(length);
            for (int index = length - 1; index >= 0; index--)
            {
                bytes[index] = (byte)pointer;
                pointer >>= 8;
            }
            return bytes;
        }
    }
}
