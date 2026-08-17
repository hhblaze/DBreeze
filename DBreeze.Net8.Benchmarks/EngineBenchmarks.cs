using BenchmarkDotNet.Attributes;
using DBreeze;

namespace DBreeze.Net8.Benchmarks;

[MemoryDiagnoser]
[ThreadingDiagnoser]
[MedianColumn]
public class EngineBenchmarks
{
    private const string TableName = "engine-hot-table";
    private DBreezeEngine _engine;
    private DBreeze.Transactions.Transaction _tableKeeper;
    private DBreezeEngine _batchWriteEngine;
    private string _benchmarkRoot;
    private string _databasePath;
    private string _batchWriteDatabasePath;
    private string[] _coldKeys;
    private Dictionary<string, byte[]> _batch;
    private Dictionary<string, byte[]> _batchUpdate;
    private DBreezeResources.Settings _noCache;
    private DBreezeResources.Settings _forceWrite;
    private byte[] _hotValue;

    [Params(8)]
    public int Workers { get; set; }

    private int PrefixCount =>
        Environment.GetEnvironmentVariable("DBREEZE_BENCHMARK_SMOKE") == "1" ? 1_000 : 50_000;

    [GlobalSetup]
    public void Setup()
    {
        string benchmarkRoot = Environment.GetEnvironmentVariable("DBREEZE_BENCHMARK_ROOT");
        if (string.IsNullOrWhiteSpace(benchmarkRoot))
        {
            benchmarkRoot = Path.Combine(
                Path.GetTempPath(),
                "DBreeze.Net8.EngineBenchmarks");
        }

        _benchmarkRoot = Path.GetFullPath(benchmarkRoot);
        _databasePath = Path.Combine(
            _benchmarkRoot,
            "engine",
            Guid.NewGuid().ToString("N"));
        _engine = new DBreezeEngine(_databasePath);

        using (var transaction = _engine.GetTransaction())
        {
            transaction.Insert(TableName, 1, 1);
            transaction.Commit();
        }

        // Keep the disk table open so this benchmark isolates the Scheme hot lookup path.
        _tableKeeper = _engine.GetTransaction();
        _ = _tableKeeper.Select<int, int>(TableName, 1);

        _noCache = new DBreezeResources.Settings { HoldInMemory = false, HoldOnDisk = true };
        _forceWrite = new DBreezeResources.Settings
        {
            HoldInMemory = false,
            HoldOnDisk = true,
            InsertWithVerification = false,
            FastUpdates = true,
        };

        _hotValue = new byte[128];
        new Random(42).NextBytes(_hotValue);
        _engine.Resources.Insert("hot", _hotValue);
        _ = _engine.Resources.Select<byte[]>("hot");

        _coldKeys = Enumerable.Range(0, 1_000).Select(static i => "cold-" + i).ToArray();
        var cold = _coldKeys.ToDictionary(static key => key, _ => _hotValue, StringComparer.Ordinal);
        _engine.Resources.Insert(cold, _noCache);

        _batch = Enumerable.Range(0, 1_000)
            .ToDictionary(static i => "batch-" + i, _ => _hotValue, StringComparer.Ordinal);
        byte[] updateValue = new byte[_hotValue.Length];
        new Random(43).NextBytes(updateValue);
        _batchUpdate = Enumerable.Range(0, 1_000)
            .ToDictionary(static i => "batch-" + i, _ => updateValue, StringComparer.Ordinal);

        var prefix = new Dictionary<string, byte[]>(PrefixCount, StringComparer.Ordinal);
        for (int i = 0; i < PrefixCount; i++)
            prefix.Add("prefix-" + i.ToString("D8"), _hotValue);
        _engine.Resources.Insert(prefix, _noCache);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        CleanupBatchWriteIteration();
        _tableKeeper?.Dispose();
        _engine?.Dispose();
        if (Directory.Exists(_databasePath))
            Directory.Delete(_databasePath, true);
    }

    [IterationSetup(Target = nameof(ResourceBatchInsertNewKeys))]
    public void SetupBatchInsertIteration() => CreateBatchWriteEngine(seedExistingKeys: false);

    [IterationCleanup(Target = nameof(ResourceBatchInsertNewKeys))]
    public void CleanupBatchInsertIteration() => CleanupBatchWriteIteration();

    [IterationSetup(Target = nameof(ResourceBatchUpdateExisting))]
    public void SetupBatchUpdateIteration() => CreateBatchWriteEngine(seedExistingKeys: true);

    [IterationCleanup(Target = nameof(ResourceBatchUpdateExisting))]
    public void CleanupBatchUpdateIteration() => CleanupBatchWriteIteration();

    [Benchmark(OperationsPerInvoke = 8)]
    public int ParallelHotTableLookup()
    {
        int checksum = 0;
        Parallel.For(0, Workers, _ =>
        {
            using var transaction = _engine.GetTransaction();
            if (transaction.Select<int, int>(TableName, 1).Exists)
                Interlocked.Increment(ref checksum);
        });
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = 1_000)]
    public int WarmedResourcePointRead()
    {
        int checksum = 0;
        for (int i = 0; i < 1_000; i++)
            checksum += _engine.Resources.Select<byte[]>("hot")[0];
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = 1_000)]
    public int CommittedResourcePointReadWithoutCache()
    {
        int checksum = 0;
        for (int i = 0; i < _coldKeys.Length; i++)
            checksum += _engine.Resources.Select<byte[]>(_coldKeys[i], _noCache)[0];
        return checksum;
    }

    [Benchmark]
    public void ResourceBatchInsertNewKeys() => _batchWriteEngine.Resources.Insert(_batch, _forceWrite);

    [Benchmark]
    public void ResourceBatchUpdateExisting() => _batchWriteEngine.Resources.Insert(_batchUpdate, _forceWrite);

    [Benchmark]
    public int ResourceBatchSelect() => _engine.Resources.Select<byte[]>(_coldKeys, _noCache).Count;

    [Benchmark]
    public int ResourcePrefixScanStreaming()
    {
        int count = 0;
        foreach (KeyValuePair<string, byte[]> _ in _engine.Resources.SelectStartsWith<byte[]>("prefix-"))
            count++;
        return count;
    }

    private void CreateBatchWriteEngine(bool seedExistingKeys)
    {
        CleanupBatchWriteIteration();
        _batchWriteDatabasePath = Path.Combine(
            _benchmarkRoot,
            "engine-batch-write",
            Guid.NewGuid().ToString("N"));
        _batchWriteEngine = new DBreezeEngine(_batchWriteDatabasePath);
        if (seedExistingKeys)
            _batchWriteEngine.Resources.Insert(_batch, _forceWrite);
    }

    private void CleanupBatchWriteIteration()
    {
        _batchWriteEngine?.Dispose();
        _batchWriteEngine = null;
        if (!string.IsNullOrEmpty(_batchWriteDatabasePath) && Directory.Exists(_batchWriteDatabasePath))
            Directory.Delete(_batchWriteDatabasePath, true);
        _batchWriteDatabasePath = null;
    }
}
