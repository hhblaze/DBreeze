using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DBreeze;
using DBreeze.DataTypes;
using DBreeze.Objects;
using DBreeze.ReleaseAudit.Protocol;
using DBreeze.TextSearch;
using DBreeze.Transactions;
using DBreeze.Utils;

namespace DBreeze.ReleaseAudit.Worker
{
    internal static class CorrectnessSuite
    {
        private const string TransactionType = "DBreeze.Transactions.Transaction";
        private const string SchemeType = "DBreeze.Scheme";

        internal static void Run(WorkerOptions options, WorkerReport report)
        {
            RequireRoot(options.Root);
            Directory.CreateDirectory(options.Root);
            report.AssemblyApi = ApiCatalog.CreateAssemblyManifest();
            report.FocusedApi = ApiCatalog.CreateFocusedManifest();
            var coverage = new CoverageRegistry();

            RunCase(report, "all-public-methods", "correctness", "single", delegate
            {
                return RunAllMethods(Path.Combine(options.Root, "single"), coverage, "single");
            });

            RunCase(report, "all-public-methods", "correctness", "parallel", delegate
            {
                string[] values = new string[2];
                var start = new ManualResetEventSlim(false);
                Task[] tasks = new Task[2];
                for (int i = 0; i < tasks.Length; i++)
                {
                    int worker = i;
                    tasks[i] = Task.Factory.StartNew(delegate
                    {
                        start.Wait();
                        values[worker] = RunAllMethods(Path.Combine(options.Root, "parallel-" + worker), coverage, "parallel");
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }
                start.Set();
                if (!Task.WaitAll(tasks, TimeSpan.FromSeconds(150)))
                    throw new TimeoutException("Parallel 85-method coverage timed out.");
                return String.Join("|", values);
            });

            RunCase(report, "shared-engine-workloads", "concurrency", "parallel", delegate
            {
                return RunSharedEngine(Path.Combine(options.Root, "shared"));
            });
            RunCase(report, "transaction-owner-thread-negative", "concurrency", "parallel", delegate
            {
                return RunOwnerThreadNegative(Path.Combine(options.Root, "owner-negative"));
            });
            RunCase(report, "text-remove-known-delta", "correctness", "single", delegate
            {
                return RunTextRemoveProbe(Path.Combine(options.Root, "text-remove-probe"));
            });

            report.Coverage = coverage.Snapshot();
            int missing = report.Coverage.Count(delegate(CoverageEntry item) { return item.Attempts == 0; });
            int failed = report.Coverage.Count(delegate(CoverageEntry item) { return item.Attempts != 0 && item.Successes == 0; });
            report.Cases.Add(new CaseResult
            {
                Id = "coverage-85x2",
                Category = "coverage",
                Mode = "single+parallel",
                Succeeded = report.Coverage.Count == 170 && missing == 0,
                SemanticValue = report.Coverage.Count + "/170;missing=" + missing + ";executed-but-failed=" + failed,
                Detail = "Every focused method must be invoked at least once in each mode; failed invocations remain correctness failures."
            });
        }

        private static string RunAllMethods(string root, CoverageRegistry coverage, string mode)
        {
            Directory.CreateDirectory(root);
            var checksum = new StableChecksum();
            string database = Path.Combine(root, "db");
            var engine = new DBreezeEngine(database);
            try
            {
                RunCrud(engine, coverage, mode, checksum);
                RunTraversal(engine, coverage, mode, checksum);
                RunCollectionsObjectsNested(engine, coverage, mode, checksum);
                string textSemantic = RunText(engine, coverage, mode, checksum);
                RunRestore(root, engine, coverage, mode, checksum);
                RunScheme(engine, coverage, mode, checksum);

                Transaction disposable = engine.GetTransaction();
                coverage.Execute(mode, M(coverage, TransactionType, "Dispose", 0, null), "explicit Transaction.Dispose", disposable.Dispose);
                Exception vectorFailure = null;
                try { RunVectors(engine, coverage, mode, checksum); }
                catch (Exception error) { vectorFailure = error; }
                coverage.Execute(mode, M(coverage, SchemeType, "Dispose", 0, null), "explicit Scheme.Dispose", engine.Scheme.Dispose);
                if (vectorFailure != null) throw vectorFailure;
                return checksum.Value + ";text-remove=" + textSemantic;
            }
            finally
            {
                engine.Dispose();
            }
        }

        private static void RunCrud(DBreezeEngine engine, CoverageRegistry c, string mode, StableChecksum sum)
        {
            using (Transaction syncParams = engine.GetTransaction())
            {
                c.Execute(mode, M(c, TransactionType, "SynchronizeTables", 1, "System.String[]"),
                    "write reservation via params", delegate { syncParams.SynchronizeTables("technical", "params-only"); });
            }
            Transaction t = engine.GetTransaction();
            try
            {
                c.Execute(mode, M(c, TransactionType, "SynchronizeTables", 1, "System.Collections.Generic.IList"),
                    "CRUD write reservation via IList", delegate { t.SynchronizeTables(new List<string> { "crud", "parts", "blocks", "fixed", "rks", "nested", "objects", "technical", "rollback", "remove-all" }); });
                byte[] ptr1 = null;
                byte[] ptr2 = null;
                bool updated = false;
                c.Execute(mode, M(c, TransactionType, "Insert", 3, null), "Insert<TKey,TValue>(3)", delegate { t.Insert("crud", 1, "one"); });
                c.Execute(mode, M(c, TransactionType, "Insert", 4, null), "Insert<TKey,TValue>(4)", delegate { t.Insert("crud", 2, "two", out ptr1); });
                c.Execute(mode, M(c, TransactionType, "Insert", 5, null), "Insert<TKey,TValue>(5)", delegate { t.Insert("crud", 3, "three", out ptr2, out updated); });
                Ensure(!updated, "New row was reported as updated.");
                c.Execute(mode, M(c, TransactionType, "Insert", 6, null), "Insert<TKey,TValue>(6)", delegate
                {
                    byte[] ignored;
                    bool existed;
                    t.Insert("crud", 3, "ignored", out ignored, out existed, true);
                    Ensure(existed, "dontUpdateIfExists did not report an existing row.");
                });

                t.Insert("parts", 1, new byte[32]);
                c.Execute(mode, M(c, TransactionType, "InsertPart", 4, null), "InsertPart<TKey,TValue>(4)",
                    delegate { t.InsertPart("parts", 1, new byte[] { 1, 2 }, 2); });
                c.Execute(mode, M(c, TransactionType, "InsertPart", 5, null), "InsertPart<TKey,TValue>(5)", delegate
                {
                    byte[] pointer;
                    t.InsertPart("parts", 1, new byte[] { 3, 4 }, 8, out pointer);
                });
                c.Execute(mode, M(c, TransactionType, "InsertPart", 6, null), "InsertPart<TKey,TValue>(6)", delegate
                {
                    byte[] pointer;
                    bool wasUpdated;
                    t.InsertPart("parts", 1, new byte[] { 5, 6 }, 16, out pointer, out wasUpdated);
                    Ensure(wasUpdated, "InsertPart did not update an existing row.");
                });

                byte[] blockPointer = c.Execute(mode, M(c, TransactionType, "InsertDataBlock", 3, null), "InsertDataBlock",
                    delegate { return t.InsertDataBlock("blocks", null, Encoding.UTF8.GetBytes("ordinary-block")); });
                byte[] fixedPointer = c.Execute(mode, M(c, TransactionType, "InsertDataBlockWithFixedAddress", 3, null),
                    "InsertDataBlockWithFixedAddress", delegate { return t.InsertDataBlockWithFixedAddress("fixed", null, "fixed-block"); });
                t.Insert("blocks", 1, blockPointer);
                t.Insert("fixed", 1, fixedPointer);

                c.Execute(mode, M(c, TransactionType, "InsertRandomKeySorter", 3, null), "InsertRandomKeySorter",
                    delegate { t.InsertRandomKeySorter("rks", 1, 11); });
                t.RandomKeySorter.Insert("rks", 2, 22);
                c.Execute(mode, M(c, TransactionType, "RemoveRandomKeySorter", 2, null), "RemoveRandomKeySorter",
                    delegate { t.RemoveRandomKeySorter("rks", 2); });

                c.Execute(mode, M(c, TransactionType, "Technical_SetTable_OverwriteIsNotAllowed", 1, null),
                    "Technical overwrite contract", delegate { t.Technical_SetTable_OverwriteIsNotAllowed("technical"); });
                t.Insert("technical", 1, 1);
                c.Execute(mode, M(c, TransactionType, "Commit", 0, null), "initial CRUD commit", t.Commit);

                ulong count = c.Execute(mode, M(c, TransactionType, "Count", 1, null), "Count", delegate { return t.Count("crud"); });
                Ensure(count == 3, "CRUD count mismatch.");
                Row<int, string> min = c.Execute(mode, M(c, TransactionType, "Min", 1, null), "Min<TKey,TValue>", delegate { return t.Min<int, string>("crud"); });
                Row<int, string> max = c.Execute(mode, M(c, TransactionType, "Max", 1, null), "Max<TKey,TValue>", delegate { return t.Max<int, string>("crud"); });
                Row<int, string> selected = c.Execute(mode, M(c, TransactionType, "Select", 3, null), "Select<TKey,TValue>", delegate { return t.Select<int, string>("crud", 2, false); });
                Row<int, string> direct = c.Execute(mode, M(c, TransactionType, "SelectDirect", 2, null), "SelectDirect<TKey,TValue>", delegate { return t.SelectDirect<int, string>("crud", ptr1); });
                sum.Add(min.Key); sum.Add(max.Key); sum.Add(selected.Value); sum.Add(direct.Value);

                byte[] ordinary = c.Execute(mode, M(c, TransactionType, "SelectDataBlock", 2, null), "SelectDataBlock",
                    delegate { return t.SelectDataBlock("blocks", blockPointer); });
                string fixedValue = c.Execute(mode, M(c, TransactionType, "SelectDataBlockWithFixedAddress", 2, null),
                    "SelectDataBlockWithFixedAddress<TValue>", delegate { return t.SelectDataBlockWithFixedAddress<string>("fixed", fixedPointer); });
                sum.Add(ordinary); sum.Add(fixedValue);

                c.Execute(mode, M(c, TransactionType, "ChangeKey", 3, null), "ChangeKey<TKey>(3)", delegate { t.ChangeKey("crud", 1, 10); });
                c.Execute(mode, M(c, TransactionType, "ChangeKey", 4, null), "ChangeKey<TKey>(4)", delegate
                {
                    byte[] pointer;
                    t.ChangeKey("crud", 2, 20, out pointer);
                });
                c.Execute(mode, M(c, TransactionType, "ChangeKey", 5, null), "ChangeKey<TKey>(5)", delegate
                {
                    byte[] pointer;
                    bool changed;
                    t.ChangeKey("crud", 3, 30, out pointer, out changed);
                    Ensure(changed, "ChangeKey did not report success.");
                });

                c.Execute(mode, M(c, TransactionType, "RemoveKey", 2, null), "RemoveKey<TKey>(2)", delegate { t.RemoveKey("crud", 10); });
                c.Execute(mode, M(c, TransactionType, "RemoveKey", 3, null), "RemoveKey<TKey>(3)", delegate
                {
                    bool removed;
                    t.RemoveKey("crud", 20, out removed);
                    Ensure(removed, "RemoveKey(3) failed.");
                });
                c.Execute(mode, M(c, TransactionType, "RemoveKey", 4, null), "RemoveKey<TKey>(4)", delegate
                {
                    bool removed;
                    byte[] deleted;
                    t.RemoveKey("crud", 30, out removed, out deleted);
                    Ensure(removed && deleted != null, "RemoveKey(4) failed.");
                });
                t.Commit();

                t.Insert("rollback", 1, 1);
                c.Execute(mode, M(c, TransactionType, "Rollback", 0, null), "Rollback", t.Rollback);
                Ensure(!t.Select<int, int>("rollback", 1).Exists, "Rollback leaked data.");
                t.Insert("remove-all", 1, 1);
                t.Commit();
                c.Execute(mode, M(c, TransactionType, "RemoveAllKeys", 2, null), "RemoveAllKeys", delegate { t.RemoveAllKeys("remove-all", true); });
                Ensure(t.Count("remove-all") == 0, "RemoveAllKeys failed.");
            }
            finally { t.Dispose(); }
        }

        private static void RunTraversal(DBreezeEngine engine, CoverageRegistry c, string mode, StableChecksum sum)
        {
            using (Transaction seed = engine.GetTransaction())
            {
                seed.SynchronizeTables("numbers", "numbers2", "prefix");
                for (int i = 0; i < 64; i++)
                {
                    seed.Insert("numbers", i, i * 3);
                    seed.Insert("numbers2", i, i * 5);
                    seed.Insert("prefix", new byte[] { (byte)(i / 8), (byte)i }, i);
                }
                seed.Commit();
            }
            using (Transaction t = engine.GetTransaction())
            {
                Func<IEnumerable<Row<int, int>>, int> consume = delegate(IEnumerable<Row<int, int>> rows)
                {
                    int count = 0;
                    foreach (Row<int, int> row in rows) { sum.Add(row.Key); sum.Add(row.Value); count++; }
                    return count;
                };
                consume(c.Execute(mode, M(c, TransactionType, "SelectForward", 2, null), "SelectForward", delegate { return t.SelectForward<int, int>("numbers", false).Take(5); }));
                consume(c.Execute(mode, M(c, TransactionType, "SelectBackward", 2, null), "SelectBackward", delegate { return t.SelectBackward<int, int>("numbers", true).Take(5); }));
                consume(c.Execute(mode, M(c, TransactionType, "SelectForwardStartFrom", 4, null), "SelectForwardStartFrom", delegate { return t.SelectForwardStartFrom<int, int>("numbers", 20, true, false).Take(5); }));
                consume(c.Execute(mode, M(c, TransactionType, "SelectBackwardStartFrom", 4, null), "SelectBackwardStartFrom", delegate { return t.SelectBackwardStartFrom<int, int>("numbers", 20, true, false).Take(5); }));
                consume(c.Execute(mode, M(c, TransactionType, "SelectForwardFromTo", 6, null), "SelectForwardFromTo(6)", delegate { return t.SelectForwardFromTo<int, int>("numbers", 10, true, 20, true, false); }));
                consume(c.Execute(mode, M(c, TransactionType, "SelectForwardFromTo", 7, null), "SelectForwardFromTo(7)", delegate { return t.SelectForwardFromTo<int, int>("numbers", 10, true, 20, true, 2, false); }));
                consume(c.Execute(mode, M(c, TransactionType, "SelectBackwardFromTo", 6, null), "SelectBackwardFromTo(6)", delegate { return t.SelectBackwardFromTo<int, int>("numbers", 20, true, 10, true, false); }));
                consume(c.Execute(mode, M(c, TransactionType, "SelectBackwardFromTo", 7, null), "SelectBackwardFromTo(7)", delegate { return t.SelectBackwardFromTo<int, int>("numbers", 20, true, 10, true, 2, false); }));
                consume(c.Execute(mode, M(c, TransactionType, "SelectForwardSkip", 3, null), "SelectForwardSkip", delegate { return t.SelectForwardSkip<int, int>("numbers", 4, false).Take(5); }));
                consume(c.Execute(mode, M(c, TransactionType, "SelectBackwardSkip", 3, null), "SelectBackwardSkip", delegate { return t.SelectBackwardSkip<int, int>("numbers", 4, false).Take(5); }));
                consume(c.Execute(mode, M(c, TransactionType, "SelectForwardSkipFrom", 4, null), "SelectForwardSkipFrom", delegate { return t.SelectForwardSkipFrom<int, int>("numbers", 12, 4, false).Take(5); }));
                consume(c.Execute(mode, M(c, TransactionType, "SelectBackwardSkipFrom", 4, null), "SelectBackwardSkipFrom", delegate { return t.SelectBackwardSkipFrom<int, int>("numbers", 40, 4, false).Take(5); }));

                byte[] prefix = new byte[] { 3 };
                sum.Add(c.Execute(mode, M(c, TransactionType, "SelectForwardStartsWith", 3, null), "SelectForwardStartsWith", delegate { return t.SelectForwardStartsWith<byte[], int>("prefix", prefix, false).Count(); }));
                sum.Add(c.Execute(mode, M(c, TransactionType, "SelectBackwardStartsWith", 3, null), "SelectBackwardStartsWith", delegate { return t.SelectBackwardStartsWith<byte[], int>("prefix", prefix, false).Count(); }));
                sum.Add(c.Execute(mode, M(c, TransactionType, "SelectForwardStartsWithClosestToPrefix", 3, null), "SelectForwardStartsWithClosestToPrefix", delegate { return t.SelectForwardStartsWithClosestToPrefix<byte[], int>("prefix", new byte[] { 3, 26 }, false).Take(3).Count(); }));
                sum.Add(c.Execute(mode, M(c, TransactionType, "SelectBackwardStartsWithClosestToPrefix", 3, null), "SelectBackwardStartsWithClosestToPrefix", delegate { return t.SelectBackwardStartsWithClosestToPrefix<byte[], int>("prefix", new byte[] { 3, 26 }, false).Take(3).Count(); }));
                var tables = new HashSet<string>(new[] { "numbers", "numbers2" }, StringComparer.Ordinal);
                sum.Add(c.Execute(mode, M(c, TransactionType, "Multi_SelectForwardFromTo", 6, null), "Multi_SelectForwardFromTo", delegate { return t.Multi_SelectForwardFromTo<int, int>(tables, 5, true, 8, true, false).Count(); }));
                sum.Add(c.Execute(mode, M(c, TransactionType, "Multi_SelectBackwardFromTo", 6, null), "Multi_SelectBackwardFromTo", delegate { return t.Multi_SelectBackwardFromTo<int, int>(tables, 8, true, 5, true, false).Count(); }));
            }
        }

        private static void RunCollectionsObjectsNested(DBreezeEngine engine, CoverageRegistry c, string mode, StableChecksum sum)
        {
            byte[] objectPointer = null;
            using (Transaction t = engine.GetTransaction())
            {
                t.SynchronizeTables("dictionary", "nested-dictionary", "set", "nested-set", "nested", "objects");
                c.Execute(mode, M(c, TransactionType, "InsertDictionary", 3, null), "InsertDictionary<TKey,TValue>", delegate
                { t.InsertDictionary("dictionary", new Dictionary<int, string> { { 1, "one" }, { 2, "two" } }, true); });
                c.Execute(mode, M(c, TransactionType, "InsertDictionary", 5, null), "nested InsertDictionary", delegate
                { t.InsertDictionary("nested-dictionary", 7, new Dictionary<int, string> { { 8, "eight" } }, 0, true); });
                c.Execute(mode, M(c, TransactionType, "InsertHashSet", 3, null), "InsertHashSet<TValue>", delegate
                { t.InsertHashSet("set", new HashSet<int> { 3, 4 }, true); });
                c.Execute(mode, M(c, TransactionType, "InsertHashSet", 5, null), "nested InsertHashSet", delegate
                { t.InsertHashSet("nested-set", 7, new HashSet<int> { 10, 11 }, 0, true); });

                NestedTable nested = c.Execute(mode, M(c, TransactionType, "InsertTable", 3, null), "InsertTable<TKey>",
                    delegate { return t.InsertTable("nested", 1, 0); });
                nested.Insert(1, "inside");
                nested.CloseTable();

                long identity = c.Execute(mode, M(c, TransactionType, "ObjectGetNewIdentity", 3, null), "ObjectGetNewIdentity<TIdentity>",
                    delegate { return t.ObjectGetNewIdentity<long>("objects", null, 1); });
                var value = new DBreezeObject<byte[]>
                {
                    NewEntity = true,
                    Entity = new byte[] { 7, 8, 9 },
                    IncludeOldEntityIntoResult = true,
                    Indexes = new List<DBreezeIndex>
                    {
                        new DBreezeIndex(1, identity) { PrimaryIndex = true },
                        new DBreezeIndex(2, "alpha")
                    }
                };
                DBreezeObjectInsertResult<byte[]> inserted = c.Execute(mode, M(c, TransactionType, "ObjectInsert", 3, null), "ObjectInsert<T>",
                    delegate { return t.ObjectInsert("objects", value, false); });
                Ensure(inserted.EntityWasInserted, "ObjectInsert failed.");
                objectPointer = inserted.PtrToObject;
                t.Commit();
            }

            using (Transaction t = engine.GetTransaction())
            {
                Dictionary<int, string> dictionary = c.Execute(mode, M(c, TransactionType, "SelectDictionary", 1, null), "SelectDictionary<TKey,TValue>",
                    delegate { return t.SelectDictionary<int, string>("dictionary"); });
                Dictionary<int, string> nestedDictionary = c.Execute(mode, M(c, TransactionType, "SelectDictionary", 3, null), "nested SelectDictionary", delegate
                { return t.SelectDictionary<int, int, string>("nested-dictionary", 7, 0); });
                HashSet<int> set = c.Execute(mode, M(c, TransactionType, "SelectHashSet", 1, null), "SelectHashSet<TValue>",
                    delegate { return t.SelectHashSet<int>("set"); });
                HashSet<int> nestedSet = c.Execute(mode, M(c, TransactionType, "SelectHashSet", 3, null), "nested SelectHashSet", delegate
                { return t.SelectHashSet<int, int>("nested-set", 7, 0); });
                sum.Add(dictionary.Count); sum.Add(nestedDictionary.Count); sum.Add(set.Count); sum.Add(nestedSet.Count);

                NestedTable nested = c.Execute(mode, M(c, TransactionType, "SelectTable", 3, null), "SelectTable<TKey>",
                    delegate { return t.SelectTable<int>("nested", 1, 0); });
                sum.Add(nested.Select<int, string>(1).Value);
                nested.CloseTable();

                DBreezeObject<byte[]> entity = c.Execute(mode, M(c, TransactionType, "ObjectGetByFixedAddress", 2, null),
                    "ObjectGetByFixedAddress<T>", delegate { return t.ObjectGetByFixedAddress<byte[]>("objects", objectPointer); });
                Ensure(entity != null && entity.Entity != null, "Object fixed-address lookup failed.");
                sum.Add(entity.Entity);
                c.Execute(mode, M(c, TransactionType, "ObjectRemove", 3, null), "ObjectRemove", delegate
                { t.ObjectRemove("objects", 1.ToIndex(1L), false); });
                t.Commit();
            }
        }

        private static string RunText(DBreezeEngine engine, CoverageRegistry c, string mode, StableChecksum sum)
        {
            using (Transaction t = engine.GetTransaction())
            {
                byte[] one = 1.To_4_bytes_array_BigEndian();
                byte[] two = 2.To_4_bytes_array_BigEndian();
                c.Execute(mode, M(c, TransactionType, "TextInsert", 6, null), "TextInsert", delegate { t.TextInsert("text", one, "alpha beta", "group-a", false, 3); });
                t.TextInsert("text", two, "beta gamma", "group-b", false, 3);
                c.Execute(mode, M(c, TransactionType, "TextAppend", 7, null), "TextAppend", delegate { t.TextAppend("text", two, "delta", "group-b", false, 3, false); });
                c.Execute(mode, M(c, TransactionType, "TextRemove", 5, null), "TextRemove", delegate { t.TextRemove("text", two, "delta", false, 3); });
                c.Execute(mode, M(c, TransactionType, "TextRemoveAll", 3, null), "TextRemoveAll", delegate { t.TextRemoveAll("text", one, false); });
                t.Commit();
            }
            string semantic;
            using (Transaction t = engine.GetTransaction())
            {
                int[] beta = c.Execute(mode, M(c, TransactionType, "TextSearch", 1, null), "TextSearch", delegate
                {
                    return t.TextSearch("text").BlockAnd("beta").GetDocumentIDs().Select(delegate(byte[] id)
                    { return id.To_Int32_BigEndian(); }).OrderBy(delegate(int id) { return id; }).ToArray();
                });
                var keys = new HashSet<byte[]>(new[] { 2.To_4_bytes_array_BigEndian() }, ByteArrayComparer.Instance);
                Dictionary<byte[], HashSet<string>> searchables = c.Execute(mode,
                    M(c, TransactionType, "TextGetDocumentsSearchables", 2, null), "TextGetDocumentsSearchables",
                    delegate { return t.TextGetDocumentsSearchables("text", keys); });
                semantic = "beta=" + String.Join(",", beta) + ";searchables=" + searchables.Count;
                sum.Add(semantic);
            }

            string migrationRoot = Path.Combine(Path.GetDirectoryName(engine.Scheme.GetTablePathFromTableName("text")), "audit-migration-db");
            byte[] key = Enumerable.Range(0, 32).Select(delegate(int value) { return (byte)value; }).ToArray();
            byte[] iv = Enumerable.Range(0, 16).Select(delegate(int value) { return (byte)(15 - value); }).ToArray();
            var configuration = new DBreezeConfiguration { DBreezeDataFolderName = migrationRoot, NotifyAhead_WhenWriteTablePossibleDeadlock = false };
            configuration.TextSearchConfig.TextEncryptor = new WabiStreamCrypto(key, iv);
            configuration.TextSearchConfig.UseTextEncryptor = false;
            using (var migrationEngine = new DBreezeEngine(configuration))
            {
                using (Transaction seed = migrationEngine.GetTransaction())
                {
                    seed.TextInsert("migration-source", 1.To_4_bytes_array_BigEndian(), "migrate alpha", String.Empty);
                    seed.Commit();
                }
                using (Transaction migrate = migrationEngine.GetTransaction())
                {
                    migrate.SynchronizeTables("migration-source", "migration-target");
                    c.Execute(mode, M(c, TransactionType, "Support_Migration_EncryptTextSearchTable", 2, null),
                        "Support_Migration_EncryptTextSearchTable", delegate
                        { migrate.Support_Migration_EncryptTextSearchTable("migration-source", "migration-target"); });
                    migrate.Commit();
                }
                using (Transaction verify = migrationEngine.GetTransaction())
                    Ensure(verify.TextSearch("migration-target").BlockAnd("alpha").GetDocumentIDs().Any(), "Text migration lost document.");
            }
            return semantic;
        }

        private static void RunVectors(DBreezeEngine engine, CoverageRegistry c, string mode, StableChecksum sum)
        {
            var floats = new List<(long, float[])>
            {
                (1L, new float[] { 1f, 0f, 0f }), (2L, new float[] { 0f, 1f, 0f }),
                (3L, new float[] { 0f, 0f, 1f }), (4L, new float[] { 0.7f, 0.7f, 0f })
            };
            var doubles = new List<(long, double[])>
            {
                (11L, new double[] { 1d, 0d, 0d }), (12L, new double[] { 0d, 1d, 0d }),
                (13L, new double[] { 0d, 0d, 1d }), (14L, new double[] { 0.7d, 0.7d, 0d })
            };
            using (Transaction t = engine.GetTransaction())
            {
                t.SynchronizeTables("vectors-f", "vectors-d");
                c.Execute(mode, M(c, TransactionType, "VectorsInsert", 3, "System.Single[]"), "VectorsInsert(float)",
                    delegate { t.VectorsInsert("vectors-f", floats, null); });
                c.Execute(mode, M(c, TransactionType, "VectorsInsert", 3, "System.Double[]"), "VectorsInsert(double)",
                    delegate { t.VectorsInsert("vectors-d", doubles, null); });
                t.Commit();
            }
            long count = 0;
            int all = 0, byId = 0, similarF = 0, similarD = 0;
            var failures = new List<Exception>();
            Attempt(failures, delegate { using (Transaction t = engine.GetTransaction()) count = c.Execute(mode, M(c, TransactionType, "VectorsCount", 3, null), "VectorsCount<TVector>", delegate { return t.VectorsCount<float[]>("vectors-f", null, false); }); });
            Attempt(failures, delegate { using (Transaction t = engine.GetTransaction()) all = c.Execute(mode, M(c, TransactionType, "VectorsGetAll", 3, null), "VectorsGetAll<TVector>", delegate { return t.VectorsGetAll<float[]>("vectors-f", null, true).Count(); }); });
            Attempt(failures, delegate { using (Transaction t = engine.GetTransaction()) byId = c.Execute(mode, M(c, TransactionType, "VectorsGetByExternalId", 4, null), "VectorsGetByExternalId<TVector>", delegate { return t.VectorsGetByExternalId<double[]>("vectors-d", new List<long> { 11, 14 }, null, true).Count(); }); });
            Attempt(failures, delegate { using (Transaction t = engine.GetTransaction()) similarF = c.Execute(mode, M(c, TransactionType, "VectorsSearchSimilar", 5, "System.Single[]"), "VectorsSearchSimilar(float)", delegate { return t.VectorsSearchSimilar("vectors-f", new float[] { 1f, 0f, 0f }, 2, null, true).Count(); }); });
            Attempt(failures, delegate { using (Transaction t = engine.GetTransaction()) similarD = c.Execute(mode, M(c, TransactionType, "VectorsSearchSimilar", 5, "System.Double[]"), "VectorsSearchSimilar(double)", delegate { return t.VectorsSearchSimilar("vectors-d", new double[] { 1d, 0d, 0d }, 2, null, true).Count(); }); });
            Attempt(failures, delegate { using (Transaction t = engine.GetTransaction()) { c.Execute(mode, M(c, TransactionType, "VectorsRemove", 3, null), "VectorsRemove<TVector>", delegate { t.VectorsRemove<float[]>("vectors-f", new List<long> { 4 }, null); }); t.Commit(); } });
            if (count != 4 || byId != 2 || similarF == 0 || similarD == 0)
                failures.Add(new InvalidDataException("Vector contract mismatch: count=" + count + ", all=" + all + ", byId=" + byId + ", search=" + similarF + "/" + similarD));
            sum.Add(count); sum.Add(all); sum.Add(byId); sum.Add(similarF); sum.Add(similarD);
            if (failures.Count != 0) throw new AggregateException("One or more vector public contracts failed.", failures);
        }

        private static void Attempt(ICollection<Exception> failures, Action action)
        {
            try { action(); }
            catch (Exception error) { failures.Add(error); }
        }

        private static void RunRestore(string root, DBreezeEngine engine, CoverageRegistry c, string mode, StableChecksum sum)
        {
            string sourceRoot = Path.Combine(root, "restore-source-db");
            string sourcePath;
            using (var source = new DBreezeEngine(sourceRoot))
            {
                using (Transaction t = source.GetTransaction()) { t.Insert("source", 77, "restored"); t.Commit(); }
                sourcePath = source.Scheme.GetTablePathFromTableName("source");
            }
            using (Transaction seed = engine.GetTransaction()) { seed.Insert("restore-target", 1, "old"); seed.Commit(); }
            using (Transaction t = engine.GetTransaction())
            {
                c.Execute(mode, M(c, TransactionType, "RestoreTableFromTheOtherFile", 3, null), "RestoreTableFromTheOtherFile",
                    delegate { t.RestoreTableFromTheOtherFile("restore-target", sourcePath, false); });
                t.Commit();
            }
            using (Transaction verify = engine.GetTransaction())
            {
                string value = verify.Select<int, string>("restore-target", 77).Value;
                Ensure(value == "restored", "RestoreTableFromTheOtherFile mismatch.");
                sum.Add(value);
            }
        }

        private static void RunScheme(DBreezeEngine engine, CoverageRegistry c, string mode, StableChecksum sum)
        {
            using (Transaction t = engine.GetTransaction()) { t.SynchronizeTables("scheme-source", "scheme-delete"); t.Insert("scheme-source", 1, "value"); t.Insert("scheme-delete", 1, 1); t.Commit(); }
            bool exists = c.Execute(mode, M(c, SchemeType, "IfUserTableExists", 1, null), "Scheme.IfUserTableExists",
                delegate { return engine.Scheme.IfUserTableExists("scheme-source"); });
            string path = c.Execute(mode, M(c, SchemeType, "GetTablePathFromTableName", 1, null), "Scheme.GetTablePathFromTableName",
                delegate { return engine.Scheme.GetTablePathFromTableName("scheme-source"); });
            List<string> names = c.Execute(mode, M(c, SchemeType, "GetUserTableNamesStartingWith", 1, null), "Scheme.GetUserTableNamesStartingWith",
                delegate { return engine.Scheme.GetUserTableNamesStartingWith("scheme-"); });
            Ensure(exists && File.Exists(path) && names.Count >= 2, "Scheme lookup contract failed.");
            c.Execute(mode, M(c, SchemeType, "RenameTable", 2, null), "Scheme.RenameTable",
                delegate { engine.Scheme.RenameTable("scheme-source", "scheme-renamed"); });
            c.Execute(mode, M(c, SchemeType, "DeleteTable", 1, null), "Scheme.DeleteTable",
                delegate { engine.Scheme.DeleteTable("scheme-delete"); });
            Ensure(engine.Scheme.IfUserTableExists("scheme-renamed") && !engine.Scheme.IfUserTableExists("scheme-delete"), "Scheme mutation contract failed.");
            sum.Add(names.Count); sum.Add(path);
        }

        private static string RunSharedEngine(string root)
        {
            Directory.CreateDirectory(root);
            using (var engine = new DBreezeEngine(Path.Combine(root, "db")))
            {
                var start = new ManualResetEventSlim(false);
                Task[] tasks = Enumerable.Range(0, 4).Select(delegate(int worker)
                {
                    return Task.Factory.StartNew(delegate
                    {
                        start.Wait();
                        using (Transaction t = engine.GetTransaction())
                        {
                            t.SynchronizeTables("shared", "disjoint-" + worker);
                            for (int i = 0; i < 100; i++)
                            {
                                t.Insert("disjoint-" + worker, i, worker);
                                t.Insert("shared", worker * 1000 + i, i);
                            }
                            t.Commit();
                        }
                        string table = "scheme-parallel-" + worker;
                        using (Transaction t = engine.GetTransaction()) { t.Insert(table, worker, worker); t.Commit(); }
                        if (!engine.Scheme.IfUserTableExists(table)) throw new InvalidDataException("Shared Scheme lost " + table);
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }).ToArray();
                start.Set();
                if (!Task.WaitAll(tasks, TimeSpan.FromSeconds(60))) throw new TimeoutException("Shared engine workload deadlocked.");
                using (Transaction verify = engine.GetTransaction())
                {
                    ulong count = verify.Count("shared");
                    Ensure(count == 400, "Shared-table final count mismatch: " + count);
                    return "shared=" + count + ";workers=4";
                }
            }
        }

        private static string RunOwnerThreadNegative(string root)
        {
            Directory.CreateDirectory(root);
            using (var engine = new DBreezeEngine(Path.Combine(root, "db")))
            {
                Transaction foreign = engine.GetTransaction();
                Exception observed = null;
                var started = new ManualResetEventSlim(false);
                Task task = Task.Factory.StartNew(delegate
                {
                    started.Set();
                    try { foreign.Insert("forbidden", 1, 1); }
                    catch (Exception error) { observed = error; }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                if (!started.Wait(TimeSpan.FromSeconds(5)) || !task.Wait(TimeSpan.FromSeconds(15)))
                    throw new TimeoutException("Foreign-thread transaction call deadlocked.");
                foreign.Dispose();
                Ensure(observed != null, "State-changing transaction call from a foreign thread was accepted.");
                using (Transaction verify = engine.GetTransaction())
                    Ensure(!verify.Select<int, int>("forbidden", 1).Exists, "Foreign-thread write leaked into database.");
                return observed.GetType().FullName;
            }
        }

        private static string RunTextRemoveProbe(string root)
        {
            Directory.CreateDirectory(root);
            using (var engine = new DBreezeEngine(Path.Combine(root, "db")))
            {
                using (Transaction t = engine.GetTransaction())
                {
                    t.TextInsert("text", 1.To_4_bytes_array_BigEndian(), "alpha beta", "group-a");
                    t.TextInsert("text", 2.To_4_bytes_array_BigEndian(), "beta gamma", "group-b");
                    t.TextAppend("text", 2.To_4_bytes_array_BigEndian(), "delta", "group-b");
                    t.TextInsert("text", 3.To_4_bytes_array_BigEndian(), "alpha delta", "group-a");
                    t.TextRemove("text", 3.To_4_bytes_array_BigEndian(), "delta");
                    t.Commit();
                }
                using (Transaction t = engine.GetTransaction())
                {
                    int[] ids = t.TextSearch("text").BlockOr(new[] { "alpha", "gamma" }, null).GetDocumentIDs()
                        .Select(delegate(byte[] id) { return id.To_Int32_BigEndian(); }).OrderBy(delegate(int id) { return id; }).ToArray();
                    return "or=" + String.Join(",", ids);
                }
            }
        }

        private static MethodInfo M(CoverageRegistry registry, string type, string name, int parameters, string discriminator)
        {
            return registry.Method(type, name, parameters, discriminator);
        }

        private static void RunCase(WorkerReport report, string id, string category, string mode, Func<string> run)
        {
            var item = new CaseResult { Id = id, Category = category, Mode = mode };
            var timer = Stopwatch.StartNew();
            try { item.SemanticValue = run(); item.Succeeded = true; }
            catch (Exception error) { item.Detail = error.ToString(); item.Succeeded = false; }
            finally { timer.Stop(); item.ElapsedMilliseconds = timer.ElapsedMilliseconds; report.Cases.Add(item); }
        }

        private static void RequireRoot(string root)
        {
            if (String.IsNullOrWhiteSpace(root)) throw new ArgumentException("This action requires --root.");
        }

        private static void Ensure(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }

        private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
        {
            internal static readonly ByteArrayComparer Instance = new ByteArrayComparer();
            public bool Equals(byte[] left, byte[] right) { return ReferenceEquals(left, right) || left != null && right != null && left.SequenceEqual(right); }
            public int GetHashCode(byte[] value)
            {
                if (value == null) return 0;
                unchecked { int hash = 17; foreach (byte item in value) hash = hash * 31 + item; return hash; }
            }
        }

        private sealed class StableChecksum
        {
            private ulong _value = 14695981039346656037UL;
            internal string Value { get { return _value.ToString("x16", System.Globalization.CultureInfo.InvariantCulture); } }
            internal void Add(int value) { Add((long)value); }
            internal void Add(long value) { unchecked { ulong data = (ulong)value; for (int shift = 0; shift < 64; shift += 8) Mix((byte)(data >> shift)); } }
            internal void Add(string value) { if (value == null) { Add(-1); return; } foreach (char item in value) { Mix((byte)item); Mix((byte)(item >> 8)); } }
            internal void Add(byte[] value) { if (value == null) { Add(-1); return; } Add(value.Length); foreach (byte item in value) Mix(item); }
            private void Mix(byte value) { _value ^= value; _value *= 1099511628211UL; }
        }
    }
}
