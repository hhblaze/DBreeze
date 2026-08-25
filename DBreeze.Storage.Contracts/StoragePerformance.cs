using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using DBreeze;
using DBreeze.DataTypes;

internal static class StoragePerformance
{
    private const string Table = "perf";

    private static HashSet<string> _scenarioFilter;

    internal static void Run(string root, int records, string scenarioFilter)
    {
        if (records < 1000 || records > 1000000)
            throw new ArgumentOutOfRangeException("records", "Performance records must be in [1000, 1000000].");
        Directory.CreateDirectory(root);
        _scenarioFilter = ParseFilter(scenarioFilter);
        Console.WriteLine("PERF_HEADER\ttarget\tscenario\telapsed_ms\tops\tchecksum\tdatabase_bytes\tallocated_bytes");
        if (Selected("StoragePointExisting")) StoragePointRead(root, records, false);
        if (Selected("StoragePointMissing")) StoragePointRead(root, records, true);
        if (Selected("PointExisting")) PointRead(root, records, false);
        if (Selected("PointMissing")) PointRead(root, records, true);
        if (Selected("ForwardScan")) Scan(root, records, "ForwardScan");
        if (Selected("RangeMiddle")) Scan(root, records, "RangeMiddle");
        if (Selected("PrefixScan")) Scan(root, records, "PrefixScan");
        if (Selected("SkipNinetyPercent")) Scan(root, records, "SkipNinetyPercent");
        if (Selected("SequentialInsert")) SequentialInsert(root, records, false);
        if (Selected("RandomInsert")) SequentialInsert(root, records, true);
        if (Selected("Update")) Update(root, records);
        if (Selected("Rollback")) Rollback(root, records);
        if (Selected("RandomKeySorter")) RandomKeySorter(root, records);
        if (Selected("Restore")) Restore(root, records);
    }

    private static void StoragePointRead(string root, int records, bool missing)
    {
        string scenario = missing ? "StoragePointMissing" : "StoragePointExisting";
        string path = ScenarioRoot(root, scenario);
        using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
        {
            var storage = new DBreeze.Storage.StorageLayer(Path.Combine(path, "1"),
                new DBreeze.Storage.TrieSettings(), configuration);
            try
            {
                byte[] value = Value(71);
                long offset = StorageTestSupport.DecodePointer(storage.Table_WriteToTheEnd(value));
                storage.Commit();
                // Exact EOF is the only logically missing offset that a83424e handles without its
                // historical negative-array-length overflow; beyond-EOF is covered by correctness tests.
                long readOffset = missing ? offset + value.Length : offset;
                Measure(path, scenario, records, delegate
                {
                    long checksum = 0;
                    int count = 0;
                    for (int index = 0; index < records; index++)
                    {
                        byte[] read = storage.Table_Read(true, readOffset, value.Length);
                        count += read.Length;
                        if (read.Length != 0)
                            checksum += read[0];
                    }
                    return new PerfValue(count, checksum);
                });
            }
            finally
            {
                storage.Table_Dispose();
            }
        }
    }

    private static HashSet<string> ParseFilter(string value)
    {
        if (String.IsNullOrWhiteSpace(value))
            return null;
        return new HashSet<string>(value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool Selected(string scenario)
    {
        return _scenarioFilter == null || _scenarioFilter.Contains(scenario);
    }

    private static void PointRead(string root, int records, bool missing)
    {
        string scenario = missing ? "PointMissing" : "PointExisting";
        string path = ScenarioRoot(root, scenario);
        CreateIntegerFixture(path, records);
        using (DBreezeEngine engine = CreateEngine(path))
        using (DBreeze.Transactions.Transaction transaction = engine.GetTransaction())
        {
            Measure(path, scenario, records, delegate
            {
                long checksum = 0;
                int found = 0;
                for (int index = 0; index < records; index++)
                {
                    int key = missing ? checked(records + index + 1) : index;
                    Row<int, byte[]> row = transaction.Select<int, byte[]>(Table, key);
                    if (row.Exists)
                    {
                        found++;
                        checksum += row.Value[0];
                    }
                }
                return new PerfValue(found, checksum);
            });
        }
    }

    private static void Scan(string root, int records, string scenario)
    {
        string path = ScenarioRoot(root, scenario);
        if (scenario == "PrefixScan")
            CreateStringFixture(path, records);
        else
            CreateIntegerFixture(path, records);

        using (DBreezeEngine engine = CreateEngine(path))
        using (DBreeze.Transactions.Transaction transaction = engine.GetTransaction())
        {
            Measure(path, scenario, records, delegate
            {
                int count = 0;
                long checksum = 0;
                if (scenario == "ForwardScan")
                {
                    foreach (Row<int, byte[]> row in transaction.SelectForward<int, byte[]>(Table))
                    {
                        count++;
                        checksum += row.Key + row.Value[0];
                    }
                }
                else if (scenario == "RangeMiddle")
                {
                    foreach (Row<int, byte[]> row in transaction.SelectForwardFromTo<int, byte[]>(
                        Table, records / 4, true, records * 3 / 4, false))
                    {
                        count++;
                        checksum += row.Key + row.Value[0];
                    }
                }
                else if (scenario == "PrefixScan")
                {
                    foreach (Row<string, int> row in transaction.SelectForwardStartsWith<string, int>(Table, "prefix/03/"))
                    {
                        count++;
                        checksum += row.Value;
                    }
                }
                else
                {
                    foreach (Row<int, byte[]> row in transaction.SelectForwardSkip<int, byte[]>(Table,
                        checked((ulong)(records * 9 / 10))))
                    {
                        count++;
                        checksum += row.Key + row.Value[0];
                    }
                }
                return new PerfValue(count, checksum);
            });
        }
    }

    private static void SequentialInsert(string root, int records, bool random)
    {
        string scenario = random ? "RandomInsert" : "SequentialInsert";
        string path = ScenarioRoot(root, scenario);
        int[] order = CreateOrder(records, random ? 3101 : 0);
        using (DBreezeEngine engine = CreateEngine(path))
        {
            Measure(path, scenario, records, delegate
            {
                long checksum = 0;
                using (DBreeze.Transactions.Transaction transaction = engine.GetTransaction())
                {
                    for (int index = 0; index < order.Length; index++)
                    {
                        int key = order[index];
                        transaction.Insert<int, byte[]>(Table, key, Value(key));
                        checksum += key;
                    }
                    transaction.Commit();
                }
                return new PerfValue(records, checksum);
            });
        }
    }

    private static void Update(string root, int records)
    {
        string scenario = "Update";
        string path = ScenarioRoot(root, scenario);
        CreateIntegerFixture(path, records);
        using (DBreezeEngine engine = CreateEngine(path))
        {
            Measure(path, scenario, records, delegate
            {
                long checksum = 0;
                using (DBreeze.Transactions.Transaction transaction = engine.GetTransaction())
                {
                    for (int key = 0; key < records; key++)
                    {
                        byte[] value = Value(key + 17);
                        transaction.Insert<int, byte[]>(Table, key, value);
                        checksum += value[0];
                    }
                    transaction.Commit();
                }
                return new PerfValue(records, checksum);
            });
        }
    }

    private static void Rollback(string root, int records)
    {
        string scenario = "Rollback";
        string path = ScenarioRoot(root, scenario);
        CreateIntegerFixture(path, records);
        using (DBreezeEngine engine = CreateEngine(path))
        {
            Measure(path, scenario, records, delegate
            {
                using (DBreeze.Transactions.Transaction transaction = engine.GetTransaction())
                {
                    for (int key = 0; key < records; key++)
                        transaction.Insert<int, byte[]>(Table, key, Value(key + 31));
                    transaction.Rollback();
                }
                using (DBreeze.Transactions.Transaction verify = engine.GetTransaction())
                {
                    Row<int, byte[]> row = verify.Select<int, byte[]>(Table, records / 2);
                    return new PerfValue(records, row.Value[0]);
                }
            });
        }
    }

    private static void RandomKeySorter(string root, int records)
    {
        string scenario = "RandomKeySorter";
        string path = ScenarioRoot(root, scenario);
        int[] order = CreateOrder(records, 3201);
        using (DBreezeEngine engine = CreateEngine(path))
        {
            Measure(path, scenario, records, delegate
            {
                long checksum = 0;
                using (DBreeze.Transactions.Transaction transaction = engine.GetTransaction())
                {
                    for (int index = 0; index < records; index++)
                    {
                        int key = order[index];
                        transaction.RandomKeySorter.Insert<int, byte[]>(Table, key, Value(key));
                        checksum += key;
                    }
                    transaction.RandomKeySorter.Flush(Table);
                    transaction.Commit();
                }
                return new PerfValue(records, checksum);
            });
        }
    }

    private static void Restore(string root, int records)
    {
        string scenario = "Restore";
        string path = ScenarioRoot(root, scenario);
        string source = Path.Combine(path, "source");
        string backup = Path.Combine(path, "backup");
        string destination = Path.Combine(path, "destination");
        Directory.CreateDirectory(source);
        using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
        {
            configuration.Backup.BackupFolderName = backup;
            using (DBreezeEngine engine = new DBreezeEngine(ConfigurePath(configuration, source)))
            using (DBreeze.Transactions.Transaction transaction = engine.GetTransaction())
            {
                for (int key = 0; key < records; key++)
                    transaction.Insert<int, byte[]>(Table, key, Value(key));
                transaction.Commit();
            }
        }
        Measure(path, scenario, records, delegate
        {
            DBreeze.Storage.BackupRestorer restorer = StorageTestSupport.CreateRestorer(backup, destination);
            // a83424e invokes the event without a null-check; keeping the worker identical for both sides
            // avoids benchmarking an unrelated historical API bug.
            restorer.OnRestore += delegate { };
            restorer.StartRestoration();
            return new PerfValue(records, StorageTestSupport.DatabaseSize(destination));
        });
    }

    private static void CreateIntegerFixture(string path, int records)
    {
        using (DBreezeEngine engine = CreateEngine(path))
        using (DBreeze.Transactions.Transaction transaction = engine.GetTransaction())
        {
            for (int key = 0; key < records; key++)
                transaction.Insert<int, byte[]>(Table, key, Value(key));
            transaction.Commit();
        }
    }

    private static void CreateStringFixture(string path, int records)
    {
        using (DBreezeEngine engine = CreateEngine(path))
        using (DBreeze.Transactions.Transaction transaction = engine.GetTransaction())
        {
            for (int key = 0; key < records; key++)
                transaction.Insert<string, int>(Table, "prefix/" + (key % 10).ToString("D2") + "/" + key.ToString("D8"), key);
            transaction.Commit();
        }
    }

    private static DBreezeEngine CreateEngine(string path)
    {
        DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration();
        return new DBreezeEngine(ConfigurePath(configuration, path));
    }

    private static DBreezeConfiguration ConfigurePath(DBreezeConfiguration configuration, string path)
    {
        configuration.DBreezeDataFolderName = path;
        return configuration;
    }

    private static string ScenarioRoot(string root, string scenario)
    {
        string result = Path.Combine(root, scenario);
        if (Directory.Exists(result))
            Directory.Delete(result, true);
        Directory.CreateDirectory(result);
        return result;
    }

    private static void Measure(string path, string scenario, int operations, Func<PerfValue> action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore = AllocatedBytes();
        Stopwatch stopwatch = Stopwatch.StartNew();
        PerfValue result = action();
        stopwatch.Stop();
        long allocatedAfter = AllocatedBytes();
        long allocated = allocatedBefore < 0 || allocatedAfter < 0 ? -1 : allocatedAfter - allocatedBefore;
        Console.WriteLine("PERF\t" + StorageTestSupport.TargetName + "\t" + scenario + "\t" +
            stopwatch.Elapsed.TotalMilliseconds.ToString("F4", CultureInfo.InvariantCulture) + "\t" +
            operations + "\t" + result.Count + ":" + result.Checksum + "\t" +
            StorageTestSupport.DatabaseSize(path) + "\t" + allocated);
    }

    private static long AllocatedBytes()
    {
#if ALLOC_COUNTER
        return GC.GetAllocatedBytesForCurrentThread();
#else
        return -1;
#endif
    }

    private static byte[] Value(int key)
    {
        byte[] result = new byte[64];
        byte value = (byte)(key * 37);
        for (int index = 0; index < result.Length; index++)
            result[index] = value;
        return result;
    }

    private static int[] CreateOrder(int records, int seed)
    {
        int[] result = new int[records];
        for (int index = 0; index < records; index++)
            result[index] = index;
        if (seed == 0)
            return result;
        var random = new Random(seed);
        for (int index = result.Length - 1; index > 0; index--)
        {
            int other = random.Next(index + 1);
            int value = result[index];
            result[index] = result[other];
            result[other] = value;
        }
        return result;
    }

    private struct PerfValue
    {
        internal readonly int Count;
        internal readonly long Checksum;

        internal PerfValue(int count, long checksum)
        {
            Count = count;
            Checksum = checksum;
        }
    }
}
