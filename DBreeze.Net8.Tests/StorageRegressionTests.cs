using System.Reflection;
using DBreeze;
using DBreeze.Storage;
using DBreeze.Storage.RemoteInstance;

internal static class StorageRegressionTests
{
    public static void StorageViewsCommitRollbackAndAutoFlush()
    {
        RunStorageViewScenario(DBreezeConfiguration.eStorage.DISK);
        RunStorageViewScenario(DBreezeConfiguration.eStorage.MEMORY);
    }

    public static void CommittedPageCacheTracksStorageLifecycle()
    {
        string root = CreateFolder(nameof(CommittedPageCacheTracksStorageLifecycle));
        string tablePath = Path.Combine(root, "1");
        try
        {
            byte[] payload = new byte[24 * 1024];
            new Random(6143).NextBytes(payload);
            long start;

            using (var configuration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK })
            {
                var storage = new StorageLayer(tablePath, new TrieSettings(), configuration);
                start = DecodePointer(storage.Table_WriteToTheEnd(payload));
                storage.Commit();

                byte[] localExpected = payload.AsSpan(97, 64).ToArray();
                AssertBytes(localExpected, storage.Table_Read(true, start + 97, localExpected.Length),
                    "Initial committed page read failed.");
                AssertBytes(localExpected, storage.Table_Read(true, start + 97, localExpected.Length),
                    "Cached committed page read failed.");

                long crossPageOffset = ((start + 8192) / 8192 * 8192) - 23;
                int payloadOffset = checked((int)(crossPageOffset - start));
                byte[] crossExpected = payload.AsSpan(payloadOffset, 128).ToArray();
                AssertBytes(crossExpected, storage.Table_Read(true, crossPageOffset, crossExpected.Length),
                    "Cross-page committed read was truncated or reordered.");

                byte[] appended = Enumerable.Range(0, 257).Select(static value => (byte)value).ToArray();
                long appendOffset = DecodePointer(storage.Table_WriteToTheEnd(appended));
                Assert(storage.Table_Read(true, appendOffset, appended.Length).Length == 0,
                    "Committed view exposed an uncommitted append.");
                AssertBytes(appended, storage.Table_Read(false, appendOffset, appended.Length),
                    "Writer view missed a buffered append.");
                storage.Commit();
                AssertBytes(appended, storage.Table_Read(true, appendOffset, appended.Length),
                    "Commit did not advance the cached physical length.");

                byte[] original = payload.AsSpan(97, 64).ToArray();
                byte[] replacement = Enumerable.Repeat((byte)0xA5, 64).ToArray();
                storage.Table_WriteByOffset(start + 97, replacement);
                AssertBytes(original, storage.Table_Read(true, start + 97, replacement.Length),
                    "Committed cache exposed a buffered writer update.");
                AssertBytes(replacement, storage.Table_Read(false, start + 97, replacement.Length),
                    "Writer view did not expose its buffered update.");
                storage.TransactionalCommit();
                AssertBytes(original, storage.Table_Read(true, start + 97, replacement.Length),
                    "Transactional commit exposed data before commit-finished.");
                storage.TransactionalCommitIsFinished();
                AssertBytes(replacement, storage.Table_Read(true, start + 97, replacement.Length),
                    "Commit-finished did not invalidate the committed page.");

                byte[] rolledBack = Enumerable.Repeat((byte)0x3C, 64).ToArray();
                storage.Table_WriteByOffset(start + 97, rolledBack);
                storage.TransactionalCommit();
                storage.TransactionalRollback();
                AssertBytes(replacement, storage.Table_Read(true, start + 97, replacement.Length),
                    "Rollback left a stale page in the committed cache.");
                storage.Table_Dispose();
            }

            using (var reopenedConfiguration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK })
            {
                var reopened = new StorageLayer(tablePath, new TrieSettings(), reopenedConfiguration);
                AssertBytes(Enumerable.Repeat((byte)0xA5, 64).ToArray(), reopened.Table_Read(true, start + 97, 64),
                    "Reopen used a page from the previous FSR instance.");

                string sourcePath = Path.Combine(root, "2");
                byte[] restoredPayload = Enumerable.Repeat((byte)0x6D, payload.Length).ToArray();
                using (var sourceConfiguration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK })
                {
                    var source = new StorageLayer(sourcePath, new TrieSettings(), sourceConfiguration);
                    source.Table_WriteToTheEnd(restoredPayload);
                    source.Commit();
                    source.Table_Dispose();
                }

                reopened.RestoreTableFromTheOtherTable(sourcePath);
                AssertBytes(restoredPayload.AsSpan(97, 64).ToArray(), reopened.Table_Read(true, start + 97, 64),
                    "Restore reused a page from the replaced data file.");

                reopened.RecreateFiles();
                byte[] recreatedPayload = { 9, 8, 7, 6, 5 };
                long recreatedStart = DecodePointer(reopened.Table_WriteToTheEnd(recreatedPayload));
                reopened.Commit();
                AssertBytes(recreatedPayload, reopened.Table_Read(true, recreatedStart, recreatedPayload.Length),
                    "Recreate retained cached bytes from the old file.");
                reopened.Table_Dispose();
            }
        }
        finally
        {
            DeleteFolder(root);
        }
    }

    public static void CommittedPageCacheIsSafeDuringConcurrentCommits()
    {
        string root = CreateFolder(nameof(CommittedPageCacheIsSafeDuringConcurrentCommits));
        try
        {
            using var configuration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK };
            var storage = new StorageLayer(Path.Combine(root, "1"), new TrieSettings(), configuration);
            byte[] initial = new byte[64];
            long start = DecodePointer(storage.Table_WriteToTheEnd(initial));
            storage.Commit();
            storage.Table_Read(true, start, initial.Length);
            storage.Table_Read(true, start, initial.Length);

            int finished = 0;
            Task writer = Task.Run(() =>
            {
                try
                {
                    for (int generation = 1; generation <= 100; generation++)
                    {
                        storage.Table_WriteByOffset(start, Enumerable.Repeat((byte)generation, 64).ToArray());
                        storage.Commit();
                    }
                }
                finally
                {
                    Volatile.Write(ref finished, 1);
                }
            });

            Task[] readers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                while (Volatile.Read(ref finished) == 0)
                {
                    byte[] value = storage.Table_Read(true, start, 64);
                    byte generation = value[0];
                    if (value.AsSpan().IndexOfAnyExcept(generation) >= 0)
                        throw new InvalidOperationException("Concurrent committed read observed torn page contents.");
                }
            })).ToArray();

            Task.WaitAll(readers.Append(writer).ToArray());
            AssertBytes(Enumerable.Repeat((byte)100, 64).ToArray(), storage.Table_Read(true, start, 64),
                "Concurrent commit did not publish its final generation.");
            storage.Table_Dispose();
        }
        finally
        {
            DeleteFolder(root);
        }
    }

    public static void BackupRestoreStreamsAndRejectsTruncation()
    {
        string root = CreateFolder(nameof(BackupRestoreStreamsAndRejectsTruncation));
        string source = Path.Combine(root, "source");
        string restored = Path.Combine(root, "restored");
        string backup = Path.Combine(root, "backup");
        Directory.CreateDirectory(source);

        try
        {
            byte[] payload = new byte[1024 * 1024 + 137];
            new Random(12345).NextBytes(payload);

            using (var configuration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK })
            {
                configuration.Backup.BackupFolderName = backup;
                var storage = new StorageLayer(Path.Combine(source, "1"), new TrieSettings(), configuration);
                storage.Table_WriteToTheEnd(payload);
                storage.Commit();
                storage.Table_Dispose();
            }

            string[] backupFiles = Directory.GetFiles(backup, "dbreeze_ibp_*.ibp");
            Assert(backupFiles.Length != 0, "Incremental backup file was not created.");
            Assert(backupFiles.Sum(FileSize) > payload.Length,
                "A direct append larger than 1 MiB was omitted from incremental backup.");
            byte[] backupBytes = File.ReadAllBytes(backupFiles[0]);
            Assert(backupBytes.Length >= 21 && backupBytes[12] <= 2 && backupBytes[13] == 0x80 &&
                   backupBytes.AsSpan(14, 7).IndexOfAnyExcept((byte)0) < 0,
                "Incremental backup changed the legacy signed Int64 offset encoding.");

            File.WriteAllBytes(Path.Combine(backup, "unrelated-large-file.bin"), new byte[2 * 1024 * 1024]);
            new BackupRestorer { BackupFolder = backup, DataBaseFolder = restored }.StartRestoration();
            AssertBytes(File.ReadAllBytes(Path.Combine(source, "1")), File.ReadAllBytes(Path.Combine(restored, "1")),
                "Streaming backup round-trip changed the data file.");

            string malformedBackup = Path.Combine(root, "malformed-backup");
            string malformedDestination = Path.Combine(root, "malformed-destination");
            Directory.CreateDirectory(malformedBackup);
            byte[] malformed = BuildBackupWriteRecord(1, 0, 0, new byte[] { 7 });
            Array.Resize(ref malformed, malformed.Length + 6);
            WriteUInt32BigEndian(malformed, malformed.Length - 6, 20);
            File.WriteAllBytes(Path.Combine(malformedBackup, "dbreeze_ibp_20000101000000.ibp"), malformed);

            AssertThrows<InvalidDataException>(() => new BackupRestorer
            {
                BackupFolder = malformedBackup,
                DataBaseFolder = malformedDestination,
            }.StartRestoration());

            string partiallyRestored = Path.Combine(malformedDestination, "1");
            using (new FileStream(partiallyRestored, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }

            string deleteBackup = Path.Combine(root, "delete-backup");
            string deleteDestination = Path.Combine(root, "delete-destination");
            Directory.CreateDirectory(deleteBackup);
            Directory.CreateDirectory(deleteDestination);
            File.WriteAllBytes(Path.Combine(deleteDestination, "1"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(deleteDestination, "1.rol"), new byte[] { 2 });
            File.WriteAllBytes(Path.Combine(deleteDestination, "1.rhp"), new byte[] { 3 });
            File.WriteAllBytes(Path.Combine(deleteBackup, "dbreeze_ibp_20000101000000.ibp"), BuildBackupCommand(1, 5));
            new BackupRestorer { BackupFolder = deleteBackup, DataBaseFolder = deleteDestination }.StartRestoration();
            Assert(!File.Exists(Path.Combine(deleteDestination, "1")) &&
                   !File.Exists(Path.Combine(deleteDestination, "1.rol")) &&
                   !File.Exists(Path.Combine(deleteDestination, "1.rhp")),
                "Backup delete command left table files on disk.");
        }
        finally
        {
            DeleteFolder(root);
        }
    }

    public static void RemoteStorageKeepsSharedTablesAliveAndHonorsOffsets()
    {
        string root = CreateFolder(nameof(RemoteStorageKeepsSharedTablesAliveAndHonorsOffsets));
        using var handler = new RemoteTablesHandler(root);
        var communicator = new LoopbackCommunicator(handler);

        try
        {
            using var firstConfiguration = CreateRemoteConfiguration(communicator);
            using var secondConfiguration = CreateRemoteConfiguration(communicator);
            var first = new StorageLayer(Path.Combine("nested", "42"), new TrieSettings(), firstConfiguration);
            var second = new StorageLayer(Path.Combine("nested", "42"), new TrieSettings(), secondConfiguration);

            first.Table_Dispose();
            byte[] value = { 10, 20, 30, 40 };
            long position = DecodePointer(second.Table_WriteToTheEnd(value));
            second.Commit();
            AssertBytes(value, second.Table_Read(true, position, value.Length),
                "Closing one remote client closed the shared table for another client.");
            second.Table_Dispose();
            Assert(Directory.Exists(Path.Combine(root, "nested")), "Remote table did not create its nested directory.");

            object commander = CreateCommander(communicator);
            Invoke(commander, "OpenRemoteTable", Path.Combine("nested", "43"));
            byte[] source = { 99, 98, 1, 2, 3, 97 };
            Invoke(commander, "DataFileWrite", source, 2, 3, false);
            SetProperty(commander, "DataFilePosition", 0L);
            byte[] destination = Enumerable.Repeat((byte)0xCC, 10).ToArray();
            int read = (int)Invoke(commander, "DataFileRead", destination, 4, 3);
            Assert(read == 3, "Remote commander returned an incorrect read count.");
            AssertBytes(new byte[] { 1, 2, 3 }, destination.AsSpan(4, 3).ToArray(),
                "Remote commander ignored the caller buffer offset.");
            SetProperty(commander, "DataFilePosition", 2L);
            read = (int)Invoke(commander, "DataFileRead", destination, 0, 8);
            Assert(read == 1, "Remote commander did not return the actual EOF-limited count.");
            Invoke(commander, "CloseRemoteTable");
            Invoke(commander, "CloseRemoteTable");

            AssertBytes(new byte[] { 255 }, handler.ParseProtocol(new byte[] { 1, 7 }),
                "Malformed remote packet did not return the stable protocol error.");
            AssertBytes(new byte[] { 255 }, handler.ParseProtocol(new byte[] { 9, 1 }),
                "Unknown remote protocol version did not return the stable protocol error.");
        }
        finally
        {
            handler.Dispose();
            handler.Dispose();
            DeleteFolder(root);
        }
    }

    public static void RollbackRecoveryIsBoundedAndExact()
    {
        string root = CreateFolder(nameof(RollbackRecoveryIsBoundedAndExact));
        string diskFolder = Path.Combine(root, "disk");
        Directory.CreateDirectory(diskFolder);

        try
        {
            string tablePath = Path.Combine(diskFolder, "1");
            using (var configuration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK })
            {
                var storage = new StorageLayer(tablePath, new TrieSettings(), configuration);
                storage.Commit();
                storage.Table_Dispose();
            }

            File.WriteAllBytes(tablePath + ".rol", new byte[] { 1, 0, 0, 0, 0 });
            File.WriteAllBytes(tablePath + ".rhp", Int64BigEndian(5));
            using (var configuration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK })
                AssertThrows<Exception>(() => new StorageLayer(tablePath, new TrieSettings(), configuration));

            string remoteRoot = Path.Combine(root, "remote");
            using var handler = new RemoteTablesHandler(remoteRoot);
            var normal = new LoopbackCommunicator(handler);
            byte[] original = new byte[2048];
            new Random(6789).NextBytes(original);

            using (var configuration = CreateRemoteConfiguration(normal))
            {
                var storage = new StorageLayer("2", new TrieSettings(), configuration);
                long start = DecodePointer(storage.Table_WriteToTheEnd(original));
                storage.Commit();
                for (int i = 0; i < 1000; i++)
                    storage.Table_WriteByOffset(start + i, new byte[] { (byte)(original[i] ^ 0xFF) });
                storage.TransactionalCommit();
                storage.Table_Dispose(); // simulated crash: durable marker remains non-zero
            }

            using (var configuration = CreateRemoteConfiguration(new FragmentingCommunicator(handler, 2)))
            {
                var recovered = new StorageLayer("2", new TrieSettings(), configuration);
                AssertBytes(original, recovered.Table_Read(true, 64, original.Length),
                    "Fragmented exact-read recovery did not restore all rollback records.");
                recovered.Table_Dispose();
            }
        }
        finally
        {
            DeleteFolder(root);
        }
    }

    public static void RestoreMissingSourceKeepsDestination()
    {
        string root = CreateFolder(nameof(RestoreMissingSourceKeepsDestination));
        string tablePath = Path.Combine(root, "1");

        try
        {
            using var configuration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK };
            var storage = new StorageLayer(tablePath, new TrieSettings(), configuration);
            byte[] value = { 3, 1, 4, 1, 5 };
            long offset = DecodePointer(storage.Table_WriteToTheEnd(value));
            storage.Commit();

            AssertThrows<FileNotFoundException>(() =>
                storage.RestoreTableFromTheOtherTable(Path.Combine(root, "missing")));
            AssertBytes(value, storage.Table_Read(true, offset, value.Length),
                "Missing restore source changed the destination table.");
            storage.Table_Dispose();

            using var memoryConfiguration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.MEMORY };
            var memory = new StorageLayer("memory", new TrieSettings(), memoryConfiguration);
            AssertThrows<NotSupportedException>(() => memory.RestoreTableFromTheOtherTable("anything"));
            memory.Table_Dispose();
        }
        finally
        {
            DeleteFolder(root);
        }
    }

    public static void InvalidStorageSettingsFailBeforeCreatingFiles()
    {
        string root = CreateFolder(nameof(InvalidStorageSettingsFailBeforeCreatingFiles));
        string tablePath = Path.Combine(root, "1");

        try
        {
            using var configuration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK };
            AssertThrows<ArgumentOutOfRangeException>(() => new StorageLayer(
                tablePath, new TrieSettings { POINTER_LENGTH = 0 }, configuration));
            Assert(!File.Exists(tablePath) && !File.Exists(tablePath + ".rol") && !File.Exists(tablePath + ".rhp"),
                "Invalid pointer settings created storage files before failing.");

            configuration.Storage = (DBreezeConfiguration.eStorage)123;
            AssertThrows<ArgumentOutOfRangeException>(() => new StorageLayer(tablePath, new TrieSettings(), configuration));
            Assert(!File.Exists(tablePath), "Invalid storage kind created a table file before failing.");
        }
        finally
        {
            DeleteFolder(root);
        }
    }

    private static void RunStorageViewScenario(DBreezeConfiguration.eStorage kind)
    {
        string root = CreateFolder("views-" + kind);
        try
        {
            using var configuration = new DBreezeConfiguration { Storage = kind };
            var storage = new StorageLayer(Path.Combine(root, "1"), new TrieSettings(), configuration);
            byte[] original = new byte[2048];
            new Random(17).NextBytes(original);
            long start = DecodePointer(storage.Table_WriteToTheEnd(original));
            storage.Commit();

            byte[] first = { 11, 12, 13 };
            storage.Table_WriteByOffset(start, first);
            AssertBytes(first, storage.Table_Read(false, start, first.Length), "Writer view missed a buffered update.");
            AssertBytes(original.AsSpan(0, first.Length).ToArray(), storage.Table_Read(true, start, first.Length),
                "Committed view observed an uncommitted update.");

            for (int i = 0; i < 500; i++)
                storage.Table_WriteByOffset(start + 256 + i, new byte[] { (byte)i });

            byte[] grown = { 21, 22, 23, 24, 25 };
            storage.Table_WriteByOffset(start, grown);
            AssertBytes(grown, storage.Table_Read(false, start, grown.Length),
                "Writer view missed a grown replacement after auto-flush.");
            AssertBytes(original.AsSpan(0, grown.Length).ToArray(), storage.Table_Read(true, start, grown.Length),
                "Rollback coverage was lost when a replacement grew after auto-flush.");

            storage.TransactionalCommit();
            AssertBytes(original.AsSpan(0, grown.Length).ToArray(), storage.Table_Read(true, start, grown.Length),
                "Committed view read overwritten data instead of rollback storage.");
            storage.TransactionalRollback();
            AssertBytes(original.AsSpan(0, grown.Length).ToArray(), storage.Table_Read(false, start, grown.Length),
                "Transactional rollback did not restore the original bytes.");

            storage.Table_WriteByOffset(start, grown);
            storage.TransactionalCommit();
            storage.TransactionalCommitIsFinished();
            AssertBytes(grown, storage.Table_Read(true, start, grown.Length),
                "Finished transaction did not expose committed bytes.");
            storage.Table_Dispose();
        }
        finally
        {
            DeleteFolder(root);
        }
    }

    private static DBreezeConfiguration CreateRemoteConfiguration(IRemoteInstanceCommunicator communicator) => new()
    {
        Storage = DBreezeConfiguration.eStorage.RemoteInstance,
        RICommunicator = communicator,
    };

    private static object CreateCommander(IRemoteInstanceCommunicator communicator)
    {
        Type type = typeof(RemoteTablesHandler).Assembly.GetType(
            "DBreeze.Storage.RemoteInstance.RemoteInstanceCommander", throwOnError: true);
        return Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: new object[] { communicator }, culture: null);
    }

    private static object Invoke(object target, string method, params object[] args)
    {
        try
        {
            return target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Invoke(target, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static void SetProperty(object target, string property, object value) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SetValue(target, value);

    private static byte[] BuildBackupWriteRecord(ulong fileNumber, byte type, long offset, byte[] payload)
    {
        byte[] record = new byte[4 + 17 + payload.Length];
        WriteUInt32BigEndian(record, 0, (uint)(17 + payload.Length));
        WriteUInt64BigEndian(record, 4, fileNumber);
        record[12] = type;
        WriteUInt64BigEndian(record, 13, unchecked((ulong)offset) ^ 0x8000000000000000UL);
        Buffer.BlockCopy(payload, 0, record, 21, payload.Length);
        return record;
    }

    private static byte[] BuildBackupCommand(ulong fileNumber, byte type)
    {
        byte[] record = new byte[13];
        WriteUInt32BigEndian(record, 0, 9);
        WriteUInt64BigEndian(record, 4, fileNumber);
        record[12] = type;
        return record;
    }

    private static void WriteUInt32BigEndian(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }

    private static void WriteUInt64BigEndian(byte[] data, int offset, ulong value)
    {
        for (int i = 7; i >= 0; i--)
        {
            data[offset + i] = (byte)value;
            value >>= 8;
        }
    }

    private static byte[] Int64BigEndian(long value)
    {
        byte[] data = new byte[8];
        WriteUInt64BigEndian(data, 0, unchecked((ulong)value) ^ 0x8000000000000000UL);
        return data;
    }

    private static long DecodePointer(byte[] pointer)
    {
        ulong value = 0;
        foreach (byte item in pointer)
            value = (value << 8) | item;
        return checked((long)value);
    }

    private static long FileSize(string path) => new FileInfo(path).Length;

    private static string CreateFolder(string scenario)
    {
        string path = Path.Combine(Path.GetTempPath(), "DBreeze-StorageTests", scenario + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteFolder(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertBytes(byte[] expected, byte[] actual, string message)
    {
        if (expected == null || actual == null || !expected.AsSpan().SequenceEqual(actual))
            throw new InvalidOperationException(message);
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException("Expected " + typeof(TException).Name + " was not thrown.");
    }

    private class LoopbackCommunicator : IRemoteInstanceCommunicator
    {
        protected readonly RemoteTablesHandler Handler;

        public LoopbackCommunicator(RemoteTablesHandler handler) => Handler = handler;

        public virtual byte[] Send(byte[] data) => Handler.ParseProtocol(data);
    }

    private sealed class FragmentingCommunicator : LoopbackCommunicator
    {
        private readonly int _maxPayload;

        public FragmentingCommunicator(RemoteTablesHandler handler, int maxPayload) : base(handler) =>
            _maxPayload = maxPayload;

        public override byte[] Send(byte[] data)
        {
            byte[] response = base.Send(data);
            if (data.Length < 2 || data[1] < 7 || data[1] > 9 || response.Length <= _maxPayload + 1)
                return response;

            byte[] fragment = new byte[_maxPayload + 1];
            Buffer.BlockCopy(response, 0, fragment, 0, fragment.Length);
            return fragment;
        }
    }
}
