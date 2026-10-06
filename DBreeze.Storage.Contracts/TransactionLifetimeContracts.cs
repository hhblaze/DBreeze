using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DBreeze;
using DBreeze.DataTypes;
using DBreeze.Exceptions;
using DBreeze.Transactions;
using DBreeze.Utils;

internal static class TransactionLifetimeContracts
{
    private const string NestedError = "NESTED TRANSACTIONS ARE NOT ALLOWED";
    private const string RowError = "ROW READ REQUIRES AN ACTIVE TRANSACTION. Load the required data before disposing the transaction.";

    internal static void RunAll()
    {
        string[] names = Enum.GetNames(typeof(DBreezeException.eDBreezeExceptions));
        Check(names[names.Length - 2] == "TRANSACTION_NESTED_NOT_ALLOWED" &&
            names[names.Length - 1] == "ROW_TRANSACTION_IS_NOT_ACTIVE", "New enum values must be appended.");
        Check((int)DBreezeException.eDBreezeExceptions.TRANSACTION_DOESNT_EXIST == 24,
            "Existing exception enum numbers changed.");
        foreach (bool disk in new[] { false, true })
        {
            NestedTransactions(disk);
            EscapedRows(disk);
            NestedRows(disk);
            EngineTermination(disk);
            RegistrationFailure(disk);
            Console.WriteLine("PASS TransactionLifetimeContracts " + (disk ? "disk" : "memory"));
        }
        DeadlockTerminatesRows();
        Console.WriteLine("PASS DeadlockTerminatesRows");
    }

    private static Transaction Open(DBreezeEngine engine, int kind)
    {
        return kind == 0 ? engine.GetTransaction() : engine.GetTransaction(
            kind == 1 ? eTransactionTablesLockTypes.SHARED : eTransactionTablesLockTypes.EXCLUSIVE, "snapshot");
    }

    private static void NestedTransactions(bool disk)
    {
        // A timeout catches accidental self-wait in the locked transaction constructor.
        Task worker = Task.Factory.StartNew(delegate
        {
            using (var fixture = new Fixture(disk))
            {
                for (int first = 0; first < 3; first++)
                for (int second = 0; second < 3; second++)
                {
                    using (Transaction outer = Open(fixture.Engine, first))
                    {
                        int innerKind = second;
                        Error(delegate { using (Open(fixture.Engine, innerKind)) { } }, NestedError);
                        outer.Insert("snapshot", 1, new byte[] { 1, 2, 3 });
                        Row<int, byte[]> row = outer.Select<int, byte[]>("snapshot", 1);
                        Error(delegate { using (Open(fixture.Engine, innerKind)) { } }, NestedError);
                        Check(row.Value.SequenceEqual(new byte[] { 1, 2, 3 }), "Rejected nesting invalidated the outer row.");
                        outer.Commit();
                        Error(delegate { using (Open(fixture.Engine, innerKind)) { } }, NestedError);
                        Row<int, byte[]> afterCommit = outer.Select<int, byte[]>("snapshot", 1);
                        outer.Insert("snapshot", 1, new byte[] { 9 });
                        outer.Rollback();
                        Error(delegate { using (Open(fixture.Engine, innerKind)) { } }, NestedError);
                        Check(afterCommit.Value.Length == 3, "Commit/Rollback ended the row lifetime.");
                        Check(outer.Select<int, byte[]>("snapshot", 1).Value.Length == 3, "Outer rollback failed.");
                    }
                    using (Transaction next = Open(fixture.Engine, second))
                        Check(next.Select<int, byte[]>("snapshot", 1).Value.Length == 3, "Outer commit was lost.");
                }

                using (Transaction first = fixture.Engine.GetTransaction())
                using (var other = new Fixture(disk))
                using (Transaction second = other.Engine.GetTransaction())
                {
                    first.Select<int, byte[]>("snapshot", 1);
                    second.Select<int, byte[]>("snapshot", 1);
                    Task independent = Task.Factory.StartNew(delegate
                    {
                        using (Transaction parallel = fixture.Engine.GetTransaction())
                            Check(parallel.Select<int, byte[]>("snapshot", 1).Exists, "Independent thread cannot read.");
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    Check(independent.Wait(TimeSpan.FromSeconds(5)), "Independent transaction was blocked.");
                }
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Check(worker.Wait(TimeSpan.FromSeconds(20)), "Nested transaction waited on its own lock.");
    }

    private static void EscapedRows(bool disk)
    {
        using (var fixture = new Fixture(disk))
        {
            DBreezeEngine engine = fixture.Engine;
            List<Row<int, byte[]>> lazy;
            List<Row<int, byte[]>> variants;
            List<Row<int, byte[]>> eager;
            Row<int, byte[]> loaded;
            Row<int, byte[]> loadedNull;
            Row<int, byte[]> loadedEmpty;
            Row<int, byte[]> partial;
            Row<int, byte[]> missing;
            Row<int, byte[]> block;
            Row<int, byte[]> fixedBlock;
            Row<byte[], byte[]> objectRow;
            DBreeze.Objects.DBreezeObject<byte[]> materializedObject;
            using (Transaction reader = engine.GetTransaction())
            {
                lazy = reader.SelectForward<int, byte[]>("snapshot").ToList();
                variants = Variants(reader);
                loaded = reader.Select<int, byte[]>("snapshot", 1);
                Check(loaded.Value.SequenceEqual(new byte[] { 1, 2, 3 }), "Loading a row failed.");
                loadedNull = reader.Select<int, byte[]>("snapshot", 2);
                loadedEmpty = reader.Select<int, byte[]>("snapshot", 3);
                Check(loadedNull.Value == null && loadedEmpty.Value.Length == 0, "Loading null/empty values failed.");
                partial = reader.Select<int, byte[]>("snapshot", 1);
                Check(partial.GetValuePart(1, 1)[0] == 2, "Active partial read failed.");
                missing = reader.Select<int, byte[]>("snapshot", 999);
                block = reader.Select<int, byte[]>("block", 1);
                Check(block.GetDataBlock().SequenceEqual(new byte[] { 7, 8, 9 }), "Active datablock read failed.");
                fixedBlock = reader.Select<int, byte[]>("block", 2);
                Check(fixedBlock.GetDataBlockWithFixedAddress<byte[]>().SequenceEqual(new byte[] { 7, 8, 9 }),
                    "Active fixed-address datablock read failed.");
                objectRow = reader.Select<byte[], byte[]>("objects", 1.ToIndex(7));
                materializedObject = objectRow.ObjectGet<byte[]>();
                Check(materializedObject.Entity[0] == 11, "Active object read failed.");
                Check(objectRow.Value.Length > 0 && fixedBlock.Value.Length > 0,
                    "Pointer values could not be loaded.");
                reader.ValuesLazyLoadingIsOn = false;
                eager = reader.SelectForward<int, byte[]>("snapshot").ToList();
            }

            Inactive(delegate { var value = lazy[0].Value; });
            Inactive(delegate { var value = partial.Value; });
            Inactive(delegate { partial.GetValuePart(0); });
            Check(loaded.Value.SequenceEqual(new byte[] { 1, 2, 3 }), "Loaded value requires a live transaction.");
            Check(loaded.GetValuePart(1, 2).SequenceEqual(new byte[] { 2, 3 }), "Loaded partial value requires storage.");
            foreach (Row<int, byte[]> row in variants)
            {
                Check(row.Exists && row.Key != 0, "Escaped row metadata was lost.");
                Inactive(delegate { var pointer = row.LinkToValue; });
            }
            Inactive(delegate { lazy[0].GetTable(0); });
            Inactive(delegate { block.GetDataBlock(); });
            Inactive(delegate { block.GetDataBlockWithFixedAddress<byte[]>(); });
            Inactive(delegate { block.ObjectGet<byte[]>(); });
            Inactive(delegate { fixedBlock.GetDataBlockWithFixedAddress<byte[]>(); });
            Inactive(delegate { objectRow.ObjectGet<byte[]>(); });

            using (Transaction writer = engine.GetTransaction())
            {
                writer.Insert("snapshot", 1, new byte[] { 4, 5, 6 });
                writer.Commit();
                Inactive(delegate { var value = lazy[0].Value; });
                Check(loaded.Value[0] == 1, "Loaded value changed with the database.");
            }
            Inactive(delegate { var value = lazy[0].Value; });
            Check(!missing.Exists && missing.Value == null && missing.LinkToValue == null &&
                missing.GetValuePart(0) == null && missing.GetDataBlock() == null &&
                missing.GetDataBlockWithFixedAddress<byte[]>() == null && missing.ObjectGet<byte[]>() == null,
                "Missing-row defaults changed.");
            using (NestedTable absent = missing.GetTable(0)) Check(absent.Count() == 0, "Missing nested table default changed.");

            engine.Dispose();
            Inactive(delegate { var value = lazy[0].Value; });
            Check(eager[0].Value.SequenceEqual(new byte[] { 1, 2, 3 }), "Eager value cannot outlive the engine.");
            Check(eager[1].Value == null && eager[2].Value.Length == 0, "Eager null/empty values changed.");
            Check(loadedNull.Value == null && loadedEmpty.Value.Length == 0 &&
                loadedNull.GetValuePart(0) == null && loadedEmpty.GetValuePart(0).Length == 0,
                "Explicitly loaded null/empty values cannot outlive the engine.");
            Check(materializedObject.Entity[0] == 11, "Materialized object cannot outlive the engine.");
            Check(eager[1].GetValuePart(0) == null && eager[2].GetValuePart(0).Length == 0,
                "Eager null/empty partial values changed.");
            Check(loaded.GetValuePart(0).Length == 3, "Loaded partial read touched the closed engine.");
        }
    }

    private static List<Row<int, byte[]>> Variants(Transaction reader)
    {
        var rows = new List<Row<int, byte[]>>();
        rows.Add(reader.Select<int, byte[]>("snapshot", 1));
        rows.Add(reader.SelectDirect<int, byte[]>("snapshot", reader.Select<int, byte[]>("snapshot", 1).LinkToValue));
        rows.Add(reader.Min<int, byte[]>("snapshot"));
        rows.Add(reader.Max<int, byte[]>("snapshot"));
        rows.AddRange(reader.SelectForward<int, byte[]>("snapshot"));
        rows.AddRange(reader.SelectBackward<int, byte[]>("snapshot"));
        rows.AddRange(reader.SelectForwardStartFrom<int, byte[]>("snapshot", 1, true));
        rows.AddRange(reader.SelectBackwardStartFrom<int, byte[]>("snapshot", 3, true));
        rows.AddRange(reader.SelectForwardFromTo<int, byte[]>("snapshot", 1, true, 3, true, 1));
        rows.AddRange(reader.SelectBackwardFromTo<int, byte[]>("snapshot", 3, true, 1, true, 1));
        rows.AddRange(reader.SelectForwardSkip<int, byte[]>("snapshot", 1));
        rows.AddRange(reader.SelectBackwardSkip<int, byte[]>("snapshot", 1));
        rows.AddRange(reader.SelectForwardSkipFrom<int, byte[]>("snapshot", 1, 1));
        rows.AddRange(reader.SelectBackwardSkipFrom<int, byte[]>("snapshot", 3, 1));
        var tables = new HashSet<string> { "snapshot", "peer" };
        List<Row<int, byte[]>> merged = reader.Multi_SelectForwardFromTo<int, byte[]>(tables, 1, true, 3, true).ToList();
        Check(merged.All(row => row.TableName != null), "Multi-select table metadata was lost.");
        rows.AddRange(merged);
        rows.AddRange(reader.Multi_SelectBackwardFromTo<int, byte[]>(tables, 3, true, 1, true));
        return rows;
    }

    private static void NestedRows(bool disk)
    {
        using (var fixture = new Fixture(disk))
        {
            var rows = new List<Row<int, byte[]>>();
            Row<int, byte[]> loaded;
            Row<int, byte[]> eager;
            NestedTable escapedParent;
            using (Transaction reader = fixture.Engine.GetTransaction())
            using (NestedTable parent = reader.SelectTable("nested", 1, 0))
            {
                escapedParent = parent;
                rows.Add(parent.Select<int, byte[]>(1));
                rows.Add(parent.SelectDirect<int, byte[]>(rows[0].LinkToValue));
                rows.Add(parent.Min<int, byte[]>());
                rows.Add(parent.Max<int, byte[]>());
                rows.AddRange(parent.SelectForward<int, byte[]>());
                rows.AddRange(parent.SelectBackward<int, byte[]>());
                rows.AddRange(parent.SelectForwardFromTo<int, byte[]>(1, true, 3, true, 1));
                rows.AddRange(parent.SelectBackwardFromTo<int, byte[]>(3, true, 1, true, 1));
                rows.AddRange(parent.SelectForwardSkip<int, byte[]>(1));
                rows.AddRange(parent.SelectBackwardSkip<int, byte[]>(1));
                using (NestedTable child = parent.GetTable(4, 0)) rows.Add(child.Select<int, byte[]>(1));
                using (NestedTable viaRow = parent.Select<int, byte[]>(4).GetTable(0)) rows.Add(viaRow.Select<int, byte[]>(1));
                using (NestedTable viaRoot = reader.Select<int, byte[]>("nested", 1).GetTable(0)) rows.Add(viaRoot.Select<int, byte[]>(1));
                Task<Row<int, byte[]>> independent = Task.Factory.StartNew(delegate
                {
                    using (Transaction other = fixture.Engine.GetTransaction())
                    using (NestedTable sameTable = other.SelectTable("nested", 1, 0))
                        return sameTable.Select<int, byte[]>(1);
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                Check(independent.Wait(TimeSpan.FromSeconds(5)), "Independent nested reader did not complete.");
                Inactive(delegate { var value = independent.Result.Value; });
                Check(parent.Select<int, byte[]>(1).Value[0] == 1,
                    "Another transaction invalidated this handle's lifetime.");
                loaded = parent.Select<int, byte[]>(1);
                Check(loaded.Value[0] == 1, "Nested lazy read failed.");
                parent.ValuesLazyLoadingIsOn = false;
                eager = parent.Select<int, byte[]>(1);
            }
            foreach (Row<int, byte[]> row in rows) Inactive(delegate { var pointer = row.LinkToValue; });
            Inactive(delegate { escapedParent.Select<int, byte[]>(1); });
            Inactive(delegate { var value = rows[0].Value; });
            Inactive(delegate { var value = rows[rows.Count - 1].Value; });
            fixture.Engine.Dispose();
            Check(loaded.Value[0] == 1 && eager.Value[0] == 1, "Materialized nested value cannot outlive its transaction.");
        }
    }

    private static void EngineTermination(bool disk)
    {
        using (var fixture = new Fixture(disk))
        using (Transaction reader = fixture.Engine.GetTransaction())
        {
            Row<int, byte[]> lazy = reader.Select<int, byte[]>("snapshot", 1);
            fixture.Engine.Dispose();
            Inactive(delegate { var value = lazy.Value; });
            Check(lazy.Exists && lazy.Key == 1, "Engine termination invalidated metadata.");
        }
    }

    private static void RegistrationFailure(bool disk)
    {
        using (var fixture = new Fixture(disk))
        {
            fixture.Configuration.NotifyAhead_WhenWriteTablePossibleDeadlock = true;
            using (Transaction failed = fixture.Engine.GetTransaction())
            {
                Row<int, byte[]> lazy = failed.Select<int, byte[]>("snapshot", 1);
                failed.Insert("snapshot", 1, new byte[] { 9 });
                bool terminated = false;
                try { failed.Insert("unreserved", 1, new byte[] { 8 }); }
                catch (DBreezeException) { terminated = true; }
                Check(terminated, "Expected registration failure did not occur.");
                Inactive(delegate { var value = lazy.Value; });
            }
            using (Transaction next = fixture.Engine.GetTransaction())
                Check(next.Select<int, byte[]>("snapshot", 1).Value[0] == 1, "Terminated transaction did not roll back.");
        }
    }

    private static void DeadlockTerminatesRows()
    {
        using (var fixture = new Fixture(false))
        using (var barrier = new Barrier(2))
        {
            int terminated = 0;
            Task[] workers = Enumerable.Range(0, 2).Select(index => Task.Factory.StartNew(delegate
            {
                using (Transaction transaction = fixture.Engine.GetTransaction())
                {
                    Row<int, byte[]> lazy = transaction.Select<int, byte[]>("snapshot", 1);
                    transaction.Insert("deadlock-" + index, 1, 1);
                    Check(barrier.SignalAndWait(TimeSpan.FromSeconds(5)), "Deadlock setup timed out.");
                    try
                    {
                        transaction.Insert("deadlock-" + (1 - index), 1, 2);
                        transaction.Commit();
                    }
                    catch (DBreezeException exception)
                    {
                        Check(exception.Message.IndexOf("deadlock", StringComparison.OrdinalIgnoreCase) >= 0,
                            "Unexpected coordinator failure: " + exception.Message);
                        Inactive(delegate { var value = lazy.Value; });
                        Interlocked.Increment(ref terminated);
                    }
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            Check(Task.WaitAll(workers, TimeSpan.FromSeconds(15)), "Deadlock workers did not finish.");
            Check(terminated > 0, "No deadlock victim was terminated.");
        }
    }

    internal static void RunPerformance()
    {
        const int records = 20000;
        const int cycles = 50;
        foreach (bool disk in new[] { false, true })
        using (var fixture = new Fixture(disk))
        {
            using (Transaction writer = fixture.Engine.GetTransaction())
            {
                for (int key = 1; key <= records; key++) writer.Insert("perf", key, new byte[64]);
                writer.Commit();
            }
            foreach (string scenario in new[] { "PointRead", "LazyScan", "EagerScan" })
            {
                Func<long> read = delegate
                {
                    long count = 0;
                    using (Transaction reader = fixture.Engine.GetTransaction())
                    {
                        reader.ValuesLazyLoadingIsOn = scenario != "EagerScan";
                        if (scenario == "PointRead")
                            for (int key = 1; key <= records; key++) count += reader.Select<int, byte[]>("perf", key).Value.Length;
                        else foreach (Row<int, byte[]> row in reader.SelectForward<int, byte[]>("perf")) count += row.Value.Length;
                    }
                    return count;
                };
                for (int warm = 0; warm < 5; warm++) read();
                GC.Collect();
                long before = AllocatedBytes();
                var timer = Stopwatch.StartNew();
                long checksum = 0;
                for (int cycle = 0; cycle < cycles; cycle++) checksum += read();
                timer.Stop();
                long allocated = AllocatedBytes() - before;
                Console.WriteLine("LIFETIME_PERF\t" + (disk ? "disk" : "memory") + "\t" + scenario + "\t" +
                    timer.Elapsed.TotalMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "\t" +
                    records * cycles + "\t" + allocated + "\t" + checksum);
            }
        }
    }

    private static long AllocatedBytes()
    {
#if ALLOC_COUNTER || NET8_0_OR_GREATER
        return GC.GetAllocatedBytesForCurrentThread();
#else
        return 0;
#endif
    }

    private static void Inactive(Action action) { Error(action, RowError); }
    private static void Error(Action action, string message)
    {
        try { action(); }
        catch (DBreezeException exception) { Check(exception.Message == message, "Unexpected error: " + exception.Message); return; }
        throw new InvalidOperationException("Expected DBreezeException: " + message);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture : IDisposable
    {
        internal readonly DBreezeConfiguration Configuration;
        internal readonly DBreezeEngine Engine;
        private readonly string _root;
        private readonly string _baseRoot;
        internal Fixture(bool disk)
        {
            _baseRoot = Path.GetFullPath(Environment.GetEnvironmentVariable("DBREEZE_TEST_ROOT") ??
                Path.Combine(Path.GetTempPath(), "DBreeze-lifetime-tests"));
            _root = Path.Combine(_baseRoot, "transaction-lifetime-" + Guid.NewGuid().ToString("N"));
            Configuration = new DBreezeConfiguration {
                Storage = disk ? DBreezeConfiguration.eStorage.DISK : DBreezeConfiguration.eStorage.MEMORY,
                DBreezeDataFolderName = _root,
                NotifyAhead_WhenWriteTablePossibleDeadlock = false,
            };
#if PORTABLE_HOST
            Configuration.FSFactory = new DBreeze.Programmers.FSFactory();
#endif
            Engine = new DBreezeEngine(Configuration);
            using (Transaction writer = Engine.GetTransaction())
            {
                writer.Insert("snapshot", 1, new byte[] { 1, 2, 3 });
                writer.Insert<int, byte[]>("snapshot", 2, null);
                writer.Insert("snapshot", 3, new byte[0]);
                writer.Insert("peer", 1, new byte[] { 1, 2, 3 });
                byte[] block = writer.InsertDataBlock("block", null, new byte[] { 7, 8, 9 });
                writer.Insert("block", 1, block);
                byte[] fixedBlock = writer.InsertDataBlockWithFixedAddress("block", null, new byte[] { 7, 8, 9 });
                writer.Insert("block", 2, fixedBlock);
                writer.ObjectInsert("objects", new DBreeze.Objects.DBreezeObject<byte[]> {
                    NewEntity = true,
                    Entity = new byte[] { 11 },
                    Indexes = new List<DBreeze.Objects.DBreezeIndex> {
                        new DBreeze.Objects.DBreezeIndex(1, 7) { PrimaryIndex = true }
                    }
                });
                using (NestedTable parent = writer.InsertTable("nested", 1, 0))
                {
                    for (int key = 1; key <= 3; key++) parent.Insert(key, new byte[] { 1, 2, 3 });
                    using (NestedTable child = parent.GetTable(4, 0)) child.Insert(1, new byte[] { 1, 2, 3 });
                }
                writer.Commit();
            }
        }
        public void Dispose()
        {
            Engine.Dispose();
            string resolved = Path.GetFullPath(_root);
            Check(resolved.StartsWith(_baseRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase), "Test cleanup escaped its root.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }
}
