using System.Text;
using DBreeze;
using DBreeze.DataTypes;
using DBreeze.Objects;
using DBreeze.Storage;
using DBreeze.Utils;

namespace DBreeze.Net8.Benchmarks;

internal static class AuditCorrectnessSuite
{
    internal static AuditCorrectnessReport Run(AuditWorkerOptions options)
    {
        var report = new AuditCorrectnessReport { Variant = options.Variant };
        var scenarios = new (string Id, string Contract, Func<string, AuditOutcome> Run)[]
        {
            ("engine-scheme", "Engine lifecycle and Scheme table lifecycle", RunEngineScheme),
            ("transaction-crud", "CRUD overloads, rollback, partial values and data blocks", RunTransactionCrud),
            ("transaction-traversal", "Forward/backward/range/prefix/skip/multi-table traversal", RunTraversal),
            ("nested-row", "NestedTable and Row public contracts", RunNestedAndRow),
            ("collections-objects", "Dictionary, HashSet and object-layer contracts", RunCollectionsAndObjects),
            ("text-resources", "TextSearch and synchronized Resources contracts", RunTextAndResources),
            ("data-types-utils", "Data type ordering, wrappers, bytes and compression", RunDataTypesAndUtils),
            ("storage-backup-remote", "Public StorageLayer transactional I/O contract", RunStorage),
        };

        try
        {
            foreach ((string id, string contract, Func<string, AuditOutcome> run) in scenarios)
            {
                string scenarioRoot = Path.Combine(options.RootPath, Sanitize(id));
                var item = new AuditCorrectnessScenario { Id = id, Contract = contract };
                try
                {
                    Directory.CreateDirectory(scenarioRoot);
                    AuditOutcome outcome = run(scenarioRoot);
                    item.Count = outcome.Count;
                    item.Checksum = outcome.Checksum;
                    item.Succeeded = true;
                }
                catch (Exception ex)
                {
                    item.Error = ex.ToString();
                    item.Succeeded = false;
                    report.Failure ??= ex.ToString();
                }
                report.Scenarios.Add(item);
            }
        }
        finally
        {
            report.Succeeded = report.Scenarios.Count == scenarios.Length &&
                               report.Scenarios.All(static item => item.Succeeded);
        }
        return report;
    }

    private static AuditOutcome RunEngineScheme(string root)
    {
        string database = Path.Combine(root, "db");
        var checksum = new AuditChecksum();
        using (var engine = new DBreezeEngine(database))
        {
            Ensure(engine.DBisOperable, "Engine is not operable.");
            using (var transaction = engine.GetTransaction())
            {
                transaction.SynchronizeTables(new List<string> { "audit-source", "audit-second" });
                transaction.Insert("audit-source", 1, "source");
                transaction.Insert("audit-second", 2, "second");
                transaction.Commit();
            }
            Ensure(engine.Scheme.IfUserTableExists("audit-source"), "Scheme did not expose a created table.");
            string path = engine.Scheme.GetTablePathFromTableName("audit-source");
            Ensure(!String.IsNullOrWhiteSpace(path) && File.Exists(path), "Scheme returned an invalid disk path.");
            string[] names = engine.Scheme.GetUserTableNamesStartingWith("audit-")
                .OrderBy(static name => name, StringComparer.Ordinal).ToArray();
            Ensure(names.Length == 2, "Scheme prefix list is incomplete.");
            foreach (string name in names)
                checksum.Add(name);
            engine.Scheme.RenameTable("audit-source", "audit-renamed");
            Ensure(!engine.Scheme.IfUserTableExists("audit-source") &&
                   engine.Scheme.IfUserTableExists("audit-renamed"), "Scheme rename failed.");
            using (var reader = engine.GetTransaction())
                checksum.Add(reader.Select<int, string>("audit-renamed", 1).Value);
            engine.Scheme.DeleteTable("audit-second");
            Ensure(!engine.Scheme.IfUserTableExists("audit-second"), "Scheme delete failed.");
        }

        using (var memory = new DBreezeEngine(new DBreezeConfiguration
               {
                   Storage = DBreezeConfiguration.eStorage.MEMORY,
                   NotifyAhead_WhenWriteTablePossibleDeadlock = false,
               }))
        using (var transaction = memory.GetTransaction(eTransactionTablesLockTypes.EXCLUSIVE, "memory-table"))
        {
            transaction.Insert("memory-table", 1, 7);
            transaction.Commit();
            checksum.Add(transaction.Select<int, int>("memory-table", 1).Value);
            string memoryPath = memory.Scheme.GetTablePathFromTableName("memory-table");
            Ensure(!String.IsNullOrWhiteSpace(memoryPath), "Memory table path is missing.");
            checksum.Add(memoryPath);
        }
        return new AuditOutcome(9, checksum.Value);
    }

    private static AuditOutcome RunTransactionCrud(string root)
    {
        string database = Path.Combine(root, "db");
        var checksum = new AuditChecksum();
        byte[] directPointer;
        byte[] fixedPointer;
        using (var engine = new DBreezeEngine(database))
        {
            using (var transaction = engine.GetTransaction())
            {
                transaction.ValuesLazyLoadingIsOn = false;
                transaction.SynchronizeTables(new List<string> { "crud", "parts", "blocks", "fixed", "rks" });
                transaction.Insert("crud", 1, "one");
                transaction.Insert("crud", 2, "two", out _);
                transaction.Insert("crud", 3, "three", out directPointer, out bool wasUpdated);
                Ensure(!wasUpdated, "New insert was reported as update.");
                transaction.Insert("crud", 4, "four", out _, out _, dontUpdateIfExists: true);
                transaction.Insert("crud", 4, "ignored", out _, out bool existing, dontUpdateIfExists: true);
                Ensure(existing, "Existing insert was not reported as update.");
                transaction.Insert("parts", 1, new byte[32]);
                transaction.InsertPart("parts", 1, new byte[] { 4, 5, 6 }, 7, out _, out bool partUpdated);
                Ensure(partUpdated, "InsertPart did not update an existing value.");
                byte[] block = transaction.InsertDataBlock("blocks", null, Encoding.UTF8.GetBytes("ordinary-block"));
                transaction.Insert("blocks", 1, block);
                fixedPointer = transaction.InsertDataBlockWithFixedAddress("fixed", null,
                    Encoding.UTF8.GetBytes("fixed-block"));
                transaction.Insert("fixed", 1, fixedPointer);
                transaction.InsertRandomKeySorter("rks", 1, 11);
                transaction.RandomKeySorter.Insert("rks", 2, 22);
                transaction.RandomKeySorter.Flush("rks");
                transaction.Commit();
            }

            using (var transaction = engine.GetTransaction())
            {
                transaction.ValuesLazyLoadingIsOn = true;
                Ensure(transaction.Count("crud") == 4, "Unexpected CRUD count.");
                checksum.Add(transaction.Min<int, string>("crud").Key);
                checksum.Add(transaction.Max<int, string>("crud").Key);
                checksum.Add(transaction.Select<int, string>("crud", 1).Value);
                checksum.Add(transaction.SelectDirect<int, string>("crud", directPointer).Value);
                byte[] part = transaction.Select<int, byte[]>("parts", 1).Value;
                Ensure(part.AsSpan(7, 3).SequenceEqual(new byte[] { 4, 5, 6 }), "Partial value mismatch.");
                checksum.Add(part);
                byte[] ordinary = transaction.SelectDataBlock("blocks",
                    transaction.Select<int, byte[]>("blocks", 1).Value);
                byte[] fixedValue = transaction.SelectDataBlockWithFixedAddress<byte[]>("fixed", fixedPointer);
                checksum.Add(ordinary);
                checksum.Add(fixedValue);
                checksum.Add(transaction.Select<int, int>("rks", 1).Value);
                checksum.Add(transaction.Select<int, int>("rks", 2).Value);

                transaction.ChangeKey("crud", 1, 10, out _, out bool changed);
                Ensure(changed, "ChangeKey failed.");
                transaction.ChangeKey("crud", 2, 20, out _);
                transaction.ChangeKey("crud", 3, 30);
                transaction.RemoveKey("crud", 4, out bool removed, out byte[] deleted);
                Ensure(removed && deleted != null, "RemoveKey overload failed.");
                transaction.Commit();
            }

            using (var transaction = engine.GetTransaction())
            {
                transaction.SynchronizeTables(new List<string> { "rollback", "recreate" });
                transaction.Insert("rollback", 1, 1);
                transaction.Rollback();
                Ensure(!transaction.Select<int, int>("rollback", 1).Exists, "Rollback leaked a row.");
                transaction.Insert("recreate", 1, 1);
                transaction.Commit();
                transaction.RemoveAllKeys("recreate", false);
                transaction.Commit();
                Ensure(transaction.Count("recreate") == 0, "RemoveAllKeys(false) failed.");
                transaction.Insert("recreate", 2, 2);
                transaction.Commit();
                transaction.RemoveAllKeys("recreate", true);
                Ensure(transaction.Count("recreate") == 0, "RemoveAllKeys(true) failed.");
            }
        }
        return new AuditOutcome(22, checksum.Value);
    }

    private static AuditOutcome RunTraversal(string root)
    {
        using var engine = new DBreezeEngine(Path.Combine(root, "db"));
        using (var transaction = engine.GetTransaction())
        {
            transaction.SynchronizeTables("numbers", "numbers-2", "prefix");
            for (int i = 0; i < 256; i++)
            {
                transaction.Insert("numbers", i, i * 3);
                transaction.Insert("numbers-2", i, i * 5);
                transaction.Insert("prefix", new byte[] { (byte)(i / 16), (byte)i }, i);
            }
            transaction.Commit();
        }

        var checksum = new AuditChecksum();
        long count = 0;
        using (var transaction = engine.GetTransaction())
        {
            void Consume(IEnumerable<Row<int, int>> rows)
            {
                foreach (Row<int, int> row in rows)
                {
                    count++;
                    checksum.Add(row.Key);
                    checksum.Add(row.Value);
                }
            }

            Consume(transaction.SelectForward<int, int>("numbers").Take(17));
            Consume(transaction.SelectBackward<int, int>("numbers", true).Take(17));
            Consume(transaction.SelectForwardStartFrom<int, int>("numbers", 100, true).Take(13));
            Consume(transaction.SelectBackwardStartFrom<int, int>("numbers", 100, false, true).Take(13));
            Consume(transaction.SelectForwardFromTo<int, int>("numbers", 40, true, 60, false));
            Consume(transaction.SelectForwardFromTo<int, int>("numbers", 40, true, 60, true, 3, false).Take(24));
            Consume(transaction.SelectBackwardFromTo<int, int>("numbers", 60, true, 40, false));
            Consume(transaction.SelectBackwardFromTo<int, int>("numbers", 60, true, 40, true, 3, false).Take(24));
            Consume(transaction.SelectForwardSkip<int, int>("numbers", 200).Take(11));
            Consume(transaction.SelectBackwardSkip<int, int>("numbers", 200, true).Take(11));
            Consume(transaction.SelectForwardSkipFrom<int, int>("numbers", 80, 20, true).Take(9));
            Consume(transaction.SelectBackwardSkipFrom<int, int>("numbers", 180, 20, true).Take(9));
            Consume(transaction.Multi_SelectForwardFromTo<int, int>(new HashSet<string> { "numbers", "numbers-2" },
                120, true, 125, true).ToArray());
            Consume(transaction.Multi_SelectBackwardFromTo<int, int>(new HashSet<string> { "numbers", "numbers-2" },
                125, true, 120, true).ToArray());

            byte[] prefix = { 7 };
            foreach (Row<byte[], int> row in transaction.SelectForwardStartsWith<byte[], int>("prefix", prefix))
            {
                count++;
                checksum.Add(row.Key);
            }
            foreach (Row<byte[], int> row in transaction.SelectBackwardStartsWith<byte[], int>("prefix", prefix, true))
            {
                count++;
                checksum.Add(row.Key);
            }
            foreach (Row<byte[], int> row in transaction
                         .SelectForwardStartsWithClosestToPrefix<byte[], int>("prefix", new byte[] { 7, 8 }).Take(5))
            {
                count++;
                checksum.Add(row.Key);
            }
            foreach (Row<byte[], int> row in transaction
                         .SelectBackwardStartsWithClosestToPrefix<byte[], int>("prefix", new byte[] { 7, 8 }, true).Take(5))
            {
                count++;
                checksum.Add(row.Key);
            }
        }
        Ensure(count > 200, "Traversal suite returned too few rows.");
        return new AuditOutcome(count, checksum.Value);
    }

    private static AuditOutcome RunNestedAndRow(string root)
    {
        using var engine = new DBreezeEngine(Path.Combine(root, "db"));
        byte[] nestedBlock;
        using (var transaction = engine.GetTransaction())
        {
            NestedTable nested = transaction.InsertTable("master", 1, 0);
            nested.ValuesLazyLoadingIsOn = false;
            nested.Insert(1, "one");
            nested.Insert(2, "two", out _);
            nested.Insert(3, "three", out _, out _);
            nested.Insert(4, "four", out _, out _, true);
            nested.Insert(5, new byte[24]);
            nested.InsertPart(5, new byte[] { 9, 8, 7 }, 4, out _, out _);
            nestedBlock = nested.InsertDataBlock(null, Encoding.UTF8.GetBytes("nested-block"));
            nested.Insert(6, nestedBlock);
            NestedTable child = nested.GetTable(100, 1);
            child.Insert(1, 101);
            NestedTable dictionaryTable = nested.GetTable(101, 1);
            dictionaryTable.InsertDictionary(new Dictionary<int, string> { [1] = "a", [2] = "b" }, true);
            NestedTable setTable = nested.GetTable(102, 1);
            setTable.InsertHashSet(new HashSet<int> { 3, 4 }, true);
            transaction.Commit();
            setTable.CloseTable();
            dictionaryTable.CloseTable();
            child.CloseTable();
            nested.CloseTable();
        }

        var checksum = new AuditChecksum();
        long count = 0;
        using (var transaction = engine.GetTransaction())
        {
            NestedTable nested = transaction.SelectTable<int>("master", 1, 0);
            Ensure(nested.Count() >= 6, "Nested count mismatch.");
            checksum.Add(nested.Min<int, string>().Key);
            checksum.Add(nested.Max<int, byte[]>().Key);
            foreach (Row<int, string> row in nested.SelectForward<int, string>().Take(4))
            {
                count++;
                checksum.Add(row.Key);
            }
            count += nested.SelectBackward<int, byte[]>(true).Take(2).Count();
            count += nested.SelectForwardFromTo<int, byte[]>(1, true, 6, true, 2).Count();
            count += nested.SelectBackwardFromTo<int, byte[]>(6, true, 1, true, true, 2).Count();
            count += nested.SelectForwardSkip<int, byte[]>(2, true).Take(2).Count();
            count += nested.SelectBackwardSkipFrom<int, byte[]>(6, 1, true).Take(2).Count();
            Row<int, byte[]> partRow = nested.Select<int, byte[]>(5);
            checksum.Add(partRow.GetValuePart(4, 3));
            Row<int, byte[]> blockRow = nested.Select<int, byte[]>(6);
            checksum.Add(blockRow.GetDataBlock(0));
            checksum.Add(nested.SelectDataBlock(nestedBlock));
            NestedTable dictionaryTable = nested.GetTable(101, 1);
            checksum.Add(dictionaryTable.SelectDictionary<int, string>().Count);
            dictionaryTable.CloseTable();
            NestedTable setTable = nested.GetTable(102, 1);
            checksum.Add(setTable.SelectHashSet<int>().Count);
            setTable.CloseTable();
            NestedTable child = nested.GetTable(100, 1);
            checksum.Add(child.Select<int, int>(1).Value);
            child.CloseTable();
            nested.CloseTable();

            NestedTable writable = transaction.InsertTable("master", 1, 0);
            writable.ChangeKey(1, 10, out _, out bool changed);
            Ensure(changed, "Nested ChangeKey failed.");
            writable.RemoveKey(2, out bool removed, out _);
            Ensure(removed, "Nested RemoveKey failed.");
            transaction.Commit();
            writable.Dispose();
        }
        return new AuditOutcome(count + 9, checksum.Value);
    }

    private static AuditOutcome RunCollectionsAndObjects(string root)
    {
        using var engine = new DBreezeEngine(Path.Combine(root, "db"));
        byte[] objectPointer;
        using (var transaction = engine.GetTransaction())
        {
            transaction.SynchronizeTables(new List<string>
                { "dict", "set", "nested-dict", "nested-set", "objects" });
            transaction.InsertDictionary("dict", new Dictionary<int, string> { [1] = "one", [2] = "two" }, true);
            transaction.InsertHashSet("set", new HashSet<int> { 3, 4, 5 }, true);
            transaction.InsertDictionary("nested-dict", 7,
                new Dictionary<int, string> { [8] = "eight", [9] = "nine" }, 0, true);
            transaction.InsertHashSet("nested-set", 7, new HashSet<int> { 10, 11 }, 0, true);
            long identity = transaction.ObjectGetNewIdentity<long>("objects");
            var value = new DBreezeObject<byte[]>
            {
                NewEntity = true,
                Entity = new byte[] { 1, 2, 3 },
                IncludeOldEntityIntoResult = true,
                Indexes = new List<DBreezeIndex>
                {
                    new(1, identity) { PrimaryIndex = true },
                    new(2, "alpha"),
                },
            };
            DBreezeObjectInsertResult<byte[]> inserted = transaction.ObjectInsert("objects", value);
            Ensure(inserted.EntityWasInserted, "Object insert failed.");
            objectPointer = inserted.PtrToObject;
            transaction.Commit();
        }

        var checksum = new AuditChecksum();
        using (var transaction = engine.GetTransaction())
        {
            transaction.SynchronizeTables(new List<string>
                { "dict", "set", "nested-dict", "nested-set", "objects" });
            Dictionary<int, string> dictionary = transaction.SelectDictionary<int, string>("dict");
            HashSet<int> set = transaction.SelectHashSet<int>("set");
            Dictionary<int, string> nestedDictionary = transaction.SelectDictionary<int, int, string>("nested-dict", 7, 0);
            HashSet<int> nestedSet = transaction.SelectHashSet<int, int>("nested-set", 7, 0);
            checksum.Add(dictionary.Count);
            checksum.Add(set.Count);
            checksum.Add(nestedDictionary.Count);
            checksum.Add(nestedSet.Count);
            DBreezeObject<byte[]> byAddress = transaction.ObjectGetByFixedAddress<byte[]>("objects", objectPointer);
            Ensure(byAddress?.Entity?.SequenceEqual(new byte[] { 1, 2, 3 }) == true, "Object fixed-address read failed.");
            checksum.Add(byAddress.Entity);
            Row<byte[], byte[]> row = transaction.Select<byte[], byte[]>("objects", 1.ToIndex(1L));
            DBreezeObject<byte[]> fromRow = row.ObjectGet<byte[]>();
            Ensure(fromRow?.Entity != null, "Row.ObjectGet failed.");
            transaction.ObjectRemove("objects", 1.ToIndex(1L));
            transaction.Commit();
        }
        return new AuditOutcome(8, checksum.Value);
    }

    private static AuditOutcome RunTextAndResources(string root)
    {
        using var engine = new DBreezeEngine(Path.Combine(root, "db"));
        using (var transaction = engine.GetTransaction())
        {
            transaction.TextInsert("text", 1.To_4_bytes_array_BigEndian(), "alpha beta", "group-a");
            transaction.TextInsert("text", 2.To_4_bytes_array_BigEndian(), "beta gamma", "group-b");
            transaction.TextAppend("text", 2.To_4_bytes_array_BigEndian(), "delta", "group-b");
            transaction.TextInsert("text", 3.To_4_bytes_array_BigEndian(), "alpha delta", "group-a");
            transaction.TextRemove("text", 3.To_4_bytes_array_BigEndian(), "delta");
            transaction.Commit();
        }

        var checksum = new AuditChecksum();
        using (var transaction = engine.GetTransaction())
        {
            int[] beta = transaction.TextSearch("text").BlockAnd("beta").GetDocumentIDs()
                .Select(static id => id.To_Int32_BigEndian()).OrderBy(static id => id).ToArray();
            int[] alphaOrGamma = transaction.TextSearch("text").BlockOr(new[] { "alpha", "gamma" }, null)
                .GetDocumentIDs().Select(static id => id.To_Int32_BigEndian()).OrderBy(static id => id).ToArray();
            Ensure(beta.SequenceEqual(new[] { 1 }),
                "TextSearch beta result mismatch: " + String.Join(",", beta));
            Ensure(alphaOrGamma.SequenceEqual(new[] { 1 }) || alphaOrGamma.SequenceEqual(new[] { 1, 3 }),
                "TextSearch OR result mismatch: " + String.Join(",", alphaOrGamma));
            checksum.Add(beta.Length);
            checksum.Add(alphaOrGamma.Length);
            foreach (int id in beta)
                checksum.Add(id);
            foreach (int id in alphaOrGamma)
                checksum.Add(id);
            byte[][] betaKeys = beta.Select(static id => id.To_4_bytes_array_BigEndian()).ToArray();
            Dictionary<byte[], HashSet<string>> searchables = transaction.TextGetDocumentsSearchables("text",
                new HashSet<byte[]>(betaKeys, ByteArrayComparer.Instance));
            checksum.Add(searchables.Count);
        }

        engine.Resources.Insert("resource-a", new byte[] { 1, 2 });
        engine.Resources.Insert<byte[]>("resource-null", null);
        engine.Resources.Insert(new Dictionary<string, byte[]>
        {
            ["resource-b"] = new byte[] { 3 },
            ["resource-c"] = Array.Empty<byte>(),
        });
        checksum.Add(engine.Resources.Select<byte[]>("resource-a"));
        checksum.Add(engine.Resources.Select<byte[]>(new[] { "resource-b", "resource-c" }).Count);
        checksum.Add(engine.Resources.SelectStartsWith<byte[]>("resource-").Count());
        engine.Resources.Remove(new[] { "resource-b", "resource-c" });
        engine.Resources.Remove("resource-a");
        return new AuditOutcome(13, checksum.Value);
    }

    private static AuditOutcome RunDataTypesAndUtils(string root)
    {
        _ = root;
        var checksum = new AuditChecksum();
        int intValue = -1234567;
        long longValue = -9_876_543_210L;
        decimal decimalValue = 123456.789m;
        DateTime dateValue = new(2026, 8, 21, 12, 34, 56, DateTimeKind.Utc);
        Guid guidValue = new("b5137a44-7228-4f64-a06a-708c24df45cb");
        checksum.Add(DataTypesConvertor.ConvertBack<int>(DataTypesConvertor.ConvertKey(intValue)));
        checksum.Add(DataTypesConvertor.ConvertBack<long>(DataTypesConvertor.ConvertKey(longValue)));
        checksum.Add(DataTypesConvertor.ConvertBack<decimal>(DataTypesConvertor.ConvertKey(decimalValue)).GetHashCode());
        checksum.Add(DataTypesConvertor.ConvertBack<DateTime>(DataTypesConvertor.ConvertKey(dateValue)).Ticks);
        checksum.Add(DataTypesConvertor.ConvertBack<Guid>(DataTypesConvertor.ConvertKey(guidValue)).ToByteArray());
        checksum.Add(DataTypesConvertor.ConvertBack<string>(DataTypesConvertor.ConvertValue("Привет DBreeze")));
        checksum.Add(new DbUTF8("utf8-周").GetBytes());
        checksum.Add(new DbAscii("ascii").GetBytes());
        checksum.Add(new DbUnicode("unicode-ß").GetBytes());
        checksum.Add(123.To_4_bytes_array_BigEndian().To_Int32_BigEndian());
        checksum.Add((-123L).To_8_bytes_array_BigEndian().To_Int64_BigEndian());
        checksum.Add(12.5d.To_9_bytes_array_BigEndian().To_Double_BigEndian().GetHashCode());
        byte[] payload = Encoding.UTF8.GetBytes(new string('x', 4096));
        byte[] compressed = Compression.GZip_Compress(payload);
        Ensure(Compression.GZip_Decompress(compressed).SequenceEqual(payload), "GZip round-trip failed.");
        checksum.Add(compressed.Length);
        checksum.Add(BytesProcessing.Concat(new byte[] { 1, 2 }, new byte[] { 3, 4 }));
        checksum.Add(BytesProcessing.Substring(new byte[] { 1, 2, 3, 4 }, 1, 2));
        return new AuditOutcome(16, checksum.Value);
    }

    private static AuditOutcome RunStorage(string root)
    {
        string tablePath = Path.Combine(root, "storage", "3");
        Directory.CreateDirectory(Path.GetDirectoryName(tablePath));
        var configuration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK };
        var storage = new StorageLayer(tablePath, new TrieSettings(), configuration);
        byte[] first = Enumerable.Range(0, 256).Select(static value => (byte)value).ToArray();
        byte[] read;
        long length;
        try
        {
            byte[] pointer = storage.Table_WriteToTheEnd(first);
            storage.Commit();
            read = storage.Table_Read(false, pointer, first.Length);
            Ensure(read.SequenceEqual(first), "Storage pointer read failed.");
            storage.Table_WriteByOffset(10L, new byte[] { 9, 9, 9 });
            storage.Rollback();
            storage.TransactionalCommit();
            storage.TransactionalCommitIsFinished();
            storage.TransactionalRollback();
            length = storage.Length;
        }
        finally
        {
            storage.Table_Dispose();
            configuration.Dispose();
        }
        var checksum = new AuditChecksum();
        checksum.Add(read);
        checksum.Add(length);
        return new AuditOutcome(9, checksum.Value);
    }

    private static string Sanitize(string value) => value.Replace('.', '-');

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidDataException(message);
    }

    internal readonly record struct AuditOutcome(long Count, long Checksum);

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        internal static readonly ByteArrayComparer Instance = new();
        public bool Equals(byte[] x, byte[] y) => ReferenceEquals(x, y) ||
                                                 x != null && y != null && x.AsSpan().SequenceEqual(y);
        public int GetHashCode(byte[] obj)
        {
            if (obj == null)
                return 0;
            var hash = new HashCode();
            foreach (byte value in obj)
                hash.Add(value);
            return hash.ToHashCode();
        }
    }
}

internal sealed class AuditChecksum
{
    private ulong _value = 14695981039346656037UL;
    internal long Value => unchecked((long)_value);

    internal void Add(byte value) => Mix(value);
    internal void Add(int value) => Add(unchecked((long)value));
    internal void Add(long value)
    {
        unchecked
        {
            ulong data = (ulong)value;
            for (int shift = 0; shift < 64; shift += 8)
                Mix((byte)(data >> shift));
        }
    }
    internal void Add(string value)
    {
        if (value == null)
        {
            Add(-1);
            return;
        }
        foreach (char character in value)
        {
            Mix((byte)character);
            Mix((byte)(character >> 8));
        }
    }
    internal void Add(byte[] value)
    {
        if (value == null)
        {
            Add(-1);
            return;
        }
        Add(value.Length);
        foreach (byte item in value)
            Mix(item);
    }
    private void Mix(byte value)
    {
        _value ^= value;
        _value *= 1099511628211UL;
    }
}
