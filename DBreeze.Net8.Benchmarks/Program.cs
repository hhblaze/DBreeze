using BenchmarkDotNet.Running;
using DBreeze.Net8.Benchmarks;

if (args.Any(static arg => string.Equals(arg, "--backward-read-audit-self-test", StringComparison.OrdinalIgnoreCase)))
    return BackwardReadAuditSelfTests.Run();

if (args.Any(static arg => string.Equals(arg, "--backward-read-audit", StringComparison.OrdinalIgnoreCase)))
    return BackwardReadAudit.Run(args);

if (args.Any(static arg => string.Equals(arg, "--point-read-audit", StringComparison.OrdinalIgnoreCase)))
    return PointReadAudit.Run(args);

if (args.Any(static arg => string.Equals(arg, "--sqlite-compare-self-test", StringComparison.OrdinalIgnoreCase)))
    return SqliteComparisonSelfTests.Run();

if (args.Any(static arg =>
        string.Equals(arg, "--sqlite-compare-augment-rks-update", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "--sqlite-compare-augment-rks-no-overwrite-update", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "--sqlite-compare-augment-sorted-delete", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "--sqlite-compare-augment-delete-fallbacks", StringComparison.OrdinalIgnoreCase)))
    return SqliteComparisonSuite.RunAugment(args);

if (args.Any(static arg => string.Equals(arg, "--sqlite-compare", StringComparison.OrdinalIgnoreCase)))
    return SqliteComparisonSuite.Run(args);

if (args.Any(static arg => string.Equals(arg, "--render-audit", StringComparison.OrdinalIgnoreCase)))
    return AuditArtifactRenderer.Run(args);

if (args.Any(static arg => string.Equals(arg, "--release-audit", StringComparison.OrdinalIgnoreCase)
        || string.Equals(arg, "--compare-all", StringComparison.OrdinalIgnoreCase)))
    return ReleaseAuditOrchestrator.Run(args);

if (args.Any(static arg => string.Equals(arg, "--audit-worker", StringComparison.OrdinalIgnoreCase)))
    return AuditWorker.Run(args);

if (args.Any(static arg => string.Equals(arg, "--api-surface", StringComparison.OrdinalIgnoreCase)
        || string.Equals(arg, "--api-compare", StringComparison.OrdinalIgnoreCase)
        || string.Equals(arg, "--api-compatible", StringComparison.OrdinalIgnoreCase)))
    return ApiSurfaceProbe.Run(args);

if (args.Any(static arg => string.Equals(arg, "--focused-compare", StringComparison.OrdinalIgnoreCase)))
    return FocusedBenchmarkComparison.Run(args);

if (args.Any(static arg => string.Equals(arg, "--disk-compat", StringComparison.OrdinalIgnoreCase)))
    return DiskCompatibilityProbe.Run(args);

if (args.Any(static arg => string.Equals(arg, "--liana-compat", StringComparison.OrdinalIgnoreCase)))
    return LianaTrieCompatibilityProbe.Run(args);

if (args.Any(static arg => string.Equals(arg, "--nested-lifecycle-compat", StringComparison.OrdinalIgnoreCase)))
    return NestedTableLifecycleCompatibilityProbe.Run(args);

if (args.Any(static arg => string.Equals(arg, "--nested-lifecycle-perf", StringComparison.OrdinalIgnoreCase)))
    return NestedTableLifecyclePerformanceProbe.Run(args);

if (args.Any(static arg => string.Equals(arg, "--liana-traversal-perf", StringComparison.OrdinalIgnoreCase)))
    return LianaTrieTraversalPerformanceProbe.Run(args);

if (args.Any(static arg => string.Equals(arg, "--historical-compare", StringComparison.OrdinalIgnoreCase)))
    return HistoricalBenchmarkComparison.Run(args);

if (args.Any(static arg => string.Equals(arg, "--historical-core", StringComparison.OrdinalIgnoreCase)
        || string.Equals(arg, "--historical-skip", StringComparison.OrdinalIgnoreCase)
        || string.Equals(arg, "--historical-scan", StringComparison.OrdinalIgnoreCase)
        || string.Equals(arg, "--historical-random", StringComparison.OrdinalIgnoreCase)))
    return HistoricalBenchmarkSuite.Run(args);

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return 0;
