/*
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.Buffers;
using System.Collections.Generic;
using DBreeze.DataTypes;
using DBreeze.LianaTrie;
using DBreeze.Utils;

namespace DBreeze.Transactions
{
    /// <summary>
    /// Net8 implementation of the transaction-local random-key buffer.
    /// It stores one final operation per binary key and never expands keys to hex strings.
    /// Binary keys are copied because they participate in dictionary identity; converted values
    /// are borrowed until Flush or Commit and must not be mutated by the caller meanwhile.
    /// </summary>
    public sealed class RandomKeySorter
    {
        private readonly Dictionary<string, TableBatch> _tables =
            new Dictionary<string, TableBatch>(StringComparer.Ordinal);
        private HashSet<string> _tablesWithOverwriteIsNotAllowed;

        internal Transaction _t;

        /// <summary>
        /// NOT USED ANYMORE. Preserved for public API compatibility.
        /// Automatic flush makes TryGetValueByKey ambiguous after flushing: a miss can mean
        /// either "not buffered" or "already flushed", forcing an LTrie/storage lookup and
        /// breaking the object-layer fast path.
        /// </summary>
        public int AutomaticFlushLimitQuantityPerTable = 1_000_000;

        private sealed class TableBatch
        {
            internal readonly Dictionary<ByteKey, PendingOperation> Operations =
                new Dictionary<ByteKey, PendingOperation>();
        }

        private readonly struct ByteKey : IEquatable<ByteKey>
        {
            internal readonly byte[] Bytes;
            private readonly int _hashCode;

            internal ByteKey(byte[] bytes, bool clone)
            {
                Bytes = clone ? (byte[])bytes.Clone() : bytes;
                _hashCode = ComputeHash(bytes);
            }

            public bool Equals(ByteKey other) => Bytes.AsSpan().SequenceEqual(other.Bytes);
            public override bool Equals(object obj) => obj is ByteKey other && Equals(other);
            public override int GetHashCode() => _hashCode;

            private static int ComputeHash(ReadOnlySpan<byte> bytes)
            {
                // FNV-1a is cheap for the usually short DBreeze keys and is stable for the
                // lifetime of this in-memory dictionary.
                unchecked
                {
                    uint hash = 2166136261;
                    foreach (byte value in bytes)
                    {
                        hash ^= value;
                        hash *= 16777619;
                    }
                    return (int)hash;
                }
            }
        }

        private readonly struct PendingOperation
        {
            internal readonly ByteKey Key;
            internal readonly byte[] Value;
            internal readonly bool IsRemove;

            internal PendingOperation(ByteKey key, byte[] value, bool isRemove)
            {
                Key = key;
                Value = value;
                IsRemove = isRemove;
            }
        }

        private sealed class PendingOperationComparer : IComparer<PendingOperation>
        {
            internal static readonly PendingOperationComparer Instance = new PendingOperationComparer();

            public int Compare(PendingOperation x, PendingOperation y) =>
                x.Key.Bytes.AsSpan().SequenceCompareTo(y.Key.Bytes);
        }

        internal void TablesWithOverwriteIsNotAllowed(string tableName)
        {
            _t.EnsureTransactionOwner();
            _tablesWithOverwriteIsNotAllowed ??= new HashSet<string>(StringComparer.Ordinal);
            if (_tablesWithOverwriteIsNotAllowed.Add(tableName))
                _t.Technical_SetTable_OverwriteIsNotAllowed(tableName);
        }

        internal byte[] TryGetValueByKey(string tableName, string key) =>
            TryGetValueByKey(tableName, key.ToByteArrayFromHex());

        internal byte[] TryGetValueByKey(string tableName, byte[] key)
        {
            if (!_tables.TryGetValue(tableName, out TableBatch batch))
                return null;

            if (!batch.Operations.TryGetValue(new ByteKey(key, false), out PendingOperation operation) || operation.IsRemove)
                return null;

            return operation.Value;
        }

        /// <summary>
        /// Buffers an insert; the last operation for the same serialized key wins.
        /// </summary>
        /// <remarks>
        /// The serialized value buffer is borrowed until <see cref="Flush(string)"/> or transaction Commit.
        /// A mutable <c>byte[]</c> value must not be changed by the caller during that interval.
        /// </remarks>
        public void Insert<TKey, TValue>(string tableName, TKey key, TValue value)
        {
            _t.EnsureTransactionOwner();
            if (key == null)
                throw new ArgumentNullException(nameof(key), "RandomKeySorter key can't be null");

            byte[] keyBytes = DataTypesConvertor.ConvertKey(key);
            byte[] valueBytes = DataTypesConvertor.ConvertValue(value);
            TableBatch batch = GetOrCreateBatch(tableName);
            ByteKey lookup = new ByteKey(keyBytes, false);

            if (batch.Operations.TryGetValue(lookup, out PendingOperation existing))
            {
                batch.Operations[lookup] = new PendingOperation(existing.Key, valueBytes, false);
            }
            else
            {
                ByteKey ownedKey = new ByteKey(keyBytes, true);
                batch.Operations.Add(ownedKey, new PendingOperation(ownedKey, valueBytes, false));
            }

        }

        public void Remove<TKey>(string tableName, TKey key)
        {
            _t.EnsureTransactionOwner();
            if (key == null)
                throw new ArgumentNullException(nameof(key), "RandomKeySorter key can't be null");

            byte[] keyBytes = DataTypesConvertor.ConvertKey(key);
            TableBatch batch = GetOrCreateBatch(tableName);
            ByteKey lookup = new ByteKey(keyBytes, false);

            if (batch.Operations.TryGetValue(lookup, out PendingOperation existing))
                batch.Operations[lookup] = new PendingOperation(existing.Key, null, true);
            else
            {
                ByteKey ownedKey = new ByteKey(keyBytes, true);
                batch.Operations.Add(ownedKey, new PendingOperation(ownedKey, null, true));
            }

        }

        private TableBatch GetOrCreateBatch(string tableName)
        {
            if (!_tables.TryGetValue(tableName, out TableBatch batch))
            {
                batch = new TableBatch();
                _tables.Add(tableName, batch);
            }
            return batch;
        }

        public void Flush(string tableName)
        {
            _t.EnsureTransactionOwner();
            if (_tables.TryGetValue(tableName, out TableBatch batch))
                FlushBatch(tableName, batch);
        }

        public void Flush()
        {
            _t.EnsureTransactionOwner();
            if (_tables.Count == 0)
                return;

            // FlushBatch removes from _tables, therefore snapshot only the table names.
            string[] tableNames = new string[_tables.Count];
            _tables.Keys.CopyTo(tableNames, 0);
            Array.Sort(tableNames, StringComparer.Ordinal);
            foreach (string tableName in tableNames)
            {
                if (_tables.TryGetValue(tableName, out TableBatch batch))
                    FlushBatch(tableName, batch);
            }
        }

        private void FlushBatch(string tableName, TableBatch batch)
        {
            int count = batch.Operations.Count;
            if (count == 0)
            {
                _tables.Remove(tableName);
                return;
            }

            PendingOperation[] buffer = ArrayPool<PendingOperation>.Shared.Rent(count);
            int index = 0;
            foreach (PendingOperation operation in batch.Operations.Values)
                buffer[index++] = operation;

            Array.Sort(buffer, 0, count, PendingOperationComparer.Instance);

            try
            {
                LTrie table = _t.GetWriteTableFromBuffer(tableName);
                bool wasOperated;
                byte[] deletedValue;

                // Preserve the historical flush contract: sorted removes first, then sorted inserts.
                for (int i = 0; i < count; i++)
                {
                    if (!buffer[i].IsRemove)
                        continue;
                    byte[] key = buffer[i].Key.Bytes;
                    table.Remove(ref key, out wasOperated, false, out deletedValue);
                }

                for (int i = 0; i < count; i++)
                {
                    if (buffer[i].IsRemove)
                        continue;
                    byte[] key = buffer[i].Key.Bytes;
                    byte[] value = buffer[i].Value;
                    table.Add(ref key, ref value, out wasOperated, false);
                }

                _tables.Remove(tableName);
            }
            finally
            {
                ArrayPool<PendingOperation>.Shared.Return(buffer, clearArray: true);
            }
        }

        internal void Reset()
        {
            _tables.Clear();
            _tablesWithOverwriteIsNotAllowed?.Clear();
        }
    }
}
