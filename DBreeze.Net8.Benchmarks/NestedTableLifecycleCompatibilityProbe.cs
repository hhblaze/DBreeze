using System.Text;
using System.Text.Json;
using DBreeze;
using DBreeze.DataTypes;

namespace DBreeze.Net8.Benchmarks;

internal static class NestedTableLifecycleCompatibilityProbe
{
    private const string Table = "nested-lifecycle";
    private static readonly byte[] OriginalParent = { 0x71, 0x10 };
    private static readonly byte[] RenamedParent = { 0x71, 0x20 };
    private static readonly byte[] DeepParent = { 0x04 };

    internal static int Run(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);
            switch (options.Action)
            {
                case "create":
                    Create(options.DatabasePath);
                    Verify(options.DatabasePath, FixtureState.Base);
                    break;
                case "verify-base":
                    Verify(options.DatabasePath, FixtureState.Base);
                    break;
                case "write-early":
                    Verify(options.DatabasePath, FixtureState.Base);
                    WriteWithEarlyDispose(options.DatabasePath);
                    Verify(options.DatabasePath, FixtureState.EarlyDispose);
                    break;
                case "verify-early":
                    Verify(options.DatabasePath, FixtureState.EarlyDispose);
                    break;
                case "write-safe":
                    Verify(options.DatabasePath, FixtureState.Base);
                    WriteWhileHandlesAreOpen(options.DatabasePath);
                    Verify(options.DatabasePath, FixtureState.SafeWrite);
                    break;
                case "verify-safe":
                    Verify(options.DatabasePath, FixtureState.SafeWrite);
                    break;
                case "write-multi":
                    Verify(options.DatabasePath, FixtureState.Base);
                    WriteWithOpenHandlesAcrossCommits(options.DatabasePath);
                    Verify(options.DatabasePath, FixtureState.SafeWrite);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(options.Action), options.Action,
                        "Unknown nested lifecycle compatibility action.");
            }

            WriteResult(options.OutputPath, options.Action, options.DatabasePath);
            Console.WriteLine($"PASS nested-lifecycle-compat {options.Action}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
    }

    private static void Create(string databasePath)
    {
        if (Directory.Exists(databasePath))
            throw new IOException($"Fixture already exists and will not be overwritten: {databasePath}");

        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)
            ?? throw new InvalidOperationException("Fixture path must have a parent directory."));

        using var engine = new DBreezeEngine(databasePath);
        using var transaction = engine.GetTransaction();
        transaction.Insert(Table, new byte[] { 0x01 }, new byte[] { 0xA1 });
        using NestedTable parent = transaction.InsertTable(Table, OriginalParent, 0);
        parent.Insert(new byte[] { 0x01 }, new byte[] { 10 });
        parent.Insert(new byte[] { 0x02 }, new byte[] { 20 });
        using NestedTable deep = parent.GetTable(DeepParent, 7);
        deep.Insert(new byte[] { 0x05 }, new byte[] { 50 });
        transaction.Commit();
    }

    private static void WriteWithEarlyDispose(string databasePath)
    {
        using var engine = new DBreezeEngine(databasePath);
        using var transaction = engine.GetTransaction();

        NestedTable parent = transaction.InsertTable(Table, OriginalParent, 0);
        parent.Insert(new byte[] { 0x01 }, new byte[] { 11 });
        parent.RemoveKey(new byte[] { 0x02 });
        parent.Insert(new byte[] { 0x03 }, new byte[] { 30 });

        NestedTable deep = parent.GetTable(DeepParent, 7);
        deep.Insert(new byte[] { 0x06 }, new byte[] { 60 });
        deep.Dispose();
        deep.CloseTable();
        parent.Dispose();
        parent.CloseTable();

        transaction.ChangeKey(Table, OriginalParent, RenamedParent);
        transaction.Commit();
    }

    private static void WriteWhileHandlesAreOpen(string databasePath)
    {
        using var engine = new DBreezeEngine(databasePath);
        using var transaction = engine.GetTransaction();
        using NestedTable parent = transaction.InsertTable(Table, OriginalParent, 0);
        parent.Insert(new byte[] { 0x01 }, new byte[] { 12 });
        parent.Insert(new byte[] { 0x07 }, new byte[] { 70 });
        using NestedTable deep = parent.GetTable(DeepParent, 7);
        deep.Insert(new byte[] { 0x05 }, new byte[] { 55 });
        transaction.Commit();
    }

    private static void WriteWithOpenHandlesAcrossCommits(string databasePath)
    {
        using var engine = new DBreezeEngine(databasePath);
        using var transaction = engine.GetTransaction();
        using NestedTable parent = transaction.InsertTable(Table, OriginalParent, 0);
        using NestedTable deep = parent.GetTable(DeepParent, 7);

        parent.Insert(new byte[] { 0x07 }, new byte[] { 70 });
        transaction.Commit();

        parent.Insert(new byte[] { 0x01 }, new byte[] { 12 });
        deep.Insert(new byte[] { 0x05 }, new byte[] { 55 });
        transaction.Commit();
    }

    private static void Verify(string databasePath, FixtureState state)
    {
        if (!Directory.Exists(databasePath))
            throw new DirectoryNotFoundException(databasePath);

        using var engine = new DBreezeEngine(databasePath);
        using var transaction = engine.GetTransaction();
        Row<byte[], byte[]> marker = transaction.Select<byte[], byte[]>(Table, new byte[] { 0x01 });
        Ensure(marker.Exists && marker.Value.SequenceEqual(new byte[] { 0xA1 }), "Master marker mismatch.");

        byte[] parentKey = state == FixtureState.EarlyDispose ? RenamedParent : OriginalParent;
        byte[] absentKey = state == FixtureState.EarlyDispose ? OriginalParent : RenamedParent;
        Ensure(transaction.Select<byte[], byte[]>(Table, parentKey).Exists, "Nested parent is missing.");
        Ensure(!transaction.Select<byte[], byte[]>(Table, absentKey).Exists, "Unexpected parent identity exists.");

        using NestedTable parent = transaction.SelectTable(Table, parentKey, 0);
        switch (state)
        {
            case FixtureState.Base:
                AssertValue(parent, 0x01, 10);
                AssertValue(parent, 0x02, 20);
                Ensure(!parent.Select<byte[], byte[]>(new byte[] { 0x03 }).Exists, "Unexpected early-dispose row.");
                break;
            case FixtureState.EarlyDispose:
                AssertValue(parent, 0x01, 11);
                Ensure(!parent.Select<byte[], byte[]>(new byte[] { 0x02 }).Exists, "Removed row survived.");
                AssertValue(parent, 0x03, 30);
                break;
            case FixtureState.SafeWrite:
                AssertValue(parent, 0x01, 12);
                AssertValue(parent, 0x02, 20);
                AssertValue(parent, 0x07, 70);
                break;
        }

        using NestedTable deep = parent.GetTable(DeepParent, 7);
        AssertValue(deep, 0x05, state == FixtureState.SafeWrite ? (byte)55 : (byte)50);
        if (state == FixtureState.EarlyDispose)
            AssertValue(deep, 0x06, 60);
        else
            Ensure(!deep.Select<byte[], byte[]>(new byte[] { 0x06 }).Exists, "Unexpected deep early-dispose row.");
    }

    private static void AssertValue(NestedTable table, byte key, byte value)
    {
        Row<byte[], byte[]> row = table.Select<byte[], byte[]>(new byte[] { key });
        Ensure(row.Exists && row.Value.SequenceEqual(new byte[] { value }), $"Nested value {key:X2} mismatch.");
    }

    private static void WriteResult(string outputPath, string action, string databasePath)
    {
        if (File.Exists(outputPath))
            throw new IOException($"Result already exists and will not be overwritten: {outputPath}");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("Result path must have a parent directory."));
        var result = new
        {
            Action = action,
            DatabasePath = databasePath,
            AssemblyVersion = typeof(DBreezeEngine).Assembly.GetName().Version?.ToString() ?? String.Empty,
        };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidDataException(message);
    }

    private enum FixtureState
    {
        Base,
        EarlyDispose,
        SafeWrite,
    }

    private sealed class Options
    {
        internal string Action { get; private set; }
        internal string DatabasePath { get; private set; }
        internal string OutputPath { get; private set; }

        internal static Options Parse(string[] args)
        {
            var options = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--nested-lifecycle-compat":
                        options.Action = ReadValue(args, ref i, "--nested-lifecycle-compat").ToLowerInvariant();
                        break;
                    case "--database":
                        options.DatabasePath = Path.GetFullPath(ReadValue(args, ref i, "--database"));
                        break;
                    case "--output":
                        options.OutputPath = Path.GetFullPath(ReadValue(args, ref i, "--output"));
                        break;
                    default:
                        throw new ArgumentException($"Unknown nested lifecycle compatibility option: {args[i]}", nameof(args));
                }
            }

            if (String.IsNullOrEmpty(options.Action) || String.IsNullOrEmpty(options.DatabasePath) ||
                String.IsNullOrEmpty(options.OutputPath))
            {
                throw new ArgumentException(
                    "--nested-lifecycle-compat requires an action, --database and --output.", nameof(args));
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
