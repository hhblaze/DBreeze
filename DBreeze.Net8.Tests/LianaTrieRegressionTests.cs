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
            ? Path.Combine(Path.GetTempPath(), "DBreeze.Net8.Tests", name, Guid.NewGuid().ToString("N"))
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
