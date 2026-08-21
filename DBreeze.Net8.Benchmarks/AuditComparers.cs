namespace DBreeze.Net8.Benchmarks;

internal static class AuditCorrectnessComparer
{
    private static readonly IReadOnlyDictionary<string, AcceptedPolicy> AcceptedPolicies =
        new Dictionary<string, AcceptedPolicy>(StringComparer.Ordinal)
        {
            ["text-resources"] = new(
                "historical-fix: TextRemove in current preserves unrelated alpha token; " +
                "expected baseline OR={1}, current OR={1,3}",
                "count=13;checksum=-7554483337503716765",
                "count=13;checksum=-7169211175236201053"),
        };

    internal static AuditCorrectnessComparison Compare(AuditCorrectnessReport baseline,
        AuditCorrectnessReport current)
    {
        var result = new AuditCorrectnessComparison();
        var oldById = baseline.Scenarios.ToDictionary(static item => item.Id, StringComparer.Ordinal);
        var newById = current.Scenarios.ToDictionary(static item => item.Id, StringComparer.Ordinal);
        foreach (string id in oldById.Keys.Union(newById.Keys, StringComparer.Ordinal)
                     .OrderBy(static value => value, StringComparer.Ordinal))
        {
            oldById.TryGetValue(id, out AuditCorrectnessScenario oldItem);
            newById.TryGetValue(id, out AuditCorrectnessScenario newItem);
            string oldValue = Format(oldItem);
            string newValue = Format(newItem);
            if (String.Equals(oldValue, newValue, StringComparison.Ordinal))
                continue;
            bool accepted = AcceptedPolicies.TryGetValue(id, out AcceptedPolicy policy) &&
                            String.Equals(oldValue, policy.ExpectedBaseline, StringComparison.Ordinal) &&
                            String.Equals(newValue, policy.ExpectedCurrent, StringComparison.Ordinal);
            result.Deltas.Add(new AuditCorrectnessDelta
            {
                Scenario = id,
                Policy = accepted ? policy.Description : "exact-parity",
                Baseline = oldValue,
                Current = newValue,
                Accepted = accepted,
            });
        }
        result.Passed = baseline.Succeeded && current.Succeeded && result.Deltas.All(static delta => delta.Accepted);
        return result;
    }

    private static string Format(AuditCorrectnessScenario item) => item == null
        ? "missing"
            : item.Succeeded
            ? $"count={item.Count};checksum={item.Checksum}"
            : "failed:" + item.Error;

    private sealed record AcceptedPolicy(string Description, string ExpectedBaseline, string ExpectedCurrent);
}

internal static class AuditPerformanceComparer
{
    private const double SpeedRegressionLimitPercent = 5d;
    private const double AllocationRegressionLimitPercent = 2d;
    private const double AllocationRegressionLimitBytes = 32d;

    internal static List<AuditPerformanceComparison> Compare(IReadOnlyCollection<AuditMeasurement> measurements,
        ISet<string> confirmationKeys)
    {
        var results = new List<AuditPerformanceComparison>();
        var groups = measurements.Where(static item => item.Succeeded)
            .GroupBy(static item => new ScenarioKey(item.Category, item.Scenario, item.Workers))
            .OrderBy(static group => group.Key.Category, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.Scenario, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.Workers);
        foreach (var group in groups)
        {
            AuditMeasurement[] oldValues = group.Where(static item => item.Variant == "old")
                .OrderBy(static item => item.Round).ToArray();
            AuditMeasurement[] newValues = group.Where(static item => item.Variant == "new")
                .OrderBy(static item => item.Round).ToArray();
            if (oldValues.Length == 0 || newValues.Length == 0)
                continue;
            int[] commonRounds = oldValues.Select(static item => item.Round)
                .Intersect(newValues.Select(static item => item.Round)).OrderBy(static round => round).ToArray();
            double oldMedianMs = Median(oldValues.Select(static item => item.ElapsedMilliseconds));
            double newMedianMs = Median(newValues.Select(static item => item.ElapsedMilliseconds));
            double oldAllocated = Median(oldValues.Select(static item => item.AllocatedBytesPerOperation));
            double newAllocated = Median(newValues.Select(static item => item.AllocatedBytesPerOperation));
            double timeDelta = PercentDelta(oldMedianMs, newMedianMs);
            double allocationDelta = PercentDelta(oldAllocated, newAllocated);
            int regressedPairs = commonRounds.Count(round =>
            {
                double oldMs = oldValues.Single(item => item.Round == round).ElapsedMilliseconds;
                double newMs = newValues.Single(item => item.Round == round).ElapsedMilliseconds;
                return PercentDelta(oldMs, newMs) > SpeedRegressionLimitPercent;
            });
            string filterKey = group.Key.FilterKey;
            bool confirmed = confirmationKeys?.Contains(filterKey) == true;
            int requiredPairs = confirmed ? 3 : Math.Max(2, commonRounds.Length);
            bool speedPassed = timeDelta <= SpeedRegressionLimitPercent || regressedPairs < requiredPairs;
            bool allocationRegression = oldAllocated == 0
                ? newAllocated > 0
                : newAllocated - oldAllocated > AllocationRegressionLimitBytes &&
                  allocationDelta > AllocationRegressionLimitPercent;
            bool allocationPassed = !allocationRegression;
            string verdict = speedPassed && allocationPassed ? "PASS" :
                !speedPassed && !allocationPassed ? "FAIL speed+alloc" :
                !speedPassed ? "FAIL speed" : "FAIL alloc";
            results.Add(new AuditPerformanceComparison
            {
                Category = group.Key.Category,
                Scenario = group.Key.Scenario,
                Workers = group.Key.Workers,
                Records = oldValues[0].Records,
                Operations = oldValues[0].Operations,
                BaselineMedianMilliseconds = oldMedianMs,
                CurrentMedianMilliseconds = newMedianMs,
                BaselineMinMilliseconds = oldValues.Min(static item => item.ElapsedMilliseconds),
                CurrentMinMilliseconds = newValues.Min(static item => item.ElapsedMilliseconds),
                BaselineMaxMilliseconds = oldValues.Max(static item => item.ElapsedMilliseconds),
                CurrentMaxMilliseconds = newValues.Max(static item => item.ElapsedMilliseconds),
                BaselineMedianOperationsPerSecond = Median(oldValues.Select(static item => item.OperationsPerSecond)),
                CurrentMedianOperationsPerSecond = Median(newValues.Select(static item => item.OperationsPerSecond)),
                Speedup = newMedianMs > 0 ? oldMedianMs / newMedianMs : 0,
                TimeDeltaPercent = timeDelta,
                BaselineMedianAllocatedBytesPerOperation = oldAllocated,
                CurrentMedianAllocatedBytesPerOperation = newAllocated,
                BaselineMedianAllocatedBytes = Median(oldValues.Select(static item => (double)item.AllocatedBytes)),
                CurrentMedianAllocatedBytes = Median(newValues.Select(static item => (double)item.AllocatedBytes)),
                AllocatedDeltaPercent = allocationDelta,
                BaselineMedianGen0Collections = Median(oldValues.Select(static item => (double)item.Gen0Collections)),
                CurrentMedianGen0Collections = Median(newValues.Select(static item => (double)item.Gen0Collections)),
                BaselineMedianGen1Collections = Median(oldValues.Select(static item => (double)item.Gen1Collections)),
                CurrentMedianGen1Collections = Median(newValues.Select(static item => (double)item.Gen1Collections)),
                BaselineMedianGen2Collections = Median(oldValues.Select(static item => (double)item.Gen2Collections)),
                CurrentMedianGen2Collections = Median(newValues.Select(static item => (double)item.Gen2Collections)),
                BaselineMedianDatabaseBytes = Median(oldValues.Select(static item => (double)item.DatabaseBytes)),
                CurrentMedianDatabaseBytes = Median(newValues.Select(static item => (double)item.DatabaseBytes)),
                PairCount = commonRounds.Length,
                SpeedRegressionPairCount = regressedPairs,
                ConfirmationRun = confirmed,
                SpeedGatePassed = speedPassed,
                AllocationGatePassed = allocationPassed,
                Verdict = verdict,
            });
        }

        foreach (AuditPerformanceComparison item in results.Where(static item => item.Workers > 1))
        {
            AuditPerformanceComparison single = results.FirstOrDefault(candidate =>
                candidate.Workers == 1 && candidate.Category == item.Category && candidate.Scenario == item.Scenario);
            if (single == null)
                continue;
            item.BaselineScalingEfficiency = single.BaselineMedianOperationsPerSecond > 0
                ? item.BaselineMedianOperationsPerSecond / single.BaselineMedianOperationsPerSecond / item.Workers : 0;
            item.CurrentScalingEfficiency = single.CurrentMedianOperationsPerSecond > 0
                ? item.CurrentMedianOperationsPerSecond / single.CurrentMedianOperationsPerSecond / item.Workers : 0;
        }
        return results;
    }

    internal static HashSet<string> FindConfirmationCandidates(IEnumerable<AuditPerformanceComparison> comparisons)
    {
        return comparisons.Where(static item =>
                item.TimeDeltaPercent > SpeedRegressionLimitPercent ||
                (item.BaselineMedianAllocatedBytesPerOperation == 0
                    ? item.CurrentMedianAllocatedBytesPerOperation > 0
                    : item.CurrentMedianAllocatedBytesPerOperation - item.BaselineMedianAllocatedBytesPerOperation >
                      AllocationRegressionLimitBytes && item.AllocatedDeltaPercent > AllocationRegressionLimitPercent))
            .Select(static item => new ScenarioKey(item.Category, item.Scenario, item.Workers).FilterKey)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static double Median(IEnumerable<double> source)
    {
        double[] values = source.OrderBy(static value => value).ToArray();
        if (values.Length == 0)
            return 0;
        int middle = values.Length / 2;
        return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / 2d : values[middle];
    }

    private static double PercentDelta(double baseline, double current)
    {
        if (baseline == 0)
            return current == 0 ? 0 : Double.PositiveInfinity;
        return (current - baseline) * 100d / baseline;
    }

    private readonly record struct ScenarioKey(string Category, string Scenario, int Workers)
    {
        internal string FilterKey => Category + "|" + Scenario + "|" + Workers;
    }
}
