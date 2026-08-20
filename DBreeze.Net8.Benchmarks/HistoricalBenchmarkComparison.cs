using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DBreeze.Net8.Benchmarks;

internal static class HistoricalBenchmarkComparison
{
    private const int ExpectedCoreScenarios = 72;
    private const int ExpectedSkipScenarios = 12;
    private const int ExpectedRandomScenarios = 1;
    private const int ExpectedScanScenarios = 2;
    private const int MinimumMeasuredRepetitions = 3;

    private static readonly HashSet<string> DurableScenarios = new(StringComparer.Ordinal)
    {
        "InsertCommitEach100K",
        "UpdateCommitEach100K",
        "RandomInsertCommitEach200K",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    internal static int Run(string[] args)
    {
        try
        {
            ComparisonOptions options = ComparisonOptions.Parse(args);
            HistoricalBenchmarkReport newReport = Load(options.NewResultsPath);
            HistoricalBenchmarkReport oldReport = Load(options.OldResultsPath);

            ValidatedRun newRun = Validate(newReport, "new", allowIncompleteSuite: true);
            ValidatedRun oldRun = Validate(oldReport, "old", allowIncompleteSuite: false);
            HistoricalComparisonReport comparison = Compare(options, newRun, oldRun);
            ApplyPerformanceGate(comparison, options);

            Directory.CreateDirectory(options.OutputDirectory);
            WriteAtomic(Path.Combine(options.OutputDirectory, "comparison.json"),
                JsonSerializer.Serialize(comparison, JsonOptions));
            WriteAtomic(Path.Combine(options.OutputDirectory, "comparison.csv"), BuildCsv(comparison.Scenarios));
            WriteAtomic(Path.Combine(options.OutputDirectory, "comparison.md"), BuildMarkdown(comparison));

            Console.WriteLine($"Compared {comparison.Scenarios.Count} scenarios.");
            Console.WriteLine($"New-version geometric-mean speedup: {comparison.OverallGeometricMeanSpeedup:F3}x");
            Console.WriteLine($"Reports: {options.OutputDirectory}");
            if (!comparison.PassedPerformanceGate)
            {
                Console.Error.WriteLine("Performance gate failed:\n" + String.Join("\n", comparison.GateViolations));
                return 1;
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
    }

    private static HistoricalBenchmarkReport Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Benchmark results were not found.", path);

        HistoricalBenchmarkReport report = JsonSerializer.Deserialize<HistoricalBenchmarkReport>(
            File.ReadAllText(path), JsonOptions);
        if (report?.Metadata is null || report.Measurements is null)
            throw new InvalidDataException($"Invalid benchmark report: {path}");
        return report;
    }

    private static ValidatedRun Validate(
        HistoricalBenchmarkReport report,
        string label,
        bool allowIncompleteSuite)
    {
        if (report.Metadata.Repetitions < MinimumMeasuredRepetitions || report.Metadata.Repetitions > 20)
            throw new InvalidDataException(
                $"The {label} report has {report.Metadata.Repetitions} repetitions; expected {MinimumMeasuredRepetitions}..20.");
        if (!string.IsNullOrEmpty(report.Metadata.Failure))
            throw new InvalidDataException($"The {label} suite failed: {report.Metadata.Failure}");
        if (!allowIncompleteSuite && report.Metadata.CompletedUtc is null)
            throw new InvalidDataException($"The {label} suite did not complete normally.");

        List<HistoricalBenchmarkMeasurement> core = report.Measurements
            .Where(static item => !DurableScenarios.Contains(item.Scenario))
            .ToList();
        if (core.Any(static item => !item.Succeeded))
            throw new InvalidDataException($"The {label} report contains failed core measurements.");

        string scenarioSet = NormalizeScenarioSet(report.Metadata.ScenarioSet);
        int expectedScenarios = scenarioSet switch
        {
            "skip" => ExpectedSkipScenarios,
            "random" => ExpectedRandomScenarios,
            "scan" => ExpectedScanScenarios,
            _ => ExpectedCoreScenarios,
        };
        var groups = core
            .GroupBy(static item => new ScenarioKey(item.Category, item.Scenario))
            .ToDictionary(static group => group.Key, static group => group.ToList());
        if (groups.Count != expectedScenarios)
            throw new InvalidDataException(
                $"The {label} report has {groups.Count} {scenarioSet} scenarios; expected {expectedScenarios}.");

        foreach ((ScenarioKey key, List<HistoricalBenchmarkMeasurement> values) in groups)
        {
            int warmups = values.Count(static item => item.IsWarmup);
            int measured = values.Count(static item => !item.IsWarmup);
            int expectedMeasured = report.Metadata.Repetitions;
            if (warmups != 1 || measured != expectedMeasured || values.Count != expectedMeasured + 1)
            {
                throw new InvalidDataException(
                    $"The {label} scenario {key} has warmup={warmups}, measured={measured}; expected 1 and {expectedMeasured}.");
            }

            ValidateStable(values, static item => item.Operations, label, key, "operations");
            ValidateStable(values, static item => item.ReturnedCount, label, key, "returned count");
            ValidateStable(values, static item => item.Checksum, label, key, "checksum");
        }

        return new ValidatedRun(report, scenarioSet, groups);
    }

    private static string NormalizeScenarioSet(string scenarioSet)
    {
        if (string.IsNullOrEmpty(scenarioSet))
            return "core";
        if (scenarioSet is "core" or "skip" or "random" or "scan")
            return scenarioSet;
        throw new InvalidDataException($"Unknown historical scenario set: {scenarioSet}");
    }

    private static void ValidateStable(
        IEnumerable<HistoricalBenchmarkMeasurement> measurements,
        Func<HistoricalBenchmarkMeasurement, long> selector,
        string label,
        ScenarioKey key,
        string valueName)
    {
        if (measurements.Select(selector).Distinct().Take(2).Count() != 1)
            throw new InvalidDataException($"The {label} scenario {key} has inconsistent {valueName}.");
    }

    private static HistoricalComparisonReport Compare(
        ComparisonOptions options,
        ValidatedRun newRun,
        ValidatedRun oldRun)
    {
        if (!string.Equals(newRun.ScenarioSet, oldRun.ScenarioSet, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Scenario sets differ: new={newRun.ScenarioSet}, old={oldRun.ScenarioSet}.");
        if (newRun.Report.Metadata.Smoke != oldRun.Report.Metadata.Smoke)
            throw new InvalidDataException(
                $"Smoke modes differ: new={newRun.Report.Metadata.Smoke}, old={oldRun.Report.Metadata.Smoke}.");
        if (newRun.Report.Metadata.Repetitions != oldRun.Report.Metadata.Repetitions)
            throw new InvalidDataException(
                $"Measured repetitions differ: new={newRun.Report.Metadata.Repetitions}, old={oldRun.Report.Metadata.Repetitions}.");

        ScenarioKey[] newKeys = newRun.Groups.Keys.OrderBy(static key => key.Category)
            .ThenBy(static key => key.Scenario).ToArray();
        ScenarioKey[] oldKeys = oldRun.Groups.Keys.OrderBy(static key => key.Category)
            .ThenBy(static key => key.Scenario).ToArray();
        if (!newKeys.SequenceEqual(oldKeys))
            throw new InvalidDataException("The old and new reports contain different scenario sets.");

        var rows = new List<HistoricalComparisonRow>(newKeys.Length);
        foreach (ScenarioKey key in newKeys)
        {
            List<HistoricalBenchmarkMeasurement> newAll = newRun.Groups[key];
            List<HistoricalBenchmarkMeasurement> oldAll = oldRun.Groups[key];
            List<HistoricalBenchmarkMeasurement> newMeasured = newAll.Where(static item => !item.IsWarmup).ToList();
            List<HistoricalBenchmarkMeasurement> oldMeasured = oldAll.Where(static item => !item.IsWarmup).ToList();

            long operations = RequireEqual(oldAll[0].Operations, newAll[0].Operations, key, "operations");
            long returned = RequireEqual(oldAll[0].ReturnedCount, newAll[0].ReturnedCount, key, "returned count");
            long checksum = RequireEqual(oldAll[0].Checksum, newAll[0].Checksum, key, "checksum");

            double oldMedianMs = Median(oldMeasured.Select(static item => item.ElapsedMilliseconds));
            double newMedianMs = Median(newMeasured.Select(static item => item.ElapsedMilliseconds));
            double speedup = oldMedianMs / newMedianMs;
            double oldAllocated = Median(oldMeasured.Select(static item => (double)item.AllocatedBytes));
            double newAllocated = Median(newMeasured.Select(static item => (double)item.AllocatedBytes));
            double oldDatabaseBytes = Median(oldMeasured.Select(static item => (double)item.DatabaseBytes));
            double newDatabaseBytes = Median(newMeasured.Select(static item => (double)item.DatabaseBytes));

            rows.Add(new HistoricalComparisonRow
            {
                Category = key.Category,
                Scenario = key.Scenario,
                Operations = operations,
                ReturnedCount = returned,
                Checksum = checksum,
                OldMedianMilliseconds = oldMedianMs,
                OldMinMilliseconds = oldMeasured.Min(static item => item.ElapsedMilliseconds),
                OldMaxMilliseconds = oldMeasured.Max(static item => item.ElapsedMilliseconds),
                NewMedianMilliseconds = newMedianMs,
                NewMinMilliseconds = newMeasured.Min(static item => item.ElapsedMilliseconds),
                NewMaxMilliseconds = newMeasured.Max(static item => item.ElapsedMilliseconds),
                NewSpeedup = speedup,
                NewTimeDeltaPercent = PercentDelta(oldMedianMs, newMedianMs),
                OldMedianOperationsPerSecond = Median(oldMeasured.Select(static item => item.OperationsPerSecond)),
                NewMedianOperationsPerSecond = Median(newMeasured.Select(static item => item.OperationsPerSecond)),
                OldMedianAllocatedBytes = oldAllocated,
                NewMedianAllocatedBytes = newAllocated,
                NewAllocatedDeltaPercent = PercentDelta(oldAllocated, newAllocated),
                OldMedianDatabaseBytes = oldDatabaseBytes,
                NewMedianDatabaseBytes = newDatabaseBytes,
                NewDatabaseBytesDeltaPercent = PercentDelta(oldDatabaseBytes, newDatabaseBytes),
                OldMedianGen0 = Median(oldMeasured.Select(static item => (double)item.Gen0Collections)),
                NewMedianGen0 = Median(newMeasured.Select(static item => (double)item.Gen0Collections)),
                OldMedianGen1 = Median(oldMeasured.Select(static item => (double)item.Gen1Collections)),
                NewMedianGen1 = Median(newMeasured.Select(static item => (double)item.Gen1Collections)),
                OldMedianGen2 = Median(oldMeasured.Select(static item => (double)item.Gen2Collections)),
                NewMedianGen2 = Median(newMeasured.Select(static item => (double)item.Gen2Collections)),
            });
        }

        List<string> warnings = BuildWarnings(newRun.Report.Metadata, oldRun.Report.Metadata);
        if (newRun.Report.Metadata.CompletedUtc is null)
        {
            warnings.Add(
                $"The new suite was stopped after all {rows.Count} scenarios completed; its CompletedUtc is null, " +
                $"but all {rows.Count * (newRun.Report.Metadata.Repetitions + 1)} records passed strict validation.");
        }

        List<HistoricalCategoryComparison> categories = rows
            .GroupBy(static row => row.Category)
            .OrderBy(static group => group.Key)
            .Select(static group => new HistoricalCategoryComparison
            {
                Category = group.Key,
                ScenarioCount = group.Count(),
                NewGeometricMeanSpeedup = GeometricMean(group.Select(static row => row.NewSpeedup)),
                NewFasterCount = group.Count(static row => row.NewSpeedup > 1.0),
                OldFasterCount = group.Count(static row => row.NewSpeedup < 1.0),
            })
            .ToList();

        return new HistoricalComparisonReport
        {
            GeneratedUtc = DateTime.UtcNow,
            ScenarioSet = newRun.ScenarioSet,
            NewResultsPath = options.NewResultsPath,
            OldResultsPath = options.OldResultsPath,
            NewRun = newRun.Report.Metadata,
            OldRun = oldRun.Report.Metadata,
            OverallGeometricMeanSpeedup = GeometricMean(rows.Select(static row => row.NewSpeedup)),
            NewFasterCount = rows.Count(static row => row.NewSpeedup > 1.0),
            OldFasterCount = rows.Count(static row => row.NewSpeedup < 1.0),
            EqualCount = rows.Count(static row => row.NewSpeedup == 1.0),
            Warnings = warnings,
            Categories = categories,
            Scenarios = rows,
        };
    }

    private static void ApplyPerformanceGate(HistoricalComparisonReport report, ComparisonOptions options)
    {
        report.MaxTimeRegressionPercent = options.MaxTimeRegressionPercent;
        report.FailOnAllocationGrowth = options.FailOnAllocationGrowth;

        foreach (HistoricalComparisonRow row in report.Scenarios)
        {
            if (options.MaxTimeRegressionPercent.HasValue &&
                row.NewTimeDeltaPercent > options.MaxTimeRegressionPercent.Value &&
                row.NewMinMilliseconds > row.OldMaxMilliseconds)
            {
                report.GateViolations.Add(
                    $"Time {row.Category}/{row.Scenario}: {row.NewTimeDeltaPercent:+0.00;-0.00;0.00}% " +
                    $"(old max {row.OldMaxMilliseconds:F3} ms, new min {row.NewMinMilliseconds:F3} ms).");
            }

            if (options.FailOnAllocationGrowth && row.NewMedianAllocatedBytes > row.OldMedianAllocatedBytes)
            {
                report.GateViolations.Add(
                    $"Allocations {row.Category}/{row.Scenario}: old {row.OldMedianAllocatedBytes:F0}, " +
                    $"new {row.NewMedianAllocatedBytes:F0} bytes.");
            }
        }

        report.PassedPerformanceGate = report.GateViolations.Count == 0;
    }

    private static long RequireEqual(long oldValue, long newValue, ScenarioKey key, string valueName)
    {
        if (oldValue != newValue)
            throw new InvalidDataException(
                $"Scenario {key} differs in {valueName}: old={oldValue}, new={newValue}.");
        return oldValue;
    }

    private static List<string> BuildWarnings(
        HistoricalBenchmarkMetadata newMetadata,
        HistoricalBenchmarkMetadata oldMetadata)
    {
        var warnings = new List<string>
        {
            "The OS file cache was warmed and was not explicitly flushed.",
            "The runs were sequential rather than interleaved, so system-state drift may affect small differences.",
        };

        if (newMetadata.Smoke && oldMetadata.Smoke)
            warnings.Add("Smoke mode scales the historical 1M/10M workloads down to 10K/100K records.");

        AddMetadataWarning(warnings, "framework", newMetadata.Framework, oldMetadata.Framework);
        AddMetadataWarning(warnings, "runtime", newMetadata.RuntimeVersion, oldMetadata.RuntimeVersion);
        AddMetadataWarning(warnings, "operating system", newMetadata.OS, oldMetadata.OS);
        AddMetadataWarning(warnings, "architecture", newMetadata.Architecture, oldMetadata.Architecture);
        AddMetadataWarning(warnings, "processor", newMetadata.ProcessorIdentifier, oldMetadata.ProcessorIdentifier);
        if (newMetadata.ProcessorCount != oldMetadata.ProcessorCount)
            warnings.Add($"Processor count differs: new={newMetadata.ProcessorCount}, old={oldMetadata.ProcessorCount}.");
        if (newMetadata.ServerGc != oldMetadata.ServerGc)
            warnings.Add($"Server GC differs: new={newMetadata.ServerGc}, old={oldMetadata.ServerGc}.");
        AddMetadataWarning(warnings, "GC latency mode", newMetadata.GcLatencyMode, oldMetadata.GcLatencyMode);
        return warnings;
    }

    private static void AddMetadataWarning(List<string> warnings, string name, string newValue, string oldValue)
    {
        if (!string.Equals(newValue, oldValue, StringComparison.Ordinal))
            warnings.Add($"{name} differs: new={newValue}, old={oldValue}.");
    }

    private static string BuildCsv(IEnumerable<HistoricalComparisonRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Category,Scenario,Operations,ReturnedCount,Checksum,OldMedianMs,OldMinMs,OldMaxMs,NewMedianMs,NewMinMs,NewMaxMs,NewSpeedup,NewTimeDeltaPercent,OldMedianOpsPerSec,NewMedianOpsPerSec,OldMedianAllocatedBytes,NewMedianAllocatedBytes,NewAllocatedDeltaPercent,OldMedianDatabaseBytes,NewMedianDatabaseBytes,NewDatabaseBytesDeltaPercent,OldMedianGen0,NewMedianGen0,OldMedianGen1,NewMedianGen1,OldMedianGen2,NewMedianGen2");
        foreach (HistoricalComparisonRow row in rows)
        {
            AppendCsv(sb, row.Category);
            AppendCsv(sb, row.Scenario);
            AppendCsv(sb, row.Operations);
            AppendCsv(sb, row.ReturnedCount);
            AppendCsv(sb, row.Checksum);
            AppendCsv(sb, row.OldMedianMilliseconds);
            AppendCsv(sb, row.OldMinMilliseconds);
            AppendCsv(sb, row.OldMaxMilliseconds);
            AppendCsv(sb, row.NewMedianMilliseconds);
            AppendCsv(sb, row.NewMinMilliseconds);
            AppendCsv(sb, row.NewMaxMilliseconds);
            AppendCsv(sb, row.NewSpeedup);
            AppendCsv(sb, row.NewTimeDeltaPercent);
            AppendCsv(sb, row.OldMedianOperationsPerSecond);
            AppendCsv(sb, row.NewMedianOperationsPerSecond);
            AppendCsv(sb, row.OldMedianAllocatedBytes);
            AppendCsv(sb, row.NewMedianAllocatedBytes);
            AppendCsv(sb, row.NewAllocatedDeltaPercent);
            AppendCsv(sb, row.OldMedianDatabaseBytes);
            AppendCsv(sb, row.NewMedianDatabaseBytes);
            AppendCsv(sb, row.NewDatabaseBytesDeltaPercent);
            AppendCsv(sb, row.OldMedianGen0);
            AppendCsv(sb, row.NewMedianGen0);
            AppendCsv(sb, row.OldMedianGen1);
            AppendCsv(sb, row.NewMedianGen1);
            AppendCsv(sb, row.OldMedianGen2);
            AppendCsv(sb, row.NewMedianGen2, last: true);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string BuildMarkdown(HistoricalComparisonReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# DBreeze.Net8 historical core comparison");
        sb.AppendLine();
        sb.AppendLine($"Generated UTC: `{report.GeneratedUtc:O}`  ");
        sb.AppendLine($"New results: `{report.NewResultsPath}`  ");
        sb.AppendLine($"Old results: `{report.OldResultsPath}`  ");
        sb.AppendLine($"Scenario set: `{report.ScenarioSet}`  ");
        sb.AppendLine($"Smoke mode: `{report.NewRun.Smoke}`  ");
        sb.AppendLine("Speedup is `old median ms / new median ms`; values above 1.0 mean the new version is faster.");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- Scenarios: {report.Scenarios.Count}");
        sb.AppendLine($"- New-version geometric-mean speedup: **{report.OverallGeometricMeanSpeedup:F3}x**");
        sb.AppendLine($"- New faster: {report.NewFasterCount}; old faster: {report.OldFasterCount}; equal: {report.EqualCount}");
        sb.AppendLine();
        sb.AppendLine("| Category | Scenarios | New speedup (geomean) | New faster | Old faster |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        foreach (HistoricalCategoryComparison category in report.Categories)
            sb.AppendLine($"| {category.Category} | {category.ScenarioCount} | {category.NewGeometricMeanSpeedup:F3}x | {category.NewFasterCount} | {category.OldFasterCount} |");

        AppendTopTable(sb, "Largest new-version improvements", report.Scenarios.OrderByDescending(static row => row.NewSpeedup).Take(10));
        AppendTopTable(sb, "Largest new-version regressions", report.Scenarios.OrderBy(static row => row.NewSpeedup).Take(10));

        sb.AppendLine();
        sb.AppendLine("## All scenarios");
        sb.AppendLine();
        sb.AppendLine("| Category | Scenario | Old median ms | New median ms | Speedup | Time delta | Allocation delta | Old DB MiB | New DB MiB | DB-size delta |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (HistoricalComparisonRow row in report.Scenarios)
        {
            sb.AppendLine($"| {row.Category} | {row.Scenario} | {row.OldMedianMilliseconds:F3} | {row.NewMedianMilliseconds:F3} | {row.NewSpeedup:F3}x | {row.NewTimeDeltaPercent:+0.00;-0.00;0.00}% | {row.NewAllocatedDeltaPercent:+0.00;-0.00;0.00}% | {row.OldMedianDatabaseBytes / (1024 * 1024):F3} | {row.NewMedianDatabaseBytes / (1024 * 1024):F3} | {row.NewDatabaseBytesDeltaPercent:+0.00;-0.00;0.00}% |");
        }

        sb.AppendLine();
        sb.AppendLine("## Notes and validation");
        sb.AppendLine();
        sb.AppendLine($"All scenarios have {report.NewRun.Repetitions} measured repetitions plus one warmup. Operations, returned counts, and checksums match between versions.");
        if (report.MaxTimeRegressionPercent.HasValue || report.FailOnAllocationGrowth)
        {
            sb.AppendLine($"- Performance gate passed: `{report.PassedPerformanceGate}`");
            if (report.MaxTimeRegressionPercent.HasValue)
                sb.AppendLine($"- Confirmed time-regression limit: `{report.MaxTimeRegressionPercent.Value:F2}%`");
            sb.AppendLine($"- Fail on allocation growth: `{report.FailOnAllocationGrowth}`");
            foreach (string violation in report.GateViolations)
                sb.AppendLine("- Gate violation: " + violation);
        }
        foreach (string warning in report.Warnings)
            sb.AppendLine($"- {warning}");
        return sb.ToString();
    }

    private static void AppendTopTable(
        StringBuilder sb,
        string heading,
        IEnumerable<HistoricalComparisonRow> rows)
    {
        sb.AppendLine();
        sb.AppendLine($"## {heading}");
        sb.AppendLine();
        sb.AppendLine("| Category | Scenario | Old median ms | New median ms | Speedup | Time delta |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|");
        foreach (HistoricalComparisonRow row in rows)
            sb.AppendLine($"| {row.Category} | {row.Scenario} | {row.OldMedianMilliseconds:F3} | {row.NewMedianMilliseconds:F3} | {row.NewSpeedup:F3}x | {row.NewTimeDeltaPercent:+0.00;-0.00;0.00}% |");
    }

    private static void AppendCsv(StringBuilder sb, object value, bool last = false)
    {
        string text = value switch
        {
            double number => number.ToString("G17", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value?.ToString() ?? string.Empty,
        };
        sb.Append('"').Append(text.Replace("\"", "\"\"")).Append('"');
        if (!last)
            sb.Append(',');
    }

    private static double Median(IEnumerable<double> source)
    {
        double[] values = source.OrderBy(static value => value).ToArray();
        if (values.Length == 0)
            throw new InvalidOperationException("Cannot calculate a median for an empty sequence.");
        int middle = values.Length / 2;
        return values.Length % 2 == 0
            ? (values[middle - 1] + values[middle]) / 2.0
            : values[middle];
    }

    private static double PercentDelta(double oldValue, double newValue)
    {
        if (oldValue == 0)
            return newValue == 0 ? 0 : double.PositiveInfinity;
        return ((newValue / oldValue) - 1.0) * 100.0;
    }

    private static double GeometricMean(IEnumerable<double> source)
    {
        double[] values = source.ToArray();
        if (values.Length == 0 || values.Any(static value => value <= 0 || !double.IsFinite(value)))
            throw new InvalidOperationException("Geometric mean requires finite positive values.");
        return Math.Exp(values.Average(static value => Math.Log(value)));
    }

    private static void WriteAtomic(string path, string content)
    {
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private readonly record struct ScenarioKey(string Category, string Scenario)
    {
        public override string ToString() => Category + "/" + Scenario;
    }

    private sealed record ValidatedRun(
        HistoricalBenchmarkReport Report,
        string ScenarioSet,
        Dictionary<ScenarioKey, List<HistoricalBenchmarkMeasurement>> Groups);

    private sealed class ComparisonOptions
    {
        internal string NewResultsPath { get; private set; }
        internal string OldResultsPath { get; private set; }
        internal string OutputDirectory { get; private set; }
        internal double? MaxTimeRegressionPercent { get; private set; }
        internal bool FailOnAllocationGrowth { get; private set; }

        internal static ComparisonOptions Parse(string[] args)
        {
            var options = new ComparisonOptions();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--historical-compare":
                        break;
                    case "--new-results":
                        options.NewResultsPath = ReadValue(args, ref i, "--new-results");
                        break;
                    case "--old-results":
                        options.OldResultsPath = ReadValue(args, ref i, "--old-results");
                        break;
                    case "--output":
                        options.OutputDirectory = ReadValue(args, ref i, "--output");
                        break;
                    case "--max-time-regression-percent":
                        string percentage = ReadValue(args, ref i, "--max-time-regression-percent");
                        if (!Double.TryParse(percentage, NumberStyles.Float, CultureInfo.InvariantCulture,
                                out double maxRegression) || maxRegression < 0 || !Double.IsFinite(maxRegression))
                        {
                            throw new ArgumentOutOfRangeException(nameof(args),
                                "--max-time-regression-percent must be a finite non-negative number.");
                        }
                        options.MaxTimeRegressionPercent = maxRegression;
                        break;
                    case "--fail-on-allocation-growth":
                        options.FailOnAllocationGrowth = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown comparison option: {args[i]}", nameof(args));
                }
            }

            if (string.IsNullOrWhiteSpace(options.NewResultsPath)
                || string.IsNullOrWhiteSpace(options.OldResultsPath)
                || string.IsNullOrWhiteSpace(options.OutputDirectory))
            {
                throw new ArgumentException(
                    "--historical-compare requires --new-results, --old-results, and --output.", nameof(args));
            }

            options.NewResultsPath = Path.GetFullPath(options.NewResultsPath);
            options.OldResultsPath = Path.GetFullPath(options.OldResultsPath);
            options.OutputDirectory = Path.GetFullPath(options.OutputDirectory);
            return options;
        }

        private static string ReadValue(string[] args, ref int index, string option)
        {
            if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                throw new ArgumentException($"{option} requires a value.", nameof(args));
            return args[index];
        }
    }
}

internal sealed class HistoricalComparisonReport
{
    public DateTime GeneratedUtc { get; set; }
    public string ScenarioSet { get; set; }
    public string NewResultsPath { get; set; }
    public string OldResultsPath { get; set; }
    public HistoricalBenchmarkMetadata NewRun { get; set; }
    public HistoricalBenchmarkMetadata OldRun { get; set; }
    public double OverallGeometricMeanSpeedup { get; set; }
    public int NewFasterCount { get; set; }
    public int OldFasterCount { get; set; }
    public int EqualCount { get; set; }
    public double? MaxTimeRegressionPercent { get; set; }
    public bool FailOnAllocationGrowth { get; set; }
    public bool PassedPerformanceGate { get; set; } = true;
    public List<string> GateViolations { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<HistoricalCategoryComparison> Categories { get; set; } = new();
    public List<HistoricalComparisonRow> Scenarios { get; set; } = new();
}

internal sealed class HistoricalCategoryComparison
{
    public string Category { get; set; }
    public int ScenarioCount { get; set; }
    public double NewGeometricMeanSpeedup { get; set; }
    public int NewFasterCount { get; set; }
    public int OldFasterCount { get; set; }
}

internal sealed class HistoricalComparisonRow
{
    public string Category { get; set; }
    public string Scenario { get; set; }
    public long Operations { get; set; }
    public long ReturnedCount { get; set; }
    public long Checksum { get; set; }
    public double OldMedianMilliseconds { get; set; }
    public double OldMinMilliseconds { get; set; }
    public double OldMaxMilliseconds { get; set; }
    public double NewMedianMilliseconds { get; set; }
    public double NewMinMilliseconds { get; set; }
    public double NewMaxMilliseconds { get; set; }
    public double NewSpeedup { get; set; }
    public double NewTimeDeltaPercent { get; set; }
    public double OldMedianOperationsPerSecond { get; set; }
    public double NewMedianOperationsPerSecond { get; set; }
    public double OldMedianAllocatedBytes { get; set; }
    public double NewMedianAllocatedBytes { get; set; }
    public double NewAllocatedDeltaPercent { get; set; }
    public double OldMedianDatabaseBytes { get; set; }
    public double NewMedianDatabaseBytes { get; set; }
    public double NewDatabaseBytesDeltaPercent { get; set; }
    public double OldMedianGen0 { get; set; }
    public double NewMedianGen0 { get; set; }
    public double OldMedianGen1 { get; set; }
    public double NewMedianGen1 { get; set; }
    public double OldMedianGen2 { get; set; }
    public double NewMedianGen2 { get; set; }
}
