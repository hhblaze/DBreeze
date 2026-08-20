using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DBreeze;
using DBreeze.DataTypes;

namespace DBreeze.Net8.Benchmarks;

internal static class NestedTableLifecyclePerformanceProbe
{
    private const string Table = "nested-lifecycle-perf";

    internal static int Run(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);
            PrepareDatabase(options.DatabasePath);

            int operations = options.Scenario == "readonly" ? 10_000 : 100;
            using var engine = new DBreezeEngine(options.DatabasePath);
            Action<int> workload = options.Scenario switch
            {
                "readonly" => count => ReadOnlyOpenClose(engine, count),
                "early-commit" => count => EarlyDisposeCommit(engine, count),
                "early-rollback" => count => EarlyDisposeRollback(engine, count),
                _ => throw new ArgumentOutOfRangeException(nameof(options.Scenario), options.Scenario,
                    "Unknown nested lifecycle performance scenario."),
            };

            workload(Math.Min(operations, 100));
            CollectGarbage();
            long retainedBefore = GC.GetTotalMemory(forceFullCollection: true);
            var measurements = new Measurement[5];
            for (int run = 0; run < measurements.Length; run++)
                measurements[run] = Measure(workload, operations);
            CollectGarbage();
            long retainedAfter = GC.GetTotalMemory(forceFullCollection: true);

            VerifyDatabase(engine, options.Scenario);
            WriteResult(options.OutputPath, options, operations, retainedAfter - retainedBefore, measurements);
            Console.WriteLine($"PASS nested-lifecycle-perf {options.Scenario}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
    }

    private static void PrepareDatabase(string databasePath)
    {
        if (Directory.Exists(databasePath))
            throw new IOException($"Performance fixture already exists and will not be overwritten: {databasePath}");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)
            ?? throw new InvalidOperationException("Fixture path must have a parent directory."));

        using var engine = new DBreezeEngine(databasePath);
        using var transaction = engine.GetTransaction();
        using NestedTable nested = transaction.InsertTable(Table, 1, 0);
        nested.Insert(1, 1);
        transaction.Commit();
    }

    private static void ReadOnlyOpenClose(DBreezeEngine engine, int operations)
    {
        using var transaction = engine.GetTransaction();
        for (int i = 0; i < operations; i++)
        {
            using NestedTable nested = transaction.SelectTable(Table, 1, 0);
            Row<int, int> row = nested.Select<int, int>(1);
            if (!row.Exists || row.Value < 1)
                throw new InvalidDataException("Read-only fixture value mismatch.");
        }
    }

    private static void EarlyDisposeCommit(DBreezeEngine engine, int operations)
    {
        for (int i = 0; i < operations; i++)
        {
            using var transaction = engine.GetTransaction();
            NestedTable nested = transaction.InsertTable(Table, 1, 0);
            nested.Insert(1, i + 2);
            nested.Dispose();
            transaction.Commit();
        }
    }

    private static void EarlyDisposeRollback(DBreezeEngine engine, int operations)
    {
        for (int i = 0; i < operations; i++)
        {
            using var transaction = engine.GetTransaction();
            NestedTable nested = transaction.InsertTable(Table, 1, 0);
            nested.Insert(2, i + 2);
            nested.Dispose();
            transaction.Rollback();
        }
    }

    private static Measurement Measure(Action<int> workload, int operations)
    {
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        workload(operations);
        long elapsed = Stopwatch.GetTimestamp() - started;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new Measurement
        {
            NanosecondsPerOperation = elapsed * (1_000_000_000.0 / Stopwatch.Frequency) / operations,
            AllocatedBytesPerOperation = (double)allocated / operations,
        };
    }

    private static void VerifyDatabase(DBreezeEngine engine, string scenario)
    {
        using var transaction = engine.GetTransaction();
        using NestedTable nested = transaction.SelectTable(Table, 1, 0);
        Row<int, int> committed = nested.Select<int, int>(1);
        if (!committed.Exists || scenario == "early-commit" && committed.Value < 2)
            throw new InvalidDataException("Committed value was not preserved.");
        if (nested.Select<int, int>(2).Exists)
            throw new InvalidDataException("Rolled-back value became visible.");
    }

    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void WriteResult(
        string outputPath,
        Options options,
        int operations,
        long retainedBytesDelta,
        Measurement[] measurements)
    {
        if (File.Exists(outputPath))
            throw new IOException($"Performance result already exists and will not be overwritten: {outputPath}");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("Result path must have a parent directory."));
        var result = new
        {
            Scenario = options.Scenario,
            DatabasePath = options.DatabasePath,
            AssemblyVersion = typeof(DBreezeEngine).Assembly.GetName().Version?.ToString() ?? String.Empty,
            OperationsPerMeasurement = operations,
            Warmup = true,
            RetainedBytesDelta = retainedBytesDelta,
            Measurements = measurements,
        };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
    }

    private sealed class Measurement
    {
        public double NanosecondsPerOperation { get; set; }
        public double AllocatedBytesPerOperation { get; set; }
    }

    private sealed class Options
    {
        internal string Scenario { get; private set; }
        internal string DatabasePath { get; private set; }
        internal string OutputPath { get; private set; }

        internal static Options Parse(string[] args)
        {
            var options = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--nested-lifecycle-perf":
                        options.Scenario = ReadValue(args, ref i, "--nested-lifecycle-perf").ToLowerInvariant();
                        break;
                    case "--database":
                        options.DatabasePath = Path.GetFullPath(ReadValue(args, ref i, "--database"));
                        break;
                    case "--output":
                        options.OutputPath = Path.GetFullPath(ReadValue(args, ref i, "--output"));
                        break;
                    default:
                        throw new ArgumentException($"Unknown nested lifecycle performance option: {args[i]}", nameof(args));
                }
            }

            if (String.IsNullOrEmpty(options.Scenario) || String.IsNullOrEmpty(options.DatabasePath) ||
                String.IsNullOrEmpty(options.OutputPath))
            {
                throw new ArgumentException(
                    "--nested-lifecycle-perf requires a scenario, --database and --output.", nameof(args));
            }
            return options;
        }

        private static string ReadValue(string[] args, ref int index, string option)
        {
            if (++index >= args.Length || String.IsNullOrWhiteSpace(args[index]))
                throw new ArgumentException(option + " requires a value.", nameof(args));
            return args[index];
        }
    }
}
