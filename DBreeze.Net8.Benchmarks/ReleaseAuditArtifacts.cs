using System.Globalization;
using System.Net;
using System.Text;
using DBreeze.ReleaseAudit.Protocol;

namespace DBreeze.Net8.Benchmarks;

internal static class ReleaseAuditArtifacts
{
    internal static void Write(ReleaseAuditReport report, ReleaseAuditOptions options, AuditRunLayout layout)
    {
        string directory = Path.GetDirectoryName(options.Report) ?? throw new InvalidOperationException("HTML report needs a parent directory.");
        string timestamp = report.Metadata.CompletedUtc.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        bool firstWrite = String.IsNullOrEmpty(report.Metadata.TimestampedHtml);
        string timestamped = firstWrite
            ? Path.Combine(directory, "DBreeze_Release_Audit_" + timestamp + ".html")
            : report.Metadata.TimestampedHtml;
        report.Metadata.TimestampedHtml = timestamped;
        string html = Render(report);
        AuditPersistence.WriteJson(Path.Combine(layout.ReportsDirectory, "results.json"), report);
        AuditPersistence.WriteTextAtomic(Path.Combine(layout.ReportsDirectory, "performance.csv"), PerformanceCsv(report));
        AuditPersistence.WriteTextAtomic(Path.Combine(layout.ReportsDirectory, "coverage.csv"), CoverageCsv(report));
        if (firstWrite)
        {
            if (File.Exists(timestamped)) throw new IOException("Immutable timestamped report already exists: " + timestamped);
            AuditPersistence.WriteTextAtomic(timestamped, html);
        }
        AuditPersistence.WriteTextAtomic(options.Report, html);
    }

    internal static string Html(string value) => WebUtility.HtmlEncode(value ?? String.Empty);

    private static string Render(ReleaseAuditReport report)
    {
        string verdict = report.ReleaseVerdictIssued ? report.Passed ? "PASS" : "FAIL" : report.Incomplete ? "INCOMPLETE" : "SMOKE — NO RELEASE VERDICT";
        string verdictClass = report.Passed ? "pass" : report.ReleaseVerdictIssued || report.Incomplete ? "fail" : "warn";
        var b = new StringBuilder(256 * 1024);
        b.Append("<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>DBreeze Release Audit</title><style>")
            .Append("body{font:14px/1.45 system-ui,Segoe UI,sans-serif;margin:0;background:#0d1117;color:#d8dee9}header,main{max-width:1500px;margin:auto;padding:24px}header{background:#161b22;border-bottom:1px solid #30363d}h1{margin:.2em 0}h2{margin-top:2em;border-bottom:1px solid #30363d;padding-bottom:.3em}code,.mono{font-family:Cascadia Mono,Consolas,monospace}table{border-collapse:collapse;width:100%;font-size:12px}th,td{border:1px solid #30363d;padding:6px;vertical-align:top;text-align:left}th{position:sticky;top:0;background:#21262d}.wrap{overflow:auto;max-height:70vh}.pass{color:#3fb950}.fail{color:#f85149}.warn{color:#d29922}.muted{color:#8b949e}.badge{font-weight:700}.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(230px,1fr));gap:12px}.card{border:1px solid #30363d;background:#161b22;border-radius:8px;padding:12px}details{margin:.6em 0}a{color:#58a6ff}pre{white-space:pre-wrap;background:#161b22;padding:12px;border:1px solid #30363d}.small{font-size:11px}</style></head><body><header>")
            .Append("<div class=\"muted\">DBreeze · release gate · baseline a83424e</div><h1>Release Audit: <span class=\"").Append(verdictClass).Append("\">").Append(Html(verdict)).Append("</span></h1>")
            .Append("<p>Current <code>").Append(Html(Short(report.Metadata.CurrentCommit))).Append("</code> против baseline <code>").Append(Html(Short(report.Metadata.BaselineCommit))).Append("</code>; net8.0 + .NET Framework 4.7.2.</p></header><main>");

        Section(b, "Итог");
        b.Append("<div class=\"cards\"><div class=\"card\"><b class=\"").Append(verdictClass).Append("\">").Append(Html(verdict)).Append("</b><br>release verdict</div>")
            .Append("<div class=\"card\"><b>").Append(report.CorrectnessWorkers.Sum(static worker => worker.Coverage.Count(static value => value.Attempts != 0))).Append(" / 680</b><br>method-mode invocations</div>")
            .Append("<div class=\"card\"><b>").Append(report.Compatibility.Count(static value => value.Passed)).Append(" / ").Append(report.Compatibility.Count).Append("</b><br>file flows PASS</div>")
            .Append("<div class=\"card\"><b>").Append(report.Performance.Count(static value => value.Verdict == "PASS")).Append(" / ").Append(report.Performance.Count).Append("</b><br>performance gates PASS</div></div>");
        if (report.GateViolations.Count != 0) List(b, "Gate violations", report.GateViolations, "fail");
        if (report.Warnings.Count != 0) List(b, "Warnings", report.Warnings.Distinct(StringComparer.Ordinal), "warn");
        if (!String.IsNullOrEmpty(report.Failure)) b.Append("<details><summary class=\"fail\">Failure</summary><pre>").Append(Html(report.Failure)).Append("</pre></details>");

        Section(b, "Метод и окружение");
        b.Append("<table><tbody>");
        Row(b, "Run / profile / budget", report.Metadata.RunId + " / " + report.Metadata.Profile + " / " + report.Metadata.BudgetMinutes + " min");
        Row(b, "Started / completed UTC", report.Metadata.StartedUtc.ToString("O") + " / " + report.Metadata.CompletedUtc.ToString("O"));
        Row(b, "Baseline", report.Metadata.BaselineRepository + " @ " + report.Metadata.BaselineCommit);
        Row(b, "Current", report.Metadata.CurrentRepository + " @ " + report.Metadata.CurrentCommit + (report.Metadata.CurrentDirty ? " (dirty allowed)" : " (clean)"));
        Row(b, "Source fingerprints baseline", report.Metadata.BaselineFingerprintBefore + " → " + report.Metadata.BaselineFingerprintAfter);
        Row(b, "Source fingerprints current", report.Metadata.CurrentFingerprintBefore + " → " + report.Metadata.CurrentFingerprintAfter);
        Row(b, "Dirty fingerprint", report.Metadata.CurrentDirtyFingerprint);
        Row(b, "SDK / runtime", report.Metadata.DotNetSdk + " / " + report.Metadata.Runtime);
        Row(b, "OS / CPU / GC", report.Metadata.OperatingSystem + " / " + report.Metadata.Architecture + " / " + report.Metadata.Processor + " / logical=" + report.Metadata.LogicalProcessors + " / " + report.Metadata.Gc);
        Row(b, "Limits", "records=" + report.Metadata.MaxRecords + ", text=" + report.Metadata.MaxTextRecords + ", vectors=" + report.Metadata.MaxVectorRecords);
        Row(b, "Raw reports", report.Metadata.ReportsDirectory);
        b.Append("</tbody></table>");

        Section(b, "Prerequisites и self-tests");
        b.Append("<table><thead><tr><th>Check</th><th>Exit</th><th>Detail</th><th>Gate</th></tr></thead><tbody>");
        foreach (ReleasePrerequisite value in report.Prerequisites)
            b.Append("<tr><td><code>").Append(Html(value.Id)).Append("</code></td><td>").Append(value.ExitCode).Append("</td><td>").Append(Html(value.Detail)).Append("</td><td class=\"").Append(value.Passed ? "pass" : "fail").Append("\">").Append(value.Passed ? "PASS" : "FAIL").Append("</td></tr>");
        b.Append("</tbody></table>");

        Section(b, "Изолированные сборки");
        b.Append("<table><thead><tr><th>Variant</th><th>Framework</th><th>DBreeze.dll SHA-256</th><th>Worker SHA-256</th><th>Warnings</th><th>Paths</th></tr></thead><tbody>");
        foreach (ReleaseBuild build in report.Builds.OrderBy(static value => value.Key, StringComparer.Ordinal))
            b.Append("<tr><td>").Append(Html(build.Variant)).Append("</td><td>").Append(Html(build.Framework)).Append("</td><td class=\"mono\">").Append(Html(build.LibrarySha256)).Append("</td><td class=\"mono\">").Append(Html(build.WorkerSha256)).Append("</td><td>").Append(build.WarningCount).Append("</td><td class=\"small mono\">").Append(Html(build.Library)).Append("<br>").Append(Html(build.Worker)).Append("</td></tr>");
        b.Append("</tbody></table>");

        Section(b, "API compatibility");
        b.Append("<table><thead><tr><th>Framework</th><th>Scope</th><th>Baseline</th><th>Current</th><th>Missing</th><th>Added</th><th>Gate</th></tr></thead><tbody>");
        foreach (ReleaseApiDelta value in report.ApiDeltas)
            b.Append("<tr><td>").Append(Html(value.Framework)).Append("</td><td>").Append(Html(value.Scope)).Append("</td><td>").Append(value.BaselineCount).Append("</td><td>").Append(value.CurrentCount).Append("</td><td>").Append(value.Missing.Count).Append("</td><td>").Append(value.Added.Count).Append("</td><td class=\"").Append(value.Passed ? "pass" : "fail").Append("\">").Append(value.Passed ? "PASS" : "FAIL").Append("</td></tr>");
        b.Append("</tbody></table>");
        foreach (ReleaseApiDelta value in report.ApiDeltas.Where(static value => value.Missing.Count + value.Added.Count != 0))
        {
            if (value.Missing.Count != 0) Details(b, value.Framework + "/" + value.Scope + " missing", value.Missing, "fail");
            if (value.Added.Count != 0) Details(b, value.Framework + "/" + value.Scope + " additions", value.Added, "warn");
        }

        Section(b, "85-method coverage matrix");
        RenderCoverage(b, report);

        Section(b, "Correctness и concurrency");
        b.Append("<table><thead><tr><th>Worker</th><th>Case</th><th>Mode</th><th>Semantic</th><th>Time</th><th>Gate</th></tr></thead><tbody>");
        foreach (WorkerReport worker in report.CorrectnessWorkers.OrderBy(static value => value.Variant).ThenBy(static value => value.Framework))
        foreach (CaseResult item in worker.Cases)
            b.Append("<tr><td>").Append(Html(worker.Variant + "-" + worker.Framework)).Append("</td><td><code>").Append(Html(item.Id)).Append("</code></td><td>").Append(Html(item.Mode)).Append("</td><td class=\"mono small\">").Append(Html(item.SemanticValue)).Append("</td><td>").Append(item.ElapsedMilliseconds).Append(" ms</td><td class=\"").Append(item.Succeeded ? "pass" : "fail").Append("\">").Append(item.Succeeded ? "PASS" : "FAIL").Append("</td></tr>");
        b.Append("</tbody></table>");
        foreach (ReleaseCorrectnessDelta delta in report.CorrectnessDeltas)
            b.Append("<p class=\"").Append(delta.Verdict == "ACCEPTED" ? "warn" : "fail").Append("\"><b>").Append(Html(delta.Verdict)).Append("</b> ").Append(Html(delta.Framework + "/" + delta.Case)).Append(": <code>").Append(Html(delta.Baseline)).Append("</code> → <code>").Append(Html(delta.Current)).Append("</code>; ").Append(Html(delta.Policy)).Append("</p>");

        Section(b, "File protocol: 4×4, mutable, backup, journal");
        b.Append("<div class=\"wrap\"><table><thead><tr><th>Flow</th><th>Kind</th><th>Producer</th><th>Consumer</th><th>Semantic oracle</th><th>Gate</th><th>DB</th></tr></thead><tbody>");
        foreach (ReleaseCompatibilityFlow flow in report.Compatibility)
            b.Append("<tr><td><code>").Append(Html(flow.Id)).Append("</code></td><td>").Append(Html(flow.Kind)).Append("</td><td>").Append(Html(flow.Producer)).Append("</td><td>").Append(Html(flow.Consumer)).Append("</td><td class=\"mono small\">").Append(Html(flow.Semantic)).Append("</td><td class=\"").Append(flow.Passed ? "pass" : "fail").Append("\">").Append(flow.Passed ? "PASS" : "FAIL").Append("</td><td class=\"mono small\">").Append(Html(flow.DatabasePath)).Append("</td></tr>");
        b.Append("</tbody></table></div>");

        Section(b, "Speed и allocations");
        b.Append("<div class=\"wrap\"><table><thead><tr><th>Framework</th><th>Category / scenario</th><th>W</th><th>Pairs</th><th>Baseline ms</th><th>Current ms</th><th>Δ speed</th><th>Worse pairs</th><th>Baseline B/op</th><th>Current B/op</th><th>Δ alloc</th><th>Verdict</th></tr></thead><tbody>");
        foreach (ReleasePerformanceComparison value in report.Performance)
        {
            string css = value.Verdict == "PASS" ? "pass" : value.Verdict == "INCOMPLETE" ? "warn" : "fail";
            b.Append("<tr><td>").Append(Html(value.Framework)).Append("</td><td>").Append(Html(value.Category)).Append(" / <code>").Append(Html(value.Scenario)).Append("</code></td><td>").Append(value.Workers).Append("</td><td>").Append(value.PairCount).Append(value.Confirmed ? " confirmed" : String.Empty).Append("</td><td>").Append(Number(value.BaselineMedianMilliseconds)).Append("</td><td>").Append(Number(value.CurrentMedianMilliseconds)).Append("</td><td>").Append(Percent(value.SpeedDeltaPercent)).Append("</td><td>").Append(value.WorseSpeedPairs).Append("</td><td>").Append(Number(value.BaselineBytesPerOperation)).Append("</td><td>").Append(Number(value.CurrentBytesPerOperation)).Append("</td><td>").Append(Percent(value.AllocationDeltaPercent)).Append("</td><td class=\"").Append(css).Append("\">").Append(Html(value.Verdict)).Append("</td></tr>");
        }
        b.Append("</tbody></table></div><p class=\"muted\">Speed FAIL: median &gt;5%, absolute &gt;1 ms and at least 3/5 confirmed pairs. Allocation FAIL: median &gt;5% and &gt;1 B/op; background counters additionally use 64 KiB noise floor.</p>");
        b.Append("<details><summary>Raw measurement counters (informational except elapsed/allocation gate inputs)</summary><div class=\"wrap\"><table><thead><tr><th>Worker / round</th><th>Scenario</th><th>Ops</th><th>ms</th><th>Thread/workers allocated</th><th>Process/AppDomain allocated</th><th>GC 0/1/2</th><th>Live heap</th><th>Peak private</th><th>DB bytes</th><th>Checksum</th></tr></thead><tbody>");
        foreach (ReleasePerformanceSample sample in report.PerformanceSamples.OrderBy(static value => value.Framework).ThenBy(static value => value.Value.Scenario).ThenBy(static value => value.Value.Round).ThenBy(static value => value.Variant))
        {
            Measurement value = sample.Value;
            b.Append("<tr><td>").Append(Html(sample.Variant + "-" + sample.Framework + " / " + value.Round)).Append("</td><td><code>").Append(Html(value.Scenario)).Append("</code></td><td>").Append(value.Operations).Append("</td><td>").Append(Number(value.ElapsedMilliseconds)).Append("</td><td>").Append(value.AllocatedBytes).Append("</td><td>").Append(value.ProcessAllocatedBytes).Append("</td><td>").Append(value.Gen0Collections).Append('/').Append(value.Gen1Collections).Append('/').Append(value.Gen2Collections).Append("</td><td>").Append(value.LiveHeapBytes).Append("</td><td>").Append(value.PeakPrivateBytes).Append("</td><td>").Append(value.DatabaseBytes).Append("</td><td class=\"mono\">").Append(Html(value.Checksum)).Append("</td></tr>");
        }
        b.Append("</tbody></table></div></details>");

        Section(b, "Воспроизведение");
        b.Append("<pre>").Append(Html(report.Metadata.ReproductionCommand)).Append("</pre><table><tbody>");
        Row(b, "Canonical latest", report.Metadata.CanonicalHtml); Row(b, "Immutable copy", report.Metadata.TimestampedHtml); Row(b, "Raw JSON/CSV/log", report.Metadata.ReportsDirectory); Row(b, "Scratch", report.Metadata.ScratchDirectory + " (owner-marker cleanup=" + (!report.Metadata.Profile.Equals("keep", StringComparison.OrdinalIgnoreCase)) + ")");
        b.Append("</tbody></table></main></body></html>");
        return b.ToString();
    }

    private static void RenderCoverage(StringBuilder b, ReleaseAuditReport report)
    {
        string[] workers = { "baseline-net8", "current-net8", "baseline-net472", "current-net472" };
        var maps = report.CorrectnessWorkers.ToDictionary(static worker => worker.Variant + "-" + worker.Framework,
            static worker => worker.Coverage.ToDictionary(static entry => entry.MemberId + "\n" + entry.Mode, StringComparer.Ordinal), StringComparer.Ordinal);
        string[] methods = report.CorrectnessWorkers.SelectMany(static worker => worker.Coverage.Select(static entry => entry.MemberId)).Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        b.Append("<div class=\"wrap\"><table><thead><tr><th rowspan=\"2\">Canonical member ID</th>");
        foreach (string worker in workers) b.Append("<th colspan=\"2\">").Append(Html(worker)).Append("</th>");
        b.Append("</tr><tr>"); foreach (string worker in workers) b.Append("<th>single</th><th>parallel</th>"); b.Append("</tr></thead><tbody>");
        foreach (string method in methods)
        {
            b.Append("<tr><td class=\"mono small\">").Append(Html(method)).Append("</td>");
            foreach (string worker in workers)
            foreach (string mode in new[] { "single", "parallel" })
            {
                maps.TryGetValue(worker, out Dictionary<string, CoverageEntry> map);
                CoverageEntry entry = null;
                if (map != null) map.TryGetValue(method + "\n" + mode, out entry);
                string text = entry == null || entry.Attempts == 0 ? "MISSING" : entry.Successes == 0 ? "EXEC/FAIL" : "PASS " + entry.Successes + "/" + entry.Attempts;
                string css = entry == null || entry.Attempts == 0 || entry.Successes == 0 ? "fail" : "pass";
                b.Append("<td class=\"").Append(css).Append("\">").Append(text).Append("</td>");
            }
            b.Append("</tr>");
        }
        b.Append("</tbody></table></div>");
    }

    private static string PerformanceCsv(ReleaseAuditReport report)
    {
        var b = new StringBuilder("framework,variant,category,scenario,workers,round,operations,elapsed_ms,allocated_bytes,process_allocated_bytes,b_per_op,gen0,gen1,gen2,live_heap,peak_private,db_bytes,checksum\r\n");
        foreach (ReleasePerformanceSample sample in report.PerformanceSamples)
        {
            Measurement v = sample.Value;
            b.Append(Csv(sample.Framework)).Append(',').Append(Csv(sample.Variant)).Append(',').Append(Csv(v.Category)).Append(',').Append(Csv(v.Scenario)).Append(',')
                .Append(v.Workers).Append(',').Append(v.Round).Append(',').Append(v.Operations).Append(',').Append(v.ElapsedMilliseconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(v.AllocatedBytes).Append(',').Append(v.ProcessAllocatedBytes).Append(',').Append((v.AllocatedBytes / (double)Math.Max(1, v.Operations)).ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(v.Gen0Collections).Append(',').Append(v.Gen1Collections).Append(',').Append(v.Gen2Collections).Append(',').Append(v.LiveHeapBytes).Append(',').Append(v.PeakPrivateBytes).Append(',').Append(v.DatabaseBytes).Append(',').Append(Csv(v.Checksum)).Append("\r\n");
        }
        return b.ToString();
    }

    private static string CoverageCsv(ReleaseAuditReport report)
    {
        var b = new StringBuilder("variant,framework,mode,attempts,successes,member_id,evidence\r\n");
        foreach (WorkerReport worker in report.CorrectnessWorkers)
        foreach (CoverageEntry entry in worker.Coverage)
            b.Append(Csv(worker.Variant)).Append(',').Append(Csv(worker.Framework)).Append(',').Append(Csv(entry.Mode)).Append(',').Append(entry.Attempts).Append(',').Append(entry.Successes).Append(',').Append(Csv(entry.MemberId)).Append(',').Append(Csv(entry.Evidence)).Append("\r\n");
        return b.ToString();
    }

    private static void Section(StringBuilder b, string title) => b.Append("<h2>").Append(Html(title)).Append("</h2>");
    private static void Row(StringBuilder b, string name, string value) => b.Append("<tr><th>").Append(Html(name)).Append("</th><td class=\"mono\">").Append(Html(value)).Append("</td></tr>");
    private static void List(StringBuilder b, string title, IEnumerable<string> values, string css) { b.Append("<div class=\"").Append(css).Append("\"><b>").Append(Html(title)).Append("</b><ul>"); foreach (string value in values) b.Append("<li>").Append(Html(value)).Append("</li>"); b.Append("</ul></div>"); }
    private static void Details(StringBuilder b, string title, IEnumerable<string> values, string css) { b.Append("<details><summary class=\"").Append(css).Append("\">").Append(Html(title)).Append("</summary><ul class=\"mono small\">"); foreach (string value in values) b.Append("<li>").Append(Html(value)).Append("</li>"); b.Append("</ul></details>"); }
    private static string Short(string value) => String.IsNullOrEmpty(value) ? String.Empty : value.Substring(0, Math.Min(12, value.Length));
    private static string Number(double value) => Double.IsNaN(value) ? "n/a" : value.ToString("N3", CultureInfo.InvariantCulture);
    private static string Percent(double value) => Double.IsNaN(value) ? "n/a" : value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "%";
    private static string Csv(string value) => "\"" + (value ?? String.Empty).Replace("\"", "\"\"") + "\"";
}
