using DBreeze.ReleaseAudit.Protocol;

namespace DBreeze.Net8.Benchmarks;

internal sealed class ReleaseAuditReport
{
    public ReleaseAuditMetadata Metadata { get; set; } = new();
    public List<ReleaseBuild> Builds { get; set; } = new();
    public List<ReleasePrerequisite> Prerequisites { get; set; } = new();
    public List<WorkerReport> ApiWorkers { get; set; } = new();
    public List<WorkerReport> CorrectnessWorkers { get; set; } = new();
    public List<ReleaseApiDelta> ApiDeltas { get; set; } = new();
    public List<ReleaseCorrectnessDelta> CorrectnessDeltas { get; set; } = new();
    public List<ReleaseCompatibilityFlow> Compatibility { get; set; } = new();
    public List<ReleasePerformanceSample> PerformanceSamples { get; set; } = new();
    public List<ReleasePerformanceComparison> Performance { get; set; } = new();
    public List<string> GateViolations { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public bool Passed { get; set; }
    public bool Incomplete { get; set; }
    public bool ReleaseVerdictIssued { get; set; }
    public string Failure { get; set; }
}

internal sealed class ReleasePrerequisite
{
    public string Id { get; set; }
    public bool Passed { get; set; }
    public int ExitCode { get; set; }
    public string Detail { get; set; }
}

internal sealed class ReleaseAuditMetadata
{
    public string RunId { get; set; }
    public string Profile { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime CompletedUtc { get; set; }
    public int BudgetMinutes { get; set; }
    public int MaxRecords { get; set; }
    public int MaxTextRecords { get; set; }
    public int MaxVectorRecords { get; set; }
    public string BaselineRepository { get; set; }
    public string BaselineCommit { get; set; }
    public string BaselineFingerprintBefore { get; set; }
    public string BaselineFingerprintAfter { get; set; }
    public string CurrentRepository { get; set; }
    public string CurrentCommit { get; set; }
    public bool CurrentDirty { get; set; }
    public string CurrentDirtyFingerprint { get; set; }
    public string CurrentFingerprintBefore { get; set; }
    public string CurrentFingerprintAfter { get; set; }
    public string DotNetSdk { get; set; }
    public string Runtime { get; set; }
    public string OperatingSystem { get; set; }
    public string Architecture { get; set; }
    public string Processor { get; set; }
    public int LogicalProcessors { get; set; }
    public string Gc { get; set; }
    public string FullMsBuild { get; set; }
    public string ScratchDirectory { get; set; }
    public string ReportsDirectory { get; set; }
    public string CanonicalHtml { get; set; }
    public string TimestampedHtml { get; set; }
    public string ReproductionCommand { get; set; }
}

internal sealed class ReleaseBuild
{
    public string Key { get; set; }
    public string Framework { get; set; }
    public string Variant { get; set; }
    public string Library { get; set; }
    public string LibrarySha256 { get; set; }
    public string Worker { get; set; }
    public string WorkerSha256 { get; set; }
    public int WarningCount { get; set; }
}

internal sealed class ReleaseApiDelta
{
    public string Framework { get; set; }
    public string Scope { get; set; }
    public int BaselineCount { get; set; }
    public int CurrentCount { get; set; }
    public List<string> Missing { get; set; } = new();
    public List<string> Added { get; set; } = new();
    public bool Passed { get; set; }
}

internal sealed class ReleaseCorrectnessDelta
{
    public string Framework { get; set; }
    public string Case { get; set; }
    public string Mode { get; set; }
    public string Baseline { get; set; }
    public string Current { get; set; }
    public string Verdict { get; set; }
    public string Policy { get; set; }
}

internal sealed class ReleaseCompatibilityFlow
{
    public string Id { get; set; }
    public string Kind { get; set; }
    public string Producer { get; set; }
    public string Consumer { get; set; }
    public bool Passed { get; set; }
    public string Semantic { get; set; }
    public string Detail { get; set; }
    public string DatabasePath { get; set; }
}

internal sealed class ReleasePerformanceSample
{
    public string Variant { get; set; }
    public string Framework { get; set; }
    public Measurement Value { get; set; }
}

internal sealed class ReleasePerformanceComparison
{
    public string Framework { get; set; }
    public string Category { get; set; }
    public string Scenario { get; set; }
    public int Workers { get; set; }
    public int PairCount { get; set; }
    public int WorseSpeedPairs { get; set; }
    public double BaselineMedianMilliseconds { get; set; }
    public double CurrentMedianMilliseconds { get; set; }
    public double SpeedDeltaPercent { get; set; }
    public double BaselineBytesPerOperation { get; set; }
    public double CurrentBytesPerOperation { get; set; }
    public double AllocationDeltaPercent { get; set; }
    public bool SpeedPassed { get; set; }
    public bool AllocationPassed { get; set; }
    public bool Complete { get; set; }
    public bool Confirmed { get; set; }
    public string Verdict { get; set; }
}
