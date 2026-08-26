using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DBreeze;
using DBreeze.DataTypes;
using Microsoft.Data.Sqlite;

namespace DBreeze.Net8.Benchmarks;

internal static class PointReadAudit
{
    private const string Table = "kv";
    private const int Seed = 20260826;

    internal static int Run(string[] args)
    {
        PointReadAuditOptions options;
        try { options = PointReadAuditOptions.Parse(args); }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Point-read audit configuration error: " + exception.Message);
            return 2;
        }

        var layout = new AuditRunLayout(options.RootPath, options.RunId);
        var report = PointReadAuditReport.Create(options, layout);
        layout.Create();
        try
        {
            Execute(options, layout, report);
        }
        catch (Exception exception)
        {
            report.Failures.Add("Fatal point-read audit failure: " + exception);
        }

        report.CompletedUtc = DateTime.UtcNow;
        Summarize(report);
        report.CorrectnessPassed = report.Failures.Count == 0 &&
            report.Measurements.All(static measurement => measurement.Succeeded);
        report.PerformancePassed = EvaluateGates(report);
        Persist(report);

        if (report.CorrectnessPassed && !options.KeepDatabases)
        {
            try { layout.CleanupScratch(); }
            catch (Exception exception)
            {
                report.Failures.Add("Owned scratch cleanup failed: " + exception.Message);
                report.CorrectnessPassed = false;
                Persist(report);
            }
        }

        Console.WriteLine($"Point-read audit {(report.CorrectnessPassed && report.PerformancePassed ? "PASS" : "FAIL")}: {options.ReportPath}");
        return report.CorrectnessPassed && report.PerformancePassed ? 0 : 1;
    }

    private static void Execute(PointReadAuditOptions options, AuditRunLayout layout,
        PointReadAuditReport report)
    {
        LoadControl(options, report);
        byte[][] payloads = CreatePayloadPool(options.PayloadBytes);
        long[] allKeys = Enumerable.Range(0, options.Records).Select(static value => (long)value).ToArray();
        Shuffle(allKeys);
        int pointCount = Math.Min(250_000, options.Records);
        long[] hits = allKeys.Take(pointCount).ToArray();
        long[] mixed = new long[pointCount];
        for (int index = 0; index < mixed.Length; index++)
            mixed[index] = index % 10 == 0 ? options.Records + index + 1L : hits[index];

        string dbreezePath = Path.Combine(layout.ScratchDirectory, "fixture-dbreeze");
        string sqliteFile = Path.Combine(layout.ScratchDirectory, "fixture-sqlite", "database.sqlite");
        BuildDbreezeFixture(dbreezePath, options.Records, payloads);
        BuildSqliteFixture(sqliteFile, options, payloads);
        report.DbreezeBytes = DirectoryBytes(dbreezePath);
        report.SqliteBytes = DirectoryBytes(Path.GetDirectoryName(sqliteFile)!);

        WarmUp(dbreezePath, sqliteFile, options, hits.Take(Math.Min(2_000, hits.Length)).ToArray());
        var scenarios = new[]
        {
            new PointScenario("Random hits", hits, Parallel: false),
            new PointScenario("Mixed 90/10", mixed, Parallel: false),
            new PointScenario("Parallel hits", hits, Parallel: true),
        };
        if (options.ParallelOnly)
            scenarios = scenarios.Where(static scenario => scenario.Parallel).ToArray();

        foreach (PointScenario scenario in scenarios)
        {
            for (int round = 1; round <= options.Rounds; round++)
            {
                var providers = new[] { "DBreeze lazy", "DBreeze eager", "SQLite" };
                int rotation = (round - 1) % providers.Length;
                foreach (string provider in providers.Skip(rotation).Concat(providers.Take(rotation)))
                {
                    StabilizeGc();
                    PointReadMeasurement measurement;
                    try
                    {
                        measurement = provider switch
                        {
                            "DBreeze lazy" => MeasureDbreeze(dbreezePath, scenario, options,
                                lazy: true, round),
                            "DBreeze eager" => MeasureDbreeze(dbreezePath, scenario, options,
                                lazy: false, round),
                            _ => MeasureSqlite(sqliteFile, scenario, options, round),
                        };
                    }
                    catch (Exception exception)
                    {
                        measurement = new PointReadMeasurement
                        {
                            Scenario = scenario.Name,
                            Provider = provider,
                            Round = round,
                            Succeeded = false,
                            Error = exception.ToString(),
                        };
                        report.Failures.Add($"{scenario.Name} / {provider} / round {round}: {exception.Message}");
                    }
                    report.Measurements.Add(measurement);
                    Console.WriteLine($"{scenario.Name} / {provider} / {round}: " +
                        (measurement.Succeeded
                            ? $"{measurement.OperationsPerSecond:N0} ops/s, {measurement.BytesPerOperation:N0} B/op"
                            : "FAIL"));
                    Persist(report);
                }
            }
        }
    }

    private static PointReadMeasurement MeasureDbreeze(string path, PointScenario scenario,
        PointReadAuditOptions options, bool lazy, int round)
    {
        long operations = scenario.Parallel
            ? (long)options.Parallelism * options.ParallelOperationsPerWorker
            : scenario.Keys.Length;
        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        long cacheHitsBefore, cacheMissesBefore, ignored;
        ReadCacheDiagnostics(out cacheHitsBefore, out cacheMissesBefore, out ignored);
        long returned;
        long checksum;
        long allocated;
        long retained;
        double elapsed;

        using (var engine = new DBreezeEngine(path))
        {
            if (scenario.Parallel)
            {
                (returned, checksum, allocated, elapsed) = MeasureDbreezeParallel(
                    engine, scenario.Keys, options, lazy);
            }
            else
            {
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var stopwatch = Stopwatch.StartNew();
                using (var transaction = engine.GetTransaction())
                {
                    transaction.ValuesLazyLoadingIsOn = lazy;
                    returned = 0;
                    checksum = 0;
                    for (int index = 0; index < scenario.Keys.Length; index++)
                    {
                        long key = scenario.Keys[index];
                        Row<long, byte[]> row = transaction.Select<long, byte[]>(Table, key);
                        if (!row.Exists)
                            continue;
                        checksum = AddChecksum(checksum, key, row.Value);
                        returned++;
                    }
                }
                stopwatch.Stop();
                elapsed = stopwatch.Elapsed.TotalMilliseconds;
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            }
            ReadCacheDiagnostics(out _, out _, out retained);
        }

        long cacheHitsAfter, cacheMissesAfter;
        ReadCacheDiagnostics(out cacheHitsAfter, out cacheMissesAfter, out _);
        Verify(scenario, options, returned, checksum);
        return PointReadMeasurement.Success(scenario.Name, lazy ? "DBreeze lazy" : "DBreeze eager",
            round, operations, returned, checksum, elapsed, allocated,
            GC.CollectionCount(0) - gc0, GC.CollectionCount(1) - gc1, GC.CollectionCount(2) - gc2,
            cacheHitsAfter - cacheHitsBefore, cacheMissesAfter - cacheMissesBefore, retained);
    }

    private static (long Returned, long Checksum, long Allocated, double Elapsed)
        MeasureDbreezeParallel(DBreezeEngine engine, long[] keys, PointReadAuditOptions options, bool lazy)
    {
        using var start = new ManualResetEventSlim(false);
        using var ready = new CountdownEvent(options.Parallelism);
        var tasks = new Task<(long Count, long Checksum, long Allocated)>[options.Parallelism];
        for (int worker = 0; worker < tasks.Length; worker++)
        {
            int workerId = worker;
            tasks[worker] = Task.Run(() =>
            {
                ready.Signal();
                start.Wait();
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                long count = 0, checksum = 0;
                using var transaction = engine.GetTransaction();
                transaction.ValuesLazyLoadingIsOn = lazy;
                for (int index = 0; index < options.ParallelOperationsPerWorker; index++)
                {
                    long key = keys[(int)(((long)workerId * options.ParallelOperationsPerWorker + index) % keys.Length)];
                    Row<long, byte[]> row = transaction.Select<long, byte[]>(Table, key);
                    if (!row.Exists)
                        throw new InvalidDataException("DBreeze parallel read missed an existing key.");
                    checksum = AddChecksum(checksum, key, row.Value);
                    count++;
                }
                return (count, checksum, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
            });
        }
        ready.Wait();
        var stopwatch = Stopwatch.StartNew();
        start.Set();
        if (!Task.WaitAll(tasks, TimeSpan.FromMinutes(5)))
            throw new TimeoutException("DBreeze parallel point reads timed out.");
        stopwatch.Stop();
        return (tasks.Sum(static task => task.Result.Count),
            tasks.Aggregate(0L, static (sum, task) => unchecked(sum + task.Result.Checksum)),
            tasks.Sum(static task => task.Result.Allocated), stopwatch.Elapsed.TotalMilliseconds);
    }

    private static PointReadMeasurement MeasureSqlite(string file, PointScenario scenario,
        PointReadAuditOptions options, int round)
    {
        if (scenario.Parallel)
            return MeasureSqliteParallel(file, scenario, options, round);

        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        long returned = 0, checksum = 0;
        long allocated;
        double elapsed;
        using (SqliteConnection connection = OpenSqlite(file, options, create: false))
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT v FROM kv WHERE k=$k;";
            SqliteParameter parameter = command.Parameters.Add("$k", SqliteType.Integer);
            command.Prepare();
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                command.Transaction = transaction;
                foreach (long key in scenario.Keys)
                {
                    parameter.Value = key;
                    using SqliteDataReader reader = command.ExecuteReader();
                    if (!reader.Read())
                        continue;
                    checksum = AddChecksum(checksum, key, (byte[])reader.GetValue(0));
                    returned++;
                }
            }
            stopwatch.Stop();
            elapsed = stopwatch.Elapsed.TotalMilliseconds;
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        }
        Verify(scenario, options, returned, checksum);
        return PointReadMeasurement.Success(scenario.Name, "SQLite", round, scenario.Keys.Length,
            returned, checksum, elapsed, allocated, GC.CollectionCount(0) - gc0,
            GC.CollectionCount(1) - gc1, GC.CollectionCount(2) - gc2, 0, 0, 0);
    }

    private static PointReadMeasurement MeasureSqliteParallel(string file, PointScenario scenario,
        PointReadAuditOptions options, int round)
    {
        var connections = new SqliteConnection[options.Parallelism];
        var commands = new SqliteCommand[options.Parallelism];
        var parameters = new SqliteParameter[options.Parallelism];
        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        try
        {
            for (int worker = 0; worker < options.Parallelism; worker++)
            {
                connections[worker] = OpenSqlite(file, options, false);
                commands[worker] = connections[worker].CreateCommand();
                commands[worker].CommandText = "SELECT v FROM kv WHERE k=$k;";
                parameters[worker] = commands[worker].Parameters.Add("$k", SqliteType.Integer);
                commands[worker].Prepare();
            }
            using var start = new ManualResetEventSlim(false);
            using var ready = new CountdownEvent(options.Parallelism);
            var tasks = new Task<(long Count, long Checksum, long Allocated)>[options.Parallelism];
            for (int worker = 0; worker < options.Parallelism; worker++)
            {
                int workerId = worker;
                tasks[worker] = Task.Run(() =>
                {
                    ready.Signal(); start.Wait();
                    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                    long count = 0, checksum = 0;
                    using SqliteTransaction transaction = connections[workerId].BeginTransaction();
                    commands[workerId].Transaction = transaction;
                    for (int index = 0; index < options.ParallelOperationsPerWorker; index++)
                    {
                        long key = scenario.Keys[(int)(((long)workerId * options.ParallelOperationsPerWorker + index) % scenario.Keys.Length)];
                        parameters[workerId].Value = key;
                        using SqliteDataReader reader = commands[workerId].ExecuteReader();
                        if (!reader.Read())
                            throw new InvalidDataException("SQLite parallel read missed an existing key.");
                        checksum = AddChecksum(checksum, key, (byte[])reader.GetValue(0));
                        count++;
                    }
                    return (count, checksum, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
                });
            }
            ready.Wait();
            var stopwatch = Stopwatch.StartNew(); start.Set();
            if (!Task.WaitAll(tasks, TimeSpan.FromMinutes(5)))
                throw new TimeoutException("SQLite parallel point reads timed out.");
            stopwatch.Stop();
            long returned = tasks.Sum(static task => task.Result.Count);
            long checksum = tasks.Aggregate(0L, static (sum, task) => unchecked(sum + task.Result.Checksum));
            Verify(scenario, options, returned, checksum);
            return PointReadMeasurement.Success(scenario.Name, "SQLite", round,
                (long)options.Parallelism * options.ParallelOperationsPerWorker, returned, checksum,
                stopwatch.Elapsed.TotalMilliseconds, tasks.Sum(static task => task.Result.Allocated),
                GC.CollectionCount(0) - gc0, GC.CollectionCount(1) - gc1,
                GC.CollectionCount(2) - gc2, 0, 0, 0);
        }
        finally
        {
            foreach (SqliteCommand command in commands) command?.Dispose();
            foreach (SqliteConnection connection in connections) connection?.Dispose();
        }
    }

    private static void Verify(PointScenario scenario, PointReadAuditOptions options,
        long returned, long checksum)
    {
        long expectedReturned;
        long expectedChecksum = 0;
        if (scenario.Parallel)
        {
            expectedReturned = (long)options.Parallelism * options.ParallelOperationsPerWorker;
            for (int worker = 0; worker < options.Parallelism; worker++)
                for (int index = 0; index < options.ParallelOperationsPerWorker; index++)
                {
                    long key = scenario.Keys[(int)(((long)worker * options.ParallelOperationsPerWorker + index) % scenario.Keys.Length)];
                    expectedChecksum = AddChecksum(expectedChecksum, key,
                        CreatePayloadForKey(key, options.PayloadBytes));
                }
        }
        else
        {
            expectedReturned = 0;
            foreach (long key in scenario.Keys)
            {
                if (key < 0 || key >= options.Records) continue;
                expectedReturned++;
                expectedChecksum = AddChecksum(expectedChecksum, key,
                    CreatePayloadForKey(key, options.PayloadBytes));
            }
        }
        if (returned != expectedReturned || checksum != expectedChecksum)
            throw new InvalidDataException($"Point-read oracle mismatch: {returned}/{checksum} != {expectedReturned}/{expectedChecksum}.");
    }

    private static void BuildDbreezeFixture(string path, int records, byte[][] payloads)
    {
        Directory.CreateDirectory(path);
        using var engine = new DBreezeEngine(path);
        using var transaction = engine.GetTransaction();
        for (long key = 0; key < records; key++)
            transaction.Insert(Table, key, payloads[(int)(key & 1023)]);
        transaction.Commit();
    }

    private static void BuildSqliteFixture(string file, PointReadAuditOptions options, byte[][] payloads)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using SqliteConnection connection = OpenSqlite(file, options, create: true);
        ExecuteNonQuery(connection, "CREATE TABLE kv (k INTEGER NOT NULL PRIMARY KEY, v BLOB NOT NULL);");
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO kv(k,v) VALUES($k,$v);";
        SqliteParameter keyParameter = command.Parameters.Add("$k", SqliteType.Integer);
        SqliteParameter valueParameter = command.Parameters.Add("$v", SqliteType.Blob);
        command.Prepare();
        using (SqliteTransaction transaction = connection.BeginTransaction())
        {
            command.Transaction = transaction;
            for (long key = 0; key < options.Records; key++)
            {
                keyParameter.Value = key;
                valueParameter.Value = payloads[(int)(key & 1023)];
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        ExecuteNonQuery(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
    }

    private static void WarmUp(string dbreezePath, string sqliteFile,
        PointReadAuditOptions options, long[] keys)
    {
        var scenario = new PointScenario("warmup", keys, false);
        MeasureDbreeze(dbreezePath, scenario, options, true, 0);
        MeasureSqlite(sqliteFile, scenario, options, 0);
    }

    private static SqliteConnection OpenSqlite(string file, PointReadAuditOptions options, bool create)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = file,
            Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        ExecuteNonQuery(connection, "PRAGMA busy_timeout=5000;");
        if (create) ExecuteNonQuery(connection, "PRAGMA journal_mode=WAL;");
        ExecuteNonQuery(connection, "PRAGMA synchronous=FULL;");
        return connection;
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static byte[][] CreatePayloadPool(int size)
    {
        var pool = new byte[1024][];
        for (int index = 0; index < pool.Length; index++)
            pool[index] = CreatePayload(index, size);
        return pool;
    }

    private static byte[] CreatePayloadForKey(long key, int size) => CreatePayload((int)(key & 1023), size);
    private static byte[] CreatePayload(int index, int size)
    {
        var value = new byte[size];
        uint state = unchecked((uint)(Seed + index * 2654435761u));
        for (int offset = 0; offset < value.Length; offset++)
        {
            state = unchecked(state * 1664525u + 1013904223u);
            value[offset] = (byte)(state >> 24);
        }
        return value;
    }

    private static long AddChecksum(long checksum, long key, byte[] value)
    {
        int middle = value.Length / 2;
        long mixed = unchecked(key * 6364136223846793005L + value.Length * 1442695040888963407L);
        mixed ^= value[0]; mixed = unchecked(mixed * 1099511628211L) ^ value[middle];
        mixed = unchecked(mixed * 1099511628211L) ^ value[^1];
        return unchecked(checksum + mixed);
    }

    private static void Shuffle(long[] values)
    {
        var random = new Random(Seed);
        for (int index = values.Length - 1; index > 0; index--)
        {
            int other = random.Next(index + 1);
            (values[index], values[other]) = (values[other], values[index]);
        }
    }

    private static void ReadCacheDiagnostics(out long hits, out long misses, out long retained)
    {
        hits = misses = retained = 0;
        Type type = typeof(DBreezeEngine).Assembly.GetType(
            "DBreeze.LianaTrie.CommittedReadNodeCacheRegistry", throwOnError: false);
        MethodInfo method = type?.GetMethod("GetDiagnostics",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (method?.Invoke(null, null) is long[] values && values.Length >= 3)
        {
            hits = values[0]; misses = values[1]; retained = values[2];
        }
    }

    private static void Summarize(PointReadAuditReport report)
    {
        report.Summaries = report.Measurements.Where(static value => value.Succeeded)
            .GroupBy(static value => (value.Scenario, value.Provider))
            .Select(group => new PointReadSummary
            {
                Scenario = group.Key.Scenario,
                Provider = group.Key.Provider,
                MedianMilliseconds = Median(group.Select(static value => value.ElapsedMilliseconds)),
                MedianOperationsPerSecond = Median(group.Select(static value => value.OperationsPerSecond)),
                MedianBytesPerOperation = Median(group.Select(static value => value.BytesPerOperation)),
            }).OrderBy(static value => value.Scenario).ThenBy(static value => value.Provider).ToList();
        foreach (PointReadSummary summary in report.Summaries.Where(static value => value.Provider != "SQLite"))
        {
            PointReadSummary sqlite = report.Summaries.Single(value =>
                value.Scenario == summary.Scenario && value.Provider == "SQLite");
            summary.ThroughputVsSqlite = summary.MedianOperationsPerSecond / sqlite.MedianOperationsPerSecond;
        }
    }

    private static void LoadControl(PointReadAuditOptions options, PointReadAuditReport report)
    {
        if (!File.Exists(options.ControlJson))
        {
            if (options.Smoke)
                return;
            throw new FileNotFoundException("Pre-change control JSON was not found.", options.ControlJson);
        }

        SqliteComparisonReport control = JsonSerializer.Deserialize<SqliteComparisonReport>(
            File.ReadAllText(options.ControlJson), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? throw new InvalidDataException("Pre-change control JSON is empty.");
        if (!control.Succeeded || control.Configuration.Records != options.Records ||
            control.Configuration.PayloadBytes != options.PayloadBytes ||
            control.Configuration.Parallelism != options.Parallelism)
        {
            if (options.Smoke)
                return;
            throw new InvalidDataException("Pre-change control configuration does not match this audit.");
        }

        string[] scenarios =
        {
            "Random point reads (hits)",
            "Mixed point reads (90% hits)",
            "Parallel point reads",
        };
        report.ControlSummaries = control.Summaries
            .Where(summary => scenarios.Contains(summary.Scenario) &&
                (summary.Provider == "DBreeze" || summary.Provider == "SQLite"))
            .Select(summary => new PointReadControlSummary
            {
                Scenario = summary.Scenario,
                Provider = summary.Provider,
                MedianOperationsPerSecond = summary.MedianOperationsPerSecond,
            }).ToList();
        if (report.ControlSummaries.Count != scenarios.Length * 2)
            throw new InvalidDataException("Pre-change control is missing point-read summaries.");
    }

    private static bool EvaluateGates(PointReadAuditReport report)
    {
        bool passed = true;
        foreach (string scenario in new[] { "Random hits", "Mixed 90/10", "Parallel hits" })
        {
            if (!report.Summaries.Any(value => value.Scenario == scenario))
                continue;
            PointReadSummary lazy = report.Summaries.SingleOrDefault(value =>
                value.Scenario == scenario && value.Provider == "DBreeze lazy");
            if (lazy == null || lazy.ThroughputVsSqlite < 0.85)
            {
                report.GateViolations.Add($"{scenario}: DBreeze lazy throughput is {lazy?.ThroughputVsSqlite:P1}; required >= 85% SQLite.");
                passed = false;
            }
        }
        PointReadSummary hits = report.Summaries.SingleOrDefault(static value =>
            value.Scenario == "Random hits" && value.Provider == "DBreeze lazy");
        if (hits != null && hits.MedianBytesPerOperation > 2048)
        {
            report.GateViolations.Add($"Random hits: DBreeze lazy allocation is {hits?.MedianBytesPerOperation:N0} B/op; required <= 2,048 B/op.");
            passed = false;
        }
        return passed;
    }

    private static double Median(IEnumerable<double> source)
    {
        double[] values = source.OrderBy(static value => value).ToArray();
        if (values.Length == 0) return Double.NaN;
        int middle = values.Length / 2;
        return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / 2 : values[middle];
    }

    private static void Persist(PointReadAuditReport report)
    {
        AuditPersistence.WriteJson(report.RawJson, report);
        AuditPersistence.WriteTextAtomic(report.RawCsv, BuildCsv(report));
        string html = BuildHtml(report);
        AuditPersistence.WriteTextAtomic(report.ImmutableHtml, html);
        AuditPersistence.WriteTextAtomic(report.CanonicalHtml, html);
    }

    private static string BuildCsv(PointReadAuditReport report)
    {
        var b = new StringBuilder("scenario,provider,round,elapsed_ms,ops_per_second,bytes_per_operation,returned,checksum,cache_hits,cache_misses,retained_bytes,process_private_bytes,peak_working_set_bytes,succeeded\n");
        foreach (PointReadMeasurement m in report.Measurements)
            b.Append(Csv(m.Scenario)).Append(',').Append(Csv(m.Provider)).Append(',').Append(m.Round).Append(',')
                .Append(m.ElapsedMilliseconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(m.OperationsPerSecond.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(m.BytesPerOperation.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(m.Returned).Append(',').Append(m.Checksum).Append(',').Append(m.CacheHits).Append(',')
                .Append(m.CacheMisses).Append(',').Append(m.CacheRetainedBytes).Append(',')
                .Append(m.ProcessPrivateBytes).Append(',').Append(m.PeakWorkingSetBytes).Append(',')
                .Append(m.Succeeded).AppendLine();
        return b.ToString();
    }

    private static string BuildHtml(PointReadAuditReport report)
    {
        static string H(string value) => System.Net.WebUtility.HtmlEncode(value ?? String.Empty);
        var b = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><title>DBreeze Point Read Audit</title><style>body{font:14px Segoe UI,Arial;margin:28px;color:#18202a}table{border-collapse:collapse;width:100%;margin:14px 0}th,td{border:1px solid #ccd3da;padding:6px;text-align:right}th:first-child,td:first-child,th:nth-child(2),td:nth-child(2){text-align:left}.pass{color:#087830}.fail{color:#b00020}code{background:#f3f5f7;padding:2px 4px}</style></head><body>");
        bool pass = report.CorrectnessPassed && report.PerformancePassed;
        b.Append("<h1>DBreeze Point Read Audit</h1><h2 class=\"")
            .Append(pass ? "pass\">PASS" : "fail\">FAIL").Append("</h2>")
            .Append("<p>Run <code>").Append(H(report.RunId)).Append("</code>; records ").Append(report.Records.ToString("N0"))
            .Append(", payload ").Append(report.PayloadBytes).Append(" bytes, rounds ").Append(report.Rounds)
            .Append(", parallelism ").Append(report.Parallelism).Append(".</p>")
            .Append("<p>Git <code>").Append(H(report.GitHead)).Append("</code>")
            .Append(report.GitDirty ? "; dirty fingerprint <code>" + H(report.GitStatusSha256) + "</code>" : "; clean")
            .Append(". DBreeze SHA-256 <code>").Append(H(report.DBreezeSha256)).Append("</code>.</p>")
            .Append("<p>Control source: <code>").Append(H(report.ControlJson)).Append("</code>. Warm OS cache; fixture construction excluded.</p>")
            .Append("<h2>Median results</h2><table><thead><tr><th>Scenario</th><th>Provider</th><th>ms</th><th>ops/s</th><th>B/op</th><th>vs SQLite</th></tr></thead><tbody>");
        foreach (PointReadSummary s in report.Summaries)
            b.Append("<tr><td>").Append(H(s.Scenario)).Append("</td><td>").Append(H(s.Provider)).Append("</td><td>")
                .Append(s.MedianMilliseconds.ToString("N2")).Append("</td><td>").Append(s.MedianOperationsPerSecond.ToString("N0"))
                .Append("</td><td>").Append(s.MedianBytesPerOperation.ToString("N0")).Append("</td><td>")
                .Append(Double.IsNaN(s.ThroughputVsSqlite) ? "—" : s.ThroughputVsSqlite.ToString("P1")).Append("</td></tr>");
        b.Append("</tbody></table><h2>Pre-change control vs current</h2><table><thead><tr><th>Scenario</th><th>Old DBreeze</th><th>Old SQLite</th><th>Current DBreeze lazy</th><th>Current SQLite</th><th>DBreeze speedup</th></tr></thead><tbody>");
        foreach ((string current, string control) in new[]
        {
            ("Random hits", "Random point reads (hits)"),
            ("Mixed 90/10", "Mixed point reads (90% hits)"),
            ("Parallel hits", "Parallel point reads"),
        })
        {
            PointReadControlSummary oldDb = report.ControlSummaries.SingleOrDefault(value => value.Scenario == control && value.Provider == "DBreeze");
            PointReadControlSummary oldSqlite = report.ControlSummaries.SingleOrDefault(value => value.Scenario == control && value.Provider == "SQLite");
            PointReadSummary newDb = report.Summaries.SingleOrDefault(value => value.Scenario == current && value.Provider == "DBreeze lazy");
            PointReadSummary newSqlite = report.Summaries.SingleOrDefault(value => value.Scenario == current && value.Provider == "SQLite");
            if (oldDb == null || oldSqlite == null || newDb == null || newSqlite == null)
                continue;
            b.Append("<tr><td>").Append(H(current)).Append("</td><td>").Append(oldDb.MedianOperationsPerSecond.ToString("N0"))
                .Append("</td><td>").Append(oldSqlite.MedianOperationsPerSecond.ToString("N0"))
                .Append("</td><td>").Append(newDb.MedianOperationsPerSecond.ToString("N0"))
                .Append("</td><td>").Append(newSqlite.MedianOperationsPerSecond.ToString("N0"))
                .Append("</td><td>").Append((newDb.MedianOperationsPerSecond / oldDb.MedianOperationsPerSecond).ToString("N2"))
                .Append("×</td></tr>");
        }
        b.Append("</tbody></table><h2>Gates</h2><ul>");
        if (report.GateViolations.Count == 0) b.Append("<li class=\"pass\">All performance gates passed.</li>");
        foreach (string failure in report.GateViolations) b.Append("<li class=\"fail\">").Append(H(failure)).Append("</li>");
        foreach (string failure in report.Failures) b.Append("<li class=\"fail\">").Append(H(failure)).Append("</li>");
        b.Append("</ul><h2>Per round</h2><table><thead><tr><th>Scenario</th><th>Provider</th><th>Round</th><th>ms</th><th>ops/s</th><th>B/op</th><th>GC 0/1/2</th><th>Cache hit/miss</th><th>Retained</th><th>Private bytes</th><th>Peak working set</th></tr></thead><tbody>");
        foreach (PointReadMeasurement m in report.Measurements)
            b.Append("<tr><td>").Append(H(m.Scenario)).Append("</td><td>").Append(H(m.Provider)).Append("</td><td>").Append(m.Round)
                .Append("</td><td>").Append(m.ElapsedMilliseconds.ToString("N2")).Append("</td><td>").Append(m.OperationsPerSecond.ToString("N0"))
                .Append("</td><td>").Append(m.BytesPerOperation.ToString("N0")).Append("</td><td>").Append($"{m.Gen0}/{m.Gen1}/{m.Gen2}")
                .Append("</td><td>").Append($"{m.CacheHits:N0}/{m.CacheMisses:N0}").Append("</td><td>").Append(m.CacheRetainedBytes.ToString("N0"))
                .Append("</td><td>").Append(m.ProcessPrivateBytes.ToString("N0")).Append("</td><td>").Append(m.PeakWorkingSetBytes.ToString("N0")).Append("</td></tr>");
        b.Append("</tbody></table><p>DB sizes: DBreeze ").Append(report.DbreezeBytes.ToString("N0")).Append(" B; SQLite ")
            .Append(report.SqliteBytes.ToString("N0")).Append(" B. Raw artifacts: <code>").Append(H(Path.GetDirectoryName(report.RawJson))).Append("</code>.</p></body></html>");
        return b.ToString();
    }

    private static string Csv(string value) => '"' + (value ?? String.Empty).Replace("\"", "\"\"") + '"';
    private static long DirectoryBytes(string path) => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(static file => new FileInfo(file).Length);
    private static void StabilizeGc() { GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true); GC.WaitForPendingFinalizers(); GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true); }

    private sealed record PointScenario(string Name, long[] Keys, bool Parallel);
}

internal sealed class PointReadAuditOptions
{
    internal string RootPath { get; private set; } = @"D:\Temp\DbreezeDbTest";
    internal string ReportPath { get; private set; }
    internal string RunId { get; private set; }
    internal string ControlJson { get; private set; } = @"D:\Temp\DbreezeDbTest\reports\20260826-171400-sqlite-rks-no-overwrite-update\DBreeze_vs_SQLite.json";
    internal int Records { get; private set; } = 1_000_000;
    internal int PayloadBytes { get; private set; } = 256;
    internal int Rounds { get; private set; } = 5;
    internal int Parallelism { get; private set; } = 4;
    internal int ParallelOperationsPerWorker { get; private set; } = 100_000;
    internal bool KeepDatabases { get; private set; }
    internal bool ParallelOnly { get; private set; }
    internal bool Smoke { get; private set; }

    internal static PointReadAuditOptions Parse(string[] args)
    {
        var result = new PointReadAuditOptions();
        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index].ToLowerInvariant();
            switch (arg)
            {
                case "--point-read-audit": break;
                case "--smoke": result.Smoke = true; result.Records = 10_000; result.Rounds = 1; result.ParallelOperationsPerWorker = 10_000; break;
                case "--keep-databases": result.KeepDatabases = true; break;
                case "--parallel-only": result.ParallelOnly = true; break;
                case "--root": result.RootPath = Read(args, ref index, arg); break;
                case "--report": result.ReportPath = Read(args, ref index, arg); break;
                case "--run-id": result.RunId = Read(args, ref index, arg); break;
                case "--control-json": result.ControlJson = Read(args, ref index, arg); break;
                case "--records": result.Records = ReadInt(args, ref index, arg, 1_000, 1_000_000); break;
                case "--payload-bytes": result.PayloadBytes = ReadInt(args, ref index, arg, 1, 65_536); break;
                case "--rounds": result.Rounds = ReadInt(args, ref index, arg, 1, 5); break;
                case "--parallelism": result.Parallelism = ReadInt(args, ref index, arg, 1, 8); break;
                case "--parallel-operations": result.ParallelOperationsPerWorker = ReadInt(args, ref index, arg, 1_000, 100_000); break;
                default: throw new ArgumentException("Unknown point-read audit option: " + args[index]);
            }
        }
        result.RootPath = Path.GetFullPath(result.RootPath);
        result.ReportPath = Path.GetFullPath(result.ReportPath ?? Path.Combine(result.RootPath, "DBreeze_Point_Read_Audit.html"));
        result.ControlJson = Path.GetFullPath(result.ControlJson);
        AuditRunLayout.EnsureUnderRoot(result.ReportPath, result.RootPath);
        AuditRunLayout.EnsureUnderRoot(result.ControlJson, result.RootPath);
        result.RunId ??= DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-point-read";
        AuditRunLayout.ValidateLeafName(result.RunId, "--run-id");
        return result;
    }

    private static string Read(string[] args, ref int index, string option) =>
        ++index < args.Length && !String.IsNullOrWhiteSpace(args[index]) ? args[index] : throw new ArgumentException(option + " requires a value.");
    private static int ReadInt(string[] args, ref int index, string option, int min, int max) =>
        Int32.TryParse(Read(args, ref index, option), NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value >= min && value <= max
            ? value : throw new ArgumentOutOfRangeException(option, $"Expected {min}..{max}.");
}

internal sealed class PointReadAuditReport
{
    public string RunId { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string Runtime { get; set; }
    public string OS { get; set; }
    public string DBreezeVersion { get; set; }
    public string DBreezeSha256 { get; set; }
    public string GitHead { get; set; }
    public string GitStatusSha256 { get; set; }
    public bool GitDirty { get; set; }
    public string ControlJson { get; set; }
    public int Records { get; set; }
    public int PayloadBytes { get; set; }
    public int Rounds { get; set; }
    public int Parallelism { get; set; }
    public int ParallelOperationsPerWorker { get; set; }
    public long DbreezeBytes { get; set; }
    public long SqliteBytes { get; set; }
    public string RawJson { get; set; }
    public string RawCsv { get; set; }
    public string ImmutableHtml { get; set; }
    public string CanonicalHtml { get; set; }
    public bool CorrectnessPassed { get; set; }
    public bool PerformancePassed { get; set; }
    public List<PointReadMeasurement> Measurements { get; set; } = new();
    public List<PointReadSummary> Summaries { get; set; } = new();
    public List<PointReadControlSummary> ControlSummaries { get; set; } = new();
    public List<string> Failures { get; set; } = new();
    public List<string> GateViolations { get; set; } = new();

    internal static PointReadAuditReport Create(PointReadAuditOptions options, AuditRunLayout layout)
    {
        Assembly assembly = typeof(DBreezeEngine).Assembly;
        string location = assembly.Location;
        string gitHead = Git("rev-parse", "HEAD").Trim();
        string gitStatus = Git("status", "--porcelain=v1", "--untracked-files=all");
        return new PointReadAuditReport
        {
            RunId = options.RunId, StartedUtc = DateTime.UtcNow,
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            DBreezeVersion = assembly.GetName().Version?.ToString(),
            DBreezeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(location))).ToLowerInvariant(),
            GitHead = gitHead,
            GitDirty = !String.IsNullOrWhiteSpace(gitStatus),
            GitStatusSha256 = String.IsNullOrEmpty(gitStatus) ? String.Empty :
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(gitStatus))).ToLowerInvariant(),
            ControlJson = options.ControlJson, Records = options.Records, PayloadBytes = options.PayloadBytes,
            Rounds = options.Rounds, Parallelism = options.Parallelism,
            ParallelOperationsPerWorker = options.ParallelOperationsPerWorker,
            RawJson = Path.Combine(layout.ReportsDirectory, "DBreeze_Point_Read_Audit.json"),
            RawCsv = Path.Combine(layout.ReportsDirectory, "DBreeze_Point_Read_Audit.csv"),
            ImmutableHtml = Path.Combine(layout.ReportsDirectory, "DBreeze_Point_Read_Audit.html"),
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

internal sealed class PointReadMeasurement
{
    public string Scenario { get; set; }
    public string Provider { get; set; }
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
    public long CacheHits { get; set; }
    public long CacheMisses { get; set; }
    public long CacheRetainedBytes { get; set; }
    public long ProcessPrivateBytes { get; set; }
    public long PeakWorkingSetBytes { get; set; }
    public bool Succeeded { get; set; }
    public string Error { get; set; }

    internal static PointReadMeasurement Success(string scenario, string provider, int round,
        long operations, long returned, long checksum, double elapsed, long allocated,
        int gen0, int gen1, int gen2, long hits, long misses, long retained)
    {
        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        return new PointReadMeasurement
        {
            Scenario = scenario, Provider = provider, Round = round, Operations = operations,
            Returned = returned, Checksum = checksum, ElapsedMilliseconds = elapsed,
            OperationsPerSecond = operations * 1000.0 / elapsed, AllocatedBytes = allocated,
            BytesPerOperation = allocated / (double)operations, Gen0 = gen0, Gen1 = gen1, Gen2 = gen2,
            CacheHits = hits, CacheMisses = misses, CacheRetainedBytes = retained,
            ProcessPrivateBytes = process.PrivateMemorySize64,
            PeakWorkingSetBytes = process.PeakWorkingSet64,
            Succeeded = true,
        };
    }
}

internal sealed class PointReadSummary
{
    public string Scenario { get; set; }
    public string Provider { get; set; }
    public double MedianMilliseconds { get; set; }
    public double MedianOperationsPerSecond { get; set; }
    public double MedianBytesPerOperation { get; set; }
    public double ThroughputVsSqlite { get; set; } = Double.NaN;
}

internal sealed class PointReadControlSummary
{
    public string Scenario { get; set; }
    public string Provider { get; set; }
    public double MedianOperationsPerSecond { get; set; }
}
