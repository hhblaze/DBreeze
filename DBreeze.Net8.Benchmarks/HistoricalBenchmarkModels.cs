using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using DBreeze;

namespace DBreeze.Net8.Benchmarks;

internal sealed class HistoricalBenchmarkOptions
{
    internal string RootPath { get; private set; } = @"D:\Temp\DbreezeDbTest";
    internal string RunId { get; private set; }
    internal int Repetitions { get; private set; } = 3;
    internal bool Smoke { get; private set; }
    internal bool SkipDurableCommits { get; private set; }
    internal bool SkipOnly { get; private set; }
    internal bool ScanOnly { get; private set; }
    internal bool RandomOnly { get; private set; }
    internal int? RandomRecordCount { get; private set; }

    internal static HistoricalBenchmarkOptions Parse(string[] args)
    {
        var options = new HistoricalBenchmarkOptions();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--historical-core":
                    break;
                case "--historical-skip":
                    options.SkipOnly = true;
                    break;
                case "--historical-scan":
                    options.ScanOnly = true;
                    break;
                case "--historical-random":
                    options.RandomOnly = true;
                    break;
                case "--smoke":
                    options.Smoke = true;
                    break;
                case "--skip-durable-commits":
                    options.SkipDurableCommits = true;
                    break;
                case "--root":
                    options.RootPath = ReadValue(args, ref i, "--root");
                    break;
                case "--run-id":
                    options.RunId = ReadValue(args, ref i, "--run-id");
                    break;
                case "--repetitions":
                    string value = ReadValue(args, ref i, "--repetitions");
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int repetitions)
                        || repetitions < 1 || repetitions > 20)
                    {
                        throw new ArgumentOutOfRangeException(nameof(args),
                            "--repetitions must be an integer between 1 and 20.");
                    }

                    options.Repetitions = repetitions;
                    break;
                case "--random-records":
                    string recordCount = ReadValue(args, ref i, "--random-records");
                    if (!int.TryParse(recordCount, NumberStyles.None, CultureInfo.InvariantCulture,
                            out int randomRecords) || randomRecords < 1 || randomRecords > 10_000_000)
                    {
                        throw new ArgumentOutOfRangeException(nameof(args),
                            "--random-records must be an integer between 1 and 10000000.");
                    }
                    options.RandomRecordCount = randomRecords;
                    break;
                default:
                    throw new ArgumentException($"Unknown historical benchmark option: {args[i]}", nameof(args));
            }
        }

        options.RootPath = Path.GetFullPath(options.RootPath);
        int selectedModes = (options.SkipOnly ? 1 : 0) + (options.ScanOnly ? 1 : 0) +
            (options.RandomOnly ? 1 : 0);
        if (selectedModes > 1)
            throw new ArgumentException("Only one historical focused mode can be selected.", nameof(args));
        if (options.RandomRecordCount.HasValue && !options.RandomOnly)
            throw new ArgumentException("--random-records is valid only with --historical-random.", nameof(args));
        options.RunId ??= DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + (options.RandomOnly
                ? options.Smoke ? "-net8-random-smoke" : "-net8-random"
                : options.SkipOnly
                    ? options.Smoke ? "-net8-skip-smoke" : "-net8-skip"
                    : options.ScanOnly
                        ? options.Smoke ? "-net8-scan-smoke" : "-net8-scan"
                    : options.Smoke ? "-net8-smoke" : "-net8");
        ValidateRunId(options.RunId);
        return options;
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
            throw new ArgumentException($"{option} requires a value.", nameof(args));

        return args[index];
    }

    private static void ValidateRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId)
            || runId is "." or ".."
            || runId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || runId.Contains(Path.DirectorySeparatorChar)
            || runId.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("--run-id must be a single valid directory name.", nameof(runId));
        }
    }
}

internal sealed class HistoricalBenchmarkMetadata
{
    public string RunId { get; set; }
    public string RunDirectory { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public bool Smoke { get; set; }
    public int Repetitions { get; set; }
    public string ScenarioSet { get; set; }
    public string Framework { get; set; }
    public string RuntimeVersion { get; set; }
    public string OS { get; set; }
    public string Architecture { get; set; }
    public int ProcessorCount { get; set; }
    public string ProcessorIdentifier { get; set; }
    public bool ServerGc { get; set; }
    public string GcLatencyMode { get; set; }
    public string DBreezeAssemblyVersion { get; set; }
    public string BenchmarkAssemblyVersion { get; set; }
    public long InitialDriveFreeBytes { get; set; }
    public string CachePolicy { get; set; }
    public string Failure { get; set; }

    internal static HistoricalBenchmarkMetadata Create(
        HistoricalBenchmarkOptions options,
        string runDirectory)
    {
        string driveRoot = Path.GetPathRoot(runDirectory);
        long freeBytes = 0;
        if (!string.IsNullOrEmpty(driveRoot))
        {
            var drive = new DriveInfo(driveRoot);
            if (drive.IsReady)
                freeBytes = drive.AvailableFreeSpace;
        }

        return new HistoricalBenchmarkMetadata
        {
            RunId = options.RunId,
            RunDirectory = runDirectory,
            StartedUtc = DateTime.UtcNow,
            Smoke = options.Smoke,
            Repetitions = options.Repetitions,
            ScenarioSet = options.RandomOnly ? "random" : options.SkipOnly ? "skip" :
                options.ScanOnly ? "scan" : "core",
            Framework = RuntimeInformation.FrameworkDescription,
            RuntimeVersion = Environment.Version.ToString(),
            OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            ProcessorIdentifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? string.Empty,
            ServerGc = GCSettings.IsServerGC,
            GcLatencyMode = GCSettings.LatencyMode.ToString(),
            DBreezeAssemblyVersion = typeof(DBreezeEngine).Assembly.GetName().Version?.ToString() ?? string.Empty,
            BenchmarkAssemblyVersion = typeof(HistoricalBenchmarkMetadata).Assembly.GetName().Version?.ToString() ?? string.Empty,
            InitialDriveFreeBytes = freeBytes,
            CachePolicy = "Warm process/JIT and OS cache; no operating-system cache flush is performed.",
        };
    }
}

internal sealed class HistoricalBenchmarkMeasurement
{
    public string Category { get; set; }
    public string Scenario { get; set; }
    public string Phase { get; set; }
    public bool IsWarmup { get; set; }
    public int Iteration { get; set; }
    public DateTime StartedUtc { get; set; }
    public long Operations { get; set; }
    public long ReturnedCount { get; set; }
    public long Checksum { get; set; }
    public double ElapsedMilliseconds { get; set; }
    public double OperationsPerSecond { get; set; }
    public long AllocatedBytes { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
    public long DatabaseBytes { get; set; }
    public string DatabasePath { get; set; }
    public bool Succeeded { get; set; }
    public string Error { get; set; }
}

internal sealed class HistoricalBenchmarkReport
{
    public HistoricalBenchmarkMetadata Metadata { get; set; }
    public List<HistoricalBenchmarkMeasurement> Measurements { get; set; } = new();
}

internal readonly record struct HistoricalOperationOutcome(long Count, long Checksum);

internal sealed class PreparedHistoricalOperation : IDisposable
{
    private Action _dispose;

    internal PreparedHistoricalOperation(
        Func<HistoricalOperationOutcome> execute,
        Action<HistoricalOperationOutcome> verify,
        Action dispose)
    {
        Execute = execute ?? throw new ArgumentNullException(nameof(execute));
        Verify = verify ?? throw new ArgumentNullException(nameof(verify));
        _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
    }

    internal Func<HistoricalOperationOutcome> Execute { get; }
    internal Action<HistoricalOperationOutcome> Verify { get; }

    public void Dispose()
    {
        Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
