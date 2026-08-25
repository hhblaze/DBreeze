using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DBreeze.ReleaseAudit.Protocol;

namespace DBreeze.Net8.Benchmarks;

internal static class ReleaseAuditOrchestrator
{
    internal static int Run(string[] args)
    {
        ReleaseAuditOptions options;
        try { options = ReleaseAuditOptions.Parse(args); }
        catch (Exception error) { Console.Error.WriteLine(error); return 2; }

        var layout = new AuditRunLayout(options.Root, options.RunId);
        var report = CreateReport(options, layout);
        var deadline = new ReleaseDeadline(TimeSpan.FromMinutes(options.BudgetMinutes));
        AuditLog log = null;
        bool created = false;
        bool setupComplete = false;
        bool evaluated = false;
        try
        {
            layout.Create(); created = true;
            log = new AuditLog(Path.Combine(layout.ReportsDirectory, "run.log"));
            log.Write("START release audit " + options.RunId + "; profile=" + options.Profile);
            ValidateRepositories(options, report, log, deadline);
            report.Metadata.DotNetSdk = RequireProcess("dotnet", new[] { "--version" }, options.CurrentRepository, log, deadline).StandardOutput.Trim();
            report.Metadata.FullMsBuild = FindMsBuild();
            ReleaseBuildSet builds = BuildAll(options, layout, report, log, deadline);
            setupComplete = true;

            RunPrerequisites(options, layout, builds, report, log, deadline);
            RunApi(options, layout, builds, report, log, deadline);
            RunCorrectness(options, layout, builds, report, log, deadline);
            RunCompatibility(options, layout, builds, report, log, deadline);
            RunPerformance(options, layout, builds, report, log, deadline);
            Evaluate(report, options);
            evaluated = true;
        }
        catch (ReleaseAuditTimeoutException error)
        {
            report.Incomplete = true;
            report.Failure = error.ToString();
            report.GateViolations.Add("Global audit timeout: " + error.Message);
            log?.Write("TIMEOUT " + error);
        }
        catch (Exception error)
        {
            report.Incomplete = true;
            report.Failure = error.ToString();
            report.GateViolations.Add((setupComplete ? "Audit stage failure: " : "Configuration/build failure: ") + error.Message);
            log?.Write("FAILED " + error);
        }
        finally
        {
            report.Metadata.CompletedUtc = DateTime.UtcNow;
            if (created)
            {
                try
                {
                    if (setupComplete && !evaluated)
                    {
                        Evaluate(report, options);
                        evaluated = true;
                    }
                    report.Metadata.BaselineFingerprintAfter = SourceFingerprint(options.BaselineRepository);
                    report.Metadata.CurrentFingerprintAfter = SourceFingerprint(options.CurrentRepository);
                    if (!String.Equals(report.Metadata.BaselineFingerprintBefore, report.Metadata.BaselineFingerprintAfter, StringComparison.Ordinal))
                        report.GateViolations.Add("Baseline source fingerprint changed during the run.");
                    if (!String.Equals(report.Metadata.CurrentFingerprintBefore, report.Metadata.CurrentFingerprintAfter, StringComparison.Ordinal))
                        report.GateViolations.Add("Current source fingerprint changed during the run.");
                    FinalizeVerdict(report, options);
                    ReleaseAuditArtifacts.Write(report, options, layout);
                }
                catch (Exception persistError)
                {
                    report.Passed = false;
                    report.Incomplete = true;
                    Console.Error.WriteLine(persistError);
                    log?.Write("REPORT FAILED " + persistError);
                }
                if (!options.KeepDatabases)
                {
                    try { layout.CleanupScratch(); log?.Write("CLEANUP scratch complete"); }
                    catch (Exception cleanupError)
                    {
                        report.Passed = false;
                        report.GateViolations.Add("Scratch cleanup failed: " + cleanupError.Message);
                        log?.Write("CLEANUP FAILED " + cleanupError);
                        try { ReleaseAuditArtifacts.Write(report, options, layout); } catch { }
                    }
                }
            }
            log?.Dispose();
        }
        if (!setupComplete && report.Incomplete) return 2;
        return report.Passed || options.Profile == "smoke" && report.GateViolations.Count == 0 ? 0 : 1;
    }

    private static ReleaseAuditReport CreateReport(ReleaseAuditOptions options, AuditRunLayout layout)
    {
        return new ReleaseAuditReport
        {
            Metadata = new ReleaseAuditMetadata
            {
                RunId = options.RunId, Profile = options.Profile, StartedUtc = DateTime.UtcNow,
                BudgetMinutes = options.BudgetMinutes, MaxRecords = options.MaxRecords,
                MaxTextRecords = options.MaxTextRecords, MaxVectorRecords = options.MaxVectorRecords,
                BaselineRepository = options.BaselineRepository, BaselineCommit = options.ExpectedBaseline,
                CurrentRepository = options.CurrentRepository,
                Runtime = RuntimeInformation.FrameworkDescription + " / " + Environment.Version,
                OperatingSystem = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? String.Empty,
                LogicalProcessors = Environment.ProcessorCount,
                Gc = (GCSettings.IsServerGC ? "Server" : "Workstation") + " / " + GCSettings.LatencyMode,
                ScratchDirectory = layout.ScratchDirectory, ReportsDirectory = layout.ReportsDirectory,
                CanonicalHtml = options.Report, ReproductionCommand = options.ReproductionCommand
            }
        };
    }

    private static void ValidateRepositories(ReleaseAuditOptions options, ReleaseAuditReport report, AuditLog log, ReleaseDeadline deadline)
    {
        foreach (string path in new[]
        {
            Path.Combine(options.BaselineRepository, "DBreeze.Net8", "DBreeze.Net8.csproj"),
            Path.Combine(options.BaselineRepository, "DBreeze", "DBreeze.csproj"),
            Path.Combine(options.CurrentRepository, "DBreeze.Net8", "DBreeze.Net8.csproj"),
            Path.Combine(options.CurrentRepository, "DBreeze", "DBreeze.csproj"),
            Path.Combine(options.CurrentRepository, "DBreeze.ReleaseAudit.Worker", "DBreeze.ReleaseAudit.Worker.csproj")
        }) if (!File.Exists(path)) throw new FileNotFoundException("Required audit project was not found.", path);

        string baselineHead = Git(options.BaselineRepository, log, deadline, "rev-parse", "HEAD").Trim();
        if (!String.Equals(baselineHead, options.ExpectedBaseline, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Baseline commit mismatch: expected " + options.ExpectedBaseline + ", actual " + baselineHead);
        string baselineStatus = Git(options.BaselineRepository, log, deadline, "status", "--porcelain", "--untracked-files=no");
        if (!String.IsNullOrWhiteSpace(baselineStatus)) throw new InvalidOperationException("Baseline has tracked changes:\n" + baselineStatus);
        string currentHead = Git(options.CurrentRepository, log, deadline, "rev-parse", "HEAD").Trim();
        string currentStatus = Git(options.CurrentRepository, log, deadline, "status", "--porcelain", "--untracked-files=all");
        bool dirty = !String.IsNullOrWhiteSpace(currentStatus);
        if (options.Profile == "full" && dirty && !options.AllowDirtyCurrent)
            throw new InvalidOperationException("Full audit requires a clean current repository or explicit --allow-dirty-current.");

        report.Metadata.BaselineCommit = baselineHead;
        report.Metadata.CurrentCommit = currentHead;
        report.Metadata.CurrentDirty = dirty;
        report.Metadata.CurrentDirtyFingerprint = Sha256(Encoding.UTF8.GetBytes(currentStatus + "\n" + SourceFingerprint(options.CurrentRepository)));
        report.Metadata.BaselineFingerprintBefore = SourceFingerprint(options.BaselineRepository);
        report.Metadata.CurrentFingerprintBefore = SourceFingerprint(options.CurrentRepository);
        log.Write("REPOSITORIES baseline=" + baselineHead + "; current=" + currentHead + "; dirty=" + dirty);
    }

    private static ReleaseBuildSet BuildAll(ReleaseAuditOptions options, AuditRunLayout layout, ReleaseAuditReport report, AuditLog log, ReleaseDeadline deadline)
    {
        string root = Path.Combine(layout.ScratchDirectory, "build");
        string protocol = BuildDotNet(Path.Combine(options.CurrentRepository, "DBreeze.ReleaseAudit.Protocol", "DBreeze.ReleaseAudit.Protocol.csproj"),
            Path.Combine(root, "protocol"), "DBreeze.ReleaseAudit.Protocol.dll", null, options.CurrentRepository, log, deadline, out int protocolWarnings);
        var builds = new ReleaseBuildSet();
        foreach (string variant in new[] { "baseline", "current" })
        {
            string repository = variant == "baseline" ? options.BaselineRepository : options.CurrentRepository;
            string net8Library = BuildDotNet(Path.Combine(repository, "DBreeze.Net8", "DBreeze.Net8.csproj"),
                Path.Combine(root, variant + "-net8-library"), "DBreeze.dll", null, repository, log, deadline, out int net8Warnings);
            string net472Library = BuildFramework(Path.Combine(repository, "DBreeze", "DBreeze.csproj"),
                Path.Combine(root, variant + "-net472-library"), "DBreeze.dll", null, repository, log, deadline, out int net472Warnings);
            builds.Add(BuildWorker(options, root, variant, "net8", net8Library, protocol, net8Warnings, log, deadline));
            builds.Add(BuildWorker(options, root, variant, "net472", net472Library, protocol, net472Warnings, log, deadline));
        }
        report.Builds.AddRange(builds.Values.Select(static value => value.Build));
        return builds;
    }

    private static BuiltWorker BuildWorker(ReleaseAuditOptions options, string root, string variant, string framework,
        string library, string protocol, int libraryWarnings, AuditLog log, ReleaseDeadline deadline)
    {
        string project = Path.Combine(options.CurrentRepository, "DBreeze.ReleaseAudit.Worker", "DBreeze.ReleaseAudit.Worker.csproj");
        string buildRoot = Path.Combine(root, variant + "-" + framework + "-worker");
        var properties = new Dictionary<string, string>
        {
            ["DBreezeAssemblyReference"] = library,
            ["ReleaseAuditProtocolReference"] = protocol,
            ["AuditTarget"] = framework == "net8" ? "Net8" : "NetFramework"
        };
        int warnings;
        string worker = framework == "net8"
            ? BuildDotNet(project, buildRoot, "DBreeze.ReleaseAudit.Worker.dll", properties, options.CurrentRepository, log, deadline, out warnings)
            : BuildFramework(project, buildRoot, "DBreeze.ReleaseAudit.Worker.exe", properties, options.CurrentRepository, log, deadline, out warnings);
        var build = new ReleaseBuild
        {
            Key = variant + "-" + framework, Variant = variant, Framework = framework,
            Library = library, LibrarySha256 = Sha256(library), Worker = worker, WorkerSha256 = Sha256(worker),
            WarningCount = libraryWarnings + warnings
        };
        return new BuiltWorker(build);
    }

    private static string BuildDotNet(string project, string root, string assembly, IReadOnlyDictionary<string, string> properties,
        string workingDirectory, AuditLog log, ReleaseDeadline deadline, out int warnings)
    {
        string output = Path.Combine(root, "out") + Path.DirectorySeparatorChar;
        string intermediate = Path.Combine(root, "obj") + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        var args = new List<string> { "build", project, "-c", "Release", "--nologo", "-v:minimal",
            "-p:OutputPath=" + output, "-p:IntermediateOutputPath=" + intermediate,
            "-p:UseSharedCompilation=false", "-p:SignAssembly=false", "-p:NuGetAudit=false" };
        if (properties != null) args.AddRange(properties.Select(static pair => "-p:" + pair.Key + "=" + pair.Value));
        ProcessResult result = RequireProcess("dotnet", args, workingDirectory, log, deadline);
        warnings = CountWarnings(result);
        return Locate(output, assembly);
    }

    private static string BuildFramework(string project, string root, string assembly, IReadOnlyDictionary<string, string> properties,
        string workingDirectory, AuditLog log, ReleaseDeadline deadline, out int warnings)
    {
        string output = Path.Combine(root, "out") + Path.DirectorySeparatorChar;
        string intermediate = Path.Combine(root, "obj") + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        var args = new List<string> { project, "/t:Restore,Rebuild", "/p:Configuration=Release", "/p:Platform=AnyCPU",
            "/p:OutputPath=" + output, "/p:IntermediateOutputPath=" + intermediate,
            "/p:UseSharedCompilation=false", "/p:SignAssembly=false", "/p:NuGetAudit=false", "/nologo", "/v:minimal" };
        if (properties != null) args.AddRange(properties.Select(static pair => "/p:" + pair.Key + "=" + pair.Value));
        ProcessResult result = RequireProcess(FindMsBuild(), args, workingDirectory, log, deadline);
        warnings = CountWarnings(result);
        return Locate(output, assembly);
    }

    private static void RunPrerequisites(ReleaseAuditOptions options, AuditRunLayout layout, ReleaseBuildSet builds,
        ReleaseAuditReport report, AuditLog log, ReleaseDeadline deadline)
    {
        List<string> evaluatorFailures = ReleaseAuditEvaluator.SelfTest();
        report.Prerequisites.Add(new ReleasePrerequisite
        {
            Id = "release-audit-self-tests", Passed = evaluatorFailures.Count == 0,
            ExitCode = evaluatorFailures.Count == 0 ? 0 : 1,
            Detail = evaluatorFailures.Count == 0 ? "thresholds/coverage/allowlist/timeout/process/limits/path/hash/HTML" : String.Join("; ", evaluatorFailures)
        });
        foreach (string failure in evaluatorFailures)
            report.GateViolations.Add("Release-audit self-test failed: " + failure);
        foreach (BuiltWorker build in builds.Values)
        {
            string selfTestRoot = Path.Combine(layout.ScratchDirectory, "self-tests", build.Build.Key);
            WorkerReport selfTest = RunWorker(build, "self-test", selfTestRoot, options, layout, log, deadline,
                "self-test-" + build.Build.Key);
            report.Prerequisites.Add(new ReleasePrerequisite
            {
                Id = "worker-self-test-" + build.Build.Key, Passed = selfTest.Succeeded,
                ExitCode = selfTest.Succeeded ? 0 : 1,
                Detail = String.Join("; ", selfTest.Cases.Select(static value => value.Id + "=" + (value.Succeeded ? "PASS" : "FAIL")))
            });
            if (!selfTest.Succeeded)
                report.GateViolations.Add("Worker self-test failed: " + build.Build.Key + ".");
        }
        BuiltWorker net8 = builds["current-net8"];
        int warnings;
        string tests = BuildDotNet(Path.Combine(options.CurrentRepository, "DBreeze.Net8.Tests", "DBreeze.Net8.Tests.csproj"),
            Path.Combine(layout.ScratchDirectory, "build", "current-net8-tests"), "DBreeze.Net8.Tests.dll",
            new Dictionary<string, string> { ["DBreezeAssemblyReference"] = net8.Build.Library }, options.CurrentRepository, log, deadline, out warnings);
        ProcessResult testsResult = RunProcess("dotnet", new[] { tests }, options.CurrentRepository, log, deadline,
            new Dictionary<string, string> { ["DBREEZE_TEST_ROOT"] = Path.Combine(layout.ScratchDirectory, "prerequisites", "net8-tests") });
        AddPrerequisite(report, "current-net8-regressions", testsResult);

        foreach (string framework in new[] { "net8", "net472" })
        {
            BuiltWorker build = builds["current-" + framework];
            string storageRoot = Path.Combine(layout.ScratchDirectory, "build", "storage-contracts-" + framework);
            var props = new Dictionary<string, string>
            {
                ["DBreezeAssemblyReference"] = build.Build.Library,
                ["StorageTarget"] = framework == "net8" ? "Net8" : "NetFramework"
            };
            string runner = framework == "net8"
                ? BuildDotNet(Path.Combine(options.CurrentRepository, "DBreeze.Storage.Contracts", "DBreeze.Storage.Contracts.csproj"), storageRoot,
                    "DBreeze.Storage.Contracts.dll", props, options.CurrentRepository, log, deadline, out warnings)
                : BuildFramework(Path.Combine(options.CurrentRepository, "DBreeze.Storage.Contracts", "DBreeze.Storage.Contracts.csproj"), storageRoot,
                    "DBreeze.Storage.Contracts.exe", props, options.CurrentRepository, log, deadline, out warnings);
            ProcessResult result = framework == "net8"
                ? RunProcess("dotnet", new[] { runner, "--storage-contracts" }, options.CurrentRepository, log, deadline)
                : RunProcess(runner, new[] { "--storage-contracts" }, options.CurrentRepository, log, deadline);
            AddPrerequisite(report, "current-storage-contracts-" + framework, result);

            string durabilityLibraryRoot = Path.Combine(layout.ScratchDirectory, "build", "durability-library-" + framework);
            var durabilityLibraryProps = new Dictionary<string, string>
            {
                ["DBreezeDurabilityTestHooks"] = "true"
            };
            string durabilityLibrary = framework == "net8"
                ? BuildDotNet(Path.Combine(options.CurrentRepository, "DBreeze.Net8", "DBreeze.Net8.csproj"),
                    durabilityLibraryRoot, "DBreeze.dll", durabilityLibraryProps,
                    options.CurrentRepository, log, deadline, out warnings)
                : BuildFramework(Path.Combine(options.CurrentRepository, "DBreeze", "DBreeze.csproj"),
                    durabilityLibraryRoot, "DBreeze.dll", durabilityLibraryProps,
                    options.CurrentRepository, log, deadline, out warnings);

            string durabilityContractsRoot = Path.Combine(layout.ScratchDirectory, "build", "durability-contracts-" + framework);
            var durabilityContractsProps = new Dictionary<string, string>
            {
                ["DBreezeAssemblyReference"] = durabilityLibrary,
                ["StorageTarget"] = framework == "net8" ? "Net8" : "NetFramework"
            };
            string durabilityRunner = framework == "net8"
                ? BuildDotNet(Path.Combine(options.CurrentRepository, "DBreeze.Storage.Contracts", "DBreeze.Storage.Contracts.csproj"),
                    durabilityContractsRoot, "DBreeze.Storage.Contracts.dll", durabilityContractsProps,
                    options.CurrentRepository, log, deadline, out warnings)
                : BuildFramework(Path.Combine(options.CurrentRepository, "DBreeze.Storage.Contracts", "DBreeze.Storage.Contracts.csproj"),
                    durabilityContractsRoot, "DBreeze.Storage.Contracts.exe", durabilityContractsProps,
                    options.CurrentRepository, log, deadline, out warnings);
            ProcessResult durabilityResult = framework == "net8"
                ? RunProcess("dotnet", new[] { durabilityRunner, "--durability-crash-contracts" },
                    options.CurrentRepository, log, deadline)
                : RunProcess(durabilityRunner, new[] { "--durability-crash-contracts" },
                    options.CurrentRepository, log, deadline);
            AddPrerequisite(report, "current-durability-crash-contracts-" + framework, durabilityResult);
        }
    }

    private static void AddPrerequisite(ReleaseAuditReport report, string id, ProcessResult result)
    {
        report.Prerequisites.Add(new ReleasePrerequisite
        {
            Id = id, Passed = result.ExitCode == 0, ExitCode = result.ExitCode,
            Detail = result.ExitCode == 0 ? "process completed successfully" : result.StandardError
        });
        if (result.ExitCode != 0) report.GateViolations.Add("Prerequisite failed: " + id + ".");
    }

    private static void RunApi(ReleaseAuditOptions options, AuditRunLayout layout, ReleaseBuildSet builds,
        ReleaseAuditReport report, AuditLog log, ReleaseDeadline deadline)
    {
        foreach (BuiltWorker build in builds.Values)
        {
            WorkerReport worker = RunWorker(build, "api", null, options, layout, log, deadline, "api-" + build.Build.Key);
            report.ApiWorkers.Add(worker);
            if (!worker.Succeeded) report.GateViolations.Add("API worker failed: " + build.Build.Key);
            ValidateLoadedAssembly(build, worker, report);
        }
    }

    private static void RunCorrectness(ReleaseAuditOptions options, AuditRunLayout layout, ReleaseBuildSet builds,
        ReleaseAuditReport report, AuditLog log, ReleaseDeadline deadline)
    {
        foreach (BuiltWorker build in builds.Values)
        {
            string root = Path.Combine(layout.ScratchDirectory, "correctness", build.Build.Key);
            WorkerReport worker = RunWorker(build, "correctness", root, options, layout, log, deadline, "correctness-" + build.Build.Key);
            report.CorrectnessWorkers.Add(worker);
            ValidateLoadedAssembly(build, worker, report);
        }
    }

    private static void RunCompatibility(ReleaseAuditOptions options, AuditRunLayout layout, ReleaseBuildSet builds,
        ReleaseAuditReport report, AuditLog log, ReleaseDeadline deadline)
    {
        string root = Path.Combine(layout.ScratchDirectory, "compatibility");
        foreach (BuiltWorker producer in builds.Values)
        {
            string database = Path.Combine(root, "readonly", producer.Build.Key, "database");
            WorkerReport created = RunWorker(producer, "fixture-create", database, options, layout, log, deadline, "compat-create-" + producer.Build.Key);
            AddFlow(report, "readonly-create-" + producer.Build.Key, "read-only", producer, producer, created, database);
            // Snapshot only after the producer process has exited. TextSearch can finish background
            // maintenance after the worker has serialized its own report but before process exit.
            List<FileEntry> original = CompatibilityFiles(database);
            foreach (BuiltWorker consumer in builds.Values)
            {
                WorkerReport verified = RunWorker(consumer, "fixture-verify", database, options, layout, log, deadline,
                    "compat-read-" + producer.Build.Key + "-by-" + consumer.Build.Key);
                AddFlow(report, "readonly-" + producer.Build.Key + "-by-" + consumer.Build.Key, "read-only", producer, consumer, verified, database);
            }
            List<FileEntry> after = CompatibilityFiles(database);
            if (!FileListsEqual(original, after)) report.GateViolations.Add("4x4 read-only matrix changed files produced by " + producer.Build.Key);
        }

        foreach ((string from, string to) in new[] { ("baseline", "current"), ("current", "baseline") })
        foreach (string producerFramework in new[] { "net8", "net472" })
        foreach (string consumerFramework in new[] { "net8", "net472" })
        {
            BuiltWorker producer = builds[from + "-" + producerFramework];
            BuiltWorker consumer = builds[to + "-" + consumerFramework];
            string suffix = producer.Build.Key + "-to-" + consumer.Build.Key;

            string mutableRoot = Path.Combine(root, "mutable", suffix);
            string original = Path.Combine(mutableRoot, "producer");
            RunWorker(producer, "fixture-create", original, options, layout, log, deadline, "mutable-create-" + suffix);
            string handoff = Path.Combine(mutableRoot, "handoff");
            CopyDirectory(original, handoff);
            WorkerReport extended = RunWorker(consumer, "fixture-extend", handoff, options, layout, log, deadline, "mutable-extend-" + suffix);
            WorkerReport reopened = RunWorker(producer, "fixture-verify-extended", handoff, options, layout, log, deadline, "mutable-reopen-" + suffix);
            AddFlow(report, "mutable-" + suffix, "mutable", producer, consumer, Merge(extended, reopened), handoff);

            string backupRoot = Path.Combine(root, "backup", suffix);
            WorkerReport backup = RunWorker(producer, "backup-create", backupRoot, options, layout, log, deadline, "backup-create-" + suffix);
            WorkerReport restore = RunWorker(consumer, "backup-restore", backupRoot, options, layout, log, deadline, "backup-restore-" + suffix);
            WorkerReport backupReopen = RunWorker(producer, "fixture-verify", Path.Combine(backupRoot, "restored"), options, layout, log, deadline, "backup-reopen-" + suffix);
            AddFlow(report, "backup-" + suffix, "backup", producer, consumer, Merge(backup, restore, backupReopen), Path.Combine(backupRoot, "restored"));

            string journalRoot = Path.Combine(root, "journal", suffix);
            WorkerReport journal = RunWorker(producer, "journal-prepare", journalRoot, options, layout, log, deadline, "journal-prepare-" + suffix);
            WorkerReport recovered = RunWorker(consumer, "journal-recover", journalRoot, options, layout, log, deadline, "journal-recover-" + suffix);
            WorkerReport journalReopen = RunWorker(producer, "journal-recover", journalRoot, options, layout, log, deadline, "journal-reopen-" + suffix);
            AddFlow(report, "journal-" + suffix, "journal", producer, consumer, Merge(journal, recovered, journalReopen), journalRoot);
        }
        report.Warnings.Add("SHA-256 differences between independently created physical files are informational; cross-reader immutability and logical oracles are gates.");
        report.Warnings.Add("Journal cross-baseline compatibility is limited to table names without XML text metacharacters (&, <, >, CR/LF/TAB); current-to-current payloads preserve them by escaping.");
    }

    private static void RunPerformance(ReleaseAuditOptions options, AuditRunLayout layout, ReleaseBuildSet builds,
        ReleaseAuditReport report, AuditLog log, ReleaseDeadline deadline)
    {
        int primaryRounds = options.Profile == "full" ? 3 : 1;
        string smokeScenarios = options.Profile == "smoke"
            ? "disk-crud;text-index-query;vector-float-insert-search"
            : null;
        foreach (string framework in new[] { "net8", "net472" })
        {
            BuiltWorker baseline = builds["baseline-" + framework];
            WorkerReport calibration = RunPerformanceWorker(baseline, 0, null, smokeScenarios, options, layout, log, deadline);
            AddPerformanceWorkerFailures(report, baseline, calibration, "calibration");
            string operations = String.Join(";", calibration.Measurements.Select(static value => value.Scenario + "=" + value.Operations));
            if (calibration.Measurements.Count == 0) report.GateViolations.Add("Performance calibration produced no measurements for " + framework);
            for (int round = 1; round <= primaryRounds; round++)
            {
                BuiltWorker[] order = round % 2 == 1
                    ? new[] { baseline, builds["current-" + framework] }
                    : new[] { builds["current-" + framework], baseline };
                foreach (BuiltWorker worker in order)
                {
                    WorkerReport measured = RunPerformanceWorker(worker, round, operations, smokeScenarios, options, layout, log, deadline);
                    AddPerformanceWorkerFailures(report, worker, measured, "round-" + round);
                    AddSamples(report, worker, measured);
                }
            }
        }
        report.Performance = ReleaseAuditEvaluator.ComparePerformance(report.PerformanceSamples, false, options.Profile == "smoke" ? 1 : 3);
        if (options.Profile == "full")
        {
            foreach (string framework in new[] { "net8", "net472" })
            {
                string[] candidates = report.Performance.Where(value => value.Framework == framework &&
                    (!value.SpeedPassed || !value.AllocationPassed || !value.Complete)).Select(value => value.Scenario).Distinct(StringComparer.Ordinal).ToArray();
                if (candidates.Length == 0) continue;
                string filter = String.Join(";", candidates);
                Dictionary<string, int> plan = report.PerformanceSamples.Where(value => value.Framework == framework)
                    .GroupBy(value => value.Value.Scenario, StringComparer.Ordinal).ToDictionary(group => group.Key, group => (int)group.First().Value.Operations, StringComparer.Ordinal);
                string operations = String.Join(";", plan.Select(static pair => pair.Key + "=" + pair.Value));
                for (int round = 4; round <= 5; round++)
                {
                    BuiltWorker[] order = round % 2 == 0
                        ? new[] { builds["current-" + framework], builds["baseline-" + framework] }
                        : new[] { builds["baseline-" + framework], builds["current-" + framework] };
                    foreach (BuiltWorker worker in order)
                    {
                        WorkerReport measured = RunPerformanceWorker(worker, round, operations, filter, options, layout, log, deadline);
                        AddPerformanceWorkerFailures(report, worker, measured, "round-" + round + "-confirmation");
                        AddSamples(report, worker, measured);
                    }
                }
            }
            report.Performance = ReleaseAuditEvaluator.ComparePerformance(report.PerformanceSamples, true);
        }
    }

    private static WorkerReport RunPerformanceWorker(BuiltWorker worker, int round, string operations, string scenarios,
        ReleaseAuditOptions options, AuditRunLayout layout, AuditLog log, ReleaseDeadline deadline)
    {
        string token = worker.Build.Key + "-r" + round + (String.IsNullOrEmpty(scenarios) ? String.Empty : "-confirm");
        var extra = new List<string> { "--round", round.ToString(CultureInfo.InvariantCulture) };
        if (!String.IsNullOrEmpty(operations)) { extra.Add("--operations"); extra.Add(operations); }
        if (!String.IsNullOrEmpty(scenarios)) { extra.Add("--scenarios"); extra.Add(scenarios); }
        return RunWorker(worker, "performance", Path.Combine(layout.ScratchDirectory, "performance", token), options, layout, log, deadline, "performance-" + token, extra);
    }

    private static void AddSamples(ReleaseAuditReport report, BuiltWorker worker, WorkerReport measured)
    {
        foreach (Measurement value in measured.Measurements)
            report.PerformanceSamples.Add(new ReleasePerformanceSample { Variant = worker.Build.Variant, Framework = worker.Build.Framework, Value = value });
    }

    private static void AddPerformanceWorkerFailures(ReleaseAuditReport report, BuiltWorker worker, WorkerReport measured, string stage)
    {
        foreach (CaseResult failed in measured.Cases.Where(static value => !value.Succeeded))
            report.GateViolations.Add("Performance measurement failed: " + worker.Build.Key + "/" + stage + "/" + failed.Id + ".");
        if (!measured.Succeeded && measured.Cases.All(static value => value.Succeeded))
            report.GateViolations.Add("Performance worker failed: " + worker.Build.Key + "/" + stage + ".");
    }

    private static WorkerReport RunWorker(BuiltWorker worker, string action, string root, ReleaseAuditOptions options,
        AuditRunLayout layout, AuditLog log, ReleaseDeadline deadline, string outputName, IEnumerable<string> extra = null)
    {
        string output = Path.Combine(layout.ReportsDirectory, outputName + ".json");
        var args = new List<string>();
        string executable;
        if (worker.Build.Framework == "net8") { executable = "dotnet"; args.Add(worker.Build.Worker); }
        else executable = worker.Build.Worker;
        args.AddRange(new[] { "--action", action, "--variant", worker.Build.Variant, "--framework", worker.Build.Framework,
            "--output", output, "--profile", options.Profile, "--max-records", options.MaxRecords.ToString(CultureInfo.InvariantCulture),
            "--max-text-records", options.MaxTextRecords.ToString(CultureInfo.InvariantCulture),
            "--max-vector-records", options.MaxVectorRecords.ToString(CultureInfo.InvariantCulture) });
        if (!String.IsNullOrEmpty(root)) { args.Add("--root"); args.Add(root); }
        if (extra != null) args.AddRange(extra);
        ProcessResult process = RunProcess(executable, args, options.CurrentRepository, log, deadline);
        if (!File.Exists(output)) throw new InvalidDataException("Worker did not produce report: " + output + "; exit=" + process.ExitCode);
        WorkerReport report = WireJson.Read<WorkerReport>(output);
        if (process.ExitCode == 0 != report.Succeeded) log.Write("WORKER exit/report mismatch: " + outputName);
        if (!String.Equals(worker.Build.LibrarySha256, report.AssemblySha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Loaded DBreeze.dll SHA mismatch in " + worker.Build.Key + ": " + report.AssemblySha256);
        if (!String.Equals(worker.Build.Framework, report.Framework, StringComparison.Ordinal) ||
            !String.Equals(worker.Build.Variant, report.Variant, StringComparison.Ordinal))
            throw new InvalidDataException("Worker identity mismatch in " + worker.Build.Key + ".");
        return report;
    }

    private static void ValidateLoadedAssembly(BuiltWorker expected, WorkerReport actual, ReleaseAuditReport report)
    {
        if (!String.Equals(expected.Build.LibrarySha256, actual.AssemblySha256, StringComparison.OrdinalIgnoreCase))
            report.GateViolations.Add("Loaded DBreeze.dll SHA mismatch in " + expected.Build.Key + ": " + actual.AssemblySha256);
        if (!String.Equals(expected.Build.Framework, actual.Framework, StringComparison.Ordinal) ||
            !String.Equals(expected.Build.Variant, actual.Variant, StringComparison.Ordinal))
            report.GateViolations.Add("Worker identity mismatch in " + expected.Build.Key);
    }

    private static void Evaluate(ReleaseAuditReport report, ReleaseAuditOptions options)
    {
        ReleaseAuditEvaluator.EvaluateApi(report);
        ReleaseAuditEvaluator.EvaluateCoverageAndCorrectness(report, options.ExpectedBaseline);
        foreach (ReleaseCompatibilityFlow flow in report.Compatibility.Where(static flow => !flow.Passed))
            report.GateViolations.Add("File compatibility failed: " + flow.Id);
        foreach (ReleasePerformanceComparison value in report.Performance)
        {
            if (!value.Complete) report.GateViolations.Add("Missing performance pair: " + value.Framework + "/" + value.Scenario);
            else if (!value.SpeedPassed || !value.AllocationPassed)
                report.GateViolations.Add(value.Verdict + ": " + value.Framework + "/" + value.Category + "/" + value.Scenario);
        }
    }

    private static void FinalizeVerdict(ReleaseAuditReport report, ReleaseAuditOptions options)
    {
        report.GateViolations = report.GateViolations.Distinct(StringComparer.Ordinal).ToList();
        report.ReleaseVerdictIssued = options.Profile == "full" && !report.Incomplete;
        report.Passed = report.ReleaseVerdictIssued && report.GateViolations.Count == 0 && String.IsNullOrEmpty(report.Failure);
    }

    private static void AddFlow(ReleaseAuditReport report, string id, string kind, BuiltWorker producer, BuiltWorker consumer,
        WorkerReport worker, string database)
    {
        CaseResult failed = worker.Cases.FirstOrDefault(static value => !value.Succeeded);
        report.Compatibility.Add(new ReleaseCompatibilityFlow
        {
            Id = id, Kind = kind, Producer = producer.Build.Key, Consumer = consumer.Build.Key,
            Passed = worker.Succeeded && failed == null,
            Semantic = String.Join(" | ", worker.Cases.Select(static value => value.SemanticValue).Where(static value => !String.IsNullOrEmpty(value))),
            Detail = failed?.Detail ?? worker.Failure, DatabasePath = database
        });
    }

    private static WorkerReport Merge(params WorkerReport[] reports)
    {
        var result = new WorkerReport { Succeeded = reports.All(static value => value.Succeeded) };
        foreach (WorkerReport report in reports) { result.Cases.AddRange(report.Cases); if (!String.IsNullOrEmpty(report.Failure)) result.Failure += report.Failure + Environment.NewLine; }
        return result;
    }

    private static List<FileEntry> CompatibilityFiles(string root)
    {
        var result = new List<FileEntry>();
        if (!Directory.Exists(root)) return result;
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        foreach (string path in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(new FileEntry
            {
                RelativePath = path.Substring(prefix.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/'),
                Length = new FileInfo(path).Length,
                Sha256 = Sha256(path)
            });
        }
        return result;
    }

    private static bool FileListsEqual(IList<FileEntry> left, IList<FileEntry> right)
    {
        if (left.Count != right.Count) return false;
        for (int i = 0; i < left.Count; i++) if (left[i].RelativePath != right[i].RelativePath || left[i].Length != right[i].Length || left[i].Sha256 != right[i].Sha256) return false;
        return true;
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (Directory.Exists(destination)) throw new IOException("Copy destination exists: " + destination);
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), false);
    }

    private static string Git(string repository, AuditLog log, ReleaseDeadline deadline, params string[] arguments)
    {
        var args = new List<string> { "-C", repository }; args.AddRange(arguments);
        return RequireProcess("git", args, repository, log, deadline).StandardOutput;
    }

    private static ProcessResult RequireProcess(string file, IEnumerable<string> args, string working, AuditLog log, ReleaseDeadline deadline,
        IReadOnlyDictionary<string, string> environment = null)
    {
        ProcessResult result = RunProcess(file, args, working, log, deadline, environment);
        EnsureProcessSucceeded(result.ExitCode, file, args, result.StandardError);
        return result;
    }

    internal static void EnsureProcessSucceeded(int exitCode, string file, IEnumerable<string> args, string standardError)
    {
        if (exitCode != 0)
            throw new InvalidOperationException("Process failed (" + exitCode + "): " + file + " " + String.Join(" ", args) + "\n" + standardError);
    }

    private static ProcessResult RunProcess(string file, IEnumerable<string> args, string working, AuditLog log, ReleaseDeadline deadline,
        IReadOnlyDictionary<string, string> environment = null)
    {
        TimeSpan remaining = deadline.Remaining;
        if (remaining <= TimeSpan.Zero) throw new ReleaseAuditTimeoutException("Budget expired before starting " + file);
        var start = new ProcessStartInfo(file) { WorkingDirectory = working, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"; start.Environment["DOTNET_NOLOGO"] = "1";
        if (environment != null) foreach ((string key, string value) in environment) start.Environment[key] = value;
        log?.Write("PROCESS " + file + " " + String.Join(" ", args));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start " + file);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
        int wait = (int)Math.Min(Int32.MaxValue, Math.Max(1, remaining.TotalMilliseconds));
        if (!process.WaitForExit(wait))
        {
            try { process.Kill(true); } catch { }
            throw new ReleaseAuditTimeoutException("Process exceeded remaining budget: " + file);
        }
        string outText = stdout.GetAwaiter().GetResult(), errorText = stderr.GetAwaiter().GetResult();
        if (!String.IsNullOrWhiteSpace(outText)) log?.Write(outText.TrimEnd());
        if (!String.IsNullOrWhiteSpace(errorText)) log?.Write("STDERR " + errorText.TrimEnd());
        return new ProcessResult(process.ExitCode, outText, errorText);
    }

    private static int CountWarnings(ProcessResult result) => (result.StandardOutput + "\n" + result.StandardError)
        .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
        .Where(static line => Regex.IsMatch(line, @"\bwarning\s+[A-Z]{2}\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        .Select(static line => line.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    private static string Locate(string root, string name)
    {
        string[] files = Directory.GetFiles(root, name, SearchOption.AllDirectories)
            .Where(static path => !path.Contains(Path.DirectorySeparatorChar + "ref" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (files.Length == 0) throw new FileNotFoundException("Build output " + name + " was not found under " + root);
        return files.OrderByDescending(File.GetLastWriteTimeUtc).First();
    }

    private static string FindMsBuild()
    {
        string path = @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe";
        if (!File.Exists(path)) throw new FileNotFoundException("Full MSBuild 17 was not found.", path);
        return path;
    }

    private static string SourceFingerprint(string repository)
    {
        string[] roots = { "DBreeze", "DBreeze.Net8", "DBreeze.ReleaseAudit.Protocol", "DBreeze.ReleaseAudit.Worker", "DBreeze.Net8.Benchmarks", "DBreeze.Net8.Tests", "DBreeze.Storage.Contracts" };
        var entries = new List<KeyValuePair<string, byte[]>>();
        foreach (string root in roots)
        {
            string full = Path.Combine(repository, root);
            if (!Directory.Exists(full)) continue;
            foreach (string file in Directory.GetFiles(full, "*", SearchOption.AllDirectories)
                         .Where(static file => !file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                                               !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                                               (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
                         .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase))
            {
                entries.Add(new KeyValuePair<string, byte[]>(Path.GetRelativePath(repository, file).Replace('\\', '/'), File.ReadAllBytes(file)));
            }
        }
        return ManifestHash(entries);
    }

    internal static string ManifestHash(IEnumerable<KeyValuePair<string, byte[]>> entries)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (KeyValuePair<string, byte[]> entry in entries.OrderBy(static value => value.Key, StringComparer.OrdinalIgnoreCase))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(entry.Key.Replace('\\', '/')));
            hash.AppendData(entry.Value ?? Array.Empty<byte>());
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string Sha256(string path) { using FileStream stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed class ReleaseBuildSet
    {
        private readonly Dictionary<string, BuiltWorker> _items = new(StringComparer.Ordinal);
        internal IEnumerable<BuiltWorker> Values => _items.Values.OrderBy(static value => value.Build.Key, StringComparer.Ordinal);
        internal BuiltWorker this[string key] => _items[key];
        internal void Add(BuiltWorker worker) => _items.Add(worker.Build.Key, worker);
    }
    private sealed class BuiltWorker { internal BuiltWorker(ReleaseBuild build) { Build = build; } internal ReleaseBuild Build { get; } }
    private readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

internal sealed class ReleaseDeadline
{
    private readonly Stopwatch _timer = Stopwatch.StartNew();
    private readonly TimeSpan _budget;
    internal ReleaseDeadline(TimeSpan budget) { _budget = budget; }
    internal TimeSpan Remaining => _budget - _timer.Elapsed;
}

internal sealed class ReleaseAuditTimeoutException : TimeoutException
{
    internal ReleaseAuditTimeoutException(string message) : base(message) { }
}
