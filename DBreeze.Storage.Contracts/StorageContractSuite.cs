using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DBreeze;
using DBreeze.Storage;

internal static class StorageContractSuite
{
    internal static void RunAll()
    {
        Run("BaselineArchitecture", BaselineArchitecture);
        Run("CommitRollbackOverlapAndAutoFlush", CommitRollbackOverlapAndAutoFlush);
        Run("CrashRecoveryAndTruncatedJournal", CrashRecoveryAndTruncatedJournal);
        Run("RestoreRecreateAndReopen", RestoreRecreateAndReopen);
        Run("BackupRoundTrip", BackupRoundTrip);
        Run("ConcurrentReadersAndWriter2", delegate { ConcurrentReadersAndWriter(2); });
        Run("ConcurrentReadersAndWriter8", delegate { ConcurrentReadersAndWriter(8); });
        Console.WriteLine("PASS StorageContracts target=" + StorageTestSupport.TargetName);
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine("PASS " + name);
    }

    private static void BaselineArchitecture()
    {
        string root = StorageTestSupport.CreateRoot("architecture");
        try
        {
            using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
            {
                var storage = new StorageLayer(Path.Combine(root, "1"), new TrieSettings(), configuration);
                object implementation = typeof(StorageLayer).GetField("_tableStorage",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(storage);
                Type type = implementation.GetType();
#if NET8_HOST
                StorageTestSupport.Assert(type.GetField("_sharedReadBuffer", BindingFlags.Instance | BindingFlags.NonPublic) != null,
                    "Net8 modern FSR has lost its shared small-read lane.");
#else
                FieldInfo gate = type.GetField("lock_fs", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo bufferSize = type.GetField("_fileStreamBufferSize", BindingFlags.Instance | BindingFlags.NonPublic);
                StorageTestSupport.Assert(gate != null && gate.GetValue(implementation) != null,
                    "Baseline FSR must serialize cursor operations with one per-table gate.");
                StorageTestSupport.Assert(bufferSize != null && (int)bufferSize.GetValue(implementation) == 8192,
                    "Baseline FSR stream buffer must remain 8 KiB.");
                StorageTestSupport.Assert(type.GetField("_readLock", BindingFlags.Instance | BindingFlags.NonPublic) == null,
                    "ReaderWriterLockSlim must not leak into the baseline FSR.");
                StorageTestSupport.Assert(type.GetField("_sharedReadBuffer", BindingFlags.Instance | BindingFlags.NonPublic) == null,
                    "The Net8 shared read lane must not be copied into cursor-based FSR.");
#endif
                storage.Table_Dispose();
            }
        }
        finally
        {
            StorageTestSupport.DeleteRoot(root);
        }
    }

    private static void CommitRollbackOverlapAndAutoFlush()
    {
        string root = StorageTestSupport.CreateRoot("views");
        try
        {
            using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
            {
                var storage = new StorageLayer(Path.Combine(root, "1"), new TrieSettings(), configuration);
                try
                {
                    byte[] original = StorageTestSupport.Bytes(4096, 1701);
                    long start = StorageTestSupport.DecodePointer(storage.Table_WriteToTheEnd(original));
                    storage.Commit();

                    byte[] append = StorageTestSupport.Bytes(1024 * 1024 + 17, 1702);
                    long appendOffset = StorageTestSupport.DecodePointer(storage.Table_WriteToTheEnd(append));
                    StorageTestSupport.Assert(storage.Table_Read(true, appendOffset, 1).Length == 0,
                        "Committed view exposed an uncommitted append.");
                    StorageTestSupport.AssertBytes(append, storage.Table_Read(false, appendOffset, append.Length),
                        "Writer view lost a large/partial sequential write.");
                    storage.Commit();
                    StorageTestSupport.AssertBytes(append, storage.Table_Read(true, appendOffset, append.Length),
                        "Committed large append differs.");

                    for (int index = 0; index < 700; index++)
                        storage.Table_WriteByOffset(start + 512 + index, new byte[] { (byte)(index * 31) });
                    byte[] overlap = StorageTestSupport.Bytes(900, 1703);
                    storage.Table_WriteByOffset(start + 350, overlap);
                    StorageTestSupport.AssertBytes(overlap, storage.Table_Read(false, start + 350, overlap.Length),
                        "Writer overlay differs after auto-flush.");
                    byte[] originalOverlap = new byte[overlap.Length];
                    Buffer.BlockCopy(original, 350, originalOverlap, 0, originalOverlap.Length);
                    StorageTestSupport.AssertBytes(originalOverlap, storage.Table_Read(true, start + 350, overlap.Length),
                        "Committed view lost an overlapping rollback range.");
                    storage.TransactionalCommit();
                    storage.TransactionalRollback();
                    StorageTestSupport.AssertBytes(original, storage.Table_Read(true, start, original.Length),
                        "Transactional rollback did not restore overlapping updates.");

                    byte[] committed = StorageTestSupport.Bytes(257, 1704);
                    storage.Table_WriteByOffset(start + 100, committed);
                    storage.TransactionalCommit();
                    storage.TransactionalCommitIsFinished();
                    StorageTestSupport.AssertBytes(committed, storage.Table_Read(true, start + 100, committed.Length),
                        "Transactional commit publication failed.");
                }
                finally
                {
                    storage.Table_Dispose();
                }
            }
        }
        finally
        {
            StorageTestSupport.DeleteRoot(root);
        }
    }

    private static void CrashRecoveryAndTruncatedJournal()
    {
        string root = StorageTestSupport.CreateRoot("recovery");
        string table = Path.Combine(root, "1");
        try
        {
            byte[] original = StorageTestSupport.Bytes(8192, 1801);
            long start;
            using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
            {
                var storage = new StorageLayer(table, new TrieSettings(), configuration);
                start = StorageTestSupport.DecodePointer(storage.Table_WriteToTheEnd(original));
                storage.Commit();
                for (int index = 0; index < 900; index++)
                    storage.Table_WriteByOffset(start + 300 + index, new byte[] { (byte)(index ^ 0xA5) });
                storage.Table_WriteByOffset(start + 512, StorageTestSupport.Bytes(2048, 1802));
                storage.TransactionalCommit();
                storage.Table_Dispose();
            }

            using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
            {
                var recovered = new StorageLayer(table, new TrieSettings(), configuration);
                StorageTestSupport.AssertBytes(original, recovered.Table_Read(true, start, original.Length),
                    "Crash recovery did not restore exact overlapping ranges.");
                recovered.Table_Dispose();
            }

            string truncated = Path.Combine(root, "2");
            using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
            {
                var storage = new StorageLayer(truncated, new TrieSettings(), configuration);
                storage.Commit();
                storage.Table_Dispose();
            }
            File.WriteAllBytes(truncated + ".rol", new byte[] { 1, 0, 0, 0, 0 });
            File.WriteAllBytes(truncated + ".rhp", StorageTestSupport.Int64BigEndian(5));
            using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
                StorageTestSupport.AssertThrows<Exception>(delegate { new StorageLayer(truncated, new TrieSettings(), configuration); });
        }
        finally
        {
            StorageTestSupport.DeleteRoot(root);
        }
    }

    private static void RestoreRecreateAndReopen()
    {
        string root = StorageTestSupport.CreateRoot("lifecycle");
        string destination = Path.Combine(root, "1");
        string source = Path.Combine(root, "2");
        try
        {
            byte[] first = StorageTestSupport.Bytes(1024, 1901);
            long firstStart;
            using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
            {
                var storage = new StorageLayer(destination, new TrieSettings(), configuration);
                firstStart = StorageTestSupport.DecodePointer(storage.Table_WriteToTheEnd(first));
                storage.Commit();
                StorageTestSupport.AssertThrows<FileNotFoundException>(delegate
                {
                    storage.RestoreTableFromTheOtherTable(Path.Combine(root, "missing"));
                });
                StorageTestSupport.AssertBytes(first, storage.Table_Read(true, firstStart, first.Length),
                    "A missing restore source changed destination data.");
                storage.Table_Dispose();
            }

            byte[] second = StorageTestSupport.Bytes(2048, 1902);
            using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
            {
                var sourceStorage = new StorageLayer(source, new TrieSettings(), configuration);
                sourceStorage.Table_WriteToTheEnd(second);
                sourceStorage.Commit();
                sourceStorage.Table_Dispose();
            }
            using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
            {
                var storage = new StorageLayer(destination, new TrieSettings(), configuration);
                storage.RestoreTableFromTheOtherTable(source);
                StorageTestSupport.AssertBytes(second, storage.Table_Read(true, StorageTestSupport.HeaderSize, second.Length),
                    "Restore did not replace destination contents.");
                storage.RecreateFiles();
                byte[] recreated = { 9, 8, 7, 6 };
                long offset = StorageTestSupport.DecodePointer(storage.Table_WriteToTheEnd(recreated));
                storage.Commit();
                StorageTestSupport.Assert(offset == StorageTestSupport.HeaderSize, "Recreate retained the previous EOF.");
                storage.Table_Dispose();
            }
            using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
            {
                var reopened = new StorageLayer(destination, new TrieSettings(), configuration);
                StorageTestSupport.AssertBytes(new byte[] { 9, 8, 7, 6 },
                    reopened.Table_Read(true, StorageTestSupport.HeaderSize, 4), "Reopen differs after recreate.");
                reopened.Table_Dispose();
            }
        }
        finally
        {
            StorageTestSupport.DeleteRoot(root);
        }
    }

    private static void BackupRoundTrip()
    {
        string root = StorageTestSupport.CreateRoot("backup");
        string source = Path.Combine(root, "source");
        string backup = Path.Combine(root, "backup");
        string restored = Path.Combine(root, "restored");
        Directory.CreateDirectory(source);
        try
        {
            byte[] payload = StorageTestSupport.Bytes(1024 * 1024 + 137, 2001);
            using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
            {
                configuration.Backup.BackupFolderName = backup;
                var storage = new StorageLayer(Path.Combine(source, "1"), new TrieSettings(), configuration);
                storage.Table_WriteToTheEnd(payload);
                storage.Commit();
                storage.Table_Dispose();
            }
            BackupRestorer restorer = StorageTestSupport.CreateRestorer(backup, restored);
            restorer.StartRestoration();
            StorageTestSupport.AssertBytes(File.ReadAllBytes(Path.Combine(source, "1")),
                File.ReadAllBytes(Path.Combine(restored, "1")), "Backup/restore changed the table file.");
        }
        finally
        {
            StorageTestSupport.DeleteRoot(root);
        }
    }

    private static void ConcurrentReadersAndWriter(int readerCount)
    {
        string root = StorageTestSupport.CreateRoot("parallel-" + readerCount);
        try
        {
            using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
            {
                var storage = new StorageLayer(Path.Combine(root, "1"), new TrieSettings(), configuration);
                byte[] initial = Repeated(0, 4096);
                long start = StorageTestSupport.DecodePointer(storage.Table_WriteToTheEnd(initial));
                storage.Commit();

                var barrier = new Barrier(readerCount + 1);
                var cancellation = new CancellationTokenSource();
                var failures = new List<Exception>();
                object failureGate = new object();
                Task[] tasks = new Task[readerCount + 1];
                for (int reader = 0; reader < readerCount; reader++)
                {
                    tasks[reader] = Task.Factory.StartNew(delegate
                    {
                        try
                        {
                            barrier.SignalAndWait();
                            while (!cancellation.IsCancellationRequested)
                            {
                                byte[] value = storage.Table_Read(true, start, initial.Length);
                                byte generation = value[0];
                                for (int index = 1; index < value.Length; index++)
                                    if (value[index] != generation)
                                        throw new InvalidOperationException("A reader observed torn cursor data.");
                            }
                        }
                        catch (Exception exception)
                        {
                            lock (failureGate) failures.Add(exception);
                            cancellation.Cancel();
                        }
                    });
                }
                tasks[readerCount] = Task.Factory.StartNew(delegate
                {
                    try
                    {
                        barrier.SignalAndWait();
                        for (int generation = 1; generation <= 60; generation++)
                        {
                            storage.Table_WriteByOffset(start, Repeated((byte)generation, initial.Length));
                            storage.TransactionalCommit();
                            storage.TransactionalCommitIsFinished();
                        }
                    }
                    catch (Exception exception)
                    {
                        lock (failureGate) failures.Add(exception);
                    }
                    finally
                    {
                        cancellation.Cancel();
                    }
                });

                if (!Task.WaitAll(tasks, TimeSpan.FromSeconds(30)))
                    throw new TimeoutException("Concurrent storage contract timed out with " + readerCount + " readers.");
                if (failures.Count != 0)
                    throw new AggregateException(failures);
                StorageTestSupport.AssertBytes(Repeated(60, initial.Length),
                    storage.Table_Read(true, start, initial.Length), "Final committed generation differs.");
                storage.Table_Dispose();
            }
        }
        finally
        {
            StorageTestSupport.DeleteRoot(root);
        }
    }

    private static byte[] Repeated(byte value, int length)
    {
        byte[] result = new byte[length];
        for (int index = 0; index < result.Length; index++)
            result[index] = value;
        return result;
    }
}
