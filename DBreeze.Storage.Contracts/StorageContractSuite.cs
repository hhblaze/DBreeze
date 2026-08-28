using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DBreeze;
using DBreeze.Storage;
using DBreeze.Storage.RemoteInstance;

internal static class StorageContractSuite
{
    internal static void RunAll()
    {
        Run("BaselineArchitecture", BaselineArchitecture);
        Run("TransactionJournalPayloadCodec", TransactionJournalPayloadCodec);
        Run("MalformedTransactionJournalFailsClosed", MalformedTransactionJournalFailsClosed);
        Run("BufferedWriteSetRandomizedModel", BufferedWriteSetRandomizedModel);
        Run("CommitRollbackOverlapAndAutoFlush", CommitRollbackOverlapAndAutoFlush);
        Run("CrashRecoveryAndTruncatedJournal", CrashRecoveryAndTruncatedJournal);
        Run("RestoreRecreateAndReopen", RestoreRecreateAndReopen);
        Run("BackupRoundTrip", BackupRoundTrip);
        Run("AlternativeDiskAndMemoryContracts", AlternativeDiskAndMemoryContracts);
        Run("RemoteInstanceLoopbackContract", RemoteInstanceLoopbackContract);
        Run("ConcurrentReadersAndWriter2", delegate { ConcurrentReadersAndWriter(2); });
            Run("ConcurrentReadersAndWriter8", delegate { ConcurrentReadersAndWriter(8); });
            SchemeConcurrencyContracts.RunAll();
            Console.WriteLine("PASS StorageContracts target=" + StorageTestSupport.TargetName);
    }

    private static void TransactionJournalPayloadCodec()
    {
        const string compact = "<string>journal-a</string>\n<string>journal-b</string>\n";
        const string framework =
            "<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n" +
            "<ArrayOfString xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" " +
            "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\r\n" +
            "  <string>journal-a</string>\r\n" +
            "  <string>journal-b</string>\r\n" +
            "</ArrayOfString>";
        const string canonical =
            "<ArrayOfString>\n<string>journal-a</string>\n<string>journal-b</string>\n</ArrayOfString>";

        Type codec = typeof(StorageLayer).Assembly.GetType(
            "DBreeze.Transactions.TransactionJournalPayloadCodec", true);
        MethodInfo serialize = codec.GetMethod("Serialize", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo deserialize = codec.GetMethod("Deserialize", BindingFlags.Static | BindingFlags.NonPublic);
        StorageTestSupport.Assert(serialize != null && deserialize != null,
            "Transaction journal payload codec contract is incomplete.");

        var names = new List<string> { "journal-a", "journal-b" };
        StorageTestSupport.Assert(Encoding.UTF8.GetByteCount(compact) == 54,
            "Legacy compact journal fixture changed.");
        StorageTestSupport.Assert(Encoding.UTF8.GetByteCount(framework) == 233,
            "Legacy Framework journal fixture changed.");
        AssertJournalNames(deserialize, compact, names, "legacy compact");
        AssertJournalNames(deserialize, framework, names, "legacy Framework");
        StorageTestSupport.Assert((string)serialize.Invoke(null, new object[] { names }) == canonical,
            "Canonical journal payload changed.");

        var escaped = new List<string> { "journal-&-<tag>", "journal-line\r\nbreak", "journal-😀" };
        string escapedPayload = (string)serialize.Invoke(null, new object[] { escaped });
        AssertJournalNames(deserialize, escapedPayload, escaped, "escaped canonical");

        AssertJournalPayloadRejected(deserialize, String.Empty);
        AssertJournalPayloadRejected(deserialize, "<unknown />");
        AssertJournalPayloadRejected(deserialize, "<string>journal-a");
        AssertJournalPayloadRejected(deserialize, "<ArrayOfString />");
        AssertJournalPayloadRejected(deserialize,
            "<!DOCTYPE ArrayOfString [<!ENTITY x 'journal-a'>]><ArrayOfString><string>&x;</string></ArrayOfString>");
    }

    private static void AssertJournalNames(
        MethodInfo deserialize, string payload, IList<string> expected, string format)
    {
        var actual = (IList<string>)deserialize.Invoke(null, new object[] { payload });
        StorageTestSupport.Assert(actual.Count == expected.Count,
            "Unexpected " + format + " journal table count.");
        for (int i = 0; i < expected.Count; i++)
            StorageTestSupport.Assert(actual[i] == expected[i],
                "Unexpected " + format + " journal table at index " + i + ".");
    }

    private static void AssertJournalPayloadRejected(MethodInfo deserialize, string payload)
    {
        try
        {
            deserialize.Invoke(null, new object[] { payload });
        }
        catch (TargetInvocationException exception)
        {
            if (exception.InnerException is InvalidDataException)
                return;
            throw;
        }
        throw new InvalidOperationException("An invalid transaction journal payload was accepted.");
    }

    private static void MalformedTransactionJournalFailsClosed()
    {
        byte[][] invalidPayloads =
        {
            new byte[0],
            Encoding.UTF8.GetBytes("<string>journal-a"),
            Encoding.UTF8.GetBytes("<ArrayOfString><string>journal-a</ArrayOfString>"),
        };

        for (int index = 0; index < invalidPayloads.Length; index++)
            AssertMalformedTransactionJournalFailsClosed(invalidPayloads[index], index);
    }

    private static void AssertMalformedTransactionJournalFailsClosed(byte[] payload, int index)
    {
        string root = StorageTestSupport.CreateRoot("malformed-transaction-journal-" + index);
        DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration();
        configuration.DBreezeDataFolderName = root;
        configuration.NotifyAhead_WhenWriteTablePossibleDeadlock = false;

        try
        {
            using (DBreezeEngine engine = new DBreezeEngine(configuration))
            {
            }

            WriteTransactionJournalPayload(root, configuration, payload);

            bool failedClosed = false;
            try
            {
                using (DBreezeEngine unexpected = new DBreezeEngine(configuration))
                {
                }
            }
            catch (DBreeze.Exceptions.DBreezeException exception)
            {
                failedClosed = ContainsException<InvalidDataException>(exception);
            }

            StorageTestSupport.Assert(failedClosed,
                "Malformed transaction journal startup did not preserve InvalidDataException.");
            byte[][] persisted = ReadTransactionJournalPayloads(root, configuration);
            StorageTestSupport.Assert(persisted.Length == 1,
                "Malformed transaction journal marker was cleared.");
            StorageTestSupport.AssertBytes(payload, persisted[0],
                "Malformed transaction journal marker was modified.");
        }
        finally
        {
            StorageTestSupport.DeleteRoot(root);
        }
    }

    private static void WriteTransactionJournalPayload(
        string root, DBreezeConfiguration configuration, byte[] payload)
    {
        var storage = new StorageLayer(
            Path.Combine(root, "_DBreezeTranJrnl"), new TrieSettings(), configuration);
        using (var journal = new DBreeze.LianaTrie.LTrie(storage))
        {
            journal.TableName = "DBreeze.TranJournal";
            byte[] key = { 0, 0, 0, 0, 0, 0, 0, 1 };
            journal.Add(ref key, ref payload);
            journal.Commit();
        }
    }

    private static byte[][] ReadTransactionJournalPayloads(
        string root, DBreezeConfiguration configuration)
    {
        var storage = new StorageLayer(
            Path.Combine(root, "_DBreezeTranJrnl"), new TrieSettings(), configuration);
        using (var journal = new DBreeze.LianaTrie.LTrie(storage))
        {
            journal.TableName = "DBreeze.TranJournal";
            var payloads = new List<byte[]>();
            foreach (var row in journal.IterateForward(true, false))
                payloads.Add(row.GetFullValue(true));
            return payloads.ToArray();
        }
    }

    private static bool ContainsException<TException>(Exception exception) where TException : Exception
    {
        while (exception != null)
        {
            if (exception is TException)
                return true;
            exception = exception.InnerException;
        }
        return false;
    }

    private static void AlternativeDiskAndMemoryContracts()
    {
        string root = StorageTestSupport.CreateRoot("storage-modes");
        string main = Path.Combine(root, "main");
        string alternative = Path.Combine(root, "alternative");
        try
        {
            using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
            {
                configuration.DBreezeDataFolderName = main;
                configuration.AlternativeTablesLocations.Add("alt*", alternative);
                configuration.AlternativeTablesLocations.Add("mem*", String.Empty);
                using (DBreezeEngine engine = new DBreezeEngine(configuration))
                {
                    using (DBreeze.Transactions.Transaction transaction = engine.GetTransaction())
                    {
                        transaction.SynchronizeTables("default_table", "alt_table");
                        transaction.Insert<int, int>("default_table", 1, 11);
                        transaction.Insert<int, int>("alt_table", 1, 22);
                        transaction.Commit();
                    }
                    using (DBreeze.Transactions.Transaction transaction = engine.GetTransaction())
                    {
                        transaction.Insert<int, int>("mem_table", 1, 33);
                        transaction.Commit();
                    }
                    using (DBreeze.Transactions.Transaction transaction = engine.GetTransaction())
                    {
                        transaction.Insert<int, int>("mem_table", 1, 44);
                        transaction.Rollback();
                        StorageTestSupport.Assert(transaction.Select<int, int>("mem_table", 1).Value == 33,
                            "MEMORY rollback contract failed in-process.");
                    }
                }
            }

            using (DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration())
            {
                configuration.DBreezeDataFolderName = main;
                configuration.AlternativeTablesLocations.Add("alt*", alternative);
                configuration.AlternativeTablesLocations.Add("mem*", String.Empty);
                using (DBreezeEngine engine = new DBreezeEngine(configuration))
                using (DBreeze.Transactions.Transaction transaction = engine.GetTransaction())
                {
                    StorageTestSupport.Assert(transaction.Select<int, int>("default_table", 1).Value == 11,
                        "Default DISK table did not reopen.");
                    StorageTestSupport.Assert(transaction.Select<int, int>("alt_table", 1).Value == 22,
                        "Alternative DISK table did not reopen.");
                    StorageTestSupport.Assert(!transaction.Select<int, int>("mem_table", 1).Exists,
                        "MEMORY table unexpectedly survived engine restart.");
                }
            }
        }
        finally
        {
            StorageTestSupport.DeleteRoot(root);
        }
    }

    private static void RemoteInstanceLoopbackContract()
    {
        string root = StorageTestSupport.CreateRoot("remote-loopback");
        try
        {
#if PORTABLE_HOST
            using (DBreezeConfiguration serverConfiguration = StorageTestSupport.CreateConfiguration())
            {
                serverConfiguration.DBreezeDataFolderName = root;
                using (RemoteTablesHandler server = new RemoteTablesHandler(serverConfiguration))
                    RunRemoteInstanceLoopback(server);
            }
#else
            using (RemoteTablesHandler server = new RemoteTablesHandler(root))
                RunRemoteInstanceLoopback(server);
#endif
        }
        finally
        {
            StorageTestSupport.DeleteRoot(root);
        }
    }

    private static void RunRemoteInstanceLoopback(RemoteTablesHandler server)
    {
            LoopbackCommunicator communicator = new LoopbackCommunicator(server);
            using (DBreezeRemoteEngine engine = CreateRemoteEngine(communicator))
            using (DBreeze.Transactions.Transaction transaction = engine.GetTransaction())
            {
                transaction.SynchronizeTables("remote_a", "remote_b");
                transaction.Insert<int, int>("remote_a", 1, 101);
                transaction.Insert<int, int>("remote_b", 1, 202);
                transaction.Commit();
            }

            using (DBreezeRemoteEngine engine = CreateRemoteEngine(communicator))
            {
                using (DBreeze.Transactions.Transaction transaction = engine.GetTransaction())
                {
                    StorageTestSupport.Assert(transaction.Select<int, int>("remote_a", 1).Value == 101,
                        "RemoteInstance durable commit did not reopen table A.");
                    StorageTestSupport.Assert(transaction.Select<int, int>("remote_b", 1).Value == 202,
                        "RemoteInstance durable commit did not reopen table B.");
                    transaction.SynchronizeTables("remote_a", "remote_b");
                    transaction.Insert<int, int>("remote_a", 1, 303);
                    transaction.Insert<int, int>("remote_b", 1, 404);
                    transaction.Rollback();
                }
                using (DBreeze.Transactions.Transaction verify = engine.GetTransaction())
                    StorageTestSupport.Assert(verify.Select<int, int>("remote_a", 1).Value == 101
                        && verify.Select<int, int>("remote_b", 1).Value == 202,
                        "RemoteInstance rollback contract failed.");
            }
            StorageTestSupport.Assert(communicator.Calls != 0, "Loopback communicator was not used.");
    }

    private static DBreezeRemoteEngine CreateRemoteEngine(IRemoteInstanceCommunicator communicator)
    {
        DBreezeConfiguration configuration = StorageTestSupport.CreateConfiguration();
        configuration.Storage = DBreezeConfiguration.eStorage.RemoteInstance;
        configuration.DBreezeDataFolderName = "remote_db";
        configuration.RICommunicator = communicator;
        return new DBreezeRemoteEngine(configuration);
    }

    private sealed class LoopbackCommunicator : IRemoteInstanceCommunicator
    {
        private readonly RemoteTablesHandler server;
        internal int Calls;

        internal LoopbackCommunicator(RemoteTablesHandler server)
        {
            this.server = server;
        }

        public byte[] Send(byte[] data)
        {
            Interlocked.Increment(ref Calls);
            return server.ParseProtocol(data);
        }
    }

    private static void BufferedWriteSetRandomizedModel()
    {
        Type type = typeof(StorageLayer).Assembly.GetType("DBreeze.Storage.BufferedWriteSet", true);
        object writeSet = Activator.CreateInstance(type, true);
        MethodInfo add = type.GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo overlay = type.GetMethod("Overlay", BindingFlags.Instance | BindingFlags.NonPublic);
        PropertyInfo count = type.GetProperty("Count", BindingFlags.Instance | BindingFlags.NonPublic);
        PropertyInfo item = type.GetProperty("Item", BindingFlags.Instance | BindingFlags.NonPublic);
        PropertyInfo operations = type.GetProperty("WriteOperations", BindingFlags.Instance | BindingFlags.NonPublic);
        StorageTestSupport.Assert(add != null && overlay != null && count != null && item != null && operations != null,
            "BufferedWriteSet internal contract is incomplete.");

        byte[] baseline = StorageTestSupport.Bytes(8192, 1601);
        byte[] oracle = (byte[])baseline.Clone();
        Random random = new Random(1602);
        int operationCount = 0;

        // Explicit nested, adjacent and full-cover cases before randomized pressure.
        AddModelWrite(writeSet, add, oracle, 1000, StorageTestSupport.Bytes(2000, 1603)); operationCount++;
        AddModelWrite(writeSet, add, oracle, 1250, StorageTestSupport.Bytes(300, 1604)); operationCount++;
        AddModelWrite(writeSet, add, oracle, 3000, StorageTestSupport.Bytes(512, 1605)); operationCount++;
        AddModelWrite(writeSet, add, oracle, 900, StorageTestSupport.Bytes(3000, 1606)); operationCount++;

        for (int index = 0; index < 5000; index++)
        {
            int offset = random.Next(0, oracle.Length);
            int maximum = Math.Min(1024, oracle.Length - offset);
            int length = random.Next(1, maximum + 1);
            byte[] data = new byte[length];
            random.NextBytes(data);
            AddModelWrite(writeSet, add, oracle, offset, data);
            operationCount++;

            if ((index & 31) == 0)
            {
                int readOffset = random.Next(0, oracle.Length);
                int readLength = random.Next(0, oracle.Length - readOffset + 1);
                byte[] actual = new byte[readLength];
                Buffer.BlockCopy(baseline, readOffset, actual, 0, readLength);
                overlay.Invoke(writeSet, new object[] { (long)readOffset, actual });
                byte[] expected = new byte[readLength];
                Buffer.BlockCopy(oracle, readOffset, expected, 0, readLength);
                StorageTestSupport.AssertBytes(expected, actual, "BufferedWriteSet overlay differs from byte-array oracle.");
                AssertSortedNonOverlapping(writeSet, count, item);
            }
        }

        StorageTestSupport.Assert((int)operations.GetValue(writeSet, null) == operationCount,
            "BufferedWriteSet lost incoming-operation accounting.");
        byte[] complete = (byte[])baseline.Clone();
        overlay.Invoke(writeSet, new object[] { 0L, complete });
        StorageTestSupport.AssertBytes(oracle, complete, "BufferedWriteSet final view differs from oracle.");
        AssertSortedNonOverlapping(writeSet, count, item);
    }

    private static void AddModelWrite(object writeSet, MethodInfo add, byte[] oracle, int offset, byte[] data)
    {
        add.Invoke(writeSet, new object[] { (long)offset, data });
        Buffer.BlockCopy(data, 0, oracle, offset, data.Length);
    }

    private static void AssertSortedNonOverlapping(object writeSet, PropertyInfo countProperty, PropertyInfo itemProperty)
    {
        int count = (int)countProperty.GetValue(writeSet, null);
        long previousEnd = -1;
        for (int index = 0; index < count; index++)
        {
            object segment = itemProperty.GetValue(writeSet, new object[] { index });
            Type segmentType = segment.GetType();
            long offset = (long)segmentType.GetField("Offset", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(segment);
            long end = (long)segmentType.GetField("End", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(segment);
            StorageTestSupport.Assert(offset >= previousEnd && end > offset,
                "BufferedWriteSet segments are not sorted and non-overlapping.");
            previousEnd = end;
        }
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
            {
                var recoveredTruncated = new StorageLayer(truncated, new TrieSettings(), configuration);
                recoveredTruncated.Table_Dispose();
            }
            StorageTestSupport.AssertBytes(StorageTestSupport.Int64BigEndian(0), File.ReadAllBytes(truncated + ".rhp"),
                "Legacy-compatible truncated rollback recovery did not clear its marker.");
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
