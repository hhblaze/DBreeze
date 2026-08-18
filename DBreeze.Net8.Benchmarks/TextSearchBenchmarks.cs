using BenchmarkDotNet.Attributes;
using DBreeze.TextSearch;
using DBreeze.Utils;

namespace DBreeze.Net8.Benchmarks;

[MemoryDiagnoser]
[MedianColumn]
public class TextSearchBenchmarks
{
    private const string DatabaseRootEnvironmentVariable = "DBREEZE_TEXTSEARCH_BENCH_ROOT";
    private const string SearchTable = "benchmark-text-search";
    private DBreezeEngine _engine;
    private DBreezeEngine _encryptedEngine;
    private DBreezeEngine _indexEngine;
    private bool _indexVersion;

    [Params(10_000)]
    public int DocumentCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        string configuredRoot = Environment.GetEnvironmentVariable(DatabaseRootEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            throw new InvalidOperationException(
                $"{DatabaseRootEnvironmentVariable} must point to a fresh benchmark directory.");
        }

        string databaseRoot = Path.GetFullPath(configuredRoot);
        Directory.CreateDirectory(databaseRoot);
        string processRoot = Path.Combine(databaseRoot, "process-" + Environment.ProcessId);
        if (Directory.Exists(processRoot))
        {
            throw new IOException(
                $"TextSearch benchmark process directory already exists and will not be overwritten: {processRoot}");
        }

        Directory.CreateDirectory(processRoot);
        _engine = CreateEngine(Path.Combine(processRoot, "plain"));
        _indexEngine = CreateEngine(Path.Combine(processRoot, "index"));
        byte[] key = Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();
        byte[] iv = Enumerable.Range(0, 16).Select(static value => (byte)(15 - value)).ToArray();
        _encryptedEngine = CreateEngine(Path.Combine(processRoot, "encrypted"), new WabiStreamCrypto(key, iv));

        Populate(_engine, SearchTable, DocumentCount);
        Populate(_encryptedEngine, SearchTable, DocumentCount);
        using (var transaction = _indexEngine.GetTransaction())
        {
            for (int id = 0; id < 128; id++)
                transaction.TextInsert("benchmark-text-indexing", id.To_4_bytes_array_BigEndian(),
                    "premium prefixable indexing", "common versiona");
            transaction.Commit();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _engine?.Dispose();
        _encryptedEngine?.Dispose();
        _indexEngine?.Dispose();
    }

    [Benchmark(OperationsPerInvoke = 128)]
    public void SynchronousIndexing()
    {
        using var transaction = _indexEngine.GetTransaction();
        string exact = _indexVersion ? "common versiona" : "common versionb";
        for (int i = 0; i < 128; i++)
        {
            transaction.TextInsert("benchmark-text-indexing", i.To_4_bytes_array_BigEndian(),
                "premium prefixable indexing", exact);
        }
        transaction.Commit();
        _indexVersion = !_indexVersion;
    }

    [Benchmark]
    public int SparseAnd()
    {
        using var transaction = _engine.GetTransaction();
        return transaction.TextSearch(SearchTable).BlockAnd("", "common sparse").GetDocumentIDs().Count();
    }

    [Benchmark]
    public int DenseAnd()
    {
        using var transaction = _engine.GetTransaction();
        return transaction.TextSearch(SearchTable).BlockAnd("", "common dense").GetDocumentIDs().Count();
    }

    [Benchmark]
    public int PrefixOr()
    {
        using var transaction = _engine.GetTransaction();
        return transaction.TextSearch(SearchTable).BlockOr("pref comm").GetDocumentIDs().Count();
    }

    [Benchmark]
    public int EncryptedSearch()
    {
        using var transaction = _encryptedEngine.GetTransaction();
        return transaction.TextSearch(SearchTable).BlockAnd("pref", "common").GetDocumentIDs().Count();
    }

    private static DBreezeEngine CreateEngine(string databasePath, ITextStreamCrypto encryptor = null)
    {
        var configuration = new DBreezeConfiguration
        {
            DBreezeDataFolderName = databasePath,
            NotifyAhead_WhenWriteTablePossibleDeadlock = false,
        };
        configuration.TextSearchConfig.TextEncryptor = encryptor;
        configuration.TextSearchConfig.UseTextEncryptor = encryptor != null;
        return new DBreezeEngine(configuration);
    }

    private static void Populate(DBreezeEngine engine, string table, int count)
    {
        using var transaction = engine.GetTransaction();
        for (int id = 0; id < count; id++)
        {
            string exact = id % 997 == 0
                ? "common dense sparse"
                : id % 2 == 0 ? "common dense" : "common";
            transaction.TextInsert(table, id.To_4_bytes_array_BigEndian(), "prefixable commonword", exact);
        }
        transaction.Commit();
    }
}
