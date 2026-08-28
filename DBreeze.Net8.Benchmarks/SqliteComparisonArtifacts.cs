using System.Globalization;
using System.Net;
using System.Text;

namespace DBreeze.Net8.Benchmarks;

internal static class SqliteComparisonArtifacts
{
    internal static void Write(SqliteComparisonReport report)
    {
        report.Summaries = BuildSummaries(report.Measurements);
        AuditPersistence.WriteJson(report.Metadata.RawJson, report);
        AuditPersistence.WriteTextAtomic(report.Metadata.RawCsv, BuildCsv(report.Measurements));
        string html = BuildHtml(report);
        AuditPersistence.WriteTextAtomic(report.Metadata.ImmutableHtml, html);
        AuditPersistence.WriteTextAtomic(report.Metadata.CanonicalHtml, html);
    }

    internal static List<SqliteComparisonSummary> BuildSummaries(
        IEnumerable<SqliteComparisonMeasurement> measurements)
    {
        var summaries = measurements
            .Where(static value => value.Succeeded)
            .GroupBy(static value => new { value.Scenario, value.Provider })
            .Select(group =>
            {
                SqliteComparisonMeasurement[] values = group.OrderBy(static value => value.Round).ToArray();
                return new SqliteComparisonSummary
                {
                    Scenario = group.Key.Scenario,
                    Provider = group.Key.Provider,
                    Rounds = values.Length,
                    Operations = values[0].Operations,
                    MedianMilliseconds = Median(values.Select(static value => value.ElapsedMilliseconds)),
                    MedianPreparationMilliseconds = NullableMedian(values.Select(static value => value.PreparationMilliseconds)),
                    MedianMutationMilliseconds = NullableMedian(values.Select(static value => value.MutationMilliseconds)),
                    MinimumMilliseconds = values.Min(static value => value.ElapsedMilliseconds),
                    MaximumMilliseconds = values.Max(static value => value.ElapsedMilliseconds),
                    MedianOperationsPerSecond = Median(values.Select(static value => value.OperationsPerSecond)),
                    MedianDatabaseBytes = (long)Median(values.Select(static value => (double)value.DatabaseBytes)),
                };
            })
            .OrderBy(static value => value.Scenario, StringComparer.Ordinal)
            .ThenBy(static value => ProviderOrder(value.Provider))
            .ToList();

        foreach (SqliteComparisonSummary summary in summaries)
        {
            SqliteComparisonSummary sqlite = summaries.FirstOrDefault(value =>
                value.Scenario == summary.Scenario && value.Provider == "SQLite");
            if (sqlite == null || sqlite.MedianOperationsPerSecond <= 0)
            {
                summary.Comparison = summary.Provider == "SQLite" ? "Reference" : "No SQLite pair";
                continue;
            }

            summary.RatioVsSqlite = summary.MedianOperationsPerSecond / sqlite.MedianOperationsPerSecond;
            summary.Comparison = summary.Provider == "SQLite"
                ? "Reference"
                : ClassifyRatio(summary.RatioVsSqlite, summary.Provider);
        }
        return summaries;
    }

    internal static double Median(IEnumerable<double> values)
    {
        double[] ordered = values.OrderBy(static value => value).ToArray();
        if (ordered.Length == 0)
            return 0;
        int middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2.0
            : ordered[middle];
    }

    private static double? NullableMedian(IEnumerable<double?> values)
    {
        double[] present = values.Where(static value => value.HasValue)
            .Select(static value => value.Value).ToArray();
        return present.Length == 0 ? null : Median(present);
    }

    internal static string ClassifyRatio(double ratio, string provider)
    {
        if (ratio >= 0.97 && ratio <= 1.03)
            return "Approximate parity (±3%)";
        return ratio > 1.03 ? provider + " faster" : "SQLite faster";
    }

    internal static string Html(string value) => WebUtility.HtmlEncode(value ?? String.Empty);

    private static string BuildCsv(IEnumerable<SqliteComparisonMeasurement> measurements)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Scenario,Provider,Round,Operations,ReturnedCount,Checksum,ElapsedMilliseconds,PreparationMilliseconds,MutationMilliseconds,TransactionCreateMilliseconds,CommitMilliseconds,DisposeMilliseconds,TransactionCount,WorkerCount,AllocatedBytes,OperationsPerSecond,DatabaseBytes,Succeeded,DatabasePath,Error");
        foreach (SqliteComparisonMeasurement value in measurements)
        {
            Csv(builder, value.Scenario); Csv(builder, value.Provider); Csv(builder, value.Round);
            Csv(builder, value.Operations); Csv(builder, value.ReturnedCount); Csv(builder, value.Checksum);
            Csv(builder, value.ElapsedMilliseconds); Csv(builder, value.PreparationMilliseconds); Csv(builder, value.MutationMilliseconds);
            Csv(builder, value.TransactionCreateMilliseconds); Csv(builder, value.CommitMilliseconds); Csv(builder, value.DisposeMilliseconds);
            Csv(builder, value.TransactionCount); Csv(builder, value.WorkerCount); Csv(builder, value.AllocatedBytes);
            Csv(builder, value.OperationsPerSecond);
            Csv(builder, value.DatabaseBytes); Csv(builder, value.Succeeded);
            Csv(builder, value.DatabasePath); Csv(builder, value.Error, true);
        }
        return builder.ToString();
    }

    private static string BuildHtml(SqliteComparisonReport report)
    {
        SqliteComparisonMetadata metadata = report.Metadata;
        SqliteComparisonConfiguration configuration = report.Configuration;
        var builder = new StringBuilder(96_000);
        builder.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>DBreeze vs SQLite Performance Comparison</title><style>")
            .Append("body{font:14px/1.45 system-ui,-apple-system,Segoe UI,sans-serif;margin:0;background:#f5f7fa;color:#17202a}main{max-width:1650px;margin:auto;padding:28px}h1{margin-bottom:4px}h2{margin-top:32px}code,.mono{font-family:Cascadia Mono,Consolas,monospace}.card{background:#fff;border:1px solid #dce3ea;border-radius:10px;padding:18px;margin:14px 0;box-shadow:0 1px 2px #0001}.ok{color:#08783e;font-weight:700}.fail{color:#b42318;font-weight:700}.warn{color:#9a6700;font-weight:700}table{border-collapse:collapse;width:100%;background:#fff}th,td{border:1px solid #dce3ea;padding:7px 9px;text-align:left;vertical-align:top}th{background:#eef3f8;position:sticky;top:0}.num{text-align:right;font-variant-numeric:tabular-nums}.small{font-size:12px;color:#536273}.bar{height:7px;background:#d5e7fa;border-radius:5px;overflow:hidden;min-width:80px}.bar>i{display:block;height:100%;background:#2878bd}.db{border-left:4px solid #2878bd}.sqlite{border-left:4px solid #d97a18}.rks{border-left:4px solid #6f42c1}.rksno{border-left:4px solid #15847b}.sorted{border-left:4px solid #b75c9d}.fallback{border-left:4px solid #67758a}details{margin-top:16px}ul{margin-top:6px}</style></head><body><main>")
            .Append("<h1>DBreeze vs SQLite Performance Comparison</h1><p class=\"small\">Generated ")
            .Append(Html((metadata.CompletedUtc ?? DateTime.UtcNow).ToString("O", CultureInfo.InvariantCulture)))
            .Append(" · descriptive benchmark, not a release gate</p>");

        builder.Append("<div class=\"card\"><strong class=\"")
            .Append(report.Succeeded ? "ok\">COMPLETE" : "fail\">INCOMPLETE")
            .Append("</strong><p>")
            .Append(report.Succeeded
                ? "Every configured workload and correctness oracle completed."
                : "One or more workloads or correctness oracles failed; successful measurements remain visible.")
            .Append("</p></div>");

        builder.Append("<h2>Configuration</h2><div class=\"card\"><table><tbody>");
        Row(builder, "Records", configuration.Records.ToString("N0", CultureInfo.InvariantCulture));
        Row(builder, "Payload", configuration.PayloadBytes.ToString(CultureInfo.InvariantCulture) + " bytes; deterministic pool of " + configuration.PayloadPoolSize);
        Row(builder, "Measured rounds", configuration.Repetitions.ToString(CultureInfo.InvariantCulture));
        Row(builder, "Parallel readers", configuration.Parallelism.ToString(CultureInfo.InvariantCulture));
        Row(builder, "Parallel table insert", $"{configuration.MultiTableRecords:N0} rows; {configuration.MultiTableCount} dedicated workers/tables; {configuration.MultiTableBatchSize} rows/transaction; SQLite busy_timeout={configuration.MultiTableSqliteBusyTimeoutMilliseconds} ms");
        Row(builder, "SQLite durability", $"journal_mode={configuration.SqliteJournalMode}; synchronous={configuration.SqliteSynchronous}; busy_timeout={configuration.SqliteBusyTimeoutMilliseconds} ms");
        Row(builder, "Main schema", configuration.MainSchema);
        Row(builder, "Prefix schema", configuration.PrefixSchema);
        Row(builder, "Timing", configuration.TimingPolicy);
        Row(builder, "Cache", configuration.CachePolicy);
        builder.Append("</tbody></table></div>");

        builder.Append("<h2>Headline medians</h2><p class=\"small\">Ratio = provider median ops/s ÷ SQLite median ops/s. Values within ±3% are labeled approximate parity.</p>")
            .Append("<table><thead><tr><th>Scenario</th><th>Provider</th><th class=\"num\">Operations</th><th class=\"num\">Median ms</th><th class=\"num\">Sort/prep ms</th><th class=\"num\">Delete+commit ms</th><th class=\"num\">Min–max ms</th><th class=\"num\">Median ops/s</th><th>Relative throughput</th><th class=\"num\">Ratio</th><th>Comparison</th><th class=\"num\">DB MB</th></tr></thead><tbody>");
        foreach (IGrouping<string, SqliteComparisonSummary> scenario in report.Summaries.GroupBy(static value => value.Scenario))
        {
            double maximum = scenario.Max(static value => value.MedianOperationsPerSecond);
            foreach (SqliteComparisonSummary value in scenario)
            {
                string css = value.Provider switch
                {
                    "SQLite" => "sqlite",
                    "DBreeze RKS + NoOverwrite" => "rksno",
                    "DBreeze RKS" => "rks",
                    "DBreeze Sorted" => "sorted",
                    "DBreeze Sorted + NoOverwrite" => "sorted",
                    "DBreeze RKS Remove" => "fallback",
                    "DBreeze RKS Remove + NoOverwrite" => "fallback",
                    _ => "db",
                };
                double width = maximum > 0 ? Math.Max(2, value.MedianOperationsPerSecond / maximum * 100) : 0;
                builder.Append("<tr class=\"").Append(css).Append("\"><td>").Append(Html(value.Scenario))
                    .Append("</td><td>").Append(Html(value.Provider)).Append("</td><td class=\"num\">").Append(N(value.Operations))
                    .Append("</td><td class=\"num\">").Append(F(value.MedianMilliseconds))
                    .Append("</td><td class=\"num\">").Append(F(value.MedianPreparationMilliseconds))
                    .Append("</td><td class=\"num\">").Append(F(value.MedianMutationMilliseconds))
                    .Append("</td><td class=\"num\">").Append(F(value.MinimumMilliseconds)).Append("–").Append(F(value.MaximumMilliseconds))
                    .Append("</td><td class=\"num\">").Append(N(value.MedianOperationsPerSecond))
                    .Append("</td><td><div class=\"bar\"><i style=\"width:").Append(width.ToString("F1", CultureInfo.InvariantCulture)).Append("%\"></i></div></td><td class=\"num\">")
                    .Append(value.Provider == "SQLite" ? "1.000×" : value.RatioVsSqlite.ToString("F3", CultureInfo.InvariantCulture) + "×")
                    .Append("</td><td>").Append(Html(value.Comparison)).Append("</td><td class=\"num\">")
                    .Append((value.MedianDatabaseBytes / 1048576.0).ToString("F2", CultureInfo.InvariantCulture)).Append("</td></tr>");
            }
        }
        builder.Append("</tbody></table>");

        builder.Append("<h2>Correctness and failures</h2><div class=\"card\">");
        if (report.Failures.Count == 0)
            builder.Append("<p class=\"ok\">All counts, checksums, ordering and final-state checks passed.</p>");
        else
        {
            builder.Append("<ul>");
            foreach (string failure in report.Failures)
                builder.Append("<li class=\"fail\">").Append(Html(failure)).Append("</li>");
            builder.Append("</ul>");
        }
        if (report.Findings != null && report.Findings.Count > 0)
        {
            builder.Append("<h3>Performance findings</h3><ul>");
            foreach (string finding in report.Findings)
                builder.Append("<li>").Append(Html(finding)).Append("</li>");
            builder.Append("</ul>");
        }
        builder.Append("</div>");

        builder.Append("<details><summary><strong>Per-round measurements</strong></summary><table><thead><tr><th>Scenario</th><th>Provider</th><th class=\"num\">Round</th><th class=\"num\">total ms</th><th class=\"num\">sort/prep ms</th><th class=\"num\">mutation ms</th><th class=\"num\">tx create ms</th><th class=\"num\">commit ms</th><th class=\"num\">dispose ms</th><th class=\"num\">transactions</th><th class=\"num\">workers</th><th class=\"num\">allocated</th><th class=\"num\">ops/s</th><th class=\"num\">Returned</th><th class=\"num\">Checksum</th><th>Status</th><th>Database</th></tr></thead><tbody>");
        foreach (SqliteComparisonMeasurement value in report.Measurements)
        {
            builder.Append("<tr><td>").Append(Html(value.Scenario)).Append("</td><td>").Append(Html(value.Provider))
                .Append("</td><td class=\"num\">").Append(value.Round).Append("</td><td class=\"num\">").Append(F(value.ElapsedMilliseconds))
                .Append("</td><td class=\"num\">").Append(F(value.PreparationMilliseconds))
                .Append("</td><td class=\"num\">").Append(F(value.MutationMilliseconds))
                .Append("</td><td class=\"num\">").Append(F(value.TransactionCreateMilliseconds))
                .Append("</td><td class=\"num\">").Append(F(value.CommitMilliseconds))
                .Append("</td><td class=\"num\">").Append(F(value.DisposeMilliseconds))
                .Append("</td><td class=\"num\">").Append(value.TransactionCount == 0 ? "—" : N(value.TransactionCount))
                .Append("</td><td class=\"num\">").Append(value.WorkerCount == 0 ? "—" : value.WorkerCount.ToString(CultureInfo.InvariantCulture))
                .Append("</td><td class=\"num\">").Append(value.AllocatedBytes == 0 ? "—" : N(value.AllocatedBytes))
                .Append("</td><td class=\"num\">").Append(N(value.OperationsPerSecond)).Append("</td><td class=\"num\">").Append(N(value.ReturnedCount))
                .Append("</td><td class=\"num mono\">").Append(value.Checksum).Append("</td><td class=\"").Append(value.Succeeded ? "ok\">PASS" : "fail\">FAIL")
                .Append(value.Succeeded ? String.Empty : ": " + Html(value.Error)).Append("</td><td class=\"small mono\">").Append(Html(value.DatabasePath)).Append("</td></tr>");
        }
        builder.Append("</tbody></table></details>");

        builder.Append("<h2>Environment and provenance</h2><div class=\"card\"><table><tbody>");
        Row(builder, "Run", metadata.RunId);
        Row(builder, ".NET / OS", metadata.Runtime + " / " + metadata.OS + " / " + metadata.Architecture);
        Row(builder, "CPU", metadata.ProcessorIdentifier + "; logical processors=" + metadata.LogicalProcessors);
        Row(builder, "GC", (metadata.ServerGc ? "Server" : "Workstation") + "; " + metadata.GcLatencyMode);
        Row(builder, "Git", metadata.GitHead + (metadata.GitDirty ? "; dirty status SHA-256 " + metadata.GitStatusSha256 : "; clean"));
        Row(builder, "DBreeze", metadata.DBreezeVersion + "; SHA-256 " + metadata.DBreezeSha256 + "; " + metadata.DBreezeAssembly);
        Row(builder, "SQLite", "Microsoft.Data.Sqlite " + metadata.MicrosoftDataSqliteVersion + "; native " + metadata.NativeSqliteVersion);
        Row(builder, "Raw JSON / CSV / log", metadata.RawJson + " | " + metadata.RawCsv + " | " + metadata.LogPath);
        if (!String.IsNullOrEmpty(metadata.AugmentedFromJson))
        {
            Row(builder, "Augmented source", metadata.AugmentedFromRunId + "; " + metadata.AugmentedFromJson);
            Row(builder, "Imported measurements", metadata.ImportedMeasurementCount.ToString(CultureInfo.InvariantCulture));
        }
        Row(builder, "Scratch", metadata.ScratchDirectory + (configuration.KeepDatabases ? " (kept)" : " (removed after complete run)"));
        builder.Append("</tbody></table></div><h2>Interpretation notes</h2><div class=\"card\"><ul>")
            .Append("<li>Results describe this machine, filesystem, runtime and exact configuration; they are not universal product claims.</li>")
            .Append("<li>Operating-system cache is deliberately not flushed. Provider order alternates across rounds to reduce systematic warm-cache bias.</li>")
            .Append("<li>SQLite uses prepared commands and WAL/FULL. DBreeze uses normal transactions; random insertion and random update additionally show RandomKeySorter with bounded flushes.</li>")
            .Append("<li>DBreeze RKS + NoOverwrite calls Technical_SetTable_OverwriteIsNotAllowed before the first update. The transaction-local mode appends changed data instead of overwriting it and can trade a larger database file for speed.</li>")
            .Append("<li>DBreeze Sorted random delete clones keys outside measurement, then includes in its headline time the in-memory ascending sort, transaction creation, all RemoveKey calls and commit. Split sort and delete+commit times are diagnostic.</li>")
            .Append("<li>Delete fallbacks use one RandomKeySorter.Remove flush after 100K operations and/or the transaction-local NoOverwrite flag. SQLite is imported once per round and shared as their reference; database growth is informational.</li>")
            .Append("<li>The parallel per-table insert uses one physical database per provider and 20 dedicated workers. Each worker owns one table and commits 50 monotonically ascending local keys per transaction. SQLite uses one prepared connection per worker and a 60-second busy timeout; lock waiting remains measured.</li>")
            .Append("<li>SQLite schemas and empty DBreeze tables are materialized before timing. DBreeze uses a committed insert/remove sentinel lifecycle because it has no schema-only table creation API; no sentinel remains in the measured fixture.</li>")
            .Append("<li>Every DBreeze transaction in the parallel per-table insert touches one table, so it uses table-local .rol/.rhp recovery and does not enlist the global _DBreezeTranJrnl.</li>")
            .Append("<li>Parallel phase timings and allocations are sums across worker threads and may exceed wall-clock elapsed time.</li>")
            .Append("<li>Engine/connection opening, fixture construction, file copying and SQLite WAL checkpoint are outside measured bodies.</li>")
            .Append("</ul></div></main></body></html>");
        return builder.ToString();
    }

    private static int ProviderOrder(string provider) => provider switch
    {
        "DBreeze" => 0,
        "DBreeze RKS" => 1,
        "DBreeze RKS + NoOverwrite" => 2,
        "DBreeze Sorted" => 3,
        "DBreeze RKS Remove" => 4,
        "DBreeze Sorted + NoOverwrite" => 5,
        "DBreeze RKS Remove + NoOverwrite" => 6,
        "SQLite" => 7,
        _ => 8,
    };

    private static void Row(StringBuilder builder, string name, string value) => builder
        .Append("<tr><th>").Append(Html(name)).Append("</th><td class=\"mono\">")
        .Append(Html(value)).Append("</td></tr>");

    private static string F(double value) => value.ToString("N3", CultureInfo.InvariantCulture);
    private static string F(double? value) => value.HasValue ? F(value.Value) : "—";
    private static string N(double value) => value.ToString("N0", CultureInfo.InvariantCulture);
    private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static void Csv(StringBuilder builder, object value, bool last = false)
    {
        string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? String.Empty;
        if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
            text = "\"" + text.Replace("\"", "\"\"") + "\"";
        builder.Append(text).Append(last ? '\n' : ',');
    }
}

internal static class SqliteComparisonSelfTests
{
    internal static int Run()
    {
        var failures = new List<string>();
        Check(failures, Math.Abs(SqliteComparisonArtifacts.Median(new[] { 3d, 1d, 2d }) - 2d) < 0.0001, "Odd median");
        Check(failures, Math.Abs(SqliteComparisonArtifacts.Median(new[] { 4d, 1d, 2d, 3d }) - 2.5d) < 0.0001, "Even median");
        Check(failures, SqliteComparisonArtifacts.ClassifyRatio(1.03, "DBreeze").Contains("parity", StringComparison.OrdinalIgnoreCase), "3% upper boundary");
        Check(failures, SqliteComparisonArtifacts.ClassifyRatio(0.97, "DBreeze").Contains("parity", StringComparison.OrdinalIgnoreCase), "3% lower boundary");
        Check(failures, SqliteComparisonArtifacts.Html("<x>&\"") == "&lt;x&gt;&amp;&quot;", "HTML escaping");

        string root = Path.Combine(Path.GetTempPath(), "dbreeze-sqlite-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            SqliteComparisonOptions parsed = SqliteComparisonOptions.Parse(new[]
            {
                "--sqlite-compare", "--smoke", "--root", root, "--run-id", "valid-run",
            });
            Check(failures, parsed.Records == 10_000 && parsed.MultiTableRecords == 10_000 &&
                parsed.MultiTableCount == 20 && parsed.MultiTableBatchSize == 50 &&
                parsed.Repetitions == 1, "Smoke limits");
            Check(failures, parsed.ReportPath.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase), "Default report containment");

            SqliteComparisonOptions multi = SqliteComparisonOptions.Parse(new[]
            {
                "--sqlite-compare", "--root", root, "--run-id", "multi-options",
                "--multi-table-records", "1001", "--multi-table-count", "20",
                "--multi-table-batch-size", "50",
            });
            Check(failures, multi.MultiTableRecords == 1001 && multi.MultiTableCount == 20 &&
                multi.MultiTableBatchSize == 50, "Multi-table option parsing");

            ExpectFailure(failures, "Record limit", () => SqliteComparisonOptions.Parse(new[] { "--sqlite-compare", "--records", "1000001" }));
            ExpectFailure(failures, "Synchronous validation", () => SqliteComparisonOptions.Parse(new[] { "--sqlite-compare", "--sqlite-synchronous", "OFF" }));
            ExpectFailure(failures, "Multi-table count limit", () => SqliteComparisonOptions.Parse(new[] { "--sqlite-compare", "--multi-table-count", "65" }));
            ExpectFailure(failures, "Run-id validation", () => SqliteComparisonOptions.Parse(new[] { "--sqlite-compare", "--run-id", "../escape" }));
            ExpectFailure(failures, "Report containment", () => SqliteComparisonOptions.Parse(new[] { "--sqlite-compare", "--root", root, "--report", Path.Combine(Path.GetTempPath(), "outside.html") }));
            ExpectFailure(failures, "Path containment", () => AuditRunLayout.EnsureUnderRoot(Path.Combine(Path.GetTempPath(), "outside"), root));

            string augmentSource = Path.Combine(root, "source.json");
            Directory.CreateDirectory(root);
            File.WriteAllText(augmentSource, "{}");
            SqliteComparisonAugmentOptions sortedOptions = SqliteComparisonAugmentOptions.Parse(new[]
            {
                "--sqlite-compare-augment-sorted-delete", "--source-report", augmentSource,
                "--root", root, "--run-id", "sorted-options",
            });
            SqliteComparisonAugmentOptions fallbackOptions = SqliteComparisonAugmentOptions.Parse(new[]
            {
                "--sqlite-compare-augment-delete-fallbacks", "--source-report", augmentSource,
                "--root", root, "--run-id", "fallback-options",
            });
            Check(failures, sortedOptions.Kind == SqliteComparisonAugmentKind.SortedDelete, "Sorted augment option");
            Check(failures, fallbackOptions.Kind == SqliteComparisonAugmentKind.DeleteFallbacks, "Fallback augment option");

            var layout = new AuditRunLayout(root, "owner-test");
            layout.Create();
            File.WriteAllText(layout.MarkerPath, "forged");
            ExpectFailure(failures, "Owner marker", layout.CleanupScratch);
            File.WriteAllText(layout.MarkerPath, layout.RunId + Environment.NewLine);
            layout.CleanupScratch();
            Check(failures, !Directory.Exists(layout.ScratchDirectory), "Owned cleanup");

            var values = new[]
            {
                new SqliteComparisonMeasurement { Scenario = "x", Provider = "DBreeze", Round = 1, Operations = 1, ElapsedMilliseconds = 5, PreparationMilliseconds = 1, MutationMilliseconds = 4, OperationsPerSecond = 200, Succeeded = true },
                new SqliteComparisonMeasurement { Scenario = "x", Provider = "DBreeze RKS", Round = 1, Operations = 1, ElapsedMilliseconds = 2, OperationsPerSecond = 500, Succeeded = true },
                new SqliteComparisonMeasurement { Scenario = "x", Provider = "DBreeze RKS + NoOverwrite", Round = 1, Operations = 1, ElapsedMilliseconds = 1, OperationsPerSecond = 1000, Succeeded = true },
                new SqliteComparisonMeasurement { Scenario = "x", Provider = "DBreeze Sorted", Round = 1, Operations = 1, ElapsedMilliseconds = 1.25, OperationsPerSecond = 800, Succeeded = true },
                new SqliteComparisonMeasurement { Scenario = "x", Provider = "SQLite", Round = 1, Operations = 1, ElapsedMilliseconds = 10, OperationsPerSecond = 100, Succeeded = true },
            };
            List<SqliteComparisonSummary> summaries = SqliteComparisonArtifacts.BuildSummaries(values);
            Check(failures, summaries.Single(value => value.Provider == "DBreeze").RatioVsSqlite == 2, "Ratio calculation");
            Check(failures, summaries.Single(value => value.Provider == "DBreeze RKS").RatioVsSqlite == 5, "Shared SQLite ratio calculation");
            Check(failures, summaries.Single(value => value.Provider == "DBreeze RKS + NoOverwrite").RatioVsSqlite == 10, "NoOverwrite shared SQLite ratio calculation");
            Check(failures, summaries.Single(value => value.Provider == "DBreeze Sorted").RatioVsSqlite == 8, "Sorted shared SQLite ratio calculation");
            Check(failures, summaries.Single(value => value.Provider == "DBreeze").MedianPreparationMilliseconds == 1 &&
                summaries.Single(value => value.Provider == "DBreeze").MedianMutationMilliseconds == 4, "Split timing summaries");

            long[] sortedKeys = { 4, -2, 9, 0, 4, Int64.MinValue, Int64.MaxValue };
            SqliteComparisonSuite.SortAscending(sortedKeys);
            Check(failures, sortedKeys.SequenceEqual(sortedKeys.OrderBy(static value => value)), "Ascending delete-key order");

            var canonicalMulti = new ParallelTableInsertSpec(200_000, 20, 50, 256, "FULL");
            Check(failures, Enumerable.Range(0, 20).All(table => canonicalMulti.RecordsForTable(table) == 10_000),
                "Canonical multi-table distribution");
            Check(failures, canonicalMulti.ExpectedTransactions() == 4_000,
                "Canonical multi-table transaction count");
            var unevenMulti = new ParallelTableInsertSpec(1001, 20, 50, 256, "FULL");
            Check(failures, Enumerable.Range(0, 20).Sum(unevenMulti.RecordsForTable) == 1001 &&
                unevenMulti.ExpectedTransactions() == 21, "Uneven multi-table distribution");
            byte[][] multiPayloads = ParallelTableInsertWorkload.CreatePayloadPool(256);
            long multiChecksum = ParallelTableInsertWorkload.ExpectedChecksum(canonicalMulti, multiPayloads);
            Check(failures, multiChecksum == ParallelTableInsertWorkload.ExpectedChecksum(canonicalMulti, multiPayloads),
                "Deterministic multi-table oracle");

            SqliteComparisonReport validSource = CreateAugmentationSource();
            SqliteComparisonSuite.ValidateAugmentationSource(validSource, "same-sha");
            SqliteComparisonReport duplicateSource = CreateAugmentationSource();
            duplicateSource.Measurements.Add(new SqliteComparisonMeasurement
            {
                Scenario = "Random update", Provider = "DBreeze RKS", Round = 1,
                Operations = 10, ReturnedCount = 10, Checksum = 42, Succeeded = true,
            });
            ExpectFailure(failures, "Duplicate augmentation", () =>
                SqliteComparisonSuite.ValidateAugmentationSource(duplicateSource, "same-sha"));
            ExpectFailure(failures, "Incompatible augmentation SHA", () =>
                SqliteComparisonSuite.ValidateAugmentationSource(validSource, "different-sha"));
            SqliteComparisonReport incompatibleConfiguration = CreateAugmentationSource();
            incompatibleConfiguration.Configuration.RandomSeed++;
            ExpectFailure(failures, "Incompatible augmentation configuration", () =>
                SqliteComparisonSuite.ValidateAugmentationSource(incompatibleConfiguration, "same-sha"));

            SqliteComparisonReport validNoOverwriteSource = CreateNoOverwriteAugmentationSource();
            SqliteComparisonSuite.ValidateNoOverwriteAugmentationSource(validNoOverwriteSource, "same-sha");
            SqliteComparisonReport repeatedNoOverwriteSource = CreateNoOverwriteAugmentationSource();
            repeatedNoOverwriteSource.Measurements.Add(new SqliteComparisonMeasurement
            {
                Scenario = "Random update", Provider = "DBreeze RKS + NoOverwrite", Round = 1,
                Operations = 10, ReturnedCount = 10, Checksum = 42, Succeeded = true,
            });
            ExpectFailure(failures, "Repeated NoOverwrite augmentation", () =>
                SqliteComparisonSuite.ValidateNoOverwriteAugmentationSource(repeatedNoOverwriteSource, "same-sha"));
            SqliteComparisonReport changedNoOverwriteOracle = CreateNoOverwriteAugmentationSource();
            changedNoOverwriteOracle.Measurements.Single(value =>
                value.Scenario == "Random update" && value.Provider == "DBreeze RKS" && value.Round == 2).Checksum++;
            ExpectFailure(failures, "NoOverwrite source oracle", () =>
                SqliteComparisonSuite.ValidateNoOverwriteAugmentationSource(changedNoOverwriteOracle, "same-sha"));
            SqliteComparisonReport incompleteNoOverwriteSource = CreateNoOverwriteAugmentationSource();
            incompleteNoOverwriteSource.Measurements.RemoveAt(incompleteNoOverwriteSource.Measurements.Count - 1);
            ExpectFailure(failures, "NoOverwrite source measurement count", () =>
                SqliteComparisonSuite.ValidateNoOverwriteAugmentationSource(incompleteNoOverwriteSource, "same-sha"));
            ExpectFailure(failures, "NoOverwrite incompatible SHA", () =>
                SqliteComparisonSuite.ValidateNoOverwriteAugmentationSource(validNoOverwriteSource, "different-sha"));

            SqliteComparisonReport validSortedDeleteSource = CreateSortedDeleteAugmentationSource();
            SqliteComparisonSuite.ValidateSortedDeleteAugmentationSource(validSortedDeleteSource, "same-sha");
            SqliteComparisonReport duplicateSortedDelete = CreateSortedDeleteAugmentationSource();
            duplicateSortedDelete.Measurements.Add(DeleteMeasurement("DBreeze Sorted", 1, 110));
            ExpectFailure(failures, "Repeated sorted delete augmentation", () =>
                SqliteComparisonSuite.ValidateSortedDeleteAugmentationSource(duplicateSortedDelete, "same-sha"));
            SqliteComparisonReport changedDeleteOracle = CreateSortedDeleteAugmentationSource();
            changedDeleteOracle.Measurements.Single(value =>
                value.Scenario == "Random delete" && value.Provider == "DBreeze" && value.Round == 2).Checksum++;
            ExpectFailure(failures, "Sorted delete source oracle", () =>
                SqliteComparisonSuite.ValidateSortedDeleteAugmentationSource(changedDeleteOracle, "same-sha"));

            SqliteComparisonReport validFallbackSource = CreateDeleteFallbackAugmentationSource();
            SqliteComparisonSuite.ValidateDeleteFallbackAugmentationSource(validFallbackSource, "same-sha");
            Check(failures, !SqliteComparisonSuite.SortedDeleteMeetsTarget(validFallbackSource, out _), "Fallback trigger threshold");
            SqliteComparisonReport duplicateFallback = CreateDeleteFallbackAugmentationSource();
            duplicateFallback.Measurements.Add(DeleteMeasurement("DBreeze RKS Remove", 1, 300));
            ExpectFailure(failures, "Repeated delete fallback augmentation", () =>
                SqliteComparisonSuite.ValidateDeleteFallbackAugmentationSource(duplicateFallback, "same-sha"));
            SqliteComparisonReport unnecessaryFallback = CreateDeleteFallbackAugmentationSource();
            foreach (SqliteComparisonMeasurement value in unnecessaryFallback.Measurements.Where(static value => value.Provider == "DBreeze Sorted"))
                value.OperationsPerSecond = 220;
            ExpectFailure(failures, "Unnecessary delete fallback", () =>
                SqliteComparisonSuite.ValidateDeleteFallbackAugmentationSource(unnecessaryFallback, "same-sha"));
        }
        catch (Exception exception)
        {
            failures.Add("Unexpected self-test failure: " + exception);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        if (failures.Count == 0)
        {
            Console.WriteLine("SQLite comparison self-tests: PASS");
            return 0;
        }
        foreach (string failure in failures)
            Console.Error.WriteLine("SELF-TEST FAIL: " + failure);
        return 1;
    }

    private static void Check(List<string> failures, bool condition, string name)
    {
        if (!condition)
            failures.Add(name);
    }

    private static void ExpectFailure(List<string> failures, string name, Action action)
    {
        try
        {
            action();
            failures.Add(name + " did not fail");
        }
        catch
        {
            // Expected.
        }
    }

    private static SqliteComparisonReport CreateAugmentationSource()
    {
        return new SqliteComparisonReport
        {
            Succeeded = true,
            Metadata = new SqliteComparisonMetadata { DBreezeSha256 = "same-sha" },
            Configuration = new SqliteComparisonConfiguration
            {
                Records = 1_000_000,
                PayloadBytes = 256,
                Repetitions = 3,
                Parallelism = 4,
                SqliteSynchronous = "FULL",
            },
            Measurements = new List<SqliteComparisonMeasurement>
            {
                new() { Scenario = "Random update", Provider = "DBreeze", Round = 1, Operations = 10, ReturnedCount = 10, Checksum = 42, Succeeded = true },
                new() { Scenario = "Random update", Provider = "SQLite", Round = 1, Operations = 10, ReturnedCount = 10, Checksum = 42, Succeeded = true },
                new() { Scenario = "Random update", Provider = "DBreeze", Round = 2, Operations = 10, ReturnedCount = 10, Checksum = 42, Succeeded = true },
                new() { Scenario = "Random update", Provider = "SQLite", Round = 2, Operations = 10, ReturnedCount = 10, Checksum = 42, Succeeded = true },
                new() { Scenario = "Random update", Provider = "DBreeze", Round = 3, Operations = 10, ReturnedCount = 10, Checksum = 42, Succeeded = true },
                new() { Scenario = "Random update", Provider = "SQLite", Round = 3, Operations = 10, ReturnedCount = 10, Checksum = 42, Succeeded = true },
            },
        };
    }

    private static SqliteComparisonReport CreateNoOverwriteAugmentationSource()
    {
        SqliteComparisonReport report = CreateAugmentationSource();
        for (int round = 1; round <= 3; round++)
        {
            report.Measurements.Add(new SqliteComparisonMeasurement
            {
                Scenario = "Random update", Provider = "DBreeze RKS", Round = round,
                Operations = 10, ReturnedCount = 10, Checksum = 42, Succeeded = true,
            });
        }
        while (report.Measurements.Count < 78)
        {
            int index = report.Measurements.Count;
            report.Measurements.Add(new SqliteComparisonMeasurement
            {
                Scenario = "source-fixture-" + index, Provider = "DBreeze", Round = 1,
                Operations = 1, ReturnedCount = 1, Checksum = index, Succeeded = true,
            });
        }
        for (int index = 0; index < 26; index++)
            report.Summaries.Add(new SqliteComparisonSummary { Scenario = "source-summary-" + index });
        return report;
    }

    private static SqliteComparisonReport CreateSortedDeleteAugmentationSource()
    {
        var report = new SqliteComparisonReport
        {
            Succeeded = true,
            Metadata = new SqliteComparisonMetadata { DBreezeSha256 = "same-sha" },
            Configuration = new SqliteComparisonConfiguration
            {
                Records = 1_000_000,
                PayloadBytes = 256,
                Repetitions = 3,
                Parallelism = 4,
                SqliteSynchronous = "FULL",
            },
        };
        for (int round = 1; round <= 3; round++)
        {
            report.Measurements.Add(DeleteMeasurement("DBreeze", round, 100));
            report.Measurements.Add(DeleteMeasurement("SQLite", round, 200));
        }
        FillSource(report, 81, 27);
        return report;
    }

    private static SqliteComparisonReport CreateDeleteFallbackAugmentationSource()
    {
        SqliteComparisonReport report = CreateSortedDeleteAugmentationSource();
        for (int round = 1; round <= 3; round++)
            report.Measurements.Add(DeleteMeasurement("DBreeze Sorted", round, 110));
        report.Summaries.Add(new SqliteComparisonSummary { Scenario = "Random delete", Provider = "DBreeze Sorted" });
        return report;
    }

    private static SqliteComparisonMeasurement DeleteMeasurement(string provider, int round, double operationsPerSecond) => new()
    {
        Scenario = "Random delete",
        Provider = provider,
        Round = round,
        Operations = 100_000,
        ReturnedCount = 100_000,
        Checksum = 42,
        ElapsedMilliseconds = 100_000 * 1000.0 / operationsPerSecond,
        OperationsPerSecond = operationsPerSecond,
        DatabaseBytes = 1000,
        Succeeded = true,
    };

    private static void FillSource(SqliteComparisonReport report, int measurementCount, int summaryCount)
    {
        while (report.Measurements.Count < measurementCount)
        {
            int index = report.Measurements.Count;
            report.Measurements.Add(new SqliteComparisonMeasurement
            {
                Scenario = "source-fixture-" + index,
                Provider = "DBreeze",
                Round = 1,
                Operations = 1,
                ReturnedCount = 1,
                Checksum = index,
                ElapsedMilliseconds = 1,
                OperationsPerSecond = 1,
                Succeeded = true,
            });
        }
        while (report.Summaries.Count < summaryCount)
            report.Summaries.Add(new SqliteComparisonSummary { Scenario = "source-summary-" + report.Summaries.Count });
    }
}
