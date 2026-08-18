using System.Reflection;
using BenchmarkDotNet.Attributes;
using DBreeze;

namespace DBreeze.Net8.Benchmarks;

[MemoryDiagnoser]
[MedianColumn]
public class WabiBenchmarks
{
    private Func<List<byte[]>, IEnumerable<uint>> _enumerate;
    private Func<List<byte[]>, byte[]> _mergeAnd;
    private Func<List<byte[]>, byte[]> _mergeOr;
    private List<byte[]> _sparse;
    private List<byte[]> _dense;
    private List<byte[]> _merge2;
    private List<byte[]> _merge16;

    [GlobalSetup]
    public void Setup()
    {
        Type type = typeof(DBreezeEngine).Assembly.GetType("DBreeze.TextSearch.WABI", throwOnError: true);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        _enumerate = (Func<List<byte[]>, IEnumerable<uint>>)type.GetMethod(
            "TextSearch_AND_logic", flags, binder: null, new[] { typeof(List<byte[]>) }, modifiers: null)
            .CreateDelegate(typeof(Func<List<byte[]>, IEnumerable<uint>>));
        _mergeAnd = (Func<List<byte[]>, byte[]>)type.GetMethod(
            "MergeByAndLogic", flags, binder: null, new[] { typeof(List<byte[]>) }, modifiers: null)
            .CreateDelegate(typeof(Func<List<byte[]>, byte[]>));
        _mergeOr = (Func<List<byte[]>, byte[]>)type.GetMethod(
            "MergeByOrLogic", flags, binder: null, new[] { typeof(List<byte[]>) }, modifiers: null)
            .CreateDelegate(typeof(Func<List<byte[]>, byte[]>));

        const int length = 128 * 1024;
        byte[] sparseLeft = new byte[length];
        byte[] sparseRight = new byte[length];
        byte[] denseLeft = new byte[length];
        byte[] denseRight = new byte[length];
        for (int i = 0; i < length; i++)
        {
            sparseLeft[i] = i % 127 == 0 ? (byte)0x81 : (byte)0;
            sparseRight[i] = i % 127 == 0 ? (byte)0x01 : (byte)0;
            denseLeft[i] = 0xF7;
            denseRight[i] = 0xDF;
        }

        _sparse = new List<byte[]> { sparseLeft, sparseRight };
        _dense = new List<byte[]> { denseLeft, denseRight };
        _merge2 = _dense;
        _merge16 = new List<byte[]>(16);
        var random = new Random(771);
        for (int i = 0; i < 16; i++)
        {
            byte[] bitmap = new byte[length];
            random.NextBytes(bitmap);
            _merge16.Add(bitmap);
        }
    }

    [Benchmark]
    public ulong SparseEnumeration() => EnumerateChecksum(_sparse);

    [Benchmark]
    public ulong DenseEnumeration() => EnumerateChecksum(_dense);

    [Benchmark]
    public byte[] MergeAnd2() => _mergeAnd(_merge2);

    [Benchmark]
    public byte[] MergeAnd16() => _mergeAnd(_merge16);

    [Benchmark]
    public byte[] MergeOr2() => _mergeOr(_merge2);

    [Benchmark]
    public byte[] MergeOr16() => _mergeOr(_merge16);

    private ulong EnumerateChecksum(List<byte[]> indexes)
    {
        ulong checksum = 0;
        foreach (uint documentId in _enumerate(indexes))
            checksum += documentId;
        return checksum;
    }
}
