using System.Text.Json.Serialization;

namespace DBreeze.Net8.Benchmarks;

internal enum AuditProfile
{
    Smoke,
    Full,
}

internal sealed class AuditRunMetadata
{
    public string RunId { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string Profile { get; set; }
    public int MaxRecords { get; set; }
    public string BaselineRepository { get; set; }
    public string BaselineCommit { get; set; }
    public string CurrentRepository { get; set; }
    public string CurrentCommit { get; set; }
    public bool CurrentSourceDirty { get; set; }
    public string CurrentSourceDiffSha256 { get; set; }
    public string BaselineAssemblySha256 { get; set; }
    public string CurrentAssemblySha256 { get; set; }
    public string BenchmarkSourceSha256 { get; set; }
    public string DotNetSdk { get; set; }
    public string Runtime { get; set; }
    public string OperatingSystem { get; set; }
    public string Architecture { get; set; }
    public string ProcessorIdentifier { get; set; }
    public int LogicalProcessorCount { get; set; }
    public bool ServerGc { get; set; }
    public string GcLatencyMode { get; set; }
    public string ScratchDirectory { get; set; }
    public string ReportsDirectory { get; set; }
    public string HtmlReportPath { get; set; }
    public bool KeepDatabases { get; set; }
    public List<string> VariantOrder { get; set; } = new();
}

internal sealed class AuditApiManifest
{
    public string Variant { get; set; }
    public string AssemblyVersion { get; set; }
    public string AssemblySha256 { get; set; }
    public int ExportedTypeCount { get; set; }
    public int IncludedTypeCount { get; set; }
    public int ExcludedVectorTypeCount { get; set; }
    public List<AuditApiRecord> Records { get; set; } = new();
}

internal sealed class AuditApiRecord
{
    public string Id { get; set; }
    public string Kind { get; set; }
    public string DeclaringType { get; set; }
    public string CoverageScenario { get; set; }
    public string CoverageMode { get; set; }
    public bool Mapped { get; set; }
}

internal sealed class AuditApiComparison
{
    public int BaselineRecordCount { get; set; }
    public int CurrentRecordCount { get; set; }
    public int MappedRecordCount { get; set; }
    public int UnmappedRecordCount { get; set; }
    public bool BackwardCompatible { get; set; }
    public bool CompleteCoverage { get; set; }
    public List<string> MissingRecords { get; set; } = new();
    public List<string> AddedRecords { get; set; } = new();
    public List<string> UnmappedRecords { get; set; } = new();
}

internal sealed class AuditCorrectnessReport
{
    public string Variant { get; set; }
    public bool Succeeded { get; set; }
    public string Failure { get; set; }
    public List<AuditCorrectnessScenario> Scenarios { get; set; } = new();
}

internal sealed class AuditCorrectnessScenario
{
    public string Id { get; set; }
    public bool Succeeded { get; set; }
    public long Count { get; set; }
    public long Checksum { get; set; }
    public string Contract { get; set; }
    public string Error { get; set; }
}

internal sealed class AuditCorrectnessComparison
{
    public bool Passed { get; set; }
    public List<AuditCorrectnessDelta> Deltas { get; set; } = new();
}

internal sealed class AuditCorrectnessDelta
{
    public string Scenario { get; set; }
    public string Policy { get; set; }
    public string Baseline { get; set; }
    public string Current { get; set; }
    public bool Accepted { get; set; }
}

internal sealed class AuditPerformanceReport
{
    public string Variant { get; set; }
    public int Round { get; set; }
    public string Profile { get; set; }
    public int MaxRecords { get; set; }
    public string Runtime { get; set; }
    public bool ServerGc { get; set; }
    public List<AuditMeasurement> Measurements { get; set; } = new();
}

internal sealed class AuditMeasurement
{
    public string Variant { get; set; }
    public int Round { get; set; }
    public string Category { get; set; }
    public string Scenario { get; set; }
    public int Workers { get; set; }
    public long Records { get; set; }
    public long Operations { get; set; }
    public long ResultCount { get; set; }
    public long Checksum { get; set; }
    public double ElapsedMilliseconds { get; set; }
    public double NanosecondsPerOperation { get; set; }
    public double OperationsPerSecond { get; set; }
    public long AllocatedBytes { get; set; }
    public double AllocatedBytesPerOperation { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
    public long DatabaseBytes { get; set; }
    public bool Succeeded { get; set; }
    public string Error { get; set; }
}

internal sealed class AuditPerformanceComparison
{
    public string Category { get; set; }
    public string Scenario { get; set; }
    public int Workers { get; set; }
    public long Records { get; set; }
    public long Operations { get; set; }
    public double BaselineMedianMilliseconds { get; set; }
    public double CurrentMedianMilliseconds { get; set; }
    public double BaselineMinMilliseconds { get; set; }
    public double CurrentMinMilliseconds { get; set; }
    public double BaselineMaxMilliseconds { get; set; }
    public double CurrentMaxMilliseconds { get; set; }
    public double BaselineMedianOperationsPerSecond { get; set; }
    public double CurrentMedianOperationsPerSecond { get; set; }
    public double Speedup { get; set; }
    public double TimeDeltaPercent { get; set; }
    public double BaselineMedianAllocatedBytesPerOperation { get; set; }
    public double CurrentMedianAllocatedBytesPerOperation { get; set; }
    public double BaselineMedianAllocatedBytes { get; set; }
    public double CurrentMedianAllocatedBytes { get; set; }
    public double AllocatedDeltaPercent { get; set; }
    public double BaselineMedianGen0Collections { get; set; }
    public double CurrentMedianGen0Collections { get; set; }
    public double BaselineMedianGen1Collections { get; set; }
    public double CurrentMedianGen1Collections { get; set; }
    public double BaselineMedianGen2Collections { get; set; }
    public double CurrentMedianGen2Collections { get; set; }
    public double BaselineMedianDatabaseBytes { get; set; }
    public double CurrentMedianDatabaseBytes { get; set; }
    public double BaselineScalingEfficiency { get; set; }
    public double CurrentScalingEfficiency { get; set; }
    public int PairCount { get; set; }
    public int SpeedRegressionPairCount { get; set; }
    public bool ConfirmationRun { get; set; }
    public bool SpeedGatePassed { get; set; }
    public bool AllocationGatePassed { get; set; }
    public string Verdict { get; set; }
}

internal sealed class AuditCompatibilityStep
{
    public string Id { get; set; }
    public string Producer { get; set; }
    public string Consumer { get; set; }
    public string Action { get; set; }
    public bool Passed { get; set; }
    public bool ReadOnlyBytesUnchanged { get; set; }
    public long RowCount { get; set; }
    public long Checksum { get; set; }
    public long TotalBytes { get; set; }
    public string ManifestPath { get; set; }
    public string Error { get; set; }
}

internal sealed class AuditCompatibilityReport
{
    public bool Passed { get; set; }
    public List<AuditCompatibilityStep> Steps { get; set; } = new();
    public List<string> PhysicalDifferences { get; set; } = new();
}

internal sealed class AuditRunReport
{
    public AuditRunMetadata Metadata { get; set; } = new();
    public AuditApiManifest BaselineApi { get; set; }
    public AuditApiManifest CurrentApi { get; set; }
    public AuditApiComparison ApiComparison { get; set; }
    public AuditCorrectnessReport BaselineCorrectness { get; set; }
    public AuditCorrectnessReport CurrentCorrectness { get; set; }
    public AuditCorrectnessComparison CorrectnessComparison { get; set; }
    public AuditCompatibilityReport Compatibility { get; set; }
    public List<AuditMeasurement> Measurements { get; set; } = new();
    public List<AuditPerformanceComparison> Performance { get; set; } = new();
    public bool Passed { get; set; }
    public List<string> GateViolations { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string Failure { get; set; }

    [JsonIgnore]
    internal int FailedPerformanceScenarios => Performance.Count(static item =>
        !item.SpeedGatePassed || !item.AllocationGatePassed);
}
