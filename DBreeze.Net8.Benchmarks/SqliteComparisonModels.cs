using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;

namespace DBreeze.Net8.Benchmarks;

internal sealed class SqliteComparisonOptions
{
    internal const int MaximumRecords = 1_000_000;

    internal string RootPath { get; private set; } = @"D:\Temp\DbreezeDbTest";
    internal string ReportPath { get; private set; }
    internal string RunId { get; private set; }
    internal int Records { get; private set; } = MaximumRecords;
    internal int PayloadBytes { get; private set; } = 256;
    internal int Repetitions { get; private set; } = 3;
    internal int Parallelism { get; private set; } = 4;
    internal string SqliteSynchronous { get; private set; } = "FULL";
    internal bool Smoke { get; private set; }
    internal bool KeepDatabases { get; private set; }

    internal static SqliteComparisonOptions Parse(string[] args)
    {
        var options = new SqliteComparisonOptions();
        bool reportSupplied = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].ToLowerInvariant();
            switch (arg)
            {
                case "--sqlite-compare":
                    break;
                case "--smoke":
                    options.Smoke = true;
                    break;
                case "--keep-databases":
                    options.KeepDatabases = true;
                    break;
                case "--root":
                    options.RootPath = ReadValue(args, ref i, arg);
                    break;
                case "--report":
                    options.ReportPath = ReadValue(args, ref i, arg);
                    reportSupplied = true;
                    break;
                case "--run-id":
                    options.RunId = ReadValue(args, ref i, arg);
                    break;
                case "--records":
                    options.Records = ReadInt(args, ref i, arg, 1_000, MaximumRecords);
                    break;
                case "--payload-bytes":
                    options.PayloadBytes = ReadInt(args, ref i, arg, 1, 65_536);
                    break;
                case "--repetitions":
                    options.Repetitions = ReadInt(args, ref i, arg, 1, 10);
                    break;
                case "--parallelism":
                    options.Parallelism = ReadInt(args, ref i, arg, 1, 64);
                    break;
                case "--sqlite-synchronous":
                    options.SqliteSynchronous = ReadValue(args, ref i, arg).ToUpperInvariant();
                    if (options.SqliteSynchronous is not ("FULL" or "NORMAL"))
                        throw new ArgumentException("--sqlite-synchronous must be FULL or NORMAL.", nameof(args));
                    break;
                default:
                    throw new ArgumentException($"Unknown SQLite comparison option: {args[i]}", nameof(args));
            }
        }

        options.RootPath = Path.GetFullPath(options.RootPath);
        if (options.Smoke)
        {
            options.Records = Math.Min(options.Records, 10_000);
            options.Repetitions = 1;
        }

        options.RunId ??= DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + (options.Smoke ? "-sqlite-smoke" : "-sqlite-full");
        AuditRunLayout.ValidateLeafName(options.RunId, "--run-id");

        if (!reportSupplied)
            options.ReportPath = Path.Combine(options.RootPath, "DBreeze_vs_SQLite.html");
        options.ReportPath = Path.GetFullPath(options.ReportPath);
        AuditRunLayout.EnsureUnderRoot(options.ReportPath, options.RootPath);
        return options;
    }

    internal static SqliteComparisonOptions CreateForAugment(
        SqliteComparisonAugmentOptions augment,
        SqliteComparisonConfiguration source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (source.Records < 1_000 || source.Records > MaximumRecords)
            throw new InvalidDataException("Source report record count is outside supported limits.");
        if (source.PayloadBytes < 1 || source.PayloadBytes > 65_536)
            throw new InvalidDataException("Source report payload size is outside supported limits.");
        if (source.Repetitions < 1 || source.Repetitions > 10)
            throw new InvalidDataException("Source report repetition count is outside supported limits.");
        if (source.Parallelism < 1 || source.Parallelism > 64)
            throw new InvalidDataException("Source report parallelism is outside supported limits.");
        if (source.SqliteSynchronous is not ("FULL" or "NORMAL"))
            throw new InvalidDataException("Source report has unsupported SQLite synchronous mode.");

        return new SqliteComparisonOptions
        {
            RootPath = augment.RootPath,
            ReportPath = augment.ReportPath,
            RunId = augment.RunId,
            Records = source.Records,
            PayloadBytes = source.PayloadBytes,
            Repetitions = source.Repetitions,
            Parallelism = source.Parallelism,
            SqliteSynchronous = source.SqliteSynchronous,
            Smoke = source.Smoke,
            KeepDatabases = augment.KeepDatabases,
        };
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || String.IsNullOrWhiteSpace(args[index]))
            throw new ArgumentException($"{option} requires a value.", nameof(args));
        return args[index];
    }

    private static int ReadInt(string[] args, ref int index, string option, int minimum, int maximum)
    {
        string value = ReadValue(args, ref index, option);
        if (!Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result) ||
            result < minimum || result > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(args),
                $"{option} must be between {minimum} and {maximum}.");
        }
        return result;
    }
}

internal enum SqliteComparisonAugmentKind
{
    RksUpdate,
    RksNoOverwriteUpdate,
    SortedDelete,
    DeleteFallbacks,
}

internal sealed class SqliteComparisonAugmentOptions
{
    internal string RootPath { get; private set; } = @"D:\Temp\DbreezeDbTest";
    internal string ReportPath { get; private set; }
    internal string SourceReportPath { get; private set; }
    internal string RunId { get; private set; }
    internal bool KeepDatabases { get; private set; }
    internal SqliteComparisonAugmentKind Kind { get; private set; }

    internal SqliteComparisonAugmentOptions CreateDeleteFallback(string sourceReportPath) => new()
    {
        RootPath = RootPath,
        ReportPath = ReportPath,
        SourceReportPath = Path.GetFullPath(sourceReportPath),
        RunId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-sqlite-delete-fallbacks",
        KeepDatabases = KeepDatabases,
        Kind = SqliteComparisonAugmentKind.DeleteFallbacks,
    };

    internal static SqliteComparisonAugmentOptions Parse(string[] args)
    {
        var options = new SqliteComparisonAugmentOptions();
        bool kindSupplied = false;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].ToLowerInvariant();
            switch (arg)
            {
                case "--sqlite-compare-augment-rks-update":
                    SetKind(options, SqliteComparisonAugmentKind.RksUpdate, ref kindSupplied);
                    break;
                case "--sqlite-compare-augment-rks-no-overwrite-update":
                    SetKind(options, SqliteComparisonAugmentKind.RksNoOverwriteUpdate, ref kindSupplied);
                    break;
                case "--sqlite-compare-augment-sorted-delete":
                    SetKind(options, SqliteComparisonAugmentKind.SortedDelete, ref kindSupplied);
                    break;
                case "--sqlite-compare-augment-delete-fallbacks":
                    SetKind(options, SqliteComparisonAugmentKind.DeleteFallbacks, ref kindSupplied);
                    break;
                case "--keep-databases":
                    options.KeepDatabases = true;
                    break;
                case "--root":
                    options.RootPath = ReadValue(args, ref i, arg);
                    break;
                case "--report":
                    options.ReportPath = ReadValue(args, ref i, arg);
                    break;
                case "--source-report":
                    options.SourceReportPath = ReadValue(args, ref i, arg);
                    break;
                case "--run-id":
                    options.RunId = ReadValue(args, ref i, arg);
                    break;
                default:
                    throw new ArgumentException($"Unknown SQLite comparison augmentation option: {args[i]}", nameof(args));
            }
        }

        options.RootPath = Path.GetFullPath(options.RootPath);
        if (!kindSupplied)
            throw new ArgumentException("An SQLite comparison augmentation mode is required.", nameof(args));
        if (String.IsNullOrWhiteSpace(options.SourceReportPath))
            throw new ArgumentException("--source-report is required.", nameof(args));
        options.SourceReportPath = Path.GetFullPath(options.SourceReportPath);
        AuditRunLayout.EnsureUnderRoot(options.SourceReportPath, options.RootPath);
        if (!File.Exists(options.SourceReportPath))
            throw new FileNotFoundException("Source SQLite comparison report was not found.", options.SourceReportPath);

        options.ReportPath = Path.GetFullPath(options.ReportPath ??
            Path.Combine(options.RootPath, "DBreeze_vs_SQLite.html"));
        AuditRunLayout.EnsureUnderRoot(options.ReportPath, options.RootPath);
        options.RunId ??= DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + options.Kind switch
            {
                SqliteComparisonAugmentKind.RksUpdate => "-sqlite-rks-update",
                SqliteComparisonAugmentKind.RksNoOverwriteUpdate => "-sqlite-rks-no-overwrite-update",
                SqliteComparisonAugmentKind.SortedDelete => "-sqlite-sorted-delete",
                SqliteComparisonAugmentKind.DeleteFallbacks => "-sqlite-delete-fallbacks",
                _ => throw new ArgumentOutOfRangeException(nameof(options.Kind)),
            };
        AuditRunLayout.ValidateLeafName(options.RunId, "--run-id");
        return options;
    }

    private static void SetKind(SqliteComparisonAugmentOptions options,
        SqliteComparisonAugmentKind kind, ref bool supplied)
    {
        if (supplied)
            throw new ArgumentException("Specify exactly one SQLite comparison augmentation mode.");
        options.Kind = kind;
        supplied = true;
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || String.IsNullOrWhiteSpace(args[index]))
            throw new ArgumentException($"{option} requires a value.", nameof(args));
        return args[index];
    }
}

internal sealed class SqliteComparisonReport
{
    public SqliteComparisonMetadata Metadata { get; set; } = new();
    public SqliteComparisonConfiguration Configuration { get; set; } = new();
    public List<SqliteComparisonMeasurement> Measurements { get; set; } = new();
    public List<SqliteComparisonSummary> Summaries { get; set; } = new();
    public List<string> Failures { get; set; } = new();
    public List<string> Findings { get; set; } = new();
    public bool Succeeded { get; set; }
}

internal sealed class SqliteComparisonMetadata
{
    public string RunId { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string Runtime { get; set; }
    public string OS { get; set; }
    public string Architecture { get; set; }
    public int LogicalProcessors { get; set; }
    public string ProcessorIdentifier { get; set; }
    public bool ServerGc { get; set; }
    public string GcLatencyMode { get; set; }
    public string GitHead { get; set; }
    public string GitStatusSha256 { get; set; }
    public bool GitDirty { get; set; }
    public string DBreezeVersion { get; set; }
    public string DBreezeAssembly { get; set; }
    public string DBreezeSha256 { get; set; }
    public string MicrosoftDataSqliteVersion { get; set; }
    public string NativeSqliteVersion { get; set; }
    public string CanonicalHtml { get; set; }
    public string ImmutableHtml { get; set; }
    public string ReportsDirectory { get; set; }
    public string ScratchDirectory { get; set; }
    public string RawJson { get; set; }
    public string RawCsv { get; set; }
    public string LogPath { get; set; }
    public string AugmentedFromRunId { get; set; }
    public string AugmentedFromJson { get; set; }
    public int ImportedMeasurementCount { get; set; }

    internal static SqliteComparisonMetadata Create(SqliteComparisonOptions options, AuditRunLayout layout)
    {
        return new SqliteComparisonMetadata
        {
            RunId = options.RunId,
            StartedUtc = DateTime.UtcNow,
            Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            LogicalProcessors = Environment.ProcessorCount,
            ProcessorIdentifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? String.Empty,
            ServerGc = GCSettings.IsServerGC,
            GcLatencyMode = GCSettings.LatencyMode.ToString(),
            CanonicalHtml = options.ReportPath,
            ImmutableHtml = Path.Combine(layout.ReportsDirectory, $"DBreeze_vs_SQLite_{options.RunId}.html"),
            ReportsDirectory = layout.ReportsDirectory,
            ScratchDirectory = layout.ScratchDirectory,
            RawJson = Path.Combine(layout.ReportsDirectory, "DBreeze_vs_SQLite.json"),
            RawCsv = Path.Combine(layout.ReportsDirectory, "DBreeze_vs_SQLite.csv"),
            LogPath = Path.Combine(layout.ReportsDirectory, "DBreeze_vs_SQLite.log"),
        };
    }
}

internal sealed class SqliteComparisonConfiguration
{
    public int Records { get; set; }
    public int PayloadBytes { get; set; }
    public int PayloadPoolSize { get; set; } = 1024;
    public int Repetitions { get; set; }
    public int Parallelism { get; set; }
    public int RandomSeed { get; set; } = 20260826;
    public bool Smoke { get; set; }
    public bool KeepDatabases { get; set; }
    public string SqliteJournalMode { get; set; } = "WAL";
    public string SqliteSynchronous { get; set; }
    public int SqliteBusyTimeoutMilliseconds { get; set; } = 5000;
    public string MainSchema { get; set; } = "DBreeze long key / SQLite INTEGER PRIMARY KEY; BLOB value";
    public string PrefixSchema { get; set; } = "DBreeze 12-byte big-endian composite key / SQLite (group_id,item_id) WITHOUT ROWID";
    public string TimingPolicy { get; set; } = "Open/JIT/fixture generation excluded; transaction, operations, value materialization and commit included.";
    public string CachePolicy { get; set; } = "Warm process/JIT and operating-system cache; OS cache is not flushed.";
}

internal sealed class SqliteComparisonMeasurement
{
    public string Scenario { get; set; }
    public string Provider { get; set; }
    public int Round { get; set; }
    public long Operations { get; set; }
    public long ReturnedCount { get; set; }
    public long Checksum { get; set; }
    public double ElapsedMilliseconds { get; set; }
    public double? PreparationMilliseconds { get; set; }
    public double? MutationMilliseconds { get; set; }
    public double OperationsPerSecond { get; set; }
    public long DatabaseBytes { get; set; }
    public string DatabasePath { get; set; }
    public bool Succeeded { get; set; }
    public string Error { get; set; }
}

internal sealed class SqliteComparisonSummary
{
    public string Scenario { get; set; }
    public string Provider { get; set; }
    public int Rounds { get; set; }
    public long Operations { get; set; }
    public double MedianMilliseconds { get; set; }
    public double? MedianPreparationMilliseconds { get; set; }
    public double? MedianMutationMilliseconds { get; set; }
    public double MinimumMilliseconds { get; set; }
    public double MaximumMilliseconds { get; set; }
    public double MedianOperationsPerSecond { get; set; }
    public long MedianDatabaseBytes { get; set; }
    public double RatioVsSqlite { get; set; }
    public string Comparison { get; set; }
}

internal readonly record struct SqliteMeasuredOutcome(
    long Operations,
    long ReturnedCount,
    long Checksum,
    double ElapsedMilliseconds,
    double? PreparationMilliseconds = null,
    double? MutationMilliseconds = null);
