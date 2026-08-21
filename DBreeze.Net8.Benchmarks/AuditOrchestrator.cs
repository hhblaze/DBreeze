using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace DBreeze.Net8.Benchmarks;

internal static class AuditOrchestrator
{
    internal static int Run(string[] args)
    {
        AuditComparisonOptions options;
        try
        {
            options = AuditComparisonOptions.Parse(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }

        var layout = new AuditRunLayout(options.RootPath, options.RunId);
        var report = new AuditRunReport
        {
            Metadata = new AuditRunMetadata
            {
                RunId = options.RunId,
                StartedUtc = DateTime.UtcNow,
                Profile = options.Profile.ToString().ToLowerInvariant(),
                MaxRecords = options.MaxRecords,
                BaselineRepository = options.BaselineRepository,
                BaselineCommit = options.ExpectedBaselineCommit,
                CurrentRepository = options.CurrentRepository,
                ScratchDirectory = layout.ScratchDirectory,
                ReportsDirectory = layout.ReportsDirectory,
                HtmlReportPath = options.ReportPath,
                KeepDatabases = options.KeepDatabases,
                Runtime = RuntimeInformation.FrameworkDescription + " / " + Environment.Version,
                OperatingSystem = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                ProcessorIdentifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? String.Empty,
                LogicalProcessorCount = Environment.ProcessorCount,
                ServerGc = GCSettings.IsServerGC,
                GcLatencyMode = GCSettings.LatencyMode.ToString(),
            },
        };

        AuditLog log = null;
        bool layoutCreated = false;
        try
        {
            layout.Create();
            layoutCreated = true;
            log = new AuditLog(Path.Combine(layout.ReportsDirectory, "run.log"));
            log.Write($"START DBreeze audit {options.RunId}; profile={report.Metadata.Profile}; maxRecords={options.MaxRecords:N0}");
            ValidateRepositories(options, report, log);
            report.Metadata.DotNetSdk = RunProcess("dotnet", new[] { "--version" }, options.CurrentRepository, log).StandardOutput.Trim();
            report.Metadata.BenchmarkSourceSha256 = ComputeBenchmarkSourceHash(options.CurrentRepository);

            BuildOutputs builds = BuildAll(options, layout, log);
            report.Metadata.BaselineAssemblySha256 = ComputeSha256(builds.BaselineLibrary);
            report.Metadata.CurrentAssemblySha256 = ComputeSha256(builds.CurrentLibrary);

            RunCurrentRegressionTests(builds.CurrentTestRunner, options, layout, log);
            AuditSelfTests.Run(layout, report, log);

            report.BaselineApi = RunApiWorker(builds.BaselineWorker, "old", options, layout, log);
            report.CurrentApi = RunApiWorker(builds.CurrentWorker, "new", options, layout, log);
            report.ApiComparison = AuditApiCatalog.Compare(report.BaselineApi, report.CurrentApi);
            AuditPersistence.WriteJson(Path.Combine(layout.ReportsDirectory, "api-coverage.json"), new
            {
                Baseline = report.BaselineApi,
                Current = report.CurrentApi,
                Comparison = report.ApiComparison,
            });

            report.BaselineCorrectness = RunCorrectnessWorker(builds.BaselineWorker, "old", options, layout, log);
            report.CurrentCorrectness = RunCorrectnessWorker(builds.CurrentWorker, "new", options, layout, log);
            report.CorrectnessComparison = AuditCorrectnessComparer.Compare(report.BaselineCorrectness,
                report.CurrentCorrectness);

            report.Compatibility = RunCompatibility(builds, options, layout, log);
            AuditPersistence.WriteJson(Path.Combine(layout.ReportsDirectory, "compatibility.json"), report.Compatibility);

            RunPrimaryPerformanceRounds(builds, options, layout, report, log);
            report.Performance = AuditPerformanceComparer.Compare(report.Measurements, new HashSet<string>());
            HashSet<string> confirmation = AuditPerformanceComparer.FindConfirmationCandidates(report.Performance);
            if (confirmation.Count != 0)
            {
                log.Write($"CONFIRM {confirmation.Count} performance/allocation gate candidates");
                RunConfirmationRounds(builds, options, layout, report, log, confirmation);
                report.Performance = AuditPerformanceComparer.Compare(report.Measurements, confirmation);
            }

            EvaluateGates(report);
            report.Metadata.CompletedUtc = DateTime.UtcNow;
            PersistFinalReports(report, layout, options, log);
            log.Write(report.Passed ? "COMPLETE PASS" : "COMPLETE FAIL");
        }
        catch (Exception ex)
        {
            report.Failure = ex.ToString();
            report.GateViolations.Add("Suite failure: " + ex.Message);
            report.Passed = false;
            report.Metadata.CompletedUtc = DateTime.UtcNow;
            log?.Write("FAILED " + ex);
            if (layoutCreated)
            {
                try { PersistFinalReports(report, layout, options, log); }
                catch (Exception persistError)
                {
                    Console.Error.WriteLine(persistError);
                }
            }
        }
        finally
        {
            if (layoutCreated && !options.KeepDatabases)
            {
                try
                {
                    layout.CleanupScratch();
                    log?.Write("CLEANUP scratch complete");
                }
                catch (Exception cleanupError)
                {
                    report.Passed = false;
                    report.GateViolations.Add("Scratch cleanup failed: " + cleanupError.Message);
                    log?.Write("CLEANUP FAILED " + cleanupError);
                    try { PersistFinalReports(report, layout, options, log); }
                    catch { }
                }
            }
            log?.Dispose();
        }

        return report.Passed ? 0 : 1;
    }

    private static void ValidateRepositories(AuditComparisonOptions options, AuditRunReport report, AuditLog log)
    {
        string baselineProject = Path.Combine(options.BaselineRepository, "DBreeze.Net8", "DBreeze.Net8.csproj");
        string currentProject = Path.Combine(options.CurrentRepository, "DBreeze.Net8", "DBreeze.Net8.csproj");
        if (!File.Exists(baselineProject))
            throw new FileNotFoundException("Baseline DBreeze.Net8 project was not found.", baselineProject);
        if (!File.Exists(currentProject))
            throw new FileNotFoundException("Current DBreeze.Net8 project was not found.", currentProject);

        string baselineCommit = Git(options.BaselineRepository, log, "rev-parse", "HEAD").Trim();
        EnsureExpectedBaselineCommit(options.ExpectedBaselineCommit, baselineCommit);
        string[] sourcePaths = { "DBreeze", "DBreeze.Net8", "DBreeze.NetStandard", "DBreeze.Net5" };
        var statusArgs = new List<string> { "status", "--porcelain", "--untracked-files=no", "--" };
        statusArgs.AddRange(sourcePaths);
        string baselineStatus = Git(options.BaselineRepository, log, statusArgs.ToArray());
        if (!String.IsNullOrWhiteSpace(baselineStatus))
            throw new InvalidOperationException("Baseline contains tracked source changes:\n" + baselineStatus);

        report.Metadata.BaselineCommit = baselineCommit;
        report.Metadata.CurrentCommit = Git(options.CurrentRepository, log, "rev-parse", "HEAD").Trim();
        string[] currentSourcePaths = sourcePaths.Concat(new[] { "DBreeze.Net8.Benchmarks", "DBreeze.Net8.Tests" })
            .ToArray();
        var currentStatusArgs = new List<string> { "status", "--porcelain", "--untracked-files=all", "--" };
        currentStatusArgs.AddRange(currentSourcePaths);
        string currentStatus = Git(options.CurrentRepository, log, currentStatusArgs.ToArray());
        var diffArgs = new List<string> { "diff", "--binary", "HEAD", "--" };
        diffArgs.AddRange(currentSourcePaths);
        string currentDiff = Git(options.CurrentRepository, log, diffArgs.ToArray());
        var untrackedArgs = new List<string> { "ls-files", "--others", "--exclude-standard", "--" };
        untrackedArgs.AddRange(currentSourcePaths);
        string[] untracked = Git(options.CurrentRepository, log, untrackedArgs.ToArray())
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var dirtyFingerprint = new StringBuilder(currentDiff);
        foreach (string relativePath in untracked.OrderBy(static path => path, StringComparer.Ordinal))
        {
            string fullPath = Path.Combine(options.CurrentRepository, relativePath.Replace('/', Path.DirectorySeparatorChar));
            dirtyFingerprint.Append("\nuntracked ").Append(relativePath).Append(' ')
                .Append(File.Exists(fullPath) ? ComputeSha256(File.ReadAllBytes(fullPath)) : "missing");
        }
        report.Metadata.CurrentSourceDirty = currentStatus.Length != 0;
        report.Metadata.CurrentSourceDiffSha256 = ComputeSha256(Encoding.UTF8.GetBytes(dirtyFingerprint.ToString()));
        log.Write($"REPOSITORIES baseline={baselineCommit}; current={report.Metadata.CurrentCommit}; currentSourceDirty={report.Metadata.CurrentSourceDirty}");
    }

    internal static void EnsureExpectedBaselineCommit(string expected, string actual)
    {
        if (!String.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Baseline commit mismatch: expected {expected}, actual {actual}.");
    }

    private static BuildOutputs BuildAll(AuditComparisonOptions options, AuditRunLayout layout, AuditLog log)
    {
        string buildRoot = Path.Combine(layout.ScratchDirectory, "build");
        string baselineLibrary = BuildProject(
            Path.Combine(options.BaselineRepository, "DBreeze.Net8", "DBreeze.Net8.csproj"),
            Path.Combine(buildRoot, "old-library"), "DBreeze.dll", options.BaselineRepository, log);
        string currentLibrary = BuildProject(
            Path.Combine(options.CurrentRepository, "DBreeze.Net8", "DBreeze.Net8.csproj"),
            Path.Combine(buildRoot, "new-library"), "DBreeze.dll", options.CurrentRepository, log);
        string benchmarkProject = Path.Combine(options.CurrentRepository, "DBreeze.Net8.Benchmarks",
            "DBreeze.Net8.Benchmarks.csproj");
        string baselineWorker = BuildProject(benchmarkProject, Path.Combine(buildRoot, "old-worker"),
            "DBreeze.Net8.Benchmarks.dll", options.CurrentRepository, log,
            new Dictionary<string, string> { ["DBreezeAssemblyReference"] = baselineLibrary });
        string currentWorker = BuildProject(benchmarkProject, Path.Combine(buildRoot, "new-worker"),
            "DBreeze.Net8.Benchmarks.dll", options.CurrentRepository, log,
            new Dictionary<string, string> { ["DBreezeAssemblyReference"] = currentLibrary });
        string testsProject = Path.Combine(options.CurrentRepository, "DBreeze.Net8.Tests", "DBreeze.Net8.Tests.csproj");
        string currentTests = BuildProject(testsProject, Path.Combine(buildRoot, "current-tests"),
            "DBreeze.Net8.Tests.dll", options.CurrentRepository, log,
            new Dictionary<string, string> { ["DBreezeAssemblyReference"] = currentLibrary });
        return new BuildOutputs(baselineLibrary, currentLibrary, baselineWorker, currentWorker, currentTests);
    }

    private static string BuildProject(string project, string buildRoot, string assemblyName, string workingDirectory,
        AuditLog log, IReadOnlyDictionary<string, string> properties = null)
    {
        string output = Path.Combine(buildRoot, "out") + Path.DirectorySeparatorChar;
        string intermediate = Path.Combine(buildRoot, "obj") + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(buildRoot);
        var arguments = new List<string>
        {
            "build", project, "-c", "Release", "-f", "net8.0", "--nologo", "-v:minimal", "-clp:ErrorsOnly",
            "-p:OutputPath=" + output,
            "-p:IntermediateOutputPath=" + intermediate,
            "-p:UseSharedCompilation=false",
            "-p:SignAssembly=false",
        };
        if (properties != null)
            arguments.AddRange(properties.Select(static pair => "-p:" + pair.Key + "=" + pair.Value));
        log.Write("BUILD " + project);
        RunProcess("dotnet", arguments, workingDirectory, log);
        string[] candidates = Directory.GetFiles(output, assemblyName, SearchOption.AllDirectories)
            .Where(static path => !path.Contains(Path.DirectorySeparatorChar + "ref" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length == 0)
            throw new FileNotFoundException($"Build output {assemblyName} was not found under {output}.");
        return candidates.OrderByDescending(File.GetLastWriteTimeUtc).First();
    }

    private static void RunCurrentRegressionTests(string testRunner, AuditComparisonOptions options,
        AuditRunLayout layout, AuditLog log)
    {
        string testRoot = Path.Combine(layout.ScratchDirectory, "current-regression-tests");
        log.Write("TEST current DBreeze.Net8.Tests");
        RunProcess("dotnet", new[] { testRunner }, options.CurrentRepository, log,
            new Dictionary<string, string> { ["DBREEZE_TEST_ROOT"] = testRoot });
    }

    private static AuditApiManifest RunApiWorker(string worker, string variant, AuditComparisonOptions options,
        AuditRunLayout layout, AuditLog log)
    {
        string output = Path.Combine(layout.ReportsDirectory, $"api-{variant}.json");
        RunWorker(worker, new[]
        {
            "--audit-worker", "api", "--variant", variant, "--output", output,
            "--profile", options.Profile.ToString().ToLowerInvariant(), "--max-records",
            options.MaxRecords.ToString(CultureInfo.InvariantCulture), "--round", "0",
        }, options, log);
        return AuditPersistence.ReadJson<AuditApiManifest>(output);
    }

    private static AuditCorrectnessReport RunCorrectnessWorker(string worker, string variant,
        AuditComparisonOptions options, AuditRunLayout layout, AuditLog log)
    {
        string output = Path.Combine(layout.ReportsDirectory, $"correctness-{variant}.json");
        string root = Path.Combine(layout.ScratchDirectory, "correctness", variant);
        RunWorker(worker, new[]
        {
            "--audit-worker", "correctness", "--variant", variant, "--output", output,
            "--root", root, "--profile", options.Profile.ToString().ToLowerInvariant(),
            "--max-records", options.MaxRecords.ToString(CultureInfo.InvariantCulture), "--round", "0",
        }, options, log);
        return AuditPersistence.ReadJson<AuditCorrectnessReport>(output);
    }

    private static AuditCompatibilityReport RunCompatibility(BuildOutputs builds, AuditComparisonOptions options,
        AuditRunLayout layout, AuditLog log)
    {
        var report = new AuditCompatibilityReport();
        string compatRoot = Path.Combine(layout.ScratchDirectory, "compatibility");
        string manifestRoot = Path.Combine(layout.ReportsDirectory, "compatibility-manifests");
        Directory.CreateDirectory(manifestRoot);

        AuditDiskManifest oldCreated = RunDisk(builds.BaselineWorker, "create",
            Path.Combine(compatRoot, "old-created"), Path.Combine(manifestRoot, "old-created-base.json"), null,
            options, log);
        AuditDiskManifest oldReadByNew = RunDisk(builds.CurrentWorker, "verify-base",
            oldCreated.DatabasePath, Path.Combine(manifestRoot, "old-created-new-read.json"), null, options, log);
        AddCompatibilityStep(report, "old-create-new-read", "old", "new", "read base", oldCreated, oldReadByNew,
            requirePhysicalIdentity: true);
        AuditDiskManifest oldExtendedByNew = RunDisk(builds.CurrentWorker, "extend", oldCreated.DatabasePath,
            Path.Combine(manifestRoot, "old-created-new-extended.json"), null, options, log);
        AuditDiskManifest oldExtendedReadByOld = RunDisk(builds.BaselineWorker, "verify-extended",
            oldCreated.DatabasePath, Path.Combine(manifestRoot, "old-created-new-extended-old-read.json"), null,
            options, log);
        AddCompatibilityStep(report, "new-extend-old-read", "new", "old", "read extended", oldExtendedByNew,
            oldExtendedReadByOld, requirePhysicalIdentity: true);

        AuditDiskManifest newCreated = RunDisk(builds.CurrentWorker, "create",
            Path.Combine(compatRoot, "new-created"), Path.Combine(manifestRoot, "new-created-base.json"), null,
            options, log);
        AuditDiskManifest newReadByOld = RunDisk(builds.BaselineWorker, "verify-base", newCreated.DatabasePath,
            Path.Combine(manifestRoot, "new-created-old-read.json"), null, options, log);
        AddCompatibilityStep(report, "new-create-old-read", "new", "old", "read base", newCreated, newReadByOld,
            requirePhysicalIdentity: true);
        AuditDiskManifest newExtendedByOld = RunDisk(builds.BaselineWorker, "extend", newCreated.DatabasePath,
            Path.Combine(manifestRoot, "new-created-old-extended.json"), null, options, log);
        AuditDiskManifest newExtendedReadByNew = RunDisk(builds.CurrentWorker, "verify-extended",
            newCreated.DatabasePath, Path.Combine(manifestRoot, "new-created-old-extended-new-read.json"), null,
            options, log);
        AddCompatibilityStep(report, "old-extend-new-read", "old", "new", "read extended", newExtendedByOld,
            newExtendedReadByNew, requirePhysicalIdentity: true);

        RecordIndependentPhysicalDifferences(report, oldCreated, newCreated);

        string oldBackup = Path.Combine(compatRoot, "old-backup-files");
        AuditDiskManifest oldBackupSource = RunDisk(builds.BaselineWorker, "create-backup",
            Path.Combine(compatRoot, "old-backup-source"), Path.Combine(manifestRoot, "old-backup-source.json"),
            oldBackup, options, log);
        AuditDiskManifest oldBackupRestoredByNew = RunDisk(builds.CurrentWorker, "restore-backup",
            Path.Combine(compatRoot, "old-backup-restored-new"),
            Path.Combine(manifestRoot, "old-backup-restored-new.json"), oldBackup, options, log);
        AddCompatibilityStep(report, "old-backup-new-restore", "old", "new", "backup restore", oldBackupSource,
            oldBackupRestoredByNew, requirePhysicalIdentity: false);

        string newBackup = Path.Combine(compatRoot, "new-backup-files");
        AuditDiskManifest newBackupSource = RunDisk(builds.CurrentWorker, "create-backup",
            Path.Combine(compatRoot, "new-backup-source"), Path.Combine(manifestRoot, "new-backup-source.json"),
            newBackup, options, log);
        AuditDiskManifest newBackupRestoredByOld = RunDisk(builds.BaselineWorker, "restore-backup",
            Path.Combine(compatRoot, "new-backup-restored-old"),
            Path.Combine(manifestRoot, "new-backup-restored-old.json"), newBackup, options, log);
        AddCompatibilityStep(report, "new-backup-old-restore", "new", "old", "backup restore", newBackupSource,
            newBackupRestoredByOld, requirePhysicalIdentity: false);

        AuditDiskManifest oldJournal = RunDisk(builds.BaselineWorker, "create-journal",
            Path.Combine(compatRoot, "old-journal"), Path.Combine(manifestRoot, "old-journal-pending.json"),
            null, options, log);
        AuditDiskManifest oldJournalRecoveredByNew = RunDisk(builds.CurrentWorker, "verify-journal",
            oldJournal.DatabasePath, Path.Combine(manifestRoot, "old-journal-recovered-new.json"),
            null, options, log);
        AddCompatibilityStep(report, "old-journal-new-recovery", "old", "new", "journal recovery",
            oldJournal, oldJournalRecoveredByNew, requirePhysicalIdentity: false);

        AuditDiskManifest newJournal = RunDisk(builds.CurrentWorker, "create-journal",
            Path.Combine(compatRoot, "new-journal"), Path.Combine(manifestRoot, "new-journal-pending.json"),
            null, options, log);
        AuditDiskManifest newJournalRecoveredByOld = RunDisk(builds.BaselineWorker, "verify-journal",
            newJournal.DatabasePath, Path.Combine(manifestRoot, "new-journal-recovered-old.json"),
            null, options, log);
        AddCompatibilityStep(report, "new-journal-old-recovery", "new", "old", "journal recovery",
            newJournal, newJournalRecoveredByOld, requirePhysicalIdentity: false);

        report.Passed = report.Steps.Count == 8 && report.Steps.All(static step => step.Passed);
        return report;
    }

    private static AuditDiskManifest RunDisk(string worker, string action, string database, string output,
        string backup, AuditComparisonOptions options, AuditLog log)
    {
        var arguments = new List<string>
        {
            "--disk-compat", action, "--database", database, "--output", output,
        };
        if (backup != null)
        {
            arguments.Add("--backup");
            arguments.Add(backup);
        }
        RunWorker(worker, arguments, options, log);
        AuditDiskManifest manifest = AuditPersistence.ReadJson<AuditDiskManifest>(output);
        manifest.ManifestPath = output;
        return manifest;
    }

    private static void AddCompatibilityStep(AuditCompatibilityReport report, string id, string producer,
        string consumer, string action, AuditDiskManifest before, AuditDiskManifest after,
        bool requirePhysicalIdentity)
    {
        bool logical = before.State == after.State && before.RowCount == after.RowCount &&
                       before.Checksum == after.Checksum;
        bool physical = PhysicalIdentity(before, after);
        report.Steps.Add(new AuditCompatibilityStep
        {
            Id = id,
            Producer = producer,
            Consumer = consumer,
            Action = action,
            Passed = logical && (!requirePhysicalIdentity || physical),
            ReadOnlyBytesUnchanged = physical,
            RowCount = after.RowCount,
            Checksum = after.Checksum,
            TotalBytes = after.TotalBytes,
            ManifestPath = after.ManifestPath,
            Error = logical ? requirePhysicalIdentity && !physical ? "Read-only verification changed files." : null
                : "Logical row count/checksum differs.",
        });
    }

    private static void RecordIndependentPhysicalDifferences(AuditCompatibilityReport report,
        AuditDiskManifest oldManifest, AuditDiskManifest newManifest)
    {
        var oldFiles = oldManifest.Files.ToDictionary(static file => file.Path, StringComparer.Ordinal);
        var newFiles = newManifest.Files.ToDictionary(static file => file.Path, StringComparer.Ordinal);
        foreach (string path in oldFiles.Keys.Union(newFiles.Keys, StringComparer.Ordinal)
                     .OrderBy(static value => value, StringComparer.Ordinal))
        {
            if (!oldFiles.TryGetValue(path, out AuditDiskFile oldFile))
                report.PhysicalDifferences.Add($"Only current: {path}");
            else if (!newFiles.TryGetValue(path, out AuditDiskFile newFile))
                report.PhysicalDifferences.Add($"Only baseline: {path}");
            else if (oldFile.Length != newFile.Length || oldFile.Sha256 != newFile.Sha256)
                report.PhysicalDifferences.Add($"{path}: old {oldFile.Length}/{oldFile.Sha256}, new {newFile.Length}/{newFile.Sha256}");
        }
    }

    private static bool PhysicalIdentity(AuditDiskManifest left, AuditDiskManifest right)
    {
        if (left.TotalBytes != right.TotalBytes || left.Files.Count != right.Files.Count)
            return false;
        var rightFiles = right.Files.ToDictionary(static file => file.Path, StringComparer.Ordinal);
        return left.Files.All(file => rightFiles.TryGetValue(file.Path, out AuditDiskFile other) &&
                                          file.Length == other.Length && file.Sha256 == other.Sha256);
    }

    private static void RunPrimaryPerformanceRounds(BuildOutputs builds, AuditComparisonOptions options,
        AuditRunLayout layout, AuditRunReport report, AuditLog log)
    {
        (string Variant, string Worker)[][] order =
        {
            new[] { ("old", builds.BaselineWorker), ("new", builds.CurrentWorker) },
            new[] { ("new", builds.CurrentWorker), ("old", builds.BaselineWorker) },
            new[] { ("old", builds.BaselineWorker), ("new", builds.CurrentWorker) },
        };
        for (int round = 1; round <= order.Length; round++)
        {
            foreach ((string variant, string worker) in order[round - 1])
                RunPerformanceWorker(worker, variant, round, options, layout, report, log, null);
        }
    }

    private static void RunConfirmationRounds(BuildOutputs builds, AuditComparisonOptions options,
        AuditRunLayout layout, AuditRunReport report, AuditLog log, HashSet<string> scenarios)
    {
        (string Variant, string Worker)[][] order =
        {
            new[] { ("new", builds.CurrentWorker), ("old", builds.BaselineWorker) },
            new[] { ("old", builds.BaselineWorker), ("new", builds.CurrentWorker) },
        };
        for (int index = 0; index < order.Length; index++)
        {
            int round = 4 + index;
            foreach ((string variant, string worker) in order[index])
                RunPerformanceWorker(worker, variant, round, options, layout, report, log, scenarios);
        }
    }

    private static void RunPerformanceWorker(string worker, string variant, int round,
        AuditComparisonOptions options, AuditRunLayout layout, AuditRunReport report, AuditLog log,
        HashSet<string> scenarios)
    {
        string rawRoot = Path.Combine(layout.ReportsDirectory, "performance-raw");
        string output = Path.Combine(rawRoot, $"performance-{variant}-round-{round}.json");
        string root = Path.Combine(layout.ScratchDirectory, "performance", $"{variant}-round-{round}");
        var arguments = new List<string>
        {
            "--audit-worker", "performance", "--variant", variant, "--output", output,
            "--root", root, "--profile", options.Profile.ToString().ToLowerInvariant(),
            "--max-records", options.MaxRecords.ToString(CultureInfo.InvariantCulture),
            "--round", round.ToString(CultureInfo.InvariantCulture),
        };
        if (scenarios != null && scenarios.Count != 0)
        {
            arguments.Add("--scenarios");
            arguments.Add(String.Join(';', scenarios.OrderBy(static value => value, StringComparer.Ordinal)));
        }
        report.Metadata.VariantOrder.Add($"round-{round}:{variant}");
        log.Write($"PERFORMANCE round={round} variant={variant} scenarios={(scenarios?.Count.ToString() ?? "all")}");
        RunWorker(worker, arguments, options, log);
        AuditPerformanceReport result = AuditPersistence.ReadJson<AuditPerformanceReport>(output);
        report.Measurements.AddRange(result.Measurements);
    }

    private static void EvaluateGates(AuditRunReport report)
    {
        if (report.ApiComparison?.BackwardCompatible != true)
            report.GateViolations.Add("Public API is not backward compatible with the baseline.");
        if (report.ApiComparison?.CompleteCoverage != true)
            report.GateViolations.Add("Public API coverage manifest contains unmapped records.");
        if (report.CorrectnessComparison?.Passed != true)
            report.GateViolations.Add("Correctness parity failed.");
        if (report.Compatibility?.Passed != true)
            report.GateViolations.Add("Bidirectional file compatibility failed.");
        foreach (AuditMeasurement failed in report.Measurements.Where(static item => !item.Succeeded))
            report.GateViolations.Add($"Benchmark failed: {failed.Category}/{failed.Scenario}/w{failed.Workers}.");
        foreach (AuditPerformanceComparison failed in report.Performance.Where(static item =>
                     !item.SpeedGatePassed || !item.AllocationGatePassed))
        {
            report.GateViolations.Add($"{failed.Verdict}: {failed.Category}/{failed.Scenario}/w{failed.Workers}.");
        }
        if (report.Compatibility?.PhysicalDifferences.Count > 0)
            report.Warnings.Add("Independently created old/new databases are not byte-identical; logical compatibility is the selected gate.");
        if (report.ApiComparison?.AddedRecords.Count > 0)
            report.Warnings.Add($"Current assembly adds {report.ApiComparison.AddedRecords.Count} non-vector API records.");
        report.Passed = report.GateViolations.Count == 0 && String.IsNullOrEmpty(report.Failure);
    }

    private static void PersistFinalReports(AuditRunReport report, AuditRunLayout layout,
        AuditComparisonOptions options, AuditLog log)
    {
        AuditPersistence.WriteJson(Path.Combine(layout.ReportsDirectory, "results.json"), report);
        AuditReportArtifacts.WritePerformanceCsv(Path.Combine(layout.ReportsDirectory, "performance.csv"), report);
        AuditHtmlReportWriter.Write(options.ReportPath, report);
        log?.Write("REPORTS " + layout.ReportsDirectory + "; html=" + options.ReportPath);
    }

    private static void RunWorker(string worker, IEnumerable<string> arguments, AuditComparisonOptions options,
        AuditLog log)
    {
        var all = new List<string> { worker };
        all.AddRange(arguments);
        RunProcess("dotnet", all, options.CurrentRepository, log);
    }

    private static string Git(string repository, AuditLog log, params string[] arguments)
    {
        var all = new List<string> { "-C", repository };
        all.AddRange(arguments);
        return RunProcess("git", all, repository, log).StandardOutput;
    }

    private static AuditProcessResult RunProcess(string fileName, IEnumerable<string> arguments,
        string workingDirectory, AuditLog log, IReadOnlyDictionary<string, string> environment = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        if (environment != null)
            foreach ((string key, string value) in environment)
                startInfo.Environment[key] = value;
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Cannot start {fileName}.");
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();
        if (!String.IsNullOrWhiteSpace(stdout))
            log?.Write(stdout.TrimEnd());
        if (!String.IsNullOrWhiteSpace(stderr))
            log?.Write("STDERR " + stderr.TrimEnd());
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Process failed ({process.ExitCode}): {fileName} {String.Join(' ', arguments)}\n{stderr}\n{stdout}");
        return new AuditProcessResult(process.ExitCode, stdout, stderr);
    }

    private static string ComputeBenchmarkSourceHash(string repository)
    {
        string root = Path.Combine(repository, "DBreeze.Net8.Benchmarks");
        string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(static path => (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                                   path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) &&
                                  !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                                      StringComparison.OrdinalIgnoreCase) &&
                                  !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                                      StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string file in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file).Replace('\\', '/')));
            hash.AppendData(File.ReadAllBytes(file));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string ComputeSha256(byte[] value) => Convert.ToHexString(SHA256.HashData(value));

    private readonly record struct BuildOutputs(string BaselineLibrary, string CurrentLibrary,
        string BaselineWorker, string CurrentWorker, string CurrentTestRunner);

    private readonly record struct AuditProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class AuditDiskManifest
    {
        public string State { get; set; }
        public string DatabasePath { get; set; }
        public string DBreezeAssemblyVersion { get; set; }
        public long RowCount { get; set; }
        public long Checksum { get; set; }
        public long TotalBytes { get; set; }
        public List<AuditDiskFile> Files { get; set; } = new();
        public string ManifestPath { get; set; }
    }

    private sealed class AuditDiskFile
    {
        public string Path { get; set; }
        public long Length { get; set; }
        public string Sha256 { get; set; }
    }
}

internal sealed class AuditLog : IDisposable
{
    private readonly object _sync = new();
    private readonly StreamWriter _writer;

    internal AuditLog(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Log path must have a parent directory."));
        _writer = new StreamWriter(path, append: false, new UTF8Encoding(false)) { AutoFlush = true };
    }

    internal void Write(string value)
    {
        string text = $"[{DateTime.UtcNow:O}] {value}";
        lock (_sync)
        {
            Console.WriteLine(text);
            _writer.WriteLine(text);
        }
    }

    public void Dispose()
    {
        lock (_sync)
            _writer.Dispose();
    }
}
