using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DBreeze;
using DBreeze.DataTypes;
using DBreeze.Transactions;
using Microsoft.Data.Sqlite;

namespace DBreeze.Net8.Benchmarks;

internal static class TraversalReadAudit
{
    private const string MainTable = "kv";
    private const string DBreezeProvider = "DBreeze";
    private const string SqliteProvider = "SQLite";
    private const int PayloadPoolSize = 1024;
    private const int Seed = 20260826;

    internal static int Run(string[] args)
    {
        TraversalReadAuditOptions options;
        try
        {
            options = TraversalReadAuditOptions.Parse(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Traversal-read audit configuration error: " + exception.Message);
            return 2;
        }

        var layout = new AuditRunLayout(options.RootPath, options.RunId);
        TraversalReadAuditReport report = TraversalReadAuditReport.Create(options, layout);
        try
        {
            layout.Create();
            Execute(options, layout, report);
        }
        catch (Exception exception)
        {
            report.Failures.Add("Fatal traversal-read audit failure: " + exception);
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
                report.ScratchCleaned = true;
            }
            catch (Exception exception)
            {
                report.Failures.Add("Owned scratch cleanup failed: " + exception.Message);
                report.CorrectnessPassed = false;
            }
            Persist(report);
        }

        bool passed = report.CorrectnessPassed && report.PerformancePassed;
        Console.WriteLine($"Traversal-read audit {(passed ? "PASS" : "FAIL")}: {options.ReportPath}");
        return passed ? 0 : 1;
    }

    private static void Execute(TraversalReadAuditOptions options, AuditRunLayout layout,
        TraversalReadAuditReport report)
    {
        byte[][] payloads = CreatePayloadPool(options.PayloadBytes);
        string dbreezePath = Path.Combine(layout.ScratchDirectory, "fixture-dbreeze");
        string sqlitePath = Path.Combine(layout.ScratchDirectory, "fixture-sqlite");
        BuildDbreezeFixture(dbreezePath, options.Records, payloads);
        BuildSqliteFixture(sqlitePath, options, payloads);
        report.DBreezeDatabaseBytes = DirectoryBytes(dbreezePath);
        report.SqliteDatabaseBytes = DirectoryBytes(sqlitePath);
        report.DBreezeManifestBefore = BuildManifest(dbreezePath);

        WarmUp(layout.ScratchDirectory, options, payloads);
        for (int round = 1; round <= options.Rounds; round++)
        {
            RunPair(report, "Full forward traversal", round,
                () => MeasureDbreezeFull(dbreezePath, options, payloads, forward: true, ReadMode.Eager),
                () => MeasureSqliteFull(sqlitePath, options, payloads, forward: true));
            RunPair(report, "Full backward traversal", round,
                () => MeasureDbreezeFull(dbreezePath, options, payloads, forward: false, ReadMode.Eager),
                () => MeasureSqliteFull(sqlitePath, options, payloads, forward: false));
            RunPair(report, "Bounded ranges", round,
                () => MeasureDbreezeRanges(dbreezePath, options, payloads),
                () => MeasureSqliteRanges(sqlitePath, options, payloads));

            Measure(report, "Diagnostic forward lazy-consumed", DBreezeProvider, round,
                () => MeasureDbreezeFull(dbreezePath, options, payloads, forward: true, ReadMode.LazyConsumed));
            Measure(report, "Diagnostic backward lazy-consumed", DBreezeProvider, round,
                () => MeasureDbreezeFull(dbreezePath, options, payloads, forward: false, ReadMode.LazyConsumed));
            Measure(report, "Diagnostic forward key-only", DBreezeProvider, round,
                () => MeasureDbreezeFull(dbreezePath, options, payloads, forward: true, ReadMode.KeyOnly));
            Measure(report, "Diagnostic backward key-only", DBreezeProvider, round,
                () => MeasureDbreezeFull(dbreezePath, options, payloads, forward: false, ReadMode.KeyOnly));
        }

        report.DBreezeManifestAfter = BuildManifest(dbreezePath);
        if (!ManifestEquals(report.DBreezeManifestBefore, report.DBreezeManifestAfter))
            report.Failures.Add("DBreeze read-only fixture length or SHA-256 changed during the audit.");
    }

    private static void RunPair(TraversalReadAuditReport report, string scenario, int round,
        Func<TraversalReadOutcome> dbreeze, Func<TraversalReadOutcome> sqlite)
    {
        if ((round & 1) != 0)
        {
            Measure(report, scenario, DBreezeProvider, round, dbreeze);
            Measure(report, scenario, SqliteProvider, round, sqlite);
        }
        else
        {
            Measure(report, scenario, SqliteProvider, round, sqlite);
            Measure(report, scenario, DBreezeProvider, round, dbreeze);
        }
    }

    private static void Measure(TraversalReadAuditReport report, string scenario, string provider, int round,
        Func<TraversalReadOutcome> action)
    {
        Console.WriteLine($"START {scenario} / {provider} / round {round}");
        var measurement = new TraversalReadMeasurement
        {
            Scenario = scenario,
            Provider = provider,
            Round = round,
        };
        try
        {
            StabilizeGc();
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            TraversalReadOutcome outcome = action();
            measurement.Operations = outcome.Operations;
            measurement.Returned = outcome.Returned;
            measurement.Checksum = outcome.Checksum;
            measurement.ElapsedMilliseconds = outcome.ElapsedMilliseconds;
            measurement.OperationsPerSecond = outcome.Operations * 1000.0 / outcome.ElapsedMilliseconds;
            measurement.AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            measurement.BytesPerOperation = (double)measurement.AllocatedBytes / outcome.Operations;
            measurement.Gen0 = GC.CollectionCount(0) - gen0;
            measurement.Gen1 = GC.CollectionCount(1) - gen1;
            measurement.Gen2 = GC.CollectionCount(2) - gen2;
            using Process process = Process.GetCurrentProcess();
            measurement.PrivateBytes = process.PrivateMemorySize64;
            measurement.PeakWorkingSetBytes = process.PeakWorkingSet64;
            measurement.Succeeded = outcome.Operations > 0 && outcome.Returned == outcome.Operations &&
                outcome.ElapsedMilliseconds > 0;
            if (!measurement.Succeeded)
                throw new InvalidOperationException("Measurement returned an invalid operation count or elapsed time.");
            Console.WriteLine($"PASS  {scenario} / {provider} / round {round}: " +
                $"{measurement.ElapsedMilliseconds:F2} ms, {measurement.OperationsPerSecond:N0} ops/s, " +
                $"{measurement.BytesPerOperation:N1} B/op");
        }
        catch (Exception exception)
        {
            measurement.Succeeded = false;
            measurement.Error = exception.ToString();
            report.Failures.Add($"{scenario} / {provider} / round {round}: {exception.Message}");
        }
        report.Measurements.Add(measurement);
    }

    private static TraversalReadOutcome MeasureDbreezeFull(string path, TraversalReadAuditOptions options,
        byte[][] payloads, bool forward, ReadMode mode)
    {
        long returned = 0;
        long checksum = 0;
        double elapsed;
        using (var engine = new DBreezeEngine(path))
        {
            var stopwatch = Stopwatch.StartNew();
            using (Transaction transaction = engine.GetTransaction())
            {
                transaction.ValuesLazyLoadingIsOn = mode != ReadMode.Eager;
                IEnumerable<Row<long, byte[]>> rows = forward
                    ? transaction.SelectForward<long, byte[]>(MainTable)
                    : transaction.SelectBackward<long, byte[]>(MainTable);
                foreach (Row<long, byte[]> row in rows)
                {
                    long expected = forward ? returned : options.Records - returned - 1L;
                    if (row.Key != expected)
                        throw new InvalidDataException($"DBreeze traversal order mismatch: {row.Key} != {expected}.");
                    checksum = mode == ReadMode.KeyOnly
                        ? AddKeyChecksum(checksum, row.Key)
                        : AddChecksum(checksum, row.Key, row.Value);
                    returned++;
                }
            }
            stopwatch.Stop();
            elapsed = stopwatch.Elapsed.TotalMilliseconds;
        }

        long expectedChecksum = mode == ReadMode.KeyOnly
            ? ExpectedKeyChecksum(options.Records)
            : ExpectedValueChecksum(options.Records, payloads);
        VerifyOutcome(options.Records, returned, checksum, expectedChecksum, "DBreeze full traversal");
        return new TraversalReadOutcome(options.Records, returned, checksum, elapsed);
    }

    private static TraversalReadOutcome MeasureSqliteFull(string path, TraversalReadAuditOptions options,
        byte[][] payloads, bool forward)
    {
        long returned = 0;
        long checksum = 0;
        double elapsed;
        using (SqliteConnection connection = OpenSqlite(Path.Combine(path, "database.sqlite"), options, false))
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = forward ? "SELECT k,v FROM kv ORDER BY k;" : "SELECT k,v FROM kv ORDER BY k DESC;";
            var stopwatch = Stopwatch.StartNew();
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                command.Transaction = transaction;
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long key = reader.GetInt64(0);
                    long expected = forward ? returned : options.Records - returned - 1L;
                    if (key != expected)
                        throw new InvalidDataException($"SQLite traversal order mismatch: {key} != {expected}.");
                    checksum = AddChecksum(checksum, key, (byte[])reader.GetValue(1));
                    returned++;
                }
            }
            stopwatch.Stop();
            elapsed = stopwatch.Elapsed.TotalMilliseconds;
        }
        VerifyOutcome(options.Records, returned, checksum, ExpectedValueChecksum(options.Records, payloads),
            "SQLite full traversal");
        return new TraversalReadOutcome(options.Records, returned, checksum, elapsed);
    }

    private static TraversalReadOutcome MeasureDbreezeRanges(string path, TraversalReadAuditOptions options,
        byte[][] payloads)
    {
        (int rangeCount, int rangeSize) = GetRangeShape(options.Records);
        long returned = 0;
        long checksum = 0;
        double elapsed;
        using (var engine = new DBreezeEngine(path))
        {
            var stopwatch = Stopwatch.StartNew();
            using (Transaction transaction = engine.GetTransaction())
            {
                transaction.ValuesLazyLoadingIsOn = false;
                for (int range = 0; range < rangeCount; range++)
                {
                    (long start, long stop) = GetRange(options.Records, rangeCount, rangeSize, range);
                    long expected = start;
                    foreach (Row<long, byte[]> row in transaction.SelectForwardFromTo<long, byte[]>(
                                 MainTable, start, true, stop, true))
                    {
                        if (row.Key != expected++)
                            throw new InvalidDataException("DBreeze bounded-range ordering mismatch.");
                        checksum = AddChecksum(checksum, row.Key, row.Value);
                        returned++;
                    }
                    if (expected != stop + 1)
                        throw new InvalidDataException("DBreeze bounded-range count mismatch.");
                }
            }
            stopwatch.Stop();
            elapsed = stopwatch.Elapsed.TotalMilliseconds;
        }
        long operations = (long)rangeCount * rangeSize;
        VerifyRangeOutcome(options.Records, rangeCount, rangeSize, payloads, returned, checksum);
        return new TraversalReadOutcome(operations, returned, checksum, elapsed);
    }

    private static TraversalReadOutcome MeasureSqliteRanges(string path, TraversalReadAuditOptions options,
        byte[][] payloads)
    {
        (int rangeCount, int rangeSize) = GetRangeShape(options.Records);
        long returned = 0;
        long checksum = 0;
        double elapsed;
        using (SqliteConnection connection = OpenSqlite(Path.Combine(path, "database.sqlite"), options, false))
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT k,v FROM kv WHERE k >= $start AND k <= $stop ORDER BY k;";
            SqliteParameter startParameter = command.Parameters.Add("$start", SqliteType.Integer);
            SqliteParameter stopParameter = command.Parameters.Add("$stop", SqliteType.Integer);
            command.Prepare();
            var stopwatch = Stopwatch.StartNew();
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                command.Transaction = transaction;
                for (int range = 0; range < rangeCount; range++)
                {
                    (long start, long stop) = GetRange(options.Records, rangeCount, rangeSize, range);
                    startParameter.Value = start;
                    stopParameter.Value = stop;
                    long expected = start;
                    using SqliteDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        long key = reader.GetInt64(0);
                        if (key != expected++)
                            throw new InvalidDataException("SQLite bounded-range ordering mismatch.");
                        checksum = AddChecksum(checksum, key, (byte[])reader.GetValue(1));
                        returned++;
                    }
                    if (expected != stop + 1)
                        throw new InvalidDataException("SQLite bounded-range count mismatch.");
                }
            }
            stopwatch.Stop();
            elapsed = stopwatch.Elapsed.TotalMilliseconds;
        }
        long operations = (long)rangeCount * rangeSize;
        VerifyRangeOutcome(options.Records, rangeCount, rangeSize, payloads, returned, checksum);
        return new TraversalReadOutcome(operations, returned, checksum, elapsed);
    }

    private static void WarmUp(string scratchRoot, TraversalReadAuditOptions options, byte[][] payloads)
    {
        int records = Math.Min(2_000, options.Records);
        string dbreeze = Path.Combine(scratchRoot, "warmup-dbreeze");
        string sqlite = Path.Combine(scratchRoot, "warmup-sqlite");
        BuildDbreezeFixture(dbreeze, records, payloads);
        var warmOptions = options.WithRecords(records);
        BuildSqliteFixture(sqlite, warmOptions, payloads);
        _ = MeasureDbreezeFull(dbreeze, warmOptions, payloads, true, ReadMode.Eager);
        _ = MeasureDbreezeFull(dbreeze, warmOptions, payloads, false, ReadMode.LazyConsumed);
        _ = MeasureSqliteFull(sqlite, warmOptions, payloads, true);
        _ = MeasureDbreezeRanges(dbreeze, warmOptions, payloads);
        _ = MeasureSqliteRanges(sqlite, warmOptions, payloads);
        AuditRunLayout.DeleteOwnedChild(dbreeze, scratchRoot);
        AuditRunLayout.DeleteOwnedChild(sqlite, scratchRoot);
    }

    private static void BuildDbreezeFixture(string path, int records, byte[][] payloads)
    {
        Directory.CreateDirectory(path);
        using var engine = new DBreezeEngine(path);
        using Transaction transaction = engine.GetTransaction();
        for (long key = 0; key < records; key++)
            transaction.Insert(MainTable, key, Payload(payloads, key));
        transaction.Commit();
    }

    private static void BuildSqliteFixture(string path, TraversalReadAuditOptions options, byte[][] payloads)
    {
        Directory.CreateDirectory(path);
        using SqliteConnection connection = OpenSqlite(Path.Combine(path, "database.sqlite"), options, true);
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
                valueParameter.Value = Payload(payloads, key);
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidDataException("SQLite fixture insert did not affect one row.");
            }
            transaction.Commit();
        }
        ExecuteNonQuery(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
    }

    private static SqliteConnection OpenSqlite(string file, TraversalReadAuditOptions options, bool create)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = file,
            Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Default,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        ExecuteNonQuery(connection, "PRAGMA busy_timeout=5000;");
        if (create)
        {
            string journal = Convert.ToString(ExecuteScalar(connection, "PRAGMA journal_mode=WAL;"),
                CultureInfo.InvariantCulture) ?? String.Empty;
            if (!String.Equals(journal, "wal", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SQLite refused WAL journal mode: " + journal);
        }
        ExecuteNonQuery(connection, "PRAGMA synchronous=" + options.SqliteSynchronous + ";");
        int actual = Convert.ToInt32(ExecuteScalar(connection, "PRAGMA synchronous;"), CultureInfo.InvariantCulture);
        int expected = options.SqliteSynchronous == "FULL" ? 2 : 1;
        if (actual != expected)
            throw new InvalidOperationException($"SQLite synchronous mismatch: {actual} != {expected}.");
        return connection;
    }

    private static object ExecuteScalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static byte[][] CreatePayloadPool(int size)
    {
        var result = new byte[PayloadPoolSize][];
        for (int index = 0; index < result.Length; index++)
        {
            var value = new byte[size];
            uint state = unchecked((uint)(Seed + index * 2654435761u));
            for (int offset = 0; offset < value.Length; offset++)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                value[offset] = (byte)(state >> 24);
            }
            result[index] = value;
        }
        return result;
    }

    private static byte[] Payload(byte[][] payloads, long key) => payloads[(int)(key & (PayloadPoolSize - 1))];

    private static long AddChecksum(long checksum, long key, byte[] value)
    {
        int middle = value.Length / 2;
        long mixed = unchecked(key * 6364136223846793005L + value.Length * 1442695040888963407L);
        mixed ^= value[0];
        mixed = unchecked(mixed * 1099511628211L) ^ value[middle];
        mixed = unchecked(mixed * 1099511628211L) ^ value[value.Length - 1];
        return unchecked(checksum + mixed);
    }

    private static long AddKeyChecksum(long checksum, long key) => unchecked(checksum + key * 397);

    private static long ExpectedValueChecksum(int records, byte[][] payloads)
    {
        long checksum = 0;
        for (long key = 0; key < records; key++)
            checksum = AddChecksum(checksum, key, Payload(payloads, key));
        return checksum;
    }

    private static long ExpectedKeyChecksum(int records)
    {
        long checksum = 0;
        for (long key = 0; key < records; key++)
            checksum = AddKeyChecksum(checksum, key);
        return checksum;
    }

    private static (int Count, int Size) GetRangeShape(int records)
    {
        int size = Math.Min(1000, Math.Max(10, records / 100));
        return (Math.Min(1000, Math.Max(1, records / size)), size);
    }

    private static (long Start, long Stop) GetRange(int records, int count, int size, int range)
    {
        long maximumStart = records - size;
        long start = count == 1 ? 0 : maximumStart * range / (count - 1L);
        return (start, start + size - 1L);
    }

    private static void VerifyRangeOutcome(int records, int rangeCount, int rangeSize, byte[][] payloads,
        long returned, long checksum)
    {
        long expectedCount = 0;
        long expectedChecksum = 0;
        for (int range = 0; range < rangeCount; range++)
        {
            (long start, long stop) = GetRange(records, rangeCount, rangeSize, range);
            for (long key = start; key <= stop; key++)
            {
                expectedCount++;
                expectedChecksum = AddChecksum(expectedChecksum, key, Payload(payloads, key));
            }
        }
        VerifyOutcome(expectedCount, returned, checksum, expectedChecksum, "Bounded ranges");
    }

    private static void VerifyOutcome(long expectedCount, long returned, long checksum,
        long expectedChecksum, string scenario)
    {
        if (returned != expectedCount || checksum != expectedChecksum)
            throw new InvalidDataException($"{scenario} oracle mismatch: count {returned}/{expectedCount}, checksum {checksum}/{expectedChecksum}.");
    }

    private static Dictionary<string, TraversalReadFileManifest> BuildManifest(string path)
    {
        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                value => Path.GetRelativePath(path, value).Replace(Path.DirectorySeparatorChar, '/'),
                value => new TraversalReadFileManifest
                {
                    Length = new FileInfo(value).Length,
                    Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(value))).ToLowerInvariant(),
                },
                StringComparer.Ordinal);
    }

    private static bool ManifestEquals(IReadOnlyDictionary<string, TraversalReadFileManifest> left,
        IReadOnlyDictionary<string, TraversalReadFileManifest> right) =>
        left.Count == right.Count && left.All(value => right.TryGetValue(value.Key, out TraversalReadFileManifest other) &&
            value.Value.Length == other.Length && String.Equals(value.Value.Sha256, other.Sha256, StringComparison.Ordinal));

    private static void Summarize(TraversalReadAuditReport report)
    {
        report.Summaries = report.Measurements.Where(static value => value.Succeeded)
            .GroupBy(static value => new { value.Scenario, value.Provider })
            .Select(group => new TraversalReadSummary
            {
                Scenario = group.Key.Scenario,
                Provider = group.Key.Provider,
                Rounds = group.Count(),
                MedianMilliseconds = Median(group.Select(static value => value.ElapsedMilliseconds)),
                MedianOperationsPerSecond = Median(group.Select(static value => value.OperationsPerSecond)),
                MedianBytesPerOperation = Median(group.Select(static value => value.BytesPerOperation)),
                MinimumOperationsPerSecond = group.Min(static value => value.OperationsPerSecond),
                MaximumOperationsPerSecond = group.Max(static value => value.OperationsPerSecond),
            })
            .OrderBy(static value => value.Scenario, StringComparer.Ordinal)
            .ThenBy(static value => value.Provider, StringComparer.Ordinal)
            .ToList();

        foreach (string scenario in PrimaryScenarios)
        {
            TraversalReadSummary dbreeze = FindSummary(report, scenario, DBreezeProvider);
            TraversalReadSummary sqlite = FindSummary(report, scenario, SqliteProvider);
            if (dbreeze != null && sqlite != null)
                dbreeze.RatioVsSqlite = dbreeze.MedianOperationsPerSecond / sqlite.MedianOperationsPerSecond;
        }
    }

    internal static bool EvaluateGates(TraversalReadAuditReport report, TraversalReadAuditOptions options)
    {
        report.GateViolations.Clear();
        if (!report.CorrectnessPassed)
        {
            report.GateViolations.Add("Correctness or measurement completeness failed.");
            return false;
        }

        foreach (string scenario in PrimaryScenarios)
        foreach (string provider in new[] { DBreezeProvider, SqliteProvider })
        {
            int count = report.Measurements.Count(value => value.Succeeded && value.Scenario == scenario &&
                value.Provider == provider);
            if (count != options.Rounds)
                report.GateViolations.Add($"{scenario} / {provider}: expected {options.Rounds} successful rounds, got {count}.");
        }
        foreach (string scenario in DiagnosticScenarios)
        {
            int count = report.Measurements.Count(value => value.Succeeded && value.Scenario == scenario &&
                value.Provider == DBreezeProvider);
            if (count != options.Rounds)
                report.GateViolations.Add($"{scenario}: expected {options.Rounds} successful rounds, got {count}.");
        }
        if (report.GateViolations.Count != 0)
            return false;

        if (options.Control || options.Smoke)
        {
            report.Warnings.Add(options.Control
                ? "Control mode is correctness-only and records no optimization verdict."
                : "Smoke mode is correctness-only and records no performance verdict.");
            return true;
        }

        bool passed = true;
        foreach (string scenario in PrimaryScenarios)
        {
            TraversalReadSummary dbreeze = FindSummary(report, scenario, DBreezeProvider);
            TraversalReadSummary sqlite = FindSummary(report, scenario, SqliteProvider);
            double ratio = dbreeze.MedianOperationsPerSecond / sqlite.MedianOperationsPerSecond;
            if (ratio < 1.03)
            {
                report.GateViolations.Add($"{scenario}: DBreeze is {ratio:P1} of SQLite; required >= 103.0%.");
                passed = false;
            }
            int pairedPasses = Enumerable.Range(1, options.Rounds).Count(round =>
            {
                TraversalReadMeasurement d = FindMeasurement(report, scenario, DBreezeProvider, round);
                TraversalReadMeasurement s = FindMeasurement(report, scenario, SqliteProvider, round);
                return d.OperationsPerSecond / s.OperationsPerSecond >= 1.03;
            });
            int requiredPairs = options.Rounds >= 5 ? 3 : (options.Rounds + 1) / 2;
            if (pairedPasses < requiredPairs)
            {
                report.GateViolations.Add($"{scenario}: {pairedPasses}/{options.Rounds} paired rounds pass 103%; required {requiredPairs}.");
                passed = false;
            }
            if (dbreeze.MedianBytesPerOperation > 1280.0)
            {
                report.GateViolations.Add($"{scenario}: DBreeze allocates {dbreeze.MedianBytesPerOperation:N1} B/op; required <= 1,280 B/op.");
                passed = false;
            }
        }

        if (!String.IsNullOrEmpty(options.ControlReportPath))
            passed &= EvaluateAgainstControl(report, options.ControlReportPath);
        return passed;
    }

    private static bool EvaluateAgainstControl(TraversalReadAuditReport report, string controlPath)
    {
        TraversalReadAuditReport control;
        try
        {
            control = JsonSerializer.Deserialize<TraversalReadAuditReport>(File.ReadAllText(controlPath),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
                });
        }
        catch (Exception exception)
        {
            report.GateViolations.Add("Cannot read control report: " + exception.Message);
            return false;
        }
        if (control == null || !control.CorrectnessPassed || control.Records != report.Records ||
            control.PayloadBytes != report.PayloadBytes || control.Rounds != report.Rounds)
        {
            report.GateViolations.Add("Control report is unsuccessful or configuration-incompatible.");
            return false;
        }

        bool passed = true;
        foreach (TraversalReadSummary current in report.Summaries.Where(static value => value.Provider == DBreezeProvider))
        {
            TraversalReadSummary previous = FindSummary(control, current.Scenario, DBreezeProvider);
            if (previous == null)
            {
                report.GateViolations.Add("Control is missing DBreeze scenario: " + current.Scenario);
                passed = false;
                continue;
            }
            if (current.MedianOperationsPerSecond < previous.MedianOperationsPerSecond * 0.95)
            {
                report.GateViolations.Add($"{current.Scenario}: throughput is less than 95% of control.");
                passed = false;
            }
            bool eager = PrimaryScenarios.Contains(current.Scenario, StringComparer.Ordinal);
            double allocationLimit = eager ? previous.MedianBytesPerOperation * 0.30 : previous.MedianBytesPerOperation * 1.05 + 1.0;
            if (current.MedianBytesPerOperation > allocationLimit)
            {
                report.GateViolations.Add(eager
                    ? $"{current.Scenario}: allocations were reduced by less than 70% versus control."
                    : $"{current.Scenario}: allocations regressed by more than 5% and 1 B/op versus control.");
                passed = false;
            }
        }
        return passed;
    }

    private static TraversalReadSummary FindSummary(TraversalReadAuditReport report, string scenario, string provider) =>
        report.Summaries.SingleOrDefault(value => value.Scenario == scenario && value.Provider == provider);

    private static TraversalReadMeasurement FindMeasurement(TraversalReadAuditReport report, string scenario,
        string provider, int round) => report.Measurements.Single(value => value.Scenario == scenario &&
            value.Provider == provider && value.Round == round && value.Succeeded);

    internal static double Median(IEnumerable<double> values)
    {
        double[] ordered = values.OrderBy(static value => value).ToArray();
        if (ordered.Length == 0)
            return Double.NaN;
        int middle = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) / 2.0 : ordered[middle];
    }

    private static readonly string[] PrimaryScenarios =
    {
        "Full forward traversal", "Full backward traversal", "Bounded ranges",
    };

    private static readonly string[] DiagnosticScenarios =
    {
        "Diagnostic forward lazy-consumed", "Diagnostic backward lazy-consumed",
        "Diagnostic forward key-only", "Diagnostic backward key-only",
    };

    private static void Persist(TraversalReadAuditReport report)
    {
        AuditPersistence.WriteJson(report.RawJson, report);
        AuditPersistence.WriteTextAtomic(report.RawCsv, BuildCsv(report));
        string html = BuildHtml(report);
        AuditPersistence.WriteTextAtomic(report.ImmutableHtml, html);
        AuditPersistence.WriteTextAtomic(report.CanonicalHtml, html);
    }

    private static string BuildCsv(TraversalReadAuditReport report)
    {
        var builder = new StringBuilder("scenario,provider,round,operations,returned,checksum,elapsed_ms,ops_per_second,allocated_bytes,bytes_per_operation,gc0,gc1,gc2,private_bytes,peak_working_set,succeeded,error\n");
        foreach (TraversalReadMeasurement value in report.Measurements)
        {
            builder.Append(Csv(value.Scenario)).Append(',').Append(Csv(value.Provider)).Append(',')
                .Append(value.Round).Append(',').Append(value.Operations).Append(',').Append(value.Returned).Append(',')
                .Append(value.Checksum).Append(',').Append(value.ElapsedMilliseconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.OperationsPerSecond.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.AllocatedBytes).Append(',').Append(value.BytesPerOperation.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.Gen0).Append(',').Append(value.Gen1).Append(',').Append(value.Gen2).Append(',')
                .Append(value.PrivateBytes).Append(',').Append(value.PeakWorkingSetBytes).Append(',')
                .Append(value.Succeeded).Append(',').Append(Csv(value.Error)).AppendLine();
        }
        return builder.ToString();
    }

    internal static string BuildHtml(TraversalReadAuditReport report)
    {
        static string H(string value) => System.Net.WebUtility.HtmlEncode(value ?? String.Empty);
        bool passed = report.CorrectnessPassed && report.PerformancePassed;
        var builder = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><title>DBreeze Traversal Read Audit</title><style>body{font:14px Segoe UI,Arial;margin:28px;color:#18202a}table{border-collapse:collapse;width:100%;margin:14px 0}th,td{border:1px solid #ccd3da;padding:6px;text-align:right}th:first-child,td:first-child,th:nth-child(2),td:nth-child(2){text-align:left}.pass{color:#087830}.fail{color:#b00020}.warn{color:#9a6200}code{background:#f3f5f7;padding:2px 4px}</style></head><body>");
        builder.Append("<h1>DBreeze Traversal Read Audit</h1><h2 class=\"")
            .Append(passed ? "pass\">PASS" : "fail\">FAIL").Append("</h2><p>Run <code>")
            .Append(H(report.RunId)).Append("</code>; phase <code>").Append(H(report.Phase))
            .Append("</code>; records ").Append(report.Records.ToString("N0")).Append(", payload ")
            .Append(report.PayloadBytes).Append(" bytes, rounds ").Append(report.Rounds).Append(".</p>")
            .Append("<p>Canonical workload semantics match DBreeze-vs-SQLite: eager materialization, SQLite WAL/FULL, transaction begin and enumeration inside the timer. Provider order alternates by round. OS cache is warm.</p>")
            .Append("<p>DBreeze <code>").Append(H(report.DBreezeVersion)).Append("</code>, SHA-256 <code>")
            .Append(H(report.DBreezeSha256)).Append("</code>; Git <code>").Append(H(report.GitHead)).Append("</code>")
            .Append(report.GitDirty ? "; dirty fingerprint <code>" + H(report.GitStatusSha256) + "</code>" : "; clean")
            .Append(". SQLite managed/native: ").Append(H(report.ManagedSqliteVersion)).Append(" / ")
            .Append(H(report.NativeSqliteVersion)).Append(".</p><h2>Median results</h2><table><thead><tr><th>Scenario</th><th>Provider</th><th>ms</th><th>ops/s</th><th>min/max ops/s</th><th>B/op</th><th>DBreeze/SQLite</th></tr></thead><tbody>");
        foreach (TraversalReadSummary value in report.Summaries)
        {
            builder.Append("<tr><td>").Append(H(value.Scenario)).Append("</td><td>").Append(H(value.Provider))
                .Append("</td><td>").Append(value.MedianMilliseconds.ToString("N2")).Append("</td><td>")
                .Append(value.MedianOperationsPerSecond.ToString("N0")).Append("</td><td>")
                .Append(value.MinimumOperationsPerSecond.ToString("N0")).Append(" / ")
                .Append(value.MaximumOperationsPerSecond.ToString("N0")).Append("</td><td>")
                .Append(value.MedianBytesPerOperation.ToString("N1")).Append("</td><td>")
                .Append(Double.IsNaN(value.RatioVsSqlite) ? "—" : value.RatioVsSqlite.ToString("P1"))
                .Append("</td></tr>");
        }
        builder.Append("</tbody></table><h2>Gates and findings</h2><ul>");
        if (report.GateViolations.Count == 0)
            builder.Append("<li class=\"pass\">All applicable gates passed.</li>");
        foreach (string value in report.GateViolations)
            builder.Append("<li class=\"fail\">").Append(H(value)).Append("</li>");
        foreach (string value in report.Failures)
            builder.Append("<li class=\"fail\">").Append(H(value)).Append("</li>");
        foreach (string value in report.Warnings)
            builder.Append("<li class=\"warn\">").Append(H(value)).Append("</li>");
        builder.Append("</ul><h2>Per round</h2><table><thead><tr><th>Scenario</th><th>Provider</th><th>Round</th><th>ms</th><th>ops/s</th><th>B/op</th><th>GC 0/1/2</th><th>Status</th></tr></thead><tbody>");
        foreach (TraversalReadMeasurement value in report.Measurements)
        {
            builder.Append("<tr><td>").Append(H(value.Scenario)).Append("</td><td>").Append(H(value.Provider))
                .Append("</td><td>").Append(value.Round).Append("</td><td>")
                .Append(value.ElapsedMilliseconds.ToString("N2")).Append("</td><td>")
                .Append(value.OperationsPerSecond.ToString("N0")).Append("</td><td>")
                .Append(value.BytesPerOperation.ToString("N1")).Append("</td><td>")
                .Append(value.Gen0).Append('/').Append(value.Gen1).Append('/').Append(value.Gen2)
                .Append("</td><td class=\"").Append(value.Succeeded ? "pass\">PASS" : "fail\">FAIL")
                .Append("</td></tr>");
        }
        builder.Append("</tbody></table><h2>Read-only fixture</h2><p>DBreeze size: ")
            .Append(report.DBreezeDatabaseBytes.ToString("N0")).Append(" bytes; SQLite size: ")
            .Append(report.SqliteDatabaseBytes.ToString("N0")).Append(" bytes. DBreeze manifest unchanged: ")
            .Append(ManifestEquals(report.DBreezeManifestBefore, report.DBreezeManifestAfter) ? "yes" : "no")
            .Append(". Scratch cleanup: ").Append(report.ScratchCleaned ? "completed" : "retained")
            .Append(".</p><p>Runtime: ").Append(H(report.Runtime)).Append("; OS: ").Append(H(report.OS))
            .Append("; GC: ").Append(report.ServerGc ? "Server" : "Workstation").Append(" / ")
            .Append(H(report.GcLatencyMode)).Append(". Raw artifacts: <code>")
            .Append(H(Path.GetDirectoryName(report.RawJson))).Append("</code>.</p><p>Reproduction: <code>")
            .Append(H(report.ReproductionCommand)).Append("</code></p></body></html>");
        return builder.ToString();
    }

    private static string Csv(string value) => '"' + (value ?? String.Empty).Replace("\"", "\"\"") + '"';
    private static long DirectoryBytes(string path) => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(static value => new FileInfo(value).Length);
    private static void StabilizeGc() { GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true); GC.WaitForPendingFinalizers(); GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true); }

    private enum ReadMode { Eager, LazyConsumed, KeyOnly }
    private readonly record struct TraversalReadOutcome(long Operations, long Returned, long Checksum, double ElapsedMilliseconds);
}

internal sealed class TraversalReadAuditOptions
{
    internal string RootPath { get; private set; } = @"D:\Temp\DbreezeDbTest";
    internal string ReportPath { get; private set; }
    internal string RunId { get; private set; }
    internal string Phase { get; private set; } = "post-change";
    internal string SqliteSynchronous { get; private set; } = "FULL";
    internal string ControlReportPath { get; private set; }
    internal int Records { get; private set; } = 1_000_000;
    internal int PayloadBytes { get; private set; } = 256;
    internal int Rounds { get; private set; } = 5;
    internal bool Smoke { get; private set; }
    internal bool Control { get; private set; }
    internal bool KeepDatabases { get; private set; }

    internal static TraversalReadAuditOptions Parse(string[] args)
    {
        var result = new TraversalReadAuditOptions();
        bool reportSupplied = false;
        for (int index = 0; index < args.Length; index++)
        {
            string option = args[index].ToLowerInvariant();
            switch (option)
            {
                case "--traversal-read-audit": break;
                case "--control": result.Control = true; result.Phase = "control"; break;
                case "--smoke": result.Smoke = true; break;
                case "--keep-databases": result.KeepDatabases = true; break;
                case "--root": result.RootPath = Read(args, ref index, option); break;
                case "--report": result.ReportPath = Read(args, ref index, option); reportSupplied = true; break;
                case "--run-id": result.RunId = Read(args, ref index, option); break;
                case "--phase": result.Phase = Read(args, ref index, option); break;
                case "--control-report": result.ControlReportPath = Read(args, ref index, option); break;
                case "--records": result.Records = ReadInt(args, ref index, option, 1_000, 1_000_000); break;
                case "--payload-bytes": result.PayloadBytes = ReadInt(args, ref index, option, 1, 65_536); break;
                case "--rounds": result.Rounds = ReadInt(args, ref index, option, 1, 5); break;
                case "--sqlite-synchronous":
                    result.SqliteSynchronous = Read(args, ref index, option).ToUpperInvariant();
                    if (result.SqliteSynchronous is not ("FULL" or "NORMAL"))
                        throw new ArgumentException("--sqlite-synchronous must be FULL or NORMAL.");
                    break;
                default: throw new ArgumentException("Unknown traversal-read audit option: " + args[index]);
            }
        }
        result.RootPath = Path.GetFullPath(result.RootPath);
        if (result.Smoke)
        {
            result.Records = Math.Min(result.Records, 10_000);
            result.Rounds = 1;
        }
        result.RunId ??= DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-traversal-read-" + Slug(result.Phase);
        AuditRunLayout.ValidateLeafName(result.RunId, "--run-id");
        if (!reportSupplied)
            result.ReportPath = Path.Combine(result.RootPath, "DBreeze_Traversal_Read_Audit.html");
        result.ReportPath = Path.GetFullPath(result.ReportPath);
        AuditRunLayout.EnsureUnderRoot(result.ReportPath, result.RootPath);
        if (!String.IsNullOrEmpty(result.ControlReportPath))
            result.ControlReportPath = Path.GetFullPath(result.ControlReportPath);
        return result;
    }

    internal TraversalReadAuditOptions WithRecords(int records) => new()
    {
        RootPath = RootPath, ReportPath = ReportPath, RunId = RunId, Phase = Phase,
        SqliteSynchronous = SqliteSynchronous, Records = records, PayloadBytes = PayloadBytes,
        Rounds = Rounds, Smoke = Smoke, Control = Control, KeepDatabases = KeepDatabases,
        ControlReportPath = ControlReportPath,
    };

    private static string Read(string[] args, ref int index, string option) =>
        ++index < args.Length && !String.IsNullOrWhiteSpace(args[index])
            ? args[index] : throw new ArgumentException(option + " requires a value.");
    private static int ReadInt(string[] args, ref int index, string option, int minimum, int maximum) =>
        Int32.TryParse(Read(args, ref index, option), NumberStyles.None, CultureInfo.InvariantCulture, out int value) &&
        value >= minimum && value <= maximum ? value : throw new ArgumentOutOfRangeException(option, $"Expected {minimum}..{maximum}.");
    private static string Slug(string value) => new(value.Select(static character => Char.IsLetterOrDigit(character) ? Char.ToLowerInvariant(character) : '-').ToArray());
}

internal sealed class TraversalReadAuditReport
{
    public string RunId { get; set; }
    public string Phase { get; set; }
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
    public string ManagedSqliteVersion { get; set; }
    public string NativeSqliteVersion { get; set; }
    public string GitHead { get; set; }
    public bool GitDirty { get; set; }
    public string GitStatusSha256 { get; set; }
    public int Records { get; set; }
    public int PayloadBytes { get; set; }
    public int Rounds { get; set; }
    public string SqliteSynchronous { get; set; }
    public string RawJson { get; set; }
    public string RawCsv { get; set; }
    public string ImmutableHtml { get; set; }
    public string CanonicalHtml { get; set; }
    public string ReproductionCommand { get; set; }
    public long DBreezeDatabaseBytes { get; set; }
    public long SqliteDatabaseBytes { get; set; }
    public bool CorrectnessPassed { get; set; }
    public bool PerformancePassed { get; set; }
    public bool ScratchCleaned { get; set; }
    public Dictionary<string, TraversalReadFileManifest> DBreezeManifestBefore { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, TraversalReadFileManifest> DBreezeManifestAfter { get; set; } = new(StringComparer.Ordinal);
    public List<TraversalReadMeasurement> Measurements { get; set; } = new();
    public List<TraversalReadSummary> Summaries { get; set; } = new();
    public List<string> Failures { get; set; } = new();
    public List<string> GateViolations { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    internal static TraversalReadAuditReport Create(TraversalReadAuditOptions options, AuditRunLayout layout)
    {
        Assembly dbreeze = typeof(DBreezeEngine).Assembly;
        string status = Git("status", "--porcelain=v1", "--untracked-files=all");
        string nativeSqlite;
        using (var connection = new SqliteConnection("Data Source=:memory:"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT sqlite_version();";
            nativeSqlite = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? String.Empty;
        }
        string commandLine = $"dotnet run --project DBreeze.Net8.Benchmarks -c Release -p:SignAssembly=false -- --traversal-read-audit --root \"{options.RootPath}\" --records {options.Records} --payload-bytes {options.PayloadBytes} --rounds {options.Rounds} --sqlite-synchronous {options.SqliteSynchronous}";
        if (options.Control)
            commandLine += " --control";
        return new TraversalReadAuditReport
        {
            RunId = options.RunId,
            Phase = options.Phase,
            StartedUtc = DateTime.UtcNow,
            Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            ProcessorIdentifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? String.Empty,
            ServerGc = GCSettings.IsServerGC,
            GcLatencyMode = GCSettings.LatencyMode.ToString(),
            DBreezeVersion = dbreeze.GetName().Version?.ToString(),
            DBreezeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dbreeze.Location))).ToLowerInvariant(),
            ManagedSqliteVersion = typeof(SqliteConnection).Assembly.GetName().Version?.ToString(),
            NativeSqliteVersion = nativeSqlite,
            GitHead = Git("rev-parse", "HEAD").Trim(),
            GitDirty = !String.IsNullOrWhiteSpace(status),
            GitStatusSha256 = String.IsNullOrEmpty(status) ? String.Empty : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(status))).ToLowerInvariant(),
            Records = options.Records,
            PayloadBytes = options.PayloadBytes,
            Rounds = options.Rounds,
            SqliteSynchronous = options.SqliteSynchronous,
            RawJson = Path.Combine(layout.ReportsDirectory, "DBreeze_Traversal_Read_Audit.json"),
            RawCsv = Path.Combine(layout.ReportsDirectory, "DBreeze_Traversal_Read_Audit.csv"),
            ImmutableHtml = Path.Combine(layout.ReportsDirectory, "DBreeze_Traversal_Read_Audit.html"),
            CanonicalHtml = options.ReportPath,
            ReproductionCommand = commandLine,
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

internal sealed class TraversalReadMeasurement
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
    public long PrivateBytes { get; set; }
    public long PeakWorkingSetBytes { get; set; }
    public bool Succeeded { get; set; }
    public string Error { get; set; }
}

internal sealed class TraversalReadSummary
{
    public string Scenario { get; set; }
    public string Provider { get; set; }
    public int Rounds { get; set; }
    public double MedianMilliseconds { get; set; }
    public double MedianOperationsPerSecond { get; set; }
    public double MedianBytesPerOperation { get; set; }
    public double MinimumOperationsPerSecond { get; set; }
    public double MaximumOperationsPerSecond { get; set; }
    public double RatioVsSqlite { get; set; } = Double.NaN;
}

internal sealed class TraversalReadFileManifest
{
    public long Length { get; set; }
    public string Sha256 { get; set; }
}

internal static class TraversalReadAuditSelfTests
{
    internal static int Run()
    {
        var failures = new List<string>();
        try
        {
            if (TraversalReadAudit.Median(new[] { 4.0, 1.0, 3.0, 2.0 }) != 2.5)
                failures.Add("Median calculation failed.");
            string root = Path.Combine(Path.GetTempPath(), "dbreeze-traversal-options");
            TraversalReadAuditOptions options = TraversalReadAuditOptions.Parse(new[]
            {
                "--traversal-read-audit", "--smoke", "--root", root,
            });
            if (options.Records != 10_000 || options.Rounds != 1)
                failures.Add("Smoke defaults failed.");
            var report = new TraversalReadAuditReport
            {
                RunId = "<escaped>", Phase = "test", Runtime = "runtime", OS = "os",
                GcLatencyMode = "Interactive", RawJson = Path.Combine(root, "raw.json"),
                CorrectnessPassed = true, PerformancePassed = true,
            };
            string html = TraversalReadAudit.BuildHtml(report);
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
            Console.WriteLine("PASS traversal-read audit self-tests");
        return failures.Count == 0 ? 0 : 1;
    }
}
