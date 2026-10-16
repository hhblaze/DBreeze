using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DBreeze;
using DBreeze.DataTypes;
using DBreeze.Transactions;

namespace DBreeze.Net8.Benchmarks;

// This runner deliberately uses only the public API, so exactly the same executable
// can run against the saved production DLL and the candidate in separate processes.
internal static class LazyReadPerformanceProbe
{
    internal static int Run(string[] args)
    {
        string Read(string name, string fallback) => Array.IndexOf(args, name) is int i && i >= 0 ? args[i + 1] : fallback;
        string root = Path.GetFullPath(Read("--root", Path.Combine("obj", "lazy-read-performance", Guid.NewGuid().ToString("N"))));
        string output = Path.GetFullPath(Read("--output", Path.Combine(root, "result.json")));
        string filter = Read("--filter", "");
        string casesPath = Read("--cases", "");
        HashSet<string> cases = casesPath.Length == 0 ? null :
            JsonSerializer.Deserialize<string[]>(File.ReadAllText(casesPath))!.ToHashSet(StringComparer.Ordinal);
        int rounds = int.Parse(Read("--rounds", "5"));
        int milliseconds = int.Parse(Read("--sample-ms", "60"));
        bool quick = args.Contains("--quick");
        var report = new Report
        {
            Label = Read("--label", "unspecified"), Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            AssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DBreezeEngine).Assembly.Location))),
            Commit = Read("--commit", "unspecified"), SampleMilliseconds = milliseconds, Rounds = rounds,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime default",
            DiagnosticsEnabled = args.Contains("--diagnostics"),
            OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
        };
        using (var process = Process.GetCurrentProcess())
        {
            try { process.PriorityClass = ProcessPriorityClass.AboveNormal; }
            catch (System.ComponentModel.Win32Exception) { }
            report.PriorityClass = process.PriorityClass.ToString();
        }
        if (typeof(DBreezeEngine).Assembly.GetType("DBreeze.Storage.DurabilityTestHooks")!
            .GetField("Handler", BindingFlags.NonPublic | BindingFlags.Static) != null)
            throw new InvalidOperationException("Performance measurements require a production DLL without test hooks.");
        Directory.CreateDirectory(root);
        Type diagnostics = typeof(DBreezeEngine).Assembly.GetType("DBreeze.Storage.CommittedReadDiagnostics")!;
        diagnostics.GetMethod("SetEnabled", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { report.DiagnosticsEnabled });
        string[] backends = Read("--backends", quick ? "memory,mapped" : "memory,mapped,fallback").Split(',');
        int[] sizes = Read("--sizes", quick ? "256,4096" : "16,128,256,2048,4096,65536").Split(',').Select(int.Parse).ToArray();
        int[] workerCounts = Read("--workers", quick ? "1,4" : "1,2,4,8").Split(',').Select(int.Parse).ToArray();
        string[] modes = Read("--modes", "SelectOnly,Value,Eager,Partial,ForwardKeys,ForwardValue,BackwardValue,NestedValue,Update,UpdateCommit,MixedValue").Split(',');
        foreach (string backend in backends)
        foreach (int size in sizes)
        {
            string prefix = $"{backend}/{size}/";
            if (cases != null && !cases.Any(id => id.StartsWith(prefix, StringComparison.Ordinal))) continue;
            using var fixture = new Fixture(root, backend, size);
            foreach (string mode in modes)
            foreach (int workers in mode is "SelectOnly" or "Value" or "Eager" or "Partial" or "MixedValue" ? workerCounts : new[] { 1 })
            {
                string id = $"{backend}/{size}/{mode}/{workers}";
                if (cases != null && !cases.Contains(id)) continue;
                if (filter.Length != 0 && !id.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                // Warm JIT, transaction roots, page windows and mappings before sampling.
                Measure(fixture, mode, workers, int.Parse(Read("--warmup-ms", "150")));
                var measurement = new Measurement { Id = id };
                for (int round = 0; round < rounds; round++)
                    measurement.Samples.Add(Measure(fixture, mode, workers, milliseconds));
                report.Measurements.Add(measurement);
                Console.WriteLine($"{id}: {Median(measurement.Samples.Select(x => x.OperationsPerSecond)):N0} ops/s, " +
                    $"{Median(measurement.Samples.Select(x => x.BytesPerOperation)):N1} B/op");
            }
            Persist(output, report);
        }
        report.StorageCounters = (long[])diagnostics.GetMethod("GetDiagnostics", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        Persist(output, report);
        return 0;
    }

    private static void Persist(string output, Report report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Sample Measure(Fixture fixture, string mode, int workers, int milliseconds)
    {
        bool mixed = mode == "MixedValue";
        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        if (workers == 1 && !mixed)
        {
            Sample sample = Worker(fixture, mode, milliseconds, null, null);
            sample.Gc0 = GC.CollectionCount(0) - gc0; sample.Gc1 = GC.CollectionCount(1) - gc1; sample.Gc2 = GC.CollectionCount(2) - gc2;
            return sample;
        }
        using var start = new ManualResetEventSlim();
        using var ready = new CountdownEvent(workers + (mixed ? 1 : 0));
        using var writerFinished = mixed ? new ManualResetEventSlim() : null;
        var tasks = Enumerable.Range(0, workers).Select(_ => Task.Factory.StartNew(
            () => Worker(fixture, mixed ? "Value" : mode, mixed ? Math.Max(200, milliseconds) : milliseconds, start, ready, writerFinished), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        Task<double[]> writer = mixed ? Task.Factory.StartNew(() =>
        {
            double[] commitLatencies = new double[4];
            ready.Signal(); start.Wait();
            long began = Stopwatch.GetTimestamp();
            try
            {
                for (int i = 0; i < commitLatencies.Length; i++)
                {
                    using var tx = fixture.Engine.GetTransaction();
                    tx.Insert("t", 0L, (i & 1) == 0 ? fixture.AlternateValue : fixture.Value);
                    long commitStarted = Stopwatch.GetTimestamp(); tx.Commit();
                    commitLatencies[i] = (Stopwatch.GetTimestamp() - commitStarted) * 1e9 / Stopwatch.Frequency;
                    long due = began + (i + 1) * Stopwatch.Frequency / 10;
                    while (Stopwatch.GetTimestamp() < due) Thread.Sleep(1);
                }
                return commitLatencies;
            }
            finally { writerFinished.Set(); }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default) : null;
        ready.Wait();
        long started = Stopwatch.GetTimestamp();
        start.Set();
        Task.WaitAll(tasks);
        writer?.GetAwaiter().GetResult();
        double elapsed = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
        long operations = tasks.Sum(x => x.Result.Operations);
        double[] latencies = tasks.SelectMany(x => x.Result.LatencySamples).ToArray();
        return new Sample { Operations = operations, ElapsedSeconds = elapsed,
            OperationsPerSecond = operations / elapsed,
            BytesPerOperation = tasks.Sum(x => x.Result.AllocatedBytes) / (double)operations,
            AllocatedBytes = tasks.Sum(x => x.Result.AllocatedBytes),
            Gc0 = GC.CollectionCount(0) - gc0, Gc1 = GC.CollectionCount(1) - gc1, Gc2 = GC.CollectionCount(2) - gc2,
            LatencyP50Nanoseconds = Percentile(latencies, .50), LatencyP95Nanoseconds = Percentile(latencies, .95),
            Commits = writer == null ? 0 : writer.Result.Length,
            CommitP50Nanoseconds = writer == null ? 0 : Percentile(writer.Result, .50),
            CommitP95Nanoseconds = writer == null ? 0 : Percentile(writer.Result, .95) };
    }

    private static Sample Worker(Fixture fixture, string mode, int milliseconds, ManualResetEventSlim start, CountdownEvent ready,
        ManualResetEventSlim writerFinished = null)
    {
        using var transaction = fixture.Engine.GetTransaction();
        transaction.ValuesLazyLoadingIsOn = mode != "Eager";
        using NestedTable nested = mode == "NestedValue" ? transaction.SelectTable("nested", 1, 0) : null;
        double[] latencySamples = new double[4096];
        int sampled = 0;
        ready?.Signal(); start?.Wait();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long operations = 0, checksum = 0;
        long started = Stopwatch.GetTimestamp();
        long until = started + Stopwatch.Frequency * milliseconds / 1000;
        int batch = Math.Max(16, Math.Min(1024, 1024 * 1024 / fixture.Size));
        do
        {
            if (mode is "ForwardKeys" or "ForwardValue" or "BackwardValue")
            {
                var rows = mode == "BackwardValue" ? transaction.SelectBackward<long, byte[]>("t") : transaction.SelectForward<long, byte[]>("t");
                foreach (var row in rows) { checksum += mode == "ForwardKeys" ? row.Key : row.Value[0]; operations++; }
            }
            else if (mode is "Update" or "UpdateCommit")
            {
                for (int i = 0; i < 64; i++) transaction.Insert("t", (long)i, fixture.Value);
                long committing = Stopwatch.GetTimestamp();
                if (mode == "UpdateCommit") transaction.Commit();
                if (sampled < latencySamples.Length) latencySamples[sampled++] = (Stopwatch.GetTimestamp() - committing) * 1e9 / Stopwatch.Frequency;
                operations++;
            }
            else
            {
                for (int i = 0; i < batch; i++)
                {
                    long key = ((operations + i) * 7919) & (Fixture.Count - 1);
                    bool sampleLatency = ((operations + i) & 255) == 0 && sampled < latencySamples.Length;
                    long before = sampleLatency ? Stopwatch.GetTimestamp() : 0;
                    Row<long, byte[]> row = nested != null ? nested.Select<long, byte[]>(key) : transaction.Select<long, byte[]>("t", key);
                    if (!row.Exists) throw new InvalidDataException("Benchmark missed a seeded key.");
                    checksum += mode == "SelectOnly" ? row.Key : mode == "Partial" ? row.GetValuePart(0, 16)[0] : row.Value[0];
                    if (sampleLatency) latencySamples[sampled++] = (Stopwatch.GetTimestamp() - before) * 1e9 / Stopwatch.Frequency;
                }
                operations += batch;
            }
        } while (Stopwatch.GetTimestamp() < until || (writerFinished != null && !writerFinished.IsSet));
        double elapsed = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        GC.KeepAlive(checksum);
        Array.Resize(ref latencySamples, sampled);
        return new Sample { Operations = operations, ElapsedSeconds = elapsed, AllocatedBytes = allocated,
            OperationsPerSecond = operations / elapsed, BytesPerOperation = allocated / (double)operations,
            LatencySamples = latencySamples,
            LatencyP50Nanoseconds = Percentile(latencySamples, .50), LatencyP95Nanoseconds = Percentile(latencySamples, .95),
            Commits = mode == "UpdateCommit" ? operations : 0,
            CommitP50Nanoseconds = mode == "UpdateCommit" ? Percentile(latencySamples, .50) : 0,
            CommitP95Nanoseconds = mode == "UpdateCommit" ? Percentile(latencySamples, .95) : 0 };
    }

    private static double Percentile(double[] samples, double fraction)
    {
        if (samples.Length == 0) return 0;
        Array.Sort(samples);
        return samples[Math.Min(samples.Length - 1, (int)(samples.Length * fraction))];
    }

    internal static double Median(IEnumerable<double> values)
    {
        double[] sorted = values.OrderBy(x => x).ToArray();
        return sorted[sorted.Length / 2];
    }

    private sealed class Fixture : IDisposable
    {
        internal const int Count = 512;
        internal readonly DBreezeEngine Engine;
        internal readonly byte[] Value;
        internal readonly byte[] AlternateValue;
        internal readonly int Size;
        internal Fixture(string root, string backend, int size)
        {
            Size = size; Value = Enumerable.Repeat((byte)65, size).ToArray();
            AlternateValue = Enumerable.Repeat((byte)66, size).ToArray();
            Engine = backend == "memory" ? new DBreezeEngine(new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.MEMORY })
                : new DBreezeEngine(Path.Combine(root, backend, size.ToString()));
            using var tx = Engine.GetTransaction();
            tx.SynchronizeTables("t", "nested");
            var reserved = new byte[size + 64];
            using (var nested = tx.InsertTable("nested", 1, 0))
            {
                for (int i = 0; i < Count; i++) { tx.Insert("t", (long)i, reserved); nested.Insert((long)i, reserved); }
                tx.Commit();
                for (int i = 0; i < Count; i++) { tx.Insert("t", (long)i, Value); nested.Insert((long)i, Value); }
                tx.Commit();
            }
            if (backend == "fallback")
            {
                DisableMapping(tx.Select<long, byte[]>("t", 0));
                DisableMapping(tx.Select<int, byte[]>("nested", 1));
            }
        }
        private static void DisableMapping<TKey>(Row<TKey, byte[]> row)
        {
            object trieRow = row.GetType().GetField("_row", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(row)!;
            object trieRoot = trieRow.GetType().GetProperty("Root")!.GetValue(trieRow)!;
            object trie = trieRoot.GetType().GetField("Tree")!.GetValue(trieRoot)!;
            object layer = trie.GetType().GetField("Storage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(trie)!;
            object storage = layer.GetType().GetField("_tableStorage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(layer)!;
            storage.GetType().GetMethod("DisableCommittedMappingForTesting", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(storage, null);
        }
        public void Dispose() => Engine.Dispose();
    }

    internal sealed class Report
    {
        public string Label { get; set; }
        public string Runtime { get; set; }
        public string AssemblySha256 { get; set; }
        public string Commit { get; set; }
        public int SampleMilliseconds { get; set; }
        public int Rounds { get; set; }
        public string TieredCompilation { get; set; }
        public bool DiagnosticsEnabled { get; set; }
        public string OperatingSystem { get; set; }
        public string Architecture { get; set; }
        public int ProcessorCount { get; set; }
        public string PriorityClass { get; set; }
        public long[] StorageCounters { get; set; }
        public List<Measurement> Measurements { get; set; } = new();
    }
    internal sealed class Measurement
    {
        public string Id { get; set; }
        public List<Sample> Samples { get; set; } = new();
    }
    internal sealed class Sample
    {
        public long Operations { get; set; }
        public double ElapsedSeconds { get; set; }
        public long AllocatedBytes { get; set; }
        public double OperationsPerSecond { get; set; }
        public double BytesPerOperation { get; set; }
        public int Gc0 { get; set; }
        public int Gc1 { get; set; }
        public int Gc2 { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public double[] LatencySamples { get; set; } = Array.Empty<double>();
        public double LatencyP50Nanoseconds { get; set; }
        public double LatencyP95Nanoseconds { get; set; }
        public long Commits { get; set; }
        public double CommitP50Nanoseconds { get; set; }
        public double CommitP95Nanoseconds { get; set; }
    }
}
