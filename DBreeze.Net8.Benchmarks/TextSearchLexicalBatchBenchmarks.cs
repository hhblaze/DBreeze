using BenchmarkDotNet.Attributes;
using DBreeze.TextSearch;

namespace DBreeze.Net8.Benchmarks;

[MemoryDiagnoser]
[MedianColumn]
[InvocationCount(1)]
public class TextSearchLexicalBatchBenchmarks
{
    private const string DatabaseRootEnvironmentVariable = "DBREEZE_TEXTSEARCH_BENCH_ROOT";
    private DBreezeEngine _plainEngine;
    private DBreezeEngine _encryptedEngine;
    private string[] _documents;
    private int _plainTable;
    private int _encryptedTable;

    [Params(16_384)]
    public int UniqueWordCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        string configuredRoot = Environment.GetEnvironmentVariable(DatabaseRootEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configuredRoot))
            throw new InvalidOperationException($"{DatabaseRootEnvironmentVariable} must point to a fresh benchmark directory.");

        string processRoot = Path.Combine(Path.GetFullPath(configuredRoot), "lexical-process-" + Environment.ProcessId);
        if (Directory.Exists(processRoot))
            throw new IOException($"TextSearch lexical benchmark directory already exists: {processRoot}");
        Directory.CreateDirectory(processRoot);

        _plainEngine = CreateEngine(Path.Combine(processRoot, "plain"));
        byte[] key = Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();
        byte[] iv = Enumerable.Range(0, 16).Select(static value => (byte)(15 - value)).ToArray();
        _encryptedEngine = CreateEngine(Path.Combine(processRoot, "encrypted"), new WabiStreamCrypto(key, iv));
        _documents = CreateInterleavedDocuments(UniqueWordCount, 128);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _plainEngine?.Dispose();
        _encryptedEngine?.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void PlainHighCardinalityIndexing() =>
        Index(_plainEngine, "lexical-plain-" + Interlocked.Increment(ref _plainTable));

    [Benchmark]
    public void EncryptedHighCardinalityIndexing() =>
        Index(_encryptedEngine, "lexical-encrypted-" + Interlocked.Increment(ref _encryptedTable));

    private void Index(DBreezeEngine engine, string tableName)
    {
        using var transaction = engine.GetTransaction();
        for (int documentId = 0; documentId < _documents.Length; documentId++)
        {
            transaction.TextInsert(tableName, BitConverter.GetBytes(documentId), null, _documents[documentId]);
        }
        transaction.Commit();
    }

    private static DBreezeEngine CreateEngine(string folder, ITextStreamCrypto encryptor = null)
    {
        var configuration = new DBreezeConfiguration
        {
            DBreezeDataFolderName = folder,
            NotifyAhead_WhenWriteTablePossibleDeadlock = false,
        };
        configuration.TextSearchConfig.TextEncryptor = encryptor;
        configuration.TextSearchConfig.UseTextEncryptor = encryptor != null;
        return new DBreezeEngine(configuration);
    }

    private static string[] CreateInterleavedDocuments(int wordCount, int documentCount)
    {
        var builders = Enumerable.Range(0, documentCount)
            .Select(static _ => new System.Text.StringBuilder())
            .ToArray();

        // Reversing the target document makes discovery order intentionally non-lexical across
        // documents; the TextSearch word-reference batch must restore ordinal prefix locality.
        for (int word = 0; word < wordCount; word++)
        {
            int document = documentCount - 1 - (word % documentCount);
            if (builders[document].Length != 0)
                builders[document].Append(' ');
            builders[document].Append("lexicalword").Append(word.ToString("D6"));
        }

        return builders.Select(static builder => builder.ToString()).ToArray();
    }
}
