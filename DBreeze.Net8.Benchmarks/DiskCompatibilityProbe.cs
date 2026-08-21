using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DBreeze;
using DBreeze.DataTypes;
using DBreeze.Objects;
using DBreeze.Storage;
using DBreeze.Utils;

namespace DBreeze.Net8.Benchmarks;

internal static class DiskCompatibilityProbe
{
    private const int BaseIntRows = 2_048;
    private const int DateRows = 256;
    private const int ByteRows = 256;

    private static readonly string[] CompatibilityTables =
    {
        "compat-int",
        "compat-date",
        "compat-bytes",
        "compat-parts",
        "compat-blocks",
        "compat-fixed",
        "compat-master",
        "compat-objects",
        "compat-nullable-values",
        "compat-type-byte",
        "compat-type-sbyte",
        "compat-type-short",
        "compat-type-ushort",
        "compat-type-uint",
        "compat-type-long",
        "compat-type-ulong",
        "compat-type-float",
        "compat-type-double",
        "compat-type-decimal",
        "compat-type-string",
        "compat-type-utf8",
        "compat-type-ascii",
        "compat-type-unicode",
        "compat-type-bool",
        "compat-type-char",
        "compat-type-guid",
        "compat-rks",
        "compat-dictionary",
        "compat-hashset",
        "compat-text",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    internal static int Run(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);
            switch (options.Action)
            {
                case "create":
                    Create(options.DatabasePath);
                    WriteManifest(options.OutputPath, BuildManifest(options.DatabasePath, "base"));
                    break;
                case "verify-base":
                    Validate(options.DatabasePath, extended: false);
                    WriteManifest(options.OutputPath, BuildManifest(options.DatabasePath, "base"));
                    break;
                case "extend":
                    Validate(options.DatabasePath, extended: false);
                    Extend(options.DatabasePath);
                    WriteManifest(options.OutputPath, BuildManifest(options.DatabasePath, "extended"));
                    break;
                case "verify-extended":
                    Validate(options.DatabasePath, extended: true);
                    WriteManifest(options.OutputPath, BuildManifest(options.DatabasePath, "extended"));
                    break;
                case "create-backup":
                    CreateWithBackup(options.DatabasePath, options.BackupPath);
                    WriteManifest(options.OutputPath, BuildManifest(options.DatabasePath, "base"));
                    break;
                case "restore-backup":
                    RestoreBackup(options.DatabasePath, options.BackupPath);
                    WriteManifest(options.OutputPath, BuildManifest(options.DatabasePath, "base"));
                    break;
                case "create-journal":
                    CreateJournalRecoveryFixture(options.DatabasePath);
                    WriteManifest(options.OutputPath, BuildJournalManifest(options.DatabasePath));
                    break;
                case "verify-journal":
                    VerifyJournalRecoveryFixture(options.DatabasePath);
                    WriteManifest(options.OutputPath, BuildJournalManifest(options.DatabasePath));
                    break;
                case "compare":
                    Compare(options.LeftManifestPath, options.RightManifestPath, options.OutputPath,
                        options.PhysicalPolicy);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(options.Action), options.Action, "Unknown disk compatibility action.");
            }

            Console.WriteLine($"PASS disk-compat {options.Action}");
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
        PrepareNewDatabase(databasePath);

        using (var engine = new DBreezeEngine(databasePath))
            PopulateBaseData(engine);

        Validate(databasePath, extended: false);
    }

    private static void CreateWithBackup(string databasePath, string backupPath)
    {
        PrepareNewDatabase(databasePath);
        if (Directory.Exists(backupPath))
            throw new IOException($"Compatibility backup already exists and will not be overwritten: {backupPath}");

        var configuration = new DBreezeConfiguration
        {
            DBreezeDataFolderName = databasePath,
        };
        configuration.Backup.BackupFolderName = backupPath;

        using (var engine = new DBreezeEngine(configuration))
            PopulateBaseData(engine);

        Validate(databasePath, extended: false);
    }

    private static void RestoreBackup(string databasePath, string backupPath)
    {
        if (Directory.Exists(databasePath))
            throw new IOException($"Compatibility restore destination already exists and will not be overwritten: {databasePath}");
        if (!Directory.Exists(backupPath))
            throw new DirectoryNotFoundException(backupPath);

        string parent = Path.GetDirectoryName(databasePath)
            ?? throw new InvalidOperationException("Compatibility restore destination must have a parent directory.");
        Directory.CreateDirectory(parent);

        var restorer = new BackupRestorer
        {
            BackupFolder = backupPath,
            DataBaseFolder = databasePath,
        };
        // Legacy restorers require a subscriber; the current implementation deliberately does not.
        restorer.OnRestore += delegate { };
        restorer.StartRestoration();

        Validate(databasePath, extended: false);
    }

    private static void CreateJournalRecoveryFixture(string databasePath)
    {
        PrepareNewDatabase(databasePath);
        var configuration = new DBreezeConfiguration
        {
            DBreezeDataFolderName = databasePath,
            NotifyAhead_WhenWriteTablePossibleDeadlock = false,
        };
        configuration.AlternativeTablesLocations["journal-b"] = Path.Combine(databasePath, "alternative");
        using (var engine = new DBreezeEngine(configuration))
        {
            using (var transaction = engine.GetTransaction())
            {
                transaction.Insert("journal-a", 1, "a");
                transaction.Commit();
            }
            using (var transaction = engine.GetTransaction())
            {
                transaction.Insert("journal-b", 2, "b");
                transaction.Commit();
            }

            var field = typeof(DBreezeEngine).GetField("_transactionsJournal",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Ensure(field != null, "Transactions journal field was not found.");
            var journal = (DBreeze.Transactions.TransactionsJournal)field.GetValue(engine);
            ulong transactionNumber = journal.GetTransactionNumber();
            journal.AddTableForTransaction(transactionNumber, new FailingJournalTable("journal-a"));
            journal.AddTableForTransaction(transactionNumber, new FailingJournalTable("journal-b"));
            try
            {
                journal.FinishTransaction(transactionNumber);
                throw new InvalidDataException("Injected journal failure did not fail.");
            }
            catch (InvalidOperationException)
            {
                // Expected: leaves a durable crash-recovery marker for the other version.
            }
        }

        byte[][] payloads = ReadJournalPayloads(databasePath, configuration);
        Ensure(payloads.Length == 1, "Journal fixture did not persist exactly one marker.");
        Ensure(payloads[0].SequenceEqual(Encoding.UTF8.GetBytes(
            "<string>journal-a</string>\n<string>journal-b</string>\n")),
            "Journal payload format changed.");
    }

    private static void VerifyJournalRecoveryFixture(string databasePath)
    {
        if (!Directory.Exists(databasePath))
            throw new DirectoryNotFoundException(databasePath);
        var configuration = new DBreezeConfiguration
        {
            DBreezeDataFolderName = databasePath,
            NotifyAhead_WhenWriteTablePossibleDeadlock = false,
        };
        configuration.AlternativeTablesLocations["journal-b"] = Path.Combine(databasePath, "alternative");
        using (var engine = new DBreezeEngine(configuration))
        using (var transaction = engine.GetTransaction())
        {
            Ensure(transaction.Select<int, string>("journal-a", 1).Value == "a",
                "Journal recovery damaged journal-a.");
            Ensure(transaction.Select<int, string>("journal-b", 2).Value == "b",
                "Journal recovery damaged journal-b.");
        }
        Ensure(ReadJournalPayloads(databasePath, configuration).Length == 0,
            "Journal recovery did not clear the durable marker.");
    }

    private static byte[][] ReadJournalPayloads(string databasePath, DBreezeConfiguration configuration)
    {
        var storage = new StorageLayer(Path.Combine(databasePath, "_DBreezeTranJrnl"),
            new TrieSettings(), configuration);
        using var journal = new DBreeze.LianaTrie.LTrie(storage) { TableName = "DBreeze.TranJournal" };
        return journal.IterateForward(true, false).Select(static row => row.GetFullValue(true)).ToArray();
    }

    private static void PrepareNewDatabase(string databasePath)
    {
        if (Directory.Exists(databasePath))
            throw new IOException($"Compatibility database already exists and will not be overwritten: {databasePath}");

        string parent = Path.GetDirectoryName(databasePath)
            ?? throw new InvalidOperationException("Compatibility database must have a parent directory.");
        Directory.CreateDirectory(parent);
    }

    private static void PopulateBaseData(DBreezeEngine engine)
    {
        using var transaction = engine.GetTransaction();
        transaction.SynchronizeTables(CompatibilityTables);

        for (int i = 0; i < BaseIntRows; i++)
            transaction.Insert("compat-int", i, BaseValue(i));

        for (int i = 0; i < DateRows; i++)
            transaction.Insert("compat-date", DateAt(i), (long)i * 17);

        for (int i = 0; i < ByteRows; i++)
            transaction.Insert("compat-bytes", ByteKey(i), ByteValue(i));

        transaction.Insert("compat-parts", 1, new byte[24]);
        transaction.InsertPart("compat-parts", 1, new byte[] { 1, 2, 3 }, 8, out _, out _);
        byte[] blockPointer = transaction.InsertDataBlock("compat-blocks", null,
            Encoding.UTF8.GetBytes("compat-ordinary-base"));
        transaction.Insert("compat-blocks", 1, blockPointer);
        byte[] fixedPointer = transaction.InsertDataBlockWithFixedAddress("compat-fixed", null,
            Encoding.UTF8.GetBytes("compat-fixed-base"));
        transaction.Insert("compat-fixed", 1, fixedPointer);

        NestedTable nested = transaction.InsertTable("compat-master", 1, 0);
        for (int i = 1; i <= 6; i++)
            nested.Insert(i, "nested-base-" + i.ToString(CultureInfo.InvariantCulture));
        NestedTable child = nested.GetTable(100, 1);
        child.Insert(1, 101);
        NestedTable nestedDictionary = nested.GetTable(101, 1);
        nestedDictionary.InsertDictionary(new Dictionary<int, string> { [1] = "one", [2] = "two" }, true);
        NestedTable nestedSet = nested.GetTable(102, 1);
        nestedSet.InsertHashSet(new HashSet<int> { 3, 4 }, true);

        long identity = transaction.ObjectGetNewIdentity<long>("compat-objects");
        DBreezeObjectInsertResult<byte[]> objectResult = transaction.ObjectInsert("compat-objects",
            new DBreezeObject<byte[]>
            {
                NewEntity = true,
                Entity = new byte[] { 7, 8, 9 },
                Indexes = new List<DBreezeIndex>
                {
                    new(1, identity) { PrimaryIndex = true },
                    new(2, "compat-object"),
                },
            });
        Ensure(objectResult.EntityWasInserted && identity == 1, "Compatibility object insert failed.");
        InsertScalarTypes(transaction);

        for (int i = 0; i < 512; i++)
            transaction.RandomKeySorter.Insert("compat-rks", i, i * 3);
        for (int i = 0; i < 64; i++)
            transaction.RandomKeySorter.Remove("compat-rks", i);

        transaction.InsertDictionary("compat-dictionary",
            Enumerable.Range(0, 128).ToDictionary(static i => i, static i => i * 11), false);
        transaction.InsertHashSet("compat-hashset", Enumerable.Range(0, 128).ToHashSet(), false);

        for (int i = 0; i < 128; i++)
        {
            string contains = i % 2 == 0 ? "compatibility prefixable even" : "compatibility prefixable odd";
            transaction.TextInsert("compat-text", i.To_4_bytes_array_BigEndian(), contains, "group" + (i % 4));
        }
        transaction.Commit();
        nestedSet.CloseTable();
        nestedDictionary.CloseTable();
        child.CloseTable();
        nested.CloseTable();

        using (var schemeTransaction = engine.GetTransaction())
        {
            schemeTransaction.Insert("compat-scheme-source", 1, "scheme-value");
            schemeTransaction.Commit();
        }
        engine.Scheme.RenameTable("compat-scheme-source", "compat-scheme-live");

        engine.Resources.Insert<byte[]>("compat-resource-null", null);
        engine.Resources.Insert("compat-resource-empty", Array.Empty<byte>());
        engine.Resources.Insert("compat-resource-value", new byte[] { 1, 2, 3 });
        engine.Resources.Insert("compat-resource-update", new byte[] { 4 });
        engine.Resources.Insert("compat-resource-remove", new byte[] { 5 });
        engine.Resources.Insert("compat-resource-prefix-a", new byte[] { 10 });
        engine.Resources.Insert("compat-resource-prefix-empty", Array.Empty<byte>());
        engine.Resources.Insert<byte[]>("compat-resource-prefix-null", null);
        engine.Resources.Insert("compat-resource-prefix-z", new byte[] { 26 });
    }

    private static void Extend(string databasePath)
    {
        using (var engine = new DBreezeEngine(databasePath))
        {
            using (var transaction = engine.GetTransaction())
            {
                transaction.SynchronizeTables(CompatibilityTables);

            for (int i = 0; i < 256; i++)
                transaction.Insert("compat-int", i, ExtendedValue(i));
            for (int i = 256; i < 512; i++)
                transaction.RemoveKey("compat-int", i);
            for (int i = BaseIntRows; i < BaseIntRows + 256; i++)
                transaction.Insert("compat-int", i, AddedValue(i));

            transaction.InsertPart("compat-parts", 1, new byte[] { 4, 5 }, 12, out _, out _);
            byte[] blockPointer = transaction.Select<int, byte[]>("compat-blocks", 1).Value;
            blockPointer = transaction.InsertDataBlock("compat-blocks", blockPointer,
                Encoding.UTF8.GetBytes("compat-ordinary-extended"));
            transaction.Insert("compat-blocks", 1, blockPointer);
            byte[] fixedPointer = transaction.Select<int, byte[]>("compat-fixed", 1).Value;
            transaction.InsertDataBlockWithFixedAddress("compat-fixed", fixedPointer,
                Encoding.UTF8.GetBytes("compat-fixed-next"));

            NestedTable nested = transaction.InsertTable("compat-master", 1, 0);
            nested.Insert(2, "nested-extended-2");
            nested.Insert(7, "nested-added-7");
            NestedTable child = nested.GetTable(100, 1);
            child.Insert(2, 202);

            for (int i = 64; i < 96; i++)
                transaction.RandomKeySorter.Remove("compat-rks", i);
            for (int i = 512; i < 640; i++)
                transaction.RandomKeySorter.Insert("compat-rks", i, i * 5);

            transaction.InsertDictionary("compat-dictionary",
                Enumerable.Range(64, 128).ToDictionary(static i => i, static i => i * 13), true);
            transaction.InsertHashSet("compat-hashset", Enumerable.Range(64, 128).ToHashSet(), true);

            for (int i = 0; i < 32; i++)
                transaction.TextInsert("compat-text", i.To_4_bytes_array_BigEndian(), "updated searchable", "extended");
            for (int i = 32; i < 48; i++)
                transaction.TextRemoveAll("compat-text", i.To_4_bytes_array_BigEndian());
            for (int i = 128; i < 160; i++)
                transaction.TextInsert("compat-text", i.To_4_bytes_array_BigEndian(), "compatibility added", "extended");
                transaction.Commit();
                child.CloseTable();
                nested.CloseTable();
            }

            engine.Resources.Insert("compat-resource-update", new byte[] { 40, 41 });
            engine.Resources.Remove("compat-resource-remove");
            engine.Resources.Insert("compat-resource-added", new byte[] { 6, 7 });
            engine.Resources.Insert("compat-resource-prefix-a", new byte[] { 100 });

            engine.Scheme.RenameTable("compat-scheme-live", "compat-scheme-renamed");
            using (var lifecycle = engine.GetTransaction())
            {
                lifecycle.Insert("compat-scheme-temporary", 1, 1);
                lifecycle.Commit();
            }
            engine.Scheme.DeleteTable("compat-scheme-temporary");
        }

        Validate(databasePath, extended: true);
    }

    private static CompatibilitySummary Validate(string databasePath, bool extended)
    {
        if (!Directory.Exists(databasePath))
            throw new DirectoryNotFoundException(databasePath);

        var checksum = new StableChecksum();
        long rowCount = 0;

        using var engine = new DBreezeEngine(databasePath);
        using var transaction = engine.GetTransaction();

        var intRows = transaction.SelectForward<int, string>("compat-int").ToArray();
        Ensure(intRows.Length == BaseIntRows, $"compat-int count: {intRows.Length}");
        foreach (var row in intRows)
        {
            string expected = ExpectedIntValue(row.Key, extended);
            Ensure(expected != null, $"Unexpected compat-int key: {row.Key}");
            Ensure(string.Equals(row.Value, expected, StringComparison.Ordinal), $"compat-int value mismatch for {row.Key}");
            checksum.Add(row.Key);
            checksum.Add(row.Value);
            rowCount++;
        }

        var dateRows = transaction.SelectForward<DateTime, long>("compat-date").ToArray();
        Ensure(dateRows.Length == DateRows, $"compat-date count: {dateRows.Length}");
        for (int i = 0; i < dateRows.Length; i++)
        {
            Ensure(dateRows[i].Key == DateAt(i), $"compat-date key mismatch at {i}");
            Ensure(dateRows[i].Value == (long)i * 17, $"compat-date value mismatch at {i}");
            checksum.Add(dateRows[i].Key.Ticks);
            checksum.Add(dateRows[i].Value);
            rowCount++;
        }

        var byteRows = transaction.SelectForward<byte[], byte[]>("compat-bytes").ToArray();
        Ensure(byteRows.Length == ByteRows, $"compat-bytes count: {byteRows.Length}");
        for (int i = 0; i < byteRows.Length; i++)
        {
            Ensure(byteRows[i].Key.SequenceEqual(ByteKey(i)), $"compat-bytes key mismatch at {i}");
            Ensure(byteRows[i].Value.SequenceEqual(ByteValue(i)), $"compat-bytes value mismatch at {i}");
            checksum.Add(byteRows[i].Key);
            checksum.Add(byteRows[i].Value);
            rowCount++;
        }

        byte[] part = transaction.Select<int, byte[]>("compat-parts", 1).Value;
        byte[] expectedPart = new byte[24];
        expectedPart[8] = 1;
        expectedPart[9] = 2;
        expectedPart[10] = 3;
        if (extended)
        {
            expectedPart[12] = 4;
            expectedPart[13] = 5;
        }
        Ensure(part.SequenceEqual(expectedPart), "compat-parts mismatch");
        checksum.Add(part);
        rowCount++;

        byte[] blockPointer = transaction.Select<int, byte[]>("compat-blocks", 1).Value;
        byte[] block = transaction.SelectDataBlock("compat-blocks", blockPointer);
        string expectedBlock = extended ? "compat-ordinary-extended" : "compat-ordinary-base";
        Ensure(Encoding.UTF8.GetString(block) == expectedBlock, "compat ordinary data block mismatch");
        checksum.Add(block);
        rowCount++;

        byte[] fixedPointer = transaction.Select<int, byte[]>("compat-fixed", 1).Value;
        byte[] fixedBlock = transaction.SelectDataBlockWithFixedAddress<byte[]>("compat-fixed", fixedPointer);
        string expectedFixed = extended ? "compat-fixed-next" : "compat-fixed-base";
        Ensure(Encoding.UTF8.GetString(fixedBlock) == expectedFixed, "compat fixed data block mismatch");
        checksum.Add(fixedBlock);
        rowCount++;

        NestedTable nested = transaction.SelectTable<int>("compat-master", 1, 0);
        int lastNestedKey = extended ? 7 : 6;
        for (int i = 1; i <= lastNestedKey; i++)
        {
            string expected = extended && i == 2
                ? "nested-extended-2"
                : i == 7 ? "nested-added-7" : "nested-base-" + i.ToString(CultureInfo.InvariantCulture);
            Ensure(nested.Select<int, string>(i).Value == expected, $"compat nested mismatch for {i}");
            checksum.Add(i);
            checksum.Add(expected);
            rowCount++;
        }
        NestedTable child = nested.GetTable(100, 1);
        Ensure(child.Select<int, int>(1).Value == 101, "compat nested child base mismatch");
        checksum.Add(101);
        rowCount++;
        if (extended)
        {
            Ensure(child.Select<int, int>(2).Value == 202, "compat nested child extension mismatch");
            checksum.Add(202);
            rowCount++;
        }
        child.CloseTable();
        NestedTable nestedDictionary = nested.GetTable(101, 1);
        Dictionary<int, string> childDictionary = nestedDictionary.SelectDictionary<int, string>();
        Ensure(childDictionary.Count == 2 && childDictionary[1] == "one" && childDictionary[2] == "two",
            "compat nested dictionary mismatch");
        foreach (KeyValuePair<int, string> pair in childDictionary.OrderBy(static pair => pair.Key))
        {
            checksum.Add(pair.Key);
            checksum.Add(pair.Value);
            rowCount++;
        }
        nestedDictionary.CloseTable();
        NestedTable nestedSet = nested.GetTable(102, 1);
        HashSet<int> childSet = nestedSet.SelectHashSet<int>();
        Ensure(childSet.SetEquals(new[] { 3, 4 }), "compat nested hash-set mismatch");
        foreach (int value in childSet.OrderBy(static value => value))
        {
            checksum.Add(value);
            rowCount++;
        }
        nestedSet.CloseTable();
        nested.CloseTable();

        Row<byte[], byte[]> objectRow = transaction.Select<byte[], byte[]>("compat-objects", 1.ToIndex(1L));
        DBreezeObject<byte[]> storedObject = objectRow.ObjectGet<byte[]>();
        Ensure(storedObject?.Entity?.SequenceEqual(new byte[] { 7, 8, 9 }) == true,
            "compat object mismatch");
        checksum.Add(storedObject.Entity);
        rowCount++;

        rowCount += ValidateScalarTypes(transaction, checksum);

        var rksRows = transaction.SelectForward<int, int>("compat-rks").ToArray();
        int expectedRksCount = extended ? 544 : 448;
        Ensure(rksRows.Length == expectedRksCount, $"compat-rks count: {rksRows.Length}");
        foreach (var row in rksRows)
        {
            bool expectedKey = extended ? row.Key is >= 96 and < 640 : row.Key is >= 64 and < 512;
            Ensure(expectedKey, $"Unexpected compat-rks key: {row.Key}");
            int expectedValue = extended && row.Key >= 512 ? row.Key * 5 : row.Key * 3;
            Ensure(row.Value == expectedValue, $"compat-rks value mismatch for {row.Key}");
            checksum.Add(row.Key);
            checksum.Add(row.Value);
            rowCount++;
        }

        Dictionary<int, int> dictionary = transaction.SelectDictionary<int, int>("compat-dictionary");
        HashSet<int> hashSet = transaction.SelectHashSet<int>("compat-hashset");
        int firstNestedKey = extended ? 64 : 0;
        Ensure(dictionary.Count == 128, $"compat-dictionary count: {dictionary.Count}");
        Ensure(hashSet.Count == 128, $"compat-hashset count: {hashSet.Count}");
        for (int i = firstNestedKey; i < firstNestedKey + 128; i++)
        {
            int expectedDictionaryValue = extended ? i * 13 : i * 11;
            Ensure(dictionary.TryGetValue(i, out int actual) && actual == expectedDictionaryValue,
                $"compat-dictionary mismatch for {i}");
            Ensure(hashSet.Contains(i), $"compat-hashset missing {i}");
            checksum.Add(i);
            checksum.Add(actual);
            checksum.Add(i);
            rowCount += 2;
        }

        int[] prefixable = TextIds(transaction, "pref");
        int[] expectedPrefixable = extended
            ? Enumerable.Range(48, 80).Reverse().ToArray()
            : Enumerable.Range(0, 128).Reverse().ToArray();
        Ensure(prefixable.SequenceEqual(expectedPrefixable), "compat-text prefix result mismatch");
        foreach (int id in prefixable)
        {
            checksum.Add(id);
            rowCount++;
        }

        int[] exact = TextIds(transaction, string.Empty, extended ? "extended" : "group1");
        int[] expectedExact = extended
            ? Enumerable.Range(128, 32).Concat(Enumerable.Range(0, 32)).OrderByDescending(static id => id).ToArray()
            : Enumerable.Range(0, 128).Where(static id => id % 4 == 1).OrderByDescending(static id => id).ToArray();
        Ensure(exact.SequenceEqual(expectedExact), "compat-text exact result mismatch");
        foreach (int id in exact)
        {
            checksum.Add(id);
            rowCount++;
        }

        ValidateResources(engine, extended, checksum, ref rowCount);

        string schemeTable = extended ? "compat-scheme-renamed" : "compat-scheme-live";
        Ensure(engine.Scheme.IfUserTableExists(schemeTable), "compat Scheme renamed table is missing");
        Ensure(!engine.Scheme.IfUserTableExists(extended ? "compat-scheme-live" : "compat-scheme-source"),
            "compat Scheme source table unexpectedly exists");
        Ensure(transaction.Select<int, string>(schemeTable, 1).Value == "scheme-value",
            "compat Scheme value mismatch");
        checksum.Add(schemeTable);
        checksum.Add("scheme-value");
        rowCount++;

        long expectedRows = extended ? 3_562 : 3_480;
        Ensure(rowCount == expectedRows, $"Total compatibility row count: {rowCount}");
        return new CompatibilitySummary(rowCount, checksum.Value);
    }

    private static void ValidateResources(
        DBreezeEngine engine,
        bool extended,
        StableChecksum checksum,
        ref long rowCount)
    {
        KeyValuePair<string, byte[]>[] resources = engine.Resources
            .SelectStartsWith<byte[]>("compat-resource-")
            .ToArray();
        Ensure(resources.Length == 9, $"compat-resources count: {resources.Length}");

        var expected = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["compat-resource-null"] = null,
            ["compat-resource-empty"] = Array.Empty<byte>(),
            ["compat-resource-value"] = new byte[] { 1, 2, 3 },
            ["compat-resource-update"] = extended ? new byte[] { 40, 41 } : new byte[] { 4 },
            [extended ? "compat-resource-added" : "compat-resource-remove"] =
                extended ? new byte[] { 6, 7 } : new byte[] { 5 },
            ["compat-resource-prefix-a"] = extended ? new byte[] { 100 } : new byte[] { 10 },
            ["compat-resource-prefix-empty"] = Array.Empty<byte>(),
            ["compat-resource-prefix-null"] = null,
            ["compat-resource-prefix-z"] = new byte[] { 26 },
        };

        foreach (KeyValuePair<string, byte[]> resource in resources)
        {
            Ensure(expected.TryGetValue(resource.Key, out byte[] expectedValue),
                $"Unexpected compatibility resource: {resource.Key}");
            Ensure(expectedValue == null
                    ? resource.Value == null
                    : resource.Value != null && resource.Value.SequenceEqual(expectedValue),
                $"Compatibility resource mismatch: {resource.Key}");
            checksum.Add(resource.Key);
            checksum.Add(resource.Value == null ? -1 : resource.Value.Length);
            if (resource.Value != null)
                checksum.Add(resource.Value);
            rowCount++;
        }

        KeyValuePair<string, byte[]>[] prefix = engine.Resources
            .SelectStartsWith<byte[]>("compat-resource-prefix-")
            .ToArray();
        Ensure(prefix.Length == 4, $"compat-resource prefix count: {prefix.Length}");
        Ensure(prefix.Any(static pair => pair.Key.EndsWith("-empty", StringComparison.Ordinal)
            && pair.Value != null && pair.Value.Length == 0), "Empty resource was not preserved.");
        Ensure(prefix.Any(static pair => pair.Key.EndsWith("-null", StringComparison.Ordinal)
            && pair.Value == null), "Null resource was not preserved.");

        Ensure(engine.Resources.Select<byte[]>("compat-resource-missing") == null,
            "Missing compatibility resource unexpectedly exists.");
        Ensure(extended
                ? engine.Resources.Select<byte[]>("compat-resource-remove") == null
                : engine.Resources.Select<byte[]>("compat-resource-remove")?.SequenceEqual(new byte[] { 5 }) == true,
            "Compatibility resource remove state mismatch.");
    }

    private static void InsertScalarTypes(DBreeze.Transactions.Transaction transaction)
    {
        transaction.Insert("compat-type-byte", (byte)7, (byte)8);
        transaction.Insert("compat-type-sbyte", (sbyte)-7, (sbyte)-8);
        transaction.Insert("compat-type-short", (short)-1_234, (short)-2_345);
        transaction.Insert("compat-type-ushort", (ushort)1_234, (ushort)2_345);
        transaction.Insert("compat-type-uint", 123_456U, 654_321U);
        transaction.Insert("compat-type-long", -9_876_543_210L, 1_234_567_890L);
        transaction.Insert("compat-type-ulong", 9_876_543_210UL, 12_345_678_901UL);
        transaction.Insert("compat-type-float", -12.25F, 77.5F);
        transaction.Insert("compat-type-double", -1234.5D, 9876.25D);
        transaction.Insert("compat-type-decimal", -123456.789M, 987654.321M);
        transaction.Insert("compat-type-string", "ключ-周", "value-ß");
        transaction.Insert("compat-type-utf8", new DbUTF8("utf8-key-周"), new DbUTF8("utf8-value-ß"));
        transaction.Insert("compat-type-ascii", new DbAscii("ascii-key"), new DbAscii("ascii-value"));
        transaction.Insert("compat-type-unicode", new DbUnicode("unicode-key-Ж"), new DbUnicode("unicode-value-周"));
        transaction.Insert("compat-type-bool", 1, true);
        transaction.Insert("compat-type-char", 'Ж', '周');
        transaction.Insert("compat-type-guid", new Guid("11111111-2222-3333-4444-555555555555"),
            new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

        transaction.Insert<int, int?>("compat-nullable-values", 1, null);
        transaction.Insert<int, uint?>("compat-nullable-values", 2, null);
        transaction.Insert<int, long?>("compat-nullable-values", 3, null);
        transaction.Insert<int, ulong?>("compat-nullable-values", 4, null);
        transaction.Insert<int, short?>("compat-nullable-values", 5, null);
        transaction.Insert<int, ushort?>("compat-nullable-values", 6, null);
        transaction.Insert<int, byte?>("compat-nullable-values", 7, null);
        transaction.Insert<int, sbyte?>("compat-nullable-values", 8, null);
        transaction.Insert<int, DateTime?>("compat-nullable-values", 9, null);
        transaction.Insert<int, double?>("compat-nullable-values", 10, null);
        transaction.Insert<int, float?>("compat-nullable-values", 11, null);
        transaction.Insert<int, decimal?>("compat-nullable-values", 12, null);
        transaction.Insert<int, bool?>("compat-nullable-values", 13, null);
        transaction.Insert<int, char?>("compat-nullable-values", 14, null);
    }

    private static int ValidateScalarTypes(DBreeze.Transactions.Transaction transaction, StableChecksum checksum)
    {
        Ensure(transaction.Select<byte, byte>("compat-type-byte", 7).Value == 8, "compat byte mismatch");
        Ensure(transaction.Select<sbyte, sbyte>("compat-type-sbyte", -7).Value == -8, "compat sbyte mismatch");
        Ensure(transaction.Select<short, short>("compat-type-short", -1_234).Value == -2_345, "compat short mismatch");
        Ensure(transaction.Select<ushort, ushort>("compat-type-ushort", 1_234).Value == 2_345, "compat ushort mismatch");
        Ensure(transaction.Select<uint, uint>("compat-type-uint", 123_456U).Value == 654_321U, "compat uint mismatch");
        Ensure(transaction.Select<long, long>("compat-type-long", -9_876_543_210L).Value == 1_234_567_890L,
            "compat long mismatch");
        Ensure(transaction.Select<ulong, ulong>("compat-type-ulong", 9_876_543_210UL).Value == 12_345_678_901UL,
            "compat ulong mismatch");
        Ensure(transaction.Select<float, float>("compat-type-float", -12.25F).Value == 77.5F, "compat float mismatch");
        Ensure(transaction.Select<double, double>("compat-type-double", -1234.5D).Value == 9876.25D,
            "compat double mismatch");
        Ensure(transaction.Select<decimal, decimal>("compat-type-decimal", -123456.789M).Value == 987654.321M,
            "compat decimal mismatch");
        Ensure(transaction.Select<string, string>("compat-type-string", "ключ-周").Value == "value-ß",
            "compat string mismatch");
        Ensure(transaction.Select<DbUTF8, DbUTF8>("compat-type-utf8", new DbUTF8("utf8-key-周")).Value.Get ==
               "utf8-value-ß", "compat DbUTF8 mismatch");
        Ensure(transaction.Select<DbAscii, DbAscii>("compat-type-ascii", new DbAscii("ascii-key")).Value.Get ==
               "ascii-value", "compat DbAscii mismatch");
        Ensure(transaction.Select<DbUnicode, DbUnicode>("compat-type-unicode", new DbUnicode("unicode-key-Ж")).Value.Get ==
               "unicode-value-周", "compat DbUnicode mismatch");
        Ensure(transaction.Select<int, bool>("compat-type-bool", 1).Value, "compat bool mismatch");
        Ensure(transaction.Select<char, char>("compat-type-char", 'Ж').Value == '周', "compat char mismatch");
        Ensure(transaction.Select<Guid, Guid>("compat-type-guid",
                   new Guid("11111111-2222-3333-4444-555555555555")).Value ==
               new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), "compat Guid mismatch");

        EnsureNull<int>(transaction, 1);
        EnsureNull<uint>(transaction, 2);
        EnsureNull<long>(transaction, 3);
        EnsureNull<ulong>(transaction, 4);
        EnsureNull<short>(transaction, 5);
        EnsureNull<ushort>(transaction, 6);
        EnsureNull<byte>(transaction, 7);
        EnsureNull<sbyte>(transaction, 8);
        EnsureNull<DateTime>(transaction, 9);
        EnsureNull<double>(transaction, 10);
        EnsureNull<float>(transaction, 11);
        EnsureNull<decimal>(transaction, 12);
        EnsureNull<bool>(transaction, 13);
        EnsureNull<char>(transaction, 14);

        foreach (string table in CompatibilityTables.Where(static name => name.StartsWith("compat-type-", StringComparison.Ordinal)))
            checksum.Add(table);
        for (int key = 1; key <= 14; key++)
            checksum.Add(key);
        return 31;
    }

    private static void EnsureNull<T>(DBreeze.Transactions.Transaction transaction, int key) where T : struct
    {
        Row<int, T?> row = transaction.Select<int, T?>("compat-nullable-values", key);
        Ensure(row.Exists && row.Value == null,
            $"compat nullable value mismatch for {key}");
    }

    private static int[] TextIds(DBreeze.Transactions.Transaction transaction, string contains, string exact = "")
    {
        return transaction.TextSearch("compat-text")
            .BlockAnd(contains, exact)
            .GetDocumentIDs()
            .Select(static id => id.To_Int32_BigEndian())
            .ToArray();
    }

    private static CompatibilityManifest BuildManifest(string databasePath, string state)
    {
        CompatibilitySummary summary = Validate(databasePath, state == "extended");
        CompatibilityFile[] files = Directory.EnumerateFiles(databasePath, "*", SearchOption.AllDirectories)
            .Select(path => new CompatibilityFile
            {
                Path = Path.GetRelativePath(databasePath, path).Replace(Path.DirectorySeparatorChar, '/'),
                Length = new FileInfo(path).Length,
                Sha256 = ComputeSha256(path),
            })
            .OrderBy(static file => file.Path, StringComparer.Ordinal)
            .ToArray();

        return new CompatibilityManifest
        {
            State = state,
            DatabasePath = databasePath,
            DBreezeAssemblyVersion = typeof(DBreezeEngine).Assembly.GetName().Version?.ToString() ?? string.Empty,
            RowCount = summary.RowCount,
            Checksum = summary.Checksum,
            TotalBytes = files.Sum(static file => file.Length),
            Files = files,
        };
    }

    private static CompatibilityManifest BuildJournalManifest(string databasePath)
    {
        var checksum = new StableChecksum();
        checksum.Add(1);
        checksum.Add("a");
        checksum.Add(2);
        checksum.Add("b");
        CompatibilityFile[] files = Directory.EnumerateFiles(databasePath, "*", SearchOption.AllDirectories)
            .Select(path => new CompatibilityFile
            {
                Path = Path.GetRelativePath(databasePath, path).Replace(Path.DirectorySeparatorChar, '/'),
                Length = new FileInfo(path).Length,
                Sha256 = ComputeSha256(path),
            })
            .OrderBy(static file => file.Path, StringComparer.Ordinal)
            .ToArray();
        return new CompatibilityManifest
        {
            State = "journal",
            DatabasePath = databasePath,
            DBreezeAssemblyVersion = typeof(DBreezeEngine).Assembly.GetName().Version?.ToString() ?? String.Empty,
            RowCount = 2,
            Checksum = checksum.Value,
            TotalBytes = files.Sum(static file => file.Length),
            Files = files,
        };
    }

    private static void Compare(
        string leftPath,
        string rightPath,
        string outputDirectory,
        string physicalPolicy)
    {
        CompatibilityManifest left = LoadManifest(leftPath);
        CompatibilityManifest right = LoadManifest(rightPath);
        Ensure(left.State == right.State, $"Manifest states differ: {left.State} / {right.State}");
        Ensure(left.RowCount == right.RowCount, $"Manifest row counts differ: {left.RowCount} / {right.RowCount}");
        Ensure(left.Checksum == right.Checksum, $"Manifest checksums differ: {left.Checksum} / {right.Checksum}");

        var differences = new List<string>();
        var leftFiles = left.Files.ToDictionary(static file => file.Path, StringComparer.Ordinal);
        var rightFiles = right.Files.ToDictionary(static file => file.Path, StringComparer.Ordinal);
        foreach (string path in leftFiles.Keys.Union(rightFiles.Keys, StringComparer.Ordinal).OrderBy(static path => path, StringComparer.Ordinal))
        {
            if (!leftFiles.TryGetValue(path, out CompatibilityFile leftFile))
            {
                differences.Add($"Only right: {path}");
                continue;
            }
            if (!rightFiles.TryGetValue(path, out CompatibilityFile rightFile))
            {
                differences.Add($"Only left: {path}");
                continue;
            }
            if (leftFile.Length != rightFile.Length)
                differences.Add($"Length differs: {path}; left={leftFile.Length}; right={rightFile.Length}");
            if (!string.Equals(leftFile.Sha256, rightFile.Sha256, StringComparison.Ordinal))
                differences.Add($"SHA-256 differs: {path}; left={leftFile.Sha256}; right={rightFile.Sha256}");
        }

        var report = new CompatibilityComparisonReport
        {
            GeneratedUtc = DateTime.UtcNow,
            State = left.State,
            LeftManifest = leftPath,
            RightManifest = rightPath,
            RowCount = left.RowCount,
            Checksum = left.Checksum,
            LeftTotalBytes = left.TotalBytes,
            RightTotalBytes = right.TotalBytes,
            PhysicalPolicy = physicalPolicy,
            FileInventoryEqual = leftFiles.Keys.OrderBy(static path => path, StringComparer.Ordinal)
                .SequenceEqual(rightFiles.Keys.OrderBy(static path => path, StringComparer.Ordinal), StringComparer.Ordinal),
            FileLengthsEqual = leftFiles.Count == rightFiles.Count
                && leftFiles.All(pair => rightFiles.TryGetValue(pair.Key, out CompatibilityFile other) && pair.Value.Length == other.Length),
            FileHashesEqual = differences.Count == 0,
            Differences = differences,
        };

        if (Directory.Exists(outputDirectory))
            throw new IOException($"Compatibility comparison output already exists and will not be overwritten: {outputDirectory}");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "disk-compatibility.json"),
            JsonSerializer.Serialize(report, JsonOptions), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outputDirectory, "disk-compatibility.md"), BuildMarkdown(report), new UTF8Encoding(false));

        if (!report.FileInventoryEqual)
            throw new InvalidDataException("Disk compatibility comparison found different relative file inventories.");
        if (physicalPolicy == "strict" && (!report.FileLengthsEqual || !report.FileHashesEqual))
            throw new InvalidDataException("Strict disk compatibility comparison found physical file differences.");
    }

    private static string BuildMarkdown(CompatibilityComparisonReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# DBreeze disk compatibility comparison");
        sb.AppendLine();
        sb.AppendLine($"- State: `{report.State}`");
        sb.AppendLine($"- Physical policy: `{report.PhysicalPolicy}`");
        sb.AppendLine($"- Rows/checksum equal: `true` ({report.RowCount} / {report.Checksum})");
        sb.AppendLine($"- Relative file inventory equal: `{report.FileInventoryEqual}`");
        sb.AppendLine($"- File lengths equal: `{report.FileLengthsEqual}`");
        sb.AppendLine($"- SHA-256 equal: `{report.FileHashesEqual}`");
        sb.AppendLine($"- Total bytes: left `{report.LeftTotalBytes}`, right `{report.RightTotalBytes}`");
        sb.AppendLine();
        sb.AppendLine("## Differences");
        sb.AppendLine();
        if (report.Differences.Count == 0)
            sb.AppendLine("None.");
        else
            foreach (string difference in report.Differences)
                sb.AppendLine("- " + difference);
        return sb.ToString();
    }

    private static CompatibilityManifest LoadManifest(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Compatibility manifest was not found.", path);
        return JsonSerializer.Deserialize<CompatibilityManifest>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"Invalid compatibility manifest: {path}");
    }

    private static void WriteManifest(string path, CompatibilityManifest manifest)
    {
        if (File.Exists(path))
            throw new IOException($"Compatibility manifest already exists and will not be overwritten: {path}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Manifest must have a parent directory."));
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string ExpectedIntValue(int key, bool extended)
    {
        if (!extended)
            return key is >= 0 and < BaseIntRows ? BaseValue(key) : null;
        if (key is >= 0 and < 256)
            return ExtendedValue(key);
        if (key is >= 512 and < BaseIntRows)
            return BaseValue(key);
        if (key is >= BaseIntRows and < BaseIntRows + 256)
            return AddedValue(key);
        return null;
    }

    private static string BaseValue(int value) => "base-" + value.ToString("D4", CultureInfo.InvariantCulture);
    private static string ExtendedValue(int value) => "extended-" + value.ToString("D4", CultureInfo.InvariantCulture);
    private static string AddedValue(int value) => "added-" + value.ToString("D4", CultureInfo.InvariantCulture);
    private static DateTime DateAt(int value) => new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(value * 7L);
    private static byte[] ByteKey(int value) => new[] { (byte)(value >> 8), (byte)value, (byte)0xA5 };
    private static byte[] ByteValue(int value) => new[] { (byte)value, (byte)(value >> 8), (byte)(value * 3), (byte)(value * 7), (byte)0x5A };

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidDataException(message);
    }

    private sealed class Options
    {
        internal string Action { get; private set; }
        internal string DatabasePath { get; private set; }
        internal string BackupPath { get; private set; }
        internal string OutputPath { get; private set; }
        internal string LeftManifestPath { get; private set; }
        internal string RightManifestPath { get; private set; }
        internal string PhysicalPolicy { get; private set; } = "strict";

        internal static Options Parse(string[] args)
        {
            var options = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--disk-compat":
                        options.Action = ReadValue(args, ref i, "--disk-compat").ToLowerInvariant();
                        break;
                    case "--database":
                        options.DatabasePath = Path.GetFullPath(ReadValue(args, ref i, "--database"));
                        break;
                    case "--backup":
                        options.BackupPath = Path.GetFullPath(ReadValue(args, ref i, "--backup"));
                        break;
                    case "--output":
                        options.OutputPath = Path.GetFullPath(ReadValue(args, ref i, "--output"));
                        break;
                    case "--left":
                        options.LeftManifestPath = Path.GetFullPath(ReadValue(args, ref i, "--left"));
                        break;
                    case "--right":
                        options.RightManifestPath = Path.GetFullPath(ReadValue(args, ref i, "--right"));
                        break;
                    case "--physical-policy":
                        options.PhysicalPolicy = ReadValue(args, ref i, "--physical-policy").ToLowerInvariant();
                        if (options.PhysicalPolicy != "strict" && options.PhysicalPolicy != "compatible")
                        {
                            throw new ArgumentException(
                                "--physical-policy must be either strict or compatible.", nameof(args));
                        }
                        break;
                    default:
                        throw new ArgumentException($"Unknown disk compatibility option: {args[i]}", nameof(args));
                }
            }

            if (string.IsNullOrEmpty(options.Action) || string.IsNullOrEmpty(options.OutputPath))
                throw new ArgumentException("--disk-compat requires an action and --output.", nameof(args));

            if (options.Action == "compare")
            {
                if (string.IsNullOrEmpty(options.LeftManifestPath) || string.IsNullOrEmpty(options.RightManifestPath))
                    throw new ArgumentException("disk-compat compare requires --left and --right.", nameof(args));
            }
            else if (string.IsNullOrEmpty(options.DatabasePath))
            {
                throw new ArgumentException($"disk-compat {options.Action} requires --database.", nameof(args));
            }

            if ((options.Action == "create-backup" || options.Action == "restore-backup")
                && string.IsNullOrEmpty(options.BackupPath))
            {
                throw new ArgumentException($"disk-compat {options.Action} requires --backup.", nameof(args));
            }

            return options;
        }

        private static string ReadValue(string[] args, ref int index, string option)
        {
            if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                throw new ArgumentException($"{option} requires a value.", nameof(args));
            return args[index];
        }
    }

    private sealed class FailingJournalTable : DBreeze.Transactions.ITransactable
    {
        internal FailingJournalTable(string tableName) => TableName = tableName;
        public string TableName { get; set; }
        public void ITRCommitFinished() => throw new InvalidOperationException("Simulated process failure.");
        public void ITRCommit() { }
        public void ITRRollBack() { }
        public void ModificationThreadId(int transactionThreadId) { }
        public void SingleCommit() { }
        public void SingleRollback() { }
        public void TransactionIsFinished(int transactionThreadId) { }
    }

    private sealed class StableChecksum
    {
        private ulong _value = 14695981039346656037UL;
        internal long Value => unchecked((long)_value);

        internal void Add(int value) => Add(unchecked((ulong)(uint)value));
        internal void Add(long value) => Add(unchecked((ulong)value));
        internal void Add(DateTime value) => Add(value.Ticks);

        internal void Add(string value)
        {
            foreach (char ch in value)
                Add(ch);
        }

        internal void Add(byte[] value)
        {
            foreach (byte item in value)
                Add(item);
        }

        private void Add(ulong value)
        {
            for (int shift = 0; shift < 64; shift += 8)
            {
                _value ^= (byte)(value >> shift);
                _value *= 1099511628211UL;
            }
        }
    }

    private readonly record struct CompatibilitySummary(long RowCount, long Checksum);

    private sealed class CompatibilityManifest
    {
        public string State { get; set; }
        public string DatabasePath { get; set; }
        public string DBreezeAssemblyVersion { get; set; }
        public long RowCount { get; set; }
        public long Checksum { get; set; }
        public long TotalBytes { get; set; }
        public CompatibilityFile[] Files { get; set; } = Array.Empty<CompatibilityFile>();
    }

    private sealed class CompatibilityFile
    {
        public string Path { get; set; }
        public long Length { get; set; }
        public string Sha256 { get; set; }
    }

    private sealed class CompatibilityComparisonReport
    {
        public DateTime GeneratedUtc { get; set; }
        public string State { get; set; }
        public string LeftManifest { get; set; }
        public string RightManifest { get; set; }
        public long RowCount { get; set; }
        public long Checksum { get; set; }
        public long LeftTotalBytes { get; set; }
        public long RightTotalBytes { get; set; }
        public string PhysicalPolicy { get; set; }
        public bool FileInventoryEqual { get; set; }
        public bool FileLengthsEqual { get; set; }
        public bool FileHashesEqual { get; set; }
        public List<string> Differences { get; set; } = new();
    }
}
