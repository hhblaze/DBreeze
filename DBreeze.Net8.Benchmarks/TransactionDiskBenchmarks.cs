using BenchmarkDotNet.Attributes;
using DBreeze;

namespace DBreeze.Net8.Benchmarks;

internal static class TransactionDiskBenchmarkData
{
    internal const string RootEnvironmentVariable = "DBREEZE_TRANSACTION_BENCH_ROOT";

    internal static string CreateInstanceRoot(string benchmarkName)
    {
        string configuredRoot = Environment.GetEnvironmentVariable(RootEnvironmentVariable);
        string root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.GetTempPath()
            : Path.GetFullPath(configuredRoot);
        string instanceRoot = Path.Combine(root, benchmarkName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(instanceRoot);
        return instanceRoot;
    }

    internal static byte[] CreateValue(int key, byte marker)
    {
        var value = new byte[128];
        value[0] = (byte)key;
        value[1] = marker;
        value[^1] = (byte)(key >> 8);
        return value;
    }

    internal static int Permute(int value, int mask) => (value * 40503) & mask;
}

[MemoryDiagnoser]
[MedianColumn]
[InvocationCount(1)]
public class TransactionDiskReadBenchmarks
{
    private const string TableName = "transaction-read";
    private const int RecordCount = 65536;
    private const int ReadsPerInvoke = 1024;
    private const int RecordMask = RecordCount - 1;

    private string _databasePath;
    private DBreezeEngine _engine;
    private int[] _localKeys;
    private int[] _randomKeys;

    [GlobalSetup]
    public void Setup()
    {
        string instanceRoot = TransactionDiskBenchmarkData.CreateInstanceRoot(nameof(TransactionDiskReadBenchmarks));
        _databasePath = Path.Combine(instanceRoot, "read");

        using (var engine = CreateEngine())
        using (var transaction = engine.GetTransaction())
        {
            for (int key = 0; key < RecordCount; key++)
            {
                transaction.Insert<int, byte[]>(TableName, key,
                    TransactionDiskBenchmarkData.CreateValue(key, 0x51));
            }
            transaction.Commit();
        }

        _localKeys = new int[ReadsPerInvoke];
        _randomKeys = new int[ReadsPerInvoke];
        int localStart = RecordCount / 2;
        for (int i = 0; i < ReadsPerInvoke; i++)
        {
            _localKeys[i] = localStart + i;
            _randomKeys[i] = TransactionDiskBenchmarkData.Permute(i, RecordMask);
        }

        _engine = CreateEngine();
        ValidateSeed();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _engine?.Dispose();
        _engine = null;
        // Keep the final database under the configured benchmark root for inspection.
    }

    [IterationSetup(Target = nameof(RandomPointSelectAfterReopen))]
    public void ReopenBeforeIteration()
    {
        _engine?.Dispose();
        _engine = CreateEngine();
    }

    [Benchmark(OperationsPerInvoke = ReadsPerInvoke)]
    public int LocalPointSelectHot() => SelectBatch(_localKeys);

    [Benchmark(OperationsPerInvoke = ReadsPerInvoke)]
    public int RandomPointSelectHot() => SelectBatch(_randomKeys);

    [Benchmark(OperationsPerInvoke = ReadsPerInvoke)]
    public int RandomPointSelectAfterReopen() => SelectBatch(_randomKeys);

    [Benchmark(OperationsPerInvoke = ReadsPerInvoke)]
    public int EightThreadRandomPointSelectHot()
    {
        int checksum = 0;
        Parallel.For(0, 8, worker =>
        {
            int localChecksum = 0;
            using var transaction = _engine.GetTransaction();
            int start = worker * 128;
            for (int i = start; i < start + 128; i++)
            {
                var row = transaction.Select<int, byte[]>(TableName, _randomKeys[i]);
                if (!row.Exists)
                    throw new InvalidOperationException("Seeded transaction benchmark row is missing.");
                localChecksum += row.Value[0];
            }
            Interlocked.Add(ref checksum, localChecksum);
        });
        return checksum;
    }

    private DBreezeEngine CreateEngine() => new(new DBreezeConfiguration
    {
        DBreezeDataFolderName = _databasePath,
        NotifyAhead_WhenWriteTablePossibleDeadlock = false,
    });

    private int SelectBatch(int[] keys)
    {
        int checksum = 0;
        using var transaction = _engine.GetTransaction();
        for (int i = 0; i < keys.Length; i++)
        {
            var row = transaction.Select<int, byte[]>(TableName, keys[i]);
            if (!row.Exists)
                throw new InvalidOperationException("Seeded transaction benchmark row is missing.");
            checksum += row.Value[0];
        }
        return checksum;
    }

    private void ValidateSeed()
    {
        using var transaction = _engine.GetTransaction();
        if (transaction.Count(TableName) != (ulong)RecordCount)
            throw new InvalidOperationException("Transaction read benchmark seed count mismatch.");

        int expected = _randomKeys.Sum(static key => (byte)key);
        if (SelectBatch(_randomKeys) != expected)
            throw new InvalidOperationException("Transaction read benchmark seed checksum mismatch.");
    }
}

[MemoryDiagnoser]
[MedianColumn]
[InvocationCount(1)]
public class TransactionDiskWriteBenchmarks
{
    private const int BatchSize = 4096;
    private const int BatchMask = BatchSize - 1;
    private const int DurableCommitCount = 64;
    private const string SequentialTable = "transaction-insert-sequential";
    private const string RandomTable = "transaction-insert-random";
    private const string UpdateTable = "transaction-update";
    private const string DurableTable = "transaction-insert-durable";

    private string _instanceRoot;
    private string _currentDatabasePath;
    private int _iteration;
    private DBreezeEngine _engine;
    private byte[][] _insertValues;
    private byte[][] _updateValues;

    [GlobalSetup]
    public void Setup()
    {
        _instanceRoot = TransactionDiskBenchmarkData.CreateInstanceRoot(nameof(TransactionDiskWriteBenchmarks));
        _insertValues = new byte[BatchSize][];
        _updateValues = new byte[BatchSize][];
        for (int i = 0; i < BatchSize; i++)
        {
            _insertValues[i] = TransactionDiskBenchmarkData.CreateValue(i, 0x31);
            _updateValues[i] = TransactionDiskBenchmarkData.CreateValue(i, 0x73);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _engine?.Dispose();
        _engine = null;
        // The last measured database is intentionally retained for inspection.
    }

    [IterationSetup(Target = nameof(SequentialInsertBatchAndCommit))]
    public void SetupSequentialInsert() => SetupIteration(seedUpdateTable: false);

    [IterationSetup(Target = nameof(RandomInsertBatchAndCommit))]
    public void SetupRandomInsert() => SetupIteration(seedUpdateTable: false);

    [IterationSetup(Target = nameof(RandomUpdateBatchAndCommit))]
    public void SetupRandomUpdate() => SetupIteration(seedUpdateTable: true);

    [IterationSetup(Target = nameof(InsertCommitEach))]
    public void SetupDurableInsert() => SetupIteration(seedUpdateTable: false);

    [IterationCleanup(Target = nameof(SequentialInsertBatchAndCommit))]
    public void ValidateSequentialInsert() => ValidateAndClose(SequentialTable, BatchSize, _insertValues[BatchSize - 1]);

    [IterationCleanup(Target = nameof(RandomInsertBatchAndCommit))]
    public void ValidateRandomInsert() => ValidateAndClose(RandomTable, BatchSize, _insertValues[BatchSize - 1]);

    [IterationCleanup(Target = nameof(RandomUpdateBatchAndCommit))]
    public void ValidateRandomUpdate() => ValidateAndClose(UpdateTable, BatchSize, _updateValues[BatchSize - 1]);

    [IterationCleanup(Target = nameof(InsertCommitEach))]
    public void ValidateDurableInsert() => ValidateAndClose(DurableTable, DurableCommitCount,
        _insertValues[DurableCommitCount - 1]);

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public void SequentialInsertBatchAndCommit()
    {
        using var transaction = _engine.GetTransaction();
        for (int key = 0; key < BatchSize; key++)
            transaction.Insert<int, byte[]>(SequentialTable, key, _insertValues[key]);
        transaction.Commit();
    }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public void RandomInsertBatchAndCommit()
    {
        using var transaction = _engine.GetTransaction();
        for (int i = 0; i < BatchSize; i++)
        {
            int key = TransactionDiskBenchmarkData.Permute(i, BatchMask);
            transaction.Insert<int, byte[]>(RandomTable, key, _insertValues[key]);
        }
        transaction.Commit();
    }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public void RandomUpdateBatchAndCommit()
    {
        using var transaction = _engine.GetTransaction();
        for (int i = 0; i < BatchSize; i++)
        {
            int key = TransactionDiskBenchmarkData.Permute(i, BatchMask);
            transaction.Insert<int, byte[]>(UpdateTable, key, _updateValues[key]);
        }
        transaction.Commit();
    }

    [Benchmark(OperationsPerInvoke = DurableCommitCount)]
    public void InsertCommitEach()
    {
        using var transaction = _engine.GetTransaction();
        for (int key = 0; key < DurableCommitCount; key++)
        {
            transaction.Insert<int, byte[]>(DurableTable, key, _insertValues[key]);
            transaction.Commit();
        }
    }

    private void SetupIteration(bool seedUpdateTable)
    {
        DisposeAndDeletePreviousIteration();
        _currentDatabasePath = Path.Combine(_instanceRoot, (++_iteration).ToString("D4"));
        _engine = new DBreezeEngine(new DBreezeConfiguration
        {
            DBreezeDataFolderName = _currentDatabasePath,
            NotifyAhead_WhenWriteTablePossibleDeadlock = false,
        });

        if (!seedUpdateTable)
            return;

        using var transaction = _engine.GetTransaction();
        for (int key = 0; key < BatchSize; key++)
            transaction.Insert<int, byte[]>(UpdateTable, key, _insertValues[key]);
        transaction.Commit();
    }

    private void ValidateAndClose(string tableName, int expectedCount, byte[] expectedLastValue)
    {
        using (var transaction = _engine.GetTransaction())
        {
            if (transaction.Count(tableName) != (ulong)expectedCount)
                throw new InvalidOperationException($"Transaction write benchmark count mismatch for {tableName}.");

            var last = transaction.Select<int, byte[]>(tableName, expectedCount - 1);
            if (!last.Exists || !last.Value.AsSpan().SequenceEqual(expectedLastValue))
                throw new InvalidOperationException($"Transaction write benchmark value mismatch for {tableName}.");
        }

        _engine.Dispose();
        _engine = null;
    }

    private void DisposeAndDeletePreviousIteration()
    {
        _engine?.Dispose();
        _engine = null;
        if (string.IsNullOrEmpty(_currentDatabasePath) || !Directory.Exists(_currentDatabasePath))
            return;

        string root = Path.GetFullPath(_instanceRoot).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(_currentDatabasePath);
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to delete a transaction benchmark path outside its instance root.");
        Directory.Delete(candidate, true);
    }
}
