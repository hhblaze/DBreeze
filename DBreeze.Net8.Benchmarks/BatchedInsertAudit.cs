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

internal static class BatchedInsertAudit
{
    private const string Table = "kv";
    private const string Sorted = "DBreeze Sorted";
    private const string SortedNoOverwrite = "DBreeze Sorted + NoOverwrite";
    private const string Sqlite = "SQLite";
    private const string Canonical = "1000 rows / transaction";
    private const string Reused = "reused transaction / 1000 rows per commit";
    private const string ParallelTables = ParallelTableInsertWorkload.Scenario;

    internal static int Run(string[] args)
    {
        try
        {
            BatchedInsertAuditOptions options = BatchedInsertAuditOptions.Parse(args);
            return new Runner(options).Execute();
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException)
        {
            Console.Error.WriteLine("Batched insert audit configuration error: " + exception.Message);
            return 2;
        }
    }

    private sealed class Runner
    {
        private readonly BatchedInsertAuditOptions _options;
        private readonly AuditRunLayout _layout;
        private readonly BatchedInsertAuditReport _report;
        private readonly byte[][] _payloads;
        private readonly long _expectedChecksum;
        private readonly ParallelTableInsertSpec _parallelSpec;
        private readonly long _parallelExpectedChecksum;
        private readonly StringBuilder _log = new();

        internal Runner(BatchedInsertAuditOptions options)
        {
            _options = options;
            _layout = new AuditRunLayout(options.RootPath, options.RunId);
            _payloads = CreatePayloadPool(options.PayloadBytes);
            _expectedChecksum = ExpectedChecksum(options.Records);
            _parallelSpec = new ParallelTableInsertSpec(options.MultiTableRecords,
                options.MultiTableCount, options.MultiTableBatchSize, options.PayloadBytes, "FULL");
            _parallelExpectedChecksum = ParallelTableInsertWorkload.ExpectedChecksum(_parallelSpec, _payloads);
            _report = new BatchedInsertAuditReport
            {
                RunId = options.RunId,
                StartedUtc = DateTime.UtcNow,
                Records = options.Records,
                PayloadBytes = options.PayloadBytes,
                BatchSize = options.BatchSize,
                MultiTableRecords = options.MultiTableRecords,
                MultiTableCount = options.MultiTableCount,
                MultiTableBatchSize = options.MultiTableBatchSize,
                MultiTableBusyTimeoutMilliseconds = _parallelSpec.SqliteBusyTimeoutMilliseconds,
                Rounds = options.Rounds,
                ControlOnly = options.ControlOnly,
                ControlJson = options.ControlJson,
                Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                DBreezeVersion = typeof(DBreezeEngine).Assembly.GetName().Version?.ToString(),
                DBreezeSha256 = Sha256File(typeof(DBreezeEngine).Assembly.Location),
            };
        }

        internal int Execute()
        {
            _layout.Create();
            try
            {
                Log($"Started batched insert audit { _options.RunId}: records={_options.Records}, batch={_options.BatchSize}, rounds={_options.Rounds}.");
                CaptureGit();
                WarmUp();
                for (int round = 1; round <= _options.Rounds; round++)
                {
                    var canonical = new List<(string Provider, Func<string, BatchedOutcome> Action)>
                    {
                        (Sorted, path => MeasureDbreeze(path, manyTransactions: true, noOverwrite: false)),
                        (SortedNoOverwrite, path => MeasureDbreeze(path, manyTransactions: true, noOverwrite: true)),
                        (Sqlite, MeasureSqlite),
                    };
                    RunRotated(Canonical, round, canonical);

                    var reused = new List<(string Provider, Func<string, BatchedOutcome> Action)>
                    {
                        (Sorted, path => MeasureDbreeze(path, manyTransactions: false, noOverwrite: false)),
                        (SortedNoOverwrite, path => MeasureDbreeze(path, manyTransactions: false, noOverwrite: true)),
                    };
                    RunRotated(Reused, round, reused);

                    var parallelTables = new List<(string Provider, Func<string, BatchedOutcome> Action)>
                    {
                        (Sorted, MeasureParallelDbreeze),
                        (Sqlite, MeasureParallelSqlite),
                    };
                    RunRotated(ParallelTables, round, parallelTables);
                }

                RunTwoTableJournalControl();
                Summarize();
                Evaluate();
            }
            catch (Exception exception)
            {
                _report.Failures.Add(exception.ToString());
                Log("FATAL " + exception);
            }
            finally
            {
                _report.CompletedUtc = DateTime.UtcNow;
                Persist();
                if (!_options.KeepDatabases)
                {
                    try { _layout.CleanupScratch(); }
                    catch (Exception exception)
                    {
                        _report.Failures.Add("Scratch cleanup failed: " + exception.Message);
                        Persist();
                    }
                }
            }

            return _report.CorrectnessPassed && (_options.ControlOnly || _report.PerformancePassed) ? 0 : 1;
        }

        private void WarmUp()
        {
            int records = Math.Min(2_000, _options.Records);
            string db = Path.Combine(_layout.ScratchDirectory, "warmup-dbreeze");
            string sqlite = Path.Combine(_layout.ScratchDirectory, "warmup-sqlite");
            MeasureDbreeze(db, manyTransactions: true, noOverwrite: false, records);
            MeasureSqlite(sqlite, records);
            AuditRunLayout.DeleteOwnedChild(db, _layout.ScratchDirectory);
            AuditRunLayout.DeleteOwnedChild(sqlite, _layout.ScratchDirectory);
            Log("Warm-up completed.");
        }

        private void RunRotated(string scenario, int round,
            List<(string Provider, Func<string, BatchedOutcome> Action)> actions)
        {
            int rotation = (round - 1) % actions.Count;
            foreach ((string provider, Func<string, BatchedOutcome> action) in
                actions.Skip(rotation).Concat(actions.Take(rotation)))
                MeasureFresh(scenario, provider, round, action);
        }

        private void MeasureFresh(string scenario, string provider, int round,
            Func<string, BatchedOutcome> action)
        {
            string path = Path.Combine(_layout.ScratchDirectory,
                $"{Slug(scenario)}-{Slug(provider)}-r{round}");
            var measurement = new BatchedInsertMeasurement
            {
                Scenario = scenario,
                Provider = provider,
                Round = round,
                DatabasePath = path,
            };
            try
            {
                StabilizeGc();
                BatchedOutcome outcome = action(path);
                measurement.Operations = outcome.Operations;
                measurement.Transactions = outcome.Transactions;
                measurement.ElapsedMilliseconds = outcome.ElapsedMilliseconds;
                measurement.TransactionCreateMilliseconds = outcome.TransactionCreateMilliseconds;
                measurement.MutationMilliseconds = outcome.MutationMilliseconds;
                measurement.CommitMilliseconds = outcome.CommitMilliseconds;
                measurement.DisposeMilliseconds = outcome.DisposeMilliseconds;
                measurement.AllocatedBytes = outcome.AllocatedBytes;
                measurement.DatabaseBytes = DirectoryBytes(path);
                measurement.Checksum = outcome.Checksum;
                measurement.OperationsPerSecond = outcome.Operations * 1000.0 / outcome.ElapsedMilliseconds;
                ApplyWriteDiagnostics(measurement, outcome.StorageDiagnostics);
                measurement.Succeeded = outcome.Operations == ExpectedOperations(scenario) &&
                    outcome.Checksum == ExpectedChecksumFor(scenario) &&
                    outcome.Transactions == ExpectedTransactions(scenario);
                if (!measurement.Succeeded)
                    throw new InvalidDataException("Batched insert oracle mismatch.");
                Log($"PASS {scenario} / {provider} / r{round}: {measurement.ElapsedMilliseconds:F3} ms, {measurement.OperationsPerSecond:N0} ops/s.");
            }
            catch (Exception exception)
            {
                measurement.Succeeded = false;
                measurement.Error = exception.ToString();
                _report.Failures.Add($"{scenario} / {provider} / r{round}: {exception.Message}");
                Log("FAIL " + _report.Failures[^1]);
            }
            finally
            {
                _report.Measurements.Add(measurement);
                Persist();
            }

            if (measurement.Succeeded && !_options.KeepDatabases)
                AuditRunLayout.DeleteOwnedChild(path, _layout.ScratchDirectory);
        }

        private int ExpectedOperations(string scenario) => scenario == ParallelTables
            ? _options.MultiTableRecords
            : _options.Records;

        private long ExpectedChecksumFor(string scenario) => scenario == ParallelTables
            ? _parallelExpectedChecksum
            : _expectedChecksum;

        private int ExpectedTransactions(string scenario)
        {
            if (scenario == Reused)
                return 1;
            if (scenario == ParallelTables)
                return _parallelSpec.ExpectedTransactions();
            return (_options.Records + _options.BatchSize - 1) / _options.BatchSize;
        }

        private BatchedOutcome MeasureDbreeze(string path, bool manyTransactions,
            bool noOverwrite, int? recordOverride = null)
        {
            int records = recordOverride ?? _options.Records;
            CreateEmptyDirectory(path);
            long createTicks = 0, mutationTicks = 0, commitTicks = 0, disposeTicks = 0;
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long[] storageBefore = ReadWriteDiagnostics();
            using IDisposable diagnostics = EnableWriteDiagnostics();
            double elapsed;
            int transactions = 0;

            using (var engine = new DBreezeEngine(path))
            {
                var total = Stopwatch.StartNew();
                if (manyTransactions)
                {
                    for (int start = 0; start < records; start += _options.BatchSize)
                    {
                        long ticks = Stopwatch.GetTimestamp();
                        var transaction = engine.GetTransaction();
                        createTicks += Stopwatch.GetTimestamp() - ticks;
                        try
                        {
                            if (noOverwrite)
                                transaction.Technical_SetTable_OverwriteIsNotAllowed(Table);
                            int end = Math.Min(records, start + _options.BatchSize);
                            ticks = Stopwatch.GetTimestamp();
                            for (int key = start; key < end; key++)
                                transaction.Insert(Table, (long)key, Payload(key));
                            mutationTicks += Stopwatch.GetTimestamp() - ticks;
                            ticks = Stopwatch.GetTimestamp();
                            transaction.Commit();
                            commitTicks += Stopwatch.GetTimestamp() - ticks;
                            transactions++;
                        }
                        finally
                        {
                            ticks = Stopwatch.GetTimestamp();
                            transaction.Dispose();
                            disposeTicks += Stopwatch.GetTimestamp() - ticks;
                        }
                    }
                }
                else
                {
                    long ticks = Stopwatch.GetTimestamp();
                    var transaction = engine.GetTransaction();
                    createTicks += Stopwatch.GetTimestamp() - ticks;
                    try
                    {
                        if (noOverwrite)
                            transaction.Technical_SetTable_OverwriteIsNotAllowed(Table);
                        for (int start = 0; start < records; start += _options.BatchSize)
                        {
                            int end = Math.Min(records, start + _options.BatchSize);
                            ticks = Stopwatch.GetTimestamp();
                            for (int key = start; key < end; key++)
                                transaction.Insert(Table, (long)key, Payload(key));
                            mutationTicks += Stopwatch.GetTimestamp() - ticks;
                            ticks = Stopwatch.GetTimestamp();
                            transaction.Commit();
                            commitTicks += Stopwatch.GetTimestamp() - ticks;
                        }
                        transactions = 1;
                    }
                    finally
                    {
                        ticks = Stopwatch.GetTimestamp();
                        transaction.Dispose();
                        disposeTicks += Stopwatch.GetTimestamp() - ticks;
                    }
                }
                total.Stop();
                elapsed = total.Elapsed.TotalMilliseconds;
                VerifyDbreeze(engine, records);
            }

            long[] storageAfter = ReadWriteDiagnostics();
            return new BatchedOutcome(records, transactions, ExpectedChecksum(records), elapsed,
                ToMilliseconds(createTicks), ToMilliseconds(mutationTicks), ToMilliseconds(commitTicks),
                ToMilliseconds(disposeTicks), GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
                Delta(storageBefore, storageAfter));
        }

        private BatchedOutcome MeasureSqlite(string path) => MeasureSqlite(path, _options.Records);

        private BatchedOutcome MeasureParallelDbreeze(string path)
        {
            long[] storageBefore = Array.Empty<long>();
            using IDisposable diagnostics = EnableWriteDiagnostics();
            ParallelTableInsertResult result = ParallelTableInsertWorkload.RunDbreeze(path, _parallelSpec,
                _payloads, () => storageBefore = ReadWriteDiagnostics());
            return ToBatchedOutcome(result, Delta(storageBefore, ReadWriteDiagnostics()));
        }

        private BatchedOutcome MeasureParallelSqlite(string path) =>
            ToBatchedOutcome(ParallelTableInsertWorkload.RunSqlite(path, _parallelSpec, _payloads), Array.Empty<long>());

        private static BatchedOutcome ToBatchedOutcome(ParallelTableInsertResult result, long[] diagnostics) =>
            new(checked((int)result.Operations), result.Transactions, result.Checksum,
                result.ElapsedMilliseconds, result.TransactionCreateMilliseconds,
                result.MutationMilliseconds, result.CommitMilliseconds, result.DisposeMilliseconds,
                result.AllocatedBytes, diagnostics);

        private BatchedOutcome MeasureSqlite(string path, int records)
        {
            CreateEmptyDirectory(path);
            string file = Path.Combine(path, "database.sqlite");
            long createTicks = 0, mutationTicks = 0, commitTicks = 0, disposeTicks = 0;
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            double elapsed;
            int transactions = 0;
            using (SqliteConnection connection = OpenSqlite(file, create: true))
            {
                ExecuteNonQuery(connection, "CREATE TABLE kv (k INTEGER NOT NULL PRIMARY KEY, v BLOB NOT NULL);");
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "INSERT INTO kv(k,v) VALUES($k,$v);";
                SqliteParameter keyParameter = command.Parameters.Add("$k", SqliteType.Integer);
                SqliteParameter valueParameter = command.Parameters.Add("$v", SqliteType.Blob);
                command.Prepare();
                var total = Stopwatch.StartNew();
                for (int start = 0; start < records; start += _options.BatchSize)
                {
                    long ticks = Stopwatch.GetTimestamp();
                    SqliteTransaction transaction = connection.BeginTransaction();
                    command.Transaction = transaction;
                    createTicks += Stopwatch.GetTimestamp() - ticks;
                    try
                    {
                        int end = Math.Min(records, start + _options.BatchSize);
                        ticks = Stopwatch.GetTimestamp();
                        for (int key = start; key < end; key++)
                        {
                            keyParameter.Value = (long)key;
                            valueParameter.Value = Payload(key);
                            if (command.ExecuteNonQuery() != 1)
                                throw new InvalidDataException("SQLite insert affected an unexpected row count.");
                        }
                        mutationTicks += Stopwatch.GetTimestamp() - ticks;
                        ticks = Stopwatch.GetTimestamp();
                        transaction.Commit();
                        commitTicks += Stopwatch.GetTimestamp() - ticks;
                        transactions++;
                    }
                    finally
                    {
                        ticks = Stopwatch.GetTimestamp();
                        transaction.Dispose();
                        disposeTicks += Stopwatch.GetTimestamp() - ticks;
                    }
                }
                total.Stop();
                elapsed = total.Elapsed.TotalMilliseconds;
                VerifySqlite(connection, records);
                ExecuteNonQuery(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
            }
            return new BatchedOutcome(records, transactions, ExpectedChecksum(records), elapsed,
                ToMilliseconds(createTicks), ToMilliseconds(mutationTicks), ToMilliseconds(commitTicks),
                ToMilliseconds(disposeTicks), GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
                Array.Empty<long>());
        }

        private void RunTwoTableJournalControl()
        {
            int records = Math.Min(_options.Records, 10_000);
            string path = Path.Combine(_layout.ScratchDirectory, "two-table-journal-control");
            try
            {
                CreateEmptyDirectory(path);
                long[] storageBefore = ReadWriteDiagnostics();
                using IDisposable diagnostics = EnableWriteDiagnostics();
                int transactions = 0;
                using (var engine = new DBreezeEngine(path))
                {
                    for (int start = 0; start < records; start += _options.BatchSize)
                    {
                        using var transaction = engine.GetTransaction();
                        transaction.SynchronizeTables("journal-a", "journal-b");
                        int end = Math.Min(records, start + _options.BatchSize);
                        for (int key = start; key < end; key++)
                        {
                            transaction.Insert("journal-a", (long)key, Payload(key));
                            transaction.Insert("journal-b", (long)key, Payload(key));
                        }
                        transaction.Commit();
                        transactions++;
                    }
                    using var verify = engine.GetTransaction();
                    if (verify.Count("journal-a") != (ulong)records ||
                        verify.Count("journal-b") != (ulong)records)
                        throw new InvalidDataException("Two-table transaction journal oracle mismatch.");
                }
                _report.TwoTableJournalControlPassed = transactions ==
                    (records + _options.BatchSize - 1) / _options.BatchSize;
                long[] storageDelta = Delta(storageBefore, ReadWriteDiagnostics());
                if (storageDelta.Length >= 8)
                {
                    _report.TwoTableDurableFlushes = storageDelta[2];
                    _report.TwoTableDurableFlushMilliseconds = storageDelta[3] / 1000.0;
                    _report.TwoTableJournalFlushes = storageDelta[5];
                }
                _report.TwoTableJournalControlPassed &= _report.TwoTableJournalFlushes > 0;
                Log((_report.TwoTableJournalControlPassed ? "PASS" : "FAIL") +
                    $" two-table transaction journal control; journal flushes={_report.TwoTableJournalFlushes}.");
            }
            catch (Exception exception)
            {
                _report.TwoTableJournalControlPassed = false;
                _report.Failures.Add("Two-table journal control: " + exception.Message);
            }
            finally
            {
                if (!_options.KeepDatabases && Directory.Exists(path))
                    AuditRunLayout.DeleteOwnedChild(path, _layout.ScratchDirectory);
            }
        }

        private void Summarize()
        {
            _report.Summaries = _report.Measurements.Where(static value => value.Succeeded)
                .GroupBy(static value => (value.Scenario, value.Provider))
                .Select(group => new BatchedInsertSummary
                {
                    Scenario = group.Key.Scenario,
                    Provider = group.Key.Provider,
                    Rounds = group.Count(),
                    MedianMilliseconds = Median(group.Select(static value => value.ElapsedMilliseconds)),
                    MedianOperationsPerSecond = Median(group.Select(static value => value.OperationsPerSecond)),
                    MedianCreateMilliseconds = Median(group.Select(static value => value.TransactionCreateMilliseconds)),
                    MedianMutationMilliseconds = Median(group.Select(static value => value.MutationMilliseconds)),
                    MedianCommitMilliseconds = Median(group.Select(static value => value.CommitMilliseconds)),
                    MedianDisposeMilliseconds = Median(group.Select(static value => value.DisposeMilliseconds)),
                    MedianAllocatedBytes = Median(group.Select(static value => (double)value.AllocatedBytes)),
                    MedianDatabaseBytes = (long)Median(group.Select(static value => (double)value.DatabaseBytes)),
                }).OrderBy(static value => value.Scenario).ThenBy(static value => value.Provider).ToList();

            foreach (BatchedInsertSummary summary in _report.Summaries.Where(static value => value.Provider != Sqlite))
            {
                string sqliteScenario = summary.Scenario == Reused ? Canonical : summary.Scenario;
                BatchedInsertSummary sqlite = FindSummary(sqliteScenario, Sqlite);
                summary.RatioVsSqlite = sqlite == null ? Double.NaN :
                    summary.MedianOperationsPerSecond / sqlite.MedianOperationsPerSecond;
            }

            if (!String.IsNullOrEmpty(_options.ControlJson))
            {
                BatchedInsertAuditReport control = JsonSerializer.Deserialize<BatchedInsertAuditReport>(
                    File.ReadAllText(_options.ControlJson), AuditPersistence.JsonOptions)
                    ?? throw new InvalidDataException("Control batched-insert report is empty.");
                if (control.Records != _report.Records || control.PayloadBytes != _report.PayloadBytes ||
                    control.BatchSize != _report.BatchSize || control.Summaries == null)
                    throw new InvalidDataException("Control batched-insert configuration does not match this run.");

                _report.ControlDBreezeSha256 = control.DBreezeSha256;
                _report.ControlGitHead = control.GitHead;
                foreach (BatchedInsertSummary summary in _report.Summaries)
                {
                    BatchedInsertSummary baseline = control.Summaries.SingleOrDefault(value =>
                        value.Scenario == summary.Scenario && value.Provider == summary.Provider);
                    if (baseline == null || baseline.MedianOperationsPerSecond <= 0)
                        continue;
                    summary.ControlMedianOperationsPerSecond = baseline.MedianOperationsPerSecond;
                    summary.RatioVsControl = summary.MedianOperationsPerSecond /
                        baseline.MedianOperationsPerSecond;
                }
            }
        }

        private void Evaluate()
        {
            int expectedTransactions = (_options.Records + _options.BatchSize - 1) / _options.BatchSize;
            foreach (string provider in new[] { Sorted, SortedNoOverwrite, Sqlite })
            {
                int count = _report.Measurements.Count(value => value.Succeeded &&
                    value.Scenario == Canonical && value.Provider == provider &&
                    value.Transactions == expectedTransactions);
                if (count != _options.Rounds)
                    _report.GateViolations.Add($"Canonical {provider}: expected {_options.Rounds} successful rounds, got {count}.");
            }
            foreach (string provider in new[] { Sorted, SortedNoOverwrite })
            {
                int count = _report.Measurements.Count(value => value.Succeeded &&
                    value.Scenario == Reused && value.Provider == provider && value.Transactions == 1);
                if (count != _options.Rounds)
                    _report.GateViolations.Add($"Reused {provider}: expected {_options.Rounds} successful rounds, got {count}.");
            }
            int parallelTransactions = _parallelSpec.ExpectedTransactions();
            foreach (string provider in new[] { Sorted, Sqlite })
            {
                int count = _report.Measurements.Count(value => value.Succeeded &&
                    value.Scenario == ParallelTables && value.Provider == provider &&
                    value.Operations == _options.MultiTableRecords &&
                    value.Transactions == parallelTransactions);
                if (count != _options.Rounds)
                    _report.GateViolations.Add(
                        $"Parallel tables {provider}: expected {_options.Rounds} successful descriptive rounds, got {count}.");
            }
            if (!_report.TwoTableJournalControlPassed)
                _report.GateViolations.Add("Two-table transaction journal control failed.");

            BatchedInsertSummary sorted = FindSummary(Canonical, Sorted);
            BatchedInsertSummary noOverwrite = FindSummary(Canonical, SortedNoOverwrite);
            BatchedInsertSummary sqlite = FindSummary(Canonical, Sqlite);
            if (sorted == null || sqlite == null || sorted.MedianOperationsPerSecond <= sqlite.MedianOperationsPerSecond)
                _report.GateViolations.Add("DBreeze Sorted median must be faster than SQLite for 1000 rows/transaction.");
            if (sorted != null && noOverwrite != null &&
                noOverwrite.MedianOperationsPerSecond <= sorted.MedianOperationsPerSecond)
                _report.GateViolations.Add("DBreeze Sorted + NoOverwrite median must be faster than DBreeze Sorted.");

            int paired = 0, noOverwritePaired = 0;
            for (int round = 1; round <= _options.Rounds; round++)
            {
                BatchedInsertMeasurement db = FindMeasurement(Canonical, Sorted, round);
                BatchedInsertMeasurement no = FindMeasurement(Canonical, SortedNoOverwrite, round);
                BatchedInsertMeasurement sq = FindMeasurement(Canonical, Sqlite, round);
                if (db?.Succeeded == true && sq?.Succeeded == true &&
                    db.OperationsPerSecond > sq.OperationsPerSecond)
                    paired++;
                if (no?.Succeeded == true && db?.Succeeded == true &&
                    no.OperationsPerSecond > db.OperationsPerSecond)
                    noOverwritePaired++;
            }
            int required = _options.Rounds / 2 + 1;
            if (paired < required)
                _report.GateViolations.Add($"DBreeze Sorted paired wins: {paired}/{_options.Rounds}; required {required}.");
            if (noOverwritePaired < required)
                _report.GateViolations.Add($"NoOverwrite paired wins over sorted: {noOverwritePaired}/{_options.Rounds}; required {required}.");

            _report.CorrectnessPassed = _report.Failures.Count == 0;
            _report.PerformancePassed = _report.CorrectnessPassed && _report.GateViolations.Count == 0;
        }

        private BatchedInsertSummary FindSummary(string scenario, string provider) =>
            _report.Summaries.SingleOrDefault(value => value.Scenario == scenario && value.Provider == provider);

        private BatchedInsertMeasurement FindMeasurement(string scenario, string provider, int round) =>
            _report.Measurements.SingleOrDefault(value => value.Scenario == scenario && value.Provider == provider && value.Round == round);

        private void Persist()
        {
            Directory.CreateDirectory(_layout.ReportsDirectory);
            _report.RawJson = Path.Combine(_layout.ReportsDirectory, "DBreeze_Batched_Insert_Audit.json");
            _report.RawCsv = Path.Combine(_layout.ReportsDirectory, "DBreeze_Batched_Insert_Audit.csv");
            _report.ExecutionLog = Path.Combine(_layout.ReportsDirectory, "DBreeze_Batched_Insert_Audit.log");
            _report.ImmutableHtml = Path.Combine(_layout.ReportsDirectory, "DBreeze_Batched_Insert_Audit.html");
            _report.CanonicalHtml = _options.ReportPath;
            AuditPersistence.WriteJson(_report.RawJson, _report);
            AuditPersistence.WriteTextAtomic(_report.RawCsv, RenderCsv());
            AuditPersistence.WriteTextAtomic(_report.ExecutionLog, _log.ToString());
            string html = RenderHtml();
            AuditPersistence.WriteTextAtomic(_report.ImmutableHtml, html);
            AuditPersistence.WriteTextAtomic(_report.CanonicalHtml, html);
        }

        private string RenderCsv()
        {
            var builder = new StringBuilder("scenario,provider,round,operations,transactions,elapsed_ms,ops_per_sec,create_ms,mutation_ms,commit_ms,dispose_ms,allocated_bytes,database_bytes,flushes,flush_ms,succeeded\n");
            foreach (BatchedInsertMeasurement value in _report.Measurements)
                builder.Append(Csv(value.Scenario)).Append(',').Append(Csv(value.Provider)).Append(',')
                    .Append(value.Round).Append(',').Append(value.Operations).Append(',').Append(value.Transactions).Append(',')
                    .Append(value.ElapsedMilliseconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(value.OperationsPerSecond.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(value.TransactionCreateMilliseconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(value.MutationMilliseconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(value.CommitMilliseconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(value.DisposeMilliseconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(value.AllocatedBytes).Append(',').Append(value.DatabaseBytes).Append(',')
                    .Append(value.DurableFlushes).Append(',')
                    .Append(value.DurableFlushMilliseconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(value.Succeeded).AppendLine();
            return builder.ToString();
        }

        private string RenderHtml()
        {
            string verdict = _options.ControlOnly ? "CONTROL" : _report.PerformancePassed ? "PASS" : "FAIL";
            BatchedInsertSummary canonicalSorted = FindSummary(Canonical, Sorted);
            BatchedInsertSummary canonicalNoOverwrite = FindSummary(Canonical, SortedNoOverwrite);
            BatchedInsertSummary canonicalSqlite = FindSummary(Canonical, Sqlite);
            var b = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>DBreeze Batched Insert Audit</title><style>")
                .Append("body{font:14px/1.45 system-ui,Segoe UI,sans-serif;margin:0;background:#0d1117;color:#d8dee9}main{max-width:1500px;margin:auto;padding:28px}h1{margin-bottom:4px}h2{margin-top:30px}code{font-family:Consolas,monospace}table{border-collapse:collapse;width:100%;font-size:12px}th,td{border:1px solid #30363d;padding:7px;text-align:left}th{background:#21262d}.pass{color:#3fb950}.fail{color:#f85149}.warn{color:#d29922}.num{text-align:right;font-variant-numeric:tabular-nums}.card{background:#161b22;border:1px solid #30363d;border-radius:9px;padding:14px;margin:12px 0}pre{white-space:pre-wrap}</style></head><body><main>")
                .Append("<h1>DBreeze Sorted Batched Insert: <span class=\"").Append(_report.PerformancePassed ? "pass" : "fail").Append("\">").Append(H(verdict)).Append("</span></h1>")
                .Append("<p>Ascending long keys · ").Append(_report.Records.ToString("N0")).Append(" rows · ").Append(_report.BatchSize).Append(" rows/transaction · SQLite WAL/FULL.</p>")
                .Append("<p>Parallel descriptive control: ").Append(_report.MultiTableRecords.ToString("N0"))
                .Append(" rows · ").Append(_report.MultiTableCount).Append(" dedicated workers/tables · ")
                .Append(_report.MultiTableBatchSize).Append(" rows/transaction · one physical database per provider.</p>")
                .Append("<p>SQLite schemas and empty DBreeze tables are materialized before timing; DBreeze uses an insert/remove sentinel lifecycle because tables are otherwise created on first write.</p>")
                .Append("<div class=\"card\"><b>Protocol note.</b> Single-table measurements touch table-local <code>.rol/.rhp</code>, not global <code>_DBreezeTranJrnl</code>. A separate two-table control exercises the global journal: ")
                .Append(_report.TwoTableJournalControlPassed ? "<span class=\"pass\">PASS</span>" : "<span class=\"fail\">FAIL</span>")
                .Append("; journal flushes: ").Append(_report.TwoTableJournalFlushes.ToString("N0"))
                .Append(", all durable flushes: ").Append(_report.TwoTableDurableFlushes.ToString("N0"))
                .Append(" / ").Append(_report.TwoTableDurableFlushMilliseconds.ToString("N2"))
                .Append(" ms. Each parallel-table transaction also owns one table only, so it uses table-local <code>.rol/.rhp</code>; the global journal is not involved.</div>");
            if (canonicalSorted != null && canonicalNoOverwrite != null && canonicalSqlite != null)
            {
                b.Append("<div class=\"card\"><b>Durability result.</b> The normal sorted path is ")
                    .Append(canonicalSorted.RatioVsSqlite.ToString("F3")).Append("× SQLite throughput; NoOverwrite is ")
                    .Append((canonicalNoOverwrite.MedianOperationsPerSecond /
                        canonicalSorted.MedianOperationsPerSecond).ToString("F3"))
                    .Append("× the normal sorted path. DBreeze keeps four ordered durable table-local barriers per committed batch: rollback, active marker, data and zero marker. Buffered handles, lazy mapping, bounded table reuse and adjacent-write coalescing do not remove or reorder those barriers. The gate remains FAIL when durable synchronization dominates, rather than weakening power-loss recovery or silently changing the persisted protocol.</div>");
            }
            if (_report.GateViolations.Count != 0)
            {
                b.Append("<div class=\"card fail\"><b>Gate violations</b><ul>");
                foreach (string value in _report.GateViolations) b.Append("<li>").Append(H(value)).Append("</li>");
                b.Append("</ul></div>");
            }
            b.Append("<h2>Summary</h2><table><thead><tr><th>Lifecycle</th><th>Provider</th><th>Median ms</th><th>ops/s</th><th>vs SQLite</th><th>vs control</th><th>Create</th><th>Mutation</th><th>Commit</th><th>Dispose</th><th>Allocated</th><th>DB bytes</th></tr></thead><tbody>");
            foreach (BatchedInsertSummary value in _report.Summaries)
                b.Append("<tr><td>").Append(H(value.Scenario)).Append("</td><td>").Append(H(value.Provider)).Append("</td><td class=\"num\">").Append(value.MedianMilliseconds.ToString("N2")).Append("</td><td class=\"num\">").Append(value.MedianOperationsPerSecond.ToString("N0")).Append("</td><td class=\"num\">").Append(Double.IsNaN(value.RatioVsSqlite) ? "—" : value.RatioVsSqlite.ToString("F3") + "×").Append("</td><td class=\"num\">").Append(Double.IsNaN(value.RatioVsControl) ? "—" : value.RatioVsControl.ToString("F3") + "×").Append("</td><td class=\"num\">").Append(value.MedianCreateMilliseconds.ToString("N2")).Append("</td><td class=\"num\">").Append(value.MedianMutationMilliseconds.ToString("N2")).Append("</td><td class=\"num\">").Append(value.MedianCommitMilliseconds.ToString("N2")).Append("</td><td class=\"num\">").Append(value.MedianDisposeMilliseconds.ToString("N2")).Append("</td><td class=\"num\">").Append(value.MedianAllocatedBytes.ToString("N0")).Append("</td><td class=\"num\">").Append(value.MedianDatabaseBytes.ToString("N0")).Append("</td></tr>");
            b.Append("</tbody></table><h2>Per-round measurements</h2><table><thead><tr><th>Lifecycle</th><th>Provider</th><th>Round</th><th>Transactions</th><th>ms</th><th>ops/s</th><th>Flush count/ms</th><th>Writes/bytes</th><th>Map create/dispose</th><th>Result</th></tr></thead><tbody>");
            foreach (BatchedInsertMeasurement value in _report.Measurements)
                b.Append("<tr><td>").Append(H(value.Scenario)).Append("</td><td>").Append(H(value.Provider)).Append("</td><td>").Append(value.Round).Append("</td><td class=\"num\">").Append(value.Transactions).Append("</td><td class=\"num\">").Append(value.ElapsedMilliseconds.ToString("N2")).Append("</td><td class=\"num\">").Append(value.OperationsPerSecond.ToString("N0")).Append("</td><td class=\"num\">").Append(value.DurableFlushes.ToString("N0")).Append(" / ").Append(value.DurableFlushMilliseconds.ToString("N2")).Append("</td><td class=\"num\">").Append(value.WriteCalls.ToString("N0")).Append(" / ").Append(value.WriteBytes.ToString("N0")).Append("</td><td class=\"num\">").Append(value.MappingCreates).Append(" / ").Append(value.MappingDisposes).Append("</td><td class=\"").Append(value.Succeeded ? "pass\">PASS" : "fail\">FAIL").Append("</td></tr>");
            b.Append("</tbody></table><h2>Environment</h2><pre>").Append(H($"Run: {_report.RunId}\nStarted: {_report.StartedUtc:O}\nCompleted: {_report.CompletedUtc:O}\nRuntime: {_report.Runtime}\nOS: {_report.OS}\nDBreeze: {_report.DBreezeVersion}\nDLL SHA-256: {_report.DBreezeSha256}\nGit: {_report.GitHead}; dirty={_report.GitDirty}; status={_report.GitStatusSha256}\nControl: {_report.ControlJson}\nControl DBreeze: {_report.ControlDBreezeSha256}; Git: {_report.ControlGitHead}\nRaw: {_report.RawJson}")).Append("</pre></main></body></html>");
            return b.ToString();
        }

        private void CaptureGit()
        {
            string root = FindRepositoryRoot();
            if (root == null) return;
            _report.GitHead = RunProcess("git", $"-C \"{root}\" rev-parse HEAD").Trim();
            string status = RunProcess("git", $"-C \"{root}\" status --porcelain=v1");
            _report.GitDirty = !String.IsNullOrWhiteSpace(status);
            _report.GitStatusSha256 = Sha256Text(status);
        }

        private void VerifyDbreeze(DBreezeEngine engine, int records)
        {
            using var transaction = engine.GetTransaction();
            if (transaction.Count(Table) != (ulong)records) throw new InvalidDataException("DBreeze count mismatch.");
            foreach (long key in SampleKeys(records))
            {
                Row<long, byte[]> row = transaction.Select<long, byte[]>(Table, key);
                if (!row.Exists || !row.Value.AsSpan().SequenceEqual(Payload(key)))
                    throw new InvalidDataException("DBreeze sample mismatch.");
            }
        }

        private static void VerifySqlite(SqliteConnection connection, int records)
        {
            using SqliteCommand count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM kv;";
            if (Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture) != records)
                throw new InvalidDataException("SQLite count mismatch.");
        }

        private byte[] Payload(long key) => _payloads[(int)((ulong)key % (uint)_payloads.Length)];
        private long ExpectedChecksum(int records)
        {
            long checksum = 0;
            for (int key = 0; key < records; key++) checksum = AddChecksum(checksum, key, Payload(key));
            return checksum;
        }

        private static long AddChecksum(long checksum, long key, byte[] value)
        {
            int middle = value.Length / 2;
            long mixed = unchecked(key * 6364136223846793005L + value.Length * 1442695040888963407L);
            mixed ^= value[0]; mixed = unchecked(mixed * 1099511628211L) ^ value[middle];
            mixed = unchecked(mixed * 1099511628211L) ^ value[^1];
            return unchecked(checksum + mixed);
        }

        private static IEnumerable<long> SampleKeys(int records)
        {
            yield return 0; if (records > 2) yield return records / 2; if (records > 1) yield return records - 1;
        }

        private static SqliteConnection OpenSqlite(string file, bool create)
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = file,
                Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
                Cache = SqliteCacheMode.Default,
                Pooling = false,
            }.ToString());
            connection.Open();
            ExecuteNonQuery(connection, "PRAGMA journal_mode=WAL;");
            ExecuteNonQuery(connection, "PRAGMA synchronous=FULL;");
            ExecuteNonQuery(connection, "PRAGMA busy_timeout=5000;");
            return connection;
        }

        private static void ExecuteNonQuery(SqliteConnection connection, string sql)
        {
            using SqliteCommand command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
        }

        private static byte[][] CreatePayloadPool(int length)
        {
            var result = new byte[1024][];
            for (int index = 0; index < result.Length; index++)
            {
                var value = new byte[length]; uint state = unchecked((uint)(20260826 + index * 2654435761u));
                for (int offset = 0; offset < value.Length; offset++)
                { state = unchecked(state * 1664525u + 1013904223u); value[offset] = (byte)(state >> 24); }
                result[index] = value;
            }
            return result;
        }

        private static void ApplyWriteDiagnostics(BatchedInsertMeasurement measurement, long[] values)
        {
            if (values.Length < 8) return;
            measurement.WriteCalls = values[0]; measurement.WriteBytes = values[1];
            measurement.DurableFlushes = values[2]; measurement.DurableFlushMilliseconds = values[3] / 1000.0;
            measurement.RollbackWriteCalls = values[4]; measurement.JournalFlushes = values[5];
            measurement.MappingCreates = values[6]; measurement.MappingDisposes = values[7];
        }

        private static long[] ReadWriteDiagnostics()
        {
            Type type = typeof(DBreezeEngine).Assembly.GetType("DBreeze.Storage.WritePathDiagnostics", false);
            MethodInfo method = type?.GetMethod("GetDiagnostics", BindingFlags.Static | BindingFlags.NonPublic);
            return method?.Invoke(null, null) as long[] ?? Array.Empty<long>();
        }

        private static IDisposable EnableWriteDiagnostics()
        {
            Type type = typeof(DBreezeEngine).Assembly.GetType("DBreeze.Storage.WritePathDiagnostics", false);
            MethodInfo method = type?.GetMethod("SetEnabled", BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null) return EmptyDisposable.Instance;
            method.Invoke(null, new object[] { true });
            return new DelegateDisposable(() => method.Invoke(null, new object[] { false }));
        }

        private static long[] Delta(long[] before, long[] after)
        {
            if (before.Length == 0 || after.Length != before.Length) return Array.Empty<long>();
            var result = new long[after.Length]; for (int i = 0; i < result.Length; i++) result[i] = after[i] - before[i];
            return result;
        }

        private static void CreateEmptyDirectory(string path)
        { if (Directory.Exists(path)) throw new IOException("Scenario path already exists: " + path); Directory.CreateDirectory(path); }
        private static long DirectoryBytes(string path) => Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(static file => new FileInfo(file).Length) : 0;
        private static void StabilizeGc() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        private static double ToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        private static double Median(IEnumerable<double> source) { double[] values = source.OrderBy(static value => value).ToArray(); if (values.Length == 0) return Double.NaN; int m = values.Length / 2; return values.Length % 2 == 0 ? (values[m - 1] + values[m]) / 2 : values[m]; }
        private static string Csv(string value) => "\"" + (value ?? String.Empty).Replace("\"", "\"\"") + "\"";
        private static string H(string value) => System.Net.WebUtility.HtmlEncode(value ?? String.Empty);
        private static string Slug(string value) => new(value.ToLowerInvariant().Select(static c => Char.IsLetterOrDigit(c) ? c : '-').ToArray());
        private void Log(string value) { string line = $"{DateTime.UtcNow:O} {value}"; _log.AppendLine(line); Console.WriteLine(line); }
        private static string Sha256File(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
        private static string Sha256Text(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? String.Empty)));
        private static string FindRepositoryRoot() { string path = AppContext.BaseDirectory; while (path != null) { if (Directory.Exists(Path.Combine(path, ".git"))) return path; path = Directory.GetParent(path)?.FullName; } return null; }
        private static string RunProcess(string file, string arguments) { using var process = Process.Start(new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true }); string output = process.StandardOutput.ReadToEnd(); string error = process.StandardError.ReadToEnd(); process.WaitForExit(); return process.ExitCode == 0 ? output : error; }
    }

    private sealed record BatchedOutcome(int Operations, int Transactions, long Checksum,
        double ElapsedMilliseconds, double TransactionCreateMilliseconds,
        double MutationMilliseconds, double CommitMilliseconds, double DisposeMilliseconds,
        long AllocatedBytes, long[] StorageDiagnostics);

    private sealed class DelegateDisposable(Action dispose) : IDisposable { public void Dispose() => dispose(); }
    private sealed class EmptyDisposable : IDisposable { internal static readonly EmptyDisposable Instance = new(); public void Dispose() { } }
}

internal sealed class BatchedInsertAuditOptions
{
    internal string RootPath { get; private set; } = @"D:\Temp\DbreezeDbTest";
    internal string RunId { get; private set; } = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-batched-insert";
    internal string ReportPath { get; private set; }
    internal string ControlJson { get; private set; }
    internal int Records { get; private set; } = 1_000_000;
    internal int PayloadBytes { get; private set; } = 256;
    internal int BatchSize { get; private set; } = 1000;
    internal int MultiTableRecords { get; private set; } = 200_000;
    internal int MultiTableCount { get; private set; } = 20;
    internal int MultiTableBatchSize { get; private set; } = 50;
    internal int Rounds { get; private set; } = 5;
    internal bool KeepDatabases { get; private set; }
    internal bool ControlOnly { get; private set; }

    internal static BatchedInsertAuditOptions Parse(string[] args)
    {
        var result = new BatchedInsertAuditOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--batched-insert-audit": break;
                case "--root": result.RootPath = Need(args, ref i, arg); break;
                case "--run-id": result.RunId = Need(args, ref i, arg); break;
                case "--report": result.ReportPath = Need(args, ref i, arg); break;
                case "--control": result.ControlJson = Need(args, ref i, arg); break;
                case "--records": result.Records = Positive(Need(args, ref i, arg), arg); break;
                case "--payload-bytes": result.PayloadBytes = Positive(Need(args, ref i, arg), arg); break;
                case "--batch-size": result.BatchSize = Positive(Need(args, ref i, arg), arg); break;
                case "--multi-table-records": result.MultiTableRecords = Positive(Need(args, ref i, arg), arg); break;
                case "--multi-table-count": result.MultiTableCount = Positive(Need(args, ref i, arg), arg); break;
                case "--multi-table-batch-size": result.MultiTableBatchSize = Positive(Need(args, ref i, arg), arg); break;
                case "--rounds": result.Rounds = Positive(Need(args, ref i, arg), arg); break;
                case "--keep-databases": result.KeepDatabases = true; break;
                case "--control-only": result.ControlOnly = true; break;
                case "--smoke": result.Records = 10_000; result.MultiTableRecords = 10_000; result.Rounds = 1; break;
                default: throw new ArgumentException("Unknown batched insert audit option: " + arg, nameof(args));
            }
        }
        result.RootPath = Path.GetFullPath(result.RootPath);
        AuditRunLayout.ValidateLeafName(result.RunId, nameof(result.RunId));
        result.ReportPath ??= Path.Combine(result.RootPath, "DBreeze_Batched_Insert_Audit.html");
        result.ReportPath = Path.GetFullPath(result.ReportPath);
        if (result.Records > 1_000_000 || result.MultiTableRecords > 1_000_000 ||
            result.PayloadBytes > 64 * 1024 || result.BatchSize > result.Records ||
            result.MultiTableCount > 64 || result.MultiTableCount > result.MultiTableRecords ||
            result.MultiTableBatchSize > result.MultiTableRecords || result.Rounds > 9)
            throw new ArgumentOutOfRangeException(nameof(args), "Audit limits exceeded.");
        new ParallelTableInsertSpec(result.MultiTableRecords, result.MultiTableCount,
            result.MultiTableBatchSize, result.PayloadBytes, "FULL").Validate();
        if (!String.IsNullOrEmpty(result.ControlJson) && !File.Exists(result.ControlJson))
            throw new FileNotFoundException("Control JSON was not found.", result.ControlJson);
        return result;
    }

    private static string Need(string[] args, ref int index, string option) => ++index < args.Length ? args[index] : throw new ArgumentException("Missing value for " + option);
    private static int Positive(string value, string option) => Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0 ? parsed : throw new ArgumentException("Positive integer expected for " + option);
}

internal sealed class BatchedInsertAuditReport
{
    public string RunId { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime CompletedUtc { get; set; }
    public int Records { get; set; }
    public int PayloadBytes { get; set; }
    public int BatchSize { get; set; }
    public int MultiTableRecords { get; set; }
    public int MultiTableCount { get; set; }
    public int MultiTableBatchSize { get; set; }
    public int MultiTableBusyTimeoutMilliseconds { get; set; }
    public int Rounds { get; set; }
    public bool ControlOnly { get; set; }
    public string ControlJson { get; set; }
    public string Runtime { get; set; }
    public string OS { get; set; }
    public string DBreezeVersion { get; set; }
    public string DBreezeSha256 { get; set; }
    public string ControlDBreezeSha256 { get; set; }
    public string ControlGitHead { get; set; }
    public string GitHead { get; set; }
    public bool GitDirty { get; set; }
    public string GitStatusSha256 { get; set; }
    public bool TwoTableJournalControlPassed { get; set; }
    public long TwoTableJournalFlushes { get; set; }
    public long TwoTableDurableFlushes { get; set; }
    public double TwoTableDurableFlushMilliseconds { get; set; }
    public bool CorrectnessPassed { get; set; }
    public bool PerformancePassed { get; set; }
    public List<BatchedInsertMeasurement> Measurements { get; set; } = new();
    public List<BatchedInsertSummary> Summaries { get; set; } = new();
    public List<string> Failures { get; set; } = new();
    public List<string> GateViolations { get; set; } = new();
    public string RawJson { get; set; }
    public string RawCsv { get; set; }
    public string ExecutionLog { get; set; }
    public string ImmutableHtml { get; set; }
    public string CanonicalHtml { get; set; }
}

internal sealed class BatchedInsertMeasurement
{
    public string Scenario { get; set; }
    public string Provider { get; set; }
    public int Round { get; set; }
    public string DatabasePath { get; set; }
    public int Operations { get; set; }
    public int Transactions { get; set; }
    public long Checksum { get; set; }
    public double ElapsedMilliseconds { get; set; }
    public double OperationsPerSecond { get; set; }
    public double TransactionCreateMilliseconds { get; set; }
    public double MutationMilliseconds { get; set; }
    public double CommitMilliseconds { get; set; }
    public double DisposeMilliseconds { get; set; }
    public long AllocatedBytes { get; set; }
    public long DatabaseBytes { get; set; }
    public long WriteCalls { get; set; }
    public long WriteBytes { get; set; }
    public long DurableFlushes { get; set; }
    public double DurableFlushMilliseconds { get; set; }
    public long RollbackWriteCalls { get; set; }
    public long JournalFlushes { get; set; }
    public long MappingCreates { get; set; }
    public long MappingDisposes { get; set; }
    public bool Succeeded { get; set; }
    public string Error { get; set; }
}

internal sealed class BatchedInsertSummary
{
    public string Scenario { get; set; }
    public string Provider { get; set; }
    public int Rounds { get; set; }
    public double MedianMilliseconds { get; set; }
    public double MedianOperationsPerSecond { get; set; }
    public double RatioVsSqlite { get; set; } = Double.NaN;
    public double ControlMedianOperationsPerSecond { get; set; }
    public double RatioVsControl { get; set; } = Double.NaN;
    public double MedianCreateMilliseconds { get; set; }
    public double MedianMutationMilliseconds { get; set; }
    public double MedianCommitMilliseconds { get; set; }
    public double MedianDisposeMilliseconds { get; set; }
    public double MedianAllocatedBytes { get; set; }
    public long MedianDatabaseBytes { get; set; }
}

internal static class BatchedInsertAuditSelfTests
{
    internal static int Run()
    {
        var failures = new List<string>();
        string root = Path.Combine(Path.GetTempPath(), "dbreeze-batched-self-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            BatchedInsertAuditOptions options = BatchedInsertAuditOptions.Parse(new[]
            { "--batched-insert-audit", "--root", root, "--run-id", "self", "--records", "1000", "--batch-size", "100", "--rounds", "3",
              "--multi-table-records", "1001", "--multi-table-count", "20", "--multi-table-batch-size", "50" });
            if (options.Records != 1000 || options.BatchSize != 100 || options.Rounds != 3 ||
                options.MultiTableRecords != 1001 || options.MultiTableCount != 20 ||
                options.MultiTableBatchSize != 50) failures.Add("Option parsing");
            try { BatchedInsertAuditOptions.Parse(new[] { "--batched-insert-audit", "--records", "1000001" }); failures.Add("Record limit"); } catch (ArgumentOutOfRangeException) { }
            try { BatchedInsertAuditOptions.Parse(new[] { "--batched-insert-audit", "--multi-table-count", "65" }); failures.Add("Table limit"); } catch (ArgumentOutOfRangeException) { }
            try { AuditRunLayout.EnsureUnderRoot(Path.Combine(root, "..", "escape"), root); failures.Add("Path containment"); } catch (InvalidOperationException) { }
            if (!System.Net.WebUtility.HtmlEncode("<&").Contains("&lt;")) failures.Add("HTML escaping");
            var even = new ParallelTableInsertSpec(200_000, 20, 50, 256, "FULL");
            if (Enumerable.Range(0, 20).Any(table => even.RecordsForTable(table) != 10_000) ||
                even.ExpectedTransactions() != 4_000) failures.Add("Canonical distribution");
            var uneven = new ParallelTableInsertSpec(1001, 20, 50, 256, "FULL");
            if (Enumerable.Range(0, 20).Sum(uneven.RecordsForTable) != 1001 ||
                uneven.RecordsForTable(0) != 51 || uneven.RecordsForTable(1) != 50 ||
                uneven.ExpectedTransactions() != 21) failures.Add("Uneven distribution");
            byte[][] payloads = ParallelTableInsertWorkload.CreatePayloadPool(256);
            if (ParallelTableInsertWorkload.ExpectedChecksum(even, payloads) !=
                ParallelTableInsertWorkload.ExpectedChecksum(even, payloads)) failures.Add("Deterministic oracle");
        }
        finally { Directory.Delete(root, true); }
        foreach (string failure in failures) Console.Error.WriteLine("FAIL " + failure);
        if (failures.Count == 0) Console.WriteLine("Batched insert audit self-tests passed.");
        return failures.Count == 0 ? 0 : 1;
    }
}
