using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DBreeze.Net8.Benchmarks;

internal static class FocusedBenchmarkComparison
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static int Run(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);
            Dictionary<string, FocusedMeasurement> oldMeasurements = ReadReport(options.OldReportPath);
            Dictionary<string, FocusedMeasurement> newMeasurements = ReadReport(options.NewReportPath);

            string[] oldMethods = oldMeasurements.Keys.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
            string[] newMethods = newMeasurements.Keys.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
            if (!oldMethods.SequenceEqual(newMethods, StringComparer.Ordinal))
                throw new InvalidDataException("Focused benchmark reports contain different method sets.");

            List<FocusedComparisonRow> rows = oldMethods.Select(method =>
            {
                FocusedMeasurement oldValue = oldMeasurements[method];
                FocusedMeasurement newValue = newMeasurements[method];
                return new FocusedComparisonRow
                {
                    Method = method,
                    OldMeanNanoseconds = oldValue.MeanNanoseconds,
                    NewMeanNanoseconds = newValue.MeanNanoseconds,
                    OldMedianNanoseconds = oldValue.MedianNanoseconds,
                    NewMedianNanoseconds = newValue.MedianNanoseconds,
                    NewSpeedup = oldValue.MedianNanoseconds / newValue.MedianNanoseconds,
                    NewTimeDeltaPercent = PercentDelta(oldValue.MedianNanoseconds, newValue.MedianNanoseconds),
                    OldAllocatedBytes = oldValue.AllocatedBytes,
                    NewAllocatedBytes = newValue.AllocatedBytes,
                    NewAllocatedDeltaPercent = PercentDelta(oldValue.AllocatedBytes, newValue.AllocatedBytes),
                };
            }).ToList();

            var report = new FocusedComparisonReport
            {
                GeneratedUtc = DateTime.UtcNow,
                OldReportPath = options.OldReportPath,
                NewReportPath = options.NewReportPath,
                OverallGeometricMeanSpeedup = GeometricMean(rows.Select(static row => row.NewSpeedup)),
                Rows = rows,
            };

            if (Directory.Exists(options.OutputDirectory))
                throw new IOException($"Focused comparison output already exists and will not be overwritten: {options.OutputDirectory}");
            Directory.CreateDirectory(options.OutputDirectory);
            File.WriteAllText(Path.Combine(options.OutputDirectory, "focused-comparison.json"),
                JsonSerializer.Serialize(report, JsonOptions), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(options.OutputDirectory, "focused-comparison.csv"), BuildCsv(rows), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(options.OutputDirectory, "focused-comparison.md"), BuildMarkdown(report), new UTF8Encoding(false));

            Console.WriteLine($"Compared {rows.Count} focused benchmarks.");
            Console.WriteLine(FormattableString.Invariant(
                $"New-version focused geometric-mean median speedup: {report.OverallGeometricMeanSpeedup:F3}x"));
            Console.WriteLine($"Reports: {options.OutputDirectory}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
    }

    private static Dictionary<string, FocusedMeasurement> ReadReport(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Focused BenchmarkDotNet report was not found.", path);

        string[] lines = File.ReadAllLines(path);
        int headerIndex = Array.FindIndex(lines, static line => line.StartsWith("| Method", StringComparison.Ordinal));
        if (headerIndex < 0)
            throw new InvalidDataException($"Benchmark table was not found: {path}");

        string[] headers = ParseMarkdownRow(lines[headerIndex]);
        int methodIndex = Array.IndexOf(headers, "Method");
        int meanIndex = Array.IndexOf(headers, "Mean");
        int medianIndex = Array.IndexOf(headers, "Median");
        int allocatedIndex = Array.IndexOf(headers, "Allocated");
        if (methodIndex < 0 || meanIndex < 0 || medianIndex < 0 || allocatedIndex < 0)
            throw new InvalidDataException($"Required benchmark columns were not found: {path}");

        var result = new Dictionary<string, FocusedMeasurement>(StringComparer.Ordinal);
        for (int i = headerIndex + 2; i < lines.Length && lines[i].StartsWith('|'); i++)
        {
            string[] values = ParseMarkdownRow(lines[i]);
            if (values.Length != headers.Length)
                throw new InvalidDataException($"Invalid benchmark table row: {lines[i]}");
            if (values[meanIndex].Equals("NA", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Benchmark {values[methodIndex]} has no measurements in {path}.");

            result.Add(values[methodIndex], new FocusedMeasurement(
                ParseTimeNanoseconds(values[meanIndex]),
                ParseTimeNanoseconds(values[medianIndex]),
                ParseBytes(values[allocatedIndex])));
        }

        if (result.Count == 0)
            throw new InvalidDataException($"Benchmark report contains no measurements: {path}");
        return result;
    }

    private static string[] ParseMarkdownRow(string line) => line.Trim().Trim('|').Split('|')
        .Select(static value => value.Trim()).ToArray();

    private static double ParseTimeNanoseconds(string text)
    {
        (double value, string unit) = ParseMetric(text);
        return unit switch
        {
            "ns" => value,
            "us" or "μs" => value * 1_000,
            "ms" => value * 1_000_000,
            "s" => value * 1_000_000_000,
            _ => throw new InvalidDataException($"Unknown benchmark time unit: {unit}"),
        };
    }

    private static double ParseBytes(string text)
    {
        if (text == "-")
            return 0;
        (double value, string unit) = ParseMetric(text);
        return unit switch
        {
            "B" => value,
            "KB" => value * 1024,
            "MB" => value * 1024 * 1024,
            "GB" => value * 1024 * 1024 * 1024,
            _ => throw new InvalidDataException($"Unknown allocation unit: {unit}"),
        };
    }

    private static (double Value, string Unit) ParseMetric(string text)
    {
        int separator = text.LastIndexOf(' ');
        if (separator <= 0)
            throw new InvalidDataException($"Invalid benchmark metric: {text}");
        string number = text[..separator].Replace(",", string.Empty, StringComparison.Ordinal);
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            throw new InvalidDataException($"Invalid benchmark number: {text}");
        return (value, text[(separator + 1)..]);
    }

    private static string BuildCsv(IEnumerable<FocusedComparisonRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Method,OldMeanNanoseconds,NewMeanNanoseconds,OldMedianNanoseconds,NewMedianNanoseconds,NewSpeedup,NewTimeDeltaPercent,OldAllocatedBytes,NewAllocatedBytes,NewAllocatedDeltaPercent");
        foreach (FocusedComparisonRow row in rows)
        {
            sb.Append(row.Method).Append(',')
                .Append(row.OldMeanNanoseconds.ToString("G17", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.NewMeanNanoseconds.ToString("G17", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.OldMedianNanoseconds.ToString("G17", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.NewMedianNanoseconds.ToString("G17", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.NewSpeedup.ToString("G17", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.NewTimeDeltaPercent.ToString("G17", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.OldAllocatedBytes.ToString("G17", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.NewAllocatedBytes.ToString("G17", CultureInfo.InvariantCulture)).Append(',')
                .AppendLine(row.NewAllocatedDeltaPercent.ToString("G17", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static string BuildMarkdown(FocusedComparisonReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# DBreeze.Net8 focused benchmark comparison");
        sb.AppendLine();
        sb.AppendLine("Mean and median are taken from the supplied BenchmarkDotNet reports.");
        sb.AppendLine(FormattableString.Invariant(
            $"Overall geometric-mean median speedup: **{report.OverallGeometricMeanSpeedup:F3}x**"));
        sb.AppendLine();
        sb.AppendLine("| Method | Old mean | New mean | Old median | New median | Speedup | Time delta | Old allocated | New allocated | Allocation delta |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (FocusedComparisonRow row in report.Rows)
        {
            sb.AppendLine(FormattableString.Invariant(
                $"| {row.Method} | {FormatTime(row.OldMeanNanoseconds)} | {FormatTime(row.NewMeanNanoseconds)} | {FormatTime(row.OldMedianNanoseconds)} | {FormatTime(row.NewMedianNanoseconds)} | {row.NewSpeedup:F3}x | {row.NewTimeDeltaPercent:+0.00;-0.00;0.00}% | {row.OldAllocatedBytes:F0} B | {row.NewAllocatedBytes:F0} B | {row.NewAllocatedDeltaPercent:+0.00;-0.00;0.00}% |"));
        }
        return sb.ToString();
    }

    private static string FormatTime(double nanoseconds)
    {
        if (nanoseconds >= 1_000_000)
            return (nanoseconds / 1_000_000).ToString("F3", CultureInfo.InvariantCulture) + " ms";
        if (nanoseconds >= 1_000)
            return (nanoseconds / 1_000).ToString("F3", CultureInfo.InvariantCulture) + " μs";
        return nanoseconds.ToString("F1", CultureInfo.InvariantCulture) + " ns";
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

    private readonly record struct FocusedMeasurement(
        double MeanNanoseconds,
        double MedianNanoseconds,
        double AllocatedBytes);

    private sealed class Options
    {
        internal string NewReportPath { get; private set; }
        internal string OldReportPath { get; private set; }
        internal string OutputDirectory { get; private set; }

        internal static Options Parse(string[] args)
        {
            var options = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--focused-compare":
                        break;
                    case "--new-report":
                        options.NewReportPath = Path.GetFullPath(ReadValue(args, ref i, "--new-report"));
                        break;
                    case "--old-report":
                        options.OldReportPath = Path.GetFullPath(ReadValue(args, ref i, "--old-report"));
                        break;
                    case "--output":
                        options.OutputDirectory = Path.GetFullPath(ReadValue(args, ref i, "--output"));
                        break;
                    default:
                        throw new ArgumentException($"Unknown focused comparison option: {args[i]}", nameof(args));
                }
            }

            if (string.IsNullOrEmpty(options.NewReportPath)
                || string.IsNullOrEmpty(options.OldReportPath)
                || string.IsNullOrEmpty(options.OutputDirectory))
            {
                throw new ArgumentException("--focused-compare requires --new-report, --old-report, and --output.", nameof(args));
            }
            return options;
        }

        private static string ReadValue(string[] args, ref int index, string option)
        {
            if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                throw new ArgumentException($"{option} requires a value.", nameof(args));
            return args[index];
        }
    }

    private sealed class FocusedComparisonReport
    {
        public DateTime GeneratedUtc { get; set; }
        public string OldReportPath { get; set; }
        public string NewReportPath { get; set; }
        public double OverallGeometricMeanSpeedup { get; set; }
        public List<FocusedComparisonRow> Rows { get; set; } = new();
    }

    private sealed class FocusedComparisonRow
    {
        public string Method { get; set; }
        public double OldMeanNanoseconds { get; set; }
        public double NewMeanNanoseconds { get; set; }
        public double OldMedianNanoseconds { get; set; }
        public double NewMedianNanoseconds { get; set; }
        public double NewSpeedup { get; set; }
        public double NewTimeDeltaPercent { get; set; }
        public double OldAllocatedBytes { get; set; }
        public double NewAllocatedBytes { get; set; }
        public double NewAllocatedDeltaPercent { get; set; }
    }
}
