using System.Collections;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using DBreeze;

namespace DBreeze.Net8.Benchmarks;

// Synthetic eviction isolates cache bookkeeping, rather than storage or reflection overhead.
internal static class CacheStressAudit
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Type CacheType(string name) => typeof(DBreezeEngine).Assembly.GetType("DBreeze.LianaTrie." + name, true)!;
    private static object Field(object value, string name) => value.GetType().GetField(name, Instance)!.GetValue(value)!;
    private static object Property(object value, string name) => value.GetType().GetProperty(name, Instance)!.GetValue(value)!;
    private static int Count(object value) => (int)Property(value, "Count");
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Collect() { GC.Collect(2, GCCollectionMode.Forced, true, true); GC.WaitForPendingFinalizers(); GC.Collect(2, GCCollectionMode.Forced, true, true); }
    private static long[] Diagnostics() => (long[])CacheType("CommittedReadNodeCacheRegistry").GetMethod("GetDiagnostics", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;

    internal static int Run(string[] args)
    {
        string root = null, output = null, variant = null;
        int round = 1;
        bool requireReclamation = false;
        var filter = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            string option = args[i];
            if (option == "--cache-stress-audit") continue;
            if (option == "--require-reclamation") { requireReclamation = true; continue; }
            if (++i == args.Length) throw new ArgumentException("Missing value for " + option);
            switch (option)
            {
                case "--root": root = Path.GetFullPath(args[i]); break;
                case "--output": output = Path.GetFullPath(args[i]); break;
                case "--variant": variant = args[i]; break;
                case "--round": round = int.Parse(args[i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--scenarios": filter = args[i].Split(';').ToHashSet(StringComparer.Ordinal); break;
                default: throw new ArgumentException("Unknown cache-stress option: " + option);
            }
        }
        if (root == null || output == null || variant is not ("baseline" or "current") || round < 1)
            throw new ArgumentException("Cache stress requires --root, --output, --variant baseline|current and --round >= 1.");
        string[] scenarios = { "epoch-churn", "table-eviction", "global-eviction" };
        Check(filter.All(scenarios.Contains), "Unknown cache-stress scenario.");
        var report = new CacheStressReport
        {
            Variant = variant, Round = round, StartedUtc = DateTime.UtcNow,
            AssemblyPath = typeof(DBreezeEngine).Assembly.Location,
            Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
            Processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), ProcessorCount = Environment.ProcessorCount,
            ServerGc = GCSettings.IsServerGC, GcLatencyMode = GCSettings.LatencyMode.ToString(),
        };
        report.AssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(report.AssemblyPath)));
        try
        {
            Directory.CreateDirectory(root);
            foreach (string scenario in scenarios.Where(s => filter.Count == 0 || filter.Contains(s)))
            {
                string folder = Path.Combine(root, scenario);
                Check(!Directory.Exists(folder), "Scratch folder already exists: " + folder);
                CacheStressMeasurement measurement = scenario == "epoch-churn"
                    ? EpochChurn(folder) : Eviction(folder, scenario == "global-eviction" ? 12 : 1);
                measurement.Scenario = scenario;
                report.Measurements.Add(measurement);
                Check(measurement.AccountedBytes <= 64L * 1024 * 1024 &&
                    measurement.TableAccountedBytes.All(b => b <= 8L * 1024 * 1024), "Cache accounting exceeded a budget.");
                if (requireReclamation)
                {
                    Check(measurement.GlobalEntries == measurement.ActiveEntries && measurement.RemovedGlobalEntries == 0 &&
                        measurement.TableOrderEntries == measurement.ActiveEntries, "Cache ordering retains removed entries.");
                    Check(!measurement.RetiredNodeAlive, "A retired node is still rooted.");
                    Check(measurement.GlobalEntriesAfterDispose == 0 && measurement.AccountedBytesAfterDispose == 0,
                        "Disposed engine retains cache entries or accounted bytes.");
                    if (scenario == "epoch-churn") Check(measurement.HeapAfter40 <= measurement.HeapAfter20 + 8L * 1024 * 1024,
                        "Live heap grew by more than 8 MiB between epochs 20 and 40.");
                }
                Console.WriteLine($"PASS {scenario}: {measurement.ElapsedMilliseconds:F3} ms; " +
                    $"{measurement.AllocatedBytes / (double)measurement.Operations:F1} B/op; " +
                    $"global={measurement.GlobalEntries}, active={measurement.ActiveEntries}, removed={measurement.RemovedGlobalEntries}, " +
                    $"heap={measurement.PostGcLiveHeapBytes / 1048576.0:F2} MiB");
                AuditRunLayout.DeleteOwnedChild(folder, root);
            }
            report.Succeeded = report.Measurements.Count == (filter.Count == 0 ? scenarios.Length : filter.Count);
        }
        catch (Exception exception) { report.Failure = exception.ToString(); Console.Error.WriteLine(exception); }
        report.CompletedUtc = DateTime.UtcNow;
        AuditPersistence.WriteJson(output, report);
        return report.Succeeded ? 0 : 1;
    }

    private static object Manager(DBreezeConfiguration configuration)
    {
        object managers = CacheType("CommittedReadNodeCacheRegistry").GetField("Managers", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        object[] parameters = { configuration, null };
        Check((bool)managers.GetType().GetMethod("TryGetValue")!.Invoke(managers, parameters)!, "Cache manager missing.");
        return parameters[1];
    }

    private static string[] Keys()
    {
        var random = new Random(20260826);
        return Enumerable.Range(0, 1000).Select(_ => { var bytes = new byte[16]; random.NextBytes(bytes); return Convert.ToHexString(bytes); }).ToArray();
    }

    private static void Warm(DBreezeEngine engine, string[] keys)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            using var tx = engine.GetTransaction();
            foreach (string key in keys) Check(tx.Select<string, byte[]>("cache-stress", key).Exists, "Stored key lost.");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Retired(object manager) => new(Property(CacheOrderProbe.Global(manager).First(), "Node"));

    private static CacheStressMeasurement EpochChurn(string folder)
    {
        var configuration = new DBreezeConfiguration { DBreezeDataFolderName = folder };
        var engine = new DBreezeEngine(configuration);
        string[] keys = Keys();
        using (var tx = engine.GetTransaction()) { foreach (string key in keys) tx.Insert("cache-stress", key, new byte[100]); tx.Commit(); }
        Warm(engine, keys);
        object manager = Manager(configuration);
        WeakReference retired = Retired(manager);
        var result = new CacheStressMeasurement { Operations = 40L * (2000 + 2), Checksum = "last-update=39;keys=1000;epochs=40" };
        long[] before = Diagnostics();
        for (int batch = 0; batch < 2; batch++)
        {
            Collect();
            Measure(result, () =>
            {
                for (int cycle = batch * 20; cycle < (batch + 1) * 20; cycle++)
                {
                    using (var tx = engine.GetTransaction()) { tx.Insert("cache-stress", keys[0], BitConverter.GetBytes(cycle)); tx.Commit(); }
                    Warm(engine, keys);
                    using var check = engine.GetTransaction();
                    Check(BitConverter.ToInt32(check.Select<string, byte[]>("cache-stress", keys[0]).Value) == cycle, "Stale cache value.");
                }
            });
            Collect();
            if (batch == 0) result.HeapAfter20 = GC.GetTotalMemory(false);
            else result.HeapAfter40 = GC.GetTotalMemory(false);
        }
        Snapshot(result, manager, Tables(manager), retired, before);
        engine.Dispose();
        AfterDispose(result, manager);
        return result;
    }

    private static CacheStressMeasurement Eviction(string folder, int tableCount)
    {
        var configuration = new DBreezeConfiguration { DBreezeDataFolderName = folder };
        var engine = new DBreezeEngine(configuration);
        using (var tx = engine.GetTransaction()) { tx.Insert("cache-stress", "key", new byte[100]); tx.Commit(); }
        Warm(engine, new[] { "key" });
        object manager = Manager(configuration);
        object originalTable = Tables(manager).Single();
        object tree = Field(originalTable, "_tree");
        MethodInfo createTable = manager.GetType().GetMethod("CreateTableCache", Instance)!;
        object[] tables = Enumerable.Range(0, tableCount).Select(_ => createTable.Invoke(manager, new[] { tree })!).ToArray();
        Check(tables.All(t => t != null), "Synthetic cache creation failed.");
        ConstructorInfo nodeCtor = CacheType("CommittedReadNode").GetConstructors(Instance).Single();
        ConstructorInfo keyCtor = CacheType("CommittedReadNodeCacheKey").GetConstructors(Instance).Single();
        object[] nodes = Enumerable.Range(0, tableCount * 5000).Select(_ => nodeCtor.Invoke(new object[] { new ulong[257] })).ToArray();
        object[] cacheKeys = Enumerable.Range(0, 5000).Select(i => keyCtor.Invoke(new object[] { 0L, (ulong)i })).ToArray();
        Action<object, object, object> admit = BindAdmit(manager);
        var result = new CacheStressMeasurement { Operations = nodes.Length, Checksum = $"admissions={nodes.Length};tables={tableCount}" };
        long[] before = Diagnostics();
        WeakReference retired = AdmitPrepared(result, manager, tables, nodes, cacheKeys, admit);
        Snapshot(result, manager, tables.Append(originalTable).ToArray(), retired, before);
        Check(result.ActiveEntries < result.Operations, "Eviction workload did not exceed capacity.");
        foreach (IDisposable table in tables) table.Dispose();
        engine.Dispose();
        AfterDispose(result, manager);
        return result;
    }

    // A separate frame prevents JIT temporaries from rooting the node passed to the warm-up call.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AdmitPrepared(CacheStressMeasurement result, object manager,
        object[] tables, object[] nodes, object[] cacheKeys, Action<object, object, object> admit)
    {
        WeakReference retired = new(nodes[0]);
        // JIT the delegate/cache path, then invalidate the one warm-up entry before timing.
        admit(tables[0], cacheKeys[0], nodes[0]);
        manager.GetType().GetMethod("AdvanceEpoch", Instance)!.Invoke(manager, new object[] { tables[0], 0L });
        Collect();
        Measure(result, () =>
        {
            for (int table = 0; table < tables.Length; table++)
                for (int index = 0; index < cacheKeys.Length; index++)
                    admit(tables[table], cacheKeys[index], nodes[table * 5000 + index]);
        });
        // Release preconstructed inputs so the post-GC sample exposes retired-node retention.
        Array.Clear(nodes); Array.Clear(cacheKeys);
        return retired;
    }

    private static Action<object, object, object> BindAdmit(object manager)
    {
        MethodInfo method = manager.GetType().GetMethod("Admit", Instance)!;
        var parameters = method.GetParameters();
        var inputs = Enumerable.Range(0, 3).Select(i => Expression.Parameter(typeof(object), "p" + i)).ToArray();
        var arguments = inputs.Select((p, i) => Expression.Convert(p, parameters[i].ParameterType));
        return Expression.Lambda<Action<object, object, object>>(
            Expression.Call(Expression.Constant(manager), method, arguments), inputs).Compile();
    }

    private static object[] Tables(object manager) => CacheOrderProbe.Global(manager).Select(entry => Property(entry, "Table")).Distinct().ToArray();

    private static void Measure(CacheStressMeasurement result, Action body)
    {
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        var timer = new Stopwatch();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        timer.Start(); body(); timer.Stop();
        result.AllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
        result.ElapsedMilliseconds += timer.Elapsed.TotalMilliseconds;
        result.Gen0Collections += GC.CollectionCount(0) - g0;
        result.Gen1Collections += GC.CollectionCount(1) - g1;
        result.Gen2Collections += GC.CollectionCount(2) - g2;
    }

    private static void Snapshot(CacheStressMeasurement result, object manager, object[] tables, WeakReference retired, long[] before)
    {
        Collect();
        long[] after = Diagnostics();
        result.CacheHits = after[0] - before[0]; result.CacheMisses = after[1] - before[1];
        result.AccountedBytes = (long)Field(manager, "_retainedBytes");
        result.TableAccountedBytes = tables.Select(t => (long)Property(t, "RetainedBytes")).ToArray();
        result.GlobalEntries = CacheOrderProbe.GlobalCount(manager);
        result.RemovedGlobalEntries = CacheOrderProbe.Global(manager).Count(e => (int)Field(e, "Removed") != 0);
        result.ActiveEntries = tables.Sum(t => Count(Field(t, "_entries")));
        result.TableOrderEntries = tables.Sum(t => CacheOrderProbe.Table(t).Length);
        result.RetiredNodeAlive = retired.IsAlive;
        result.PostGcLiveHeapBytes = GC.GetTotalMemory(false);
        using var process = Process.GetCurrentProcess(); process.Refresh(); result.PeakWorkingSetBytes = process.PeakWorkingSet64;
    }

    private static void AfterDispose(CacheStressMeasurement result, object manager)
    {
        Collect();
        result.GlobalEntriesAfterDispose = CacheOrderProbe.GlobalCount(manager);
        result.AccountedBytesAfterDispose = (long)Field(manager, "_retainedBytes");
        result.PostDisposeLiveHeapBytes = GC.GetTotalMemory(false);
    }
}

internal sealed class CacheStressReport
{
    public string Variant { get; set; }
    public int Round { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime CompletedUtc { get; set; }
    public string AssemblyPath { get; set; }
    public string AssemblySha256 { get; set; }
    public string Runtime { get; set; }
    public string OS { get; set; }
    public string Processor { get; set; }
    public int ProcessorCount { get; set; }
    public bool ServerGc { get; set; }
    public string GcLatencyMode { get; set; }
    public bool Succeeded { get; set; }
    public string Failure { get; set; }
    public List<CacheStressMeasurement> Measurements { get; set; } = new();
}

internal sealed class CacheStressMeasurement
{
    public string Scenario { get; set; }
    public long Operations { get; set; }
    public string Checksum { get; set; }
    public double ElapsedMilliseconds { get; set; }
    public long AllocatedBytes { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
    public long CacheHits { get; set; }
    public long CacheMisses { get; set; }
    public long AccountedBytes { get; set; }
    public long[] TableAccountedBytes { get; set; }
    public int GlobalEntries { get; set; }
    public int RemovedGlobalEntries { get; set; }
    public int ActiveEntries { get; set; }
    public int TableOrderEntries { get; set; }
    public bool RetiredNodeAlive { get; set; }
    public long HeapAfter20 { get; set; }
    public long HeapAfter40 { get; set; }
    public long PostGcLiveHeapBytes { get; set; }
    public long PeakWorkingSetBytes { get; set; }
    public int GlobalEntriesAfterDispose { get; set; }
    public long AccountedBytesAfterDispose { get; set; }
    public long PostDisposeLiveHeapBytes { get; set; }
}
