using System.Buffers.Binary;
using System.Reflection;
using DBreeze;
using DBreeze.DataTypes;
using DBreeze.Transactions;
using DBreeze.Storage.RemoteInstance;

internal static class LazyReadCommitTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private const string Key = "X";

    internal static void RunAll()
    {
        foreach (string backend in new[] { "memory", "mapped", "fallback" })
        {
            foreach (bool nested in new[] { false, true })
            foreach (bool longKey in new[] { false, true })
                BarrierContract(backend, nested, longKey ? new string('K', 12000) : Key);
            EdgeCases(backend);
            PreparedCommitContract(backend);
            IteratorAcrossCommits(backend);
            foreach (string mode in new[] { "full-value", "partial-once", "header-then-partial" })
                Stress(backend, mode);
            InFlightRead(backend, eager: false);
            InFlightRead(backend, eager: true);
            foreach (bool longKey in new[] { false, true })
            foreach (byte[] next in new[] { Frame(2048, 66), null, Array.Empty<byte>() })
                EagerProbeAcrossCommit(backend, longKey ? new string('K', 12000) : Key, next);
            Console.WriteLine("PASS LazyReadCommit " + backend);
        }
        string remoteRoot = Path.Combine(Environment.GetEnvironmentVariable("DBREEZE_TEST_ROOT") ?? Path.GetTempPath(), "lazy-read-remote", Guid.NewGuid().ToString("N"));
        using var handler = new RemoteTablesHandler(remoteRoot);
        using var remote = new DBreezeRemoteEngine(new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.RemoteInstance, RICommunicator = new Loopback(handler) });
        BarrierContract("remote", false, Key, remote);
        BarrierContract("remote", true, new string('K', 12000), remote);
        Console.WriteLine("PASS LazyReadCommit remote");
    }

    private static DBreezeEngine Engine(string backend)
    {
        string root = Environment.GetEnvironmentVariable("DBREEZE_TEST_ROOT") ?? Path.Combine(Path.GetTempPath(), "DBreeze.Tests");
        return backend == "memory" ? new DBreezeEngine(new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.MEMORY })
            : new DBreezeEngine(Path.Combine(root, "lazy-read-commit", backend, Guid.NewGuid().ToString("N")));
    }

    private static byte[] Frame(int length, byte tag)
    {
        byte[] bytes = Enumerable.Repeat(tag, length).ToArray();
        if (length >= 4) BinaryPrimitives.WriteInt32LittleEndian(bytes, length);
        return bytes;
    }

    private static bool Equal(byte[] actual, byte[] expected) => actual == null ? expected == null : expected != null && actual.AsSpan().SequenceEqual(expected);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Wait(ManualResetEventSlim signal) => Check(signal.Wait(Timeout), "Lazy-read test barrier timed out.");
    private static Row<string, byte[]> Select(Transaction tx, NestedTable nested, string key) => nested == null ? tx.Select<string, byte[]>("t", key) : nested.Select<string, byte[]>(key);
    private static void Insert(Transaction tx, NestedTable nested, string key, byte[] value)
    {
        if (nested == null) tx.Insert("t", key, value); else nested.Insert(key, value);
    }

    private static void Seed(DBreezeEngine engine, string backend, byte[] value, bool nested = false, string key = Key)
    {
        using var tx = engine.GetTransaction();
        using var table = nested ? tx.InsertTable("parents", 1, 0) : null;
        Insert(tx, table, key, value); tx.Commit();
        if (backend == "fallback")
            DisableMapping(nested ? tx.Select<int, byte[]>("parents", 1) : (object)tx.Select<string, byte[]>("t", key));
    }

    private static void DisableMapping(object row)
    {
        object trieRow = row.GetType().GetField("_row", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(row)!;
        object root = trieRow.GetType().GetProperty("Root")!.GetValue(trieRow)!;
        object trie = root.GetType().GetField("Tree")!.GetValue(root)!;
        object layer = trie.GetType().GetField("Storage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(trie)!;
        object storage = layer.GetType().GetField("_tableStorage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(layer)!;
        storage.GetType().GetMethod("DisableCommittedMappingForTesting", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(storage, null);
    }

    private static void BarrierContract(string backend, bool nested, string key, DBreezeEngine suppliedEngine = null)
    {
        byte[] a = Frame(4096, 65), b = Frame(2048, 66);
        using var ownedEngine = suppliedEngine == null ? Engine(backend) : null;
        var engine = suppliedEngine ?? ownedEngine;
        Seed(engine, backend, a, nested, key);
        using var written = new ManualResetEventSlim(); using var allowCommit = new ManualResetEventSlim();
        var writer = Task.Factory.StartNew(() =>
        {
            using var tx = engine.GetTransaction();
            using var table = nested ? tx.InsertTable("parents", 1, 0) : null;
            Insert(tx, table, key, b);
            Check(Equal(Select(tx, table, key).Value, b), "Writer lost its own uncommitted value.");
            written.Set(); Wait(allowCommit); tx.Commit();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Wait(written);
        try
        {
            using var tx = engine.GetTransaction();
            using var table = nested ? tx.SelectTable("parents", 1, 0) : null;
            var lazy = Select(tx, table, key);
            var partial = Select(tx, table, key);
            var once = Select(tx, table, key);
            var direct = table == null ? tx.SelectDirect<string, byte[]>("t", lazy.LinkToValue) : table.SelectDirect<string, byte[]>(lazy.LinkToValue);
            var forward = (table == null ? tx.SelectForward<string, byte[]>("t") : table.SelectForward<string, byte[]>()).Single();
            var backward = (table == null ? tx.SelectBackward<string, byte[]>("t") : table.SelectBackward<string, byte[]>()).Single();
            Check(Equal(Select(tx, table, key).Value, a), "Reader saw an uncommitted writer value.");
            Check(partial.GetValuePart(0, 16).AsSpan().SequenceEqual(a.AsSpan(0, 16)), "Reader header was dirty.");
            tx.ValuesLazyLoadingIsOn = false;
            if (table != null) table.ValuesLazyLoadingIsOn = false;
            var eager = Select(tx, table, key);
            allowCommit.Set(); Check(writer.Wait(Timeout), "Writer failed to commit.");
            byte[] lazyValue = lazy.Value;
            Check(Equal(lazyValue, a) || Equal(lazyValue, b), "Retained lazy Value returned an invalid version without a preceding partial read.");
            foreach (var row in new[] { lazy, partial, once, direct, forward, backward })
            {
                byte[] value = row.GetValuePart(0, 8193);
                Check(Equal(value, a) || Equal(value, b), "Retained row returned an invalid partial value.");
                value = row.Value;
                Check(Equal(value, a) || Equal(value, b), "Retained row returned an invalid full value.");
                Check(row.Key == key, "Retained row key changed.");
            }
            Check(Equal(eager.Value, a), "An eager row changed after commit.");
            Check(Equal(Select(tx, table, key).Value, b), "New Select did not advance after commit.");
        }
        finally { allowCommit.Set(); Check(writer.Wait(Timeout), "Writer did not finish."); }
    }

    private static void EagerProbeAcrossCommit(string backend, string key, byte[] next)
    {
        using var engine = Engine(backend);
        Seed(engine, backend, Frame(4096, 65), key: key);
        using var probed = new ManualResetEventSlim();
        using var published = new ManualResetEventSlim();
        FieldInfo hook = typeof(DBreezeEngine).Assembly.GetType("DBreeze.Storage.DurabilityTestHooks")!
            .GetField("Handler", BindingFlags.Static | BindingFlags.NonPublic)!;
        int readerThread = 0, paused = 0;
        Action<string> handler = checkpoint =>
        {
            if (checkpoint == "read.eager-probe" && Environment.CurrentManagedThreadId == readerThread &&
                Interlocked.Exchange(ref paused, 1) == 0)
            { probed.Set(); Wait(published); }
        };
        Task<byte[]> reader = null;
        hook.SetValue(null, handler);
        try
        {
            reader = Task.Factory.StartNew(() =>
            {
                readerThread = Environment.CurrentManagedThreadId;
                using var tx = engine.GetTransaction();
                tx.ValuesLazyLoadingIsOn = false;
                var row = tx.Select<string, byte[]>("t", key);
                Check(row.Exists && row.Key == key, "Eager retry lost the key.");
                return row.Value;
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Wait(probed);
            using (var tx = engine.GetTransaction())
            {
                tx.Insert("t", key, next);
                Check(Equal(tx.Select<string, byte[]>("t", key).Value, next), "Writer lost its own value during eager retry.");
                tx.Commit();
            }
            published.Set();
            Check(reader.Wait(Timeout), "Eager publication retry timed out.");
            Check(Equal(reader.Result, next), "Eager payload used a pre-publication extent.");
        }
        finally
        {
            published.Set();
            hook.SetValue(null, null);
            if (reader != null) Check(reader.Wait(Timeout), "Eager retry reader did not finish.");
        }
    }

    private static void EdgeCases(string backend)
    {
        using var engine = Engine(backend); Seed(engine, backend, Frame(4096, 65));
        byte[][] values = { Frame(2048, 66), Frame(200, 67), Frame(8192, 68), Frame(8192, 69), null, Array.Empty<byte>(), Frame(16, 70) };
        using var reader = engine.GetTransaction();
        foreach (byte[] next in values)
        {
            byte[] before = reader.Select<string, byte[]>("t", Key).Value;
            var held = reader.Select<string, byte[]>("t", Key);
            Task.Run(() => { using var tx = engine.GetTransaction(); tx.Insert("t", Key, next); tx.Commit(); }).GetAwaiter().GetResult();
            byte[] actual = held.Value;
            Check(Equal(actual, before) || Equal(actual, next), "Value extent survived a commit incorrectly.");
            var fresh = reader.Select<string, byte[]>("t", Key);
            Check(Equal(fresh.Value, next), "Fresh null/empty/grown value differs.");
            Check(Equal(fresh.GetValuePart(0, UInt32.MaxValue), next), "Full partial-read boundaries differ.");
            if (next != null && next.Length != 0)
            {
                var boundary = reader.Select<string, byte[]>("t", Key);
                Check(boundary.GetValuePart((uint)next.Length, 99).Length == 0, "End boundary was not empty.");
                Check(boundary.GetValuePart((uint)next.Length + 1, 99) == null, "Past-end boundary was not null.");
                Check(boundary.GetValuePart(0, 0).Length == 0, "Zero-length read was not empty.");
            }
            Task.Run(() => { using var tx = engine.GetTransaction(); tx.Insert("t", Key, Frame(128, 90)); tx.Rollback(); }).GetAwaiter().GetResult();
            Check(Equal(reader.Select<string, byte[]>("t", Key).Value, next), "Rollback changed committed data.");
        }
    }

    private static void Stress(string backend, string mode)
    {
        byte[][] values = { Frame(4096, 65), Frame(2048, 66), Frame(200, 67) };
        using var engine = Engine(backend); Seed(engine, backend, values[0]);
        using var start = new ManualResetEventSlim();
        int failures = 0, ownFailures = 0;
        var writer = Task.Run(() =>
        {
            Wait(start);
            for (int i = 0; i < 120; i++)
            {
                using var tx = engine.GetTransaction(); tx.Insert("t", Key, values[i % 3]);
                if (!Equal(tx.Select<string, byte[]>("t", Key).Value, values[i % 3])) Interlocked.Increment(ref ownFailures);
                tx.Commit();
            }
        });
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            Wait(start);
            for (int i = 0; i < 2000; i++)
            {
                using var tx = engine.GetTransaction(); var row = tx.Select<string, byte[]>("t", Key);
                if (mode == "header-then-partial") row.GetValuePart(0, 16);
                Thread.Yield();
                byte[] bytes = mode == "full-value" ? row.Value : row.GetValuePart(0, 8193);
                if (!values.Any(value => Equal(bytes, value))) Interlocked.Increment(ref failures);
            }
        })).ToArray();
        start.Set();
        Check(Task.WaitAll(readers.Append(writer).ToArray(), TimeSpan.FromSeconds(60)), "Concurrent lazy reads hung.");
        Check(failures == 0 && ownFailures == 0, $"{backend}/{mode}: invalid={failures}, own={ownFailures}");
    }

    private static void PreparedCommitContract(string backend)
    {
        FieldInfo hook = typeof(DBreezeEngine).Assembly.GetType("DBreeze.Storage.DurabilityTestHooks")!
            .GetField("Handler", BindingFlags.Static | BindingFlags.NonPublic)!;
        byte[] a = Frame(4096, 65), b = Frame(2048, 66);
        using var engine = Engine(backend); Seed(engine, backend, a);
        using var prepared = new ManualResetEventSlim(); using var finish = new ManualResetEventSlim();
        int participants = 0;
        Action<string> handler = checkpoint =>
        {
            if (checkpoint == "transaction.participant-prepared" && Interlocked.Increment(ref participants) == 2)
            { prepared.Set(); Wait(finish); }
        };
        hook.SetValue(null, handler);
        Task writer = null;
        try
        {
            writer = Task.Factory.StartNew(() =>
            {
                using var tx = engine.GetTransaction(); tx.SynchronizeTables("t", "peer");
                tx.Insert("t", Key, b); tx.Insert("peer", 1, new byte[] { 1 }); tx.Commit();
            },
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Wait(prepared);
            // B is physically written, A is served through rollback overlays.
            // Publishing the commit now retires overlays without another data write.
            using var reader = engine.GetTransaction();
            var lazy = reader.Select<string, byte[]>("t", Key);
            var partial = reader.Select<string, byte[]>("t", Key);
            Check(Equal(reader.Select<string, byte[]>("t", Key).Value, a), "Prepare exposed B before publication.");
            Check(partial.GetValuePart(0, 16).AsSpan().SequenceEqual(a.AsSpan(0, 16)), "Prepared overlay header differs.");
            finish.Set(); Check(writer.Wait(Timeout), "Prepared commit did not finish.");
            Check(Equal(lazy.Value, b), "Overlay retirement did not invalidate the saved extent.");
            Check(Equal(partial.GetValuePart(0), b), "Partial extent survived overlay retirement.");
        }
        finally { finish.Set(); if (writer != null) Check(writer.Wait(Timeout), "Prepared writer cleanup timed out."); hook.SetValue(null, null); }
    }

    private static void IteratorAcrossCommits(string backend)
    {
        byte[] a = Frame(4096, 65), b = Frame(2048, 66), c = Frame(200, 67);
        using var engine = Engine(backend); Seed(engine, backend, a);
        Task.Run(() => { using var tx = engine.GetTransaction(); tx.Insert("t", "A", a); tx.Commit(); }).GetAwaiter().GetResult();
        using var reader = engine.GetTransaction();
        using var iterator = reader.SelectForward<string, byte[]>("t").GetEnumerator();
        Check(iterator.MoveNext() && iterator.Current.Key == "A", "Iterator anchor differs.");
        Task.Run(() => { using var tx = engine.GetTransaction(); tx.Insert("t", Key, b); tx.Commit(); }).GetAwaiter().GetResult();
        Check(iterator.MoveNext() && iterator.Current.Key == Key, "Iterator lost its second key.");
        var held = iterator.Current;
        Check(Equal(held.GetValuePart(0), b), "Old-root iterator read a mixed value.");
        Task.Run(() => { using var tx = engine.GetTransaction(); tx.Insert("t", Key, c); tx.Commit(); }).GetAwaiter().GetResult();
        Check(Equal(held.GetValuePart(0), c), "Old-root extent was incorrectly made current.");
    }

    private static void InFlightRead(string backend, bool eager)
    {
        FieldInfo hook = typeof(DBreezeEngine).Assembly.GetType("DBreeze.Storage.DurabilityTestHooks")!
            .GetField("Handler", BindingFlags.Static | BindingFlags.NonPublic);
        if (hook == null) throw new InvalidOperationException("In-flight regression requires the test-hook build.");
        byte[] a = Frame(4096, 65), b = Frame(2048, 66);
        using var engine = Engine(backend); Seed(engine, backend, a);
        using var prepared = new ManualResetEventSlim(); using var allowCommit = new ManualResetEventSlim();
        using var header = new ManualResetEventSlim(); using var resume = new ManualResetEventSlim();
        using var commitAttempt = new ManualResetEventSlim(); using var committed = new ManualResetEventSlim();
        var writer = Task.Factory.StartNew(() =>
        {
            using var tx = engine.GetTransaction(); tx.Insert("t", Key, b); prepared.Set();
            Wait(allowCommit); tx.Commit(); committed.Set();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Wait(prepared);
        int readerThread = 0;
        Action<string> handler = checkpoint =>
        {
            if (checkpoint == "storage.write-admission" && Environment.CurrentManagedThreadId != readerThread) commitAttempt.Set();
            if (checkpoint == "read.record-header" && Environment.CurrentManagedThreadId == readerThread)
            { header.Set(); Wait(resume); }
        };
        hook.SetValue(null, handler);
        Task<byte[]> read = null;
        try
        {
            read = Task.Factory.StartNew(() =>
            {
                readerThread = Environment.CurrentManagedThreadId;
                using var tx = engine.GetTransaction(); tx.ValuesLazyLoadingIsOn = !eager;
                // A traversal row has no previously cached extent, forcing the
                // real header/payload boundary for both lazy and eager reads.
                return tx.SelectForward<string, byte[]>("t").Single().Value;
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Wait(header); allowCommit.Set(); Wait(commitAttempt);
            Check(!committed.Wait(TimeSpan.FromMilliseconds(50)), "Commit crossed an in-flight record read.");
            resume.Set(); Check(read.Wait(Timeout), "Protected read did not finish.");
            Check(Equal(read.Result, a), "In-flight record mixed two committed versions.");
            Check(writer.Wait(Timeout), "Commit remained blocked after the read.");
        }
        finally
        {
            resume.Set(); allowCommit.Set();
            if (read != null) Check(read.Wait(Timeout), "Read cleanup timed out.");
            Check(writer.Wait(Timeout), "Commit cleanup timed out.");
            hook.SetValue(null, null);
        }
    }

    private sealed class Loopback : IRemoteInstanceCommunicator
    {
        private readonly RemoteTablesHandler _handler;
        internal Loopback(RemoteTablesHandler handler) { _handler = handler; }
        public byte[] Send(byte[] data) => _handler.ParseProtocol(data);
    }
}
