using DBreeze.ReleaseAudit.Protocol;

namespace DBreeze.Net8.Benchmarks;

internal static class ReleaseAuditEvaluator
{
    internal const string VectorsGetAllMemberId = "M public DBreeze.Transactions.Transaction.VectorsGetAll<TVector>(System.String,DBreeze.Transactions.Transaction+VectorTableParameters<`TVector>=null,System.Boolean=true):System.Collections.Generic.IEnumerable<System.ValueTuple<System.Int64,`TVector>>";
    private const string VectorsGetAllBaselineOutcome = "float=fail:System.ArgumentOutOfRangeException;double=fail:System.ArgumentOutOfRangeException";
    private const string VectorsGetAllCurrentOutcome = "float=active:1,2,3|all:1,2,3,4;double=active:11,12,13|all:11,12,13,14";

    internal static void EvaluateApi(ReleaseAuditReport report)
    {
        foreach (string framework in new[] { "net8", "net472" })
        {
            WorkerReport baseline = Worker(report.ApiWorkers, "baseline", framework);
            WorkerReport current = Worker(report.ApiWorkers, "current", framework);
            CompareApi(report, framework, "assembly-public-protected", baseline?.AssemblyApi, current?.AssemblyApi);
            CompareApi(report, framework, "transaction-scheme-canonical", baseline?.FocusedApi, current?.FocusedApi);
            int baselineMethods = baseline?.FocusedApi.Count(static item => item.Kind == "method") ?? 0;
            int currentMethods = current?.FocusedApi.Count(static item => item.Kind == "method") ?? 0;
            if (baselineMethods != 85 || currentMethods != 85)
                report.GateViolations.Add($"Focused method manifest is not 85 for {framework}: {baselineMethods}/{currentMethods}.");
        }
    }

    internal static void EvaluateCoverageAndCorrectness(ReleaseAuditReport report, string baselineSha)
    {
        foreach (WorkerReport worker in report.CorrectnessWorkers)
        {
            bool acceptedVectorsGetAll = worker.Variant == "baseline" &&
                                         IsAcceptedVectorsGetAllHistoricalFix(report, baselineSha, worker.Framework);
            int entries = worker.Coverage.Count;
            int missing = worker.Coverage.Count(static entry => entry.Attempts == 0);
            if (entries != 170 || missing != 0)
                report.GateViolations.Add($"Coverage incomplete for {worker.Variant}-{worker.Framework}: entries={entries}, missing={missing}.");
            foreach (CoverageEntry failed in worker.Coverage.Where(static entry => entry.Attempts != 0 && entry.Successes == 0))
            {
                if (acceptedVectorsGetAll && failed.MemberId == VectorsGetAllMemberId) continue;
                report.GateViolations.Add($"Executed public method failed for {worker.Variant}-{worker.Framework}/{failed.Mode}: {failed.MemberId}");
            }
            foreach (CaseResult failed in worker.Cases.Where(static item => !item.Succeeded && item.Id != "coverage-85x2"))
            {
                if (acceptedVectorsGetAll && failed.Id == "all-public-methods" && (failed.Mode == "single" || failed.Mode == "parallel")) continue;
                report.GateViolations.Add($"Correctness failed for {worker.Variant}-{worker.Framework}: {failed.Id}/{failed.Mode}.");
            }
        }

        foreach (string framework in new[] { "net8", "net472" })
        {
            WorkerReport baseline = Worker(report.CorrectnessWorkers, "baseline", framework);
            WorkerReport current = Worker(report.CorrectnessWorkers, "current", framework);
            var oldCases = (baseline?.Cases ?? new List<CaseResult>()).ToDictionary(CaseKey, StringComparer.Ordinal);
            var newCases = (current?.Cases ?? new List<CaseResult>()).ToDictionary(CaseKey, StringComparer.Ordinal);
            foreach (string key in oldCases.Keys.Union(newCases.Keys, StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal))
            {
                oldCases.TryGetValue(key, out CaseResult oldValue);
                newCases.TryGetValue(key, out CaseResult newValue);
                string oldSemantic = Format(oldValue), newSemantic = Format(newValue);
                if (String.Equals(oldSemantic, newSemantic, StringComparison.Ordinal)) continue;
                bool acceptedTextRemove = IsAcceptedTextRemove(baselineSha, framework, oldValue, newValue);
                bool acceptedVectorsGetAll = IsAcceptedVectorsGetAllHistoricalFix(report, baselineSha, framework) &&
                                             IsVectorsGetAllDelta(oldValue, newValue);
                bool accepted = acceptedTextRemove || acceptedVectorsGetAll;
                string policy = acceptedTextRemove
                    ? "historical-fix: TextRemove preserves unrelated alpha token; exact old/new oracle"
                    : acceptedVectorsGetAll
                        ? "historical-fix: VectorsGetAll scans only prefix 4; exact baseline failure/current oracle"
                        : "exact-parity";
                var delta = new ReleaseCorrectnessDelta
                {
                    Framework = framework,
                    Case = oldValue?.Id ?? newValue?.Id,
                    Mode = oldValue?.Mode ?? newValue?.Mode,
                    Baseline = oldSemantic,
                    Current = newSemantic,
                    Verdict = accepted ? "ACCEPTED" : "FAIL",
                    Policy = policy
                };
                report.CorrectnessDeltas.Add(delta);
                if (!accepted) report.GateViolations.Add($"Unexpected semantic delta: {framework}/{delta.Case}/{delta.Mode}.");
            }
        }
    }

    internal static List<ReleasePerformanceComparison> ComparePerformance(IReadOnlyCollection<ReleasePerformanceSample> samples, bool final, int requiredPrimaryPairs = 3)
    {
        if (requiredPrimaryPairs < 1 || requiredPrimaryPairs > 3) throw new ArgumentOutOfRangeException(nameof(requiredPrimaryPairs));
        var result = new List<ReleasePerformanceComparison>();
        foreach (var group in samples.GroupBy(static sample => new
                 {
                     sample.Framework, sample.Value.Category, sample.Value.Scenario, sample.Value.Workers
                 }).OrderBy(static group => group.Key.Framework, StringComparer.Ordinal)
                 .ThenBy(static group => group.Key.Category, StringComparer.Ordinal)
                 .ThenBy(static group => group.Key.Scenario, StringComparer.Ordinal))
        {
            ReleasePerformanceSample[] baseline = group.Where(static item => item.Variant == "baseline").OrderBy(static item => item.Value.Round).ToArray();
            ReleasePerformanceSample[] current = group.Where(static item => item.Variant == "current").OrderBy(static item => item.Value.Round).ToArray();
            int[] rounds = baseline.Select(static item => item.Value.Round).Intersect(current.Select(static item => item.Value.Round)).OrderBy(static value => value).ToArray();
            var comparison = new ReleasePerformanceComparison
            {
                Framework = group.Key.Framework, Category = group.Key.Category, Scenario = group.Key.Scenario, Workers = group.Key.Workers,
                PairCount = rounds.Length, Confirmed = rounds.Length >= 5
            };
            int expected = comparison.Confirmed ? 5 : requiredPrimaryPairs;
            comparison.Complete = rounds.Length >= expected && baseline.All(static item => item.Value.Operations > 0) && current.All(static item => item.Value.Operations > 0);
            if (rounds.Length != 0)
            {
                Measurement[] oldValues = rounds.Select(round => baseline.Single(value => value.Value.Round == round).Value).ToArray();
                Measurement[] newValues = rounds.Select(round => current.Single(value => value.Value.Round == round).Value).ToArray();
                comparison.BaselineMedianMilliseconds = Median(oldValues.Select(static value => value.ElapsedMilliseconds));
                comparison.CurrentMedianMilliseconds = Median(newValues.Select(static value => value.ElapsedMilliseconds));
                comparison.SpeedDeltaPercent = Percent(comparison.BaselineMedianMilliseconds, comparison.CurrentMedianMilliseconds);
                comparison.WorseSpeedPairs = rounds.Count(round =>
                {
                    double oldMs = baseline.Single(value => value.Value.Round == round).Value.ElapsedMilliseconds;
                    double newMs = current.Single(value => value.Value.Round == round).Value.ElapsedMilliseconds;
                    return newMs > oldMs * 1.05 && newMs - oldMs > 1.0;
                });
                double[] oldBop = oldValues.Select(static value => value.Operations == 0 ? Double.NaN : value.AllocatedBytes / (double)value.Operations).ToArray();
                double[] newBop = newValues.Select(static value => value.Operations == 0 ? Double.NaN : value.AllocatedBytes / (double)value.Operations).ToArray();
                comparison.BaselineBytesPerOperation = Median(oldBop);
                comparison.CurrentBytesPerOperation = Median(newBop);
                comparison.AllocationDeltaPercent = Percent(comparison.BaselineBytesPerOperation, comparison.CurrentBytesPerOperation);
                bool speedThreshold = comparison.CurrentMedianMilliseconds > comparison.BaselineMedianMilliseconds * 1.05 &&
                                      comparison.CurrentMedianMilliseconds - comparison.BaselineMedianMilliseconds > 1.0;
                bool speedFail = final && comparison.Confirmed ? speedThreshold && comparison.WorseSpeedPairs >= 3 : speedThreshold;
                bool background = oldValues.Any(static value => value.BackgroundAllocationCounter) || newValues.Any(static value => value.BackgroundAllocationCounter);
                double oldTotal = Median(oldValues.Select(static value => (double)value.AllocatedBytes));
                double newTotal = Median(newValues.Select(static value => (double)value.AllocatedBytes));
                bool allocationFail = comparison.CurrentBytesPerOperation > comparison.BaselineBytesPerOperation * 1.05 &&
                                      comparison.CurrentBytesPerOperation - comparison.BaselineBytesPerOperation > 1.0 &&
                                      (!background || newTotal - oldTotal > 65536.0);
                comparison.SpeedPassed = !speedFail;
                comparison.AllocationPassed = !allocationFail;
            }
            comparison.Verdict = !comparison.Complete ? "INCOMPLETE" : !comparison.SpeedPassed && !comparison.AllocationPassed ? "SPEED+ALLOC FAIL" :
                !comparison.SpeedPassed ? "SPEED FAIL" : !comparison.AllocationPassed ? "ALLOC FAIL" : "PASS";
            result.Add(comparison);
        }
        return result;
    }

    internal static List<string> SelfTest()
    {
        var failures = new List<string>();
        if (IsRegression(100, 105, 2)) failures.Add("Exactly 5% must pass.");
        if (!IsRegression(100, 105.0001, 2)) failures.Add("Above 5% and 1ms must fail.");
        if (IsRegression(10, 10.6, 0.6)) failures.Add("Speed delta <=1ms must pass.");
        var missing = ComparePerformance(new[]
        {
            Sample("baseline", 1, 100), Sample("current", 2, 100)
        }, false);
        if (missing.Count != 1 || missing[0].Complete) failures.Add("Missing paired rounds must be incomplete.");
        var coverage = Enumerable.Range(0, 170).Select(index => new CoverageEntry { MemberId = "m" + index, Mode = index % 2 == 0 ? "single" : "parallel", Attempts = 1, Successes = 1 }).ToList();
        coverage[42].Attempts = 0;
        if (coverage.Count != 170 || coverage.Count(static value => value.Attempts == 0) != 1) failures.Add("Missing coverage detection failed.");
        var acceptedOld = new CaseResult { Id = "text-remove-known-delta", Mode = "single", Succeeded = true, SemanticValue = "or=1" };
        var acceptedNew = new CaseResult { Id = "text-remove-known-delta", Mode = "single", Succeeded = true, SemanticValue = "or=1,3" };
        if (!IsAcceptedTextRemove(ReleaseAuditOptions.DefaultBaselineCommit, "net8", acceptedOld, acceptedNew) ||
            IsAcceptedTextRemove(ReleaseAuditOptions.DefaultBaselineCommit, "net8", acceptedOld,
                new CaseResult { Id = acceptedNew.Id, Mode = acceptedNew.Mode, Succeeded = true, SemanticValue = "or=1,2,3" }))
            failures.Add("Exact allowlist matching failed.");
        ReleaseAuditReport vectorsFix = VectorsGetAllSelfTestReport();
        if (!IsAcceptedVectorsGetAllHistoricalFix(vectorsFix, ReleaseAuditOptions.DefaultBaselineCommit, "net8"))
            failures.Add("Exact VectorsGetAll allowlist matching failed.");
        vectorsFix.CorrectnessWorkers.Single(static worker => worker.Variant == "current")
            .Cases.Single(static item => item.Id == "vectors-get-all-known-delta" && item.Mode == "single").SemanticValue += ",unexpected";
        if (IsAcceptedVectorsGetAllHistoricalFix(vectorsFix, ReleaseAuditOptions.DefaultBaselineCommit, "net8") ||
            IsAcceptedVectorsGetAllHistoricalFix(VectorsGetAllSelfTestReport(), "wrong-sha", "net8") ||
            IsAcceptedVectorsGetAllHistoricalFix(VectorsGetAllSelfTestReport(), ReleaseAuditOptions.DefaultBaselineCommit, "other"))
            failures.Add("VectorsGetAll allowlist accepted an inexact identity or outcome.");
        try { ReleaseAuditOptions.Parse(new[] { "--release-audit", "--max-records", "1000001" }); failures.Add("Record limit accepted 1,000,001."); }
        catch (ArgumentOutOfRangeException) { }
        if (new ReleaseDeadline(TimeSpan.Zero).Remaining > TimeSpan.Zero) failures.Add("Expired deadline was not detected.");
        try { ReleaseAuditOrchestrator.EnsureProcessSucceeded(7, "self-test", Array.Empty<string>(), "expected"); failures.Add("Non-zero process exit was accepted."); }
        catch (InvalidOperationException) { }
        var manifestA = new[] { new KeyValuePair<string, byte[]>("b.cs", new byte[] { 2 }), new KeyValuePair<string, byte[]>("a.cs", new byte[] { 1 }) };
        var manifestB = new[] { manifestA[1], manifestA[0] };
        var manifestChanged = new[] { manifestA[1], new KeyValuePair<string, byte[]>("b.cs", new byte[] { 3 }) };
        if (ReleaseAuditOrchestrator.ManifestHash(manifestA) != ReleaseAuditOrchestrator.ManifestHash(manifestB) ||
            ReleaseAuditOrchestrator.ManifestHash(manifestA) == ReleaseAuditOrchestrator.ManifestHash(manifestChanged))
            failures.Add("Manifest hashing is not deterministic/content-sensitive.");
        string escaped = ReleaseAuditArtifacts.Html("<script>&\"");
        if (escaped.Contains("<script>", StringComparison.Ordinal) || !escaped.Contains("&lt;", StringComparison.Ordinal)) failures.Add("HTML escaping failed.");
        try { AuditRunLayout.EnsureUnderRoot(Path.Combine(Path.GetTempPath(), "outside"), Path.Combine(Path.GetTempPath(), "owner")); failures.Add("Path containment accepted escape."); }
        catch (InvalidOperationException) { }
        return failures;
    }

    private static void CompareApi(ReleaseAuditReport report, string framework, string scope, IList<ApiMember> baseline, IList<ApiMember> current)
    {
        var oldIds = new HashSet<string>((baseline ?? new List<ApiMember>()).Select(static item => item.Id), StringComparer.Ordinal);
        var newIds = new HashSet<string>((current ?? new List<ApiMember>()).Select(static item => item.Id), StringComparer.Ordinal);
        var delta = new ReleaseApiDelta
        {
            Framework = framework, Scope = scope, BaselineCount = oldIds.Count, CurrentCount = newIds.Count,
            Missing = oldIds.Except(newIds, StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToList(),
            Added = newIds.Except(oldIds, StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToList()
        };
        delta.Passed = delta.Missing.Count == 0 && baseline != null && current != null;
        report.ApiDeltas.Add(delta);
        if (!delta.Passed) report.GateViolations.Add($"Backward API compatibility failed: {framework}/{scope}; missing={delta.Missing.Count}.");
        if (delta.Added.Count != 0) report.Warnings.Add($"Additive API: {framework}/{scope}, {delta.Added.Count} member(s).");
    }

    private static bool IsAcceptedTextRemove(string sha, string framework, CaseResult baseline, CaseResult current)
    {
        return String.Equals(sha, ReleaseAuditOptions.DefaultBaselineCommit, StringComparison.OrdinalIgnoreCase) &&
               framework is "net8" or "net472" && baseline?.Id == "text-remove-known-delta" && current?.Id == baseline.Id &&
               baseline.Mode == "single" && current.Mode == "single" && baseline.Succeeded && current.Succeeded &&
               baseline.SemanticValue == "or=1" && current.SemanticValue == "or=1,3";
    }

    internal static bool IsAcceptedVectorsGetAllHistoricalFix(ReleaseAuditReport report, string sha, string framework)
    {
        if (!String.Equals(sha, ReleaseAuditOptions.DefaultBaselineCommit, StringComparison.OrdinalIgnoreCase) ||
            framework is not ("net8" or "net472"))
            return false;

        WorkerReport baseline = Worker(report.CorrectnessWorkers, "baseline", framework);
        WorkerReport current = Worker(report.CorrectnessWorkers, "current", framework);
        if (baseline == null || current == null) return false;

        CoverageEntry[] failedBaseline = baseline.Coverage.Where(static entry => entry.Attempts != 0 && entry.Successes == 0).ToArray();
        if (failedBaseline.Length != 2 || failedBaseline.Any(static entry => entry.MemberId != VectorsGetAllMemberId) ||
            !failedBaseline.Select(static entry => entry.Mode).OrderBy(static value => value, StringComparer.Ordinal).SequenceEqual(new[] { "parallel", "single" }))
            return false;

        foreach (string mode in new[] { "single", "parallel" })
        {
            CoverageEntry currentEntry = current.Coverage.SingleOrDefault(entry => entry.MemberId == VectorsGetAllMemberId && entry.Mode == mode);
            if (currentEntry == null || currentEntry.Attempts == 0 || currentEntry.Successes != currentEntry.Attempts) return false;
            CaseResult oldProbe = baseline.Cases.SingleOrDefault(item => item.Id == "vectors-get-all-known-delta" && item.Mode == mode);
            CaseResult newProbe = current.Cases.SingleOrDefault(item => item.Id == "vectors-get-all-known-delta" && item.Mode == mode);
            if (oldProbe?.Succeeded != true || newProbe?.Succeeded != true ||
                oldProbe.SemanticValue != VectorsGetAllBaselineOutcome || newProbe.SemanticValue != VectorsGetAllCurrentOutcome)
                return false;
            CaseResult oldAll = baseline.Cases.SingleOrDefault(item => item.Id == "all-public-methods" && item.Mode == mode);
            CaseResult newAll = current.Cases.SingleOrDefault(item => item.Id == "all-public-methods" && item.Mode == mode);
            if (oldAll == null || oldAll.Succeeded || newAll?.Succeeded != true) return false;
        }

        return true;
    }

    private static bool IsVectorsGetAllDelta(CaseResult baseline, CaseResult current)
    {
        if (baseline == null || current == null || baseline.Id != current.Id || baseline.Mode != current.Mode) return false;
        return baseline.Id == "vectors-get-all-known-delta" || baseline.Id == "coverage-85x2" ||
               baseline.Id == "all-public-methods" && (baseline.Mode == "single" || baseline.Mode == "parallel");
    }

    private static ReleaseAuditReport VectorsGetAllSelfTestReport()
    {
        var report = new ReleaseAuditReport();
        foreach (string variant in new[] { "baseline", "current" })
        {
            var worker = new WorkerReport { Variant = variant, Framework = "net8" };
            foreach (string mode in new[] { "single", "parallel" })
            {
                worker.Coverage.Add(new CoverageEntry
                {
                    MemberId = VectorsGetAllMemberId, Mode = mode, Attempts = 1,
                    Successes = variant == "baseline" ? 0 : 1
                });
                worker.Cases.Add(new CaseResult
                {
                    Id = "all-public-methods", Mode = mode, Succeeded = variant == "current",
                    SemanticValue = variant == "current" ? "current" : null
                });
                worker.Cases.Add(new CaseResult
                {
                    Id = "vectors-get-all-known-delta", Mode = mode, Succeeded = true,
                    SemanticValue = variant == "baseline" ? VectorsGetAllBaselineOutcome : VectorsGetAllCurrentOutcome
                });
            }
            report.CorrectnessWorkers.Add(worker);
        }
        return report;
    }

    private static WorkerReport Worker(IEnumerable<WorkerReport> reports, string variant, string framework) =>
        reports.FirstOrDefault(value => value.Variant == variant && value.Framework == framework);
    private static string CaseKey(CaseResult value) => value.Id + "\n" + value.Mode;
    private static string Format(CaseResult value) => value == null ? "missing" : value.Succeeded ? "ok:" + value.SemanticValue : "failed";
    private static double Median(IEnumerable<double> values)
    {
        double[] sorted = values.Where(static value => !Double.IsNaN(value) && !Double.IsInfinity(value)).OrderBy(static value => value).ToArray();
        if (sorted.Length == 0) return Double.NaN;
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2.0 : sorted[middle];
    }
    private static double Percent(double baseline, double current) => baseline == 0 ? current == 0 ? 0 : Double.PositiveInfinity : (current / baseline - 1.0) * 100.0;
    private static bool IsRegression(double baseline, double current, double absoluteDifference) => current > baseline * 1.05 && absoluteDifference > 1.0;
    private static ReleasePerformanceSample Sample(string variant, int round, double milliseconds) => new()
    {
        Variant = variant, Framework = "test", Value = new Measurement { Category = "test", Scenario = "test", Workers = 1, Round = round, Operations = 1, ElapsedMilliseconds = milliseconds }
    };
}
