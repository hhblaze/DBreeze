using DBreeze;
using DBreeze.DataTypes;

internal static class LianaTrieRegressionTests
{
    private const string RecreateTable = "liana-recreate";
    private const string ContractTable = "liana-contract";
    private static readonly IComparer<byte[]> ByteComparer = new LexicographicByteComparer();

    internal static void RemoveAllWithFileRecreationKeepsTableReusable()
    {
        RunRemoveAllWithFileRecreation(storageOnDisk: false);
        RunRemoveAllWithFileRecreation(storageOnDisk: true);
    }

    internal static void TraversalContractMatchesReferenceModel()
    {
        AssertEmptyTraversalContract();

        byte[][] keys =
        {
            Array.Empty<byte>(),
            new byte[] { 0 },
            new byte[] { 0, 0 },
            new byte[] { 0, 255 },
            new byte[] { 1 },
            new byte[] { 1, 0 },
            new byte[] { 1, 0, 0 },
            new byte[] { 1, 0, 255 },
            new byte[] { 1, 1 },
            new byte[] { 2 },
            new byte[] { 127 },
            new byte[] { 128 },
            new byte[] { 254, 255 },
            new byte[] { 255 },
            new byte[] { 255, 0 },
            Enumerable.Repeat((byte)42, 4_096).ToArray(),
        };

        byte[][] forward = keys.OrderBy(static key => key, ByteComparer).ToArray();
        byte[][] backward = forward.Reverse().ToArray();

        using var engine = CreateMemoryEngine();
        using (var transaction = engine.GetTransaction())
        {
            foreach (byte[] key in keys)
                transaction.Insert(ContractTable, key, ValueFor(key));
            transaction.Commit();
        }

        foreach (bool lazyLoading in new[] { true, false })
        {
            using var transaction = engine.GetTransaction();
            transaction.ValuesLazyLoadingIsOn = lazyLoading;

            AssertRows(forward,
                transaction.SelectForward<byte[], byte[]>(ContractTable),
                $"Forward, lazy={lazyLoading}");
            AssertRows(backward,
                transaction.SelectBackward<byte[], byte[]>(ContractTable),
                $"Backward, lazy={lazyLoading}");

            byte[] pivot = { 1, 0 };
            foreach (bool include in new[] { false, true })
            {
                AssertRows(forward.Where(key => Compare(key, pivot) > 0 || include && Compare(key, pivot) == 0),
                    transaction.SelectForwardStartFrom<byte[], byte[]>(ContractTable, pivot, include),
                    $"ForwardStartFrom include={include}, lazy={lazyLoading}");
                AssertRows(backward.Where(key => Compare(key, pivot) < 0 || include && Compare(key, pivot) == 0),
                    transaction.SelectBackwardStartFrom<byte[], byte[]>(ContractTable, pivot, include),
                    $"BackwardStartFrom include={include}, lazy={lazyLoading}");
            }

            byte[] rangeStart = { 0 };
            byte[] rangeStop = { 2 };
            foreach (bool includeStart in new[] { false, true })
            {
                foreach (bool includeStop in new[] { false, true })
                {
                    AssertRows(forward.Where(key => InForwardRange(key, rangeStart, includeStart, rangeStop, includeStop)),
                        transaction.SelectForwardFromTo<byte[], byte[]>(
                            ContractTable, rangeStart, includeStart, rangeStop, includeStop),
                        $"ForwardFromTo {includeStart}/{includeStop}, lazy={lazyLoading}");
                    AssertRows(backward.Where(key => InBackwardRange(key, rangeStop, includeStop, rangeStart, includeStart)),
                        transaction.SelectBackwardFromTo<byte[], byte[]>(
                            ContractTable, rangeStop, includeStop, rangeStart, includeStart),
                        $"BackwardFromTo {includeStop}/{includeStart}, lazy={lazyLoading}");
                }
            }

            foreach (ulong skip in new[] { 0UL, 1UL, 5UL, (ulong)keys.Length, ulong.MaxValue })
            {
                int count = skip >= (ulong)keys.Length ? keys.Length : (int)skip;
                AssertRows(forward.Skip(count),
                    transaction.SelectForwardSkip<byte[], byte[]>(ContractTable, skip),
                    $"ForwardSkip {skip}, lazy={lazyLoading}");
                AssertRows(backward.Skip(count),
                    transaction.SelectBackwardSkip<byte[], byte[]>(ContractTable, skip),
                    $"BackwardSkip {skip}, lazy={lazyLoading}");
            }

            foreach (byte[] skipPivot in new[] { pivot, new byte[] { 1, 0, 1 } })
            {
                foreach (ulong skip in new[] { 0UL, 1UL, ulong.MaxValue })
                {
                    byte[][] forwardCandidates = forward.Where(key => Compare(key, skipPivot) > 0).ToArray();
                    byte[][] backwardCandidates = backward.Where(key => Compare(key, skipPivot) < 0).ToArray();
                    int forwardSkip = skip >= (ulong)forwardCandidates.Length ? forwardCandidates.Length : (int)skip;
                    int backwardSkip = skip >= (ulong)backwardCandidates.Length ? backwardCandidates.Length : (int)skip;

                    AssertRows(forwardCandidates.Skip(forwardSkip),
                        transaction.SelectForwardSkipFrom<byte[], byte[]>(ContractTable, skipPivot, skip),
                        $"ForwardSkipFrom {Format(skipPivot)}/{skip}, lazy={lazyLoading}");
                    AssertRows(backwardCandidates.Skip(backwardSkip),
                        transaction.SelectBackwardSkipFrom<byte[], byte[]>(ContractTable, skipPivot, skip),
                        $"BackwardSkipFrom {Format(skipPivot)}/{skip}, lazy={lazyLoading}");
                }
            }

            byte[] prefix = { 1, 0 };
            byte[][] prefixForward = forward.Where(key => StartsWith(key, prefix)).ToArray();
            AssertRows(prefixForward,
                transaction.SelectForwardStartsWith<byte[], byte[]>(ContractTable, prefix),
                $"ForwardStartsWith, lazy={lazyLoading}");
            AssertRows(prefixForward.Reverse(),
                transaction.SelectBackwardStartsWith<byte[], byte[]>(ContractTable, prefix),
                $"BackwardStartsWith, lazy={lazyLoading}");

            byte[] closest = { 1, 0, 128, 77 };
            byte[] closestPrefix = LongestExistingPrefix(forward, closest);
            byte[][] closestForward = forward.Where(key => StartsWith(key, closestPrefix)).ToArray();
            AssertRows(closestForward,
                transaction.SelectForwardStartsWithClosestToPrefix<byte[], byte[]>(ContractTable, closest),
                $"ForwardClosestPrefix, lazy={lazyLoading}");
            AssertRows(closestForward.Reverse(),
                transaction.SelectBackwardStartsWithClosestToPrefix<byte[], byte[]>(ContractTable, closest),
                $"BackwardClosestPrefix, lazy={lazyLoading}");

            AssertRow(forward[0], transaction.Min<byte[], byte[]>(ContractTable), $"Min, lazy={lazyLoading}");
            AssertRow(backward[0], transaction.Max<byte[], byte[]>(ContractTable), $"Max, lazy={lazyLoading}");
        }
    }

    private static void AssertEmptyTraversalContract()
    {
        const string table = "liana-contract-empty";
        byte[] pivot = { 1 };

        using var engine = CreateMemoryEngine();
        foreach (bool lazyLoading in new[] { true, false })
        {
            using var transaction = engine.GetTransaction();
            transaction.ValuesLazyLoadingIsOn = lazyLoading;

            AssertRows(Array.Empty<byte[]>(), transaction.SelectForward<byte[], byte[]>(table), "Empty Forward.");
            AssertRows(Array.Empty<byte[]>(), transaction.SelectBackward<byte[], byte[]>(table), "Empty Backward.");
            AssertRows(Array.Empty<byte[]>(),
                transaction.SelectForwardStartFrom<byte[], byte[]>(table, pivot, true), "Empty ForwardStartFrom.");
            AssertRows(Array.Empty<byte[]>(),
                transaction.SelectBackwardStartFrom<byte[], byte[]>(table, pivot, true), "Empty BackwardStartFrom.");
            AssertRows(Array.Empty<byte[]>(),
                transaction.SelectForwardFromTo<byte[], byte[]>(table, pivot, true, pivot, true),
                "Empty ForwardFromTo.");
            AssertRows(Array.Empty<byte[]>(),
                transaction.SelectBackwardFromTo<byte[], byte[]>(table, pivot, true, pivot, true),
                "Empty BackwardFromTo.");
            AssertRows(Array.Empty<byte[]>(),
                transaction.SelectForwardSkip<byte[], byte[]>(table, 1), "Empty ForwardSkip.");
            AssertRows(Array.Empty<byte[]>(),
                transaction.SelectBackwardSkip<byte[], byte[]>(table, 1), "Empty BackwardSkip.");
            AssertRows(Array.Empty<byte[]>(),
                transaction.SelectForwardSkipFrom<byte[], byte[]>(table, pivot, 1), "Empty ForwardSkipFrom.");
            AssertRows(Array.Empty<byte[]>(),
                transaction.SelectBackwardSkipFrom<byte[], byte[]>(table, pivot, 1), "Empty BackwardSkipFrom.");
            AssertRows(Array.Empty<byte[]>(),
                transaction.SelectForwardStartsWith<byte[], byte[]>(table, pivot), "Empty ForwardStartsWith.");
            AssertRows(Array.Empty<byte[]>(),
                transaction.SelectBackwardStartsWith<byte[], byte[]>(table, pivot), "Empty BackwardStartsWith.");
            AssertRows(Array.Empty<byte[]>(),
                transaction.SelectForwardStartsWithClosestToPrefix<byte[], byte[]>(table, pivot),
                "Empty ForwardClosestPrefix.");
            AssertRows(Array.Empty<byte[]>(),
                transaction.SelectBackwardStartsWithClosestToPrefix<byte[], byte[]>(table, pivot),
                "Empty BackwardClosestPrefix.");
            Assert(!transaction.Min<byte[], byte[]>(table).Exists, "Empty Min exists.");
            Assert(!transaction.Max<byte[], byte[]>(table).Exists, "Empty Max exists.");
        }
    }

    private static void RunRemoveAllWithFileRecreation(bool storageOnDisk)
    {
        string folder = storageOnDisk
            ? Path.Combine(Path.GetTempPath(), "DBreeze.Net8.Tests", Guid.NewGuid().ToString("N"))
            : null;

        DBreezeEngine engine = storageOnDisk
            ? new DBreezeEngine(folder)
            : CreateMemoryEngine();

        try
        {
            byte[] oldParent = { 1 };
            byte[] newValueKey = { 2 };
            byte[] newParent = { 3 };
            byte[] childKey = { 4 };
            byte[] childValue = { 5 };

            using (var transaction = engine.GetTransaction())
            {
                transaction.Insert(RecreateTable, oldParent, new byte[] { 10 });
                using var nested = transaction.InsertTable(RecreateTable, oldParent, 0);
                nested.Insert(new byte[] { 11 }, new byte[] { 12 });
                transaction.Commit();
            }

            using (var transaction = engine.GetTransaction())
            {
                transaction.RemoveAllKeys(RecreateTable, true);
                transaction.Insert(RecreateTable, newValueKey, new byte[] { 20 });
                transaction.Insert(RecreateTable, newParent, new byte[] { 30 });
                using var nested = transaction.InsertTable(RecreateTable, newParent, 0);
                nested.Insert(childKey, childValue);
                transaction.Commit();
            }

            using (var transaction = engine.GetTransaction())
            {
                Assert(!transaction.Select<byte[], byte[]>(RecreateTable, oldParent).Exists,
                    "RemoveAll(true) left the old parent row visible.");
                AssertSequenceEqual(new byte[] { 20 },
                    transaction.Select<byte[], byte[]>(RecreateTable, newValueKey).Value,
                    "Value inserted after RemoveAll(true).");
                using var nested = transaction.SelectTable(RecreateTable, newParent, 0);
                AssertSequenceEqual(childValue, nested.Select<byte[], byte[]>(childKey).Value,
                    "Nested value inserted after RemoveAll(true).");
            }

            for (int cycle = 0; cycle < 3; cycle++)
            {
                using (var transaction = engine.GetTransaction())
                {
                    transaction.RemoveAllKeys(RecreateTable, true);
                    transaction.Commit();
                }

                using (var transaction = engine.GetTransaction())
                {
                    AssertEqual(0UL, transaction.Count(RecreateTable),
                        $"Count after RemoveAll(true), cycle {cycle}.");
                    transaction.Insert(RecreateTable, cycle, cycle * 10);
                    transaction.Commit();
                }
            }

            using (var transaction = engine.GetTransaction())
            {
                AssertEqual(1UL, transaction.Count(RecreateTable), "Count after repeated recreation.");
                AssertEqual(20, transaction.Select<int, int>(RecreateTable, 2).Value,
                    "Value after repeated recreation.");
            }

            engine.Dispose();
            engine.Dispose();
        }
        finally
        {
            engine.Dispose();
            if (folder != null && Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static DBreezeEngine CreateMemoryEngine() => new(new DBreezeConfiguration
    {
        Storage = DBreezeConfiguration.eStorage.MEMORY,
        NotifyAhead_WhenWriteTablePossibleDeadlock = false,
    });

    private static bool InForwardRange(
        byte[] key, byte[] start, bool includeStart, byte[] stop, bool includeStop)
    {
        int startComparison = Compare(key, start);
        int stopComparison = Compare(key, stop);
        return (startComparison > 0 || includeStart && startComparison == 0)
            && (stopComparison < 0 || includeStop && stopComparison == 0);
    }

    private static bool InBackwardRange(
        byte[] key, byte[] start, bool includeStart, byte[] stop, bool includeStop)
    {
        int startComparison = Compare(key, start);
        int stopComparison = Compare(key, stop);
        return (startComparison < 0 || includeStart && startComparison == 0)
            && (stopComparison > 0 || includeStop && stopComparison == 0);
    }

    private static byte[] LongestExistingPrefix(IEnumerable<byte[]> keys, byte[] candidate)
    {
        for (int length = candidate.Length; length > 0; length--)
        {
            byte[] prefix = candidate.AsSpan(0, length).ToArray();
            if (keys.Any(key => StartsWith(key, prefix)))
                return prefix;
        }

        return Array.Empty<byte>();
    }

    private static bool StartsWith(byte[] value, byte[] prefix) =>
        value.Length >= prefix.Length && value.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    private static int Compare(byte[] left, byte[] right) => left.AsSpan().SequenceCompareTo(right);

    private static byte[] ValueFor(byte[] key)
    {
        byte[] value = new byte[key.Length + 2];
        value[0] = (byte)key.Length;
        value[1] = 0xA5;
        for (int i = 0; i < key.Length; i++)
            value[i + 2] = key[key.Length - i - 1];
        return value;
    }

    private static void AssertRows(
        IEnumerable<byte[]> expectedKeys,
        IEnumerable<Row<byte[], byte[]>> actualRows,
        string message)
    {
        byte[][] expected = expectedKeys.ToArray();
        Row<byte[], byte[]>[] actual = actualRows.ToArray();
        AssertEqual(expected.Length, actual.Length, message + " count.");
        for (int i = 0; i < expected.Length; i++)
        {
            AssertSequenceEqual(expected[i], actual[i].Key, $"{message} key {i}.");
            AssertSequenceEqual(ValueFor(expected[i]), actual[i].Value, $"{message} value {i}.");
        }
    }

    private static void AssertRow(byte[] expectedKey, Row<byte[], byte[]> actual, string message)
    {
        Assert(actual.Exists, message + " row does not exist.");
        AssertSequenceEqual(expectedKey, actual.Key, message + " key.");
        AssertSequenceEqual(ValueFor(expectedKey), actual.Value, message + " value.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}.");
    }

    private static void AssertSequenceEqual(byte[] expected, byte[] actual, string message)
    {
        if (expected == null || actual == null || !expected.AsSpan().SequenceEqual(actual))
            throw new InvalidOperationException($"{message} Expected: {Format(expected)}; actual: {Format(actual)}.");
    }

    private static string Format(byte[] value) => value == null ? "<null>" : Convert.ToHexString(value);

    private sealed class LexicographicByteComparer : IComparer<byte[]>
    {
        public int Compare(byte[] x, byte[] y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x == null)
                return -1;
            if (y == null)
                return 1;
            return x.AsSpan().SequenceCompareTo(y);
        }
    }
}
