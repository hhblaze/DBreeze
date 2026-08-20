using DBreeze;
using DBreeze.DataTypes;
using DBreeze.LianaTrie;
using DBreeze.Storage;
using DBreeze.Transactions;

internal static class LianaTrieRegressionTests
{
    private const string RecreateTable = "liana-recreate";
    private const string ContractTable = "liana-contract";
    private static readonly string DatabaseTestRoot = @"D:\Temp\DbreezeDbTest";
    private static readonly IComparer<byte[]> ByteComparer = new LexicographicByteComparer();

    internal static void RemoveAllWithFileRecreationKeepsTableReusable()
    {
        RunRemoveAllWithFileRecreation(storageOnDisk: false);
        RunRemoveAllWithFileRecreation(storageOnDisk: true);
    }

    internal static void EarlyDisposedNestedTablesFollowMasterTransaction()
    {
        RunEarlyDisposedNestedTables(storageOnDisk: false);
        RunEarlyDisposedNestedTables(storageOnDisk: true);
        RunDirectLTrieEarlyDispose();
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

    internal static void AlternativeTraversalsAreIterativeAndIsolated()
    {
        const int depth = 8_192;
        byte[] prefix = Enumerable.Repeat((byte)0x42, depth).ToArray();
        byte[][] keys = CreateDeepTraversalKeys(prefix);
        byte[][] forward = keys.OrderBy(static key => key, ByteComparer).ToArray();
        byte[][] backward = forward.Reverse().ToArray();

        using var engine = CreateMemoryEngine();
        using (var transaction = engine.GetTransaction())
        {
            foreach (byte[] key in keys)
                transaction.Insert(ContractTable, key, CompactValueFor(key));
            transaction.Commit();
        }

        foreach (bool lazyLoading in new[] { true, false })
        {
            using var transaction = engine.GetTransaction();
            transaction.ValuesLazyLoadingIsOn = lazyLoading;
            AssertDeepTraversalMatrix(transaction, ContractTable, prefix, forward, backward, lazyLoading);
        }

        AssertEmptyAlternativeTraversalContract();
        AssertReadVisibilityForAlternativeTraversals(engine, prefix);
        AssertEagerRowsSurviveRecreation();
    }

    internal static void RecursiveNestedTraversalsAreIterative()
    {
        foreach (bool storageOnDisk in new[] { false, true })
        {
            byte[] prefix = Enumerable.Repeat((byte)0x63, 8_192).ToArray();
            byte[][] keys = CreateDeepTraversalKeys(prefix);
            byte[][] forward = keys.OrderBy(static key => key, ByteComparer).ToArray();
            byte[][] backward = forward.Reverse().ToArray();
            byte[] rootKey = { 0x10 };
            byte[] secondKey = { 0x20 };
            byte[] thirdKey = { 0x30 };
            byte[] fourthKey = { 0x40 };

            RunStorageScenario(
                $"deep-nested-traversal-{storageOnDisk}",
                storageOnDisk,
                engine =>
                {
                    using var transaction = engine.GetTransaction();
                    transaction.Insert(ContractTable, rootKey, new byte[] { 1 });

                    NestedTable level1 = null;
                    NestedTable level2 = null;
                    NestedTable level3 = null;
                    NestedTable level4 = null;
                    try
                    {
                        level1 = transaction.InsertTable(ContractTable, rootKey, 0);
                        level1.Insert(secondKey, new byte[] { 2 });
                        level2 = level1.GetTable(secondKey, 1);
                        level2.Insert(thirdKey, new byte[] { 3 });
                        level3 = level2.GetTable(thirdKey, 2);
                        level3.Insert(fourthKey, new byte[] { 4 });
                        level4 = level3.GetTable(fourthKey, 3);
                        foreach (byte[] key in keys)
                            level4.Insert(key, CompactValueFor(key));

                        // All nested owners deliberately remain open until the master transaction commits.
                        transaction.Commit();
                    }
                    finally
                    {
                        level4?.Dispose();
                        level3?.Dispose();
                        level2?.Dispose();
                        level1?.Dispose();
                    }
                },
                engine =>
                {
                    foreach (bool lazyLoading in new[] { true, false })
                    {
                        using var transaction = engine.GetTransaction();
                        using NestedTable level1 = transaction.SelectTable(ContractTable, rootKey, 0);
                        using NestedTable level2 = level1.GetTable(secondKey, 1);
                        using NestedTable level3 = level2.GetTable(thirdKey, 2);
                        using NestedTable level4 = level3.GetTable(fourthKey, 3);
                        level4.ValuesLazyLoadingIsOn = lazyLoading;
                        AssertDeepNestedTraversalMatrix(level4, prefix, forward, backward, lazyLoading);
                    }
                });
        }
    }

    private static void AssertDeepTraversalMatrix(
        Transaction transaction,
        string table,
        byte[] prefix,
        byte[][] forward,
        byte[][] backward,
        bool lazyLoading)
    {
        byte[] existingPivot = Append(prefix, 0x00);
        byte[] missingPivot = Append(prefix, 0x00, 0x80);
        byte[] rangeStop = Append(prefix, 0x01);
        byte[] closestCandidate = Append(prefix, 0x02, 0x77);

        foreach (byte[] pivot in new[] { existingPivot, missingPivot, new byte[] { 0x40 }, new byte[] { 0x44 } })
        {
            foreach (bool include in new[] { false, true })
            {
                AssertCompactRows(
                    forward.Where(key => Compare(key, pivot) > 0 || include && Compare(key, pivot) == 0),
                    transaction.SelectForwardStartFrom<byte[], byte[]>(table, pivot, include),
                    $"Deep ForwardStartFrom {Format(pivot)}/{include}, lazy={lazyLoading}.");
                AssertCompactRows(
                    backward.Where(key => Compare(key, pivot) < 0 || include && Compare(key, pivot) == 0),
                    transaction.SelectBackwardStartFrom<byte[], byte[]>(table, pivot, include),
                    $"Deep BackwardStartFrom {Format(pivot)}/{include}, lazy={lazyLoading}.");
            }
        }

        foreach (bool includeStart in new[] { false, true })
        {
            foreach (bool includeStop in new[] { false, true })
            {
                AssertCompactRows(
                    forward.Where(key => InForwardRange(key, existingPivot, includeStart, rangeStop, includeStop)),
                    transaction.SelectForwardFromTo<byte[], byte[]>(
                        table, existingPivot, includeStart, rangeStop, includeStop),
                    $"Deep ForwardFromTo {includeStart}/{includeStop}, lazy={lazyLoading}.");
                AssertCompactRows(
                    backward.Where(key => InBackwardRange(key, rangeStop, includeStop, existingPivot, includeStart)),
                    transaction.SelectBackwardFromTo<byte[], byte[]>(
                        table, rangeStop, includeStop, existingPivot, includeStart),
                    $"Deep BackwardFromTo {includeStop}/{includeStart}, lazy={lazyLoading}.");
            }
        }

        foreach (ulong skip in new[] { 0UL, 1UL, 3UL, (ulong)forward.Length, ulong.MaxValue })
        {
            byte[][] forwardCandidates = forward.Where(key => Compare(key, missingPivot) > 0).ToArray();
            byte[][] backwardCandidates = backward.Where(key => Compare(key, missingPivot) < 0).ToArray();
            int forwardSkip = skip >= (ulong)forwardCandidates.Length ? forwardCandidates.Length : (int)skip;
            int backwardSkip = skip >= (ulong)backwardCandidates.Length ? backwardCandidates.Length : (int)skip;
            AssertCompactRows(forwardCandidates.Skip(forwardSkip),
                transaction.SelectForwardSkipFrom<byte[], byte[]>(table, missingPivot, skip),
                $"Deep ForwardSkipFrom {skip}, lazy={lazyLoading}.");
            AssertCompactRows(backwardCandidates.Skip(backwardSkip),
                transaction.SelectBackwardSkipFrom<byte[], byte[]>(table, missingPivot, skip),
                $"Deep BackwardSkipFrom {skip}, lazy={lazyLoading}.");
        }

        byte[][] prefixForward = forward.Where(key => StartsWith(key, prefix)).ToArray();
        AssertCompactRows(prefixForward,
            transaction.SelectForwardStartsWith<byte[], byte[]>(table, prefix),
            $"Deep ForwardStartsWith, lazy={lazyLoading}.");
        AssertCompactRows(prefixForward.Reverse(),
            transaction.SelectBackwardStartsWith<byte[], byte[]>(table, prefix),
            $"Deep BackwardStartsWith, lazy={lazyLoading}.");
        AssertCompactRows(prefixForward,
            transaction.SelectForwardStartsWithClosestToPrefix<byte[], byte[]>(table, closestCandidate),
            $"Deep ForwardClosestPrefix, lazy={lazyLoading}.");
        AssertCompactRows(prefixForward.Reverse(),
            transaction.SelectBackwardStartsWithClosestToPrefix<byte[], byte[]>(table, closestCandidate),
            $"Deep BackwardClosestPrefix, lazy={lazyLoading}.");

        AssertCompactRow(forward[0], transaction.Min<byte[], byte[]>(table), $"Deep Min, lazy={lazyLoading}.");
        AssertCompactRow(backward[0], transaction.Max<byte[], byte[]>(table), $"Deep Max, lazy={lazyLoading}.");

        int leadingCount = 2;
        byte[][] leadingForward = forward.Where(key => Compare(key, existingPivot) < 0).ToArray();
        leadingForward = leadingForward.Skip(Math.Max(0, leadingForward.Length - leadingCount)).ToArray();
        byte[][] forwardRange = forward
            .Where(key => InForwardRange(key, existingPivot, true, rangeStop, true)).ToArray();
        AssertCompactRows(leadingForward.Concat(forwardRange),
            transaction.SelectForwardFromTo<byte[], byte[]>(
                table, existingPivot, true, rangeStop, true, leadingCount),
            $"Deep ForwardFromTo leading, lazy={lazyLoading}.");

        byte[][] leadingBackward = forward.Where(key => Compare(key, rangeStop) > 0).Take(leadingCount).Reverse().ToArray();
        byte[][] backwardRange = backward
            .Where(key => InBackwardRange(key, rangeStop, true, existingPivot, true)).ToArray();
        AssertCompactRows(leadingBackward.Concat(backwardRange),
            transaction.SelectBackwardFromTo<byte[], byte[]>(
                table, rangeStop, true, existingPivot, true, leadingCount),
            $"Deep BackwardFromTo leading, lazy={lazyLoading}.");

        IEnumerable<Row<byte[], byte[]>> repeatable =
            transaction.SelectForwardStartFrom<byte[], byte[]>(table, missingPivot, true);
        byte[][] repeatableExpected = forward.Where(key => Compare(key, missingPivot) >= 0).ToArray();
        AssertCompactRows(repeatableExpected, repeatable, $"Deep repeatable first pass, lazy={lazyLoading}.");
        AssertCompactRows(repeatableExpected, repeatable, $"Deep repeatable second pass, lazy={lazyLoading}.");

        AssertEarlyDispose(transaction.SelectForwardStartFrom<byte[], byte[]>(table, existingPivot, true),
            $"Deep ForwardStartFrom early dispose, lazy={lazyLoading}.");
        AssertEarlyDispose(transaction.SelectBackwardSkipFrom<byte[], byte[]>(table, rangeStop, 0),
            $"Deep BackwardSkipFrom early dispose, lazy={lazyLoading}.");
        AssertEarlyDispose(transaction.SelectForwardStartsWith<byte[], byte[]>(table, prefix),
            $"Deep StartsWith early dispose, lazy={lazyLoading}.");
        AssertEarlyDispose(
            transaction.SelectBackwardStartsWithClosestToPrefix<byte[], byte[]>(table, closestCandidate),
            $"Deep ClosestPrefix early dispose, lazy={lazyLoading}.");
    }

    private static void AssertDeepNestedTraversalMatrix(
        NestedTable table,
        byte[] prefix,
        byte[][] forward,
        byte[][] backward,
        bool lazyLoading)
    {
        byte[] existingPivot = Append(prefix, 0x00);
        byte[] missingPivot = Append(prefix, 0x00, 0x80);
        byte[] rangeStop = Append(prefix, 0x01);
        byte[] closestCandidate = Append(prefix, 0x02, 0x77);

        AssertCompactRows(forward.Where(key => Compare(key, existingPivot) >= 0),
            table.SelectForwardStartFrom<byte[], byte[]>(existingPivot, true),
            $"Nested ForwardStartFrom, lazy={lazyLoading}.");
        AssertCompactRows(backward.Where(key => Compare(key, rangeStop) <= 0),
            table.SelectBackwardStartFrom<byte[], byte[]>(rangeStop, true),
            $"Nested BackwardStartFrom, lazy={lazyLoading}.");
        AssertCompactRows(forward.Where(key => InForwardRange(key, existingPivot, true, rangeStop, false)),
            table.SelectForwardFromTo<byte[], byte[]>(existingPivot, true, rangeStop, false),
            $"Nested ForwardFromTo, lazy={lazyLoading}.");
        AssertCompactRows(backward.Where(key => InBackwardRange(key, rangeStop, true, existingPivot, false)),
            table.SelectBackwardFromTo<byte[], byte[]>(rangeStop, true, existingPivot, false),
            $"Nested BackwardFromTo, lazy={lazyLoading}.");

        byte[][] forwardCandidates = forward.Where(key => Compare(key, missingPivot) > 0).ToArray();
        byte[][] backwardCandidates = backward.Where(key => Compare(key, missingPivot) < 0).ToArray();
        foreach (ulong skip in new[] { 0UL, 1UL, ulong.MaxValue })
        {
            int forwardSkip = skip >= (ulong)forwardCandidates.Length ? forwardCandidates.Length : (int)skip;
            int backwardSkip = skip >= (ulong)backwardCandidates.Length ? backwardCandidates.Length : (int)skip;
            AssertCompactRows(forwardCandidates.Skip(forwardSkip),
                table.SelectForwardSkipFrom<byte[], byte[]>(missingPivot, skip),
                $"Nested ForwardSkipFrom {skip}, lazy={lazyLoading}.");
            AssertCompactRows(backwardCandidates.Skip(backwardSkip),
                table.SelectBackwardSkipFrom<byte[], byte[]>(missingPivot, skip),
                $"Nested BackwardSkipFrom {skip}, lazy={lazyLoading}.");
        }

        byte[][] prefixForward = forward.Where(key => StartsWith(key, prefix)).ToArray();
        AssertCompactRows(prefixForward, table.SelectForwardStartsWith<byte[], byte[]>(prefix),
            $"Nested ForwardStartsWith, lazy={lazyLoading}.");
        AssertCompactRows(prefixForward.Reverse(), table.SelectBackwardStartsWith<byte[], byte[]>(prefix),
            $"Nested BackwardStartsWith, lazy={lazyLoading}.");
        AssertCompactRows(prefixForward,
            table.SelectForwardStartsWithClosestToPrefix<byte[], byte[]>(closestCandidate),
            $"Nested ForwardClosestPrefix, lazy={lazyLoading}.");
        AssertCompactRows(prefixForward.Reverse(),
            table.SelectBackwardStartsWithClosestToPrefix<byte[], byte[]>(closestCandidate),
            $"Nested BackwardClosestPrefix, lazy={lazyLoading}.");
        AssertCompactRow(forward[0], table.Min<byte[], byte[]>(), $"Nested Min, lazy={lazyLoading}.");
        AssertCompactRow(backward[0], table.Max<byte[], byte[]>(), $"Nested Max, lazy={lazyLoading}.");

        IEnumerable<Row<byte[], byte[]>> repeatable = table.SelectForwardStartsWith<byte[], byte[]>(prefix);
        AssertCompactRows(prefixForward, repeatable, $"Nested repeatable first pass, lazy={lazyLoading}.");
        AssertCompactRows(prefixForward, repeatable, $"Nested repeatable second pass, lazy={lazyLoading}.");
        AssertEarlyDispose(table.SelectForwardStartFrom<byte[], byte[]>(existingPivot, true),
            $"Nested StartFrom early dispose, lazy={lazyLoading}.");
        AssertEarlyDispose(table.SelectBackwardStartsWith<byte[], byte[]>(prefix),
            $"Nested StartsWith early dispose, lazy={lazyLoading}.");
    }

    private static void AssertEmptyAlternativeTraversalContract()
    {
        const string table = "liana-empty-key-contract";
        using var engine = CreateMemoryEngine();
        using (var transaction = engine.GetTransaction())
        {
            transaction.Insert(table, Array.Empty<byte>(), new byte[] { 0xEE });
            transaction.Insert(table, new byte[] { 1 }, new byte[] { 1 });
            transaction.Commit();
        }

        foreach (bool lazyLoading in new[] { true, false })
        {
            using var transaction = engine.GetTransaction();
            transaction.ValuesLazyLoadingIsOn = lazyLoading;
            AssertThrows<IndexOutOfRangeException>(() =>
                transaction.SelectForwardStartFrom<byte[], byte[]>(table, Array.Empty<byte>(), true).ToArray());
            AssertThrows<IndexOutOfRangeException>(() =>
                transaction.SelectBackwardStartFrom<byte[], byte[]>(table, Array.Empty<byte>(), true).ToArray());
            AssertThrows<IndexOutOfRangeException>(() =>
                transaction.SelectForwardSkipFrom<byte[], byte[]>(table, Array.Empty<byte>(), 0).ToArray());
            AssertThrows<IndexOutOfRangeException>(() =>
                transaction.SelectBackwardSkipFrom<byte[], byte[]>(table, Array.Empty<byte>(), 0).ToArray());
            AssertEqual(0,
                transaction.SelectForwardStartsWith<byte[], byte[]>(table, Array.Empty<byte>()).Count(),
                "ForwardStartsWith(empty) must remain empty.");
            AssertEqual(0,
                transaction.SelectBackwardStartsWith<byte[], byte[]>(table, Array.Empty<byte>()).Count(),
                "BackwardStartsWith(empty) must remain empty.");
            AssertEqual(0,
                transaction.SelectForwardStartsWithClosestToPrefix<byte[], byte[]>(table, Array.Empty<byte>()).Count(),
                "ForwardClosestPrefix(empty) must remain empty.");
            AssertEqual(0,
                transaction.SelectBackwardStartsWithClosestToPrefix<byte[], byte[]>(table, Array.Empty<byte>()).Count(),
                "BackwardClosestPrefix(empty) must remain empty.");
        }
    }

    private static void AssertReadVisibilityForAlternativeTraversals(DBreezeEngine engine, byte[] prefix)
    {
        byte[] newKey = Append(prefix, 0x00, 0x80);
        using var transaction = engine.GetTransaction();
        transaction.Insert(ContractTable, newKey, CompactValueFor(newKey));

        Assert(transaction.SelectForwardStartFrom<byte[], byte[]>(ContractTable, newKey, true)
                .Any(row => row.Key.AsSpan().SequenceEqual(newKey)),
            "Write-root StartFrom did not see the uncommitted row.");
        Assert(!transaction.SelectForwardStartFrom<byte[], byte[]>(ContractTable, newKey, true, true)
                .Any(row => row.Key.AsSpan().SequenceEqual(newKey)),
            "Read-visibility StartFrom saw the uncommitted row.");
        Assert(transaction.SelectBackwardStartsWith<byte[], byte[]>(ContractTable, newKey)
                .Any(row => row.Key.AsSpan().SequenceEqual(newKey)),
            "Write-root StartsWith did not see the uncommitted row.");
        Assert(!transaction.SelectBackwardStartsWith<byte[], byte[]>(ContractTable, newKey, true)
                .Any(row => row.Key.AsSpan().SequenceEqual(newKey)),
            "Read-visibility StartsWith saw the uncommitted row.");
        transaction.Rollback();
    }

    private static void AssertEagerRowsSurviveRecreation()
    {
        const string table = "liana-eager-recreate";
        byte[][] keys =
        {
            new byte[] { 1, 0 }, new byte[] { 1, 1 }, new byte[] { 1, 2 }, new byte[] { 2 },
        };

        using var engine = CreateMemoryEngine();
        using (var transaction = engine.GetTransaction())
        {
            foreach (byte[] key in keys)
                transaction.Insert(table, key, CompactValueFor(key));
            transaction.Commit();
        }

        using var eager = engine.GetTransaction();
        eager.ValuesLazyLoadingIsOn = false;
        var retained = new List<(Row<byte[], byte[]> Row, byte[] Expected)>();
        AddRetained(retained, eager.SelectForwardStartFrom<byte[], byte[]>(table, new byte[] { 1, 0 }, true).First());
        AddRetained(retained, eager.SelectBackwardStartFrom<byte[], byte[]>(table, new byte[] { 1, 2 }, true).First());
        AddRetained(retained, eager.SelectForwardSkipFrom<byte[], byte[]>(table, new byte[] { 1, 0 }, 0).First());
        AddRetained(retained, eager.SelectBackwardSkipFrom<byte[], byte[]>(table, new byte[] { 2 }, 0).First());
        AddRetained(retained, eager.SelectForwardStartsWith<byte[], byte[]>(table, new byte[] { 1 }).First());
        AddRetained(retained, eager.SelectBackwardStartsWith<byte[], byte[]>(table, new byte[] { 1 }).First());
        AddRetained(retained,
            eager.SelectForwardStartsWithClosestToPrefix<byte[], byte[]>(table, new byte[] { 1, 9 }).First());
        AddRetained(retained,
            eager.SelectBackwardStartsWithClosestToPrefix<byte[], byte[]>(table, new byte[] { 1, 9 }).First());
        AddRetained(retained, eager.Min<byte[], byte[]>(table));
        AddRetained(retained, eager.Max<byte[], byte[]>(table));

        IEnumerable<Row<byte[], byte[]>> repeatable =
            eager.SelectForwardStartFrom<byte[], byte[]>(table, new byte[] { 1 }, true);
        AssertCompactRows(keys.OrderBy(static key => key, ByteComparer), repeatable, "Eager repeatable first pass.");
        AssertCompactRows(keys.OrderBy(static key => key, ByteComparer), repeatable, "Eager repeatable second pass.");

        eager.RemoveAllKeys(table, true);
        foreach ((Row<byte[], byte[]> row, byte[] expected) in retained)
            AssertSequenceEqual(expected, row.Value, "Eager traversal row was not materialized before recreation.");
    }

    private static void AddRetained(
        ICollection<(Row<byte[], byte[]> Row, byte[] Expected)> retained,
        Row<byte[], byte[]> row)
    {
        retained.Add((row, CompactValueFor(row.Key)));
    }

    private static void AssertEarlyDispose(IEnumerable<Row<byte[], byte[]>> rows, string message)
    {
        using IEnumerator<Row<byte[], byte[]>> iterator = rows.GetEnumerator();
        Assert(iterator.MoveNext(), message + " No first row.");
        Assert(iterator.Current.Exists, message + " First row does not exist.");
    }

    private static byte[][] CreateDeepTraversalKeys(byte[] prefix)
    {
        return new[]
        {
            new byte[] { 0x41, 0xFF },
            prefix,
            Append(prefix, 0x00),
            Append(prefix, 0x00, 0x00),
            Append(prefix, 0x00, 0xFF),
            Append(prefix, 0x01),
            Append(prefix, 0x01, 0x00),
            Append(prefix, 0xFF),
            new byte[] { 0x43 },
            new byte[] { 0xFF },
            new byte[] { 0xFF, 0x00 },
            new byte[] { 0xFF, 0xFF },
        };
    }

    private static byte[] Append(byte[] prefix, params byte[] suffix)
    {
        byte[] result = new byte[prefix.Length + suffix.Length];
        Buffer.BlockCopy(prefix, 0, result, 0, prefix.Length);
        Buffer.BlockCopy(suffix, 0, result, prefix.Length, suffix.Length);
        return result;
    }

    private static byte[] CompactValueFor(byte[] key)
    {
        return new[]
        {
            (byte)(key.Length >> 8),
            (byte)key.Length,
            key.Length == 0 ? (byte)0 : key[0],
            key.Length == 0 ? (byte)0 : key[key.Length - 1],
        };
    }

    private static void AssertCompactRows(
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
            AssertSequenceEqual(CompactValueFor(expected[i]), actual[i].Value, $"{message} value {i}.");
        }
    }

    private static void AssertCompactRow(byte[] expectedKey, Row<byte[], byte[]> actual, string message)
    {
        Assert(actual.Exists, message + " row does not exist.");
        AssertSequenceEqual(expectedKey, actual.Key, message + " key.");
        AssertSequenceEqual(CompactValueFor(expectedKey), actual.Value, message + " value.");
    }

    internal static void ChangeKeyPreservesDirtySiblingBranches()
    {
        foreach (bool storageOnDisk in new[] { false, true })
        {
            RunDirtySiblingChangeKey(storageOnDisk, insertedLeafCount: 1, sourceExists: true);
            RunDirtySiblingChangeKey(storageOnDisk, insertedLeafCount: 2, sourceExists: true);
            RunDirtySiblingChangeKey(storageOnDisk, insertedLeafCount: 2, sourceExists: false);
            RunMultipleBranchChangeKeys(storageOnDisk);
            RunDirtySiblingRollback(storageOnDisk);
            RunNestedParentRenameWithDirtySibling(storageOnDisk);
        }
    }

    internal static void MixedWriteEpochPreservesAllMutations()
    {
        string[] operationOrders = { "BCN", "BNC", "CBN", "CNB", "NBC", "NCB" };
        foreach (bool storageOnDisk in new[] { false, true })
        {
            foreach (string operationOrder in operationOrders)
                RunMixedWriteEpoch(storageOnDisk, operationOrder);
        }
    }

    private static void RunDirtySiblingChangeKey(bool storageOnDisk, int insertedLeafCount, bool sourceExists)
    {
        RunStorageScenario(
            $"dirty-sibling-{insertedLeafCount}-{sourceExists}",
            storageOnDisk,
            engine =>
            {
                using (var transaction = engine.GetTransaction())
                {
                    transaction.Insert(ContractTable, GeneratedKey(0), GeneratedValue(0));
                    transaction.Insert(ContractTable, GeneratedKey(1), GeneratedValue(1));
                    transaction.Commit();
                }

                using (var transaction = engine.GetTransaction())
                {
                    transaction.Insert(ContractTable, GeneratedKey(256), GeneratedValue(256));
                    if (insertedLeafCount == 2)
                        transaction.Insert(ContractTable, GeneratedKey(257), GeneratedValue(257));

                    byte[] sourceKey = GeneratedKey(sourceExists ? 1 : 100);
                    byte[] renamedKey = { 0x41, 0xFE, 0xED };
                    transaction.ChangeKey(ContractTable, sourceKey, renamedKey,
                        out byte[] pointer, out bool wasChanged);

                    AssertEqual(sourceExists, wasChanged, "ChangeKey result for a dirty sibling branch.");
                    Assert(sourceExists ? pointer is { Length: 8 } : pointer == null,
                        "ChangeKey returned an invalid value pointer.");
                    transaction.Commit();
                }
            },
            engine =>
            {
                byte[] renamedKey = { 0x41, 0xFE, 0xED };
                using var transaction = engine.GetTransaction();
                AssertSequenceEqual(GeneratedValue(256),
                    transaction.Select<byte[], byte[]>(ContractTable, GeneratedKey(256)).Value,
                    "First newly inserted sibling after ChangeKey.");
                if (insertedLeafCount == 2)
                {
                    AssertSequenceEqual(GeneratedValue(257),
                        transaction.Select<byte[], byte[]>(ContractTable, GeneratedKey(257)).Value,
                        "Second newly inserted sibling after ChangeKey.");
                }

                if (sourceExists)
                {
                    Assert(!transaction.Select<byte[], byte[]>(ContractTable, GeneratedKey(1)).Exists,
                        "Old key remained after ChangeKey.");
                    AssertSequenceEqual(GeneratedValue(1),
                        transaction.Select<byte[], byte[]>(ContractTable, renamedKey).Value,
                        "Renamed value after dirty-sibling ChangeKey.");
                }
                else
                {
                    AssertSequenceEqual(GeneratedValue(1),
                        transaction.Select<byte[], byte[]>(ContractTable, GeneratedKey(1)).Value,
                        "No-op ChangeKey changed an existing row.");
                    Assert(!transaction.Select<byte[], byte[]>(ContractTable, renamedKey).Exists,
                        "No-op ChangeKey created its destination key.");
                }

                AssertEqual((ulong)(2 + insertedLeafCount), transaction.Count(ContractTable),
                    "Row count after dirty-sibling ChangeKey.");
            });
    }

    private static void RunMultipleBranchChangeKeys(bool storageOnDisk)
    {
        byte[] firstRenamedKey = { 0x51, 0x01 };
        byte[] secondRenamedKey = { 0x61, 0x01 };
        RunStorageScenario(
            "multiple-branch-change-keys",
            storageOnDisk,
            engine =>
            {
                using (var transaction = engine.GetTransaction())
                {
                    transaction.Insert(ContractTable, GeneratedKey(0), GeneratedValue(0));
                    transaction.Insert(ContractTable, GeneratedKey(1), GeneratedValue(1));
                    transaction.Insert(ContractTable, GeneratedKey(256), GeneratedValue(256));
                    transaction.Insert(ContractTable, GeneratedKey(257), GeneratedValue(257));
                    transaction.Commit();
                }

                using (var transaction = engine.GetTransaction())
                {
                    transaction.Insert(ContractTable, GeneratedKey(512), GeneratedValue(512));
                    transaction.Insert(ContractTable, GeneratedKey(513), GeneratedValue(513));
                    transaction.ChangeKey(ContractTable, GeneratedKey(1), firstRenamedKey);
                    transaction.ChangeKey(ContractTable, GeneratedKey(257), secondRenamedKey);
                    transaction.Commit();
                }
            },
            engine =>
            {
                using var transaction = engine.GetTransaction();
                AssertSequenceEqual(GeneratedValue(512),
                    transaction.Select<byte[], byte[]>(ContractTable, GeneratedKey(512)).Value,
                    "First dirty leaf after multiple branch ChangeKey calls.");
                AssertSequenceEqual(GeneratedValue(513),
                    transaction.Select<byte[], byte[]>(ContractTable, GeneratedKey(513)).Value,
                    "Second dirty leaf after multiple branch ChangeKey calls.");
                Assert(!transaction.Select<byte[], byte[]>(ContractTable, GeneratedKey(1)).Exists,
                    "First old key remained after multiple branch ChangeKey calls.");
                Assert(!transaction.Select<byte[], byte[]>(ContractTable, GeneratedKey(257)).Exists,
                    "Second old key remained after multiple branch ChangeKey calls.");
                AssertSequenceEqual(GeneratedValue(1),
                    transaction.Select<byte[], byte[]>(ContractTable, firstRenamedKey).Value,
                    "First renamed value after multiple branch ChangeKey calls.");
                AssertSequenceEqual(GeneratedValue(257),
                    transaction.Select<byte[], byte[]>(ContractTable, secondRenamedKey).Value,
                    "Second renamed value after multiple branch ChangeKey calls.");
                AssertEqual(6UL, transaction.Count(ContractTable),
                    "Row count after multiple branch ChangeKey calls.");
            });
    }

    private static void RunDirtySiblingRollback(bool storageOnDisk)
    {
        RunStorageScenario(
            "dirty-sibling-rollback",
            storageOnDisk,
            engine =>
            {
                using (var transaction = engine.GetTransaction())
                {
                    transaction.Insert(ContractTable, GeneratedKey(0), GeneratedValue(0));
                    transaction.Insert(ContractTable, GeneratedKey(1), GeneratedValue(1));
                    transaction.Commit();
                }

                using (var transaction = engine.GetTransaction())
                {
                    transaction.Insert(ContractTable, GeneratedKey(256), GeneratedValue(256));
                    transaction.Insert(ContractTable, GeneratedKey(257), GeneratedValue(257));
                    transaction.ChangeKey(ContractTable, GeneratedKey(1), new byte[] { 0x41, 1 });
                    transaction.Rollback();
                }
            },
            engine =>
            {
                using var transaction = engine.GetTransaction();
                AssertSequenceEqual(GeneratedValue(0),
                    transaction.Select<byte[], byte[]>(ContractTable, GeneratedKey(0)).Value,
                    "First base row after rollback.");
                AssertSequenceEqual(GeneratedValue(1),
                    transaction.Select<byte[], byte[]>(ContractTable, GeneratedKey(1)).Value,
                    "Changed row after rollback.");
                Assert(!transaction.Select<byte[], byte[]>(ContractTable, GeneratedKey(256)).Exists,
                    "Rollback retained the first dirty sibling.");
                Assert(!transaction.Select<byte[], byte[]>(ContractTable, GeneratedKey(257)).Exists,
                    "Rollback retained the second dirty sibling.");
                Assert(!transaction.Select<byte[], byte[]>(ContractTable, new byte[] { 0x41, 1 }).Exists,
                    "Rollback retained the renamed key.");
                AssertEqual(2UL, transaction.Count(ContractTable), "Row count after dirty-sibling rollback.");
            });
    }

    private static void RunNestedParentRenameWithDirtySibling(bool storageOnDisk)
    {
        byte[] parentKey = GeneratedKey(1);
        byte[] renamedParentKey = { 0x41, 0xFA, 0xCE };
        RunStorageScenario(
            "dirty-sibling-nested-parent",
            storageOnDisk,
            engine =>
            {
                using (var transaction = engine.GetTransaction())
                {
                    transaction.Insert(ContractTable, GeneratedKey(0), GeneratedValue(0));
                    transaction.Insert(ContractTable, parentKey, GeneratedValue(1));
                    using NestedTable nested = transaction.InsertTable(ContractTable, parentKey, 0);
                    nested.Insert(new byte[] { 5 }, new byte[] { 55 });
                    using NestedTable deep = nested.GetTable(new byte[] { 5 }, 7);
                    deep.Insert(new byte[] { 6 }, new byte[] { 66 });
                    transaction.Commit();
                }

                using (var transaction = engine.GetTransaction())
                {
                    transaction.Insert(ContractTable, GeneratedKey(256), GeneratedValue(256));
                    transaction.Insert(ContractTable, GeneratedKey(257), GeneratedValue(257));
                    transaction.ChangeKey(ContractTable, parentKey, renamedParentKey);
                    transaction.Commit();
                }
            },
            engine =>
            {
                using var transaction = engine.GetTransaction();
                Assert(!transaction.Select<byte[], byte[]>(ContractTable, parentKey).Exists,
                    "Old nested parent remained after ChangeKey.");
                Assert(transaction.Select<byte[], byte[]>(ContractTable, renamedParentKey).Exists,
                    "Renamed nested parent is missing.");
                Assert(transaction.Select<byte[], byte[]>(ContractTable, GeneratedKey(256)).Exists,
                    "First dirty sibling was lost while renaming a nested parent.");
                Assert(transaction.Select<byte[], byte[]>(ContractTable, GeneratedKey(257)).Exists,
                    "Second dirty sibling was lost while renaming a nested parent.");
                using NestedTable nested = transaction.SelectTable(ContractTable, renamedParentKey, 0);
                Row<byte[], byte[]> nestedParent = nested.Select<byte[], byte[]>(new byte[] { 5 });
                Assert(nestedParent.Exists && nestedParent.Value is { Length: > 0 } && nestedParent.Value[0] == 55,
                    "Nested row after parent ChangeKey is missing its original payload.");
                using NestedTable deep = nested.GetTable(new byte[] { 5 }, 7);
                AssertSequenceEqual(new byte[] { 66 }, deep.Select<byte[], byte[]>(new byte[] { 6 }).Value,
                    "Recursive nested row after parent ChangeKey.");
            });
    }

    private static void RunMixedWriteEpoch(bool storageOnDisk, string operationOrder)
    {
        byte[] parentKey = { 0xF0, 0x01 };
        byte[] renamedKey = { 0x41, 0xFE, 0xED };
        RunStorageScenario(
            $"mixed-{operationOrder}",
            storageOnDisk,
            engine =>
            {
                using (var transaction = engine.GetTransaction())
                {
                    for (int i = 0; i < 256; i++)
                        transaction.Insert(ContractTable, GeneratedKey(i), GeneratedValue(i));
                    transaction.Insert(ContractTable, parentKey, new byte[] { 9 });
                    using NestedTable nested = transaction.InsertTable(ContractTable, parentKey, 0);
                    nested.Insert(new byte[] { 1 }, new byte[] { 11 });
                    nested.Insert<byte[], byte[]>(new byte[] { 2 }, null);
                    nested.Insert(new byte[] { 3 }, Array.Empty<byte>());
                    nested.Insert(new byte[] { 4 }, new byte[] { 44 });
                    using NestedTable deep = nested.GetTable(new byte[] { 4 }, 7);
                    deep.Insert(new byte[] { 5 }, new byte[] { 55 });
                    transaction.Commit();
                }

                using (var transaction = engine.GetTransaction())
                {
                    NestedTable nested = null;
                    NestedTable deep = null;
                    try
                    {
                        foreach (char operation in operationOrder)
                        {
                            switch (operation)
                            {
                                case 'B':
                                    for (int i = 0; i < 32; i++)
                                        transaction.Insert(ContractTable, GeneratedKey(i), UpdatedGeneratedValue(i));
                                    for (int i = 32; i < 64; i++)
                                        transaction.RemoveKey(ContractTable, GeneratedKey(i));
                                    for (int i = 256; i < 320; i++)
                                        transaction.Insert(ContractTable, GeneratedKey(i), GeneratedValue(i));
                                    break;
                                case 'C':
                                    transaction.ChangeKey(ContractTable, GeneratedKey(100), renamedKey);
                                    break;
                                case 'N':
                                    nested = transaction.InsertTable(ContractTable, parentKey, 0);
                                    nested.RemoveKey(new byte[] { 1 });
                                    nested.ChangeKey(new byte[] { 2 }, new byte[] { 0x12 });
                                    nested.Insert(new byte[] { 6 }, new byte[] { 66 });
                                    deep = nested.GetTable(new byte[] { 4 }, 7);
                                    deep.Insert(new byte[] { 7 }, new byte[] { 77 });
                                    break;
                                default:
                                    throw new InvalidOperationException($"Unknown mixed-epoch operation: {operation}.");
                            }
                        }

                        transaction.Commit();
                    }
                    finally
                    {
                        deep?.Dispose();
                        nested?.Dispose();
                    }
                }
            },
            engine => VerifyMixedWriteEpoch(engine, parentKey, renamedKey, operationOrder));
    }

    private static void VerifyMixedWriteEpoch(
        DBreezeEngine engine, byte[] parentKey, byte[] renamedKey, string operationOrder)
    {
        using var transaction = engine.GetTransaction();
        for (int i = 0; i < 32; i++)
        {
            AssertSequenceEqual(UpdatedGeneratedValue(i),
                transaction.Select<byte[], byte[]>(ContractTable, GeneratedKey(i)).Value,
                $"Updated row {i}, order {operationOrder}.");
        }
        for (int i = 32; i < 64; i++)
        {
            Assert(!transaction.Select<byte[], byte[]>(ContractTable, GeneratedKey(i)).Exists,
                $"Removed row {i}, order {operationOrder}.");
        }
        for (int i = 64; i < 256; i++)
        {
            if (i == 100)
                continue;
            AssertSequenceEqual(GeneratedValue(i),
                transaction.Select<byte[], byte[]>(ContractTable, GeneratedKey(i)).Value,
                $"Base row {i}, order {operationOrder}.");
        }
        for (int i = 256; i < 320; i++)
        {
            AssertSequenceEqual(GeneratedValue(i),
                transaction.Select<byte[], byte[]>(ContractTable, GeneratedKey(i)).Value,
                $"New row {i}, order {operationOrder}.");
        }

        Assert(!transaction.Select<byte[], byte[]>(ContractTable, GeneratedKey(100)).Exists,
            $"Old renamed key, order {operationOrder}.");
        AssertSequenceEqual(GeneratedValue(100),
            transaction.Select<byte[], byte[]>(ContractTable, renamedKey).Value,
            $"Renamed value, order {operationOrder}.");
        AssertEqual(289UL, transaction.Count(ContractTable), $"Mixed row count, order {operationOrder}.");

        using NestedTable nested = transaction.SelectTable(ContractTable, parentKey, 0);
        byte[][] expectedNestedKeys =
        {
            new byte[] { 3 }, new byte[] { 4 }, new byte[] { 6 }, new byte[] { 0x12 },
        };
        Row<byte[], byte[]>[] nestedRows = nested.SelectForward<byte[], byte[]>().ToArray();
        AssertEqual(expectedNestedKeys.Length, nestedRows.Length,
            $"Nested row count, order {operationOrder}.");
        for (int i = 0; i < expectedNestedKeys.Length; i++)
        {
            AssertSequenceEqual(expectedNestedKeys[i], nestedRows[i].Key,
                $"Nested key {i}, order {operationOrder}.");
        }
        Assert(nested.Select<byte[], byte[]>(new byte[] { 0x12 }).Exists,
            $"Renamed null nested row, order {operationOrder}.");
        Assert(nested.Select<byte[], byte[]>(new byte[] { 0x12 }).Value == null,
            $"Renamed null nested value, order {operationOrder}.");
        AssertSequenceEqual(new byte[] { 66 }, nested.Select<byte[], byte[]>(new byte[] { 6 }).Value,
            $"Inserted nested value, order {operationOrder}.");
        using NestedTable deep = nested.GetTable(new byte[] { 4 }, 7);
        AssertSequenceEqual(new byte[] { 55 }, deep.Select<byte[], byte[]>(new byte[] { 5 }).Value,
            $"Original recursive nested value, order {operationOrder}.");
        AssertSequenceEqual(new byte[] { 77 }, deep.Select<byte[], byte[]>(new byte[] { 7 }).Value,
            $"Inserted recursive nested value, order {operationOrder}.");
    }

    private static void RunStorageScenario(
        string name,
        bool storageOnDisk,
        Action<DBreezeEngine> arrangeAndAct,
        Action<DBreezeEngine> verify)
    {
        string folder = storageOnDisk
            ? Path.Combine(DatabaseTestRoot, name, Guid.NewGuid().ToString("N"))
            : null;
        DBreezeEngine engine = storageOnDisk ? new DBreezeEngine(folder) : CreateMemoryEngine();
        try
        {
            arrangeAndAct(engine);
            verify(engine);

            if (storageOnDisk)
            {
                engine.Dispose();
                engine = new DBreezeEngine(folder);
                verify(engine);
            }
        }
        finally
        {
            engine?.Dispose();
            if (folder != null && Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void RunEarlyDisposedNestedTables(bool storageOnDisk)
    {
        const string table = "nested-early-dispose";
        byte[] parent = { 0x10 };
        byte[] renamedParent = { 0x11 };
        byte[] recursiveParent = { 0x20 };
        byte[] removedParent = { 0x30 };
        byte[] replacementParent = { 0x40 };
        byte[] key1 = { 1 };
        byte[] key2 = { 2 };
        byte[] key3 = { 3 };
        byte[] key4 = { 4 };

        RunStorageScenario(
            nameof(EarlyDisposedNestedTablesFollowMasterTransaction) + (storageOnDisk ? "-disk" : "-memory"),
            storageOnDisk,
            engine =>
            {
                using (var transaction = engine.GetTransaction())
                {
                    NestedTable nested = transaction.InsertTable(table, parent, 0);
                    nested.Insert(key1, new byte[] { 11 });
                    nested.Insert(key2, new byte[] { 22 });
                    nested.Dispose();
                    nested.Dispose();
                    nested.CloseTable();
                    transaction.Commit();
                }

                AssertNestedValue(engine, table, parent, key1, new byte[] { 11 },
                    "Initial early-disposed insert.");

                using (var transaction = engine.GetTransaction())
                {
                    NestedTable first = transaction.InsertTable(table, parent, 0);
                    NestedTable second = transaction.InsertTable(table, parent, 0);
                    first.Insert(key1, new byte[] { 12 });
                    first.RemoveKey(key2);
                    first.Dispose();
                    second.Insert(key3, new byte[] { 33 });
                    second.Dispose();

                    NestedTable reopened = transaction.InsertTable(table, parent, 0);
                    AssertSequenceEqual(new byte[] { 12 }, reopened.Select<byte[], byte[]>(key1).Value,
                        "Reopened nested table lost the dirty value.");
                    reopened.Insert(key4, new byte[] { 44 });
                    reopened.Dispose();
                    transaction.Commit();
                }

                using (var transaction = engine.GetTransaction())
                {
                    using NestedTable nested = transaction.SelectTable(table, parent, 0);
                    AssertSequenceEqual(new byte[] { 12 }, nested.Select<byte[], byte[]>(key1).Value,
                        "Updated nested value.");
                    Assert(!nested.Select<byte[], byte[]>(key2).Exists, "Removed nested value is visible.");
                    AssertSequenceEqual(new byte[] { 33 }, nested.Select<byte[], byte[]>(key3).Value,
                        "Second handle insert.");
                    AssertSequenceEqual(new byte[] { 44 }, nested.Select<byte[], byte[]>(key4).Value,
                        "Reopened handle insert.");
                }

                using (var readerTransaction = engine.GetTransaction())
                using (NestedTable reader = readerTransaction.SelectTable(table, parent, 0))
                {
                    AssertSequenceEqual(new byte[] { 12 }, reader.Select<byte[], byte[]>(key1).Value,
                        "Committed reader initial value.");

                    using (var writerTransaction = engine.GetTransaction())
                    {
                        NestedTable writer = writerTransaction.InsertTable(table, parent, 0);
                        writer.Insert(key1, new byte[] { 13 });
                        writer.Dispose();

                        AssertSequenceEqual(new byte[] { 12 },
                            reader.Select<byte[], byte[]>(key1, true).Value,
                            "Committed reader observed the uncommitted writer view.");
                        writerTransaction.Commit();
                    }

                    AssertSequenceEqual(new byte[] { 13 },
                        reader.Select<byte[], byte[]>(key1, true).Value,
                        "Reader handle did not advance after writer commit.");
                }

                AssertNestedValue(engine, table, parent, key1, new byte[] { 13 },
                    "Writer value after committed reader release.");

                using (var transaction = engine.GetTransaction())
                {
                    NestedTable nested = transaction.InsertTable(table, parent, 0);
                    nested.Insert(new byte[] { 5 }, new byte[] { 55 });
                    nested.Dispose();
                    transaction.ChangeKey(table, parent, renamedParent);
                    transaction.Commit();
                }

                using (var transaction = engine.GetTransaction())
                {
                    Assert(!transaction.Select<byte[], byte[]>(table, parent).Exists,
                        "Old parent key remained after ChangeKey.");
                    using NestedTable nested = transaction.SelectTable(table, renamedParent, 0);
                    AssertSequenceEqual(new byte[] { 55 }, nested.Select<byte[], byte[]>(new byte[] { 5 }).Value,
                        "Deferred nested state was lost across ChangeKey.");
                }

                using (var transaction = engine.GetTransaction())
                {
                    NestedTable nested = transaction.InsertTable(table, renamedParent, 0);
                    nested.Insert(key1, new byte[] { 99 });
                    nested.Insert(new byte[] { 6 }, new byte[] { 66 });
                    nested.Dispose();
                    transaction.Rollback();
                }

                AssertNestedValue(engine, table, renamedParent, key1, new byte[] { 13 },
                    "Explicit rollback of an early-disposed table.");

                using (var transaction = engine.GetTransaction())
                {
                    NestedTable nested = transaction.InsertTable(table, renamedParent, 0);
                    nested.Insert(key1, new byte[] { 98 });
                    nested.Insert(new byte[] { 7 }, new byte[] { 77 });
                    nested.Dispose();
                }

                AssertNestedValue(engine, table, renamedParent, key1, new byte[] { 13 },
                    "Implicit rollback of an early-disposed table.");

                using (var transaction = engine.GetTransaction())
                {
                    NestedTable parentTable = transaction.InsertTable(table, recursiveParent, 0);
                    parentTable.Insert(key1, new byte[] { 1 });
                    NestedTable childTable = parentTable.GetTable(key1, 3);
                    childTable.Insert(key2, new byte[] { 2 });

                    // Deliberately close the parent handle first. The internal trie remains owned
                    // by the master coordinator until the recursive child has been committed.
                    parentTable.Dispose();
                    childTable.Dispose();
                    transaction.Commit();
                }

                using (var transaction = engine.GetTransaction())
                {
                    using NestedTable parentTable = transaction.SelectTable(table, recursiveParent, 0);
                    using NestedTable childTable = parentTable.GetTable(key1, 3);
                    AssertSequenceEqual(new byte[] { 2 }, childTable.Select<byte[], byte[]>(key2).Value,
                        "Recursive early-disposed nested value.");
                }

                using (var transaction = engine.GetTransaction())
                {
                    NestedTable nested = transaction.InsertTable(table, removedParent, 0);
                    nested.Insert(key1, new byte[] { 1 });
                    nested.Dispose();
                    transaction.RemoveKey(table, removedParent);
                    transaction.Commit();
                }

                using (var transaction = engine.GetTransaction())
                {
                    Assert(!transaction.Select<byte[], byte[]>(table, removedParent).Exists,
                        "Removed parent was resurrected by deferred nested cleanup.");
                }

                using (var transaction = engine.GetTransaction())
                {
                    NestedTable discarded = transaction.InsertTable(table, renamedParent, 0);
                    discarded.Insert(new byte[] { 8 }, new byte[] { 88 });

                    transaction.RemoveAllKeys(table, true);
                    discarded.Dispose();
                    discarded.Dispose();

                    NestedTable replacement = transaction.InsertTable(table, replacementParent, 0);
                    replacement.Insert(key1, new byte[] { 101 });
                    replacement.Dispose();
                    transaction.Commit();
                }
            },
            engine =>
            {
                using var transaction = engine.GetTransaction();
                AssertEqual(1UL, transaction.Count(table), "Row count after recreate with deferred tables.");
                using NestedTable nested = transaction.SelectTable(table, replacementParent, 0);
                AssertSequenceEqual(new byte[] { 101 }, nested.Select<byte[], byte[]>(key1).Value,
                    "Replacement nested table after recreate.");
            });
    }

    private static void RunDirectLTrieEarlyDispose()
    {
        string folder = Path.Combine(
            DatabaseTestRoot, nameof(RunDirectLTrieEarlyDispose), Guid.NewGuid().ToString("N"));
        string tablePath = Path.Combine(folder, "1");
        byte[] parent = { 0x51 };
        byte[] child = { 0x52 };
        byte[] committedValue = { 0x53 };

        DBreezeConfiguration configuration = null;
        LTrie trie = null;
        try
        {
            Directory.CreateDirectory(folder);
            configuration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK };
            trie = new LTrie(new StorageLayer(tablePath, new TrieSettings(), configuration));

            LTrieRow row = trie.GetKey(parent, false, true);
            NestedTable nested = trie.GetTable(row, ref parent, 0, trie, true, false);
            nested.Insert(child, committedValue);
            nested.Dispose();
            trie.SingleCommit();

            trie.Dispose();
            configuration.Dispose();
            trie = null;
            configuration = null;

            configuration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK };
            trie = new LTrie(new StorageLayer(tablePath, new TrieSettings(), configuration));
            row = trie.GetKey(parent, false, true);
            using (NestedTable reopened = trie.GetTable(row, ref parent, 0, trie, false, true))
            {
                AssertSequenceEqual(committedValue, reopened.Select<byte[], byte[]>(child).Value,
                    "Direct LTrie commit lost an early-disposed nested value.");
            }

            row = trie.GetKey(parent, false, true);
            nested = trie.GetTable(row, ref parent, 0, trie, true, false);
            nested.Insert(child, new byte[] { 0x99 });
            nested.Dispose();
            trie.SingleRollback();

            row = trie.GetKey(parent, false, true);
            using NestedTable rolledBack = trie.GetTable(row, ref parent, 0, trie, false, true);
            AssertSequenceEqual(committedValue, rolledBack.Select<byte[], byte[]>(child).Value,
                "Direct LTrie rollback retained an early-disposed mutation.");
        }
        finally
        {
            if (trie != null)
                trie.Dispose();
            if (configuration != null)
                configuration.Dispose();
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void AssertNestedValue(
        DBreezeEngine engine, string table, byte[] parent, byte[] key, byte[] expected, string message)
    {
        using var transaction = engine.GetTransaction();
        using NestedTable nested = transaction.SelectTable(table, parent, 0);
        AssertSequenceEqual(expected, nested.Select<byte[], byte[]>(key).Value, message);
    }

    private static byte[] GeneratedKey(int value) =>
        new[] { (byte)0x40, (byte)(value >> 8), (byte)value, (byte)(value * 17), (byte)(255 - value) };

    private static byte[] GeneratedValue(int value) =>
        new[] { (byte)0xA5, (byte)(value >> 8), (byte)value };

    private static byte[] UpdatedGeneratedValue(int value) =>
        new[] { (byte)0xCC, (byte)(value >> 8), (byte)value, (byte)(value * 31) };

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
            ? Path.Combine(DatabaseTestRoot, nameof(RemoveAllWithFileRecreationKeepsTableReusable), Guid.NewGuid().ToString("N"))
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

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
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
