using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DBreeze;
using DBreeze.DataTypes;
using Microsoft.Data.Sqlite;

namespace DBreeze.Net8.Benchmarks;

internal sealed class SqliteComparisonSuite
{
    private const string DBreezeProvider = "DBreeze";
    private const string DBreezeRksProvider = "DBreeze RKS";
    private const string DBreezeRksNoOverwriteProvider = "DBreeze RKS + NoOverwrite";
    private const string DBreezeSortedProvider = "DBreeze Sorted";
    private const string DBreezeRksRemoveProvider = "DBreeze RKS Remove";
    private const string DBreezeSortedNoOverwriteProvider = "DBreeze Sorted + NoOverwrite";
    private const string DBreezeRksRemoveNoOverwriteProvider = "DBreeze RKS Remove + NoOverwrite";
    private const string SqliteProvider = "SQLite";
    private const string MainTable = "kv";
    private const string PrefixTable = "prefix";

    private enum DbreezeUpdateStrategy
    {
        Direct,
        Rks,
        RksNoOverwrite,
    }

    private enum DbreezeDeleteStrategy
    {
        Direct,
        Sorted,
        Rks,
        SortedNoOverwrite,
        RksNoOverwrite,
    }

    private readonly SqliteComparisonOptions _options;
    private readonly AuditRunLayout _layout;
    private readonly SqliteComparisonReport _report;
    private byte[][] _payloads;
    private byte[][] _updatedPayloads;
    private long[] _sequentialKeys;
    private long[] _randomKeys;
    private long[] _pointKeys;
    private long[] _mixedKeys;
    private long[] _updateKeys;
    private long[] _deleteKeys;
    private int _rangeCount;
    private int _rangeSize;
    private int _prefixGroups;
    private int _parallelOperationsPerWorker;

    private SqliteComparisonSuite(SqliteComparisonOptions options)
    {
        _options = options;
        _layout = new AuditRunLayout(options.RootPath, options.RunId);
        _report = new SqliteComparisonReport
        {
            Metadata = SqliteComparisonMetadata.Create(options, _layout),
            Configuration = new SqliteComparisonConfiguration
            {
                Records = options.Records,
                PayloadBytes = options.PayloadBytes,
                Repetitions = options.Repetitions,
                Parallelism = options.Parallelism,
                Smoke = options.Smoke,
                KeepDatabases = options.KeepDatabases,
                SqliteSynchronous = options.SqliteSynchronous,
            },
        };
    }

    internal static int Run(string[] args)
    {
        SqliteComparisonOptions options;
        try
        {
            options = SqliteComparisonOptions.Parse(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("SQLite comparison configuration error: " + exception.Message);
            return 2;
        }

        try
        {
            return new SqliteComparisonSuite(options).Execute();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("SQLite comparison startup failure: " + exception);
            return 2;
        }
    }

    internal static int RunAugment(string[] args)
    {
        try
        {
            SqliteComparisonAugmentOptions augment = SqliteComparisonAugmentOptions.Parse(args);
            SqliteComparisonReport source = AuditPersistence.ReadJson<SqliteComparisonReport>(augment.SourceReportPath);
            SqliteComparisonOptions options = SqliteComparisonOptions.CreateForAugment(augment, source.Configuration);
            var suite = new SqliteComparisonSuite(options);
            int result = suite.ExecuteAugment(source, augment.SourceReportPath, augment.Kind);
            if (result == 0 && augment.Kind == SqliteComparisonAugmentKind.SortedDelete &&
                !SortedDeleteMeetsTarget(suite._report, out string reason))
            {
                suite.Log("Sorted delete missed the target; starting a separate safe-variant fallback augmentation. " + reason);
                SqliteComparisonAugmentOptions fallback = augment.CreateDeleteFallback(suite._report.Metadata.RawJson);
                SqliteComparisonOptions fallbackOptions = SqliteComparisonOptions.CreateForAugment(fallback, suite._report.Configuration);
                var fallbackSuite = new SqliteComparisonSuite(fallbackOptions);
                return fallbackSuite.ExecuteAugment(suite._report, suite._report.Metadata.RawJson,
                    SqliteComparisonAugmentKind.DeleteFallbacks);
            }
            return result;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("SQLite comparison augmentation configuration error: " + exception.Message);
            return 2;
        }
    }

    private int Execute()
    {
        _layout.Create();
        try
        {
            InitializeMetadata();
            PrepareInput();
            Log($"Started run {_options.RunId}: records={_options.Records:N0}, payload={_options.PayloadBytes}, repetitions={_options.Repetitions}.");
            Persist();

            WarmUp();
            RunInsertScenarios();

            string dbreezeFixture = Path.Combine(_layout.ScratchDirectory, "fixture-dbreeze-main");
            string sqliteFixture = Path.Combine(_layout.ScratchDirectory, "fixture-sqlite-main");
            string dbreezePrefixFixture = Path.Combine(_layout.ScratchDirectory, "fixture-dbreeze-prefix");
            string sqlitePrefixFixture = Path.Combine(_layout.ScratchDirectory, "fixture-sqlite-prefix");
            BuildFixtures(dbreezeFixture, sqliteFixture, dbreezePrefixFixture, sqlitePrefixFixture);
            RunReadScenarios(dbreezeFixture, sqliteFixture, dbreezePrefixFixture, sqlitePrefixFixture);
            RunMutationScenarios(dbreezeFixture, sqliteFixture);

            ValidateCompleteness();
        }
        catch (Exception exception)
        {
            Fail("Fatal benchmark failure: " + exception);
        }

        return CompleteAndCleanup();
    }

    private int ExecuteAugment(SqliteComparisonReport source, string sourcePath,
        SqliteComparisonAugmentKind kind)
    {
        _layout.Create();
        string publishCanonicalHtml = _report.Metadata.CanonicalHtml;
        _report.Metadata.CanonicalHtml = Path.Combine(
            _layout.ReportsDirectory,
            "DBreeze_vs_SQLite.augmentation-in-progress.html");
        try
        {
            InitializeMetadata();
            switch (kind)
            {
                case SqliteComparisonAugmentKind.RksUpdate:
                case SqliteComparisonAugmentKind.RksNoOverwriteUpdate:
                    ExecuteUpdateAugment(source, sourcePath, kind);
                    break;
                case SqliteComparisonAugmentKind.SortedDelete:
                    ExecuteSortedDeleteAugment(source, sourcePath);
                    break;
                case SqliteComparisonAugmentKind.DeleteFallbacks:
                    ExecuteDeleteFallbackAugment(source, sourcePath);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
        catch (Exception exception)
        {
            Fail("Fatal comparison augmentation failure: " + exception);
        }
        return CompleteAndCleanup(publishCanonicalHtml);
    }

    private void ExecuteUpdateAugment(SqliteComparisonReport source, string sourcePath,
        SqliteComparisonAugmentKind kind)
    {
        bool noOverwrite = kind == SqliteComparisonAugmentKind.RksNoOverwriteUpdate;
        if (noOverwrite)
            ValidateNoOverwriteAugmentationSource(source, _report.Metadata.DBreezeSha256);
        else
            ValidateAugmentationSource(source, _report.Metadata.DBreezeSha256);
        PrepareImportedAugmentation(source, sourcePath);
        string provider = noOverwrite ? DBreezeRksNoOverwriteProvider : DBreezeRksProvider;
        DbreezeUpdateStrategy strategy = noOverwrite
            ? DbreezeUpdateStrategy.RksNoOverwrite
            : DbreezeUpdateStrategy.Rks;
        Log($"Started targeted {provider} update augmentation from {source.Metadata.RunId}; imported {source.Measurements.Count} measurements.");
        Persist();

        string fixture = BuildAugmentationFixture(provider + " update");
        for (int round = 1; round <= _options.Repetitions; round++)
            MeasureFresh("Random update", provider, round,
                path => { CopyDirectory(fixture, path); return DbreezeUpdate(path, strategy); });
        ValidateCompleteness(includeNoOverwrite: noOverwrite, includeSortedDelete: false);
    }

    private void ExecuteSortedDeleteAugment(SqliteComparisonReport source, string sourcePath)
    {
        ValidateSortedDeleteAugmentationSource(source, _report.Metadata.DBreezeSha256);
        PrepareImportedAugmentation(source, sourcePath);
        Log($"Started targeted {DBreezeSortedProvider} delete augmentation from {source.Metadata.RunId}; imported {source.Measurements.Count} measurements.");
        Persist();

        string fixture = BuildAugmentationFixture(DBreezeSortedProvider + " delete");
        for (int round = 1; round <= _options.Repetitions; round++)
            MeasureFresh("Random delete", DBreezeSortedProvider, round,
                path => { CopyDirectory(fixture, path); return DbreezeDelete(path, DbreezeDeleteStrategy.Sorted); });
        ValidateCompleteness(includeSortedDelete: true);
        RecordSortedDeleteFinding();
    }

    private void ExecuteDeleteFallbackAugment(SqliteComparisonReport source, string sourcePath)
    {
        ValidateDeleteFallbackAugmentationSource(source, _report.Metadata.DBreezeSha256);
        PrepareImportedAugmentation(source, sourcePath);
        Log($"Started safe delete fallback augmentation from {source.Metadata.RunId}; imported {source.Measurements.Count} measurements.");
        Persist();

        string fixture = BuildAugmentationFixture("safe delete fallbacks");
        var variants = new[]
        {
            (DBreezeRksRemoveProvider, DbreezeDeleteStrategy.Rks),
            (DBreezeSortedNoOverwriteProvider, DbreezeDeleteStrategy.SortedNoOverwrite),
            (DBreezeRksRemoveNoOverwriteProvider, DbreezeDeleteStrategy.RksNoOverwrite),
        };
        for (int round = 1; round <= _options.Repetitions; round++)
        {
            int rotation = (round - 1) % variants.Length;
            foreach ((string provider, DbreezeDeleteStrategy strategy) in variants.Skip(rotation).Concat(variants.Take(rotation)))
                MeasureFresh("Random delete", provider, round,
                    path => { CopyDirectory(fixture, path); return DbreezeDelete(path, strategy); });
        }
        ValidateCompleteness(includeSortedDelete: true, includeDeleteFallbacks: true);
        RecordDeleteFallbackFinding();
    }

    private void PrepareImportedAugmentation(SqliteComparisonReport source, string sourcePath)
    {
        PrepareInput();
        _report.Measurements = source.Measurements.ToList();
        _report.Findings = source.Findings?.ToList() ?? new List<string>();
        _report.Metadata.AugmentedFromRunId = source.Metadata.RunId;
        _report.Metadata.AugmentedFromJson = sourcePath;
        _report.Metadata.ImportedMeasurementCount = source.Measurements.Count;
    }

    private string BuildAugmentationFixture(string description)
    {
        string fixture = Path.Combine(_layout.ScratchDirectory, "fixture-dbreeze-main");
        Log($"Building unmeasured DBreeze fixture for {description} augmentation.");
        DbreezeInsert(fixture, _sequentialKeys, false, 0);
        return fixture;
    }

    private int CompleteAndCleanup(string publishCanonicalHtml = null)
    {
        _report.Metadata.CompletedUtc = DateTime.UtcNow;
        _report.Succeeded = _report.Failures.Count == 0;
        Persist();

        if (_report.Succeeded && !_options.KeepDatabases)
        {
            try
            {
                _layout.CleanupScratch();
                Log("Owned scratch directory removed after successful run.");
            }
            catch (Exception exception)
            {
                Fail("Scratch cleanup failed: " + exception.Message);
                _report.Succeeded = false;
            }
        }

        string augmentationStagingHtml = null;
        if (_report.Succeeded && !string.IsNullOrEmpty(publishCanonicalHtml))
        {
            augmentationStagingHtml = _report.Metadata.CanonicalHtml;
            _report.Metadata.CanonicalHtml = publishCanonicalHtml;
        }

        Persist();
        if (_report.Succeeded && !String.IsNullOrEmpty(augmentationStagingHtml) &&
            !String.Equals(augmentationStagingHtml, _report.Metadata.CanonicalHtml, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                AuditRunLayout.EnsureUnderRoot(augmentationStagingHtml, _layout.ReportsDirectory);
                File.Delete(augmentationStagingHtml);
            }
            catch (Exception exception)
            {
                Fail("Augmentation staging HTML cleanup failed: " + exception.Message);
                _report.Succeeded = false;
                Persist();
            }
        }
        Console.WriteLine($"SQLite comparison {(_report.Succeeded ? "COMPLETE" : "INCOMPLETE")}: {_report.Metadata.CanonicalHtml}");
        return _report.Succeeded ? 0 : 1;
    }

    internal static void ValidateAugmentationSource(SqliteComparisonReport source, string currentDBreezeSha256)
    {
        ValidateAugmentationSourceCore(source, currentDBreezeSha256);
        ValidateRandomUpdateProviders(source, DBreezeProvider, SqliteProvider);
    }

    internal static void ValidateNoOverwriteAugmentationSource(
        SqliteComparisonReport source, string currentDBreezeSha256)
    {
        ValidateAugmentationSourceCore(source, currentDBreezeSha256);
        if (source.Measurements.Count != 78 || source.Summaries == null || source.Summaries.Count != 26)
            throw new InvalidDataException("Source report must contain exactly 78 measurements and 26 summaries.");
        ValidateRandomUpdateProviders(source, DBreezeProvider, DBreezeRksProvider, SqliteProvider);
    }

    internal static void ValidateSortedDeleteAugmentationSource(
        SqliteComparisonReport source, string currentDBreezeSha256)
    {
        ValidateAugmentationSourceCore(source, currentDBreezeSha256);
        if (source.Measurements.Count != 81 || source.Summaries == null || source.Summaries.Count != 27)
            throw new InvalidDataException("Source report must contain exactly 81 measurements and 27 summaries.");
        ValidateScenarioProviders(source, "Random delete", DBreezeProvider, SqliteProvider);
    }

    internal static void ValidateDeleteFallbackAugmentationSource(
        SqliteComparisonReport source, string currentDBreezeSha256)
    {
        ValidateAugmentationSourceCore(source, currentDBreezeSha256);
        if (source.Measurements.Count != 84 || source.Summaries == null || source.Summaries.Count != 28)
            throw new InvalidDataException("Fallback source report must contain exactly 84 measurements and 28 summaries.");
        ValidateScenarioProviders(source, "Random delete",
            DBreezeProvider, DBreezeSortedProvider, SqliteProvider);
        if (SortedDeleteMeetsTarget(source, out _))
            throw new InvalidDataException("Sorted delete already meets both targets; fallback augmentation is not required.");
    }

    private static void ValidateAugmentationSourceCore(
        SqliteComparisonReport source, string currentDBreezeSha256)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (source.Configuration == null || source.Metadata == null ||
            source.Measurements == null || source.Failures == null)
            throw new InvalidDataException("Source comparison report is incomplete.");
        if (source.Configuration.Records != 1_000_000 ||
            source.Configuration.PayloadBytes != 256 ||
            source.Configuration.PayloadPoolSize != 1024 ||
            source.Configuration.Repetitions != 3 ||
            source.Configuration.Parallelism != 4 ||
            source.Configuration.RandomSeed != 20260826 ||
            source.Configuration.Smoke ||
            !String.Equals(source.Configuration.SqliteJournalMode, "WAL", StringComparison.OrdinalIgnoreCase) ||
            !String.Equals(source.Configuration.SqliteSynchronous, "FULL", StringComparison.OrdinalIgnoreCase) ||
            source.Configuration.SqliteBusyTimeoutMilliseconds != 5000)
        {
            throw new InvalidDataException(
                "Source report configuration does not match the canonical 1M/FULL comparison.");
        }
        if (!source.Succeeded || source.Failures.Count != 0)
            throw new InvalidDataException("Source comparison report is not a complete successful run.");
        if (source.Measurements.Any(static value => !value.Succeeded))
            throw new InvalidDataException("Source comparison contains failed measurements.");
        if (!String.Equals(source.Metadata.DBreezeSha256, currentDBreezeSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Source report DBreeze assembly SHA-256 differs from the currently loaded assembly.");
    }

    private static void ValidateRandomUpdateProviders(
        SqliteComparisonReport source, params string[] expectedProviders)
        => ValidateScenarioProviders(source, "Random update", expectedProviders);

    private static void ValidateScenarioProviders(
        SqliteComparisonReport source, string scenario, params string[] expectedProviders)
    {
        SqliteComparisonMeasurement[] valuesForScenario = source.Measurements
            .Where(value => value.Scenario == scenario).ToArray();
        var expected = new HashSet<string>(expectedProviders, StringComparer.Ordinal);
        var actual = new HashSet<string>(valuesForScenario.Select(static value => value.Provider), StringComparer.Ordinal);
        if (!actual.SetEquals(expected))
            throw new InvalidDataException($"Source report has unexpected {scenario} providers.");

        int repetitions = source.Configuration.Repetitions;
        int[] expectedRounds = Enumerable.Range(1, repetitions).ToArray();
        foreach (string provider in expectedProviders)
        {
            SqliteComparisonMeasurement[] values = valuesForScenario
                .Where(value => value.Provider == provider)
                .OrderBy(static value => value.Round).ToArray();
            if (values.Length != repetitions ||
                !values.Select(static value => value.Round).SequenceEqual(expectedRounds))
                throw new InvalidDataException($"Source report has missing or duplicate {provider} {scenario} rounds.");
        }

        long[] operations = valuesForScenario.Select(static value => value.Operations).Distinct().ToArray();
        long[] returned = valuesForScenario.Select(static value => value.ReturnedCount).Distinct().ToArray();
        long[] checksums = valuesForScenario.Select(static value => value.Checksum).Distinct().ToArray();
        if (operations.Length != 1 || returned.Length != 1 || checksums.Length != 1)
            throw new InvalidDataException($"Source report {scenario} oracles differ.");
    }

    internal static bool SortedDeleteMeetsTarget(SqliteComparisonReport report, out string reason)
    {
        List<SqliteComparisonSummary> summaries = SqliteComparisonArtifacts.BuildSummaries(report.Measurements);
        SqliteComparisonSummary direct = summaries.SingleOrDefault(static value =>
            value.Scenario == "Random delete" && value.Provider == DBreezeProvider);
        SqliteComparisonSummary sorted = summaries.SingleOrDefault(static value =>
            value.Scenario == "Random delete" && value.Provider == DBreezeSortedProvider);
        SqliteComparisonSummary sqlite = summaries.SingleOrDefault(static value =>
            value.Scenario == "Random delete" && value.Provider == SqliteProvider);
        if (direct == null || sorted == null || sqlite == null ||
            direct.MedianOperationsPerSecond <= 0 || sqlite.MedianOperationsPerSecond <= 0)
        {
            reason = "Required direct, sorted or SQLite median is missing.";
            return false;
        }

        double sqliteRatio = sorted.MedianOperationsPerSecond / sqlite.MedianOperationsPerSecond;
        double directRatio = sorted.MedianOperationsPerSecond / direct.MedianOperationsPerSecond;
        reason = $"SQLite ratio={sqliteRatio:F3}× (target ≥0.850×); speedup vs direct={directRatio:F3}× (target ≥1.050×).";
        return sqliteRatio >= 0.85 && directRatio >= 1.05;
    }

    private void RecordSortedDeleteFinding()
    {
        bool passed = SortedDeleteMeetsTarget(_report, out string reason);
        _report.Findings.Add("DBreeze Sorted random delete " + (passed ? "met" : "missed") + " the primary target. " + reason);
        Log(_report.Findings[^1]);
    }

    private void RecordDeleteFallbackFinding()
    {
        List<SqliteComparisonSummary> summaries = SqliteComparisonArtifacts.BuildSummaries(_report.Measurements);
        SqliteComparisonSummary sqlite = summaries.Single(value =>
            value.Scenario == "Random delete" && value.Provider == SqliteProvider);
        SqliteComparisonSummary[] candidates = summaries.Where(value =>
                value.Scenario == "Random delete" && value.Provider != SqliteProvider)
            .OrderByDescending(static value => value.MedianOperationsPerSecond)
            .ToArray();
        SqliteComparisonSummary fastest = candidates[0];
        SqliteComparisonSummary recommended = candidates
            .Where(value => value.MedianOperationsPerSecond >= fastest.MedianOperationsPerSecond * 0.97)
            .OrderBy(static value => value.MedianDatabaseBytes)
            .First();
        double ratio = recommended.MedianOperationsPerSecond / sqlite.MedianOperationsPerSecond;
        string finding = $"Recommended random-delete provider: {recommended.Provider}; {recommended.MedianOperationsPerSecond:N0} ops/s, " +
            $"{ratio:F3}× SQLite, median DB {recommended.MedianDatabaseBytes / 1048576.0:F2} MiB.";
        if (candidates.All(value => value.MedianOperationsPerSecond < sqlite.MedianOperationsPerSecond * 0.85))
        {
            finding += " No safe variant reached 85% of SQLite; next core investigation should instrument LTrie generation-map eviction/save, node reads/writes and rollback bytes.";
        }
        _report.Findings.Add(finding);
        Log(finding);
    }

    private void InitializeMetadata()
    {
        Assembly dbreeze = typeof(DBreezeEngine).Assembly;
        _report.Metadata.DBreezeAssembly = dbreeze.Location;
        _report.Metadata.DBreezeVersion = dbreeze.GetName().Version?.ToString() ?? String.Empty;
        _report.Metadata.DBreezeSha256 = Sha256File(dbreeze.Location);
        _report.Metadata.MicrosoftDataSqliteVersion = typeof(SqliteConnection).Assembly.GetName().Version?.ToString() ?? String.Empty;

        using (var connection = new SqliteConnection("Data Source=:memory:"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT sqlite_version();";
            _report.Metadata.NativeSqliteVersion = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? String.Empty;
        }

        string repository = FindRepositoryRoot();
        if (!String.IsNullOrEmpty(repository))
        {
            _report.Metadata.GitHead = RunProcess("git", $"-C \"{repository}\" rev-parse HEAD").Trim();
            string status = RunProcess("git", $"-C \"{repository}\" status --porcelain=v1");
            _report.Metadata.GitDirty = !String.IsNullOrWhiteSpace(status);
            _report.Metadata.GitStatusSha256 = Sha256Text(status);
        }
    }

    private void PrepareInput()
    {
        _payloads = CreatePayloadPool(_options.PayloadBytes, updated: false);
        _updatedPayloads = CreatePayloadPool(_options.PayloadBytes, updated: true);
        _sequentialKeys = Enumerable.Range(0, _options.Records).Select(static value => (long)value).ToArray();
        _randomKeys = (long[])_sequentialKeys.Clone();
        var random = new Random(_report.Configuration.RandomSeed);
        for (int i = _randomKeys.Length - 1; i > 0; i--)
        {
            int other = random.Next(i + 1);
            (_randomKeys[i], _randomKeys[other]) = (_randomKeys[other], _randomKeys[i]);
        }

        int pointCount = Math.Min(250_000, _options.Records);
        _pointKeys = _randomKeys.Take(pointCount).ToArray();
        _mixedKeys = new long[pointCount];
        for (int i = 0; i < _mixedKeys.Length; i++)
            _mixedKeys[i] = i % 10 == 0 ? _options.Records + i + 1L : _randomKeys[i];
        _updateKeys = _randomKeys.Take(Math.Min(250_000, Math.Max(1, _options.Records / 4))).ToArray();
        _deleteKeys = _randomKeys.Take(Math.Min(100_000, Math.Max(1, _options.Records / 10))).ToArray();
        _rangeSize = Math.Min(1000, Math.Max(10, _options.Records / 100));
        _rangeCount = Math.Min(1000, Math.Max(1, _options.Records / _rangeSize));
        _prefixGroups = Math.Min(1000, Math.Max(1, _options.Records / 100));
        _parallelOperationsPerWorker = Math.Min(100_000, _options.Records);
    }

    private void WarmUp()
    {
        Log("Warm-up started.");
        int count = Math.Min(2_000, _options.Records);
        long[] keys = _sequentialKeys.Take(count).ToArray();
        string dbreezePath = Path.Combine(_layout.ScratchDirectory, "warmup-dbreeze");
        string sqlitePath = Path.Combine(_layout.ScratchDirectory, "warmup-sqlite");
        DbreezeInsert(dbreezePath, keys, randomKeySorter: false, commitBatch: 0);
        SqliteInsert(sqlitePath, keys, commitBatch: 0);
        AuditRunLayout.DeleteOwnedChild(dbreezePath, _layout.ScratchDirectory);
        AuditRunLayout.DeleteOwnedChild(sqlitePath, _layout.ScratchDirectory);
        Log("Warm-up completed.");
    }

    private void RunInsertScenarios()
    {
        for (int round = 1; round <= _options.Repetitions; round++)
        {
            RunAlternating("Sequential bulk insert", round,
                path => DbreezeInsert(path, _sequentialKeys, false, 0),
                path => SqliteInsert(path, _sequentialKeys, 0));
            RunAlternating("Sequential batched insert (1000/commit)", round,
                path => DbreezeInsert(path, _sequentialKeys, false, 1000),
                path => SqliteInsert(path, _sequentialKeys, 1000));

            var actions = new List<(string Provider, Func<string, SqliteMeasuredOutcome> Action)>
            {
                (DBreezeProvider, path => DbreezeInsert(path, _randomKeys, false, 0)),
                (SqliteProvider, path => SqliteInsert(path, _randomKeys, 0)),
                (DBreezeRksProvider, path => DbreezeInsert(path, _randomKeys, true, 0)),
            };
            int rotation = (round - 1) % actions.Count;
            foreach ((string provider, Func<string, SqliteMeasuredOutcome> action) in actions.Skip(rotation).Concat(actions.Take(rotation)))
                MeasureFresh("Random bulk insert", provider, round, action);
        }
    }

    private void BuildFixtures(string dbreezeMain, string sqliteMain, string dbreezePrefix, string sqlitePrefix)
    {
        Log("Building unmeasured canonical fixtures.");
        DbreezeInsert(dbreezeMain, _sequentialKeys, false, 0);
        SqliteInsert(sqliteMain, _sequentialKeys, 0);
        BuildDbreezePrefixFixture(dbreezePrefix);
        BuildSqlitePrefixFixture(sqlitePrefix);
        Log("Canonical fixtures completed.");
    }

    private void RunReadScenarios(string dbreezeMain, string sqliteMain, string dbreezePrefix, string sqlitePrefix)
    {
        for (int round = 1; round <= _options.Repetitions; round++)
        {
            RunAlternatingExisting("Random point reads (hits)", round, dbreezeMain, sqliteMain,
                path => DbreezePointReads(path, _pointKeys), path => SqlitePointReads(path, _pointKeys));
            RunAlternatingExisting("Mixed point reads (90% hits)", round, dbreezeMain, sqliteMain,
                path => DbreezePointReads(path, _mixedKeys), path => SqlitePointReads(path, _mixedKeys));
            RunAlternatingExisting("Full forward traversal", round, dbreezeMain, sqliteMain,
                path => DbreezeFullTraversal(path, forward: true), path => SqliteFullTraversal(path, forward: true));
            RunAlternatingExisting("Full backward traversal", round, dbreezeMain, sqliteMain,
                path => DbreezeFullTraversal(path, forward: false), path => SqliteFullTraversal(path, forward: false));
            RunAlternatingExisting("Bounded ranges", round, dbreezeMain, sqliteMain,
                DbreezeRanges, SqliteRanges);
            RunAlternatingExisting("Prefix traversal", round, dbreezePrefix, sqlitePrefix,
                DbreezePrefixTraversal, SqlitePrefixTraversal);
            RunAlternatingExisting("Parallel point reads", round, dbreezeMain, sqliteMain,
                DbreezeParallelReads, SqliteParallelReads);
        }
    }

    private void RunMutationScenarios(string dbreezeFixture, string sqliteFixture)
    {
        for (int round = 1; round <= _options.Repetitions; round++)
        {
            var updates = new List<(string Provider, Func<string, SqliteMeasuredOutcome> Action)>
            {
                (DBreezeProvider, path => { CopyDirectory(dbreezeFixture, path); return DbreezeUpdate(path, DbreezeUpdateStrategy.Direct); }),
                (SqliteProvider, path => { CopyDirectory(sqliteFixture, path); return SqliteUpdate(path); }),
                (DBreezeRksProvider, path => { CopyDirectory(dbreezeFixture, path); return DbreezeUpdate(path, DbreezeUpdateStrategy.Rks); }),
                (DBreezeRksNoOverwriteProvider, path => { CopyDirectory(dbreezeFixture, path); return DbreezeUpdate(path, DbreezeUpdateStrategy.RksNoOverwrite); }),
            };
            int rotation = (round - 1) % updates.Count;
            foreach ((string provider, Func<string, SqliteMeasuredOutcome> action) in updates.Skip(rotation).Concat(updates.Take(rotation)))
                MeasureFresh("Random update", provider, round, action);

            var deletes = new List<(string Provider, Func<string, SqliteMeasuredOutcome> Action)>
            {
                (DBreezeProvider, path => { CopyDirectory(dbreezeFixture, path); return DbreezeDelete(path, DbreezeDeleteStrategy.Direct); }),
                (DBreezeSortedProvider, path => { CopyDirectory(dbreezeFixture, path); return DbreezeDelete(path, DbreezeDeleteStrategy.Sorted); }),
                (SqliteProvider, path => { CopyDirectory(sqliteFixture, path); return SqliteDelete(path); }),
            };
            rotation = (round - 1) % deletes.Count;
            foreach ((string provider, Func<string, SqliteMeasuredOutcome> action) in deletes.Skip(rotation).Concat(deletes.Take(rotation)))
                MeasureFresh("Random delete", provider, round, action);
        }
    }

    private void RunAlternating(string scenario, int round,
        Func<string, SqliteMeasuredOutcome> dbreeze,
        Func<string, SqliteMeasuredOutcome> sqlite) =>
        RunAlternatingFresh(scenario, round, dbreeze, sqlite);

    private void RunAlternatingFresh(string scenario, int round,
        Func<string, SqliteMeasuredOutcome> dbreeze,
        Func<string, SqliteMeasuredOutcome> sqlite)
    {
        if (round % 2 == 1)
        {
            MeasureFresh(scenario, DBreezeProvider, round, dbreeze);
            MeasureFresh(scenario, SqliteProvider, round, sqlite);
        }
        else
        {
            MeasureFresh(scenario, SqliteProvider, round, sqlite);
            MeasureFresh(scenario, DBreezeProvider, round, dbreeze);
        }
    }

    private void RunAlternatingExisting(string scenario, int round, string dbreezePath, string sqlitePath,
        Func<string, SqliteMeasuredOutcome> dbreeze,
        Func<string, SqliteMeasuredOutcome> sqlite)
    {
        if (round % 2 == 1)
        {
            MeasureExisting(scenario, DBreezeProvider, round, dbreezePath, dbreeze);
            MeasureExisting(scenario, SqliteProvider, round, sqlitePath, sqlite);
        }
        else
        {
            MeasureExisting(scenario, SqliteProvider, round, sqlitePath, sqlite);
            MeasureExisting(scenario, DBreezeProvider, round, dbreezePath, dbreeze);
        }
    }

    private void MeasureFresh(string scenario, string provider, int round,
        Func<string, SqliteMeasuredOutcome> action)
    {
        string path = Path.Combine(_layout.ScratchDirectory,
            $"r{round:D2}-{Slug(scenario)}-{Slug(provider)}");
        Measure(scenario, provider, round, path, action, cleanupOnSuccess: !_options.KeepDatabases);
    }

    private void MeasureExisting(string scenario, string provider, int round, string path,
        Func<string, SqliteMeasuredOutcome> action) =>
        Measure(scenario, provider, round, path, action, cleanupOnSuccess: false);

    private void Measure(string scenario, string provider, int round, string path,
        Func<string, SqliteMeasuredOutcome> action, bool cleanupOnSuccess)
    {
        Log($"START {scenario} / {provider} / round {round}");
        var measurement = new SqliteComparisonMeasurement
        {
            Scenario = scenario,
            Provider = provider,
            Round = round,
            DatabasePath = path,
        };
        try
        {
            StabilizeGc();
            SqliteMeasuredOutcome outcome = action(path);
            measurement.Operations = outcome.Operations;
            measurement.ReturnedCount = outcome.ReturnedCount;
            measurement.Checksum = outcome.Checksum;
            measurement.ElapsedMilliseconds = outcome.ElapsedMilliseconds;
            measurement.PreparationMilliseconds = outcome.PreparationMilliseconds;
            measurement.MutationMilliseconds = outcome.MutationMilliseconds;
            measurement.OperationsPerSecond = outcome.ElapsedMilliseconds > 0
                ? outcome.Operations * 1000.0 / outcome.ElapsedMilliseconds
                : 0;
            measurement.DatabaseBytes = DirectoryBytes(path);
            measurement.Succeeded = outcome.Operations > 0 && outcome.ElapsedMilliseconds > 0;
            if (!measurement.Succeeded)
                throw new InvalidOperationException("Measurement returned an empty operation count or elapsed time.");
            Log($"PASS  {scenario} / {provider} / round {round}: {measurement.ElapsedMilliseconds:F3} ms, {measurement.OperationsPerSecond:N0} ops/s");
        }
        catch (Exception exception)
        {
            measurement.Succeeded = false;
            measurement.Error = exception.ToString();
            Fail($"{scenario} / {provider} / round {round}: {exception.Message}");
        }
        finally
        {
            _report.Measurements.Add(measurement);
            Persist();
        }

        if (measurement.Succeeded && cleanupOnSuccess && Directory.Exists(path))
            AuditRunLayout.DeleteOwnedChild(path, _layout.ScratchDirectory);
    }

    private SqliteMeasuredOutcome DbreezeInsert(string path, IReadOnlyList<long> keys,
        bool randomKeySorter, int commitBatch)
    {
        CreateEmptyDirectory(path);
        double elapsed;
        using (var engine = new DBreezeEngine(path))
        {
            var stopwatch = Stopwatch.StartNew();
            using (var transaction = engine.GetTransaction())
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    long key = keys[i];
                    if (randomKeySorter)
                    {
                        transaction.RandomKeySorter.Insert(MainTable, key, Payload(key));
                        if ((i + 1) % 100_000 == 0)
                            transaction.RandomKeySorter.Flush(MainTable);
                    }
                    else
                    {
                        transaction.Insert(MainTable, key, Payload(key));
                    }

                    if (commitBatch > 0 && (i + 1) % commitBatch == 0)
                        transaction.Commit();
                }
                if (commitBatch == 0 || keys.Count % commitBatch != 0)
                    transaction.Commit();
            }
            stopwatch.Stop();
            elapsed = stopwatch.Elapsed.TotalMilliseconds;
            VerifyDbreezeMain(engine, keys.Count);
        }
        return new SqliteMeasuredOutcome(keys.Count, keys.Count, ExpectedMainChecksum(keys), elapsed);
    }

    private SqliteMeasuredOutcome SqliteInsert(string path, IReadOnlyList<long> keys, int commitBatch)
    {
        CreateEmptyDirectory(path);
        string file = Path.Combine(path, "database.sqlite");
        double elapsed;
        using (SqliteConnection connection = OpenSqlite(file, create: true))
        {
            ExecuteNonQuery(connection, "CREATE TABLE kv (k INTEGER NOT NULL PRIMARY KEY, v BLOB NOT NULL);");
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "INSERT INTO kv(k,v) VALUES($k,$v);";
            SqliteParameter keyParameter = command.Parameters.Add("$k", SqliteType.Integer);
            SqliteParameter valueParameter = command.Parameters.Add("$v", SqliteType.Blob);
            command.Prepare();

            var stopwatch = Stopwatch.StartNew();
            SqliteTransaction transaction = null;
            try
            {
                transaction = connection.BeginTransaction();
                command.Transaction = transaction;
                for (int i = 0; i < keys.Count; i++)
                {
                    long key = keys[i];
                    keyParameter.Value = key;
                    valueParameter.Value = Payload(key);
                    if (command.ExecuteNonQuery() != 1)
                        throw new InvalidDataException("SQLite insert affected an unexpected row count.");
                    if (commitBatch > 0 && (i + 1) % commitBatch == 0 && i + 1 < keys.Count)
                    {
                        transaction.Commit();
                        transaction.Dispose();
                        transaction = connection.BeginTransaction();
                        command.Transaction = transaction;
                    }
                }
                transaction.Commit();
            }
            finally
            {
                transaction?.Dispose();
            }
            stopwatch.Stop();
            elapsed = stopwatch.Elapsed.TotalMilliseconds;
            VerifySqliteMain(connection, keys.Count);
            Checkpoint(connection);
        }
        return new SqliteMeasuredOutcome(keys.Count, keys.Count, ExpectedMainChecksum(keys), elapsed);
    }

    private SqliteMeasuredOutcome DbreezePointReads(string path, IReadOnlyList<long> keys)
    {
        long returned = 0;
        long checksum = 0;
        double elapsed;
        using (var engine = new DBreezeEngine(path))
        {
            var stopwatch = Stopwatch.StartNew();
            using (var transaction = engine.GetTransaction())
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    long key = keys[i];
                    Row<long, byte[]> row = transaction.Select<long, byte[]>(MainTable, key);
                    if (!row.Exists)
                        continue;
                    byte[] value = row.Value;
                    returned++;
                    checksum = AddChecksum(checksum, key, value);
                }
            }
            stopwatch.Stop();
            elapsed = stopwatch.Elapsed.TotalMilliseconds;
        }
        VerifyPointOutcome(keys, returned, checksum);
        return new SqliteMeasuredOutcome(keys.Count, returned, checksum, elapsed);
    }

    private SqliteMeasuredOutcome SqlitePointReads(string path, IReadOnlyList<long> keys)
    {
        long returned = 0;
        long checksum = 0;
        double elapsed;
        using (SqliteConnection connection = OpenSqlite(Path.Combine(path, "database.sqlite"), create: false))
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT v FROM kv WHERE k=$k;";
            SqliteParameter keyParameter = command.Parameters.Add("$k", SqliteType.Integer);
            command.Prepare();
            var stopwatch = Stopwatch.StartNew();
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                command.Transaction = transaction;
                for (int i = 0; i < keys.Count; i++)
                {
                    long key = keys[i];
                    keyParameter.Value = key;
                    using SqliteDataReader reader = command.ExecuteReader();
                    if (!reader.Read())
                        continue;
                    byte[] value = (byte[])reader.GetValue(0);
                    returned++;
                    checksum = AddChecksum(checksum, key, value);
                }
            }
            stopwatch.Stop();
            elapsed = stopwatch.Elapsed.TotalMilliseconds;
        }
        VerifyPointOutcome(keys, returned, checksum);
        return new SqliteMeasuredOutcome(keys.Count, returned, checksum, elapsed);
    }

    private SqliteMeasuredOutcome DbreezeFullTraversal(string path, bool forward)
    {
        long returned = 0;
        long checksum = 0;
        double elapsed;
        using (var engine = new DBreezeEngine(path))
        {
            var stopwatch = Stopwatch.StartNew();
            using (var transaction = engine.GetTransaction())
            {
                transaction.ValuesLazyLoadingIsOn = false;
                IEnumerable<Row<long, byte[]>> rows = forward
                    ? transaction.SelectForward<long, byte[]>(MainTable)
                    : transaction.SelectBackward<long, byte[]>(MainTable);
                foreach (Row<long, byte[]> row in rows)
                {
                    long expected = forward ? returned : _options.Records - returned - 1L;
                    if (row.Key != expected)
                        throw new InvalidDataException($"DBreeze traversal order mismatch: {row.Key} != {expected}.");
                    checksum = AddChecksum(checksum, row.Key, row.Value);
                    returned++;
                }
            }
            stopwatch.Stop();
            elapsed = stopwatch.Elapsed.TotalMilliseconds;
        }
        VerifyFullTraversal(returned, checksum);
        return new SqliteMeasuredOutcome(_options.Records, returned, checksum, elapsed);
    }

    private SqliteMeasuredOutcome SqliteFullTraversal(string path, bool forward)
    {
        long returned = 0;
        long checksum = 0;
        double elapsed;
        using (SqliteConnection connection = OpenSqlite(Path.Combine(path, "database.sqlite"), false))
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
                    long expected = forward ? returned : _options.Records - returned - 1L;
                    if (key != expected)
                        throw new InvalidDataException($"SQLite traversal order mismatch: {key} != {expected}.");
                    checksum = AddChecksum(checksum, key, (byte[])reader.GetValue(1));
                    returned++;
                }
            }
            stopwatch.Stop();
            elapsed = stopwatch.Elapsed.TotalMilliseconds;
        }
        VerifyFullTraversal(returned, checksum);
        return new SqliteMeasuredOutcome(_options.Records, returned, checksum, elapsed);
    }

    private SqliteMeasuredOutcome DbreezeRanges(string path)
    {
        long returned = 0;
        long checksum = 0;
        double elapsed;
        using (var engine = new DBreezeEngine(path))
        {
            var stopwatch = Stopwatch.StartNew();
            using (var transaction = engine.GetTransaction())
            {
                transaction.ValuesLazyLoadingIsOn = false;
                for (int range = 0; range < _rangeCount; range++)
                {
                    (long start, long stop) = Range(range);
                    long expected = start;
                    foreach (Row<long, byte[]> row in transaction.SelectForwardFromTo<long, byte[]>(MainTable, start, true, stop, true))
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
        VerifyRanges(returned, checksum);
        return new SqliteMeasuredOutcome((long)_rangeCount * _rangeSize, returned, checksum, elapsed);
    }

    private SqliteMeasuredOutcome SqliteRanges(string path)
    {
        long returned = 0;
        long checksum = 0;
        double elapsed;
        using (SqliteConnection connection = OpenSqlite(Path.Combine(path, "database.sqlite"), false))
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
                for (int range = 0; range < _rangeCount; range++)
                {
                    (long start, long stop) = Range(range);
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
        VerifyRanges(returned, checksum);
        return new SqliteMeasuredOutcome((long)_rangeCount * _rangeSize, returned, checksum, elapsed);
    }

    private void BuildDbreezePrefixFixture(string path)
    {
        CreateEmptyDirectory(path);
        using var engine = new DBreezeEngine(path);
        using var transaction = engine.GetTransaction();
        for (int group = 0; group < _prefixGroups; group++)
        {
            (long start, long end) = GroupBounds(group);
            for (long ordinal = start; ordinal < end; ordinal++)
                transaction.Insert(PrefixTable, CompositeKey(group, ordinal - start), Payload(ordinal));
        }
        transaction.Commit();
    }

    private void BuildSqlitePrefixFixture(string path)
    {
        CreateEmptyDirectory(path);
        string file = Path.Combine(path, "database.sqlite");
        using SqliteConnection connection = OpenSqlite(file, true);
        ExecuteNonQuery(connection, "CREATE TABLE prefix (group_id INTEGER NOT NULL, item_id INTEGER NOT NULL, v BLOB NOT NULL, PRIMARY KEY(group_id,item_id)) WITHOUT ROWID;");
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO prefix(group_id,item_id,v) VALUES($g,$i,$v);";
        SqliteParameter groupParameter = command.Parameters.Add("$g", SqliteType.Integer);
        SqliteParameter itemParameter = command.Parameters.Add("$i", SqliteType.Integer);
        SqliteParameter valueParameter = command.Parameters.Add("$v", SqliteType.Blob);
        command.Prepare();
        for (int group = 0; group < _prefixGroups; group++)
        {
            (long start, long end) = GroupBounds(group);
            for (long ordinal = start; ordinal < end; ordinal++)
            {
                groupParameter.Value = group;
                itemParameter.Value = ordinal - start;
                valueParameter.Value = Payload(ordinal);
                command.ExecuteNonQuery();
            }
        }
        transaction.Commit();
        Checkpoint(connection);
    }

    private SqliteMeasuredOutcome DbreezePrefixTraversal(string path)
    {
        long returned = 0;
        long checksum = 0;
        double elapsed;
        using (var engine = new DBreezeEngine(path))
        {
            var stopwatch = Stopwatch.StartNew();
            using (var transaction = engine.GetTransaction())
            {
                transaction.ValuesLazyLoadingIsOn = false;
                for (int group = 0; group < _prefixGroups; group++)
                {
                    long expectedItem = 0;
                    foreach (Row<byte[], byte[]> row in transaction.SelectForwardStartsWith<byte[], byte[]>(PrefixTable, GroupPrefix(group)))
                    {
                        int actualGroup = BinaryPrimitives.ReadInt32BigEndian(row.Key.AsSpan(0, 4));
                        long item = BinaryPrimitives.ReadInt64BigEndian(row.Key.AsSpan(4, 8));
                        if (actualGroup != group || item != expectedItem++)
                            throw new InvalidDataException("DBreeze prefix ordering mismatch.");
                        checksum = AddCompositeChecksum(checksum, group, item, row.Value);
                        returned++;
                    }
                }
            }
            stopwatch.Stop();
            elapsed = stopwatch.Elapsed.TotalMilliseconds;
        }
        VerifyPrefix(returned, checksum);
        return new SqliteMeasuredOutcome(_options.Records, returned, checksum, elapsed);
    }

    private SqliteMeasuredOutcome SqlitePrefixTraversal(string path)
    {
        long returned = 0;
        long checksum = 0;
        double elapsed;
        using (SqliteConnection connection = OpenSqlite(Path.Combine(path, "database.sqlite"), false))
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT item_id,v FROM prefix WHERE group_id=$g ORDER BY item_id;";
            SqliteParameter groupParameter = command.Parameters.Add("$g", SqliteType.Integer);
            command.Prepare();
            var stopwatch = Stopwatch.StartNew();
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                command.Transaction = transaction;
                for (int group = 0; group < _prefixGroups; group++)
                {
                    groupParameter.Value = group;
                    long expectedItem = 0;
                    using SqliteDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        long item = reader.GetInt64(0);
                        if (item != expectedItem++)
                            throw new InvalidDataException("SQLite prefix ordering mismatch.");
                        checksum = AddCompositeChecksum(checksum, group, item, (byte[])reader.GetValue(1));
                        returned++;
                    }
                }
            }
            stopwatch.Stop();
            elapsed = stopwatch.Elapsed.TotalMilliseconds;
        }
        VerifyPrefix(returned, checksum);
        return new SqliteMeasuredOutcome(_options.Records, returned, checksum, elapsed);
    }

    private SqliteMeasuredOutcome DbreezeUpdate(string path, DbreezeUpdateStrategy strategy)
    {
        double elapsed;
        using (var engine = new DBreezeEngine(path))
        {
            var stopwatch = Stopwatch.StartNew();
            using (var transaction = engine.GetTransaction())
            {
                if (strategy == DbreezeUpdateStrategy.RksNoOverwrite)
                    transaction.Technical_SetTable_OverwriteIsNotAllowed(MainTable);

                for (int i = 0; i < _updateKeys.Length; i++)
                {
                    long key = _updateKeys[i];
                    if (strategy != DbreezeUpdateStrategy.Direct)
                    {
                        transaction.RandomKeySorter.Insert(MainTable, key, UpdatedPayload(key));
                        if ((i + 1) % 100_000 == 0)
                            transaction.RandomKeySorter.Flush(MainTable);
                    }
                    else
                    {
                        transaction.Insert(MainTable, key, UpdatedPayload(key));
                    }
                }
                transaction.Commit();
            }
            stopwatch.Stop();
            elapsed = stopwatch.Elapsed.TotalMilliseconds;
            VerifyDbreezeUpdated(engine);
        }
        return new SqliteMeasuredOutcome(_updateKeys.Length, _updateKeys.Length, ExpectedUpdatedChecksum(), elapsed);
    }

    private SqliteMeasuredOutcome SqliteUpdate(string path)
    {
        double elapsed;
        using (SqliteConnection connection = OpenSqlite(Path.Combine(path, "database.sqlite"), false))
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE kv SET v=$v WHERE k=$k;";
            SqliteParameter valueParameter = command.Parameters.Add("$v", SqliteType.Blob);
            SqliteParameter keyParameter = command.Parameters.Add("$k", SqliteType.Integer);
            command.Prepare();
            var stopwatch = Stopwatch.StartNew();
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                command.Transaction = transaction;
                foreach (long key in _updateKeys)
                {
                    keyParameter.Value = key;
                    valueParameter.Value = UpdatedPayload(key);
                    if (command.ExecuteNonQuery() != 1)
                        throw new InvalidDataException("SQLite update did not affect exactly one row.");
                }
                transaction.Commit();
            }
            stopwatch.Stop();
            elapsed = stopwatch.Elapsed.TotalMilliseconds;
            VerifySqliteUpdated(connection);
            Checkpoint(connection);
        }
        return new SqliteMeasuredOutcome(_updateKeys.Length, _updateKeys.Length, ExpectedUpdatedChecksum(), elapsed);
    }

    private SqliteMeasuredOutcome DbreezeDelete(string path, DbreezeDeleteStrategy strategy)
    {
        bool sorted = strategy is DbreezeDeleteStrategy.Sorted or DbreezeDeleteStrategy.SortedNoOverwrite;
        bool randomKeySorter = strategy is DbreezeDeleteStrategy.Rks or DbreezeDeleteStrategy.RksNoOverwrite;
        bool noOverwrite = strategy is DbreezeDeleteStrategy.SortedNoOverwrite or DbreezeDeleteStrategy.RksNoOverwrite;
        long[] keys = sorted ? (long[])_deleteKeys.Clone() : _deleteKeys;
        double elapsed;
        double? preparation = null;
        double? mutation = null;
        using (var engine = new DBreezeEngine(path))
        {
            var totalStopwatch = Stopwatch.StartNew();
            if (sorted)
            {
                var sortStopwatch = Stopwatch.StartNew();
                SortAscending(keys);
                sortStopwatch.Stop();
                preparation = sortStopwatch.Elapsed.TotalMilliseconds;
            }

            var mutationStopwatch = Stopwatch.StartNew();
            using (var transaction = engine.GetTransaction())
            {
                if (noOverwrite)
                    transaction.Technical_SetTable_OverwriteIsNotAllowed(MainTable);
                foreach (long key in keys)
                {
                    if (randomKeySorter)
                        transaction.RandomKeySorter.Remove(MainTable, key);
                    else
                        transaction.RemoveKey<long>(MainTable, key);
                }
                if (randomKeySorter)
                    transaction.RandomKeySorter.Flush(MainTable);
                transaction.Commit();
            }
            mutationStopwatch.Stop();
            totalStopwatch.Stop();
            elapsed = totalStopwatch.Elapsed.TotalMilliseconds;
            if (sorted)
                mutation = mutationStopwatch.Elapsed.TotalMilliseconds;
            VerifyDbreezeDeleted(engine, exhaustive: strategy != DbreezeDeleteStrategy.Direct);
        }
        return new SqliteMeasuredOutcome(_deleteKeys.Length, _deleteKeys.Length,
            ExpectedKeyChecksum(_deleteKeys), elapsed, preparation, mutation);
    }

    private SqliteMeasuredOutcome SqliteDelete(string path)
    {
        double elapsed;
        using (SqliteConnection connection = OpenSqlite(Path.Combine(path, "database.sqlite"), false))
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM kv WHERE k=$k;";
            SqliteParameter keyParameter = command.Parameters.Add("$k", SqliteType.Integer);
            command.Prepare();
            var stopwatch = Stopwatch.StartNew();
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                command.Transaction = transaction;
                foreach (long key in _deleteKeys)
                {
                    keyParameter.Value = key;
                    if (command.ExecuteNonQuery() != 1)
                        throw new InvalidDataException("SQLite delete did not affect exactly one row.");
                }
                transaction.Commit();
            }
            stopwatch.Stop();
            elapsed = stopwatch.Elapsed.TotalMilliseconds;
            VerifySqliteDeleted(connection);
            Checkpoint(connection);
        }
        return new SqliteMeasuredOutcome(_deleteKeys.Length, _deleteKeys.Length, ExpectedKeyChecksum(_deleteKeys), elapsed);
    }

    private SqliteMeasuredOutcome DbreezeParallelReads(string path)
    {
        using var engine = new DBreezeEngine(path);
        using var start = new ManualResetEventSlim(false);
        using var ready = new CountdownEvent(_options.Parallelism);
        var tasks = new Task<(long Count, long Checksum)>[_options.Parallelism];
        for (int worker = 0; worker < tasks.Length; worker++)
        {
            int workerId = worker;
            tasks[worker] = Task.Run(() =>
            {
                ready.Signal();
                start.Wait();
                long count = 0;
                long checksum = 0;
                using var transaction = engine.GetTransaction();
                for (int i = 0; i < _parallelOperationsPerWorker; i++)
                {
                    long key = ParallelKey(workerId, i);
                    Row<long, byte[]> row = transaction.Select<long, byte[]>(MainTable, key);
                    if (!row.Exists)
                        throw new InvalidDataException("DBreeze parallel point read missed an existing key.");
                    checksum = AddChecksum(checksum, key, row.Value);
                    count++;
                }
                return (count, checksum);
            });
        }
        ready.Wait();
        var stopwatch = Stopwatch.StartNew();
        start.Set();
        Task.WaitAll(tasks);
        stopwatch.Stop();
        long returned = tasks.Sum(static task => task.Result.Count);
        long checksum = tasks.Aggregate(0L, static (value, task) => unchecked(value + task.Result.Checksum));
        VerifyParallel(returned, checksum);
        return new SqliteMeasuredOutcome((long)_parallelOperationsPerWorker * _options.Parallelism,
            returned, checksum, stopwatch.Elapsed.TotalMilliseconds);
    }

    private SqliteMeasuredOutcome SqliteParallelReads(string path)
    {
        var connections = new SqliteConnection[_options.Parallelism];
        var commands = new SqliteCommand[_options.Parallelism];
        var parameters = new SqliteParameter[_options.Parallelism];
        try
        {
            string file = Path.Combine(path, "database.sqlite");
            for (int worker = 0; worker < _options.Parallelism; worker++)
            {
                connections[worker] = OpenSqlite(file, false);
                commands[worker] = connections[worker].CreateCommand();
                commands[worker].CommandText = "SELECT v FROM kv WHERE k=$k;";
                parameters[worker] = commands[worker].Parameters.Add("$k", SqliteType.Integer);
                commands[worker].Prepare();
            }

            using var start = new ManualResetEventSlim(false);
            using var ready = new CountdownEvent(_options.Parallelism);
            var tasks = new Task<(long Count, long Checksum)>[_options.Parallelism];
            for (int worker = 0; worker < tasks.Length; worker++)
            {
                int workerId = worker;
                tasks[worker] = Task.Run(() =>
                {
                    ready.Signal();
                    start.Wait();
                    long count = 0;
                    long checksum = 0;
                    using SqliteTransaction transaction = connections[workerId].BeginTransaction();
                    commands[workerId].Transaction = transaction;
                    for (int i = 0; i < _parallelOperationsPerWorker; i++)
                    {
                        long key = ParallelKey(workerId, i);
                        parameters[workerId].Value = key;
                        using SqliteDataReader reader = commands[workerId].ExecuteReader();
                        if (!reader.Read())
                            throw new InvalidDataException("SQLite parallel point read missed an existing key.");
                        checksum = AddChecksum(checksum, key, (byte[])reader.GetValue(0));
                        count++;
                    }
                    return (count, checksum);
                });
            }
            ready.Wait();
            var stopwatch = Stopwatch.StartNew();
            start.Set();
            Task.WaitAll(tasks);
            stopwatch.Stop();
            long returned = tasks.Sum(static task => task.Result.Count);
            long checksum = tasks.Aggregate(0L, static (value, task) => unchecked(value + task.Result.Checksum));
            VerifyParallel(returned, checksum);
            return new SqliteMeasuredOutcome((long)_parallelOperationsPerWorker * _options.Parallelism,
                returned, checksum, stopwatch.Elapsed.TotalMilliseconds);
        }
        finally
        {
            foreach (SqliteCommand command in commands)
                command?.Dispose();
            foreach (SqliteConnection connection in connections)
                connection?.Dispose();
        }
    }

    private void VerifyDbreezeMain(DBreezeEngine engine, int expectedCount)
    {
        using var transaction = engine.GetTransaction();
        if (transaction.Count(MainTable) != (ulong)expectedCount)
            throw new InvalidDataException("DBreeze main-table count mismatch.");
        VerifyDbreezeSample(transaction, 0, updated: false);
        VerifyDbreezeSample(transaction, expectedCount / 2L, updated: false);
        VerifyDbreezeSample(transaction, expectedCount - 1L, updated: false);
    }

    private void VerifySqliteMain(SqliteConnection connection, int expectedCount)
    {
        long actual = Convert.ToInt64(ExecuteScalar(connection, "SELECT COUNT(*) FROM kv;"), CultureInfo.InvariantCulture);
        if (actual != expectedCount)
            throw new InvalidDataException("SQLite main-table count mismatch.");
        VerifySqliteSample(connection, 0, updated: false);
        VerifySqliteSample(connection, expectedCount / 2L, updated: false);
        VerifySqliteSample(connection, expectedCount - 1L, updated: false);
    }

    private void VerifyDbreezeUpdated(DBreezeEngine engine)
    {
        using var transaction = engine.GetTransaction();
        if (transaction.Count(MainTable) != (ulong)_options.Records)
            throw new InvalidDataException("DBreeze update changed table count.");
        foreach (long key in SampleKeys(_updateKeys))
            VerifyDbreezeSample(transaction, key, updated: true);
    }

    private void VerifySqliteUpdated(SqliteConnection connection)
    {
        if (Convert.ToInt64(ExecuteScalar(connection, "SELECT COUNT(*) FROM kv;"), CultureInfo.InvariantCulture) != _options.Records)
            throw new InvalidDataException("SQLite update changed table count.");
        foreach (long key in SampleKeys(_updateKeys))
            VerifySqliteSample(connection, key, updated: true);
    }

    private void VerifyDbreezeDeleted(DBreezeEngine engine, bool exhaustive)
    {
        using var transaction = engine.GetTransaction();
        if (transaction.Count(MainTable) != (ulong)(_options.Records - _deleteKeys.Length))
            throw new InvalidDataException("DBreeze delete final count mismatch.");
        IEnumerable<long> keys = exhaustive ? _deleteKeys : SampleKeys(_deleteKeys);
        foreach (long key in keys)
            if (transaction.Select<long, byte[]>(MainTable, key).Exists)
                throw new InvalidDataException("DBreeze deleted key still exists.");
    }

    private void VerifySqliteDeleted(SqliteConnection connection)
    {
        if (Convert.ToInt64(ExecuteScalar(connection, "SELECT COUNT(*) FROM kv;"), CultureInfo.InvariantCulture) != _options.Records - _deleteKeys.Length)
            throw new InvalidDataException("SQLite delete final count mismatch.");
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM kv WHERE k=$k;";
        SqliteParameter parameter = command.Parameters.Add("$k", SqliteType.Integer);
        foreach (long key in SampleKeys(_deleteKeys))
        {
            parameter.Value = key;
            if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
                throw new InvalidDataException("SQLite deleted key still exists.");
        }
    }

    private void VerifyDbreezeSample(DBreeze.Transactions.Transaction transaction, long key, bool updated)
    {
        Row<long, byte[]> row = transaction.Select<long, byte[]>(MainTable, key);
        byte[] expected = updated ? UpdatedPayload(key) : Payload(key);
        if (!row.Exists || !row.Value.AsSpan().SequenceEqual(expected))
            throw new InvalidDataException($"DBreeze sample mismatch for key {key}.");
    }

    private void VerifySqliteSample(SqliteConnection connection, long key, bool updated)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT v FROM kv WHERE k=$k;";
        command.Parameters.AddWithValue("$k", key);
        object value = command.ExecuteScalar();
        byte[] expected = updated ? UpdatedPayload(key) : Payload(key);
        if (value is not byte[] bytes || !bytes.AsSpan().SequenceEqual(expected))
            throw new InvalidDataException($"SQLite sample mismatch for key {key}.");
    }

    private void VerifyPointOutcome(IReadOnlyList<long> keys, long returned, long checksum)
    {
        long expectedReturned = 0;
        long expectedChecksum = 0;
        foreach (long key in keys)
        {
            if (key < 0 || key >= _options.Records)
                continue;
            expectedReturned++;
            expectedChecksum = AddChecksum(expectedChecksum, key, Payload(key));
        }
        if (returned != expectedReturned || checksum != expectedChecksum)
            throw new InvalidDataException("Point-read oracle mismatch.");
    }

    private void VerifyFullTraversal(long returned, long checksum)
    {
        if (returned != _options.Records || checksum != ExpectedMainChecksum(_sequentialKeys))
            throw new InvalidDataException("Full-traversal oracle mismatch.");
    }

    private void VerifyRanges(long returned, long checksum)
    {
        long expectedCount = 0;
        long expectedChecksum = 0;
        for (int range = 0; range < _rangeCount; range++)
        {
            (long start, long stop) = Range(range);
            for (long key = start; key <= stop; key++)
            {
                expectedCount++;
                expectedChecksum = AddChecksum(expectedChecksum, key, Payload(key));
            }
        }
        if (returned != expectedCount || checksum != expectedChecksum)
            throw new InvalidDataException("Bounded-range oracle mismatch.");
    }

    private void VerifyPrefix(long returned, long checksum)
    {
        long expectedCount = 0;
        long expectedChecksum = 0;
        for (int group = 0; group < _prefixGroups; group++)
        {
            (long start, long end) = GroupBounds(group);
            for (long ordinal = start; ordinal < end; ordinal++)
            {
                long item = ordinal - start;
                expectedCount++;
                expectedChecksum = AddCompositeChecksum(expectedChecksum, group, item, Payload(ordinal));
            }
        }
        if (returned != expectedCount || checksum != expectedChecksum)
            throw new InvalidDataException("Prefix-traversal oracle mismatch.");
    }

    private void VerifyParallel(long returned, long checksum)
    {
        long expectedCount = (long)_parallelOperationsPerWorker * _options.Parallelism;
        long expectedChecksum = 0;
        for (int worker = 0; worker < _options.Parallelism; worker++)
            for (int i = 0; i < _parallelOperationsPerWorker; i++)
            {
                long key = ParallelKey(worker, i);
                expectedChecksum = AddChecksum(expectedChecksum, key, Payload(key));
            }
        if (returned != expectedCount || checksum != expectedChecksum)
            throw new InvalidDataException("Parallel-read oracle mismatch.");
    }

    private void ValidateCompleteness(bool includeNoOverwrite = true,
        bool includeSortedDelete = true, bool includeDeleteFallbacks = false)
    {
        string[] updateProviders = includeNoOverwrite
            ? new[] { DBreezeProvider, DBreezeRksProvider, DBreezeRksNoOverwriteProvider, SqliteProvider }
            : new[] { DBreezeProvider, DBreezeRksProvider, SqliteProvider };
        var deleteProviders = new List<string> { DBreezeProvider };
        if (includeSortedDelete)
            deleteProviders.Add(DBreezeSortedProvider);
        if (includeDeleteFallbacks)
        {
            deleteProviders.Add(DBreezeRksRemoveProvider);
            deleteProviders.Add(DBreezeSortedNoOverwriteProvider);
            deleteProviders.Add(DBreezeRksRemoveNoOverwriteProvider);
        }
        deleteProviders.Add(SqliteProvider);
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Sequential bulk insert"] = new[] { DBreezeProvider, SqliteProvider },
            ["Sequential batched insert (1000/commit)"] = new[] { DBreezeProvider, SqliteProvider },
            ["Random bulk insert"] = new[] { DBreezeProvider, DBreezeRksProvider, SqliteProvider },
            ["Random point reads (hits)"] = new[] { DBreezeProvider, SqliteProvider },
            ["Mixed point reads (90% hits)"] = new[] { DBreezeProvider, SqliteProvider },
            ["Random update"] = updateProviders,
            ["Random delete"] = deleteProviders.ToArray(),
            ["Full forward traversal"] = new[] { DBreezeProvider, SqliteProvider },
            ["Full backward traversal"] = new[] { DBreezeProvider, SqliteProvider },
            ["Bounded ranges"] = new[] { DBreezeProvider, SqliteProvider },
            ["Prefix traversal"] = new[] { DBreezeProvider, SqliteProvider },
            ["Parallel point reads"] = new[] { DBreezeProvider, SqliteProvider },
        };

        foreach ((string scenario, string[] providers) in expected)
        foreach (string provider in providers)
        {
            SqliteComparisonMeasurement[] values = _report.Measurements
                .Where(value => value.Scenario == scenario && value.Provider == provider && value.Succeeded)
                .ToArray();
            if (values.Length != _options.Repetitions)
                Fail($"Missing measurement pair: {scenario} / {provider}; expected {_options.Repetitions}, got {values.Length}.");
        }

        foreach (IGrouping<string, SqliteComparisonMeasurement> scenario in _report.Measurements.Where(static value => value.Succeeded).GroupBy(static value => value.Scenario))
        {
            long[] counts = scenario.Select(static value => value.ReturnedCount).Distinct().ToArray();
            long[] checksums = scenario.Select(static value => value.Checksum).Distinct().ToArray();
            if (counts.Length != 1 || checksums.Length != 1)
                Fail($"Cross-provider oracle differs for {scenario}.");
        }


        foreach (SqliteComparisonMeasurement value in _report.Measurements.Where(static value =>
            value.Succeeded && value.Scenario == "Random delete" &&
            (value.Provider == DBreezeSortedProvider || value.Provider == DBreezeSortedNoOverwriteProvider)))
        {
            if (!value.PreparationMilliseconds.HasValue || !value.MutationMilliseconds.HasValue ||
                value.PreparationMilliseconds < 0 || value.MutationMilliseconds <= 0 ||
                value.PreparationMilliseconds.Value + value.MutationMilliseconds.Value > value.ElapsedMilliseconds + 1.0)
            {
                Fail($"Invalid split timings: {value.Scenario} / {value.Provider} / round {value.Round}.");
            }
        }
    }

    private SqliteConnection OpenSqlite(string file, bool create)
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
            string journal = Convert.ToString(ExecuteScalar(connection, "PRAGMA journal_mode=WAL;"), CultureInfo.InvariantCulture);
            if (!String.Equals(journal, "wal", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SQLite refused WAL journal mode: " + journal);
        }
        ExecuteNonQuery(connection, "PRAGMA synchronous=" + _options.SqliteSynchronous + ";");
        int synchronous = Convert.ToInt32(ExecuteScalar(connection, "PRAGMA synchronous;"), CultureInfo.InvariantCulture);
        int expected = _options.SqliteSynchronous == "FULL" ? 2 : 1;
        if (synchronous != expected)
            throw new InvalidOperationException($"SQLite synchronous mismatch: {synchronous} != {expected}.");
        return connection;
    }

    private static void Checkpoint(SqliteConnection connection)
    {
        ExecuteNonQuery(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
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

    private byte[] Payload(long key) => _payloads[(int)(key & 1023)];
    private byte[] UpdatedPayload(long key) => _updatedPayloads[(int)(key & 1023)];

    private static byte[][] CreatePayloadPool(int payloadBytes, bool updated)
    {
        var result = new byte[1024][];
        for (int index = 0; index < result.Length; index++)
        {
            var value = new byte[payloadBytes];
            uint state = unchecked((uint)(20260826 + index * 2654435761u + (updated ? 0xA5A5A5A5u : 0u)));
            for (int offset = 0; offset < value.Length; offset++)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                value[offset] = (byte)(state >> 24);
            }
            result[index] = value;
        }
        return result;
    }

    private static long AddChecksum(long checksum, long key, byte[] value)
    {
        int middle = value.Length / 2;
        long mixed = unchecked(key * 6364136223846793005L + value.Length * 1442695040888963407L);
        mixed ^= value[0];
        mixed = unchecked(mixed * 1099511628211L) ^ value[middle];
        mixed = unchecked(mixed * 1099511628211L) ^ value[value.Length - 1];
        return unchecked(checksum + mixed);
    }

    private static long AddCompositeChecksum(long checksum, int group, long item, byte[] value) =>
        AddChecksum(checksum, unchecked(((long)group << 32) ^ item), value);

    private long ExpectedMainChecksum(IEnumerable<long> keys)
    {
        long checksum = 0;
        foreach (long key in keys)
            checksum = AddChecksum(checksum, key, Payload(key));
        return checksum;
    }

    private long ExpectedUpdatedChecksum()
    {
        long checksum = 0;
        foreach (long key in _updateKeys)
            checksum = AddChecksum(checksum, key, UpdatedPayload(key));
        return checksum;
    }

    private static long ExpectedKeyChecksum(IEnumerable<long> keys)
    {
        long checksum = 0;
        foreach (long key in keys)
            checksum = unchecked(checksum + key * 397);
        return checksum;
    }

    private (long Start, long Stop) Range(int range)
    {
        long maximumStart = _options.Records - _rangeSize;
        long start = _rangeCount == 1 ? 0 : maximumStart * range / (_rangeCount - 1L);
        return (start, start + _rangeSize - 1L);
    }

    private (long Start, long End) GroupBounds(int group) =>
        ((long)_options.Records * group / _prefixGroups,
         (long)_options.Records * (group + 1) / _prefixGroups);

    private long ParallelKey(int worker, int operation) =>
        _randomKeys[(int)(((long)worker * _parallelOperationsPerWorker + operation) % _randomKeys.Length)];

    private static byte[] GroupPrefix(int group)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, group);
        return bytes;
    }

    private static byte[] CompositeKey(int group, long item)
    {
        var bytes = new byte[12];
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0, 4), group);
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(4, 8), item);
        return bytes;
    }

    private static IEnumerable<long> SampleKeys(IReadOnlyList<long> keys)
    {
        if (keys.Count == 0)
            yield break;
        yield return keys[0];
        if (keys.Count > 2)
            yield return keys[keys.Count / 2];
        if (keys.Count > 1)
            yield return keys[keys.Count - 1];
    }

    internal static void SortAscending(long[] keys)
    {
        if (keys == null)
            throw new ArgumentNullException(nameof(keys));
        Array.Sort(keys);
    }

    private static void CreateEmptyDirectory(string path)
    {
        if (Directory.Exists(path))
            throw new IOException("Benchmark database path already exists: " + path);
        Directory.CreateDirectory(path);
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException(source);
        CreateEmptyDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            File.Copy(file, target, overwrite: false);
        }
    }

    private static long DirectoryBytes(string path) => Directory.Exists(path)
        ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(static file => new FileInfo(file).Length)
        : 0;

    private static void StabilizeGc()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private void Persist()
    {
        try
        {
            SqliteComparisonArtifacts.Write(_report);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Failed to persist SQLite comparison report: " + exception.Message);
        }
    }

    private void Log(string message)
    {
        string line = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + " " + message;
        Console.WriteLine(line);
        Directory.CreateDirectory(Path.GetDirectoryName(_report.Metadata.LogPath)!);
        File.AppendAllText(_report.Metadata.LogPath, line + Environment.NewLine, new UTF8Encoding(false));
    }

    private void Fail(string message)
    {
        if (!_report.Failures.Contains(message, StringComparer.Ordinal))
            _report.Failures.Add(message);
        Log("FAIL " + message);
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char character in value.ToLowerInvariant())
        {
            if (Char.IsLetterOrDigit(character))
                builder.Append(character);
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }
        return builder.ToString().Trim('-');
    }

    private static string Sha256File(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string Sha256Text(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? String.Empty)));

    private static string FindRepositoryRoot()
    {
        foreach (string candidate in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(candidate);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }
        return String.Empty;
    }

    private static string RunProcess(string fileName, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);
            return process.ExitCode == 0 ? output : String.Empty;
        }
        catch
        {
            return String.Empty;
        }
    }
}
