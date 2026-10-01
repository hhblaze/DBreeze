using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using DBreeze;

namespace DBreeze.Net8.Benchmarks;

internal static class CacheObjectSizes
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Type CacheType(string name) => typeof(DBreezeEngine).Assembly.GetType("DBreeze.LianaTrie." + name, true)!;
    private static Func<object> Factory(ConstructorInfo constructor, params object[] values) =>
        Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(constructor,
            constructor.GetParameters().Select((p, i) => Expression.Convert(Expression.Constant(values[i], typeof(object)), p.ParameterType))), typeof(object))).Compile();

    private static double Size(Func<object> create)
    {
        const int count = 10000;
        var keep = new object[count];
        for (int i = 0; i < 100; i++) keep[i] = create(); // JIT before counting.
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < count; i++) keep[i] = create();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        GC.KeepAlive(keep);
        return bytes / (double)count;
    }

    internal static int Run(string[] args)
    {
        int outputIndex = Array.IndexOf(args, "--output");
        if (outputIndex < 0 || outputIndex + 1 >= args.Length) throw new ArgumentException("--output required.");
        Type keyType = CacheType("CommittedReadNodeCacheKey"), entryType = CacheType("CommittedReadNodeCacheEntry");
        object key = Activator.CreateInstance(keyType, Flags, null, new object[] { 0L, 1UL }, null)!;
        ulong[] links = new ulong[257];
        Func<object> image = Factory(CacheType("CommittedReadNode").GetConstructors(Flags).Single(), links);
        object node = image();
        Func<object> entry = Factory(entryType.GetConstructors(Flags).Single(), null, key, node);
        Type dictionaryNode = typeof(ConcurrentDictionary<,>).GetNestedType("Node", BindingFlags.NonPublic)!.MakeGenericType(keyType, entryType);
        Func<object> dictionary = Factory(dictionaryNode.GetConstructors(Flags).Single(), key, entry(), 0, null);
        Type linkedNode = typeof(LinkedListNode<>).MakeGenericType(entryType);
        Func<object> linked = Factory(linkedNode.GetConstructor(new[] { entryType })!, entry());
        double nodeBytes = Size(image), entryBytes = Size(entry), dictionaryBytes = Size(dictionary), listBytes = Size(linked);
        bool intrusive = entryType.GetField("GlobalNext", Flags) != null;
        bool linkedList = entryType.GetField("GlobalOrderNode", Flags) != null;
        var report = new
        {
            Runtime = RuntimeInformation.FrameworkDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            AssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DBreezeEngine).Assembly.Location))),
            Samples = 10000, NodeObjectBytes = nodeBytes, EntryObjectBytes = entryBytes,
            DictionaryNodeBytes = dictionaryBytes, LinkedListNodeBytes = listBytes,
            DenseArrayBytes = Size(() => new ulong[257]),
            Intrusive = intrusive,
            OrderStructure = intrusive ? "Intrusive" : linkedList ? "LinkedList" : "Queue",
            EntryAccountedOverhead = (int)entryType.GetProperty("Weight", Flags)!.GetValue(entry())! -
                (int)node.GetType().GetProperty("RetainedBytes", Flags)!.GetValue(node)!,
            MeasuredEntryOverhead = nodeBytes + entryBytes + dictionaryBytes + (linkedList ? 2 * listBytes : 0),
            Note = "Array bytes are already in node.RetainedBytes. Fixed table arrays and dictionary bucket capacity are outside the existing per-entry accounting model."
        };
        AuditPersistence.WriteJson(Path.GetFullPath(args[outputIndex + 1]), report);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report));
        return 0;
    }
}
