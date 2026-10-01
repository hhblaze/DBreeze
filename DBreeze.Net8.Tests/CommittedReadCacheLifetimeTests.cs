using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using DBreeze;

internal static class CommittedReadCacheLifetimeTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static Type Type(string name) => typeof(DBreezeEngine).Assembly.GetType("DBreeze.LianaTrie." + name)!;
    private static object Field(object value, string name) => value.GetType().GetField(name, Instance)!.GetValue(value)!;
    private static object Property(object value, string name) => value.GetType().GetProperty(name, Instance)!.GetValue(value)!;
    private static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, Instance)!.Invoke(value, args)!;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Collect() { GC.Collect(2, GCCollectionMode.Forced, true, true); GC.WaitForPendingFinalizers(); GC.Collect(2, GCCollectionMode.Forced, true, true); }
    private static int Count(object collection) => (int)Property(collection, "Count");
    private static string Folder(string test) => Path.Combine(Environment.GetEnvironmentVariable("DBREEZE_TEST_ROOT")
        ?? throw new Exception("DBREEZE_TEST_ROOT is required."), test + "-" + Guid.NewGuid().ToString("N"));
    private static object Manager(DBreezeConfiguration configuration)
    {
        object managers = Type("CommittedReadNodeCacheRegistry").GetField("Managers", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        object[] args = { configuration, null };
        Check((bool)managers.GetType().GetMethod("TryGetValue")!.Invoke(managers, args)!, "Cache manager is missing.");
        return args[1];
    }
    private static void Warm(DBreezeEngine engine, string[] keys)
    {
        for (int read = 0; read < 2; read++)
        {
            using var tx = engine.GetTransaction();
            foreach (string key in keys) Check(tx.Select<string, byte[]>("cache-lifetime", key).Exists, "Stored key was lost.");
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Capture(object manager) => new(Property(CacheOrderProbe.Global(manager).First(), "Node"));

    internal static void IntrusiveRemovalPreservesBothOrders()
    {
        object manager = Activator.CreateInstance(Type("CommittedReadNodeCacheManager"), true)!;
        Check(CacheOrderProbe.IsIntrusive(manager), "Intrusive DLL required.");
        object firstTable = Call(manager, "CreateTableCache", (object)null);
        object secondTable = Call(manager, "CreateTableCache", (object)null);
        object sync = Field(manager, "_sync");
        // Interleave tables to exercise different neighbours in the two orders.
        for (ulong i = 0; i < 6; i++)
        {
            object node = Activator.CreateInstance(Type("CommittedReadNode"), Instance, null, new object[] { new ulong[257] }, null)!;
            object key = Activator.CreateInstance(Type("CommittedReadNodeCacheKey"), Instance, null, new object[] { 0L, i }, null)!;
            Call(manager, "Admit", i % 2 == 0 ? firstTable : secondTable, key, node);
        }
        object[] original = CacheOrderProbe.Global(manager);
        var expected = original.ToList();
        // Middle, first, last, then a table's sole entry, and the manager's sole entry.
        foreach (int index in new[] { 2, 0, 5, 4, 1, 3 })
        {
            object entry = original[index], table = Property(entry, "Table");
            long bytes = (long)Field(manager, "_retainedBytes");
            lock (sync)
            {
                Check((bool)Call(table, "Remove", entry), "First removal failed.");
                Check(!(bool)Call(table, "Remove", entry), "Repeated removal changed the cache.");
            }
            expected.Remove(entry);
            Check((long)Field(manager, "_retainedBytes") == bytes - (int)Property(entry, "Weight"), "Removal accounting differs.");
            foreach (string link in new[] { "GlobalPrevious", "GlobalNext", "TablePrevious", "TableNext" })
                Check(Field(entry, link) == null, "Retired entry retains neighbours: " + link);
            Check(CacheOrderProbe.Global(manager).SequenceEqual(expected), "Global FIFO order changed.");
            foreach (object owner in new[] { firstTable, secondTable })
            {
                object[] order = CacheOrderProbe.Table(owner);
                Check(order.SequenceEqual(expected.Where(e => ReferenceEquals(Property(e, "Table"), owner))), "Table FIFO order changed.");
                Check(order.Length == Count(Field(owner, "_entries")), "Table FIFO and dictionary differ.");
                object key = Property(entry, "Key");
                int hash = (int)Property(key, "HotHash");
                var front = (Array)Field(owner, "_hotFront");
                Check(!ReferenceEquals(front.GetValue(hash & (front.Length - 1)), entry), "Removed entry remains in hot front.");
            }
        }
        Call(firstTable, "Dispose"); Call(secondTable, "Dispose");
        Check((long)Field(manager, "_retainedBytes") == 0, "Empty manager retains accounting.");
    }

    internal static void EpochChurnReleasesNodes()
    {
        string folder = Folder(nameof(EpochChurnReleasesNodes));
        try
        {
            var configuration = new DBreezeConfiguration { DBreezeDataFolderName = folder };
            using var engine = new DBreezeEngine(configuration);
            string[] keys = Enumerable.Range(0, 1000).Select(_ => Guid.NewGuid().ToString("N")).ToArray();
            using (var tx = engine.GetTransaction()) { foreach (string key in keys) tx.Insert("cache-lifetime", key, new byte[100]); tx.Commit(); }
            Warm(engine, keys);
            object manager = Manager(configuration);
            WeakReference retired = Capture(manager);
            long? warmedHeap = null;
            for (int cycle = 0; cycle < 40; cycle++)
            {
                using (var tx = engine.GetTransaction()) { tx.Insert("cache-lifetime", keys[0], BitConverter.GetBytes(cycle)); tx.Commit(); }
                Warm(engine, keys);
                using var check = engine.GetTransaction();
                Check(BitConverter.ToInt32(check.Select<string, byte[]>("cache-lifetime", keys[0]).Value) == cycle, "A cache hit returned stale data.");
                if ((cycle + 1) % 20 == 0)
                {
                    Collect();
                    long heap = GC.GetTotalMemory(false);
                    Console.WriteLine($"After {cycle + 1} epochs: live heap {heap / 1048576.0:F1} MiB; global entries {CacheOrderProbe.GlobalCount(manager)}.");
                    if (warmedHeap.HasValue)
                        Check(heap <= warmedHeap.Value + 8L * 1024 * 1024,
                            $"Live heap grew from {warmedHeap.Value} to {heap} bytes after another 20 epochs.");
                    warmedHeap = heap;
                }
            }
            Collect();
            Check(!retired.IsAlive, "Retired epoch still roots its committed-read node.");
            foreach (object entry in CacheOrderProbe.Global(manager))
                Check((int)Field(entry, "Removed") == 0, "Global ordering retains removed entries.");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    internal static void GlobalAndTableEvictionsUnlinkEntries()
    {
        string folder = Folder(nameof(GlobalAndTableEvictionsUnlinkEntries));
        try
        {
            var configuration = new DBreezeConfiguration { DBreezeDataFolderName = folder };
            using var engine = new DBreezeEngine(configuration);
            using (var tx = engine.GetTransaction()) { tx.Insert("cache-lifetime", "key", new byte[100]); tx.Commit(); }
            Warm(engine, new[] { "key" });
            object manager = Manager(configuration);
            object originalTable = Property(CacheOrderProbe.Global(manager).First(), "Table");
            object tree = Field(originalTable, "_tree");
            var tables = new List<object>();
            WeakReference retired = Fill(manager, tree, tables);
            Collect();
            Check(!retired.IsAlive, "Capacity eviction still roots a removed node.");
            int active = Count(Field(originalTable, "_entries"));
            foreach (object table in tables)
            {
                int entries = Count(Field(table, "_entries"));
                Check(CacheOrderProbe.Table(table).Length == entries, "Table FIFO retains globally or locally evicted entries.");
                Check((long)Property(table, "RetainedBytes") <= 8L * 1024 * 1024, "Table cache exceeded its budget.");
                active += entries;
            }
            Check(CacheOrderProbe.GlobalCount(manager) == active, "Global FIFO retains locally evicted entries.");
            Check((long)Field(manager, "_retainedBytes") <= 64L * 1024 * 1024, "Manager exceeded its budget.");
            foreach (object table in tables) Call(table, "Dispose");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Fill(object manager, object tree, List<object> tables)
    {
        WeakReference retired = null;
        for (int tableIndex = 0; tableIndex < 12; tableIndex++)
        {
            object table = Call(manager, "CreateTableCache", tree); tables.Add(table);
            for (ulong index = 0; index < 5000; index++)
            {
                object node = Activator.CreateInstance(Type("CommittedReadNode"), Instance, null, new object[] { new ulong[257] }, null)!;
                retired ??= new WeakReference(node);
                object key = Activator.CreateInstance(Type("CommittedReadNodeCacheKey"), Instance, null, new object[] { 0L, index }, null)!;
                Call(manager, "Admit", table, key, node);
            }
        }
        return retired;
    }

    internal static void EngineDisposeReleasesNodes()
    {
        string folder = Folder(nameof(EngineDisposeReleasesNodes));
        try
        {
            var configuration = new DBreezeConfiguration { DBreezeDataFolderName = folder };
            var (manager, retired) = DisposeEngine(configuration);
            Collect();
            Check(!retired.IsAlive, "Disposed table still roots read nodes through its manager.");
            Check(CacheOrderProbe.GlobalCount(manager) == 0, "Disposed engine retains global cache entries.");
            Check((long)Field(manager, "_retainedBytes") == 0, "Disposed engine still accounts cache bytes.");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (object Manager, WeakReference Retired) DisposeEngine(DBreezeConfiguration configuration)
    {
        using var engine = new DBreezeEngine(configuration);
        using (var tx = engine.GetTransaction()) { tx.Insert("cache-lifetime", "key", new byte[100]); tx.Commit(); }
        Warm(engine, new[] { "key" });
        object manager = Manager(configuration);
        return (manager, Capture(manager));
    }
}
