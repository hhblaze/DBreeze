using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using DBreeze;
using DBreeze.DataTypes;
using DBreeze.Transactions;

namespace DBreeze.Net8.Benchmarks;

internal static class BackwardReadAudit
{
    private const string MainTable = "kv";
    private const string PrefixTable = "prefix";
    private const int PayloadPoolSize = 1024;
    private const int Seed = 20260827;

    internal static int Run(string[] args)
    {
        BackwardReadAuditOptions options;
        try
        {
            options = BackwardReadAuditOptions.Parse(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Backward-read audit configuration error: " + exception.Message);
            return 2;
        }

        var layout = new AuditRunLayout(options.RootPath, options.RunId);
        BackwardReadAuditReport report = BackwardReadAuditReport.Create(options, layout);
        try
        {
            layout.Create();
            Execute(options, layout, report);
        }
        catch (Exception exception)
        {
            report.Failures.Add("Fatal backward-read audit failure: " + exception);
        }

        report.CompletedUtc = DateTime.UtcNow;
        Summarize(report);
        report.CorrectnessPassed = report.Failures.Count == 0 &&
            report.Measurements.Count != 0 && report.Measurements.All(static value => value.Succeeded);
        report.PerformancePassed = EvaluateGates(report, options);
        Persist(report);

        if (report.CorrectnessPassed && !options.KeepDatabases)
        {
            try
            {
                layout.CleanupScratch();
            }
            catch (Exception exception)
            {
                report.Failures.Add("Owned scratch cleanup failed: " + exception.Message);
                report.CorrectnessPassed = false;
                Persist(report);
            }
        }

        bool passed = report.CorrectnessPassed && report.PerformancePassed;
        Console.WriteLine($"Backward-read audit {(passed ? "PASS" : "FAIL")}: {options.ReportPath}");
        return passed ? 0 : 1;
    }

    private static void Execute(BackwardReadAuditOptions options, AuditRunLayout layout,
        BackwardReadAuditReport report)
    {
        byte[][] payloads = CreatePayloadPool(options.PayloadBytes);
        int diagnosticRecords = options.Smoke ? options.Records : Math.Min(options.Records, 100_000);
        int prefixRecords = options.Smoke ? options.Records : Math.Min(options.Records, 100_000);
        const int prefixGroups = 100;
        prefixRecords = Math.Max(prefixGroups, prefixRecords / prefixGroups * prefixGroups);

        using Fixture sequential = BuildFixture("Disk sequential", Path.Combine(layout.ScratchDirectory, "sequential"),
            options.Records, payloads, FixtureLayout.Sequential, memory: false, prefixRecords, prefixGroups);
        using Fixture random = BuildFixture("Disk random", Path.Combine(layout.ScratchDirectory, "random"),
            diagnosticRecords, payloads, FixtureLayout.RandomDirect, memory: false, 0, 0);
        using Fixture rks = BuildFixture("Disk RKS", Path.Combine(layout.ScratchDirectory, "rks"),
            diagnosticRecords, payloads, FixtureLayout.RandomKeySorter, memory: false, 0, 0);
        using Fixture fragmented = BuildFixture("Disk fragmented", Path.Combine(layout.ScratchDirectory, "fragmented"),
            diagnosticRecords, payloads, FixtureLayout.Fragmented, memory: false, 0, 0);
        using Fixture memory = BuildFixture("Memory", Path.Combine(layout.ScratchDirectory, "memory"),
            diagnosticRecords, payloads, FixtureLayout.Sequential, memory: true, 0, 0);

        Fixture[] fixtures = { sequential, random, rks, fragmented, memory };
        foreach (Fixture fixture in fixtures)
            report.FixtureBytes[fixture.Name] = fixture.PhysicalBytes;

        WarmUp(fixtures, payloads, options.PayloadBytes);
        List<TraversalScenario> scenarios = BuildScenarios(sequential, random, rks, fragmented, memory,
            prefixRecords, prefixGroups);
        foreach (TraversalScenario scenario in scenarios)
        {
            for (int round = 1; round <= options.Rounds; round++)
            {
                bool forwardFirst = (round & 1) != 0;
                foreach (bool forward in forwardFirst ? new[] { true, false } : new[] { false, true })
                {
                    StabilizeGc();
                    try
                    {
                        report.Measurements.Add(Measure(scenario, forward, round, payloads,
                            options.PayloadBytes));
                    }
                    catch (Exception exception)
                    {
                        report.Measurements.Add(BackwardReadMeasurement.Failure(
                            scenario.Name, forward ? "Forward" : "Backward", round, exception));
                    }
                }
            }
        }
    }

    private static Fixture BuildFixture(string name, string path, int records, byte[][] payloads,
        FixtureLayout layout, bool memory, int prefixRecords, int prefixGroups)
    {
        Directory.CreateDirectory(path);
        DBreezeConfiguration configuration = null;
        DBreezeEngine engine;
        if (memory)
        {
            configuration = new DBreezeConfiguration
            {
                Storage = DBreezeConfiguration.eStorage.MEMORY,
                NotifyAhead_WhenWriteTablePossibleDeadlock = false,
            };
            engine = new DBreezeEngine(configuration);
        }
        else
        {
            engine = new DBreezeEngine(path);
        }

        try
        {
            long[] keys = Enumerable.Range(0, records).Select(static value => (long)value).ToArray();
            if (layout is FixtureLayout.RandomDirect or FixtureLayout.RandomKeySorter)
                Shuffle(keys, Seed + records + (int)layout);

            using (Transaction transaction = engine.GetTransaction())
            {
                if (prefixRecords != 0)
                    transaction.SynchronizeTables(MainTable, PrefixTable);
                for (int index = 0; index < keys.Length; index++)
                {
                    long key = keys[index];
                    if (layout == FixtureLayout.RandomKeySorter)
                    {
                        transaction.RandomKeySorter.Insert(MainTable, key, payloads[(int)(key & (PayloadPoolSize - 1))]);
                        if ((index + 1) % 100_000 == 0)
                            transaction.RandomKeySorter.Flush(MainTable);
                    }
                    else
                    {
                        transaction.Insert(MainTable, key, payloads[(int)(key & (PayloadPoolSize - 1))]);
                    }
                }
                if (layout == FixtureLayout.RandomKeySorter)
                    transaction.RandomKeySorter.Flush(MainTable);

                if (prefixRecords != 0)
                {
                    int perGroup = prefixRecords / prefixGroups;
                    for (int group = 0; group < prefixGroups; group++)
                    for (int item = 0; item < perGroup; item++)
                    {
                        long payloadKey = (long)group * perGroup + item;
                        transaction.Insert(PrefixTable, PrefixKey(group, item),
                            payloads[(int)(payloadKey & (PayloadPoolSize - 1))]);
                    }
                }
                transaction.Commit();
            }

            if (layout == FixtureLayout.Fragmented)
            {
                Shuffle(keys, Seed ^ records);
                using Transaction transaction = engine.GetTransaction();
                for (int index = 0; index < keys.Length; index += 3)
                {
                    long key = keys[index];
                    transaction.Insert(MainTable, key, payloads[(int)(key & (PayloadPoolSize - 1))]);
                }
                transaction.Commit();
            }

            long bytes = memory ? 0 : DirectoryBytes(path);
            return new Fixture(name, engine, configuration, records, prefixRecords, prefixGroups, bytes);
        }
        catch
        {
            engine.Dispose();
            configuration?.Dispose();
            throw;
        }
    }

    private static List<TraversalScenario> BuildScenarios(Fixture sequential, Fixture random, Fixture rks,
        Fixture fragmented, Fixture memory, int prefixRecords, int prefixGroups)
    {
        int rangeCount = Math.Max(1, Math.Min(sequential.Records, sequential.Records / 10));
        int rangeStart = (sequential.Records - rangeCount) / 2;
        int skip = sequential.Records / 4;
        return new List<TraversalScenario>
        {
            new("Disk sequential / Full key-only", sequential, TraversalKind.Full, false, 0, sequential.Records - 1, 0, sequential.Records),
            new("Disk sequential / Full values", sequential, TraversalKind.Full, true, 0, sequential.Records - 1, 0, sequential.Records),
            new("Disk sequential / StartFrom values", sequential, TraversalKind.StartFrom, true, 0, sequential.Records - 1, 0, sequential.Records),
            new("Disk sequential / Range values", sequential, TraversalKind.Range, true, rangeStart, rangeStart + rangeCount - 1, 0, rangeCount),
            new("Disk sequential / Skip key-only", sequential, TraversalKind.Skip, false, 0, sequential.Records - 1, skip, sequential.Records - skip),
            new("Disk sequential / SkipFrom values", sequential, TraversalKind.SkipFrom, true, 0, sequential.Records - 1, skip, sequential.Records - skip - 1L),
            new("Disk sequential / Prefix values", sequential, TraversalKind.Prefix, true, 0, 0, 0, prefixRecords, prefixGroups),
            new("Disk random / Full values", random, TraversalKind.Full, true, 0, random.Records - 1, 0, random.Records),
            new("Disk RKS / Full values", rks, TraversalKind.Full, true, 0, rks.Records - 1, 0, rks.Records),
            new("Disk fragmented / Full values", fragmented, TraversalKind.Full, true, 0, fragmented.Records - 1, 0, fragmented.Records),
            new("Memory / Full values", memory, TraversalKind.Full, true, 0, memory.Records - 1, 0, memory.Records),
        };
    }

    private static void WarmUp(IEnumerable<Fixture> fixtures, byte[][] payloads, int payloadBytes)
    {
        foreach (Fixture fixture in fixtures)
        {
            using Transaction transaction = fixture.Engine.GetTransaction();
            transaction.ValuesLazyLoadingIsOn = false;
            foreach (Row<long, byte[]> row in transaction.SelectForward<long, byte[]>(MainTable).Take(2_000))
                ValidateValue(row.Value, row.Key, payloads, payloadBytes);
            foreach (Row<long, byte[]> row in transaction.SelectBackward<long, byte[]>(MainTable).Take(2_000))
                ValidateValue(row.Value, row.Key, payloads, payloadBytes);
        }
    }

    private static BackwardReadMeasurement Measure(TraversalScenario scenario, bool forward, int round,
        byte[][] payloads, int payloadBytes)
    {
        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long returned = 0;
        long checksum = 1469598103934665603L;
        var stopwatch = Stopwatch.StartNew();
        using (Transaction transaction = scenario.Fixture.Engine.GetTransaction())
        {
            transaction.ValuesLazyLoadingIsOn = !scenario.ReadValues;
            if (scenario.Kind == TraversalKind.Prefix)
            {
                int perGroup = scenario.Fixture.PrefixRecords / scenario.PrefixGroups;
                for (int group = 0; group < scenario.PrefixGroups; group++)
                {
                    IEnumerable<Row<byte[], byte[]>> rows = forward
                        ? transaction.SelectForwardStartsWith<byte[], byte[]>(PrefixTable, Prefix(group))
                        : transaction.SelectBackwardStartsWith<byte[], byte[]>(PrefixTable, Prefix(group));
                    long itemIndex = 0;
                    foreach (Row<byte[], byte[]> row in rows)
                    {
                        if (row.Key.Length != 8 || BinaryPrimitives.ReadInt32BigEndian(row.Key.AsSpan(0, 4)) != group)
                            throw new InvalidDataException("Prefix traversal returned an invalid composite key.");
                        int item = BinaryPrimitives.ReadInt32BigEndian(row.Key.AsSpan(4, 4));
                        int expectedItem = forward ? (int)itemIndex : perGroup - 1 - (int)itemIndex;
                        if (item != expectedItem)
                            throw new InvalidDataException($"Prefix traversal order mismatch: {item} != {expectedItem}.");
                        long payloadKey = (long)group * perGroup + item;
                        if (scenario.ReadValues)
                            ValidateValue(row.Value, payloadKey, payloads, payloadBytes);
                        checksum = AddChecksum(checksum, payloadKey, scenario.ReadValues ? row.Value : null);
                        itemIndex++;
                        returned++;
                    }
                    if (itemIndex != perGroup)
                        throw new InvalidDataException($"Prefix group {group} returned {itemIndex}/{perGroup} rows.");
                }
            }
            else
            {
                IEnumerable<Row<long, byte[]>> rows = SelectRows(transaction, scenario, forward);
                foreach (Row<long, byte[]> row in rows)
                {
                    long expected = ExpectedKey(scenario, forward, returned);
                    if (row.Key != expected)
                        throw new InvalidDataException($"Traversal order mismatch: {row.Key} != {expected}.");
                    byte[] value = scenario.ReadValues ? row.Value : null;
                    if (scenario.ReadValues)
                        ValidateValue(value, row.Key, payloads, payloadBytes);
                    checksum = AddChecksum(checksum, row.Key, value);
                    returned++;
                }
            }
        }
        stopwatch.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        if (returned != scenario.ExpectedRows)
            throw new InvalidDataException($"Traversal count mismatch: {returned} != {scenario.ExpectedRows}.");

        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        return new BackwardReadMeasurement
        {
            Scenario = scenario.Name,
            Direction = forward ? "Forward" : "Backward",
            Round = round,
            Operations = returned,
            Returned = returned,
            Checksum = checksum,
            ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
            OperationsPerSecond = returned * 1000.0 / stopwatch.Elapsed.TotalMilliseconds,
            AllocatedBytes = allocated,
            BytesPerOperation = allocated / (double)Math.Max(1, returned),
            Gen0 = GC.CollectionCount(0) - gen0,
            Gen1 = GC.CollectionCount(1) - gen1,
            Gen2 = GC.CollectionCount(2) - gen2,
            ProcessPrivateBytes = process.PrivateMemorySize64,
            PeakWorkingSetBytes = process.PeakWorkingSet64,
            Succeeded = true,
        };
    }

    private static IEnumerable<Row<long, byte[]>> SelectRows(Transaction transaction,
        TraversalScenario scenario, bool forward)
    {
        return scenario.Kind switch
        {
            TraversalKind.Full => forward
                ? transaction.SelectForward<long, byte[]>(MainTable)
                : transaction.SelectBackward<long, byte[]>(MainTable),
            TraversalKind.StartFrom => forward
                ? transaction.SelectForwardStartFrom<long, byte[]>(MainTable, scenario.LowKey, true)
                : transaction.SelectBackwardStartFrom<long, byte[]>(MainTable, scenario.HighKey, true),
            TraversalKind.Range => forward
                ? transaction.SelectForwardFromTo<long, byte[]>(MainTable, scenario.LowKey, true, scenario.HighKey, true)
                : transaction.SelectBackwardFromTo<long, byte[]>(MainTable, scenario.HighKey, true, scenario.LowKey, true),
            TraversalKind.Skip => forward
                ? transaction.SelectForwardSkip<long, byte[]>(MainTable, (ulong)scenario.Skip)
                : transaction.SelectBackwardSkip<long, byte[]>(MainTable, (ulong)scenario.Skip),
            TraversalKind.SkipFrom => forward
                ? transaction.SelectForwardSkipFrom<long, byte[]>(MainTable, scenario.LowKey, (ulong)scenario.Skip)
                : transaction.SelectBackwardSkipFrom<long, byte[]>(MainTable, scenario.HighKey, (ulong)scenario.Skip),
            _ => throw new InvalidOperationException("Unsupported traversal scenario."),
        };
    }

    private static long ExpectedKey(TraversalScenario scenario, bool forward, long index)
    {
        return scenario.Kind switch
        {
            TraversalKind.Full or TraversalKind.StartFrom => forward ? index : scenario.HighKey - index,
            TraversalKind.Range => forward ? scenario.LowKey + index : scenario.HighKey - index,
            TraversalKind.Skip => forward
                ? scenario.LowKey + scenario.Skip + index
                : scenario.HighKey - scenario.Skip - index,
            TraversalKind.SkipFrom => forward
                ? scenario.LowKey + scenario.Skip + 1L + index
                : scenario.HighKey - scenario.Skip - 1L - index,
            _ => throw new InvalidOperationException("Unsupported traversal oracle."),
        };
    }

    private static void ValidateValue(byte[] value, long key, byte[][] payloads, int payloadBytes)
    {
        byte[] expected = payloads[(int)(key & (PayloadPoolSize - 1))];
        if (value == null || value.Length != payloadBytes ||
            value[0] != expected[0] || value[^1] != expected[^1] ||
            value[payloadBytes / 2] != expected[payloadBytes / 2])
        {
            throw new InvalidDataException($"Value oracle mismatch for key {key}.");
        }
    }

    private static long AddChecksum(long checksum, long key, byte[] value)
    {
        unchecked
        {
            checksum = (checksum ^ key) * 1099511628211L;
            if (value != null)
            {
                checksum = (checksum ^ value.Length) * 1099511628211L;
                checksum = (checksum ^ value[0]) * 1099511628211L;
                checksum = (checksum ^ value[^1]) * 1099511628211L;
            }
            return checksum;
        }
    }

    private static byte[][] CreatePayloadPool(int size)
    {
        var result = new byte[PayloadPoolSize][];
        for (int index = 0; index < result.Length; index++)
        {
            byte[] value = new byte[size];
            uint state = unchecked((uint)(Seed + index * 2654435761u));
            for (int offset = 0; offset < value.Length; offset++)
            {
                state = state * 1664525u + 1013904223u;
                value[offset] = (byte)(state >> 24);
            }
            result[index] = value;
        }
        return result;
    }

    private static void Shuffle(long[] values, int seed)
    {
        var random = new Random(seed);
        for (int index = values.Length - 1; index > 0; index--)
        {
            int other = random.Next(index + 1);
            (values[index], values[other]) = (values[other], values[index]);
        }
    }

    private static byte[] PrefixKey(int group, int item)
    {
        byte[] key = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(key.AsSpan(0, 4), group);
        BinaryPrimitives.WriteInt32BigEndian(key.AsSpan(4, 4), item);
        return key;
    }

    private static byte[] Prefix(int group)
    {
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(prefix, group);
        return prefix;
    }

    private static void Summarize(BackwardReadAuditReport report)
    {
        report.Summaries = report.Measurements.Where(static value => value.Succeeded)
            .GroupBy(static value => new { value.Scenario, value.Direction })
            .Select(group => new BackwardReadSummary
            {
                Scenario = group.Key.Scenario,
                Direction = group.Key.Direction,
                MedianMilliseconds = Median(group.Select(static value => value.ElapsedMilliseconds)),
                MedianOperationsPerSecond = Median(group.Select(static value => value.OperationsPerSecond)),
                MedianBytesPerOperation = Median(group.Select(static value => value.BytesPerOperation)),
                MinimumOperationsPerSecond = group.Min(static value => value.OperationsPerSecond),
                MaximumOperationsPerSecond = group.Max(static value => value.OperationsPerSecond),
            })
            .OrderBy(static value => value.Scenario, StringComparer.Ordinal)
            .ThenBy(static value => value.Direction, StringComparer.Ordinal)
            .ToList();

        foreach (IGrouping<string, BackwardReadSummary> scenario in report.Summaries.GroupBy(static value => value.Scenario))
        {
            BackwardReadSummary forward = scenario.SingleOrDefault(static value => value.Direction == "Forward");
            BackwardReadSummary backward = scenario.SingleOrDefault(static value => value.Direction == "Backward");
            if (forward != null && backward != null)
            {
                forward.BackwardVsForward = backward.MedianOperationsPerSecond / forward.MedianOperationsPerSecond;
                backward.BackwardVsForward = forward.BackwardVsForward;
            }
        }
    }

    internal static bool EvaluateGates(BackwardReadAuditReport report, BackwardReadAuditOptions options)
    {
        report.GateViolations.Clear();
        if (!report.CorrectnessPassed)
        {
            report.GateViolations.Add("Correctness or measurement completeness failed.");
            return false;
        }
        if (options.Smoke)
        {
            report.Warnings.Add("Smoke profile is correctness-only and does not issue a release performance verdict.");
            return true;
        }

        bool passed = true;
        foreach (string scenarioName in new[]
        {
            "Disk sequential / Full key-only",
            "Disk sequential / Full values",
        })
        {
            BackwardReadSummary forward = FindSummary(report, scenarioName, "Forward");
            BackwardReadSummary backward = FindSummary(report, scenarioName, "Backward");
            if (forward == null || backward == null)
            {
                report.GateViolations.Add(scenarioName + ": missing direction pair.");
                passed = false;
                continue;
            }
            double ratio = backward.MedianOperationsPerSecond / forward.MedianOperationsPerSecond;
            if (ratio < 0.90)
            {
                report.GateViolations.Add($"{scenarioName}: backward is {ratio:P1} of forward; required >= 90%.");
                passed = false;
            }
            if (backward.MedianBytesPerOperation > forward.MedianBytesPerOperation * 1.05 + 1.0)
            {
                report.GateViolations.Add($"{scenarioName}: backward allocation exceeds forward by more than 5% and 1 B/op.");
                passed = false;
            }
        }

        BackwardReadSummary mainForward = FindSummary(report, "Disk sequential / Full values", "Forward");
        BackwardReadSummary mainBackward = FindSummary(report, "Disk sequential / Full values", "Backward");
        if (mainForward == null || mainBackward == null)
        {
            report.GateViolations.Add("Main full-value control pair is missing.");
            return false;
        }
        report.ForwardVsControl = mainForward.MedianOperationsPerSecond / options.ControlForwardOpsPerSecond;
        report.BackwardSpeedup = mainBackward.MedianOperationsPerSecond / options.ControlBackwardOpsPerSecond;
        if (report.ForwardVsControl < 0.95)
        {
            report.GateViolations.Add($"Forward throughput is {report.ForwardVsControl:P1} of control; required >= 95%.");
            passed = false;
        }
        if (report.BackwardSpeedup < 3.0)
        {
            report.GateViolations.Add($"Backward speedup is {report.BackwardSpeedup:N2}x; required >= 3.00x.");
            passed = false;
        }
        return passed;
    }

    private static BackwardReadSummary FindSummary(BackwardReadAuditReport report, string scenario, string direction) =>
        report.Summaries.SingleOrDefault(value => value.Scenario == scenario && value.Direction == direction);

    internal static double Median(IEnumerable<double> source)
    {
        double[] values = source.OrderBy(static value => value).ToArray();
        if (values.Length == 0)
            return Double.NaN;
        int middle = values.Length / 2;
        return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / 2 : values[middle];
    }

    private static void Persist(BackwardReadAuditReport report)
    {
        AuditPersistence.WriteJson(report.RawJson, report);
        AuditPersistence.WriteTextAtomic(report.RawCsv, BuildCsv(report));
        string html = BuildHtml(report);
        AuditPersistence.WriteTextAtomic(report.ImmutableHtml, html);
        AuditPersistence.WriteTextAtomic(report.CanonicalHtml, html);
    }

    private static string BuildCsv(BackwardReadAuditReport report)
    {
        var builder = new StringBuilder("scenario,direction,round,operations,returned,checksum,elapsed_ms,ops_per_second,allocated_bytes,bytes_per_operation,gc0,gc1,gc2,private_bytes,peak_working_set,succeeded,error\n");
        foreach (BackwardReadMeasurement value in report.Measurements)
        {
            builder.Append(Csv(value.Scenario)).Append(',').Append(Csv(value.Direction)).Append(',')
                .Append(value.Round).Append(',').Append(value.Operations).Append(',').Append(value.Returned).Append(',')
                .Append(value.Checksum).Append(',').Append(value.ElapsedMilliseconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.OperationsPerSecond.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.AllocatedBytes).Append(',').Append(value.BytesPerOperation.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.Gen0).Append(',').Append(value.Gen1).Append(',').Append(value.Gen2).Append(',')
                .Append(value.ProcessPrivateBytes).Append(',').Append(value.PeakWorkingSetBytes).Append(',')
                .Append(value.Succeeded).Append(',').Append(Csv(value.Error)).AppendLine();
        }
        return builder.ToString();
    }

    internal static string BuildHtml(BackwardReadAuditReport report)
    {
        static string H(string value) => System.Net.WebUtility.HtmlEncode(value ?? String.Empty);
        bool passed = report.CorrectnessPassed && report.PerformancePassed;
        var builder = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><title>DBreeze Backward Read Audit</title><style>body{font:14px Segoe UI,Arial;margin:28px;color:#18202a}table{border-collapse:collapse;width:100%;margin:14px 0}th,td{border:1px solid #ccd3da;padding:6px;text-align:right}th:first-child,td:first-child,th:nth-child(2),td:nth-child(2){text-align:left}.pass{color:#087830}.fail{color:#b00020}.warn{color:#9a6200}code{background:#f3f5f7;padding:2px 4px}</style></head><body>");
        builder.Append("<h1>DBreeze Backward Read Audit</h1><h2 class=\"")
            .Append(passed ? "pass\">PASS" : "fail\">FAIL").Append("</h2><p>Run <code>")
            .Append(H(report.RunId)).Append("</code>; records ").Append(report.Records.ToString("N0"))
            .Append(", payload ").Append(report.PayloadBytes).Append(" bytes, rounds ").Append(report.Rounds).Append(".</p>")
            .Append("<p>Warm process/JIT and OS cache; fixture construction is excluded. Direction order alternates F/B and B/F. ")
            .Append("DBreeze SHA-256 <code>").Append(H(report.DBreezeSha256)).Append("</code>; Git <code>")
            .Append(H(report.GitHead)).Append("</code>")
            .Append(report.GitDirty ? "; dirty fingerprint <code>" + H(report.GitStatusSha256) + "</code>" : "; clean")
            .Append(".</p><p>Control: forward ").Append(report.ControlForwardOpsPerSecond.ToString("N0"))
            .Append(" ops/s, backward ").Append(report.ControlBackwardOpsPerSecond.ToString("N0"))
            .Append(" ops/s. Current forward/control: ").Append(report.ForwardVsControl.ToString("P1"))
            .Append("; backward speedup: ").Append(report.BackwardSpeedup.ToString("N2")).Append("x.</p>")
            .Append("<h2>Median results</h2><table><thead><tr><th>Scenario</th><th>Direction</th><th>ms</th><th>ops/s</th><th>min/max ops/s</th><th>B/op</th><th>Backward / forward</th></tr></thead><tbody>");
        foreach (BackwardReadSummary value in report.Summaries)
        {
            builder.Append("<tr><td>").Append(H(value.Scenario)).Append("</td><td>").Append(H(value.Direction))
                .Append("</td><td>").Append(value.MedianMilliseconds.ToString("N2"))
                .Append("</td><td>").Append(value.MedianOperationsPerSecond.ToString("N0"))
                .Append("</td><td>").Append(value.MinimumOperationsPerSecond.ToString("N0")).Append(" / ")
                .Append(value.MaximumOperationsPerSecond.ToString("N0"))
                .Append("</td><td>").Append(value.MedianBytesPerOperation.ToString("N1"))
                .Append("</td><td>").Append(Double.IsNaN(value.BackwardVsForward) ? "—" : value.BackwardVsForward.ToString("P1"))
                .Append("</td></tr>");
        }
        builder.Append("</tbody></table><h2>Gates and findings</h2><ul>");
        if (report.GateViolations.Count == 0)
            builder.Append("<li class=\"pass\">All applicable performance gates passed.</li>");
        foreach (string value in report.GateViolations)
            builder.Append("<li class=\"fail\">").Append(H(value)).Append("</li>");
        foreach (string value in report.Failures)
            builder.Append("<li class=\"fail\">").Append(H(value)).Append("</li>");
        foreach (string value in report.Warnings)
            builder.Append("<li class=\"warn\">").Append(H(value)).Append("</li>");
        builder.Append("</ul><h2>Per round</h2><table><thead><tr><th>Scenario</th><th>Direction</th><th>Round</th><th>ms</th><th>ops/s</th><th>B/op</th><th>GC 0/1/2</th><th>Private bytes</th><th>Peak working set</th><th>Status</th></tr></thead><tbody>");
        foreach (BackwardReadMeasurement value in report.Measurements)
        {
            builder.Append("<tr><td>").Append(H(value.Scenario)).Append("</td><td>").Append(H(value.Direction))
                .Append("</td><td>").Append(value.Round).Append("</td><td>")
                .Append(value.ElapsedMilliseconds.ToString("N2")).Append("</td><td>")
                .Append(value.OperationsPerSecond.ToString("N0")).Append("</td><td>")
                .Append(value.BytesPerOperation.ToString("N1")).Append("</td><td>")
                .Append(value.Gen0).Append('/').Append(value.Gen1).Append('/').Append(value.Gen2)
                .Append("</td><td>").Append(value.ProcessPrivateBytes.ToString("N0"))
                .Append("</td><td>").Append(value.PeakWorkingSetBytes.ToString("N0"))
                .Append("</td><td class=\"").Append(value.Succeeded ? "pass\">PASS" : "fail\">FAIL")
                .Append("</td></tr>");
        }
        builder.Append("</tbody></table><h2>Fixture sizes</h2><ul>");
        foreach (KeyValuePair<string, long> value in report.FixtureBytes)
            builder.Append("<li>").Append(H(value.Key)).Append(": ").Append(value.Value.ToString("N0")).Append(" bytes</li>");
        builder.Append("</ul><p>Runtime: ").Append(H(report.Runtime)).Append("; OS: ").Append(H(report.OS))
            .Append("; GC: ").Append(report.ServerGc ? "Server" : "Workstation").Append(" / ").Append(H(report.GcLatencyMode))
            .Append(". Raw artifacts: <code>").Append(H(Path.GetDirectoryName(report.RawJson))).Append("</code>.</p></body></html>");
        return builder.ToString();
    }

    private static string Csv(string value) => '"' + (value ?? String.Empty).Replace("\"", "\"\"") + '"';
    private static long DirectoryBytes(string path) => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(static file => new FileInfo(file).Length);
    private static void StabilizeGc() { GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true); GC.WaitForPendingFinalizers(); GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true); }

    private sealed record TraversalScenario(string Name, Fixture Fixture, TraversalKind Kind, bool ReadValues,
        long LowKey, long HighKey, int Skip, long ExpectedRows, int PrefixGroups = 0);

    private sealed class Fixture : IDisposable
    {
        internal Fixture(string name, DBreezeEngine engine, DBreezeConfiguration configuration, int records,
            int prefixRecords, int prefixGroups, long physicalBytes)
        {
            Name = name; Engine = engine; Configuration = configuration; Records = records;
            PrefixRecords = prefixRecords; PrefixGroups = prefixGroups; PhysicalBytes = physicalBytes;
        }
        internal string Name { get; }
        internal DBreezeEngine Engine { get; }
        internal DBreezeConfiguration Configuration { get; }
        internal int Records { get; }
        internal int PrefixRecords { get; }
        internal int PrefixGroups { get; }
        internal long PhysicalBytes { get; }
        public void Dispose() { Engine.Dispose(); Configuration?.Dispose(); }
    }

    private enum FixtureLayout { Sequential, RandomDirect, RandomKeySorter, Fragmented }
    private enum TraversalKind { Full, StartFrom, Range, Skip, SkipFrom, Prefix }
}

internal sealed class BackwardReadAuditOptions
{
    internal string RootPath { get; private set; } = @"D:\Temp\DbreezeDbTest";
    internal string ReportPath { get; private set; }
    internal string RunId { get; private set; }
    internal int Records { get; private set; } = 1_000_000;
    internal int PayloadBytes { get; private set; } = 256;
    internal int Rounds { get; private set; } = 5;
    internal bool KeepDatabases { get; private set; }
    internal bool Smoke { get; private set; }
    internal double ControlForwardOpsPerSecond { get; private set; } = 1_274_510;
    internal double ControlBackwardOpsPerSecond { get; private set; } = 332_777;

    internal static BackwardReadAuditOptions Parse(string[] args)
    {
        var result = new BackwardReadAuditOptions();
        for (int index = 0; index < args.Length; index++)
        {
            string option = args[index].ToLowerInvariant();
            switch (option)
            {
                case "--backward-read-audit": break;
                case "--smoke": result.Smoke = true; result.Records = 10_000; result.Rounds = 1; break;
                case "--keep-databases": result.KeepDatabases = true; break;
                case "--root": result.RootPath = Read(args, ref index, option); break;
                case "--report": result.ReportPath = Read(args, ref index, option); break;
                case "--run-id": result.RunId = Read(args, ref index, option); break;
                case "--records": result.Records = ReadInt(args, ref index, option, 1_000, 1_000_000); break;
                case "--payload-bytes": result.PayloadBytes = ReadInt(args, ref index, option, 1, 65_536); break;
                case "--rounds": result.Rounds = ReadInt(args, ref index, option, 1, 5); break;
                case "--control-forward-ops": result.ControlForwardOpsPerSecond = ReadDouble(args, ref index, option); break;
                case "--control-backward-ops": result.ControlBackwardOpsPerSecond = ReadDouble(args, ref index, option); break;
                default: throw new ArgumentException("Unknown backward-read audit option: " + args[index]);
            }
        }
        result.RootPath = Path.GetFullPath(result.RootPath);
        result.ReportPath = Path.GetFullPath(result.ReportPath ?? Path.Combine(result.RootPath, "DBreeze_Backward_Read_Audit.html"));
        AuditRunLayout.EnsureUnderRoot(result.ReportPath, result.RootPath);
        result.RunId ??= DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-backward-read";
        AuditRunLayout.ValidateLeafName(result.RunId, "--run-id");
        return result;
    }

    private static string Read(string[] args, ref int index, string option) =>
        ++index < args.Length && !String.IsNullOrWhiteSpace(args[index])
            ? args[index]
            : throw new ArgumentException(option + " requires a value.");
    private static int ReadInt(string[] args, ref int index, string option, int min, int max) =>
        Int32.TryParse(Read(args, ref index, option), NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value >= min && value <= max
            ? value : throw new ArgumentOutOfRangeException(option, $"Expected {min}..{max}.");
    private static double ReadDouble(string[] args, ref int index, string option) =>
        Double.TryParse(Read(args, ref index, option), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0 && Double.IsFinite(value)
            ? value : throw new ArgumentOutOfRangeException(option, "Expected a positive finite number.");
}

internal sealed class BackwardReadAuditReport
{
    public string RunId { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string Runtime { get; set; }
    public string OS { get; set; }
    public string Architecture { get; set; }
    public int ProcessorCount { get; set; }
    public string ProcessorIdentifier { get; set; }
    public bool ServerGc { get; set; }
    public string GcLatencyMode { get; set; }
    public string DBreezeVersion { get; set; }
    public string DBreezeSha256 { get; set; }
    public string GitHead { get; set; }
    public bool GitDirty { get; set; }
    public string GitStatusSha256 { get; set; }
    public int Records { get; set; }
    public int PayloadBytes { get; set; }
    public int Rounds { get; set; }
    public double ControlForwardOpsPerSecond { get; set; }
    public double ControlBackwardOpsPerSecond { get; set; }
    public double ForwardVsControl { get; set; }
    public double BackwardSpeedup { get; set; }
    public string RawJson { get; set; }
    public string RawCsv { get; set; }
    public string ImmutableHtml { get; set; }
    public string CanonicalHtml { get; set; }
    public bool CorrectnessPassed { get; set; }
    public bool PerformancePassed { get; set; }
    public Dictionary<string, long> FixtureBytes { get; set; } = new(StringComparer.Ordinal);
    public List<BackwardReadMeasurement> Measurements { get; set; } = new();
    public List<BackwardReadSummary> Summaries { get; set; } = new();
    public List<string> Failures { get; set; } = new();
    public List<string> GateViolations { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    internal static BackwardReadAuditReport Create(BackwardReadAuditOptions options, AuditRunLayout layout)
    {
        Assembly assembly = typeof(DBreezeEngine).Assembly;
        string gitHead = Git("rev-parse", "HEAD").Trim();
        string gitStatus = Git("status", "--porcelain=v1", "--untracked-files=all");
        return new BackwardReadAuditReport
        {
            RunId = options.RunId,
            StartedUtc = DateTime.UtcNow,
            Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            ProcessorIdentifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? String.Empty,
            ServerGc = GCSettings.IsServerGC,
            GcLatencyMode = GCSettings.LatencyMode.ToString(),
            DBreezeVersion = assembly.GetName().Version?.ToString(),
            DBreezeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant(),
            GitHead = gitHead,
            GitDirty = !String.IsNullOrWhiteSpace(gitStatus),
            GitStatusSha256 = String.IsNullOrEmpty(gitStatus) ? String.Empty :
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(gitStatus))).ToLowerInvariant(),
            Records = options.Records,
            PayloadBytes = options.PayloadBytes,
            Rounds = options.Rounds,
            ControlForwardOpsPerSecond = options.ControlForwardOpsPerSecond,
            ControlBackwardOpsPerSecond = options.ControlBackwardOpsPerSecond,
            RawJson = Path.Combine(layout.ReportsDirectory, "DBreeze_Backward_Read_Audit.json"),
            RawCsv = Path.Combine(layout.ReportsDirectory, "DBreeze_Backward_Read_Audit.csv"),
            ImmutableHtml = Path.Combine(layout.ReportsDirectory, "DBreeze_Backward_Read_Audit.html"),
            CanonicalHtml = options.ReportPath,
        };
    }

    private static string Git(params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string argument in arguments)
                start.ArgumentList.Add(argument);
            using Process process = Process.Start(start);
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : String.Empty;
        }
        catch
        {
            return String.Empty;
        }
    }
}

internal sealed class BackwardReadMeasurement
{
    public string Scenario { get; set; }
    public string Direction { get; set; }
    public int Round { get; set; }
    public long Operations { get; set; }
    public long Returned { get; set; }
    public long Checksum { get; set; }
    public double ElapsedMilliseconds { get; set; }
    public double OperationsPerSecond { get; set; }
    public long AllocatedBytes { get; set; }
    public double BytesPerOperation { get; set; }
    public int Gen0 { get; set; }
    public int Gen1 { get; set; }
    public int Gen2 { get; set; }
    public long ProcessPrivateBytes { get; set; }
    public long PeakWorkingSetBytes { get; set; }
    public bool Succeeded { get; set; }
    public string Error { get; set; }

    internal static BackwardReadMeasurement Failure(string scenario, string direction, int round, Exception exception) => new()
    {
        Scenario = scenario,
        Direction = direction,
        Round = round,
        Succeeded = false,
        Error = exception.ToString(),
    };
}

internal sealed class BackwardReadSummary
{
    public string Scenario { get; set; }
    public string Direction { get; set; }
    public double MedianMilliseconds { get; set; }
    public double MedianOperationsPerSecond { get; set; }
    public double MedianBytesPerOperation { get; set; }
    public double MinimumOperationsPerSecond { get; set; }
    public double MaximumOperationsPerSecond { get; set; }
    public double BackwardVsForward { get; set; } = Double.NaN;
}

internal static class BackwardReadAuditSelfTests
{
    internal static int Run()
    {
        var failures = new List<string>();
        try
        {
            if (BackwardReadAudit.Median(new[] { 4.0, 1.0, 3.0, 2.0 }) != 2.5)
                failures.Add("Median calculation failed.");

            string root = Path.Combine(Path.GetTempPath(), "dbreeze-backward-options");
            BackwardReadAuditOptions smoke = BackwardReadAuditOptions.Parse(new[]
            {
                "--backward-read-audit", "--smoke", "--root", root,
            });
            if (smoke.Records != 10_000 || smoke.Rounds != 1)
                failures.Add("Smoke option defaults failed.");

            var report = new BackwardReadAuditReport
            {
                RunId = "<escaped>",
                Runtime = "runtime", OS = "os", GcLatencyMode = "Interactive",
                RawJson = Path.Combine(root, "raw.json"),
                CorrectnessPassed = true, PerformancePassed = true,
            };
            string html = BackwardReadAudit.BuildHtml(report);
            if (!html.Contains("&lt;escaped&gt;", StringComparison.Ordinal) || html.Contains("<escaped>", StringComparison.Ordinal))
                failures.Add("HTML escaping failed.");
        }
        catch (Exception exception)
        {
            failures.Add(exception.ToString());
        }

        foreach (string failure in failures)
            Console.Error.WriteLine("FAIL " + failure);
        if (failures.Count == 0)
            Console.WriteLine("PASS backward-read audit self-tests");
        return failures.Count == 0 ? 0 : 1;
    }
}
