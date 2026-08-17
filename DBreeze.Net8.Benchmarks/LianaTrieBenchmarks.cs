using BenchmarkDotNet.Attributes;
using DBreeze;

namespace DBreeze.Net8.Benchmarks;

[MemoryDiagnoser]
public class LianaTrieBenchmarks
{
    public enum StorageKind
    {
        Memory,
        Disk,
    }

    private const string TableName = "liana";
    private DBreezeEngine _engine;
    private byte[][] _keys;
    private byte[] _updateValue;
    private string _databasePath;

    [Params(StorageKind.Memory, StorageKind.Disk)]
    public StorageKind Storage { get; set; }

    [Params(8, 32, 128)]
    public int KeyLength { get; set; }

    [Params(16, 128, 4096)]
    public int ValueLength { get; set; }

    // Keeps the representative defaults while allowing CI/smoke validation to avoid
    // spending most of its time seeding every parameter combination.
    private int RecordCount =>
        Environment.GetEnvironmentVariable("DBREEZE_BENCHMARK_SMOKE") == "1"
            ? 1_000
            : ValueLength == 4096 ? 10_000 : 100_000;

    [GlobalSetup]
    public void Setup()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), "DBreeze.Net8.Benchmarks", Guid.NewGuid().ToString("N"));
        _engine = Storage == StorageKind.Memory
            ? new DBreezeEngine(new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.MEMORY })
            : new DBreezeEngine(_databasePath);

        _keys = new byte[RecordCount][];
        byte[] value = new byte[ValueLength];
        new Random(42).NextBytes(value);
        _updateValue = value.ToArray();
        _updateValue[0] ^= 0xFF;

        using var transaction = _engine.GetTransaction();
        for (int i = 0; i < _keys.Length; i++)
        {
            byte[] key = CreateCommonPrefixKey(i, KeyLength);
            _keys[i] = key;
            transaction.Insert(TableName, key, value);
        }
        transaction.Commit();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _engine?.Dispose();
        if (Storage == StorageKind.Disk && Directory.Exists(_databasePath))
            Directory.Delete(_databasePath, true);
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = 1_000)]
    public int PointReadHit()
    {
        int checksum = 0;
        using var transaction = _engine.GetTransaction();
        for (int i = 0; i < 1_000; i++)
        {
            var row = transaction.Select<byte[], byte[]>(TableName, _keys[(i * 7919) % _keys.Length]);
            checksum += row.Exists ? row.Value[0] : 0;
        }
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = 1_000)]
    public int PointReadMiss()
    {
        int misses = 0;
        using var transaction = _engine.GetTransaction();
        for (int i = 0; i < 1_000; i++)
        {
            byte[] key = CreateCommonPrefixKey(_keys.Length + i, KeyLength);
            if (!transaction.Select<byte[], byte[]>(TableName, key).Exists)
                misses++;
        }
        return misses;
    }

    [Benchmark]
    public long ForwardScanLazy()
    {
        long checksum = 0;
        using var transaction = _engine.GetTransaction();
        foreach (var row in transaction.SelectForward<byte[], byte[]>(TableName))
            checksum += row.Key[^1];
        return checksum;
    }

    [Benchmark]
    public long ForwardScanEager()
    {
        long checksum = 0;
        using var transaction = _engine.GetTransaction();
        transaction.ValuesLazyLoadingIsOn = false;
        foreach (var row in transaction.SelectForward<byte[], byte[]>(TableName))
            checksum += row.Value[0];
        return checksum;
    }

    [Benchmark]
    public long NarrowForwardRange()
    {
        int first = _keys.Length / 2;
        int last = Math.Min(first + 999, _keys.Length - 1);
        long checksum = 0;
        using var transaction = _engine.GetTransaction();
        foreach (var row in transaction.SelectForwardFromTo<byte[], byte[]>(
                     TableName, _keys[first], true, _keys[last], true))
            checksum += row.Key[^1];
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = 1_000)]
    public void UpdateBatchAndCommit()
    {
        using var transaction = _engine.GetTransaction();
        for (int i = 0; i < 1_000; i++)
            transaction.Insert(TableName, _keys[i], _updateValue);
        transaction.Commit();
    }

    [Benchmark(OperationsPerInvoke = 1_000)]
    public void RemoveBatchAndRollback()
    {
        using var transaction = _engine.GetTransaction();
        for (int i = 0; i < 1_000; i++)
            transaction.RemoveKey(TableName, _keys[i]);
        transaction.Rollback();
    }

    private static byte[] CreateCommonPrefixKey(int value, int length)
    {
        byte[] key = new byte[length];
        key.AsSpan(0, length - sizeof(int)).Fill(0x2A);
        key[^4] = (byte)(value >> 24);
        key[^3] = (byte)(value >> 16);
        key[^2] = (byte)(value >> 8);
        key[^1] = (byte)value;
        return key;
    }
}
