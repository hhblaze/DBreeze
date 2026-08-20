using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DBreeze;
using DBreeze.DataTypes;
using DBreeze.Transactions;

namespace DBreeze.Net8.Benchmarks;

internal static class LianaTrieTraversalPerformanceProbe
{
    private const int DefaultRepetitions = 5;
    private const int WarmupRepetitions = 3;
    private static int _repetitions = DefaultRepetitions;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static int Run(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);
            _repetitions = options.Repetitions;
            if (File.Exists(options.OutputPath))
                throw new IOException("Traversal performance output already exists: " + options.OutputPath);
            if (Directory.Exists(options.RootPath))
                throw new IOException("Traversal performance root already exists: " + options.RootPath);

            Directory.CreateDirectory(options.RootPath);
            var results = new List<Result>();
            RunStorage(options, storageOnDisk: false, results);
            RunStorage(options, storageOnDisk: true, results);

            var report = new Report
            {
                Label = options.Label,
                WarmupRepetitions = WarmupRepetitions,
                Repetitions = _repetitions,
                Runtime = Environment.Version.ToString(),
                AssemblyVersion = typeof(DBreezeEngine).Assembly.GetName().Version?.ToString() ?? String.Empty,
                Results = results.OrderBy(static result => result.Scenario, StringComparer.Ordinal).ToArray(),
            };
            Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)
                ?? throw new InvalidOperationException("Output path must have a parent directory."));
            File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(report, JsonOptions), new UTF8Encoding(false));
            Console.WriteLine($"PASS liana-traversal-perf {options.Label}: {results.Count} scenarios");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
    }

    private static void RunStorage(Options options, bool storageOnDisk, ICollection<Result> results)
    {
        string storage = storageOnDisk ? "disk" : "memory";
        string databasePath = Path.Combine(options.RootPath, storage);
        using DBreezeEngine engine = storageOnDisk
            ? new DBreezeEngine(databasePath)
            : new DBreezeEngine(new DBreezeConfiguration
            {
                Storage = DBreezeConfiguration.eStorage.MEMORY,
                NotifyAhead_WhenWriteTablePossibleDeadlock = false,
            });

        Fixture shallow = CreateFixture(engine, "shallow", deep: false);
        Fixture deep = CreateFixture(engine, "deep", deep: true);

        foreach (bool lazy in new[] { true, false })
        {
            RunShape(engine, storage, storageOnDisk ? databasePath : null, shallow, lazy, results);
            RunShape(engine, storage, storageOnDisk ? databasePath : null, deep, lazy, results);
            Measure(engine, storage, storageOnDisk ? databasePath : null, deep, lazy, "NestedStartFrom",
                () => ConsumeNestedStartFrom(engine, deep, lazy), results);
        }
    }

    private static Fixture CreateFixture(DBreezeEngine engine, string name, bool deep)
    {
        byte[] prefix = deep ? Enumerable.Repeat((byte)0x62, 512).ToArray() : new byte[] { 0x40 };
        int count = deep ? 256 : 4_096;
        string table = "traversal-" + name;
        string nestedTable = "traversal-nested-" + name;
        byte[] nestedRoot = { 0xF0, 1 };
        byte[] nested2 = { 0xF0, 2 };
        byte[] nested3 = { 0xF0, 3 };
        byte[] nested4 = { 0xF0, 4 };

        using (Transaction transaction = engine.GetTransaction())
        {
            transaction.SynchronizeTables(table, nestedTable);
            for (int i = 0; i < count; i++)
            {
                byte[] key = CreateKey(prefix, deep, i);
                transaction.Insert(table, key, CreateValue(i));
            }

            transaction.Insert(nestedTable, nestedRoot, new byte[] { 1 });
            NestedTable level1 = null;
            NestedTable level2 = null;
            NestedTable level3 = null;
            NestedTable level4 = null;
            try
            {
                level1 = transaction.InsertTable(nestedTable, nestedRoot, 0);
                level1.Insert(nested2, new byte[] { 2 });
                level2 = level1.GetTable(nested2, 1);
                level2.Insert(nested3, new byte[] { 3 });
                level3 = level2.GetTable(nested3, 2);
                level3.Insert(nested4, new byte[] { 4 });
                level4 = level3.GetTable(nested4, 3);
                int nestedCount = Math.Min(count, 256);
                for (int i = 0; i < nestedCount; i++)
                    level4.Insert(CreateKey(prefix, deep, i), CreateValue(i));

                transaction.Commit();
            }
            finally
            {
                level4?.Dispose();
                level3?.Dispose();
                level2?.Dispose();
                level1?.Dispose();
            }
        }

        return new Fixture
        {
            Name = name,
            Table = table,
            NestedTable = nestedTable,
            Prefix = prefix,
            Count = count,
            Pivot = CreateKey(prefix, deep, count / 2),
            RangeStart = CreateKey(prefix, deep, count / 4),
            RangeStop = CreateKey(prefix, deep, count * 3 / 4),
            Closest = Append(CreateKey(prefix, deep, count / 2), 0x7F),
            NestedRoot = nestedRoot,
            Nested2 = nested2,
            Nested3 = nested3,
            Nested4 = nested4,
        };
    }

    private static void RunShape(
        DBreezeEngine engine,
        string storage,
        string databasePath,
        Fixture fixture,
        bool lazy,
        ICollection<Result> results)
    {
        Measure(engine, storage, databasePath, fixture, lazy, "StartFrom",
            () => Consume(engine, fixture, lazy, Operation.StartFrom), results);
        Measure(engine, storage, databasePath, fixture, lazy, "SkipFrom",
            () => Consume(engine, fixture, lazy, Operation.SkipFrom), results);
        Measure(engine, storage, databasePath, fixture, lazy, "StartsWith",
            () => Consume(engine, fixture, lazy, Operation.StartsWith), results);
        Measure(engine, storage, databasePath, fixture, lazy, "ClosestPrefix",
            () => Consume(engine, fixture, lazy, Operation.ClosestPrefix), results);
        Measure(engine, storage, databasePath, fixture, lazy, "MinMax",
            () => Consume(engine, fixture, lazy, Operation.MinMax), results);
        Measure(engine, storage, databasePath, fixture, lazy, "FromToControl",
            () => Consume(engine, fixture, lazy, Operation.FromTo), results);
        Measure(engine, storage, databasePath, fixture, lazy, "SkipControl",
            () => Consume(engine, fixture, lazy, Operation.Skip), results);
        Measure(engine, storage, databasePath, fixture, lazy, "EarlyDispose",
            () => ConsumeEarlyDispose(engine, fixture, lazy), results);
    }

    private static void Measure(
        DBreezeEngine engine,
        string storage,
        string databasePath,
        Fixture fixture,
        bool lazy,
        string operation,
        Func<long> action,
        ICollection<Result> results)
    {
        for (int warmup = 0; warmup < WarmupRepetitions; warmup++)
            action();
        long bytesBefore = GetDatabaseBytes(databasePath);
        var elapsed = new double[_repetitions];
        var allocated = new long[_repetitions];
        var retained = new long[_repetitions];
        long checksum = 0;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        for (int run = 0; run < _repetitions; run++)
        {
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            checksum ^= action();
            long stopped = Stopwatch.GetTimestamp();
            long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

            elapsed[run] = (stopped - started) * 1_000.0 / Stopwatch.Frequency;
            allocated[run] = allocatedAfter - allocatedBefore;
        }

        for (int run = 0; run < _repetitions; run++)
        {
            long retainedBefore = GC.GetTotalMemory(true);
            action();
            long retainedAfter = GC.GetTotalMemory(true);
            retained[run] = retainedAfter - retainedBefore;
        }

        results.Add(new Result
        {
            Scenario = String.Join("/", storage, lazy ? "lazy" : "eager", fixture.Name, operation),
            MedianMilliseconds = Median(elapsed),
            MedianAllocatedBytes = Median(allocated),
            MedianRetainedBytes = Median(retained),
            DatabaseSizeDelta = GetDatabaseBytes(databasePath) - bytesBefore,
            Checksum = checksum,
            Milliseconds = elapsed,
            AllocatedBytes = allocated,
            RetainedBytes = retained,
        });
    }

    private static long Consume(DBreezeEngine engine, Fixture fixture, bool lazy, Operation operation)
    {
        using Transaction transaction = engine.GetTransaction();
        transaction.ValuesLazyLoadingIsOn = lazy;
        long checksum = 0;
        switch (operation)
        {
            case Operation.StartFrom:
                return ConsumeRows(transaction.SelectForwardStartFrom<byte[], byte[]>(
                    fixture.Table, fixture.Pivot, true), lazy);
            case Operation.SkipFrom:
                return ConsumeRows(transaction.SelectForwardSkipFrom<byte[], byte[]>(
                    fixture.Table, fixture.Pivot, 32), lazy);
            case Operation.StartsWith:
                return ConsumeRows(transaction.SelectForwardStartsWith<byte[], byte[]>(
                    fixture.Table, fixture.Prefix), lazy);
            case Operation.ClosestPrefix:
                return ConsumeRows(transaction.SelectForwardStartsWithClosestToPrefix<byte[], byte[]>(
                    fixture.Table, fixture.Closest), lazy);
            case Operation.MinMax:
                checksum ^= ConsumeRow(transaction.Min<byte[], byte[]>(fixture.Table), lazy);
                checksum ^= ConsumeRow(transaction.Max<byte[], byte[]>(fixture.Table), lazy);
                return checksum;
            case Operation.FromTo:
                return ConsumeRows(transaction.SelectForwardFromTo<byte[], byte[]>(
                    fixture.Table, fixture.RangeStart, true, fixture.RangeStop, true), lazy);
            case Operation.Skip:
                return ConsumeRows(transaction.SelectForwardSkip<byte[], byte[]>(
                    fixture.Table, (ulong)(fixture.Count / 2)), lazy);
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static long ConsumeEarlyDispose(DBreezeEngine engine, Fixture fixture, bool lazy)
    {
        long checksum = 0;
        for (int i = 0; i < 32; i++)
        {
            using Transaction transaction = engine.GetTransaction();
            transaction.ValuesLazyLoadingIsOn = lazy;
            using IEnumerator<Row<byte[], byte[]>> iterator = transaction
                .SelectForwardStartFrom<byte[], byte[]>(fixture.Table, fixture.Pivot, true)
                .GetEnumerator();
            if (iterator.MoveNext())
                checksum ^= ConsumeRow(iterator.Current, lazy);
        }
        return checksum;
    }

    private static long ConsumeNestedStartFrom(DBreezeEngine engine, Fixture fixture, bool lazy)
    {
        using Transaction transaction = engine.GetTransaction();
        using NestedTable level1 = transaction.SelectTable(fixture.NestedTable, fixture.NestedRoot, 0);
        using NestedTable level2 = level1.GetTable(fixture.Nested2, 1);
        using NestedTable level3 = level2.GetTable(fixture.Nested3, 2);
        using NestedTable level4 = level3.GetTable(fixture.Nested4, 3);
        level4.ValuesLazyLoadingIsOn = lazy;
        return ConsumeRows(level4.SelectForwardStartFrom<byte[], byte[]>(fixture.Pivot, true), lazy);
    }

    private static long ConsumeRows(IEnumerable<Row<byte[], byte[]>> rows, bool lazy)
    {
        long checksum = 0;
        foreach (Row<byte[], byte[]> row in rows)
            checksum = unchecked(checksum * 397) ^ ConsumeRow(row, lazy);
        return checksum;
    }

    private static long ConsumeRow(Row<byte[], byte[]> row, bool lazy)
    {
        long checksum = row.Key == null ? 0 : row.Key.Length;
        if (!lazy)
            checksum = unchecked(checksum * 397) ^ (row.Value == null ? -1 : row.Value.Length);
        return checksum;
    }

    private static byte[] CreateKey(byte[] prefix, bool deep, int value)
    {
        if (deep)
            return Append(prefix, (byte)value);
        return Append(prefix, (byte)(value >> 8), (byte)value, (byte)(value * 31));
    }

    private static byte[] CreateValue(int value) =>
        new[] { (byte)0xA5, (byte)(value >> 8), (byte)value, (byte)(value * 17) };

    private static byte[] Append(byte[] prefix, params byte[] suffix)
    {
        byte[] result = new byte[prefix.Length + suffix.Length];
        Buffer.BlockCopy(prefix, 0, result, 0, prefix.Length);
        Buffer.BlockCopy(suffix, 0, result, prefix.Length, suffix.Length);
        return result;
    }

    private static long GetDatabaseBytes(string path)
    {
        return path == null || !Directory.Exists(path)
            ? 0
            : Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(static file => new FileInfo(file).Length);
    }

    private static double Median(double[] values)
    {
        double[] ordered = values.OrderBy(static value => value).ToArray();
        return ordered[ordered.Length / 2];
    }

    private static long Median(long[] values)
    {
        long[] ordered = values.OrderBy(static value => value).ToArray();
        return ordered[ordered.Length / 2];
    }

    private enum Operation
    {
        StartFrom,
        SkipFrom,
        StartsWith,
        ClosestPrefix,
        MinMax,
        FromTo,
        Skip,
    }

    private sealed class Fixture
    {
        internal string Name { get; set; }
        internal string Table { get; set; }
        internal string NestedTable { get; set; }
        internal byte[] Prefix { get; set; }
        internal int Count { get; set; }
        internal byte[] Pivot { get; set; }
        internal byte[] RangeStart { get; set; }
        internal byte[] RangeStop { get; set; }
        internal byte[] Closest { get; set; }
        internal byte[] NestedRoot { get; set; }
        internal byte[] Nested2 { get; set; }
        internal byte[] Nested3 { get; set; }
        internal byte[] Nested4 { get; set; }
    }

    private sealed class Result
    {
        public string Scenario { get; set; }
        public double MedianMilliseconds { get; set; }
        public long MedianAllocatedBytes { get; set; }
        public long MedianRetainedBytes { get; set; }
        public long DatabaseSizeDelta { get; set; }
        public long Checksum { get; set; }
        public double[] Milliseconds { get; set; }
        public long[] AllocatedBytes { get; set; }
        public long[] RetainedBytes { get; set; }
    }

    private sealed class Report
    {
        public string Label { get; set; }
        public int WarmupRepetitions { get; set; }
        public int Repetitions { get; set; }
        public string Runtime { get; set; }
        public string AssemblyVersion { get; set; }
        public Result[] Results { get; set; }
    }

    private sealed class Options
    {
        internal string RootPath { get; private set; }
        internal string OutputPath { get; private set; }
        internal string Label { get; private set; }
        internal int Repetitions { get; private set; } = DefaultRepetitions;

        internal static Options Parse(string[] args)
        {
            var options = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--liana-traversal-perf":
                        break;
                    case "--root":
                        options.RootPath = Path.GetFullPath(ReadValue(args, ref i, "--root"));
                        break;
                    case "--output":
                        options.OutputPath = Path.GetFullPath(ReadValue(args, ref i, "--output"));
                        break;
                    case "--label":
                        options.Label = ReadValue(args, ref i, "--label");
                        break;
                    case "--repetitions":
                        string value = ReadValue(args, ref i, "--repetitions");
                        if (!Int32.TryParse(value, out int repetitions) || repetitions < 1 || repetitions > 100)
                            throw new ArgumentOutOfRangeException(
                                "--repetitions", value, "Repetitions must be in the range 1..100.");
                        options.Repetitions = repetitions;
                        break;
                    default:
                        throw new ArgumentException("Unknown traversal performance option: " + args[i], nameof(args));
                }
            }

            if (String.IsNullOrWhiteSpace(options.RootPath) || String.IsNullOrWhiteSpace(options.OutputPath) ||
                String.IsNullOrWhiteSpace(options.Label))
            {
                throw new ArgumentException(
                    "--liana-traversal-perf requires --root, --output and --label.", nameof(args));
            }
            return options;
        }

        private static string ReadValue(string[] args, ref int index, string option)
        {
            if (++index >= args.Length || String.IsNullOrWhiteSpace(args[index]))
                throw new ArgumentException(option + " requires a value.", nameof(args));
            return args[index];
        }
    }
}
