using System.Globalization;

namespace DBreeze.Net8.Benchmarks;

internal sealed class AuditComparisonOptions
{
    internal const string DefaultBaselineCommit = "a83424e2fa742ec05a8e4a359562d3f3a5e008c8";
    internal const int AbsoluteRecordLimit = 1_000_000;

    internal string CurrentRepository { get; private set; }
    internal string BaselineRepository { get; private set; } = @"D:\VS\DBreezeRealm_copy\DBreeze";
    internal string ExpectedBaselineCommit { get; private set; } = DefaultBaselineCommit;
    internal string RootPath { get; private set; } = @"D:\Temp\DbreezeDbTest";
    internal string ReportPath { get; private set; }
    internal string RunId { get; private set; }
    internal AuditProfile Profile { get; private set; } = AuditProfile.Full;
    internal int MaxRecords { get; private set; } = AbsoluteRecordLimit;
    internal bool KeepDatabases { get; private set; }

    internal static AuditComparisonOptions Parse(string[] args)
    {
        var options = new AuditComparisonOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--compare-all":
                    break;
                case "--profile":
                    string profile = ReadValue(args, ref i, "--profile");
                    options.Profile = profile.Equals("smoke", StringComparison.OrdinalIgnoreCase)
                        ? AuditProfile.Smoke
                        : profile.Equals("full", StringComparison.OrdinalIgnoreCase)
                            ? AuditProfile.Full
                            : throw new ArgumentException("--profile must be smoke or full.", nameof(args));
                    break;
                case "--baseline-repo":
                    options.BaselineRepository = Path.GetFullPath(ReadValue(args, ref i, "--baseline-repo"));
                    break;
                case "--current-repo":
                    options.CurrentRepository = Path.GetFullPath(ReadValue(args, ref i, "--current-repo"));
                    break;
                case "--expected-baseline":
                    options.ExpectedBaselineCommit = ReadValue(args, ref i, "--expected-baseline");
                    break;
                case "--root":
                    options.RootPath = Path.GetFullPath(ReadValue(args, ref i, "--root"));
                    break;
                case "--max-records":
                    string maxRecords = ReadValue(args, ref i, "--max-records");
                    if (!Int32.TryParse(maxRecords, NumberStyles.None, CultureInfo.InvariantCulture,
                            out int parsed) || parsed < 1 || parsed > AbsoluteRecordLimit)
                    {
                        throw new ArgumentOutOfRangeException(nameof(args),
                            $"--max-records must be between 1 and {AbsoluteRecordLimit}.");
                    }
                    options.MaxRecords = parsed;
                    break;
                case "--report":
                    options.ReportPath = Path.GetFullPath(ReadValue(args, ref i, "--report"));
                    break;
                case "--run-id":
                    options.RunId = ReadValue(args, ref i, "--run-id");
                    break;
                case "--keep-databases":
                    options.KeepDatabases = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown comparison option: {args[i]}", nameof(args));
            }
        }

        options.CurrentRepository ??= AuditRepositoryLocator.FindCurrentRepository();
        options.ReportPath ??= Path.Combine(options.CurrentRepository, "Documentation", "Audit",
            "DBreeze_Net8_Refactoring_Benchmark.html");
        options.RunId ??= DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + "-a83424e-vs-current";
        AuditRunLayout.ValidateLeafName(options.RunId, "--run-id");
        return options;
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || String.IsNullOrWhiteSpace(args[index]))
            throw new ArgumentException($"{option} requires a value.", nameof(args));
        return args[index];
    }
}

internal sealed class AuditWorkerOptions
{
    internal string Action { get; private set; }
    internal string Variant { get; private set; }
    internal string OutputPath { get; private set; }
    internal string RootPath { get; private set; }
    internal AuditProfile Profile { get; private set; }
    internal int MaxRecords { get; private set; }
    internal int Round { get; private set; }
    internal HashSet<string> ScenarioFilter { get; private set; }

    internal static AuditWorkerOptions Parse(string[] args)
    {
        var options = new AuditWorkerOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--audit-worker":
                    options.Action = ReadValue(args, ref i, "--audit-worker").ToLowerInvariant();
                    break;
                case "--variant":
                    options.Variant = ReadValue(args, ref i, "--variant").ToLowerInvariant();
                    break;
                case "--output":
                    options.OutputPath = Path.GetFullPath(ReadValue(args, ref i, "--output"));
                    break;
                case "--root":
                    options.RootPath = Path.GetFullPath(ReadValue(args, ref i, "--root"));
                    break;
                case "--profile":
                    string profile = ReadValue(args, ref i, "--profile");
                    options.Profile = profile.Equals("full", StringComparison.OrdinalIgnoreCase)
                        ? AuditProfile.Full : AuditProfile.Smoke;
                    break;
                case "--max-records":
                    options.MaxRecords = Int32.Parse(ReadValue(args, ref i, "--max-records"),
                        CultureInfo.InvariantCulture);
                    break;
                case "--round":
                    options.Round = Int32.Parse(ReadValue(args, ref i, "--round"),
                        CultureInfo.InvariantCulture);
                    break;
                case "--scenarios":
                    options.ScenarioFilter = ReadValue(args, ref i, "--scenarios")
                        .Split(';', StringSplitOptions.RemoveEmptyEntries)
                        .ToHashSet(StringComparer.Ordinal);
                    break;
                default:
                    throw new ArgumentException($"Unknown audit worker option: {args[i]}", nameof(args));
            }
        }

        if (options.Action is not ("api" or "correctness" or "performance"))
            throw new ArgumentException("--audit-worker must be api, correctness, or performance.");
        if (options.Variant is not ("old" or "new"))
            throw new ArgumentException("--variant must be old or new.");
        if (String.IsNullOrEmpty(options.OutputPath))
            throw new ArgumentException("Audit worker requires --output.");
        if (options.Action != "api" && String.IsNullOrEmpty(options.RootPath))
            throw new ArgumentException($"Audit worker {options.Action} requires --root.");
        if (options.MaxRecords < 1 || options.MaxRecords > AuditComparisonOptions.AbsoluteRecordLimit)
            throw new ArgumentOutOfRangeException(nameof(args), "Invalid --max-records.");
        return options;
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || String.IsNullOrWhiteSpace(args[index]))
            throw new ArgumentException($"{option} requires a value.", nameof(args));
        return args[index];
    }
}

internal static class AuditRepositoryLocator
{
    internal static string FindCurrentRepository()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "DBreeze.sln")) &&
                    File.Exists(Path.Combine(directory.FullName, "DBreeze.Net8", "DBreeze.Net8.csproj")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
        }
        throw new DirectoryNotFoundException("Cannot locate the current DBreeze repository.");
    }
}

