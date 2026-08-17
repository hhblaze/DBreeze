using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DBreeze.Net8.Benchmarks;

internal static class HistoricalBenchmarkReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    internal static void Write(HistoricalBenchmarkReport report, string runDirectory)
    {
        Directory.CreateDirectory(runDirectory);
        WriteAtomic(Path.Combine(runDirectory, "results.json"),
            JsonSerializer.Serialize(report, JsonOptions));
        WriteAtomic(Path.Combine(runDirectory, "results.csv"), BuildCsv(report.Measurements));
        WriteAtomic(Path.Combine(runDirectory, "summary.md"), BuildMarkdown(report));
    }

    private static string BuildCsv(IEnumerable<HistoricalBenchmarkMeasurement> measurements)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Category,Scenario,Phase,IsWarmup,Iteration,StartedUtc,Operations,ReturnedCount,Checksum,ElapsedMilliseconds,OperationsPerSecond,AllocatedBytes,Gen0,Gen1,Gen2,DatabaseBytes,DatabasePath,Succeeded,Error");

        foreach (HistoricalBenchmarkMeasurement item in measurements)
        {
            AppendCsv(sb, item.Category);
            AppendCsv(sb, item.Scenario);
            AppendCsv(sb, item.Phase);
            AppendCsv(sb, item.IsWarmup ? "true" : "false");
            AppendCsv(sb, item.Iteration.ToString(CultureInfo.InvariantCulture));
            AppendCsv(sb, item.StartedUtc.ToString("O", CultureInfo.InvariantCulture));
            AppendCsv(sb, item.Operations.ToString(CultureInfo.InvariantCulture));
            AppendCsv(sb, item.ReturnedCount.ToString(CultureInfo.InvariantCulture));
            AppendCsv(sb, item.Checksum.ToString(CultureInfo.InvariantCulture));
            AppendCsv(sb, item.ElapsedMilliseconds.ToString("F6", CultureInfo.InvariantCulture));
            AppendCsv(sb, item.OperationsPerSecond.ToString("F3", CultureInfo.InvariantCulture));
            AppendCsv(sb, item.AllocatedBytes.ToString(CultureInfo.InvariantCulture));
            AppendCsv(sb, item.Gen0Collections.ToString(CultureInfo.InvariantCulture));
            AppendCsv(sb, item.Gen1Collections.ToString(CultureInfo.InvariantCulture));
            AppendCsv(sb, item.Gen2Collections.ToString(CultureInfo.InvariantCulture));
            AppendCsv(sb, item.DatabaseBytes.ToString(CultureInfo.InvariantCulture));
            AppendCsv(sb, item.DatabasePath);
            AppendCsv(sb, item.Succeeded ? "true" : "false");
            AppendCsv(sb, item.Error, last: true);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendCsv(StringBuilder sb, string value, bool last = false)
    {
        value ??= string.Empty;
        sb.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
        if (!last)
            sb.Append(',');
    }

    private static string BuildMarkdown(HistoricalBenchmarkReport report)
    {
        HistoricalBenchmarkMetadata metadata = report.Metadata;
        var sb = new StringBuilder();
        sb.AppendLine("# DBreeze.Net8 historical-core benchmark");
        sb.AppendLine();
        sb.AppendLine($"- Run: `{metadata.RunId}`");
        sb.AppendLine($"- Started UTC: `{metadata.StartedUtc:O}`");
        sb.AppendLine($"- Completed UTC: `{metadata.CompletedUtc:O}`");
        sb.AppendLine($"- Runtime: `{metadata.Framework}` (`{metadata.RuntimeVersion}`)");
        sb.AppendLine($"- OS/architecture: `{metadata.OS}`, `{metadata.Architecture}`");
        sb.AppendLine($"- CPU: `{metadata.ProcessorIdentifier}`, logical processors: `{metadata.ProcessorCount}`");
        sb.AppendLine($"- GC: `{(metadata.ServerGc ? "Server" : "Workstation")}`, latency `{metadata.GcLatencyMode}`");
        sb.AppendLine($"- DBreeze assembly: `{metadata.DBreezeAssemblyVersion}`");
        sb.AppendLine($"- Repetitions: `{metadata.Repetitions}`, smoke: `{metadata.Smoke}`");
        sb.AppendLine($"- Cache policy: {metadata.CachePolicy}");
        sb.AppendLine($"- Run directory: `{metadata.RunDirectory}`");
        sb.AppendLine();

        var groups = report.Measurements
            .Where(static x => !x.IsWarmup && x.Succeeded)
            .GroupBy(static x => new { x.Category, x.Scenario })
            .OrderBy(static x => x.Key.Category, StringComparer.Ordinal)
            .ThenBy(static x => x.Key.Scenario, StringComparer.Ordinal);

        sb.AppendLine("| Category | Scenario | Operations | Median ms | Min ms | Max ms | Median ops/s | Median allocated MB | DB MB |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var group in groups)
        {
            HistoricalBenchmarkMeasurement[] values = group.ToArray();
            sb.Append("| ").Append(group.Key.Category)
                .Append(" | ").Append(group.Key.Scenario)
                .Append(" | ").Append(values[0].Operations.ToString("N0", CultureInfo.InvariantCulture))
                .Append(" | ").Append(Median(values.Select(static x => x.ElapsedMilliseconds)).ToString("F3", CultureInfo.InvariantCulture))
                .Append(" | ").Append(values.Min(static x => x.ElapsedMilliseconds).ToString("F3", CultureInfo.InvariantCulture))
                .Append(" | ").Append(values.Max(static x => x.ElapsedMilliseconds).ToString("F3", CultureInfo.InvariantCulture))
                .Append(" | ").Append(Median(values.Select(static x => x.OperationsPerSecond)).ToString("N0", CultureInfo.InvariantCulture))
                .Append(" | ").Append((Median(values.Select(static x => (double)x.AllocatedBytes)) / (1024 * 1024)).ToString("F3", CultureInfo.InvariantCulture))
                .Append(" | ").Append((Median(values.Select(static x => (double)x.DatabaseBytes)) / (1024 * 1024)).ToString("F3", CultureInfo.InvariantCulture))
                .AppendLine(" |");
        }

        HistoricalBenchmarkMeasurement[] failures = report.Measurements.Where(static x => !x.Succeeded).ToArray();
        if (failures.Length > 0 || !string.IsNullOrEmpty(metadata.Failure))
        {
            sb.AppendLine();
            sb.AppendLine("## Failures");
            sb.AppendLine();
            foreach (HistoricalBenchmarkMeasurement failure in failures)
                sb.AppendLine($"- `{failure.Scenario}` / `{failure.Phase}`: {failure.Error}");
            if (!string.IsNullOrEmpty(metadata.Failure))
                sb.AppendLine($"- Suite: {metadata.Failure}");
        }

        return sb.ToString();
    }

    private static double Median(IEnumerable<double> source)
    {
        double[] values = source.OrderBy(static x => x).ToArray();
        if (values.Length == 0)
            return 0;
        int middle = values.Length / 2;
        return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / 2 : values[middle];
    }

    private static void WriteAtomic(string path, string content)
    {
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
