using System;
using System.Collections.Generic;
using DBreeze.DataTypes;
using DBreeze.Exceptions;
using DBreeze.Utils;

namespace DBreeze.Transactions
{
    public partial class Transaction
    {
        private sealed class MergeCursor<TKey, TValue> : IDisposable
        {
            internal readonly string TableName;
            internal readonly int TableOrder;
            internal readonly IEnumerator<Row<TKey, TValue>> Enumerator;
            internal Row<TKey, TValue> Current;
            internal byte[] KeyBytes;

            internal MergeCursor(string tableName, int tableOrder, IEnumerator<Row<TKey, TValue>> enumerator)
            {
                TableName = tableName;
                TableOrder = tableOrder;
                Enumerator = enumerator;
            }

            internal bool MoveNext()
            {
                if (!Enumerator.MoveNext())
                    return false;
                Current = Enumerator.Current;
                KeyBytes = Current.Key.ToBytes();
                return true;
            }

            public void Dispose() => Enumerator.Dispose();
        }

        private readonly struct MergePriority
        {
            internal readonly byte[] Key;
            internal readonly int TableOrder;

            internal MergePriority(byte[] key, int tableOrder)
            {
                Key = key;
                TableOrder = tableOrder;
            }
        }

        private sealed class ForwardMergePriorityComparer : IComparer<MergePriority>
        {
            internal static readonly ForwardMergePriorityComparer Instance = new ForwardMergePriorityComparer();

            public int Compare(MergePriority x, MergePriority y)
            {
                int result = x.Key.AsSpan().SequenceCompareTo(y.Key);
                return result != 0 ? result : x.TableOrder.CompareTo(y.TableOrder);
            }
        }

        private sealed class BackwardMergePriorityComparer : IComparer<MergePriority>
        {
            internal static readonly BackwardMergePriorityComparer Instance = new BackwardMergePriorityComparer();

            public int Compare(MergePriority x, MergePriority y)
            {
                int result = y.Key.AsSpan().SequenceCompareTo(x.Key);
                return result != 0 ? result : x.TableOrder.CompareTo(y.TableOrder);
            }
        }

        private IEnumerable<Row<TKey, TValue>> MultiSelectForwardFromToNet8<TKey, TValue>(
            HashSet<string> tables,
            TKey startKey,
            bool includeStartKey,
            TKey stopKey,
            bool includeStopKey,
            bool asReadVisibilityScope)
        {
            return MultiSelectFromToNet8(
                tables,
                table => SelectForwardFromTo<TKey, TValue>(table, startKey, includeStartKey, stopKey, includeStopKey, asReadVisibilityScope),
                ForwardMergePriorityComparer.Instance);
        }

        private IEnumerable<Row<TKey, TValue>> MultiSelectBackwardFromToNet8<TKey, TValue>(
            HashSet<string> tables,
            TKey startKey,
            bool includeStartKey,
            TKey stopKey,
            bool includeStopKey,
            bool asReadVisibilityScope)
        {
            return MultiSelectFromToNet8(
                tables,
                table => SelectBackwardFromTo<TKey, TValue>(table, startKey, includeStartKey, stopKey, includeStopKey, asReadVisibilityScope),
                BackwardMergePriorityComparer.Instance);
        }

        private static IEnumerable<Row<TKey, TValue>> MultiSelectFromToNet8<TKey, TValue>(
            HashSet<string> tables,
            Func<string, IEnumerable<Row<TKey, TValue>>> sequenceFactory,
            IComparer<MergePriority> priorityComparer)
        {
            if (tables == null || tables.Count == 0)
                yield break;

            var cursors = new List<MergeCursor<TKey, TValue>>(tables.Count);
            var queue = new System.Collections.Generic.PriorityQueue<MergeCursor<TKey, TValue>, MergePriority>(tables.Count, priorityComparer);
            int keyLength = -1;
            int tableOrder = 0;

            try
            {
                // HashSet enumeration order is intentionally snapshotted: it is the historical
                // tie-break order promised by these methods.
                foreach (string tableName in tables)
                {
                    var cursor = new MergeCursor<TKey, TValue>(tableName, tableOrder++, sequenceFactory(tableName).GetEnumerator());
                    cursors.Add(cursor);
                    if (!cursor.MoveNext())
                        continue;

                    if (keyLength < 0)
                        keyLength = cursor.KeyBytes.Length;
                    else if (cursor.KeyBytes.Length != keyLength)
                        throw DBreezeException.Throw(DBreezeException.eDBreezeExceptions.KEYS_IN_TABLES_HAVE_DIFFERENT_SIZE, new Exception());

                    queue.Enqueue(cursor, new MergePriority(cursor.KeyBytes, cursor.TableOrder));
                }

                while (queue.TryDequeue(out MergeCursor<TKey, TValue> cursor, out _))
                {
                    Row<TKey, TValue> row = cursor.Current;
                    row.TableName = cursor.TableName;
                    yield return row;

                    if (cursor.MoveNext())
                        queue.Enqueue(cursor, new MergePriority(cursor.KeyBytes, cursor.TableOrder));
                }
            }
            finally
            {
                foreach (MergeCursor<TKey, TValue> cursor in cursors)
                    cursor.Dispose();
            }
        }
    }
}
