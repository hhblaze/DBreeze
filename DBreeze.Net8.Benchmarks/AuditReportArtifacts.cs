using System.Globalization;
using System.Net;
using System.Text;

namespace DBreeze.Net8.Benchmarks;

internal static class AuditReportArtifacts
{
    internal static void WritePerformanceCsv(string path, AuditRunReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Category,Scenario,Workers,Records,Operations,OldMedianMs,NewMedianMs,OldMinMs,NewMinMs,OldMaxMs,NewMaxMs,OldOpsPerSec,NewOpsPerSec,OldScalingEfficiency,NewScalingEfficiency,Speedup,TimeDeltaPercent,OldAllocatedBPerOp,NewAllocatedBPerOp,OldTotalAllocatedBytes,NewTotalAllocatedBytes,AllocatedDeltaPercent,OldGen0,NewGen0,OldGen1,NewGen1,OldGen2,NewGen2,OldDatabaseBytes,NewDatabaseBytes,Pairs,RegressedPairs,Confirmation,SpeedGate,AllocationGate,Verdict");
        foreach (AuditPerformanceComparison item in report.Performance)
        {
            Csv(builder, item.Category);
            Csv(builder, item.Scenario);
            Csv(builder, item.Workers);
            Csv(builder, item.Records);
            Csv(builder, item.Operations);
            Csv(builder, item.BaselineMedianMilliseconds);
            Csv(builder, item.CurrentMedianMilliseconds);
            Csv(builder, item.BaselineMinMilliseconds);
            Csv(builder, item.CurrentMinMilliseconds);
            Csv(builder, item.BaselineMaxMilliseconds);
            Csv(builder, item.CurrentMaxMilliseconds);
            Csv(builder, item.BaselineMedianOperationsPerSecond);
            Csv(builder, item.CurrentMedianOperationsPerSecond);
            Csv(builder, item.BaselineScalingEfficiency);
            Csv(builder, item.CurrentScalingEfficiency);
            Csv(builder, item.Speedup);
            Csv(builder, item.TimeDeltaPercent);
            Csv(builder, item.BaselineMedianAllocatedBytesPerOperation);
            Csv(builder, item.CurrentMedianAllocatedBytesPerOperation);
            Csv(builder, item.BaselineMedianAllocatedBytes);
            Csv(builder, item.CurrentMedianAllocatedBytes);
            Csv(builder, item.AllocatedDeltaPercent);
            Csv(builder, item.BaselineMedianGen0Collections);
            Csv(builder, item.CurrentMedianGen0Collections);
            Csv(builder, item.BaselineMedianGen1Collections);
            Csv(builder, item.CurrentMedianGen1Collections);
            Csv(builder, item.BaselineMedianGen2Collections);
            Csv(builder, item.CurrentMedianGen2Collections);
            Csv(builder, item.BaselineMedianDatabaseBytes);
            Csv(builder, item.CurrentMedianDatabaseBytes);
            Csv(builder, item.PairCount);
            Csv(builder, item.SpeedRegressionPairCount);
            Csv(builder, item.ConfirmationRun);
            Csv(builder, item.SpeedGatePassed);
            Csv(builder, item.AllocationGatePassed);
            Csv(builder, item.Verdict, last: true);
            builder.AppendLine();
        }
        AuditPersistence.WriteTextAtomic(path, builder.ToString());
    }

    private static void Csv(StringBuilder builder, object value, bool last = false)
    {
        string text = value switch
        {
            null => String.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
        builder.Append('"').Append(text.Replace("\"", "\"\"")).Append('"');
        if (!last)
            builder.Append(',');
    }
}

internal static class AuditHtmlReportWriter
{
    internal static void Write(string path, AuditRunReport report)
    {
        var html = new StringBuilder(128 * 1024);
        string verdict = report.Passed ? "PASS" : "FAIL";
        string verdictClass = report.Passed ? "good" : "danger";
        AuditRunMetadata metadata = report.Metadata ?? new AuditRunMetadata();
        html.Append("""
<!doctype html>
<html lang="ru">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <title>DBreeze — .NET 8 benchmark audit</title>
  <style>
    :root{color-scheme:light;--bg:#f2f5f7;--surface:#fff;--surface2:#edf2f4;--surface3:#e2eaed;--text:#17242b;--muted:#5d6d75;--line:#d3dee2;--accent:#006d77;--accent2:#0a9396;--good:#18794e;--warn:#a15c00;--danger:#b42318;--info:#175cd3;--violet:#6941c6;--shadow:0 12px 32px rgba(24,36,43,.08);--radius:14px;--mono:ui-monospace,SFMono-Regular,Consolas,"Liberation Mono",monospace}
    html[data-theme="dark"]{color-scheme:dark;--bg:#101719;--surface:#172125;--surface2:#202d32;--surface3:#29383e;--text:#e9f1f3;--muted:#a5b4ba;--line:#34464d;--accent:#64cdd1;--accent2:#8de0e2;--good:#65d6a2;--warn:#ffbd66;--danger:#ff8f87;--info:#84adff;--violet:#c7a7ff;--shadow:0 12px 32px rgba(0,0,0,.28)}
    @media(prefers-color-scheme:dark){html:not([data-theme="light"]){color-scheme:dark;--bg:#101719;--surface:#172125;--surface2:#202d32;--surface3:#29383e;--text:#e9f1f3;--muted:#a5b4ba;--line:#34464d;--accent:#64cdd1;--accent2:#8de0e2;--good:#65d6a2;--warn:#ffbd66;--danger:#ff8f87;--info:#84adff;--violet:#c7a7ff;--shadow:0 12px 32px rgba(0,0,0,.28)}}
    *{box-sizing:border-box}html{scroll-behavior:smooth}body{margin:0;background:var(--bg);color:var(--text);font:15px/1.55 Inter,Segoe UI,Arial,sans-serif}a{color:var(--accent);text-decoration:none}a:hover{text-decoration:underline}code{font-family:var(--mono);font-size:.9em;background:var(--surface2);padding:.12rem .35rem;border-radius:5px;overflow-wrap:anywhere}.hero{padding:4rem max(1.2rem,calc((100vw - 1500px)/2));color:#fff;background:linear-gradient(125deg,#004e57,#006d77 52%,#0a9396);position:relative}.hero h1{font-size:clamp(2rem,4vw,4rem);line-height:1.08;margin:.4rem 0 1rem;max-width:1200px}.hero code{background:rgba(255,255,255,.12)}.eyebrow{text-transform:uppercase;letter-spacing:.15em;font-size:.78rem;font-weight:700}.meta{display:flex;flex-wrap:wrap;gap:.5rem}.meta span{border:1px solid rgba(255,255,255,.35);border-radius:999px;padding:.3rem .7rem}.layout{max-width:1600px;margin:auto;padding:1.2rem;display:grid;grid-template-columns:250px minmax(0,1fr);gap:1.2rem}nav{position:sticky;top:1rem;align-self:start;max-height:calc(100vh - 2rem);overflow:auto;background:var(--surface);border:1px solid var(--line);border-radius:var(--radius);padding:1rem;box-shadow:var(--shadow)}nav a{display:block;padding:.38rem .5rem;border-radius:7px}nav a:hover{background:var(--surface2);text-decoration:none}main{min-width:0}section{background:var(--surface);border:1px solid var(--line);border-radius:var(--radius);padding:clamp(1rem,2.5vw,2rem);margin-bottom:1.2rem;box-shadow:var(--shadow)}h2{font-size:1.6rem;margin:0 0 1rem}h3{margin:.2rem 0 .5rem}.lead{font-size:1.08rem}.stats{display:grid;grid-template-columns:repeat(6,minmax(0,1fr));gap:.8rem;margin:1.2rem 0}.stat,.card{background:var(--surface2);border:1px solid var(--line);border-radius:12px;padding:1rem}.stat b{display:block;font-size:1.65rem}.stat span{color:var(--muted);font-size:.86rem}.grid2,.grid3{display:grid;gap:.8rem}.grid2{grid-template-columns:repeat(2,minmax(0,1fr))}.grid3{grid-template-columns:repeat(3,minmax(0,1fr))}.badge{display:inline-flex;align-items:center;border-radius:999px;padding:.18rem .55rem;font-size:.75rem;font-weight:700;background:var(--surface3)}.badge.good,.good{color:var(--good)}.badge.warn,.warn{color:var(--warn)}.badge.danger,.danger{color:var(--danger)}.badge.info,.info{color:var(--info)}.callout{border-left:4px solid var(--info);background:var(--surface2);padding:.8rem 1rem;border-radius:8px;margin:1rem 0}.callout.danger{border-color:var(--danger)}.callout.warn{border-color:var(--warn)}.table-wrap{overflow:auto;border:1px solid var(--line);border-radius:12px}table{border-collapse:collapse;width:100%;min-width:900px;font-size:.86rem}th,td{text-align:left;padding:.62rem .7rem;border-bottom:1px solid var(--line);vertical-align:top}th{position:sticky;top:0;background:var(--surface2);z-index:1}tbody tr:hover{background:color-mix(in srgb,var(--accent) 7%,transparent)}.num{text-align:right;font-variant-numeric:tabular-nums}.filters{display:flex;gap:.6rem;flex-wrap:wrap;margin:0 0 .8rem}.filters input,.filters select{background:var(--surface2);color:var(--text);border:1px solid var(--line);border-radius:8px;padding:.55rem .7rem}.filters input{min-width:310px}.bar-track{min-width:100px;height:7px;background:var(--surface3);border-radius:99px;overflow:hidden;margin-top:.3rem}.bar{height:100%;background:var(--accent2);border-radius:99px}.bar.regress{background:var(--danger)}.muted{color:var(--muted)}.small{font-size:.84rem}.mono{font-family:var(--mono)}button{border:1px solid rgba(255,255,255,.45);background:rgba(255,255,255,.12);color:#fff;border-radius:8px;padding:.5rem .8rem;cursor:pointer;position:absolute;right:1.2rem;top:1.2rem}footer{max-width:1500px;margin:auto;padding:0 1.2rem 2rem;color:var(--muted)}
    @media(max-width:1300px){.layout{grid-template-columns:1fr}nav{position:relative;top:0;max-height:none}.stats{grid-template-columns:repeat(3,1fr)}.grid3{grid-template-columns:repeat(2,1fr)}}@media(max-width:760px){.hero{padding:2.6rem 1rem}.layout{padding:.75rem}.stats,.grid2,.grid3{grid-template-columns:1fr}.filters input{min-width:0;width:100%}}@media print{nav,.filters,#theme-toggle{display:none!important}.layout{display:block;width:100%;padding:0}.hero{color:#111;background:#fff;padding:1cm 0}section{break-inside:avoid;box-shadow:none}.table-wrap{overflow:visible}table{min-width:0;font-size:7.5pt}}
  </style>
</head>
<body>
""");
        html.Append("<header class=\"hero\"><button id=\"theme-toggle\" type=\"button\">Тема</button>")
            .Append("<div class=\"eyebrow\">DBreeze · .NET 8 · old-vs-new benchmark audit</div><h1>")
            .Append("Рефакторинг против <code>").Append(E(Short(metadata.BaselineCommit))).Append("</code>: ")
            .Append("<span class=\"").Append(verdictClass).Append("\">").Append(verdict).Append("</span></h1>")
            .Append("<p class=\"lead\">Скорость, allocations, public non-vector API и двусторонняя совместимость файлов.</p><div class=\"meta\">")
            .Append(Meta("run", metadata.RunId)).Append(Meta("profile", metadata.Profile))
            .Append(Meta("max rows", metadata.MaxRecords.ToString("N0", CultureInfo.InvariantCulture)))
            .Append(Meta("current", Short(metadata.CurrentCommit))).Append(Meta("runtime", metadata.Runtime))
            .Append("</div></header>");

        html.Append("<div class=\"layout\"><nav><strong>Навигация</strong>")
            .Append(Nav("verdict", "Итог")).Append(Nav("method", "Метод и окружение"))
            .Append(Nav("api", "API coverage")).Append(Nav("correctness", "Correctness"))
            .Append(Nav("compatibility", "File compatibility")).Append(Nav("performance", "Performance"))
            .Append(Nav("parallel", "Parallel scaling")).Append(Nav("allocations", "Allocations"))
            .Append(Nav("reproduce", "Воспроизведение")).Append("</nav><main>");

        int performanceFails = report.Performance.Count(static item => !item.SpeedGatePassed || !item.AllocationGatePassed);
        html.Append("<section id=\"verdict\"><h2>Итог аудита</h2><p class=\"lead\"><strong class=\"")
            .Append(verdictClass).Append("\">").Append(verdict).Append(".</strong> ")
            .Append(report.Passed
                ? "Обязательные API, correctness, format, speed и allocation gates прошли."
                : "Есть нарушения обязательных gates; подробности перечислены ниже.")
            .Append("</p><div class=\"stats\">")
            .Append(Stat(report.ApiComparison?.CurrentRecordCount ?? 0, "current API records",
                report.ApiComparison?.BackwardCompatible == true ? "good" : "danger"))
            .Append(Stat(report.ApiComparison?.UnmappedRecordCount ?? 0, "unmapped API records",
                report.ApiComparison?.UnmappedRecordCount == 0 ? "good" : "danger"))
            .Append(Stat(report.CorrectnessComparison?.Deltas.Count ?? 0, "behavior deltas",
                report.CorrectnessComparison?.Passed == true ? "good" : "danger"))
            .Append(Stat(report.Compatibility?.Steps.Count(static step => step.Passed) ?? 0, "compatibility PASS",
                report.Compatibility?.Passed == true ? "good" : "danger"))
            .Append(Stat(report.Performance.Count, "perf scenarios", "info"))
            .Append(Stat(performanceFails, "perf/alloc FAIL", performanceFails == 0 ? "good" : "danger"))
            .Append("</div>");
        if (report.GateViolations.Count != 0)
        {
            html.Append("<div class=\"callout danger\"><strong>Gate violations</strong><ul>");
            foreach (string violation in report.GateViolations)
                html.Append("<li>").Append(E(violation)).Append("</li>");
            html.Append("</ul></div>");
        }
        if (report.Warnings.Count != 0)
        {
            html.Append("<div class=\"callout warn\"><strong>Warnings</strong><ul>");
            foreach (string warning in report.Warnings)
                html.Append("<li>").Append(E(warning)).Append("</li>");
            html.Append("</ul></div>");
        }
        if (!String.IsNullOrEmpty(report.Failure))
            html.Append("<details><summary>Suite failure</summary><pre class=\"mono\">").Append(E(report.Failure)).Append("</pre></details>");
        html.Append("</section>");

        html.Append("<section id=\"method\"><h2>Метод и окружение</h2><div class=\"grid2\">")
            .Append(Card("Baseline", metadata.BaselineRepository, metadata.BaselineCommit,
                metadata.BaselineAssemblySha256))
            .Append(Card("Current", metadata.CurrentRepository, metadata.CurrentCommit,
                metadata.CurrentAssemblySha256))
            .Append("</div><div class=\"table-wrap\"><table><tbody>")
            .Append(Row("Started / completed UTC", O(metadata.StartedUtc) + " / " + O(metadata.CompletedUtc)))
            .Append(Row("SDK / runtime", metadata.DotNetSdk + " / " + metadata.Runtime))
            .Append(Row("OS / architecture", metadata.OperatingSystem + " / " + metadata.Architecture))
            .Append(Row("CPU", metadata.ProcessorIdentifier + "; logical processors: " + metadata.LogicalProcessorCount))
            .Append(Row("GC", (metadata.ServerGc ? "Server" : "Workstation") + "; " + metadata.GcLatencyMode))
            .Append(Row("Benchmark sources SHA-256", metadata.BenchmarkSourceSha256))
            .Append(Row("Current source diff", metadata.CurrentSourceDirty
                ? "dirty; SHA-256 " + metadata.CurrentSourceDiffSha256 : "clean in library source paths"))
            .Append(Row("Run order", String.Join(", ", metadata.VariantOrder)))
            .Append("</tbody></table></div><p class=\"muted small\">Каждый process выполнял собственный warmup. Основные rounds чередовались old/new; кандидаты &gt;5% или allocation guard получили два confirmation rounds.</p></section>");

        AppendApi(html, report);
        AppendCorrectness(html, report);
        AppendCompatibility(html, report);
        AppendPerformance(html, report);
        AppendParallel(html, report);
        AppendAllocations(html, report);

        html.Append("<section id=\"reproduce\"><h2>Воспроизведение и raw reports</h2>")
            .Append("<pre class=\"mono\">").Append(E("dotnet run --project DBreeze.Net8.Benchmarks -c Release -- --compare-all --profile " +
                metadata.Profile + " --baseline-repo \"" + metadata.BaselineRepository + "\" --expected-baseline " +
                metadata.BaselineCommit + " --root \"" + Path.GetDirectoryName(Path.GetDirectoryName(metadata.ReportsDirectory ?? String.Empty)) +
                "\" --max-records " + metadata.MaxRecords + " --report \"" + metadata.HtmlReportPath + "\""))
            .Append("</pre><div class=\"table-wrap\"><table><tbody>")
            .Append(Row("Raw reports", metadata.ReportsDirectory))
            .Append(Row("Canonical HTML", metadata.HtmlReportPath))
            .Append(Row("Scratch cleanup", metadata.KeepDatabases ? "disabled (--keep-databases)" : "enabled; reports-only"))
            .Append("</tbody></table></div></section>");

        html.Append("</main></div><footer>Generated by DBreeze.Net8.Benchmarks · self-contained report · no external assets</footer>")
            .Append("""
<script>
(() => {
  const root=document.documentElement,button=document.getElementById('theme-toggle');
  button.addEventListener('click',()=>{const next=root.dataset.theme==='dark'?'light':'dark';root.dataset.theme=next;localStorage.setItem('dbreeze-audit-theme',next)});
  const saved=localStorage.getItem('dbreeze-audit-theme');if(saved)root.dataset.theme=saved;
  const query=document.getElementById('perf-query'),gate=document.getElementById('perf-gate');
  const apply=()=>{const q=(query?.value||'').toLowerCase(),g=gate?.value||'all';document.querySelectorAll('#perf-table tbody tr').forEach(row=>{const matchesText=row.textContent.toLowerCase().includes(q),matchesGate=g==='all'||row.dataset.gate===g;row.hidden=!(matchesText&&matchesGate)})};
  query?.addEventListener('input',apply);gate?.addEventListener('change',apply);
})();
</script>
</body></html>
""");
        AuditPersistence.WriteTextAtomic(path, html.ToString());
    }

    private static void AppendApi(StringBuilder html, AuditRunReport report)
    {
        AuditApiComparison api = report.ApiComparison;
        html.Append("<section id=\"api\"><h2>Public non-vector API coverage</h2>");
        if (api == null) { html.Append("<p class=\"danger\">API comparison unavailable.</p></section>"); return; }
        html.Append("<div class=\"grid3\">")
            .Append(CardSimple("Baseline records", api.BaselineRecordCount.ToString("N0")))
            .Append(CardSimple("Current records", api.CurrentRecordCount.ToString("N0")))
            .Append(CardSimple("Mapped", $"{api.MappedRecordCount:N0} / {api.CurrentRecordCount:N0}"))
            .Append("</div><p><span class=\"badge ").Append(api.BackwardCompatible ? "good\">backward compatible" : "danger\">breaking delta")
            .Append("</span> <span class=\"badge ").Append(api.CompleteCoverage ? "good\">100% mapped" : "danger\">unmapped records")
            .Append("</span></p>");
        AppendDetailsList(html, "Missing baseline records", api.MissingRecords, "danger");
        AppendDetailsList(html, "Additive current records", api.AddedRecords, "info");
        AppendDetailsList(html, "Unmapped records", api.UnmappedRecords, "danger");
        html.Append("</section>");
    }

    private static void AppendCorrectness(StringBuilder html, AuditRunReport report)
    {
        html.Append("<section id=\"correctness\"><h2>Correctness contracts</h2><div class=\"table-wrap\"><table><thead><tr><th>Scenario</th><th>Contract</th><th>Old</th><th>New</th><th>Gate</th></tr></thead><tbody>");
        var oldItems = report.BaselineCorrectness?.Scenarios.ToDictionary(static item => item.Id) ?? new();
        var newItems = report.CurrentCorrectness?.Scenarios.ToDictionary(static item => item.Id) ?? new();
        foreach (string id in oldItems.Keys.Union(newItems.Keys).OrderBy(static value => value, StringComparer.Ordinal))
        {
            oldItems.TryGetValue(id, out AuditCorrectnessScenario oldItem);
            newItems.TryGetValue(id, out AuditCorrectnessScenario newItem);
            bool equal = oldItem?.Succeeded == true && newItem?.Succeeded == true && oldItem.Count == newItem.Count && oldItem.Checksum == newItem.Checksum;
            AuditCorrectnessDelta delta = report.CorrectnessComparison?.Deltas
                .FirstOrDefault(item => String.Equals(item.Scenario, id, StringComparison.Ordinal));
            string gateBadge = equal ? "good\">PASS" : delta?.Accepted == true ? "warn\">ACCEPTED" : "danger\">DELTA";
            html.Append("<tr><td><code>").Append(E(id)).Append("</code></td><td>").Append(E(newItem?.Contract ?? oldItem?.Contract))
                .Append("</td><td class=\"mono\">").Append(E(CorrectnessValue(oldItem))).Append("</td><td class=\"mono\">")
                .Append(E(CorrectnessValue(newItem))).Append("</td><td><span class=\"badge ")
                .Append(gateBadge).Append("</span></td></tr>");
        }
        html.Append("</tbody></table></div>");
        if (report.CorrectnessComparison?.Deltas.Count > 0)
        {
            html.Append("<div class=\"callout warn\"><strong>Behavior deltas</strong><ul>");
            foreach (AuditCorrectnessDelta delta in report.CorrectnessComparison.Deltas)
                html.Append("<li><code>").Append(E(delta.Scenario)).Append("</code>: ").Append(E(delta.Baseline))
                    .Append(" → ").Append(E(delta.Current)).Append("; policy ").Append(E(delta.Policy)).Append("</li>");
            html.Append("</ul></div>");
        }
        html.Append("</section>");
    }

    private static void AppendCompatibility(StringBuilder html, AuditRunReport report)
    {
        html.Append("<section id=\"compatibility\"><h2>Двусторонняя file compatibility</h2><div class=\"table-wrap\"><table><thead><tr><th>Flow</th><th>Producer → consumer</th><th>Action</th><th>Rows</th><th>Checksum</th><th>Read-only bytes</th><th>Gate</th></tr></thead><tbody>");
        foreach (AuditCompatibilityStep step in report.Compatibility?.Steps ?? new())
        {
            html.Append("<tr><td><code>").Append(E(step.Id)).Append("</code></td><td>").Append(E(step.Producer))
                .Append(" → ").Append(E(step.Consumer)).Append("</td><td>").Append(E(step.Action))
                .Append("</td><td class=\"num\">").Append(step.RowCount.ToString("N0"))
                .Append("</td><td class=\"mono\">").Append(step.Checksum)
                .Append("</td><td>").Append(step.ReadOnlyBytesUnchanged ? "unchanged" : "n/a or changed")
                .Append("</td><td><span class=\"badge ").Append(step.Passed ? "good\">PASS" : "danger\">FAIL")
                .Append("</span></td></tr>");
        }
        html.Append("</tbody></table></div>");
        AppendDetailsList(html, "Informational physical differences of independently-created DBs",
            report.Compatibility?.PhysicalDifferences ?? new(), "warn");
        html.Append("</section>");
    }

    private static void AppendPerformance(StringBuilder html, AuditRunReport report)
    {
        html.Append("<section id=\"performance\"><h2>Speed comparison</h2><div class=\"filters\"><input id=\"perf-query\" type=\"search\" placeholder=\"Фильтр category/scenario…\"><select id=\"perf-gate\"><option value=\"all\">Все gates</option><option value=\"pass\">PASS</option><option value=\"fail\">FAIL</option></select></div><div class=\"table-wrap\"><table id=\"perf-table\"><thead><tr><th>Category / scenario</th><th>W</th><th>Old median [min–max] / ops/s</th><th>New median [min–max] / ops/s</th><th>Speedup / Δ</th><th>Old B/op</th><th>New B/op</th><th>Pairs</th><th>Gate</th></tr></thead><tbody>");
        foreach (AuditPerformanceComparison item in report.Performance)
        {
            bool passed = item.SpeedGatePassed && item.AllocationGatePassed;
            double width = Math.Min(100, Math.Abs(Finite(item.TimeDeltaPercent)) * 5);
            html.Append("<tr data-gate=\"").Append(passed ? "pass" : "fail").Append("\"><td><strong>")
                .Append(E(item.Category)).Append("</strong><br><code>").Append(E(item.Scenario)).Append("</code></td><td class=\"num\">")
                .Append(item.Workers).Append("</td><td class=\"num\">").Append(Ms(item.BaselineMedianMilliseconds))
                .Append(" [").Append(Ms(item.BaselineMinMilliseconds)).Append("–").Append(Ms(item.BaselineMaxMilliseconds))
                .Append("]<br>").Append(F(item.BaselineMedianOperationsPerSecond, "N0")).Append(" ops/s")
                .Append("</td><td class=\"num\">").Append(Ms(item.CurrentMedianMilliseconds))
                .Append(" [").Append(Ms(item.CurrentMinMilliseconds)).Append("–").Append(Ms(item.CurrentMaxMilliseconds))
                .Append("]<br>").Append(F(item.CurrentMedianOperationsPerSecond, "N0")).Append(" ops/s")
                .Append("</td><td class=\"num\">").Append(F(item.Speedup, "F3")).Append("× / ")
                .Append(Signed(item.TimeDeltaPercent)).Append("%<div class=\"bar-track\"><div class=\"bar ")
                .Append(item.TimeDeltaPercent > 5 ? "regress" : String.Empty).Append("\" style=\"width:")
                .Append(width.ToString("F1", CultureInfo.InvariantCulture)).Append("%\"></div></div></td><td class=\"num\">")
                .Append(Bytes(item.BaselineMedianAllocatedBytesPerOperation)).Append("</td><td class=\"num\">")
                .Append(Bytes(item.CurrentMedianAllocatedBytesPerOperation)).Append("</td><td class=\"num\">")
                .Append(item.PairCount).Append(item.ConfirmationRun ? " confirmed" : String.Empty)
                .Append("</td><td><span class=\"badge ").Append(passed ? "good\">PASS" : "danger\">" + E(item.Verdict))
                .Append("</span></td></tr>");
        }
        html.Append("</tbody></table></div></section>");
    }

    private static void AppendParallel(StringBuilder html, AuditRunReport report)
    {
        html.Append("<section id=\"parallel\"><h2>Parallel scaling</h2><div class=\"table-wrap\"><table><thead><tr><th>Scenario</th><th>Workers</th><th>Old ops/s</th><th>New ops/s</th><th>Old efficiency</th><th>New efficiency</th><th>Δ time</th></tr></thead><tbody>");
        foreach (AuditPerformanceComparison item in report.Performance.Where(static item => item.Workers > 1))
        {
            html.Append("<tr><td>").Append(E(item.Category)).Append(" / <code>").Append(E(item.Scenario))
                .Append("</code></td><td class=\"num\">").Append(item.Workers)
                .Append("</td><td class=\"num\">").Append(F(item.BaselineMedianOperationsPerSecond, "N0"))
                .Append("</td><td class=\"num\">").Append(F(item.CurrentMedianOperationsPerSecond, "N0"))
                .Append("</td><td class=\"num\">").Append(F(item.BaselineScalingEfficiency * 100, "F1")).Append("%")
                .Append("</td><td class=\"num\">").Append(F(item.CurrentScalingEfficiency * 100, "F1")).Append("%")
                .Append("</td><td class=\"num\">").Append(Signed(item.TimeDeltaPercent)).Append("%</td></tr>");
        }
        html.Append("</tbody></table></div></section>");
    }

    private static void AppendAllocations(StringBuilder html, AuditRunReport report)
    {
        html.Append("<section id=\"allocations\"><h2>Memory allocations / GC / database size</h2><p class=\"muted\">Zero-allocation path обязан остаться zero. Для allocating paths gate срабатывает только при росте одновременно &gt;2% и &gt;32 B/op. Total/GC/DB — median соответствующих paired measurements.</p><div class=\"table-wrap\"><table><thead><tr><th>Scenario</th><th>W</th><th>Old B/op</th><th>New B/op</th><th>Total allocated old → new</th><th>Gen0/1/2 old → new</th><th>DB bytes old → new</th><th>Δ</th><th>Gate</th></tr></thead><tbody>");
        foreach (AuditPerformanceComparison item in report.Performance.OrderByDescending(static item =>
                     Math.Abs(Finite(item.AllocatedDeltaPercent))))
        {
            html.Append("<tr><td>").Append(E(item.Category)).Append(" / <code>").Append(E(item.Scenario))
                .Append("</code></td><td class=\"num\">").Append(item.Workers)
                .Append("</td><td class=\"num\">").Append(Bytes(item.BaselineMedianAllocatedBytesPerOperation))
                .Append("</td><td class=\"num\">").Append(Bytes(item.CurrentMedianAllocatedBytesPerOperation))
                .Append("</td><td class=\"num\">").Append(Bytes(item.BaselineMedianAllocatedBytes)).Append(" → ")
                .Append(Bytes(item.CurrentMedianAllocatedBytes))
                .Append("</td><td class=\"num\">").Append(F(item.BaselineMedianGen0Collections, "F0")).Append("/")
                .Append(F(item.BaselineMedianGen1Collections, "F0")).Append("/")
                .Append(F(item.BaselineMedianGen2Collections, "F0")).Append(" → ")
                .Append(F(item.CurrentMedianGen0Collections, "F0")).Append("/")
                .Append(F(item.CurrentMedianGen1Collections, "F0")).Append("/")
                .Append(F(item.CurrentMedianGen2Collections, "F0"))
                .Append("</td><td class=\"num\">").Append(Bytes(item.BaselineMedianDatabaseBytes)).Append(" → ")
                .Append(Bytes(item.CurrentMedianDatabaseBytes))
                .Append("</td><td class=\"num\">").Append(Signed(item.AllocatedDeltaPercent)).Append("%</td><td><span class=\"badge ")
                .Append(item.AllocationGatePassed ? "good\">PASS" : "danger\">FAIL").Append("</span></td></tr>");
        }
        html.Append("</tbody></table></div></section>");
    }

    private static string CorrectnessValue(AuditCorrectnessScenario item) => item == null ? "missing" :
        item.Succeeded ? $"{item.Count}/{item.Checksum}" : "FAIL " + item.Error;
    private static void AppendDetailsList(StringBuilder html, string title, IReadOnlyCollection<string> values, string css)
    {
        if (values == null || values.Count == 0) return;
        html.Append("<details><summary class=\"").Append(css).Append("\">").Append(E(title)).Append(" (")
            .Append(values.Count).Append(")</summary><ul class=\"mono small\">");
        foreach (string value in values) html.Append("<li>").Append(E(value)).Append("</li>");
        html.Append("</ul></details>");
    }
    private static string Nav(string id, string label) => $"<a href=\"#{id}\">{E(label)}</a>";
    private static string Meta(string name, string value) => $"<span>{E(name)}: <strong>{E(value)}</strong></span>";
    private static string Stat(object value, string label, string css) => $"<div class=\"stat\"><b class=\"{css}\">{E(value?.ToString())}</b><span>{E(label)}</span></div>";
    private static string Card(string title, string repository, string commit, string sha) => $"<article class=\"card\"><h3>{E(title)}</h3><p><code>{E(repository)}</code><br><code>{E(commit)}</code><br><span class=\"small muted\">DLL SHA-256 {E(sha)}</span></p></article>";
    private static string CardSimple(string title, string value) => $"<article class=\"card\"><h3>{E(title)}</h3><p>{E(value)}</p></article>";
    private static string Row(string name, string value) => $"<tr><th>{E(name)}</th><td class=\"mono\">{E(value)}</td></tr>";
    private static string E(string value) => WebUtility.HtmlEncode(value ?? String.Empty);
    private static string Short(string value) => String.IsNullOrEmpty(value) ? "n/a" : value.Substring(0, Math.Min(12, value.Length));
    private static string O(DateTime? value) => value?.ToString("O", CultureInfo.InvariantCulture) ?? "n/a";
    private static string O(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static string F(double value, string format) => Double.IsFinite(value) ? value.ToString(format, CultureInfo.InvariantCulture) : "n/a";
    private static string Signed(double value) => Double.IsFinite(value) ? value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) : "n/a";
    private static double Finite(double value) => Double.IsFinite(value) ? value : 0;
    private static string Ms(double value) => F(value, value >= 100 ? "N1" : "N3") + " ms";
    private static string Bytes(double value) => F(value, value >= 100 ? "N0" : "F2") + " B";
}

internal static class AuditSelfTests
{
    internal static void Run(AuditRunLayout parent, AuditRunReport realReport, AuditLog log)
    {
        string root = Path.Combine(parent.ScratchDirectory, "self-tests");
        Directory.CreateDirectory(root);

        bool escaped = false;
        try { AuditRunLayout.EnsureUnderRoot(Path.GetDirectoryName(root), root); }
        catch (InvalidOperationException) { escaped = true; }
        Ensure(escaped, "EnsureUnderRoot accepted a parent path.");

        bool wrongCommitRefused = false;
        try { AuditOrchestrator.EnsureExpectedBaselineCommit("expected", "different"); }
        catch (InvalidOperationException) { wrongCommitRefused = true; }
        Ensure(wrongCommitRefused, "Baseline commit guard accepted a wrong commit.");

        var repeat = new AuditRunLayout(root, "repeat");
        repeat.Create();
        bool repeated = false;
        try { repeat.Create(); }
        catch (IOException) { repeated = true; }
        Ensure(repeated, "Run layout allowed a repeated run id.");
        repeat.CleanupScratch();

        var unmarked = new AuditRunLayout(root, "unmarked");
        unmarked.Create();
        File.Delete(unmarked.MarkerPath);
        bool refused = false;
        try { unmarked.CleanupScratch(); }
        catch (InvalidOperationException) { refused = true; }
        Ensure(refused, "Scratch cleanup accepted a missing marker.");
        File.WriteAllText(unmarked.MarkerPath, "unmarked" + Environment.NewLine);
        unmarked.CleanupScratch();

        string htmlPath = Path.Combine(root, "synthetic.html");
        var synthetic = new AuditRunReport
        {
            Passed = false,
            Metadata = new AuditRunMetadata { RunId = "<script>alert(1)</script>", StartedUtc = DateTime.UtcNow },
            ApiComparison = new AuditApiComparison(),
            CorrectnessComparison = new AuditCorrectnessComparison(),
            Compatibility = new AuditCompatibilityReport(),
            Performance = new List<AuditPerformanceComparison>
            {
                new() { Category = "<unsafe>", Scenario = "missing", TimeDeltaPercent = Double.NaN,
                    AllocatedDeltaPercent = Double.PositiveInfinity, SpeedGatePassed = false,
                    AllocationGatePassed = false, Verdict = "FAIL" },
            },
        };
        AuditHtmlReportWriter.Write(htmlPath, synthetic);
        string first = File.ReadAllText(htmlPath);
        Ensure(first.Contains("&lt;script&gt;", StringComparison.Ordinal) &&
               !first.Contains("<script>alert(1)</script>", StringComparison.Ordinal), "HTML encoding failed.");
        Ensure(first.Contains("FAIL", StringComparison.Ordinal) && !first.Contains("https://", StringComparison.Ordinal),
            "Synthetic report contract failed.");
        synthetic.Passed = true;
        AuditHtmlReportWriter.Write(htmlPath, synthetic);
        Ensure(File.ReadAllText(htmlPath).Contains("PASS", StringComparison.Ordinal), "Atomic HTML update failed.");
        log.Write("SELF-TESTS PASS report/safety");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
