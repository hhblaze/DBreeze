using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DBreeze;
using DBreeze.DataTypes;
using DBreeze.LianaTrie;
using DBreeze.Objects;
using DBreeze.ReleaseAudit.Protocol;
using DBreeze.Storage;
using DBreeze.Transactions;
using DBreeze.Utils;

namespace DBreeze.ReleaseAudit.Worker
{
    internal static class CompatibilitySuite
    {
        internal static void Run(WorkerOptions options, WorkerReport report)
        {
            if (String.IsNullOrWhiteSpace(options.Root)) throw new ArgumentException("Compatibility action requires --root.");
            var result = new CaseResult { Id = options.Action, Category = "file-protocol", Mode = "process" };
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                string semantic;
                switch (options.Action)
                {
                    case "fixture-create":
                        RefuseExisting(options.Root);
                        Populate(options.Root, false, null);
                        semantic = Validate(options.Root, false);
                        break;
                    case "fixture-verify":
                        semantic = VerifyReadOnly(options.Root, false);
                        break;
                    case "fixture-extend":
                        Validate(options.Root, false);
                        Extend(options.Root);
                        semantic = Validate(options.Root, true);
                        break;
                    case "fixture-verify-extended":
                        semantic = VerifyReadOnly(options.Root, true);
                        break;
                    case "backup-create":
                        semantic = CreateBackup(options.Root);
                        break;
                    case "backup-restore":
                        semantic = RestoreBackup(options.Root);
                        break;
                    case "journal-prepare":
                        semantic = PrepareJournal(options.Root, options.Variant, options.Framework);
                        break;
                    case "journal-recover":
                        semantic = RecoverJournal(options.Root);
                        break;
                    default:
                        throw new ArgumentException("Unknown compatibility action " + options.Action);
                }
                result.SemanticValue = semantic;
                result.Succeeded = true;
                report.Files = Files(options.Root);
            }
            catch (Exception error)
            {
                result.Detail = error.ToString();
                result.Succeeded = false;
            }
            finally
            {
                timer.Stop();
                result.ElapsedMilliseconds = timer.ElapsedMilliseconds;
                report.Cases.Add(result);
            }
        }

        private static void Populate(string database, bool extended, string backup)
        {
            var configuration = new DBreezeConfiguration { DBreezeDataFolderName = database, NotifyAhead_WhenWriteTablePossibleDeadlock = false };
            if (!String.IsNullOrEmpty(backup)) configuration.Backup.BackupFolderName = backup;
            using (var engine = new DBreezeEngine(configuration))
            {
                using (Transaction t = engine.GetTransaction())
                {
                    t.SynchronizeTables("compat-int", "compat-null", "compat-parts", "compat-blocks", "compat-fixed", "compat-master", "compat-dict", "compat-set", "compat-objects", "compat-text", "compat-vf", "compat-vd");
                    for (int i = 0; i < 128; i++) t.Insert("compat-int", i, "base-" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
                    t.Insert<string, byte[]>("compat-null", "null", null);
                    t.Insert("compat-null", "empty", new byte[0]);
                    t.Insert("compat-null", "value", new byte[] { 1, 2, 3 });
                    t.Insert("compat-parts", 1, new byte[24]);
                    byte[] ignoredPointer;
                    bool ignoredUpdated;
                    t.InsertPart("compat-parts", 1, new byte[] { 7, 8, 9 }, 8, out ignoredPointer, out ignoredUpdated);
                    byte[] block = t.InsertDataBlock("compat-blocks", null, Encoding.UTF8.GetBytes("block-base"));
                    byte[] fixedBlock = t.InsertDataBlockWithFixedAddress("compat-fixed", null, Encoding.UTF8.GetBytes("fixed-base"));
                    t.Insert("compat-blocks", 1, block);
                    t.Insert("compat-fixed", 1, fixedBlock);

                    NestedTable nested = t.InsertTable("compat-master", 1, 0);
                    nested.Insert(1, "nested-one");
                    nested.Insert(2, "nested-two");
                    NestedTable child = nested.GetTable(100, 1);
                    child.Insert(1, 101);
                    child.CloseTable();
                    nested.CloseTable();

                    t.InsertDictionary("compat-dict", new Dictionary<int, string> { { 1, "one" }, { 2, "two" } }, true);
                    t.InsertHashSet("compat-set", new HashSet<int> { 3, 4, 5 }, true);
                    long identity = t.ObjectGetNewIdentity<long>("compat-objects");
                    DBreezeObjectInsertResult<byte[]> inserted = t.ObjectInsert("compat-objects", new DBreezeObject<byte[]>
                    {
                        NewEntity = true,
                        Entity = new byte[] { 11, 12, 13 },
                        Indexes = new List<DBreezeIndex>
                        {
                            new DBreezeIndex(1, identity) { PrimaryIndex = true },
                            new DBreezeIndex(2, "compat-object")
                        }
                    });
                    Ensure(inserted.EntityWasInserted, "Compatibility object insert failed.");
                    for (int i = 1; i <= 12; i++)
                        t.TextInsert("compat-text", i.To_4_bytes_array_BigEndian(), i % 2 == 0 ? "alpha even" : "alpha odd", "group-" + i % 3);

                    t.VectorsInsert("compat-vf", new List<(long, float[])>
                    {
                        (1L, new float[] { 1, 0, 0 }), (2L, new float[] { 0, 1, 0 }), (3L, new float[] { 0, 0, 1 })
                    });
                    t.VectorsInsert("compat-vd", new List<(long, double[])>
                    {
                        (11L, new double[] { 1, 0, 0 }), (12L, new double[] { 0, 1, 0 }), (13L, new double[] { 0, 0, 1 })
                    });
                    t.Commit();
                }

                using (Transaction scheme = engine.GetTransaction()) { scheme.Insert("compat-scheme-source", 1, "renamed"); scheme.Insert("compat-scheme-delete", 1, 1); scheme.Commit(); }
                engine.Scheme.RenameTable("compat-scheme-source", "compat-scheme-live");
                engine.Scheme.DeleteTable("compat-scheme-delete");
                engine.Resources.Insert<byte[]>("compat-resource-null", null);
                engine.Resources.Insert("compat-resource-empty", new byte[0]);
                engine.Resources.Insert("compat-resource-value", new byte[] { 21, 22, 23 });
            }
            if (extended) Extend(database);
        }

        private static void Extend(string database)
        {
            using (var engine = new DBreezeEngine(database))
            {
                using (Transaction t = engine.GetTransaction())
                {
                    t.SynchronizeTables("compat-int", "compat-state", "compat-blocks", "compat-fixed", "compat-text", "compat-vf", "compat-vd");
                    for (int i = 0; i < 16; i++) t.Insert("compat-int", i, "extended-" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
                    for (int i = 128; i < 144; i++) t.Insert("compat-int", i, "added-" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
                    t.Insert("compat-state", "extended", true);
                    byte[] pointer = t.Select<int, byte[]>("compat-blocks", 1).Value;
                    pointer = t.InsertDataBlock("compat-blocks", pointer, Encoding.UTF8.GetBytes("block-extended"));
                    t.Insert("compat-blocks", 1, pointer);
                    byte[] fixedPointer = t.Select<int, byte[]>("compat-fixed", 1).Value;
                    t.InsertDataBlockWithFixedAddress("compat-fixed", fixedPointer, Encoding.UTF8.GetBytes("fixed-next"));
                    t.TextInsert("compat-text", 99.To_4_bytes_array_BigEndian(), "alpha added", "extended");
                    t.VectorsInsert("compat-vf", new List<(long, float[])> { (99L, new float[] { 0.5f, 0.5f, 0 }) });
                    t.VectorsInsert("compat-vd", new List<(long, double[])> { (199L, new double[] { 0.5, 0.5, 0 }) });
                    t.Commit();
                }
                engine.Resources.Insert("compat-resource-added", new byte[] { 31, 32 });
                engine.Resources.Insert("compat-resource-value", new byte[] { 41, 42 });
                engine.Scheme.RenameTable("compat-scheme-live", "compat-scheme-extended");
            }
        }

        private static string Validate(string database, bool extended)
        {
            var checksum = new Checksum();
            long rows = 0;
            using (var engine = new DBreezeEngine(database))
            {
                using (Transaction t = engine.GetTransaction())
                {
                    int expectedCount = extended ? 144 : 128;
                    Ensure(t.Count("compat-int") == (ulong)expectedCount, "compat-int count mismatch.");
                    for (int i = 0; i < expectedCount; i++)
                    {
                        string expected = extended && i < 16 ? "extended-" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) :
                            i < 128 ? "base-" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) : "added-" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
                        string actual = t.Select<int, string>("compat-int", i).Value;
                        Ensure(actual == expected, "compat-int mismatch at " + i);
                        checksum.Add(actual); rows++;
                    }
                    Row<string, byte[]> nullRow = t.Select<string, byte[]>("compat-null", "null");
                    Ensure(nullRow.Exists && nullRow.Value == null, "Null scalar contract mismatch.");
                    Ensure(t.Select<string, byte[]>("compat-null", "empty").Value.Length == 0, "Empty scalar contract mismatch.");
                    checksum.Add(t.Select<string, byte[]>("compat-null", "value").Value); rows += 3;
                    byte[] part = t.Select<int, byte[]>("compat-parts", 1).Value;
                    Ensure(part[8] == 7 && part[10] == 9, "Partial block mismatch."); checksum.Add(part); rows++;
                    byte[] blockPointer = t.Select<int, byte[]>("compat-blocks", 1).Value;
                    string block = Encoding.UTF8.GetString(t.SelectDataBlock("compat-blocks", blockPointer));
                    Ensure(block == (extended ? "block-extended" : "block-base"), "Data block mismatch."); checksum.Add(block); rows++;
                    byte[] fixedPointer = t.Select<int, byte[]>("compat-fixed", 1).Value;
                    string fixedValue = Encoding.UTF8.GetString(t.SelectDataBlockWithFixedAddress<byte[]>("compat-fixed", fixedPointer));
                    Ensure(fixedValue == (extended ? "fixed-next" : "fixed-base"), "Fixed block mismatch."); checksum.Add(fixedValue); rows++;
                    NestedTable nested = t.SelectTable<int>("compat-master", 1, 0);
                    checksum.Add(nested.Select<int, string>(1).Value); checksum.Add(nested.Select<int, string>(2).Value); rows += 2;
                    NestedTable child = nested.GetTable(100, 1); checksum.Add(child.Select<int, int>(1).Value); rows++;
                    child.CloseTable(); nested.CloseTable();
                    Dictionary<int, string> dictionary = t.SelectDictionary<int, string>("compat-dict");
                    HashSet<int> set = t.SelectHashSet<int>("compat-set");
                    Ensure(dictionary.Count == 2 && set.Count == 3, "Collection fixture mismatch."); checksum.Add(dictionary.Count); checksum.Add(set.Count); rows += 5;
                    DBreezeObject<byte[]> obj = t.Select<byte[], byte[]>("compat-objects", 1.ToIndex(1L)).ObjectGet<byte[]>();
                    Ensure(obj != null && obj.Entity.SequenceEqual(new byte[] { 11, 12, 13 }), "Object fixture mismatch."); checksum.Add(obj.Entity); rows++;
                    int textCount = t.TextSearch("compat-text").BlockAnd("alpha").GetDocumentIDs().Count();
                    Ensure(textCount == (extended ? 13 : 12), "Text fixture mismatch."); checksum.Add(textCount); rows += textCount;
                    long vf = t.VectorsCount<float[]>("compat-vf");
                    long vd = t.VectorsCount<double[]>("compat-vd");
                    Ensure(vf == (extended ? 4 : 3) && vd == (extended ? 4 : 3), "Vector fixture count mismatch.");
                    Ensure(t.VectorsSearchSimilar("compat-vf", new float[] { 1, 0, 0 }, 1).Any(), "Float vector search failed.");
                    Ensure(t.VectorsSearchSimilar("compat-vd", new double[] { 1, 0, 0 }, 1).Any(), "Double vector search failed.");
                    checksum.Add(vf); checksum.Add(vd); rows += vf + vd;
                    Ensure(t.Select<string, bool>("compat-state", "extended").Exists == extended, "Extended state marker mismatch.");
                }
                string live = extended ? "compat-scheme-extended" : "compat-scheme-live";
                Ensure(engine.Scheme.IfUserTableExists(live), "Scheme fixture missing " + live);
                Ensure(!engine.Scheme.IfUserTableExists("compat-scheme-delete"), "Deleted Scheme fixture reappeared.");
                checksum.Add(engine.Resources.Select<byte[]>("compat-resource-value"));
                Ensure(engine.Resources.Select<byte[]>("compat-resource-null") == null, "Null resource mismatch.");
                Ensure(engine.Resources.Select<byte[]>("compat-resource-empty").Length == 0, "Empty resource mismatch.");
                Ensure((engine.Resources.Select<byte[]>("compat-resource-added") != null) == extended, "Extended resource mismatch.");
                rows += extended ? 4 : 3;
            }
            return "state=" + (extended ? "extended" : "base") + ";rows=" + rows + ";checksum=" + checksum.Value;
        }

        private static string VerifyReadOnly(string database, bool extended)
        {
            List<FileEntry> before = Files(database);
            string semantic = Validate(database, extended);
            List<FileEntry> after = Files(database);
            Ensure(EqualFiles(before, after), "Read-only consumer changed database file length or SHA-256.");
            return semantic + ";readonly-files=" + before.Count;
        }

        private static string CreateBackup(string root)
        {
            RefuseExisting(root);
            Directory.CreateDirectory(root);
            string database = Path.Combine(root, "database");
            string backup = Path.Combine(root, "backup");
            Populate(database, false, backup);
            return Validate(database, false) + ";backup-files=" + Files(backup).Count;
        }

        private static string RestoreBackup(string root)
        {
            string backup = Path.Combine(root, "backup");
            string restored = Path.Combine(root, "restored");
            if (Directory.Exists(restored)) throw new IOException("Backup destination exists: " + restored);
            var restorer = new BackupRestorer { BackupFolder = backup, DataBaseFolder = restored };
            restorer.OnRestore += delegate { };
            restorer.StartRestoration();
            return Validate(restored, false);
        }

        private static string PrepareJournal(string root, string variant, string framework)
        {
            RefuseExisting(root);
            var configuration = new DBreezeConfiguration { DBreezeDataFolderName = root, NotifyAhead_WhenWriteTablePossibleDeadlock = false };
            configuration.AlternativeTablesLocations["journal-b"] = Path.Combine(root, "alternative");
            using (var engine = new DBreezeEngine(configuration))
            {
                using (Transaction t = engine.GetTransaction()) { t.Insert("journal-a", 1, "a"); t.Commit(); }
                using (Transaction t = engine.GetTransaction()) { t.Insert("journal-b", 2, "b"); t.Commit(); }
                FieldInfo field = typeof(DBreezeEngine).GetField("_transactionsJournal", BindingFlags.Instance | BindingFlags.NonPublic);
                Ensure(field != null, "Transactions journal field not found.");
                var journal = (TransactionsJournal)field.GetValue(engine);
                ulong number = journal.GetTransactionNumber();
                journal.AddTableForTransaction(number, new FailingJournalTable("journal-a"));
                journal.AddTableForTransaction(number, new FailingJournalTable("journal-b"));
                try { journal.FinishTransaction(number); throw new InvalidDataException("Injected journal failure did not fail."); }
                catch (InvalidOperationException) { }
            }
            byte[][] payloads = ReadJournal(root, configuration);
            Ensure(payloads.Length == 1, "Pending journal marker was not persisted.");
            string payload = Encoding.UTF8.GetString(payloads[0]);
            string payloadFormat = ClassifyJournalPayload(payload);
            if (String.Equals(variant, "current", StringComparison.Ordinal))
            {
                Ensure(String.Equals(payload, CanonicalJournalPayload(), StringComparison.Ordinal),
                    "Current journal writer did not persist the canonical compatibility envelope.");
            }
            else if (String.Equals(framework, "net8", StringComparison.Ordinal))
            {
                Ensure(String.Equals(payload, LegacyCompactJournalPayload(), StringComparison.Ordinal),
                    "Baseline Net8 journal fixture changed.");
            }
            else
            {
                Ensure(String.Equals(payload, LegacyFrameworkJournalPayload(), StringComparison.Ordinal),
                    "Baseline Net472 journal fixture changed.");
            }
            return "pending=1;payload=" + payloadFormat + ";bytes=" + payloads[0].Length.ToString(CultureInfo.InvariantCulture);
        }

        private static string ClassifyJournalPayload(string payload)
        {
            if (String.Equals(payload, CanonicalJournalPayload(), StringComparison.Ordinal)) return "canonical-rooted";
            if (String.Equals(payload, LegacyCompactJournalPayload(), StringComparison.Ordinal)) return "legacy-compact";
            if (String.Equals(payload, LegacyFrameworkJournalPayload(), StringComparison.Ordinal)) return "legacy-framework";
            throw new InvalidDataException("Unknown transaction journal payload fixture.");
        }

        private static string CanonicalJournalPayload()
        {
            return "<ArrayOfString>\n<string>journal-a</string>\n<string>journal-b</string>\n</ArrayOfString>";
        }

        private static string LegacyCompactJournalPayload()
        {
            return "<string>journal-a</string>\n<string>journal-b</string>\n";
        }

        private static string LegacyFrameworkJournalPayload()
        {
            return "<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n" +
                "<ArrayOfString xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" " +
                "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\r\n" +
                "  <string>journal-a</string>\r\n" +
                "  <string>journal-b</string>\r\n" +
                "</ArrayOfString>";
        }

        private static string RecoverJournal(string root)
        {
            var configuration = new DBreezeConfiguration { DBreezeDataFolderName = root, NotifyAhead_WhenWriteTablePossibleDeadlock = false };
            configuration.AlternativeTablesLocations["journal-b"] = Path.Combine(root, "alternative");
            using (var engine = new DBreezeEngine(configuration))
            using (Transaction t = engine.GetTransaction())
            {
                Ensure(t.Select<int, string>("journal-a", 1).Value == "a", "journal-a recovery mismatch.");
                Ensure(t.Select<int, string>("journal-b", 2).Value == "b", "journal-b recovery mismatch.");
            }
            Ensure(ReadJournal(root, configuration).Length == 0, "Pending journal marker was not cleared.");
            return "pending=0;values=a,b";
        }

        private static byte[][] ReadJournal(string root, DBreezeConfiguration configuration)
        {
            var storage = new StorageLayer(Path.Combine(root, "_DBreezeTranJrnl"), new TrieSettings(), configuration);
            using (var journal = new LTrie(storage) { TableName = "DBreeze.TranJournal" })
                return journal.IterateForward(true, false).Select(delegate(LTrieRow row) { return row.GetFullValue(true); }).ToArray();
        }

        internal static List<FileEntry> Files(string root)
        {
            var result = new List<FileEntry>();
            if (!Directory.Exists(root)) return result;
            foreach (string path in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(delegate(string value) { return value; }, StringComparer.OrdinalIgnoreCase))
            {
                string relative = path.Substring(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/');
                result.Add(new FileEntry { RelativePath = relative, Length = new FileInfo(path).Length, Sha256 = Sha256(path) });
            }
            return result;
        }

        private static bool EqualFiles(IList<FileEntry> left, IList<FileEntry> right)
        {
            if (left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
                if (left[i].RelativePath != right[i].RelativePath || left[i].Length != right[i].Length || left[i].Sha256 != right[i].Sha256) return false;
            return true;
        }

        private static string Sha256(string path)
        {
            using (var algorithm = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", String.Empty).ToLowerInvariant();
        }

        private static void RefuseExisting(string root)
        {
            if (Directory.Exists(root)) throw new IOException("Compatibility path already exists: " + root);
            string parent = Path.GetDirectoryName(Path.GetFullPath(root));
            if (String.IsNullOrEmpty(parent)) throw new InvalidOperationException("Compatibility path needs a parent.");
            Directory.CreateDirectory(parent);
        }

        private static void Ensure(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }

        private sealed class FailingJournalTable : ITransactable
        {
            internal FailingJournalTable(string tableName) { TableName = tableName; }
            public string TableName { get; set; }
            public void ITRCommitFinished() { throw new InvalidOperationException("Simulated process failure."); }
            public void ITRCommit() { }
            public void ITRRollBack() { }
            public void ModificationThreadId(int transactionThreadId) { }
            public void SingleCommit() { }
            public void SingleRollback() { }
            public void TransactionIsFinished(int transactionThreadId) { }
        }

        private sealed class Checksum
        {
            private ulong _value = 14695981039346656037UL;
            internal string Value { get { return _value.ToString("x16", System.Globalization.CultureInfo.InvariantCulture); } }
            internal void Add(int value) { Add((long)value); }
            internal void Add(long value) { unchecked { for (int shift = 0; shift < 64; shift += 8) Mix((byte)((ulong)value >> shift)); } }
            internal void Add(string value) { if (value == null) { Add(-1); return; } foreach (char c in value) { Mix((byte)c); Mix((byte)(c >> 8)); } }
            internal void Add(byte[] value) { if (value == null) { Add(-1); return; } Add(value.Length); foreach (byte b in value) Mix(b); }
            private void Mix(byte value) { _value ^= value; _value *= 1099511628211UL; }
        }
    }
}
