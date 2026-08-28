using System.Reflection;
using DBreeze;

internal static class SchemeIdleCacheRaceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly string TestRoot =
        Environment.GetEnvironmentVariable("DBREEZE_TEST_ROOT") ?? @"D:\Temp\DbreezeDbTest";

    internal static void LimitEvictionWaitsForPhysicalClose()
    {
        string folder = CreateFolder(nameof(LimitEvictionWaitsForPhysicalClose));
        try
        {
            using var engine = new DBreezeEngine(folder);
            using var entered = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            string closingTable = null;
            string observedCheckpoint = null;

            using (InstallHook(checkpoint =>
            {
                if (checkpoint.StartsWith("scheme.idle-close.", StringComparison.Ordinal))
                    observedCheckpoint = checkpoint;
                if (!checkpoint.StartsWith("scheme.idle-close.before-dispose|limit|@utlimit-", StringComparison.Ordinal))
                    return;
                closingTable = checkpoint.Substring(checkpoint.LastIndexOf('|') + 4);
                entered.Set();
                Assert(release.Wait(Timeout), "Limit close hook was not released.");
            }))
            {
                Task evictor = Task.Run(() =>
                {
                    using var transaction = engine.GetTransaction();
                    transaction.SynchronizeTables(Enumerable.Range(0, 9)
                        .Select(table => "limit-" + table)
                        .ToList());
                    for (int table = 0; table < 9; table++)
                        transaction.Insert("limit-" + table, 1, table + 100);
                    transaction.Commit();
                });

                if (!entered.Wait(Timeout))
                {
                    release.Set();
                    if (!evictor.Wait(Timeout))
                        throw new InvalidOperationException("The limit workload did not finish and no close hook was observed.");
                    if (evictor.IsFaulted)
                        throw new InvalidOperationException("The limit workload failed before eviction.", evictor.Exception);
                    throw new InvalidOperationException(
                        "The eight-table limit did not start an eviction. Last checkpoint: " +
                        (observedCheckpoint ?? "<none>"));
                }
                Assert(closingTable != null, "The limit hook did not identify the closing table.");

                Task<int> reacquire = Task.Run(() => Read(engine, closingTable));
                Assert(!reacquire.Wait(TimeSpan.FromMilliseconds(150)),
                    "GetTable reopened a disk table while its previous handle was closing.");

                release.Set();
                Assert(evictor.Wait(Timeout), "Limit eviction did not complete.");
                Assert(reacquire.Wait(Timeout), "Waiting GetTable did not resume after physical close.");
                Assert(reacquire.Result >= 100 && reacquire.Result <= 108,
                    "Reopened table returned an unexpected value.");
            }
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    internal static void TimerEvictionWaitsForPhysicalClose()
    {
        RunTimerWaitCase(nameof(TimerEvictionWaitsForPhysicalClose), null, "timer-main", false);
    }

    internal static void AlternativeStorageEvictionWaitsForPhysicalClose()
    {
        RunTimerWaitCase(nameof(AlternativeStorageEvictionWaitsForPhysicalClose), "alt*", "alt-timer", false);
    }

    internal static void DeleteRenameAndEngineDisposeWaitForPhysicalClose()
    {
        RunTimerWaitCase(nameof(DeleteRenameAndEngineDisposeWaitForPhysicalClose) + "Delete",
            null, "delete-wait", true);
        RunRenameWaitCase();
        RunEngineDisposeWaitCase();
    }

    internal static void CloseFailureFailsClosedAndPreservesCause()
    {
        string folder = CreateFolder(nameof(CloseFailureFailsClosedAndPreservesCause));
        DBreezeEngine engine = null;
        try
        {
            engine = new DBreezeEngine(folder);
            using var entered = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            var injected = new IOException("Injected idle-table close failure.");

            using (InstallHook(checkpoint =>
            {
                if (!checkpoint.Equals("scheme.idle-close.before-dispose|timer|@utfail-close",
                        StringComparison.Ordinal))
                    return;
                entered.Set();
                Assert(release.Wait(Timeout), "Failure hook was not released.");
                throw injected;
            }))
            {
                Write(engine, "fail-close", 41);
                Assert(entered.Wait(Timeout), "Timer did not enter the injected close failure.");

                Exception readFailure = null;
                Task waiter = Task.Run(() =>
                {
                    try { Read(engine, "fail-close"); }
                    catch (Exception exception) { readFailure = exception; }
                });
                Assert(!waiter.Wait(TimeSpan.FromMilliseconds(150)),
                    "GetTable did not wait for the closing table before the injected failure.");

                release.Set();
                Assert(waiter.Wait(Timeout), "GetTable waiter was not released after close failure.");
                Assert(readFailure != null, "Close failure was not propagated to the waiting GetTable.");
                Assert(ContainsReference(readFailure, injected),
                    "The original close exception was not retained in the exception chain.");
                Assert(!engine.DBisOperable, "Engine remained operable after a failed physical close.");
                Assert(engine.DBisOperableReason.Contains("@utfail-close", StringComparison.Ordinal),
                    "DBisOperableReason does not identify the failed table.");
            }
        }
        finally
        {
            if (engine != null)
            {
                try { engine.Dispose(); }
                catch (Exception exception)
                {
                    Assert(exception.ToString().Contains("Injected idle-table close failure", StringComparison.Ordinal),
                        "Engine disposal lost the recorded close failure.");
                }
            }
            DeleteFolder(folder);
        }
    }

    private static void RunTimerWaitCase(string testName, string alternativePattern,
        string tableName, bool delete)
    {
        string folder = CreateFolder(testName);
        string alternativeFolder = Path.Combine(folder, "alternative");
        try
        {
            DBreezeConfiguration configuration = new DBreezeConfiguration
            {
                DBreezeDataFolderName = folder,
                NotifyAhead_WhenWriteTablePossibleDeadlock = false
            };
            if (alternativePattern != null)
                configuration.AlternativeTablesLocations[alternativePattern] = alternativeFolder;

            using var engine = new DBreezeEngine(configuration);
            using var entered = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            string checkpointName = "scheme.idle-close.before-dispose|timer|@ut" + tableName;

            using (InstallHook(checkpoint =>
            {
                if (!checkpoint.Equals(checkpointName, StringComparison.Ordinal))
                    return;
                entered.Set();
                Assert(release.Wait(Timeout), "Timer close hook was not released.");
            }))
            {
                Write(engine, tableName, 73);
                Assert(entered.Wait(Timeout), "Idle timer did not start closing the table.");

                Task operation;
                if (delete)
                    operation = Task.Run(() => engine.Scheme.DeleteTable(tableName));
                else
                    operation = Task.Run(() => Assert(Read(engine, tableName) == 73,
                        "Reacquired timer table lost data."));

                Assert(!operation.Wait(TimeSpan.FromMilliseconds(150)),
                    "Operation passed a table whose old physical handle was still closing.");
                release.Set();
                Assert(operation.Wait(Timeout), "Operation did not resume after timer close.");

                if (delete)
                    Assert(!engine.Scheme.IfUserTableExists(tableName),
                        "DeleteTable did not remove the table after waiting for close.");
            }
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    private static void RunRenameWaitCase()
    {
        string folder = CreateFolder(nameof(DeleteRenameAndEngineDisposeWaitForPhysicalClose) + "Rename");
        try
        {
            using var engine = new DBreezeEngine(folder);
            using var entered = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            using (InstallHook(checkpoint =>
            {
                if (!checkpoint.Equals("scheme.idle-close.before-dispose|timer|@utrename-wait",
                        StringComparison.Ordinal))
                    return;
                entered.Set();
                Assert(release.Wait(Timeout), "Rename close hook was not released.");
            }))
            {
                Write(engine, "rename-wait", 91);
                Assert(entered.Wait(Timeout), "Rename table did not enter timer close.");
                Task rename = Task.Run(() => engine.Scheme.RenameTable("rename-wait", "rename-done"));
                Assert(!rename.Wait(TimeSpan.FromMilliseconds(150)),
                    "RenameTable passed a table whose old handle was still closing.");
                release.Set();
                Assert(rename.Wait(Timeout), "RenameTable did not resume after physical close.");
                Assert(Read(engine, "rename-done") == 91, "Renamed table lost data.");
            }
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    private static void RunEngineDisposeWaitCase()
    {
        string folder = CreateFolder(nameof(DeleteRenameAndEngineDisposeWaitForPhysicalClose) + "Dispose");
        DBreezeEngine engine = null;
        try
        {
            engine = new DBreezeEngine(folder);
            using var entered = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            using (InstallHook(checkpoint =>
            {
                if (!checkpoint.Equals("scheme.idle-close.before-dispose|timer|@utdispose-wait",
                        StringComparison.Ordinal))
                    return;
                entered.Set();
                Assert(release.Wait(Timeout), "Dispose close hook was not released.");
            }))
            {
                Write(engine, "dispose-wait", 55);
                Assert(entered.Wait(Timeout), "Dispose table did not enter timer close.");
                Task dispose = Task.Run(engine.Dispose);
                Assert(!dispose.Wait(TimeSpan.FromMilliseconds(150)),
                    "Engine disposal returned while idle-table close was still running.");
                release.Set();
                Assert(dispose.Wait(Timeout), "Engine disposal did not wait for physical close.");
                engine = null;
            }
        }
        finally
        {
            engine?.Dispose();
            DeleteFolder(folder);
        }
    }

    private static IDisposable InstallHook(Action<string> handler)
    {
        Type hooks = typeof(DBreezeEngine).Assembly.GetType("DBreeze.Storage.DurabilityTestHooks", true);
        FieldInfo field = hooks.GetField("Handler", BindingFlags.Static | BindingFlags.NonPublic);
        if (field == null)
            throw new InvalidOperationException("DBreeze was not built with concurrency test hooks.");
        object previous = field.GetValue(null);
        field.SetValue(null, handler);
        return new CallbackDisposable(() => field.SetValue(null, previous));
    }

    private static void Write(DBreezeEngine engine, string table, int value)
    {
        using var transaction = engine.GetTransaction();
        transaction.Insert(table, 1, value);
        transaction.Commit();
    }

    private static int Read(DBreezeEngine engine, string table)
    {
        using var transaction = engine.GetTransaction();
        return transaction.Select<int, int>(table, 1).Value;
    }

    private static bool ContainsReference(Exception exception, Exception expected)
    {
        for (Exception current = exception; current != null; current = current.InnerException)
        {
            if (ReferenceEquals(current, expected))
                return true;
            if (current is AggregateException aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions)
                {
                    if (ContainsReference(inner, expected))
                        return true;
                }
            }
        }
        return false;
    }

    private static string CreateFolder(string name)
    {
        string root = Path.Combine(TestRoot, "net8-regressions");
        Directory.CreateDirectory(root);
        string folder = Path.Combine(root, name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void DeleteFolder(string folder)
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class CallbackDisposable : IDisposable
    {
        private Action _callback;
        internal CallbackDisposable(Action callback) => _callback = callback;
        public void Dispose() => Interlocked.Exchange(ref _callback, null)?.Invoke();
    }
}
