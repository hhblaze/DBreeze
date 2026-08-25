using System.Reflection;
using DBreeze;
using DBreeze.Storage;
using DBreeze.Storage.RemoteInstance;

internal static class StorageRegressionTests
{
    private static readonly string DatabaseTestRoot =
        Environment.GetEnvironmentVariable("DBREEZE_TEST_ROOT") ?? @"D:\Temp\DbreezeDbTest";
    public static void StorageViewsCommitRollbackAndAutoFlush()
    {
        RunStorageViewScenario(DBreezeConfiguration.eStorage.DISK);
        RunStorageViewScenario(DBreezeConfiguration.eStorage.MEMORY);
    }

    public static void CommittedReadCachesTrackStorageLifecycle()
    {
        string root = CreateFolder(nameof(CommittedReadCachesTrackStorageLifecycle));
        string tablePath = Path.Combine(root, "1");
        try
        {
            byte[] payload = new byte[64 * 1024];
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
                    "Second committed page admission read failed.");
                AssertBytes(localExpected, storage.Table_Read(true, start + 97, localExpected.Length),
                    "Admitted committed page read failed.");
                byte[] sharedBuffer = GetSharedReadBuffer(storage);
                Assert(sharedBuffer != null && sharedBuffer.Length == 8 * 1024,
                    "Small committed read did not allocate the shared 8 KiB buffer.");

                const int readPageSize = 32 * 1024;
                long crossPageOffset = ((start + readPageSize) / readPageSize * readPageSize) - 23;
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
                storage.Table_Read(true, start + 97, original.Length);
                storage.Table_Read(true, start + 97, original.Length);
                AssertBytes(original, storage.Table_Read(true, start + 97, original.Length),
                    "Committed page was not warmed before transactional invalidation.");
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
                Assert(ReferenceEquals(sharedBuffer, GetSharedReadBuffer(storage)),
                    "Commit replaced instead of invalidating the shared read buffer.");
                storage.Table_Read(true, start + 97, replacement.Length);
                storage.Table_Read(true, start + 97, replacement.Length);

                byte[] rolledBack = Enumerable.Repeat((byte)0x3C, 64).ToArray();
                storage.Table_WriteByOffset(start + 97, rolledBack);
                storage.TransactionalCommit();
                storage.TransactionalRollback();
                AssertBytes(replacement, storage.Table_Read(true, start + 97, replacement.Length),
                    "Rollback left a stale page in the committed cache.");
                Assert(ReferenceEquals(sharedBuffer, GetSharedReadBuffer(storage)),
                    "Rollback replaced instead of invalidating the shared read buffer.");
                storage.Table_Read(true, start + 97, replacement.Length);
                storage.Table_Read(true, start + 97, replacement.Length);
                storage.Table_Dispose();
                AssertSharedReadBuffer(storage, expectedAllocated: false,
                    "Disposed storage retained the shared read buffer.");
            }

            using (var reopenedConfiguration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK })
            {
                var reopened = new StorageLayer(tablePath, new TrieSettings(), reopenedConfiguration);
                AssertBytes(Enumerable.Repeat((byte)0xA5, 64).ToArray(), reopened.Table_Read(true, start + 97, 64),
                    "Reopen used a page from the previous FSR instance.");
                reopened.Table_Read(true, start + 97, 64);
                reopened.Table_Read(true, start + 97, 64);

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
                byte[] reopenedSharedBuffer = GetSharedReadBuffer(reopened);
                reopened.Table_Read(true, start + 97, 64);
                reopened.Table_Read(true, start + 97, 64);

                reopened.RecreateFiles();
                byte[] recreatedPayload = { 9, 8, 7, 6, 5 };
                long recreatedStart = DecodePointer(reopened.Table_WriteToTheEnd(recreatedPayload));
                reopened.Commit();
                AssertBytes(recreatedPayload, reopened.Table_Read(true, recreatedStart, recreatedPayload.Length),
                    "Recreate retained cached bytes from the old file.");
                Assert(ReferenceEquals(reopenedSharedBuffer, GetSharedReadBuffer(reopened)),
                    "Restore or recreate replaced instead of invalidating the shared read buffer.");
                reopened.Table_Dispose();
            }
        }
        finally
        {
            DeleteFolder(root);
        }
    }

    public static void CommittedReadCacheAdmissionIsLazy()
    {
        const int sharedReadBufferSize = 8 * 1024;
        const int readPageSize = 32 * 1024;
        string root = CreateFolder(nameof(CommittedReadCacheAdmissionIsLazy));
        try
        {
            using var configuration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK };
            byte[] payload = new byte[10 * readPageSize];
            new Random(9827).NextBytes(payload);
            var storage = new StorageLayer(Path.Combine(root, "10"), new TrieSettings(), configuration);
            long start = DecodePointer(storage.Table_WriteToTheEnd(payload));
            storage.Commit();
            long firstFullPage = AlignUp(start, readPageSize);
            Type fsrType = GetTableStorage(storage).GetType();

            AssertSharedReadBuffer(storage, expectedAllocated: false,
                "Opening committed storage eagerly allocated the shared read buffer.");
            long firstOffset = firstFullPage + 127;
            AssertBytes(payload.AsSpan(checked((int)(firstOffset - start)), 64).ToArray(),
                storage.Table_Read(true, firstOffset, 64),
                "The first shared-buffer read returned incorrect bytes.");
            byte[] sharedBuffer = GetSharedReadBuffer(storage);
            Assert(sharedBuffer != null && sharedBuffer.Length == sharedReadBufferSize,
                "The first small committed read did not allocate exactly 8 KiB.");
            Assert(GetSharedReadBufferLength(storage) == 4 * 1024,
                "A random single-reader miss did not use the bounded 4 KiB admission fill.");

            long hitOffset = firstOffset + 257;
            AssertBytes(payload.AsSpan(checked((int)(hitOffset - start)), 64).ToArray(),
                storage.Table_Read(true, hitOffset, 64),
                "A shared-buffer hit returned incorrect bytes.");
            Assert(ReferenceEquals(sharedBuffer, GetSharedReadBuffer(storage)),
                "A shared-buffer hit replaced the 8 KiB buffer.");

            long missOffset = firstOffset + sharedReadBufferSize + 17;
            AssertBytes(payload.AsSpan(checked((int)(missOffset - start)), 64).ToArray(),
                storage.Table_Read(true, missOffset, 64),
                "A shared-buffer miss returned incorrect bytes.");
            Assert(ReferenceEquals(sharedBuffer, GetSharedReadBuffer(storage)),
                "A shared-buffer miss allocated a second buffer.");
            Assert(GetSharedReadBufferLength(storage) == sharedReadBufferSize,
                "A local forward miss did not promote read-ahead to the full 8 KiB lane.");

            long nearEofOffset = start + payload.Length - 31;
            AssertBytes(payload.AsSpan(payload.Length - 31, 31).ToArray(),
                storage.Table_Read(true, nearEofOffset, 64),
                "A shared-buffer read near EOF was not truncated correctly.");

            RunOnFreshThread(() =>
            {
                long offset = firstFullPage + 512;
                const int largeReadSize = 9 * 1024;
                storage.Table_Read(true, offset, largeReadSize);
                AssertThreadPageCacheBuffer(fsrType, expectedAllocated: false,
                    "The first large read allocated the page buffer.");
                storage.Table_Read(true, offset + largeReadSize, largeReadSize);
                AssertThreadPageCacheBuffer(fsrType, expectedAllocated: true,
                    "The second same-page large read did not populate the page buffer.");
            });

            var mixedStorages = new StorageLayer[4];
            var mixedStarts = new long[mixedStorages.Length];
            var mixedPayloadStarts = new long[mixedStorages.Length];
            byte[] mixedPayload = new byte[2 * readPageSize];
            new Random(4181).NextBytes(mixedPayload);
            for (int i = 0; i < mixedStorages.Length; i++)
            {
                mixedStorages[i] = new StorageLayer(Path.Combine(root, (20 + i).ToString()), new TrieSettings(), configuration);
                long mixedStart = DecodePointer(mixedStorages[i].Table_WriteToTheEnd(mixedPayload));
                mixedStorages[i].Commit();
                mixedPayloadStarts[i] = mixedStart;
                mixedStarts[i] = AlignUp(mixedStart, readPageSize);
            }

            var mixedBuffers = new byte[mixedStorages.Length][];
            for (int i = 0; i < mixedStorages.Length; i++)
            {
                int payloadOffset = checked((int)(mixedStarts[i] + 97 - mixedPayloadStarts[i]));
                byte[] expected = mixedPayload.AsSpan(payloadOffset, 64).ToArray();
                AssertBytes(expected, mixedStorages[i].Table_Read(true, mixedStarts[i] + 97, 64),
                    "A mixed-table shared read returned incorrect bytes.");
                mixedBuffers[i] = GetSharedReadBuffer(mixedStorages[i]);
                Assert(mixedBuffers[i] != null, "A mixed-table read did not allocate its table-local buffer.");
                for (int preceding = 0; preceding < i; preceding++)
                    Assert(!ReferenceEquals(mixedBuffers[preceding], mixedBuffers[i]),
                        "Two FSR instances unexpectedly shared one read buffer.");
            }

            foreach (StorageLayer mixedStorage in mixedStorages)
                mixedStorage.Table_Dispose();
            storage.Table_Dispose();
        }
        finally
        {
            DeleteFolder(root);
        }
    }

    public static void CommittedReadCacheIsSafeDuringConcurrentCommits()
    {
        string root = CreateFolder(nameof(CommittedReadCacheIsSafeDuringConcurrentCommits));
        try
        {
            using var configuration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK };
            foreach (int readerCount in new[] { 2, 8 })
            {
                var storage = new StorageLayer(Path.Combine(root, readerCount.ToString()), new TrieSettings(), configuration);
                byte[] initial = new byte[64];
                long start = DecodePointer(storage.Table_WriteToTheEnd(initial));
                storage.Commit();
                storage.Table_Read(true, start, initial.Length);

                int finished = 0;
                using var startBarrier = new Barrier(readerCount + 1);
                Task writer = Task.Run(() =>
                {
                    startBarrier.SignalAndWait();
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

                Task[] readers = Enumerable.Range(0, readerCount).Select(_ => Task.Run(() =>
                {
                    startBarrier.SignalAndWait();
                    while (Volatile.Read(ref finished) == 0)
                    {
                        byte[] value = storage.Table_Read(true, start, 64);
                        byte generation = value[0];
                        if (value.AsSpan().IndexOfAnyExcept(generation) >= 0)
                            throw new InvalidOperationException("Concurrent committed read observed torn buffer contents.");
                    }
                })).ToArray();

                Task[] participants = readers.Append(writer).ToArray();
                Assert(Task.WaitAll(participants, TimeSpan.FromSeconds(60)),
                    $"Concurrent committed read test timed out with {readerCount} readers.");
                AssertBytes(Enumerable.Repeat((byte)100, 64).ToArray(), storage.Table_Read(true, start, 64),
                    "Concurrent commit did not publish its final generation.");
                storage.Table_Dispose();
            }
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

            string legacyBackup = Path.Combine(root, "legacy-backup");
            string legacyDestination = Path.Combine(root, "legacy-destination");
            Directory.CreateDirectory(legacyBackup);
            File.WriteAllBytes(
                Path.Combine(legacyBackup, "dbreeze_ibp_20000101000002.custom"),
                BuildBackupWriteRecord(1, 0, 64, new byte[] { 0x22 }));
            File.WriteAllBytes(
                Path.Combine(legacyBackup, "dbreeze_ibp_20000101000001"),
                BuildBackupWriteRecord(1, 0, 64, new byte[] { 0x11 }));
            File.WriteAllBytes(Path.Combine(legacyBackup, "unrelated-data.bin"), new byte[] { 0xFF });
            var legacyProgress = new List<int>();
            var legacyRestorer = new BackupRestorer
            {
                BackupFolder = legacyBackup,
                DataBaseFolder = legacyDestination,
            };
            legacyRestorer.OnRestore += progress =>
            {
                if (!progress.Finished)
                    legacyProgress.Add(progress.ReadinessInProcent);
            };
            legacyRestorer.StartRestoration();
            Assert(File.ReadAllBytes(Path.Combine(legacyDestination, "1"))[64] == 0x22,
                "Legacy backup files were not restored in ordinal filename order.");
            Assert(legacyProgress.Contains(50),
                "Unrelated files affected legacy backup restoration progress.");

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

    public static void RemoteStorageChunksLargeIoAndFinalFlush()
    {
        const int chunkSize = 1024 * 1024;
        string root = CreateFolder(nameof(RemoteStorageChunksLargeIoAndFinalFlush));
        using var handler = new RemoteTablesHandler(root);
        var communicator = new LimitingCommunicator(handler, chunkSize, 257 * 1024 + 17);

        try
        {
            byte[] payload = new byte[2 * chunkSize + 137];
            new Random(7419).NextBytes(payload);

            using (var configuration = CreateRemoteConfiguration(communicator))
            {
                var flushStorage = new StorageLayer(Path.Combine("flush", "1"), new TrieSettings(), configuration);
                object remoteStorage = typeof(StorageLayer)
                    .GetField("_tableStorage", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(flushStorage);
                communicator.Reset();
                Invoke(remoteStorage, "DataWriteExactly", payload, 0, payload.Length, true);
                Assert(communicator.DataWriteFlushes.SequenceEqual(new[] { false, false, true }),
                    "A chunked write did not flush only its final packet.");
                Assert(communicator.MaxProtocolArrayLength <= chunkSize + 19,
                    "A chunked write allocated an oversized protocol request.");
                flushStorage.Table_Dispose();
            }

            using (var configuration = CreateRemoteConfiguration(communicator))
            {
                var storage = new StorageLayer(Path.Combine("roundtrip", "2"), new TrieSettings(), configuration);
                communicator.Reset();
                long start = DecodePointer(storage.Table_WriteToTheEnd(payload));
                storage.Commit();

                Assert(communicator.DataWritePositions.SequenceEqual(new[]
                    {
                        start,
                        start + chunkSize,
                        start + 2L * chunkSize,
                    }), "Chunked remote writes used incorrect offsets.");
                Assert(communicator.DataWritePayloads.SequenceEqual(new[] { chunkSize, chunkSize, 137 }),
                    "Large remote write was not split into 1 MiB packets.");
                Assert(communicator.DataFlushCommands == 1,
                    "A committed large remote write did not issue exactly one final data flush.");
                Assert(communicator.MaxProtocolArrayLength <= chunkSize + 19,
                    "A large remote write created an oversized protocol array.");

                communicator.Reset();
                AssertBytes(payload, storage.Table_Read(true, start, payload.Length),
                    "Chunked partial remote reads changed the payload.");
                Assert(communicator.MaxReadRequest <= chunkSize,
                    "A remote read exceeded the 1 MiB client chunk limit.");
                Assert(communicator.DataReadPositions.Count > 3,
                    "The limiting communicator did not exercise partial remote reads.");
                for (int i = 1; i < communicator.DataReadPositions.Count; i++)
                {
                    Assert(communicator.DataReadPositions[i] ==
                           communicator.DataReadPositions[i - 1] + communicator.DataReadReturns[i - 1],
                        "Remote read position did not advance by the actual returned byte count.");
                }
                storage.Table_Dispose();
            }
        }
        finally
        {
            DeleteFolder(root);
        }
    }

    public static void RemoteHandlerContainsTablePaths()
    {
        string root = CreateFolder(nameof(RemoteHandlerContainsTablePaths));
        string siblingRoot = root + "-sibling";
        string parentEscape = Path.Combine(Path.GetDirectoryName(root), "remote-escape-" + Guid.NewGuid().ToString("N"));

        try
        {
            using var handler = new RemoteTablesHandler(root);
            byte[] opened = handler.ParseProtocol(BuildOpenProtocol(Path.Combine("nested", "valid")));
            Assert(opened.Length == 33 && opened[0] == 1,
                "A valid nested remote table was rejected.");
            ulong tableId = BitConverter.ToUInt64(opened, 1);
            AssertBytes(new byte[] { 1 }, handler.ParseProtocol(BuildTableCommand(2, tableId)),
                "A valid nested remote table could not be closed.");
            Assert(File.Exists(Path.Combine(root, "nested", "valid")),
                "A valid nested remote table was not created below the configured root.");

            string siblingRelative = Path.Combine("..", Path.GetFileName(siblingRoot), "escaped");
            AssertBytes(new byte[] { 255 }, handler.ParseProtocol(BuildOpenProtocol(Path.Combine("..", Path.GetFileName(parentEscape)))),
                "A parent-traversal remote path was accepted.");
            AssertBytes(new byte[] { 255 }, handler.ParseProtocol(BuildOpenProtocol(parentEscape)),
                "An absolute remote path was accepted.");
            AssertBytes(new byte[] { 255 }, handler.ParseProtocol(BuildOpenProtocol(siblingRelative)),
                "A sibling-prefix remote path was accepted.");
            AssertBytes(new byte[] { 255 }, handler.ParseProtocol(BuildOpenProtocol(new byte[] { 0xC3, 0x28 })),
                "An invalid UTF-8 remote table name was accepted.");
            Assert(!File.Exists(parentEscape) && !Directory.Exists(siblingRoot),
                "A rejected remote path created data outside the configured root.");
        }
        finally
        {
            DeleteFolder(root);
            DeleteFolder(siblingRoot);
            if (File.Exists(parentEscape))
                File.Delete(parentEscape);
            DeleteFolder(parentEscape);
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
            {
                var truncatedRecovery = new StorageLayer(tablePath, new TrieSettings(), configuration);
                truncatedRecovery.Table_Dispose();
            }
            AssertBytes(Int64BigEndian(0), File.ReadAllBytes(tablePath + ".rhp"),
                "Legacy-compatible truncated rollback recovery did not clear its marker.");

            string recoveryPath = Path.Combine(diskFolder, "3");
            byte[] diskOriginal = new byte[4096];
            new Random(6790).NextBytes(diskOriginal);
            long diskStart;
            using (var configuration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK })
            {
                var storage = new StorageLayer(recoveryPath, new TrieSettings(), configuration);
                diskStart = DecodePointer(storage.Table_WriteToTheEnd(diskOriginal));
                storage.Commit();
                for (int i = 0; i <= 500; i++)
                    storage.Table_WriteByOffset(diskStart + 512 + i, new byte[] { (byte)(diskOriginal[512 + i] ^ 0xFF) });
                storage.Table_WriteByOffset(diskStart + 700, Enumerable.Repeat((byte)0xA7, 128).ToArray());
                storage.TransactionalCommit();
                storage.Table_Dispose();
            }

            using (var configuration = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.DISK })
            {
                var recovered = new StorageLayer(recoveryPath, new TrieSettings(), configuration);
                AssertBytes(diskOriginal, recovered.Table_Read(true, diskStart, diskOriginal.Length),
                    "Disk crash recovery did not restore overlapping auto-flushed updates.");
                recovered.Table_Dispose();
            }

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

            byte[] exactSequentialBuffer = new byte[1024 * 1024];
            byte[] largerThanSequentialBuffer = new byte[1024 * 1024 + 1];
            new Random(18).NextBytes(exactSequentialBuffer);
            new Random(19).NextBytes(largerThanSequentialBuffer);
            long exactStart = DecodePointer(storage.Table_WriteToTheEnd(exactSequentialBuffer));
            long largerStart = DecodePointer(storage.Table_WriteToTheEnd(largerThanSequentialBuffer));
            Assert(largerStart == exactStart + exactSequentialBuffer.Length,
                "Sequential buffer boundary changed append offsets.");
            Assert(storage.Table_Read(true, exactStart, 1).Length == 0,
                "Committed view exposed an append around the sequential-buffer boundary.");
            AssertBytes(exactSequentialBuffer, storage.Table_Read(false, exactStart, exactSequentialBuffer.Length),
                "Writer view lost the exact-size sequential-buffer append.");
            AssertBytes(largerThanSequentialBuffer,
                storage.Table_Read(false, largerStart, largerThanSequentialBuffer.Length),
                "Writer view lost the append larger than the sequential buffer.");
            storage.Commit();
            AssertBytes(exactSequentialBuffer, storage.Table_Read(true, exactStart, exactSequentialBuffer.Length),
                "Commit changed the exact-size sequential-buffer append.");
            AssertBytes(largerThanSequentialBuffer,
                storage.Table_Read(true, largerStart, largerThanSequentialBuffer.Length),
                "Commit changed the append larger than the sequential buffer.");

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

    private static byte[] BuildOpenProtocol(string tableName) =>
        BuildOpenProtocol(System.Text.Encoding.UTF8.GetBytes(tableName));

    private static byte[] BuildOpenProtocol(byte[] tableName)
    {
        byte[] protocol = new byte[6 + tableName.Length];
        protocol[0] = 1;
        protocol[1] = 1;
        Buffer.BlockCopy(BitConverter.GetBytes(tableName.Length), 0, protocol, 2, 4);
        Buffer.BlockCopy(tableName, 0, protocol, 6, tableName.Length);
        return protocol;
    }

    private static byte[] BuildTableCommand(byte command, ulong tableId)
    {
        byte[] protocol = new byte[10];
        protocol[0] = 1;
        protocol[1] = command;
        Buffer.BlockCopy(BitConverter.GetBytes(tableId), 0, protocol, 2, 8);
        return protocol;
    }

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

    private static long AlignUp(long value, int alignment) =>
        checked((value + alignment - 1) / alignment * alignment);

    private static object GetTableStorage(StorageLayer storage) =>
        typeof(StorageLayer).GetField("_tableStorage", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(storage)
        ?? throw new InvalidOperationException("StorageLayer._tableStorage was not found.");

    private static void AssertSharedReadBuffer(StorageLayer storage, bool expectedAllocated, string message)
    {
        byte[] buffer = GetSharedReadBuffer(storage);
        Assert((buffer != null) == expectedAllocated, message);
        if (buffer != null)
            Assert(buffer.Length == 8 * 1024, "Shared read-buffer size changed.");
    }

    private static byte[] GetSharedReadBuffer(StorageLayer storage)
    {
        object tableStorage = GetTableStorage(storage);
        FieldInfo bufferField = tableStorage.GetType().GetField(
            "_sharedReadBuffer",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FSR._sharedReadBuffer was not found.");
        return (byte[])bufferField.GetValue(tableStorage);
    }

    private static int GetSharedReadBufferLength(StorageLayer storage)
    {
        object tableStorage = GetTableStorage(storage);
        FieldInfo lengthField = tableStorage.GetType().GetField(
            "_sharedReadBufferLength",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FSR._sharedReadBufferLength was not found.");
        return (int)lengthField.GetValue(tableStorage);
    }

    private static void AssertThreadPageCacheBuffer(Type fsrType, bool expectedAllocated, string message)
    {
        byte[] buffer = GetThreadPageCacheBuffer(fsrType);
        Assert((buffer != null) == expectedAllocated, message);
        if (buffer != null)
            Assert(buffer.Length == 32 * 1024, "Committed page-cache buffer size changed.");
    }

    private static byte[] GetThreadPageCacheBuffer(Type fsrType)
    {
        FieldInfo cacheField = fsrType.GetField(
            "_threadReadPageCache",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FSR._threadReadPageCache was not found.");
        object cache = cacheField.GetValue(null);
        Assert(cache != null, "Eligible committed read did not create page-cache admission metadata.");
        FieldInfo bufferField = cache.GetType().GetField(
            "Buffer",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FSR.ReadPageCache.Buffer was not found.");
        return (byte[])bufferField.GetValue(cache);
    }

    private static void RunOnFreshThread(Action action)
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.Start();
        thread.Join();
        if (failure != null)
            throw new InvalidOperationException("A dedicated page-cache test thread failed.", failure);
    }

    private static long FileSize(string path) => new FileInfo(path).Length;

    private static string CreateFolder(string scenario)
    {
        string path = Path.Combine(DatabaseTestRoot, scenario, Guid.NewGuid().ToString("N"));
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

    private sealed class LimitingCommunicator : LoopbackCommunicator
    {
        private readonly int _maxRequestPayload;
        private readonly int _maxResponsePayload;

        public readonly List<long> DataWritePositions = new();
        public readonly List<int> DataWritePayloads = new();
        public readonly List<bool> DataWriteFlushes = new();
        public readonly List<long> DataReadPositions = new();
        public readonly List<int> DataReadReturns = new();
        public int DataFlushCommands { get; private set; }
        public int MaxProtocolArrayLength { get; private set; }
        public int MaxReadRequest { get; private set; }

        public LimitingCommunicator(RemoteTablesHandler handler, int maxRequestPayload, int maxResponsePayload)
            : base(handler)
        {
            _maxRequestPayload = maxRequestPayload;
            _maxResponsePayload = maxResponsePayload;
        }

        public void Reset()
        {
            DataWritePositions.Clear();
            DataWritePayloads.Clear();
            DataWriteFlushes.Clear();
            DataReadPositions.Clear();
            DataReadReturns.Clear();
            DataFlushCommands = 0;
            MaxProtocolArrayLength = 0;
            MaxReadRequest = 0;
        }

        public override byte[] Send(byte[] data)
        {
            MaxProtocolArrayLength = Math.Max(MaxProtocolArrayLength, data.Length);
            byte command = data.Length > 1 ? data[1] : (byte)0;
            if (command >= 4 && command <= 6)
            {
                int payloadLength = data.Length - 19;
                if (payloadLength > _maxRequestPayload)
                    return new byte[] { 255 };
                if (command == 4)
                {
                    DataWritePositions.Add(BitConverter.ToInt64(data, 10));
                    DataWritePayloads.Add(payloadLength);
                    DataWriteFlushes.Add(data[18] == 1);
                }
            }
            else if (command >= 7 && command <= 9)
            {
                int count = BitConverter.ToInt32(data, 18);
                if (count > _maxRequestPayload)
                    return new byte[] { 255 };
                MaxReadRequest = Math.Max(MaxReadRequest, count);
                if (command == 7)
                    DataReadPositions.Add(BitConverter.ToInt64(data, 10));
            }
            else if (command == 10)
            {
                DataFlushCommands++;
            }

            byte[] response = base.Send(data);
            if (command < 7 || command > 9 || response.Length <= _maxResponsePayload + 1)
            {
                if (command == 7)
                    DataReadReturns.Add(response.Length - 1);
                return response;
            }

            byte[] fragment = new byte[_maxResponsePayload + 1];
            Buffer.BlockCopy(response, 0, fragment, 0, fragment.Length);
            if (command == 7)
                DataReadReturns.Add(_maxResponsePayload);
            return fragment;
        }
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
