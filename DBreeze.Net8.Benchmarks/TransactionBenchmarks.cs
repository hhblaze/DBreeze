using BenchmarkDotNet.Attributes;
using DBreeze;

namespace DBreeze.Net8.Benchmarks;

[MemoryDiagnoser]
[MedianColumn]
public class TransactionBenchmarks
{
    private DBreezeEngine _engine;
    private Dictionary<int, int> _dictionary;
    private HashSet<int> _hashSet;
    private HashSet<string> _mergeTables;

    [Params(1_000)]
    public int ItemCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _engine = new DBreezeEngine(new DBreezeConfiguration
        {
            Storage = DBreezeConfiguration.eStorage.MEMORY,
            NotifyAhead_WhenWriteTablePossibleDeadlock = false,
        });
        _dictionary = Enumerable.Range(0, ItemCount).ToDictionary(static x => x, static x => x);
        _hashSet = Enumerable.Range(0, ItemCount).ToHashSet();
        _mergeTables = new HashSet<string>(Enumerable.Range(0, 8).Select(static x => "tran-merge-" + x));

        foreach (string table in _mergeTables)
        {
            using var transaction = _engine.GetTransaction();
            for (int i = 0; i < ItemCount; i++)
                transaction.Insert(table, i, i);
            transaction.Commit();
        }

        using (var transaction = _engine.GetTransaction())
        {
            transaction.InsertDictionary("tran-dictionary", _dictionary, false);
            transaction.Commit();
        }
        using (var transaction = _engine.GetTransaction())
        {
            transaction.InsertHashSet("tran-hashset", _hashSet, false);
            transaction.Commit();
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _engine?.Dispose();

    [Benchmark(OperationsPerInvoke = 1_000)]
    public void TransactionLifecycle()
    {
        for (int i = 0; i < 1_000; i++)
            _engine.GetTransaction().Dispose();
    }

    [Benchmark(OperationsPerInvoke = 1_000)]
    public void RandomKeySorterBatch()
    {
        using var transaction = _engine.GetTransaction();
        for (int i = 0; i < 1_000; i++)
            transaction.RandomKeySorter.Insert("tran-rks", i, i);
        transaction.Rollback();
    }

    [Benchmark]
    public int MultiSelectPriorityQueue()
    {
        using var transaction = _engine.GetTransaction();
        return transaction.Multi_SelectForwardFromTo<int, int>(
            _mergeTables, 0, true, ItemCount - 1, true).Count();
    }

    [Benchmark]
    public void DictionaryReplacement()
    {
        using var transaction = _engine.GetTransaction();
        transaction.InsertDictionary("tran-dictionary", _dictionary, true);
        transaction.Commit();
    }

    [Benchmark]
    public void HashSetReplacement()
    {
        using var transaction = _engine.GetTransaction();
        transaction.InsertHashSet("tran-hashset", _hashSet, true);
        transaction.Commit();
    }

    [Benchmark(OperationsPerInvoke = 4)]
    public void CoordinatorContention()
    {
        Parallel.For(0, 4, _ =>
        {
            using var transaction = _engine.GetTransaction();
            transaction.SynchronizeTables("tran-contention");
            var current = transaction.Select<string, int>("tran-contention", "counter");
            transaction.Insert("tran-contention", "counter", current.Exists ? current.Value + 1 : 1);
            transaction.Commit();
        });
    }
}
