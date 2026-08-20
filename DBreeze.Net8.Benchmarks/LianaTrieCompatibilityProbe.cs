using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DBreeze;
using DBreeze.DataTypes;

namespace DBreeze.Net8.Benchmarks;

internal static class LianaTrieCompatibilityProbe
{
    private const string MainTable = "liana-main";
    private const string RecreateTable = "liana-recreate";
    private const string RollbackTable = "liana-rollback";
    private const string RenameSource = "liana-rename-source";
    private const string RenameTarget = "liana-rename-target";

    private static readonly IComparer<byte[]> Comparer = new ByteArrayComparer();
    private static readonly byte[] ParentA = { 0xF0, 0x01 };
    private static readonly byte[] ParentB = { 0xF0, 0x02 };
    private static readonly byte[] RenamedKey = { 0x41, 0xFE, 0xED };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static int Run(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);
            switch (options.Action)
            {
                case "create":
                    Create(options.DatabasePath);
                    WriteManifest(options.OutputPath, BuildManifest(options.DatabasePath, extended: false));
                    break;
                case "verify-base":
                    Validate(options.DatabasePath, extended: false);
                    WriteManifest(options.OutputPath, BuildManifest(options.DatabasePath, extended: false));
                    break;
                case "extend":
                    Validate(options.DatabasePath, extended: false);
                    Extend(options.DatabasePath);
                    WriteManifest(options.OutputPath, BuildManifest(options.DatabasePath, extended: true));
                    break;
                case "extend-mixed":
                    Validate(options.DatabasePath, extended: false);
                    ExtendMixed(options.DatabasePath);
                    WriteManifest(options.OutputPath, BuildManifest(options.DatabasePath, extended: true));
                    break;
                case "verify-extended":
                    Validate(options.DatabasePath, extended: true);
                    WriteManifest(options.OutputPath, BuildManifest(options.DatabasePath, extended: true));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(options.Action), options.Action, "Unknown LianaTrie action.");
            }

            Console.WriteLine($"PASS liana-compat {options.Action}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
    }

    private static void Create(string databasePath)
    {
        if (Directory.Exists(databasePath))
            throw new IOException($"LianaTrie fixture already exists and will not be overwritten: {databasePath}");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)
            ?? throw new InvalidOperationException("Fixture path must have a parent directory."));

        using (var engine = new DBreezeEngine(databasePath))
        using (var transaction = engine.GetTransaction())
        {
            transaction.SynchronizeTables(MainTable, RecreateTable, RollbackTable, RenameSource);
            foreach (KeyValuePair<byte[], byte[]> item in BuildModel(extended: false))
                transaction.Insert(MainTable, item.Key, item.Value);

            transaction.Insert(RecreateTable, new byte[] { 1 }, new byte[] { 11 });
            transaction.Insert(RecreateTable, new byte[] { 2 }, new byte[] { 22 });
            transaction.Insert(RollbackTable, 1, 100);
            transaction.Insert(RenameSource, 1, 200);
            PopulateNested(transaction);
        }

        Validate(databasePath, extended: false);
    }

    private static void PopulateNested(DBreeze.Transactions.Transaction transaction)
    {
        using NestedTable a0 = transaction.InsertTable(MainTable, ParentA, 0);
        a0.Insert(new byte[] { 1 }, new byte[] { 11 });
        a0.Insert<byte[], byte[]>(new byte[] { 2 }, null);
        a0.Insert(new byte[] { 3 }, Array.Empty<byte>());
        a0.Insert(new byte[] { 4 }, new byte[] { 44 });
        using NestedTable deep = a0.GetTable(new byte[] { 4 }, 7);
        deep.Insert(new byte[] { 5 }, new byte[] { 55 });

        using NestedTable b3 = transaction.InsertTable(MainTable, ParentB, 3);
        b3.Insert(new byte[] { 9 }, new byte[] { 99 });
        transaction.Commit();
    }

    private static void Extend(string databasePath)
    {
        using (var engine = new DBreezeEngine(databasePath))
        {
            using (var transaction = engine.GetTransaction())
            {
                for (int i = 0; i < 32; i++)
                    transaction.Insert(MainTable, GeneratedKey(i), UpdatedValue(i));
                for (int i = 32; i < 64; i++)
                    transaction.RemoveKey(MainTable, GeneratedKey(i));
                for (int i = 256; i < 320; i++)
                    transaction.Insert(MainTable, GeneratedKey(i), ValueFor(GeneratedKey(i)));
                transaction.Commit();
            }

            using (var transaction = engine.GetTransaction())
            {
                transaction.ChangeKey(MainTable, GeneratedKey(100), RenamedKey);

                using NestedTable a0 = transaction.InsertTable(MainTable, ParentA, 0);
                a0.RemoveKey(new byte[] { 1 });
                a0.ChangeKey(new byte[] { 2 }, new byte[] { 0x12 });
                a0.Insert(new byte[] { 6 }, new byte[] { 66 });
                using NestedTable deep = a0.GetTable(new byte[] { 4 }, 7);
                deep.Insert(new byte[] { 7 }, new byte[] { 77 });

                transaction.Commit();
            }

            using (var transaction = engine.GetTransaction())
            {
                transaction.RemoveAllKeys(RecreateTable, true);
                transaction.Insert(RecreateTable, new byte[] { 3 }, new byte[] { 33 });
                transaction.Commit();
            }

            using (var transaction = engine.GetTransaction())
            {
                transaction.SynchronizeTables(MainTable, RollbackTable);
                transaction.Insert(MainTable, new byte[] { 0xDE, 0xAD }, new byte[] { 1 });
                transaction.Insert(RollbackTable, 2, 999);
                transaction.RemoveKey(MainTable, Array.Empty<byte>());
                transaction.Rollback();
            }

            engine.Scheme.RenameTable(RenameSource, RenameTarget);
        }
        Validate(databasePath, extended: true);
    }

    private static void ExtendMixed(string databasePath)
    {
        using (var engine = new DBreezeEngine(databasePath))
        {
            using (var transaction = engine.GetTransaction())
            {
                for (int i = 0; i < 32; i++)
                    transaction.Insert(MainTable, GeneratedKey(i), UpdatedValue(i));
                for (int i = 32; i < 64; i++)
                    transaction.RemoveKey(MainTable, GeneratedKey(i));
                for (int i = 256; i < 320; i++)
                    transaction.Insert(MainTable, GeneratedKey(i), ValueFor(GeneratedKey(i)));

                transaction.ChangeKey(MainTable, GeneratedKey(100), RenamedKey);

                using NestedTable a0 = transaction.InsertTable(MainTable, ParentA, 0);
                a0.RemoveKey(new byte[] { 1 });
                a0.ChangeKey(new byte[] { 2 }, new byte[] { 0x12 });
                a0.Insert(new byte[] { 6 }, new byte[] { 66 });
                using NestedTable deep = a0.GetTable(new byte[] { 4 }, 7);
                deep.Insert(new byte[] { 7 }, new byte[] { 77 });

                transaction.Commit();
            }

            using (var transaction = engine.GetTransaction())
            {
                transaction.RemoveAllKeys(RecreateTable, true);
                transaction.Insert(RecreateTable, new byte[] { 3 }, new byte[] { 33 });
                transaction.Commit();
            }

            using (var transaction = engine.GetTransaction())
            {
                transaction.SynchronizeTables(MainTable, RollbackTable);
                transaction.Insert(MainTable, new byte[] { 0xDE, 0xAD }, new byte[] { 1 });
                transaction.Insert(RollbackTable, 2, 999);
                transaction.RemoveKey(MainTable, Array.Empty<byte>());
                transaction.Rollback();
            }

            engine.Scheme.RenameTable(RenameSource, RenameTarget);
        }
        Validate(databasePath, extended: true);
    }

    private static Summary Validate(string databasePath, bool extended)
    {
        if (!Directory.Exists(databasePath))
            throw new DirectoryNotFoundException(databasePath);

        SortedDictionary<byte[], byte[]> expected = BuildModel(extended);
        var checksum = new StableChecksum();
        long rows = 0;

        using var engine = new DBreezeEngine(databasePath);
        foreach (bool lazyLoading in new[] { true, false })
        {
            using var transaction = engine.GetTransaction();
            transaction.ValuesLazyLoadingIsOn = lazyLoading;
            ValidateTraversal(transaction, expected, lazyLoading);
            ValidateDeepTraversal(transaction, expected, lazyLoading);

            if (lazyLoading)
            {
                foreach (KeyValuePair<byte[], byte[]> pair in expected)
                {
                    checksum.Add(pair.Key);
                    checksum.Add(pair.Value);
                    rows++;
                }
                ValidateNested(transaction, extended, checksum, ref rows);
            }
        }

        using (var transaction = engine.GetTransaction())
        {
            Row<byte[], byte[]>[] recreated = transaction.SelectForward<byte[], byte[]>(RecreateTable).ToArray();
            Ensure(recreated.Length == (extended ? 1 : 2), "Recreated table row count mismatch.");
            Ensure(recreated.All(static row => row.Exists), "Recreated table contains a missing row.");
            foreach (Row<byte[], byte[]> row in recreated)
            {
                checksum.Add(row.Key);
                checksum.Add(row.Value);
                rows++;
            }

            Row<int, int>[] rollback = transaction.SelectForward<int, int>(RollbackTable).ToArray();
            Ensure(rollback.Length == 1 && rollback[0].Key == 1 && rollback[0].Value == 100,
                "Rollback state mismatch.");
            checksum.Add(rollback[0].Key);
            checksum.Add(rollback[0].Value);
            rows++;

            string renameTable = extended ? RenameTarget : RenameSource;
            string absentRenameTable = extended ? RenameSource : RenameTarget;
            Row<int, int> renamed = transaction.Select<int, int>(renameTable, 1);
            Ensure(renamed.Exists && renamed.Value == 200, "Renamed table state mismatch.");
            Ensure(!transaction.Select<int, int>(absentRenameTable, 1).Exists,
                "Old rename endpoint unexpectedly contains data.");
            checksum.Add(renamed.Key);
            checksum.Add(renamed.Value);
            rows++;
        }

        return new Summary(rows, checksum.Value);
    }

    private static void ValidateTraversal(
        DBreeze.Transactions.Transaction transaction,
        SortedDictionary<byte[], byte[]> expected,
        bool lazyLoading)
    {
        byte[][] forward = expected.Keys.ToArray();
        byte[][] backward = forward.Reverse().ToArray();
        string mode = lazyLoading ? "lazy" : "eager";

        AssertRows(expected, forward, transaction.SelectForward<byte[], byte[]>(MainTable), $"Forward/{mode}");
        AssertRows(expected, backward, transaction.SelectBackward<byte[], byte[]>(MainTable), $"Backward/{mode}");

        byte[] pivot = { 0x40, 0x00, 0x00 };
        foreach (bool include in new[] { false, true })
        {
            AssertRows(expected, forward.Where(key => Compare(key, pivot) > 0 || include && Compare(key, pivot) == 0),
                transaction.SelectForwardStartFrom<byte[], byte[]>(MainTable, pivot, include),
                $"ForwardStart/{mode}/{include}");
            AssertRows(expected, backward.Where(key => Compare(key, pivot) < 0 || include && Compare(key, pivot) == 0),
                transaction.SelectBackwardStartFrom<byte[], byte[]>(MainTable, pivot, include),
                $"BackwardStart/{mode}/{include}");
        }

        byte[] rangeStart = { 0x40, 0x20, 0x00 };
        byte[] rangeStop = { 0x40, 0xD0, 0x00 };
        foreach (bool includeStart in new[] { false, true })
        foreach (bool includeStop in new[] { false, true })
        {
            AssertRows(expected, forward.Where(key => InForwardRange(key, rangeStart, includeStart, rangeStop, includeStop)),
                transaction.SelectForwardFromTo<byte[], byte[]>(MainTable, rangeStart, includeStart, rangeStop, includeStop),
                $"ForwardRange/{mode}/{includeStart}/{includeStop}");
            AssertRows(expected, backward.Where(key => InBackwardRange(key, rangeStop, includeStop, rangeStart, includeStart)),
                transaction.SelectBackwardFromTo<byte[], byte[]>(MainTable, rangeStop, includeStop, rangeStart, includeStart),
                $"BackwardRange/{mode}/{includeStart}/{includeStop}");
        }

        foreach (ulong skip in new[] { 0UL, 1UL, 17UL, (ulong)forward.Length, ulong.MaxValue })
        {
            int count = skip >= (ulong)forward.Length ? forward.Length : (int)skip;
            AssertRows(expected, forward.Skip(count),
                transaction.SelectForwardSkip<byte[], byte[]>(MainTable, skip), $"ForwardSkip/{mode}/{skip}");
            AssertRows(expected, backward.Skip(count),
                transaction.SelectBackwardSkip<byte[], byte[]>(MainTable, skip), $"BackwardSkip/{mode}/{skip}");
        }

        foreach (ulong skip in new[] { 0UL, 3UL, ulong.MaxValue })
        {
            byte[][] forwardCandidates = forward.Where(key => Compare(key, pivot) > 0).ToArray();
            byte[][] backwardCandidates = backward.Where(key => Compare(key, pivot) < 0).ToArray();
            AssertRows(expected, forwardCandidates.Skip(skip >= (ulong)forwardCandidates.Length ? forwardCandidates.Length : (int)skip),
                transaction.SelectForwardSkipFrom<byte[], byte[]>(MainTable, pivot, skip),
                $"ForwardSkipFrom/{mode}/{skip}");
            AssertRows(expected, backwardCandidates.Skip(skip >= (ulong)backwardCandidates.Length ? backwardCandidates.Length : (int)skip),
                transaction.SelectBackwardSkipFrom<byte[], byte[]>(MainTable, pivot, skip),
                $"BackwardSkipFrom/{mode}/{skip}");
        }

        byte[] prefix = { 0x40 };
        byte[][] prefixed = forward.Where(key => StartsWith(key, prefix)).ToArray();
        AssertRows(expected, prefixed, transaction.SelectForwardStartsWith<byte[], byte[]>(MainTable, prefix),
            $"ForwardPrefix/{mode}");
        AssertRows(expected, prefixed.Reverse(), transaction.SelectBackwardStartsWith<byte[], byte[]>(MainTable, prefix),
            $"BackwardPrefix/{mode}");

        byte[] closest = { 0x40, 0x80, 0x80, 0xFF };
        byte[] closestPrefix = LongestExistingPrefix(forward, closest);
        byte[][] closestRows = forward.Where(key => StartsWith(key, closestPrefix)).ToArray();
        AssertRows(expected, closestRows,
            transaction.SelectForwardStartsWithClosestToPrefix<byte[], byte[]>(MainTable, closest),
            $"ForwardClosest/{mode}");
        AssertRows(expected, closestRows.Reverse(),
            transaction.SelectBackwardStartsWithClosestToPrefix<byte[], byte[]>(MainTable, closest),
            $"BackwardClosest/{mode}");

        AssertRow(expected, forward[0], transaction.Min<byte[], byte[]>(MainTable), $"Min/{mode}");
        AssertRow(expected, backward[0], transaction.Max<byte[], byte[]>(MainTable), $"Max/{mode}");

        Row<byte[], byte[]>[] prefixTake = transaction.SelectForwardStartsWith<byte[], byte[]>(MainTable, prefix)
            .Take(3).ToArray();
        Ensure(prefixTake.Length == 3, $"Early enumeration/{mode} returned the wrong count.");
        AssertRows(expected, forward, transaction.SelectForward<byte[], byte[]>(MainTable),
            $"Forward after early disposal/{mode}");
    }

    private static void ValidateNested(
        DBreeze.Transactions.Transaction transaction,
        bool extended,
        StableChecksum checksum,
        ref long rows)
    {
        using (NestedTable a0 = transaction.SelectTable(MainTable, ParentA, 0))
        {
            byte[][] expectedKeys = extended
                ? new[] { new byte[] { 3 }, new byte[] { 4 }, new byte[] { 6 }, new byte[] { 0x12 } }
                : new[] { new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 }, new byte[] { 4 } };
            Row<byte[], byte[]>[] actual = a0.SelectForward<byte[], byte[]>().ToArray();
            Ensure(actual.Length == expectedKeys.Length,
                $"Nested A/0 row count mismatch: {actual.Length}; keys={String.Join(",", actual.Select(static row => Convert.ToHexString(row.Key)))}.");
            for (int i = 0; i < actual.Length; i++)
            {
                Ensure(actual[i].Key.SequenceEqual(expectedKeys[i]), "Nested A/0 key mismatch.");
                checksum.Add(actual[i].Key);
                checksum.Add(actual[i].Value);
                rows++;
            }

            using NestedTable deep = a0.GetTable(new byte[] { 4 }, 7);
            Row<byte[], byte[]>[] deepRows = deep.SelectForward<byte[], byte[]>().ToArray();
            Ensure(deepRows.Length == (extended ? 2 : 1), "Recursive nested row count mismatch.");
            foreach (Row<byte[], byte[]> row in deepRows)
            {
                checksum.Add(row.Key);
                checksum.Add(row.Value);
                rows++;
            }
        }

        using (NestedTable b3 = transaction.SelectTable(MainTable, ParentB, 3))
        {
            Row<byte[], byte[]> row = b3.Select<byte[], byte[]>(new byte[] { 9 });
            Ensure(row.Exists && row.Value.SequenceEqual(new byte[] { 99 }), "Nested B/3 mismatch.");
            checksum.Add(row.Key);
            checksum.Add(row.Value);
            rows++;
        }
    }

    private static void ValidateDeepTraversal(
        DBreeze.Transactions.Transaction transaction,
        SortedDictionary<byte[], byte[]> expected,
        bool lazyLoading)
    {
        byte[] prefix = Enumerable.Repeat((byte)0x62, 512).ToArray();
        byte[][] forward = expected.Keys.ToArray();
        byte[][] backward = forward.Reverse().ToArray();
        byte[] existingPivot = Append(prefix, 0x00);
        byte[] missingPivot = Append(prefix, 0x00, 0x80);
        byte[] rangeStop = Append(prefix, 0x01);
        byte[] closest = Append(prefix, 0x02, 0x77);
        string mode = lazyLoading ? "lazy" : "eager";

        foreach (bool include in new[] { false, true })
        {
            AssertRows(expected,
                forward.Where(key => Compare(key, missingPivot) > 0 || include && Compare(key, missingPivot) == 0),
                transaction.SelectForwardStartFrom<byte[], byte[]>(MainTable, missingPivot, include),
                $"DeepForwardStart/{mode}/{include}");
            AssertRows(expected,
                backward.Where(key => Compare(key, missingPivot) < 0 || include && Compare(key, missingPivot) == 0),
                transaction.SelectBackwardStartFrom<byte[], byte[]>(MainTable, missingPivot, include),
                $"DeepBackwardStart/{mode}/{include}");
        }

        foreach (bool includeStart in new[] { false, true })
        foreach (bool includeStop in new[] { false, true })
        {
            AssertRows(expected,
                forward.Where(key => InForwardRange(key, existingPivot, includeStart, rangeStop, includeStop)),
                transaction.SelectForwardFromTo<byte[], byte[]>(
                    MainTable, existingPivot, includeStart, rangeStop, includeStop),
                $"DeepForwardRange/{mode}/{includeStart}/{includeStop}");
            AssertRows(expected,
                backward.Where(key => InBackwardRange(key, rangeStop, includeStop, existingPivot, includeStart)),
                transaction.SelectBackwardFromTo<byte[], byte[]>(
                    MainTable, rangeStop, includeStop, existingPivot, includeStart),
                $"DeepBackwardRange/{mode}/{includeStart}/{includeStop}");
        }

        foreach (ulong skip in new[] { 0UL, 1UL, ulong.MaxValue })
        {
            byte[][] forwardCandidates = forward.Where(key => Compare(key, missingPivot) > 0).ToArray();
            byte[][] backwardCandidates = backward.Where(key => Compare(key, missingPivot) < 0).ToArray();
            AssertRows(expected,
                forwardCandidates.Skip(skip >= (ulong)forwardCandidates.Length ? forwardCandidates.Length : (int)skip),
                transaction.SelectForwardSkipFrom<byte[], byte[]>(MainTable, missingPivot, skip),
                $"DeepForwardSkipFrom/{mode}/{skip}");
            AssertRows(expected,
                backwardCandidates.Skip(skip >= (ulong)backwardCandidates.Length ? backwardCandidates.Length : (int)skip),
                transaction.SelectBackwardSkipFrom<byte[], byte[]>(MainTable, missingPivot, skip),
                $"DeepBackwardSkipFrom/{mode}/{skip}");
        }

        byte[][] prefixed = forward.Where(key => StartsWith(key, prefix)).ToArray();
        AssertRows(expected, prefixed,
            transaction.SelectForwardStartsWith<byte[], byte[]>(MainTable, prefix), $"DeepForwardPrefix/{mode}");
        AssertRows(expected, prefixed.Reverse(),
            transaction.SelectBackwardStartsWith<byte[], byte[]>(MainTable, prefix), $"DeepBackwardPrefix/{mode}");
        AssertRows(expected, prefixed,
            transaction.SelectForwardStartsWithClosestToPrefix<byte[], byte[]>(MainTable, closest),
            $"DeepForwardClosest/{mode}");
        AssertRows(expected, prefixed.Reverse(),
            transaction.SelectBackwardStartsWithClosestToPrefix<byte[], byte[]>(MainTable, closest),
            $"DeepBackwardClosest/{mode}");

        foreach (byte[] edgePrefix in new[] { new byte[] { 0xFF }, new byte[] { 0xFF, 0xFF } })
        {
            byte[][] edgeRows = forward.Where(key => StartsWith(key, edgePrefix)).ToArray();
            AssertRows(expected, edgeRows,
                transaction.SelectForwardStartsWith<byte[], byte[]>(MainTable, edgePrefix),
                $"EdgeForwardPrefix/{mode}/{Convert.ToHexString(edgePrefix)}");
            AssertRows(expected, edgeRows.Reverse(),
                transaction.SelectBackwardStartsWith<byte[], byte[]>(MainTable, edgePrefix),
                $"EdgeBackwardPrefix/{mode}/{Convert.ToHexString(edgePrefix)}");
        }

        IEnumerable<Row<byte[], byte[]>> repeatable =
            transaction.SelectForwardStartFrom<byte[], byte[]>(MainTable, missingPivot, true);
        byte[][] repeatableExpected = forward.Where(key => Compare(key, missingPivot) >= 0).ToArray();
        AssertRows(expected, repeatableExpected, repeatable, $"DeepRepeatable1/{mode}");
        AssertRows(expected, repeatableExpected, repeatable, $"DeepRepeatable2/{mode}");
        Ensure(transaction.SelectForwardStartsWith<byte[], byte[]>(MainTable, prefix).Take(1).Count() == 1,
            $"Deep early disposal/{mode} returned the wrong count.");

        AssertIndexOutOfRange(() =>
            transaction.SelectForwardStartFrom<byte[], byte[]>(MainTable, Array.Empty<byte>(), true).ToArray());
        AssertIndexOutOfRange(() =>
            transaction.SelectBackwardSkipFrom<byte[], byte[]>(MainTable, Array.Empty<byte>(), 0).ToArray());
        Ensure(!transaction.SelectForwardStartsWith<byte[], byte[]>(MainTable, Array.Empty<byte>()).Any(),
            $"StartsWith(empty)/{mode} must remain empty.");
        Ensure(!transaction.SelectBackwardStartsWithClosestToPrefix<byte[], byte[]>(MainTable, Array.Empty<byte>()).Any(),
            $"ClosestPrefix(empty)/{mode} must remain empty.");
    }

    private static SortedDictionary<byte[], byte[]> BuildModel(bool extended)
    {
        var model = new SortedDictionary<byte[], byte[]>(Comparer);
        byte[][] fixedKeys =
        {
            Array.Empty<byte>(), new byte[] { 0 }, new byte[] { 0, 0 }, new byte[] { 0, 255 },
            new byte[] { 1 }, new byte[] { 1, 0 }, new byte[] { 1, 0, 0 }, new byte[] { 1, 0, 255 },
            new byte[] { 1, 1 }, new byte[] { 2 }, new byte[] { 127 }, new byte[] { 128 },
            new byte[] { 254, 255 }, new byte[] { 255 }, new byte[] { 255, 0 },
            Enumerable.Repeat((byte)42, 4_096).ToArray(), ParentA, ParentB,
        };
        foreach (byte[] key in fixedKeys)
            model[key] = ValueFor(key);
        foreach (byte[] key in CreateDeepKeys())
            model[key] = ValueFor(key);
        for (int i = 0; i < 256; i++)
            model[GeneratedKey(i)] = ValueFor(GeneratedKey(i));

        byte[] nullKey = { 0xEE, 0 };
        byte[] emptyKey = { 0xEE, 1 };
        model[nullKey] = null;
        model[emptyKey] = Array.Empty<byte>();

        if (!extended)
            return model;

        for (int i = 0; i < 32; i++)
            model[GeneratedKey(i)] = UpdatedValue(i);
        for (int i = 32; i < 64; i++)
            model.Remove(GeneratedKey(i));
        for (int i = 256; i < 320; i++)
            model[GeneratedKey(i)] = ValueFor(GeneratedKey(i));
        byte[] oldKey = GeneratedKey(100);
        byte[] renamedValue = model[oldKey];
        model.Remove(oldKey);
        model[RenamedKey] = renamedValue;
        return model;
    }

    private static byte[] GeneratedKey(int value) =>
        new[] { (byte)0x40, (byte)(value >> 8), (byte)value, (byte)(value * 17), (byte)(255 - value) };

    private static byte[] ValueFor(byte[] key)
    {
        byte[] value = new byte[key.Length + 3];
        value[0] = (byte)key.Length;
        value[1] = 0xA5;
        value[2] = 0x5A;
        for (int i = 0; i < key.Length; i++)
            value[i + 3] = (byte)(key[key.Length - i - 1] ^ 0x3C);
        return value;
    }

    private static byte[] UpdatedValue(int value) =>
        new[] { (byte)0xCC, (byte)(value >> 8), (byte)value, (byte)(value * 31) };

    private static IEnumerable<byte[]> CreateDeepKeys()
    {
        byte[] prefix = Enumerable.Repeat((byte)0x62, 512).ToArray();
        yield return prefix;
        yield return Append(prefix, 0x00);
        yield return Append(prefix, 0x00, 0x00);
        yield return Append(prefix, 0x00, 0xFF);
        yield return Append(prefix, 0x01);
        yield return Append(prefix, 0x01, 0x00);
        yield return Append(prefix, 0xFF);
    }

    private static byte[] Append(byte[] prefix, params byte[] suffix)
    {
        byte[] result = new byte[prefix.Length + suffix.Length];
        Buffer.BlockCopy(prefix, 0, result, 0, prefix.Length);
        Buffer.BlockCopy(suffix, 0, result, prefix.Length, suffix.Length);
        return result;
    }

    private static void AssertRows(
        SortedDictionary<byte[], byte[]> expected,
        IEnumerable<byte[]> expectedKeys,
        IEnumerable<Row<byte[], byte[]>> actualRows,
        string message)
    {
        byte[][] keys = expectedKeys.ToArray();
        Row<byte[], byte[]>[] rows = actualRows.ToArray();
        if (rows.Length != keys.Length)
        {
            byte[][] actualKeys = rows.Select(static row => row.Key).ToArray();
            string missing = String.Join(",", keys.Where(key => !actualKeys.Any(actual => Compare(actual, key) == 0))
                .Take(8).Select(static key => Convert.ToHexString(key)));
            string unexpected = String.Join(",", actualKeys.Where(key => !keys.Any(expectedKey => Compare(expectedKey, key) == 0))
                .Take(8).Select(static key => Convert.ToHexString(key)));
            throw new InvalidDataException(
                $"{message}: count {rows.Length}, expected {keys.Length}; missing={missing}; unexpected={unexpected}.");
        }
        for (int i = 0; i < keys.Length; i++)
            AssertRow(expected, keys[i], rows[i], $"{message}/{i}");
    }

    private static void AssertRow(
        SortedDictionary<byte[], byte[]> expected,
        byte[] expectedKey,
        Row<byte[], byte[]> actual,
        string message)
    {
        Ensure(actual.Exists, message + ": row does not exist.");
        Ensure(actual.Key.SequenceEqual(expectedKey), message + ": key mismatch.");
        if (IsNestedParent(expectedKey))
        {
            Ensure(actual.Value != null && actual.Value.Length >= 64,
                message + ": nested parent does not contain its root area.");
            return;
        }
        byte[] expectedValue = expected[expectedKey];
        Ensure(expectedValue == null ? actual.Value == null : actual.Value != null && actual.Value.SequenceEqual(expectedValue),
            message + ": value mismatch.");
    }

    private static bool IsNestedParent(byte[] key) =>
        key.SequenceEqual(ParentA) || key.SequenceEqual(ParentB);

    private static bool InForwardRange(byte[] key, byte[] start, bool includeStart, byte[] stop, bool includeStop) =>
        (Compare(key, start) > 0 || includeStart && Compare(key, start) == 0) &&
        (Compare(key, stop) < 0 || includeStop && Compare(key, stop) == 0);

    private static bool InBackwardRange(byte[] key, byte[] start, bool includeStart, byte[] stop, bool includeStop) =>
        (Compare(key, start) < 0 || includeStart && Compare(key, start) == 0) &&
        (Compare(key, stop) > 0 || includeStop && Compare(key, stop) == 0);

    private static bool StartsWith(byte[] value, byte[] prefix) =>
        value.Length >= prefix.Length && value.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    private static byte[] LongestExistingPrefix(IEnumerable<byte[]> keys, byte[] candidate)
    {
        for (int length = candidate.Length; length >= 0; length--)
        {
            byte[] prefix = candidate.AsSpan(0, length).ToArray();
            if (keys.Any(key => StartsWith(key, prefix)))
                return prefix;
        }
        return Array.Empty<byte>();
    }

    private static int Compare(byte[] left, byte[] right) => left.AsSpan().SequenceCompareTo(right);

    private static Manifest BuildManifest(string databasePath, bool extended)
    {
        Summary summary = Validate(databasePath, extended);
        ManifestFile[] files = Directory.EnumerateFiles(databasePath, "*", SearchOption.AllDirectories)
            .Select(path => new ManifestFile
            {
                Path = Path.GetRelativePath(databasePath, path).Replace(Path.DirectorySeparatorChar, '/'),
                Length = new FileInfo(path).Length,
                Sha256 = ComputeSha256(path),
            })
            .OrderBy(static file => file.Path, StringComparer.Ordinal)
            .ToArray();
        return new Manifest
        {
            State = extended ? "extended" : "base",
            DatabasePath = databasePath,
            DBreezeAssemblyVersion = typeof(DBreezeEngine).Assembly.GetName().Version?.ToString() ?? String.Empty,
            RowCount = summary.RowCount,
            Checksum = summary.Checksum,
            TotalBytes = files.Sum(static file => file.Length),
            Files = files,
        };
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void WriteManifest(string path, Manifest manifest)
    {
        if (File.Exists(path))
            throw new IOException($"Manifest already exists and will not be overwritten: {path}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Manifest path must have a parent directory."));
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidDataException(message);
    }

    private static void AssertIndexOutOfRange(Action action)
    {
        try
        {
            action();
        }
        catch (IndexOutOfRangeException)
        {
            return;
        }

        throw new InvalidDataException("Expected IndexOutOfRangeException.");
    }

    private sealed class Options
    {
        internal string Action { get; private set; }
        internal string DatabasePath { get; private set; }
        internal string OutputPath { get; private set; }

        internal static Options Parse(string[] args)
        {
            var options = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--liana-compat":
                        options.Action = ReadValue(args, ref i, "--liana-compat").ToLowerInvariant();
                        break;
                    case "--database":
                        options.DatabasePath = Path.GetFullPath(ReadValue(args, ref i, "--database"));
                        break;
                    case "--output":
                        options.OutputPath = Path.GetFullPath(ReadValue(args, ref i, "--output"));
                        break;
                    default:
                        throw new ArgumentException($"Unknown LianaTrie compatibility option: {args[i]}", nameof(args));
                }
            }
            if (String.IsNullOrEmpty(options.Action) || String.IsNullOrEmpty(options.DatabasePath) ||
                String.IsNullOrEmpty(options.OutputPath))
            {
                throw new ArgumentException("--liana-compat requires an action, --database and --output.", nameof(args));
            }
            return options;
        }

        private static string ReadValue(string[] args, ref int index, string option)
        {
            if (++index >= args.Length || String.IsNullOrWhiteSpace(args[index]))
                throw new ArgumentException(option + " requires a value.", nameof(args));
            return args[index];
        }
    }

    private sealed class StableChecksum
    {
        private ulong _value = 14695981039346656037UL;
        internal long Value => unchecked((long)_value);
        internal void Add(int value) => Add(unchecked((ulong)(uint)value));
        internal void Add(byte[] value)
        {
            if (value == null)
            {
                Add(UInt64.MaxValue);
                return;
            }
            Add((ulong)value.Length);
            foreach (byte item in value)
                Add(item);
        }
        private void Add(ulong value)
        {
            for (int shift = 0; shift < 64; shift += 8)
            {
                _value ^= (byte)(value >> shift);
                _value *= 1099511628211UL;
            }
        }
    }

    private readonly record struct Summary(long RowCount, long Checksum);

    private sealed class Manifest
    {
        public string State { get; set; }
        public string DatabasePath { get; set; }
        public string DBreezeAssemblyVersion { get; set; }
        public long RowCount { get; set; }
        public long Checksum { get; set; }
        public long TotalBytes { get; set; }
        public ManifestFile[] Files { get; set; } = Array.Empty<ManifestFile>();
    }

    private sealed class ManifestFile
    {
        public string Path { get; set; }
        public long Length { get; set; }
        public string Sha256 { get; set; }
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
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
