using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using DBreeze;

internal static class SchemeConcurrencyContracts
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    internal static void RunAll()
    {
        Run("ConcurrentUniqueTableCreation", ConcurrentUniqueTableCreation);
        Run("ConcurrentSameTableCreation", ConcurrentSameTableCreation);
        Run("ParallelTableTransactionChurn", ParallelTableTransactionChurn);
        Console.WriteLine("PASS SchemeConcurrencyContracts target=" + StorageTestSupport.TargetName);
    }

    private static void ConcurrentUniqueTableCreation()
    {
        string root = StorageTestSupport.CreateRoot("scheme-concurrent-unique");
        try
        {
            DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration();
            configuration.DBreezeDataFolderName = root;
            configuration.NotifyAhead_WhenWriteTablePossibleDeadlock = false;
            using (var engine = new DBreezeEngine(configuration))
            {
                const int tableCount = 64;
                RunParallel(tableCount, delegate(int worker)
                {
                    using (var transaction = engine.GetTransaction())
                    {
                        transaction.Insert("unique-" + worker, worker, worker * 17 + 3);
                        transaction.Commit();
                    }
                });

                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int table = 0; table < tableCount; table++)
                {
                    string tableName = "unique-" + table;
                    StorageTestSupport.Assert(engine.Scheme.IfUserTableExists(tableName),
                        "Concurrently created table is missing: " + tableName);
                    string path = Path.GetFullPath(engine.Scheme.GetTablePathFromTableName(tableName));
                    StorageTestSupport.Assert(paths.Add(path),
                        "Two concurrently created tables received the same physical path: " + path);
                    using (var transaction = engine.GetTransaction())
                    {
                        var row = transaction.Select<int, int>(tableName, table);
                        StorageTestSupport.Assert(row.Exists && row.Value == table * 17 + 3,
                            "Concurrently created table has an invalid row: " + tableName);
                    }
                }
            }
        }
        finally
        {
            StorageTestSupport.DeleteRoot(root);
        }
    }

    private static void ConcurrentSameTableCreation()
    {
        string root = StorageTestSupport.CreateRoot("scheme-concurrent-same");
        try
        {
            DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration();
            configuration.DBreezeDataFolderName = root;
            configuration.NotifyAhead_WhenWriteTablePossibleDeadlock = false;
            using (var engine = new DBreezeEngine(configuration))
            {
                const int workerCount = 32;
                RunParallel(workerCount, delegate(int worker)
                {
                    using (var transaction = engine.GetTransaction())
                    {
                        transaction.SynchronizeTables("same-table");
                        transaction.Insert("same-table", worker, worker * 31 + 7);
                        transaction.Commit();
                    }
                });

                string path = engine.Scheme.GetTablePathFromTableName("same-table");
                StorageTestSupport.Assert(!String.IsNullOrEmpty(path) && File.Exists(path),
                    "Concurrent same-table creation did not produce one physical table.");
                using (var transaction = engine.GetTransaction())
                {
                    transaction.ValuesLazyLoadingIsOn = false;
                    int count = 0;
                    long checksum = 0;
                    foreach (var row in transaction.SelectForward<int, int>("same-table"))
                    {
                        count++;
                        checksum += row.Key * 1000003L + row.Value;
                    }
                    long expected = 0;
                    for (int worker = 0; worker < workerCount; worker++)
                        expected += worker * 1000003L + worker * 31 + 7;
                    StorageTestSupport.Assert(count == workerCount && checksum == expected,
                        "Concurrent same-table creation lost or duplicated rows.");
                }
            }
        }
        finally
        {
            StorageTestSupport.DeleteRoot(root);
        }
    }

    private static void ParallelTableTransactionChurn()
    {
        string root = StorageTestSupport.CreateRoot("scheme-parallel-table-churn");
        try
        {
            DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration();
            configuration.DBreezeDataFolderName = root;
            configuration.NotifyAhead_WhenWriteTablePossibleDeadlock = false;
            using (var engine = new DBreezeEngine(configuration))
            {
                const int tableCount = 20;
                const int transactionsPerTable = 25;
                RunParallel(tableCount, delegate(int table)
                {
                    string tableName = "churn-" + table;
                    for (int key = 0; key < transactionsPerTable; key++)
                    {
                        using (var transaction = engine.GetTransaction())
                        {
                            transaction.Insert(tableName, key, table * 1000 + key);
                            transaction.Commit();
                        }
                    }
                });

                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int table = 0; table < tableCount; table++)
                {
                    string tableName = "churn-" + table;
                    string path = Path.GetFullPath(engine.Scheme.GetTablePathFromTableName(tableName));
                    StorageTestSupport.Assert(paths.Add(path),
                        "Parallel table churn produced a duplicate physical path: " + path);
                    using (var transaction = engine.GetTransaction())
                    {
                        transaction.ValuesLazyLoadingIsOn = false;
                        int count = 0;
                        foreach (var row in transaction.SelectForward<int, int>(tableName))
                        {
                            StorageTestSupport.Assert(row.Value == table * 1000 + row.Key,
                                "Parallel table churn returned an invalid value.");
                            count++;
                        }
                        StorageTestSupport.Assert(count == transactionsPerTable,
                            "Parallel table churn lost rows in " + tableName);
                    }
                }
                StorageTestSupport.Assert(engine.DBisOperable,
                    "Parallel table churn made the engine non-operable: " + engine.DBisOperableReason);
            }
        }
        finally
        {
            StorageTestSupport.DeleteRoot(root);
        }
    }

    private static void RunParallel(int workerCount, Action<int> action)
    {
        var ready = new CountdownEvent(workerCount);
        var completed = new CountdownEvent(workerCount);
        var start = new ManualResetEventSlim(false);
        var failures = new ConcurrentQueue<Exception>();
        var threads = new Thread[workerCount];

        for (int worker = 0; worker < workerCount; worker++)
        {
            int captured = worker;
            threads[worker] = new Thread(delegate()
            {
                try
                {
                    ready.Signal();
                    start.Wait();
                    action(captured);
                }
                catch (Exception exception)
                {
                    failures.Enqueue(new InvalidOperationException(
                        "Scheme concurrency worker " + captured + " failed.", exception));
                }
                finally
                {
                    completed.Signal();
                }
            });
            threads[worker].IsBackground = true;
            threads[worker].Name = "DBreeze scheme contract " + worker;
            threads[worker].Start();
        }

        StorageTestSupport.Assert(ready.Wait(Timeout), "Scheme concurrency workers did not become ready.");
        start.Set();
        StorageTestSupport.Assert(completed.Wait(Timeout), "Scheme concurrency workers timed out.");
        for (int worker = 0; worker < threads.Length; worker++)
            StorageTestSupport.Assert(threads[worker].Join(5000), "Scheme concurrency worker did not stop.");

        if (!failures.IsEmpty)
            throw new AggregateException(failures);
    }

    private static void Run(string name, Action action)
    {
        action();
        Console.WriteLine("PASS " + name);
    }
}
