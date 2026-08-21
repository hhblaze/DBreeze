using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using DBreeze;
using DBreeze.DataTypes;
using DBreeze.Objects;
using DBreeze.Storage;
using DBreeze.Utils;

namespace DBreeze.Net8.Benchmarks;

internal static class AuditPerformanceSuite
{
    private const string MainTable = "audit-main";

    internal static AuditPerformanceReport Run(AuditWorkerOptions options)
    {
        int large = options.Profile == AuditProfile.Full ? options.MaxRecords : Math.Min(options.MaxRecords, 1_000);
        int medium = Math.Min(large, 100_000);
        int small = Math.Min(large, 10_000);
        int parallelRecords = Math.Min(large, 250_000);
        var report = new AuditPerformanceReport
        {
            Variant = options.Variant,
            Round = options.Round,
            Profile = options.Profile.ToString().ToLowerInvariant(),
            MaxRecords = options.MaxRecords,
            Runtime = RuntimeInformation.FrameworkDescription + " / " + Environment.Version,
            ServerGc = GCSettings.IsServerGC,
        };

        var definitions = new List<AuditScenarioDefinition>
        {
            Scenario("Memory", "SequentialInsert64", 1, large, large, PrepareMemorySequentialInsert),
            Scenario("Memory", "PointRead64", 1, large, large, PrepareMemoryPointRead),
            Scenario("Disk.Write", "SequentialInsertNull", 1, large, large, PrepareDiskSequentialNull),
            Scenario("Disk.Write", "SequentialInsert64", 1, large, large, PrepareDiskSequential64),
            Scenario("Disk.Write", "RandomInsert64", 1, medium, medium, PrepareDiskRandomInsert),
            Scenario("Disk.Write", "RandomKeySorterBatch", 1, medium, medium, PrepareRandomKeySorterBatch),
            Scenario("Disk.Write", "Update4K", 1, medium, medium, PrepareDiskUpdate4K),
            Scenario("Disk.Write", "RemoveCommit", 1, medium, medium, PrepareDiskRemove),
            Scenario("Disk.Write", "InsertRollback", 1, medium, medium, PrepareDiskRollback),
            Scenario("Disk.Read", "PointExisting64", 1, large, large, PrepareDiskPointExisting),
            Scenario("Disk.Read", "PointMissing", 1, large, large, PrepareDiskPointMissing),
            Scenario("Disk.Read", "ForwardLazyKeys", 1, large, large, PrepareDiskForwardLazy),
            Scenario("Disk.Read", "ForwardEagerValues", 1, large, large, PrepareDiskForwardEager),
            Scenario("Disk.Read", "RangeMiddle", 1, large, Math.Max(1, large / 2), PrepareDiskRange),
            Scenario("Disk.Read", "SkipNinetyPercent", 1, large, large, PrepareDiskSkip),
            Scenario("Disk.Read", "PrefixScan", 1, medium, medium, PreparePrefixScan),
            Scenario("Disk.Read", "MultiSelectTwoTables", 1, medium, medium, PrepareMultiSelect),
            Scenario("Disk.Advanced", "PartialUpdate4K", 1, small, small, PreparePartialUpdate),
            Scenario("Disk.Advanced", "FixedDataBlock4K", 1, small, small, PrepareFixedDataBlock),
            Scenario("Disk.Advanced", "NestedCrud", 1, small, small, PrepareNestedCrud),
            Scenario("Disk.Advanced", "DictionaryReplace", 1, Math.Min(small, 2_000), Math.Min(small, 2_000), PrepareCollections),
            Scenario("Objects", "InsertAndRead", 1, Math.Min(small, 2_000), Math.Min(small, 2_000) * 2L, PrepareObjects),
            Scenario("TextSearch", "IndexAndCommit", 1, small, small, PrepareTextIndex),
            Scenario("TextSearch", "PrefixQuery", 1, small, small, PrepareTextQuery),
            Scenario("Resources", "InsertSelect", 1, small, small * 2L, PrepareResources),
            Scenario("Scheme", "CreateRenameDelete", 1, Math.Min(small, 250), Math.Min(small, 250) * 3L, PrepareSchemeLifecycle),
            Scenario("Storage", "BackupRestore", 1, Math.Min(small, 10_000), Math.Min(small, 10_000), PrepareBackupRestore),
            Scenario("Utils", "IntKeyRoundTrip", 1, large, large, PrepareUtilsConversion),
        };

        foreach (int workers in WorkerCounts())
        {
            definitions.Add(Scenario("Parallel.Read", "SharedTablePointRead", workers, parallelRecords,
                parallelRecords, (path, records) => PrepareParallelRead(path, records, workers)));
            definitions.Add(Scenario("Parallel.Write", "SeparateTables", workers, parallelRecords,
                parallelRecords, (path, records) => PrepareParallelWrite(path, records, workers, sameTable: false)));
            definitions.Add(Scenario("Parallel.Write", "SharedTable", workers, parallelRecords,
                parallelRecords, (path, records) => PrepareParallelWrite(path, records, workers, sameTable: true)));
            definitions.Add(Scenario("Parallel.Write", "OverlappingMultiTable", workers, parallelRecords,
                parallelRecords, (path, records) => PrepareParallelOverlappingWrite(path, records, workers)));
            definitions.Add(Scenario("Parallel.Mixed", "Read90Write10", workers, parallelRecords,
                parallelRecords, (path, records) => PrepareParallelMixed(path, records, workers, 10)));
            definitions.Add(Scenario("Parallel.Mixed", "Read50Write50", workers, parallelRecords,
                parallelRecords, (path, records) => PrepareParallelMixed(path, records, workers, 50)));
        }

        foreach (AuditScenarioDefinition definition in definitions)
        {
            if (options.ScenarioFilter != null && !options.ScenarioFilter.Contains(definition.FilterKey))
                continue;
            report.Measurements.Add(Measure(options, definition));
        }
        return report;
    }

    private static AuditScenarioDefinition Scenario(string category, string scenario, int workers, int records,
        long operations, Func<string, int, AuditPreparedOperation> prepare) => new()
    {
        Category = category,
        Scenario = scenario,
        Workers = workers,
        Records = records,
        Operations = operations,
        Prepare = prepare,
    };

    private static AuditMeasurement Measure(AuditWorkerOptions options, AuditScenarioDefinition definition)
    {
        var measurement = new AuditMeasurement
        {
            Variant = options.Variant,
            Round = options.Round,
            Category = definition.Category,
            Scenario = definition.Scenario,
            Workers = definition.Workers,
            Records = definition.Records,
            Operations = definition.Operations,
        };
        string scenarioName = Sanitize(definition.FilterKey);
        string warmupPath = Path.Combine(options.RootPath, scenarioName + "-warmup");
        string measuredPath = Path.Combine(options.RootPath, scenarioName + "-measure");

        try
        {
            int warmupRecords = Math.Min(definition.Records, Math.Max(32, Math.Min(1_000, definition.Records)));
            using (AuditPreparedOperation warmup = definition.Prepare(warmupPath, warmupRecords))
            {
                AuditOperationOutcome outcome = warmup.Execute();
                warmup.Verify(outcome);
            }
            DeleteScenarioDirectory(warmupPath, options.RootPath);

            AuditOperationOutcome measuredOutcome = default;
            using (AuditPreparedOperation prepared = definition.Prepare(measuredPath, definition.Records))
            {
                CollectGarbage();
                long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
                int gen0Before = GC.CollectionCount(0);
                int gen1Before = GC.CollectionCount(1);
                int gen2Before = GC.CollectionCount(2);
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    measuredOutcome = prepared.Execute();
                }
                finally
                {
                    stopwatch.Stop();
                    measurement.ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                    measurement.AllocatedBytes = Math.Max(0,
                        GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore);
                    measurement.Gen0Collections = GC.CollectionCount(0) - gen0Before;
                    measurement.Gen1Collections = GC.CollectionCount(1) - gen1Before;
                    measurement.Gen2Collections = GC.CollectionCount(2) - gen2Before;
                }
                prepared.Verify(measuredOutcome);
            }
            measurement.ResultCount = measuredOutcome.Count;
            measurement.Checksum = measuredOutcome.Checksum;
            measurement.OperationsPerSecond = measurement.ElapsedMilliseconds > 0
                ? definition.Operations * 1000d / measurement.ElapsedMilliseconds : 0;
            measurement.NanosecondsPerOperation = definition.Operations > 0
                ? measurement.ElapsedMilliseconds * 1_000_000d / definition.Operations : 0;
            measurement.AllocatedBytesPerOperation = definition.Operations > 0
                ? (double)measurement.AllocatedBytes / definition.Operations : 0;
            measurement.DatabaseBytes = DirectorySize(measuredPath);
            measurement.Succeeded = true;
        }
        catch (Exception ex)
        {
            measurement.Error = ex.ToString();
            measurement.Succeeded = false;
        }
        finally
        {
            DeleteScenarioDirectory(warmupPath, options.RootPath);
            DeleteScenarioDirectory(measuredPath, options.RootPath);
        }
        return measurement;
    }

    private static AuditPreparedOperation PrepareMemorySequentialInsert(string path, int records)
    {
        _ = path;
        var engine = MemoryEngine();
        var transaction = engine.GetTransaction();
        byte[] value = Value64();
        return Prepared(
            () =>
            {
                for (int i = 0; i < records; i++)
                    transaction.Insert(MainTable, i, value);
                transaction.Commit();
                return new AuditOperationOutcome(records, records);
            },
            outcome => Ensure(transaction.Count(MainTable) == (ulong)records && outcome.Count == records,
                "Memory sequential insert count mismatch."),
            transaction, engine);
    }

    private static AuditPreparedOperation PrepareMemoryPointRead(string path, int records)
    {
        _ = path;
        var engine = MemoryEngine();
        Seed(engine, MainTable, records, Value64());
        var transaction = engine.GetTransaction();
        return Prepared(
            () => PointRead(transaction, records, missing: false),
            outcome => Ensure(outcome.Count == records, "Memory point-read count mismatch."),
            transaction, engine);
    }

    private static AuditPreparedOperation PrepareDiskSequentialNull(string path, int records)
    {
        var engine = new DBreezeEngine(path);
        var transaction = engine.GetTransaction();
        return Prepared(
            () =>
            {
                for (int i = 0; i < records; i++)
                    transaction.Insert<int, byte[]>(MainTable, i, null);
                transaction.Commit();
                return new AuditOperationOutcome(records, records);
            },
            outcome => Ensure(transaction.Count(MainTable) == (ulong)records, "Sequential null insert mismatch."),
            transaction, engine);
    }

    private static AuditPreparedOperation PrepareDiskSequential64(string path, int records)
    {
        var engine = new DBreezeEngine(path);
        var transaction = engine.GetTransaction();
        byte[] value = Value64();
        return Prepared(
            () =>
            {
                for (int i = 0; i < records; i++)
                    transaction.Insert(MainTable, i, value);
                transaction.Commit();
                return new AuditOperationOutcome(records, records * 64L);
            },
            outcome => Ensure(transaction.Count(MainTable) == (ulong)records, "Sequential 64-byte insert mismatch."),
            transaction, engine);
    }

    private static AuditPreparedOperation PrepareDiskRandomInsert(string path, int records)
    {
        int[] keys = Enumerable.Range(0, records).ToArray();
        Shuffle(keys, 1729);
        var engine = new DBreezeEngine(path);
        var transaction = engine.GetTransaction();
        byte[] value = Value64();
        return Prepared(
            () =>
            {
                foreach (int key in keys)
                    transaction.Insert(MainTable, key, value);
                transaction.Commit();
                return new AuditOperationOutcome(records, records);
            },
            outcome => Ensure(transaction.Count(MainTable) == (ulong)records, "Random insert mismatch."),
            transaction, engine);
    }

    private static AuditPreparedOperation PrepareRandomKeySorterBatch(string path, int records)
    {
        var engine = new DBreezeEngine(path);
        var transaction = engine.GetTransaction();
        return Prepared(
            () =>
            {
                for (int i = 0; i < records; i++)
                    transaction.RandomKeySorter.Insert(MainTable, Permute(i, records), i);
                transaction.RandomKeySorter.Flush(MainTable);
                transaction.Commit();
                return new AuditOperationOutcome(records, records);
            },
            outcome => Ensure(transaction.Count(MainTable) == (ulong)records,
                "RandomKeySorter batch mismatch."),
            transaction, engine);
    }

    private static AuditPreparedOperation PrepareDiskUpdate4K(string path, int records)
    {
        var engine = new DBreezeEngine(path);
        Seed(engine, MainTable, records, new byte[16]);
        var transaction = engine.GetTransaction();
        byte[] value = Enumerable.Repeat((byte)0xA5, 4096).ToArray();
        return Prepared(
            () =>
            {
                for (int i = 0; i < records; i++)
                    transaction.Insert(MainTable, i, value);
                transaction.Commit();
                return new AuditOperationOutcome(records, records * 4096L);
            },
            outcome => Ensure(transaction.Select<int, byte[]>(MainTable, records - 1).Value.Length == 4096,
                "4K update mismatch."),
            transaction, engine);
    }

    private static AuditPreparedOperation PrepareDiskRemove(string path, int records)
    {
        var engine = new DBreezeEngine(path);
        Seed(engine, MainTable, records, Value64());
        var transaction = engine.GetTransaction();
        return Prepared(
            () =>
            {
                for (int i = 0; i < records; i++)
                    transaction.RemoveKey(MainTable, i);
                transaction.Commit();
                return new AuditOperationOutcome(records, records);
            },
            outcome => Ensure(transaction.Count(MainTable) == 0, "Remove benchmark left rows."),
            transaction, engine);
    }

    private static AuditPreparedOperation PrepareDiskRollback(string path, int records)
    {
        var engine = new DBreezeEngine(path);
        var transaction = engine.GetTransaction();
        byte[] value = Value64();
        return Prepared(
            () =>
            {
                for (int i = 0; i < records; i++)
                    transaction.Insert(MainTable, i, value);
                transaction.Rollback();
                return new AuditOperationOutcome(records, records);
            },
            outcome => Ensure(transaction.Count(MainTable) == 0, "Rollback benchmark leaked rows."),
            transaction, engine);
    }

    private static AuditPreparedOperation PrepareDiskPointExisting(string path, int records) =>
        PrepareDiskPoint(path, records, missing: false);

    private static AuditPreparedOperation PrepareDiskPointMissing(string path, int records) =>
        PrepareDiskPoint(path, records, missing: true);

    private static AuditPreparedOperation PrepareDiskPoint(string path, int records, bool missing)
    {
        var engine = new DBreezeEngine(path);
        Seed(engine, MainTable, records, Value64());
        var transaction = engine.GetTransaction();
        return Prepared(
            () => PointRead(transaction, records, missing),
            outcome => Ensure(outcome.Count == records, "Point-read operation count mismatch."),
            transaction, engine);
    }

    private static AuditOperationOutcome PointRead(DBreeze.Transactions.Transaction transaction, int records, bool missing)
    {
        long checksum = 0;
        for (int i = 0; i < records; i++)
        {
            int key = missing ? records + i : Permute(i, records);
            Row<int, byte[]> row = transaction.Select<int, byte[]>(MainTable, key);
            checksum += row.Exists ? row.Value.Length : 1;
        }
        return new AuditOperationOutcome(records, checksum);
    }

    private static AuditPreparedOperation PrepareDiskForwardLazy(string path, int records) =>
        PrepareDiskScan(path, records, readValues: false);

    private static AuditPreparedOperation PrepareDiskForwardEager(string path, int records) =>
        PrepareDiskScan(path, records, readValues: true);

    private static AuditPreparedOperation PrepareDiskScan(string path, int records, bool readValues)
    {
        var engine = new DBreezeEngine(path);
        Seed(engine, MainTable, records, Value64());
        var transaction = engine.GetTransaction();
        transaction.ValuesLazyLoadingIsOn = !readValues;
        return Prepared(
            () => Consume(transaction.SelectForward<int, byte[]>(MainTable), readValues, records),
            outcome => Ensure(outcome.Count == records, "Scan count mismatch."),
            transaction, engine);
    }

    private static AuditPreparedOperation PrepareDiskRange(string path, int records)
    {
        var engine = new DBreezeEngine(path);
        Seed(engine, MainTable, records, Value64());
        var transaction = engine.GetTransaction();
        int start = records / 4;
        int stop = start + Math.Max(0, records / 2 - 1);
        long expected = stop - start + 1L;
        return Prepared(
            () => Consume(transaction.SelectForwardFromTo<int, byte[]>(MainTable, start, true, stop, true), true,
                expected),
            outcome => Ensure(outcome.Count == expected, "Range count mismatch."),
            transaction, engine);
    }

    private static AuditPreparedOperation PrepareDiskSkip(string path, int records)
    {
        var engine = new DBreezeEngine(path);
        Seed(engine, MainTable, records, Value64());
        var transaction = engine.GetTransaction();
        ulong skip = (ulong)(records * 9L / 10L);
        long expected = records - (long)skip;
        return Prepared(
            () => Consume(transaction.SelectForwardSkip<int, byte[]>(MainTable, skip), false, expected),
            outcome => Ensure(outcome.Count == expected, "Skip count mismatch."),
            transaction, engine);
    }

    private static AuditPreparedOperation PreparePrefixScan(string path, int records)
    {
        var engine = new DBreezeEngine(path);
        using (var seed = engine.GetTransaction())
        {
            for (int i = 0; i < records; i++)
                seed.Insert(MainTable, PrefixKey(i), i);
            seed.Commit();
        }
        var transaction = engine.GetTransaction();
        return Prepared(
            () =>
            {
                long count = 0;
                long checksum = 0;
                foreach (Row<byte[], int> row in transaction.SelectForwardStartsWith<byte[], int>(
                             MainTable, new byte[] { 0x42 }))
                {
                    count++;
                    checksum += row.Value;
                }
                return new AuditOperationOutcome(count, checksum);
            },
            outcome => Ensure(outcome.Count == records, "Prefix scan mismatch."),
            transaction, engine);
    }

    private static AuditPreparedOperation PrepareMultiSelect(string path, int records)
    {
        var engine = new DBreezeEngine(path);
        int perTable = Math.Max(1, records / 2);
        using (var seed = engine.GetTransaction())
        {
            seed.SynchronizeTables(new List<string> { MainTable + "-a", MainTable + "-b" });
            for (int i = 0; i < perTable; i++)
            {
                seed.Insert(MainTable + "-a", i, i);
                seed.Insert(MainTable + "-b", i, i + perTable);
            }
            seed.Commit();
        }
        long expected = perTable * 2L;
        var transaction = engine.GetTransaction();
        return Prepared(
            () =>
            {
                long count = 0;
                long checksum = 0;
                foreach (Row<int, int> row in transaction.Multi_SelectForwardFromTo<int, int>(
                             new HashSet<string> { MainTable + "-a", MainTable + "-b" },
                             0, true, perTable - 1, true))
                {
                    count++;
                    checksum += row.Value;
                }
                return new AuditOperationOutcome(count, checksum);
            },
            outcome => Ensure(outcome.Count == expected, "Multi-select benchmark mismatch."),
            transaction, engine);
    }

    private static AuditPreparedOperation PreparePartialUpdate(string path, int records)
    {
        var engine = new DBreezeEngine(path);
        Seed(engine, MainTable, records, new byte[4096]);
        var transaction = engine.GetTransaction();
        byte[] patch = Enumerable.Repeat((byte)0x5A, 16).ToArray();
        return Prepared(
            () =>
            {
                for (int i = 0; i < records; i++)
                    transaction.InsertPart(MainTable, i, patch, 2048);
                transaction.Commit();
                return new AuditOperationOutcome(records, records * 16L);
            },
            outcome => Ensure(transaction.Select<int, byte[]>(MainTable, records - 1).Value
                .AsSpan(2048, 16).SequenceEqual(patch), "Partial update mismatch."),
            transaction, engine);
    }

    private static AuditPreparedOperation PrepareFixedDataBlock(string path, int records)
    {
        var engine = new DBreezeEngine(path);
        var transaction = engine.GetTransaction();
        byte[] value = Enumerable.Repeat((byte)0x3C, 4096).ToArray();
        byte[] lastPointer = null;
        return Prepared(
            () =>
            {
                for (int i = 0; i < records; i++)
                {
                    lastPointer = transaction.InsertDataBlockWithFixedAddress(MainTable, null, value);
                    transaction.Insert(MainTable, i, lastPointer);
                }
                transaction.Commit();
                return new AuditOperationOutcome(records, records * 4096L);
            },
            outcome => Ensure(transaction.SelectDataBlockWithFixedAddress<byte[]>(MainTable, lastPointer).Length == 4096,
                "Fixed data-block mismatch."),
            transaction, engine);
    }

    private static AuditPreparedOperation PrepareNestedCrud(string path, int records)
    {
        var engine = new DBreezeEngine(path);
        var transaction = engine.GetTransaction();
        NestedTable nested = transaction.InsertTable(MainTable, 1, 0);
        byte[] value = Value64();
        return Prepared(
            () =>
            {
                for (int i = 0; i < records; i++)
                    nested.Insert(i, value);
                transaction.Commit();
                return new AuditOperationOutcome(records, records);
            },
            outcome => Ensure(nested.Count() == (ulong)records, "Nested benchmark count mismatch."),
            nested, transaction, engine);
    }

    private static AuditPreparedOperation PrepareCollections(string path, int records)
    {
        var engine = new DBreezeEngine(path);
        var transaction = engine.GetTransaction();
        var dictionary = Enumerable.Range(0, records).ToDictionary(static key => key, static key => key * 7);
        return Prepared(
            () =>
            {
                transaction.InsertDictionary(MainTable, dictionary, true);
                transaction.Commit();
                return new AuditOperationOutcome(records, records);
            },
            outcome => Ensure(transaction.SelectDictionary<int, int>(MainTable).Count == records,
                "Dictionary benchmark mismatch."),
            transaction, engine);
    }

    private static AuditPreparedOperation PrepareObjects(string path, int records)
    {
        var engine = new DBreezeEngine(path);
        var transaction = engine.GetTransaction();
        return Prepared(
            () =>
            {
                for (int i = 0; i < records; i++)
                {
                    long identity = transaction.ObjectGetNewIdentity<long>(MainTable);
                    transaction.ObjectInsert(MainTable, new DBreezeObject<byte[]>
                    {
                        NewEntity = true,
                        Entity = Value64(),
                        Indexes = new List<DBreezeIndex>
                        {
                            new(1, identity) { PrimaryIndex = true },
                            new(2, i),
                        },
                    });
                }
                transaction.Commit();
                long checksum = 0;
                for (long identity = 1; identity <= records; identity++)
                {
                    DBreezeObject<byte[]> item = transaction.Select<byte[], byte[]>(MainTable,
                        1.ToIndex(identity)).ObjectGet<byte[]>();
                    checksum += item?.Entity?.Length ?? 0;
                }
                return new AuditOperationOutcome(records * 2L, checksum);
            },
            outcome => Ensure(outcome.Count == records * 2L && outcome.Checksum == records * 64L,
                "Object benchmark mismatch."),
            transaction, engine);
    }

    private static AuditPreparedOperation PrepareTextIndex(string path, int records)
    {
        var engine = new DBreezeEngine(path);
        var transaction = engine.GetTransaction();
        string[] documents = Enumerable.Range(0, records)
            .Select(static index => $"dbreeze benchmark alpha{index % 64} beta{index % 17}").ToArray();
        return Prepared(
            () =>
            {
                for (int i = 0; i < records; i++)
                    transaction.TextInsert(MainTable, i.To_4_bytes_array_BigEndian(), documents[i], "audit");
                transaction.Commit();
                return new AuditOperationOutcome(records, records);
            },
            outcome => Ensure(transaction.TextSearch(MainTable).BlockAnd("dbreeze").GetDocumentIDs().LongCount() == records,
                "Text index benchmark mismatch."),
            transaction, engine);
    }

    private static AuditPreparedOperation PrepareTextQuery(string path, int records)
    {
        var engine = new DBreezeEngine(path);
        using (var seed = engine.GetTransaction())
        {
            for (int i = 0; i < records; i++)
                seed.TextInsert(MainTable, i.To_4_bytes_array_BigEndian(), $"prefixable token{i % 32}", "audit");
            seed.Commit();
        }
        var transaction = engine.GetTransaction();
        return Prepared(
            () =>
            {
                byte[][] ids = transaction.TextSearch(MainTable).BlockAnd("pref").GetDocumentIDs().ToArray();
                long checksum = 0;
                foreach (byte[] id in ids)
                    checksum += id.To_Int32_BigEndian();
                return new AuditOperationOutcome(ids.LongLength, checksum);
            },
            outcome => Ensure(outcome.Count == records, "Text query benchmark mismatch."),
            transaction, engine);
    }

    private static AuditPreparedOperation PrepareResources(string path, int records)
    {
        var engine = new DBreezeEngine(path);
        string[] keys = Enumerable.Range(0, records).Select(static index => "resource-" + index.ToString("D8")).ToArray();
        byte[] value = Value64();
        return Prepared(
            () =>
            {
                for (int i = 0; i < records; i++)
                    engine.Resources.Insert(keys[i], value);
                long checksum = 0;
                for (int i = 0; i < records; i++)
                    checksum += engine.Resources.Select<byte[]>(keys[i])?.Length ?? 0;
                return new AuditOperationOutcome(records * 2L, checksum);
            },
            outcome => Ensure(outcome.Count == records * 2L, "Resources benchmark mismatch."),
            engine);
    }

    private static AuditPreparedOperation PrepareSchemeLifecycle(string path, int records)
    {
        var engine = new DBreezeEngine(path);
        return Prepared(
            () =>
            {
                for (int i = 0; i < records; i++)
                {
                    string source = "scheme-source-" + i.ToString("D4");
                    string destination = "scheme-destination-" + i.ToString("D4");
                    using (var transaction = engine.GetTransaction())
                    {
                        transaction.Insert(source, 1, i);
                        transaction.Commit();
                    }
                    engine.Scheme.RenameTable(source, destination);
                    engine.Scheme.DeleteTable(destination);
                }
                return new AuditOperationOutcome(records * 3L, records);
            },
            outcome => Ensure(engine.Scheme.GetUserTableNamesStartingWith("scheme-").Count == 0,
                "Scheme lifecycle benchmark left tables."),
            engine);
    }

    private static AuditPreparedOperation PrepareBackupRestore(string path, int records)
    {
        string source = Path.Combine(path, "source");
        string backup = Path.Combine(path, "backup");
        string destination = Path.Combine(path, "restored");
        Directory.CreateDirectory(source);
        var configuration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK };
        configuration.Backup.BackupFolderName = backup;
        byte[] payload = new byte[Math.Max(64, checked(records * 64))];
        try
        {
            var storage = new StorageLayer(Path.Combine(source, "3"), new TrieSettings(), configuration);
            for (int i = 0; i < payload.Length; i++)
                payload[i] = (byte)i;
            storage.Table_WriteToTheEnd(payload);
            storage.Commit();
            storage.Table_Dispose();
        }
        finally
        {
            configuration.Dispose();
        }
        return Prepared(
            () =>
            {
                var restorer = new BackupRestorer { BackupFolder = backup, DataBaseFolder = destination };
                restorer.OnRestore += delegate { };
                restorer.StartRestoration();
                return new AuditOperationOutcome(records, payload.Length);
            },
            outcome => Ensure(Directory.Exists(destination) && Directory.EnumerateFiles(destination).Any(),
                "Backup restore benchmark produced no files."));
    }

    private static AuditPreparedOperation PrepareUtilsConversion(string path, int records)
    {
        _ = path;
        return Prepared(
            () =>
            {
                long checksum = 0;
                for (int i = 0; i < records; i++)
                    checksum += DataTypesConvertor.ConvertBack<int>(DataTypesConvertor.ConvertKey(i));
                return new AuditOperationOutcome(records, checksum);
            },
            outcome => Ensure(outcome.Count == records, "Utils conversion benchmark mismatch."));
    }

    private static AuditPreparedOperation PrepareParallelRead(string path, int records, int workers)
    {
        var engine = new DBreezeEngine(path);
        Seed(engine, MainTable, records, Value64());
        return PrepareThreaded(engine, workers, worker =>
        {
            int count = PartitionCount(records, workers, worker);
            int offset = PartitionOffset(records, workers, worker);
            using var transaction = engine.GetTransaction();
            long checksum = 0;
            for (int i = 0; i < count; i++)
            {
                int key = Permute(offset + i, records);
                checksum += transaction.Select<int, byte[]>(MainTable, key).Value.Length;
            }
            return new AuditOperationOutcome(count, checksum);
        }, outcome => Ensure(outcome.Count == records, "Parallel read count mismatch."));
    }

    private static AuditPreparedOperation PrepareParallelWrite(string path, int records, int workers, bool sameTable)
    {
        var engine = new DBreezeEngine(path);
        byte[] value = Value64();
        return PrepareThreaded(engine, workers, worker =>
        {
            int count = PartitionCount(records, workers, worker);
            int offset = PartitionOffset(records, workers, worker);
            string table = sameTable ? MainTable : MainTable + "-" + worker;
            using var transaction = engine.GetTransaction();
            for (int i = 0; i < count; i++)
                transaction.Insert(table, sameTable ? offset + i : i, value);
            transaction.Commit();
            return new AuditOperationOutcome(count, count);
        }, outcome =>
        {
            Ensure(outcome.Count == records, "Parallel write operation count mismatch.");
            using var transaction = engine.GetTransaction();
            if (sameTable)
            {
                Ensure(transaction.Count(MainTable) == (ulong)records, "Shared-table parallel write mismatch.");
            }
            else
            {
                ulong total = 0;
                for (int worker = 0; worker < workers; worker++)
                    total += transaction.Count(MainTable + "-" + worker);
                Ensure(total == (ulong)records, "Separate-table parallel write mismatch.");
            }
        });
    }

    private static AuditPreparedOperation PrepareParallelOverlappingWrite(string path, int records, int workers)
    {
        var engine = new DBreezeEngine(path);
        byte[] value = Value64();
        int tableCount = Math.Max(2, Math.Min(4, workers));
        return PrepareThreaded(engine, workers, worker =>
        {
            int count = PartitionCount(records, workers, worker);
            int offset = PartitionOffset(records, workers, worker);
            string first = MainTable + "-overlap-" + (worker % tableCount);
            string second = MainTable + "-overlap-" + ((worker + 1) % tableCount);
            string[] tables = new[] { first, second }.Distinct(StringComparer.Ordinal)
                .OrderBy(static table => table, StringComparer.Ordinal).ToArray();
            using var transaction = engine.GetTransaction();
            transaction.SynchronizeTables(tables);
            for (int i = 0; i < count; i++)
                transaction.Insert((i & 1) == 0 ? first : second, offset + i, value);
            transaction.Commit();
            return new AuditOperationOutcome(count, count);
        }, outcome =>
        {
            Ensure(outcome.Count == records, "Overlapping multi-table operation count mismatch.");
            using var transaction = engine.GetTransaction();
            ulong total = 0;
            for (int table = 0; table < tableCount; table++)
                total += transaction.Count(MainTable + "-overlap-" + table);
            Ensure(total == (ulong)records, "Overlapping multi-table final row count mismatch.");
        });
    }

    private static AuditPreparedOperation PrepareParallelMixed(string path, int records, int workers, int writePercent)
    {
        var engine = new DBreezeEngine(path);
        int seedCount = Math.Max(1, records / 2);
        Seed(engine, MainTable, seedCount, Value64());
        byte[] value = Value64();
        long expectedWrites = 0;
        for (int worker = 0; worker < workers; worker++)
        {
            int count = PartitionCount(records, workers, worker);
            expectedWrites += Enumerable.Range(0, count).LongCount(index => index % 100 < writePercent);
        }

        return PrepareThreaded(engine, workers, worker =>
        {
            int count = PartitionCount(records, workers, worker);
            int offset = PartitionOffset(records, workers, worker);
            using var transaction = engine.GetTransaction();
            long checksum = 0;
            for (int i = 0; i < count; i++)
            {
                if (i % 100 < writePercent)
                {
                    int key = seedCount + offset + i;
                    transaction.Insert(MainTable, key, value);
                    checksum += key;
                }
                else
                {
                    int key = Permute(offset + i, seedCount);
                    Row<int, byte[]> row = transaction.Select<int, byte[]>(MainTable, key);
                    checksum += row.Exists ? row.Value.Length : 0;
                }
            }
            transaction.Commit();
            return new AuditOperationOutcome(count, checksum);
        }, outcome =>
        {
            Ensure(outcome.Count == records, "Parallel mixed operation count mismatch.");
            using var transaction = engine.GetTransaction();
            Ensure(transaction.Count(MainTable) == (ulong)(seedCount + expectedWrites),
                "Parallel mixed final row count mismatch.");
        });
    }

    private static AuditPreparedOperation PrepareThreaded(DBreezeEngine engine, int workers,
        Func<int, AuditOperationOutcome> action, Action<AuditOperationOutcome> verify)
    {
        var ready = new CountdownEvent(workers);
        var start = new ManualResetEventSlim(false);
        var errors = new ConcurrentQueue<Exception>();
        var outcomes = new AuditOperationOutcome[workers];
        var threads = new Thread[workers];
        for (int worker = 0; worker < workers; worker++)
        {
            int captured = worker;
            threads[worker] = new Thread(() =>
            {
                ready.Signal();
                start.Wait();
                try
                {
                    outcomes[captured] = action(captured);
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            }) { IsBackground = true, Name = "DBreeze audit worker " + captured };
            threads[worker].Start();
        }
        if (!ready.Wait(TimeSpan.FromSeconds(30)))
            throw new TimeoutException("Parallel audit workers did not become ready.");

        return new AuditPreparedOperation(
            () =>
            {
                start.Set();
                foreach (Thread thread in threads)
                    thread.Join();
                if (!errors.IsEmpty)
                    throw new AggregateException(errors);
                return new AuditOperationOutcome(outcomes.Sum(static outcome => outcome.Count),
                    outcomes.Sum(static outcome => outcome.Checksum));
            },
            verify,
            () =>
            {
                start.Set();
                foreach (Thread thread in threads)
                    if (thread.IsAlive)
                        thread.Join();
                ready.Dispose();
                start.Dispose();
                engine.Dispose();
            });
    }

    private static AuditPreparedOperation Prepared(Func<AuditOperationOutcome> execute,
        Action<AuditOperationOutcome> verify, params IDisposable[] disposables) =>
        new(execute, verify, () =>
        {
            Exception failure = null;
            foreach (IDisposable disposable in disposables)
            {
                try { disposable?.Dispose(); }
                catch (Exception ex) { failure ??= ex; }
            }
            if (failure != null)
                throw failure;
        });

    private static DBreezeEngine MemoryEngine() => new(new DBreezeConfiguration
    {
        Storage = DBreezeConfiguration.eStorage.MEMORY,
        NotifyAhead_WhenWriteTablePossibleDeadlock = false,
    });

    private static void Seed(DBreezeEngine engine, string table, int records, byte[] value)
    {
        using var transaction = engine.GetTransaction();
        for (int i = 0; i < records; i++)
            transaction.Insert(table, i, value);
        transaction.Commit();
    }

    private static AuditOperationOutcome Consume(IEnumerable<Row<int, byte[]>> rows, bool readValues, long expected)
    {
        long count = 0;
        long checksum = 0;
        foreach (Row<int, byte[]> row in rows)
        {
            count++;
            checksum += readValues ? row.Value?.Length ?? 0 : row.Key;
        }
        Ensure(count == expected, $"Iterator returned {count}, expected {expected}.");
        return new AuditOperationOutcome(count, checksum);
    }

    private static int[] WorkerCounts()
    {
        int processors = Math.Max(1, Math.Min(Environment.ProcessorCount, 16));
        return new[] { 1, 2, (processors + 1) / 2, processors }.Where(static value => value > 0)
            .Distinct().OrderBy(static value => value).ToArray();
    }

    private static int PartitionCount(int total, int workers, int worker) =>
        total / workers + (worker < total % workers ? 1 : 0);

    private static int PartitionOffset(int total, int workers, int worker) =>
        worker * (total / workers) + Math.Min(worker, total % workers);

    private static int Permute(int value, int count)
    {
        if (count <= 1)
            return 0;
        return (int)(((long)value * 104729 + 12345) % count);
    }

    private static void Shuffle(int[] values, int seed)
    {
        var random = new Random(seed);
        for (int i = values.Length - 1; i > 0; i--)
        {
            int target = random.Next(i + 1);
            (values[i], values[target]) = (values[target], values[i]);
        }
    }

    private static byte[] Value64()
    {
        byte[] value = new byte[64];
        for (int i = 0; i < value.Length; i++)
            value[i] = (byte)(i * 17 + 3);
        return value;
    }

    private static byte[] PrefixKey(int value)
    {
        byte[] key = new byte[5];
        key[0] = 0x42;
        byte[] suffix = value.To_4_bytes_array_BigEndian();
        Buffer.BlockCopy(suffix, 0, key, 1, suffix.Length);
        return key;
    }

    private static void CollectGarbage()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static long DirectorySize(string path)
    {
        if (!Directory.Exists(path))
            return 0;
        long total = 0;
        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            total += new FileInfo(file).Length;
        return total;
    }

    private static void DeleteScenarioDirectory(string path, string root)
    {
        if (Directory.Exists(path))
            AuditRunLayout.DeleteOwnedChild(path, root);
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
            builder.Append(Char.IsLetterOrDigit(character) ? character : '-');
        return builder.ToString();
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidDataException(message);
    }

    private sealed class AuditScenarioDefinition
    {
        internal string Category { get; init; }
        internal string Scenario { get; init; }
        internal int Workers { get; init; }
        internal int Records { get; init; }
        internal long Operations { get; init; }
        internal Func<string, int, AuditPreparedOperation> Prepare { get; init; }
        internal string FilterKey => Category + "|" + Scenario + "|" + Workers;
    }
}

internal readonly record struct AuditOperationOutcome(long Count, long Checksum);

internal sealed class AuditPreparedOperation : IDisposable
{
    private Action _dispose;

    internal AuditPreparedOperation(Func<AuditOperationOutcome> execute, Action<AuditOperationOutcome> verify,
        Action dispose)
    {
        Execute = execute ?? throw new ArgumentNullException(nameof(execute));
        Verify = verify ?? throw new ArgumentNullException(nameof(verify));
        _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
    }

    internal Func<AuditOperationOutcome> Execute { get; }
    internal Action<AuditOperationOutcome> Verify { get; }

    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}
