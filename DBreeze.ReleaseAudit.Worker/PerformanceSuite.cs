using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DBreeze;
using DBreeze.Objects;
using DBreeze.ReleaseAudit.Protocol;
using DBreeze.Transactions;
using DBreeze.Utils;

namespace DBreeze.ReleaseAudit.Worker
{
    internal static class PerformanceSuite
    {
        private static long _parallelAllocatedBytes;
        private static int _parallelThreadsMeasured;

        internal static void Run(WorkerOptions options, WorkerReport report)
        {
            if (String.IsNullOrWhiteSpace(options.Root)) throw new ArgumentException("Performance action requires --root.");
            Directory.CreateDirectory(options.Root);
            AllocationCounter.Enable();
            List<Scenario> scenarios = Scenarios(options);
            HashSet<string> filter = Split(options.Scenarios);
            if (filter.Count != 0) scenarios = scenarios.Where(delegate(Scenario item) { return filter.Contains(item.Name); }).ToList();
            Dictionary<string, int> operationPlan = ParseOperations(options.Operations);

            foreach (Scenario scenario in scenarios)
            {
                try
                {
                    int operations;
                    Measurement measurement;
                    if (options.Round == 0 && !operationPlan.TryGetValue(scenario.Name, out operations))
                        measurement = Calibrate(scenario, options.Root, options);
                    else
                    {
                        if (!operationPlan.TryGetValue(scenario.Name, out operations)) operations = scenario.InitialOperations;
                        operations = Math.Max(1, Math.Min(operations, scenario.MaximumOperations));
                        measurement = scenario.Run(Path.Combine(options.Root, Safe(scenario.Name) + "-r" + options.Round), operations);
                    }
                    measurement.Scenario = scenario.Name;
                    measurement.Category = scenario.Category;
                    measurement.Workers = scenario.Workers;
                    measurement.Round = options.Round;
                    measurement.BackgroundAllocationCounter = scenario.BackgroundAllocation;
                    report.Measurements.Add(measurement);
                    bool calibrated = options.Round != 0 ||
                        measurement.ElapsedMilliseconds >= 500 && measurement.ElapsedMilliseconds <= 1500 ||
                        measurement.Operations == scenario.MaximumOperations && measurement.ElapsedMilliseconds < 500 ||
                        measurement.Operations == 1 && measurement.ElapsedMilliseconds > 1500;
                    report.Cases.Add(new CaseResult
                    {
                        Id = scenario.Name,
                        Category = "performance",
                        Mode = options.Round == 0 ? "calibration" : "round-" + options.Round,
                        Succeeded = measurement.Operations > 0 && measurement.ElapsedMilliseconds > 0 && calibrated,
                        SemanticValue = "ops=" + measurement.Operations + ";ms=" + measurement.ElapsedMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ";checksum=" + measurement.Checksum,
                        Detail = calibrated ? null : "Calibration did not reach 0.5-1.5 seconds before its attempt limit."
                    });
                }
                catch (Exception error)
                {
                    report.Cases.Add(new CaseResult { Id = scenario.Name, Category = "performance", Mode = "round-" + options.Round, Succeeded = false, Detail = error.ToString() });
                }
            }
        }

        private static Measurement Calibrate(Scenario scenario, string root, WorkerOptions options)
        {
            int operations = Math.Max(1, Math.Min(scenario.InitialOperations, scenario.MaximumOperations));
            Measurement measurement = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                string path = Path.Combine(root, "cal-" + Safe(scenario.Name) + "-" + attempt);
                measurement = scenario.Run(path, operations);
                if (measurement.ElapsedMilliseconds >= 500 && measurement.ElapsedMilliseconds <= 1500) break;
                double elapsed = Math.Max(1.0, measurement.ElapsedMilliseconds);
                int scaled = (int)Math.Round(operations * 800.0 / elapsed, MidpointRounding.AwayFromZero);
                scaled = Math.Max(1, Math.Min(scaled, scenario.MaximumOperations));
                if (scaled == operations) break;
                operations = scaled;
            }
            return measurement;
        }

        private static List<Scenario> Scenarios(WorkerOptions options)
        {
            int max = options.MaxRecords;
            int textMax = options.MaxTextRecords;
            int vectorMax = options.MaxVectorRecords;
            int smokeMax = options.Profile == "smoke" ? Math.Min(max, 20000) : max;
            return new List<Scenario>
            {
                new Scenario("disk-crud", "disk-crud", 1, 2000, smokeMax, false, DiskCrud),
                new Scenario("memory-crud", "memory-crud", 1, 5000, smokeMax, false, MemoryCrud),
                new Scenario("rks-batch", "rks", 1, 5000, smokeMax, false, RandomSorter),
                new Scenario("point-select", "traversal", 1, 10000, smokeMax, false, PointSelect),
                new Scenario("range-prefix-skip", "traversal", 1, 10000, smokeMax, false, RangePrefixSkip),
                new Scenario("multi-select", "traversal", 1, 5000, smokeMax, false, MultiSelect),
                new Scenario("partial-data-block", "blocks", 1, 2000, smokeMax, false, PartialBlocks),
                new Scenario("nested-objects", "objects", 1, 1000, Math.Min(smokeMax, 100000), false, NestedObjects),
                new Scenario("objects", "objects", 1, 1000, Math.Min(smokeMax, 100000), false, Objects),
                new Scenario("resources", "resources", 1, 50, Math.Min(smokeMax, 100000), false, Resources),
                new Scenario("scheme", "scheme", 1, 20, Math.Min(smokeMax, 10000), false, Scheme),
                new Scenario("text-index-query", "text", 1, 200, textMax, true, TextIndexQuery),
                new Scenario("vector-float-insert-search", "vector", 1, 64, vectorMax, true, VectorFloat),
                new Scenario("vector-double-insert-search", "vector", 1, 64, vectorMax, true, VectorDouble),
                new Scenario("parallel-read", "parallel", 4, 10000, smokeMax, true, ParallelRead),
                new Scenario("parallel-disjoint-write", "parallel", 4, 2000, smokeMax, true, ParallelDisjointWrite),
                new Scenario("parallel-contention", "parallel", 4, 1000, options.Profile == "smoke" ? Math.Min(smokeMax, 100000) : max, true, ParallelContention),
                new Scenario("mixed-90-10", "mixed", 4, 5000, smokeMax, true, delegate(string root, int ops) { return Mixed(root, ops, 10); }),
                new Scenario("mixed-50-50", "mixed", 4, 3000, smokeMax, true, delegate(string root, int ops) { return Mixed(root, ops, 50); })
            };
        }

        private static Measurement DiskCrud(string root, int operations)
        {
            Directory.CreateDirectory(root);
            using (var engine = new DBreezeEngine(Path.Combine(root, "db")))
            {
                return Measure(root, operations, false, delegate
                {
                    long checksum = 0;
                    using (Transaction t = engine.GetTransaction())
                    {
                        for (int i = 0; i < operations; i++) t.Insert("crud", i, i * 3);
                        t.Commit();
                    }
                    using (Transaction t = engine.GetTransaction()) for (int i = 0; i < operations; i++) checksum += t.Select<int, int>("crud", i).Value;
                    return checksum;
                });
            }
        }

        private static Measurement MemoryCrud(string root, int operations)
        {
            var configuration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.MEMORY, NotifyAhead_WhenWriteTablePossibleDeadlock = false };
            using (var engine = new DBreezeEngine(configuration))
                return Measure(root, operations, false, delegate
                {
                    long checksum = 0;
                    using (Transaction t = engine.GetTransaction()) { for (int i = 0; i < operations; i++) t.Insert("memory", i, i); t.Commit(); }
                    using (Transaction t = engine.GetTransaction()) for (int i = 0; i < operations; i++) checksum += t.Select<int, int>("memory", i).Value;
                    return checksum;
                });
        }

        private static Measurement RandomSorter(string root, int operations)
        {
            Directory.CreateDirectory(root);
            using (var engine = new DBreezeEngine(Path.Combine(root, "db")))
                return Measure(root, operations, false, delegate
                {
                    using (Transaction t = engine.GetTransaction())
                    {
                        for (int i = operations - 1; i >= 0; i--) t.InsertRandomKeySorter("rks", i, i * 7);
                        t.Commit();
                    }
                    using (Transaction t = engine.GetTransaction()) return (long)t.Count("rks");
                });
        }

        private static Measurement PointSelect(string root, int operations)
        {
            Directory.CreateDirectory(root);
            using (var engine = Seed(root, operations))
                return Measure(root, operations, false, delegate
                {
                    long checksum = 0;
                    using (Transaction t = engine.GetTransaction()) for (int i = 0; i < operations; i++) checksum += t.Select<int, int>("data", i).Value;
                    return checksum;
                });
        }

        private static Measurement RangePrefixSkip(string root, int operations)
        {
            Directory.CreateDirectory(root);
            using (var engine = new DBreezeEngine(Path.Combine(root, "db")))
            {
                using (Transaction seed = engine.GetTransaction()) { seed.SynchronizeTables("data", "prefix"); for (int i = 0; i < operations; i++) { seed.Insert("data", i, i); seed.Insert("prefix", new byte[] { (byte)(i % 16), (byte)(i >> 8), (byte)i }, i); } seed.Commit(); }
                return Measure(root, operations, false, delegate
                {
                    long checksum = 0;
                    using (Transaction t = engine.GetTransaction())
                    {
                        checksum += t.SelectForwardFromTo<int, int>("data", operations / 4, true, operations / 2, true).Sum(delegate(DBreeze.DataTypes.Row<int, int> row) { return (long)row.Value; });
                        checksum += t.SelectForwardSkip<int, int>("data", (ulong)(operations / 3)).Take(32).Sum(delegate(DBreeze.DataTypes.Row<int, int> row) { return (long)row.Value; });
                        checksum += t.SelectForwardStartsWith<byte[], int>("prefix", new byte[] { 7 }).Sum(delegate(DBreeze.DataTypes.Row<byte[], int> row) { return (long)row.Value; });
                    }
                    return checksum;
                });
            }
        }

        private static Measurement MultiSelect(string root, int operations)
        {
            Directory.CreateDirectory(root);
            using (var engine = new DBreezeEngine(Path.Combine(root, "db")))
            {
                using (Transaction seed = engine.GetTransaction()) { seed.SynchronizeTables("a", "b"); for (int i = 0; i < operations; i++) { seed.Insert("a", i, i); seed.Insert("b", i, i * 2); } seed.Commit(); }
                return Measure(root, operations, false, delegate
                {
                    using (Transaction t = engine.GetTransaction())
                        return t.Multi_SelectForwardFromTo<int, int>(new HashSet<string> { "a", "b" }, 0, true, operations - 1, true).Sum(delegate(DBreeze.DataTypes.Row<int, int> row) { return (long)row.Value; });
                });
            }
        }

        private static Measurement PartialBlocks(string root, int operations)
        {
            Directory.CreateDirectory(root);
            using (var engine = new DBreezeEngine(Path.Combine(root, "db")))
            {
                using (Transaction seed = engine.GetTransaction()) { for (int i = 0; i < operations; i++) seed.Insert("parts", i, new byte[64]); seed.Commit(); }
                return Measure(root, operations, false, delegate
                {
                    long checksum = 0;
                    using (Transaction t = engine.GetTransaction())
                    {
                        t.SynchronizeTables("parts", "blocks");
                        for (int i = 0; i < operations; i++) { t.InsertPart("parts", i, new byte[] { 1, 2, 3, 4 }, 16); byte[] p = t.InsertDataBlock("blocks", null, new byte[] { (byte)i }); checksum += p.Length; }
                        t.Commit();
                    }
                    return checksum;
                });
            }
        }

        private static Measurement NestedObjects(string root, int operations)
        {
            Directory.CreateDirectory(root);
            using (var engine = new DBreezeEngine(Path.Combine(root, "db")))
                return Measure(root, operations, false, delegate
                {
                    using (Transaction t = engine.GetTransaction())
                    {
                        DBreeze.DataTypes.NestedTable nested = t.InsertTable("nested", 1, 0);
                        for (int i = 0; i < operations; i++) nested.Insert(i, i);
                        nested.CloseTable(); t.Commit();
                    }
                    using (Transaction t = engine.GetTransaction()) { DBreeze.DataTypes.NestedTable nested = t.SelectTable<int>("nested", 1, 0); long count = (long)nested.Count(); nested.CloseTable(); return count; }
                });
        }

        private static Measurement Objects(string root, int operations)
        {
            Directory.CreateDirectory(root);
            using (var engine = new DBreezeEngine(Path.Combine(root, "db")))
                return Measure(root, operations, false, delegate
                {
                    using (Transaction t = engine.GetTransaction())
                    {
                        for (int i = 0; i < operations; i++)
                        {
                            long identity = t.ObjectGetNewIdentity<long>("objects");
                            t.ObjectInsert("objects", new DBreezeObject<byte[]>
                            {
                                NewEntity = true,
                                Entity = new byte[64],
                                Indexes = new List<DBreezeIndex>
                                {
                                    new DBreezeIndex(1, identity) { PrimaryIndex = true },
                                    new DBreezeIndex(2, i)
                                }
                            });
                        }
                        t.Commit();
                    }
                    long checksum = 0;
                    using (Transaction t = engine.GetTransaction())
                        for (long identity = 1; identity <= operations; identity++)
                        {
                            DBreezeObject<byte[]> item = t.Select<byte[], byte[]>("objects", 1.ToIndex(identity)).ObjectGet<byte[]>();
                            checksum += item == null || item.Entity == null ? 0 : item.Entity.Length;
                        }
                    return checksum;
                });
        }

        private static Measurement Resources(string root, int operations)
        {
            Directory.CreateDirectory(root);
            using (var engine = new DBreezeEngine(Path.Combine(root, "db")))
                return Measure(root, operations, false, delegate
                {
                    for (int i = 0; i < operations; i++) engine.Resources.Insert("resource-" + i.ToString("D8"), new byte[] { (byte)i });
                    return engine.Resources.SelectStartsWith<byte[]>("resource-").LongCount();
                });
        }

        private static Measurement Scheme(string root, int operations)
        {
            Directory.CreateDirectory(root);
            string[] tables = Enumerable.Range(0, operations).Select(delegate(int value) { return "scheme-" + value; }).ToArray();
            using (var engine = new DBreezeEngine(Path.Combine(root, "db")))
                return Measure(root, operations, false, delegate
                {
                    using (Transaction t = engine.GetTransaction()) { t.SynchronizeTables(tables); for (int i = 0; i < operations; i++) t.Insert(tables[i], 1, 1); t.Commit(); }
                    return engine.Scheme.GetUserTableNamesStartingWith("scheme-").Count;
                });
        }

        private static Measurement TextIndexQuery(string root, int operations)
        {
            Directory.CreateDirectory(root);
            using (var engine = new DBreezeEngine(Path.Combine(root, "db")))
                return Measure(root, operations, true, delegate
                {
                    using (Transaction t = engine.GetTransaction()) { for (int i = 0; i < operations; i++) t.TextInsert("text", i.To_4_bytes_array_BigEndian(), "alpha beta token" + i, "group"); t.Commit(); }
                    using (Transaction t = engine.GetTransaction()) return t.TextSearch("text").BlockAnd("alpha").GetDocumentIDs().LongCount();
                });
        }

        private static Measurement VectorFloat(string root, int operations)
        {
            Directory.CreateDirectory(root);
            using (var engine = new DBreezeEngine(Path.Combine(root, "db")))
                return Measure(root, operations, true, delegate
                {
                    var vectors = new List<(long, float[])>(operations);
                    for (int i = 0; i < operations; i++) vectors.Add((i, new float[] { 1f, (i % 97) / 97f, (i % 31) / 31f }));
                    using (Transaction t = engine.GetTransaction()) { t.VectorsInsert("vf", vectors); t.Commit(); }
                    using (Transaction t = engine.GetTransaction()) return t.VectorsSearchSimilar("vf", new float[] { 1, 0, 0 }, Math.Min(10, operations)).LongCount();
                });
        }

        private static Measurement VectorDouble(string root, int operations)
        {
            Directory.CreateDirectory(root);
            using (var engine = new DBreezeEngine(Path.Combine(root, "db")))
                return Measure(root, operations, true, delegate
                {
                    var vectors = new List<(long, double[])>(operations);
                    for (int i = 0; i < operations; i++) vectors.Add((i, new double[] { 1d, (i % 97) / 97d, (i % 31) / 31d }));
                    using (Transaction t = engine.GetTransaction()) { t.VectorsInsert("vd", vectors); t.Commit(); }
                    using (Transaction t = engine.GetTransaction()) return t.VectorsSearchSimilar("vd", new double[] { 1, 0, 0 }, Math.Min(10, operations)).LongCount();
                });
        }

        private static Measurement ParallelRead(string root, int operations)
        {
            Directory.CreateDirectory(root);
            using (var engine = Seed(root, operations)) return Measure(root, operations, true, delegate { return ParallelWorkers(4, delegate(int worker) { long sum = 0; using (Transaction t = engine.GetTransaction()) for (int i = worker; i < operations; i += 4) sum += t.Select<int, int>("data", i).Value; return sum; }); });
        }

        private static Measurement ParallelDisjointWrite(string root, int operations)
        {
            Directory.CreateDirectory(root);
            using (var engine = new DBreezeEngine(Path.Combine(root, "db"))) return Measure(root, operations, true, delegate { return ParallelWorkers(4, delegate(int worker) { using (Transaction t = engine.GetTransaction()) { for (int i = worker; i < operations; i += 4) t.Insert("write-" + worker, i, i); t.Commit(); return (long)t.Count("write-" + worker); } }); });
        }

        private static Measurement ParallelContention(string root, int operations)
        {
            Directory.CreateDirectory(root);
            using (var engine = new DBreezeEngine(Path.Combine(root, "db"))) return Measure(root, operations, true, delegate { return ParallelWorkers(4, delegate(int worker) { using (Transaction t = engine.GetTransaction()) { t.SynchronizeTables("contended"); for (int i = worker; i < operations; i += 4) t.Insert("contended", i, i); t.Commit(); return 1; } }); });
        }

        private static Measurement Mixed(string root, int operations, int writePercent)
        {
            Directory.CreateDirectory(root);
            using (var engine = Seed(root, operations)) return Measure(root, operations, true, delegate { return ParallelWorkers(4, delegate(int worker) { long sum = 0; using (Transaction t = engine.GetTransaction()) { for (int i = worker; i < operations; i += 4) { if (i % 100 < writePercent) t.Insert("mixed-" + worker, i, i); else sum += t.Select<int, int>("data", i).Value; } t.Commit(); } return sum; }); });
        }

        private static DBreezeEngine Seed(string root, int records)
        {
            var engine = new DBreezeEngine(Path.Combine(root, "db"));
            using (Transaction t = engine.GetTransaction()) { for (int i = 0; i < records; i++) t.Insert("data", i, i * 3); t.Commit(); }
            return engine;
        }

        private static long ParallelWorkers(int workers, Func<int, long> body)
        {
            long[] results = new long[workers];
            var start = new ManualResetEventSlim(false);
            Task[] tasks = new Task[workers];
            for (int i = 0; i < workers; i++)
            {
                int worker = i;
                tasks[i] = Task.Factory.StartNew(delegate
                {
                    start.Wait();
                    long before = AllocationCounter.HasThreadCounter ? AllocationCounter.CurrentThread() : 0;
                    results[worker] = body(worker);
                    if (AllocationCounter.HasThreadCounter)
                    {
                        Interlocked.Add(ref _parallelAllocatedBytes, Math.Max(0, AllocationCounter.CurrentThread() - before));
                        Interlocked.Increment(ref _parallelThreadsMeasured);
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            start.Set();
            if (!Task.WaitAll(tasks, TimeSpan.FromSeconds(120))) throw new TimeoutException("Parallel performance scenario timed out.");
            return results.Sum();
        }

        private static Measurement Measure(string root, int operations, bool background, Func<long> body)
        {
            ForceGc();
            Interlocked.Exchange(ref _parallelAllocatedBytes, 0);
            Interlocked.Exchange(ref _parallelThreadsMeasured, 0);
            long threadBefore = AllocationCounter.CurrentThread();
            long processBefore = AllocationCounter.Process();
            int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
            var process = Process.GetCurrentProcess(); process.Refresh(); long peakBefore = process.PeakWorkingSet64;
            var timer = Stopwatch.StartNew();
            long checksum = body();
            timer.Stop();
            long threadAfter = AllocationCounter.CurrentThread();
            long processAfter = AllocationCounter.Process();
            process.Refresh();
            long workerAllocated = Interlocked.Read(ref _parallelAllocatedBytes);
            bool summedWorkerThreads = Volatile.Read(ref _parallelThreadsMeasured) != 0;
            return new Measurement
            {
                Operations = operations,
                ElapsedMilliseconds = Math.Max(0.0001, timer.Elapsed.TotalMilliseconds),
                AllocatedBytes = Math.Max(0, summedWorkerThreads ? workerAllocated : background ? processAfter - processBefore : threadAfter - threadBefore),
                ProcessAllocatedBytes = Math.Max(0, processAfter - processBefore),
                Gen0Collections = GC.CollectionCount(0) - gen0,
                Gen1Collections = GC.CollectionCount(1) - gen1,
                Gen2Collections = GC.CollectionCount(2) - gen2,
                LiveHeapBytes = GC.GetTotalMemory(false),
                PeakPrivateBytes = Math.Max(peakBefore, process.PeakWorkingSet64),
                DatabaseBytes = Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).Sum(delegate(string file) { return new FileInfo(file).Length; }) : 0,
                Checksum = checksum.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
        }

        private static void ForceGc() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        private static string Safe(string value) { return new string(value.Select(delegate(char c) { return Char.IsLetterOrDigit(c) ? c : '-'; }).ToArray()); }
        private static HashSet<string> Split(string value) { return new HashSet<string>((value ?? String.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal); }
        private static Dictionary<string, int> ParseOperations(string value)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string pair in (value ?? String.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = pair.LastIndexOf('=');
                int parsed;
                if (equals <= 0 || !Int32.TryParse(pair.Substring(equals + 1), out parsed) || parsed < 1) throw new ArgumentException("Invalid --operations entry: " + pair);
                result.Add(pair.Substring(0, equals), parsed);
            }
            return result;
        }

        private sealed class Scenario
        {
            internal readonly string Name, Category; internal readonly int Workers, InitialOperations, MaximumOperations; internal readonly bool BackgroundAllocation; internal readonly Func<string, int, Measurement> Run;
            internal Scenario(string name, string category, int workers, int initial, int maximum, bool background, Func<string, int, Measurement> run) { Name = name; Category = category; Workers = workers; InitialOperations = initial; MaximumOperations = maximum; BackgroundAllocation = background; Run = run; }
        }

        private static class AllocationCounter
        {
            private static readonly MethodInfo ThreadMethod = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static);
            private static readonly PropertyInfo ProcessProperty = typeof(AppDomain).GetProperty("MonitoringTotalAllocatedMemorySize", BindingFlags.Public | BindingFlags.Instance);
            internal static bool HasThreadCounter { get { return ThreadMethod != null; } }
            internal static void Enable() { try { AppDomain.MonitoringIsEnabled = true; } catch { } }
            internal static long CurrentThread() { return ThreadMethod == null ? Process() : Convert.ToInt64(ThreadMethod.Invoke(null, null)); }
            internal static long Process() { return ProcessProperty == null ? GC.GetTotalMemory(false) : Convert.ToInt64(ProcessProperty.GetValue(AppDomain.CurrentDomain, null)); }
        }
    }
}
