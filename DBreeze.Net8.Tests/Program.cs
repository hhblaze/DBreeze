using DBreeze;
using DBreeze.Storage.RemoteInstance;
using DBreeze.Utils;

internal static class Program
{
    private static readonly IComparer<byte[]> ByteComparer = new LexicographicByteComparer();
    private static readonly string DatabaseTestRoot = @"D:\Temp\DbreezeDbTest";

    private static int Main(string[] args)
    {
        if (args.Any(static arg => String.Equals(arg, "--textsearch-large-batch", StringComparison.OrdinalIgnoreCase)))
        {
            TextSearchRegressionTests.LargeLexicalBatchFlushesAndReopens();
            Console.WriteLine($"PASS {nameof(TextSearchRegressionTests.LargeLexicalBatchFlushesAndReopens)}");
            return 0;
        }

        (string Name, Action Test)[] tests =
        {
            // This test injects a durable journal marker directly and therefore must run before
            // the legacy process-global in-memory journal has been created and disposed.
            (nameof(JournalPayloadAndCrashRecoveryRemainCompatible), JournalPayloadAndCrashRecoveryRemainCompatible),
            (nameof(ParallelMultiTableCommitsRemainDurable), ParallelMultiTableCommitsRemainDurable),
            (nameof(EngineLifecycleIsSafe), EngineLifecycleIsSafe),
            (nameof(RemoteInitializationFailureIsTerminal), RemoteInitializationFailureIsTerminal),
            (nameof(StorageRegressionTests.StorageViewsCommitRollbackAndAutoFlush), StorageRegressionTests.StorageViewsCommitRollbackAndAutoFlush),
            (nameof(StorageRegressionTests.CommittedPageCacheTracksStorageLifecycle), StorageRegressionTests.CommittedPageCacheTracksStorageLifecycle),
            (nameof(StorageRegressionTests.CommittedPageCacheAdmissionIsLazy), StorageRegressionTests.CommittedPageCacheAdmissionIsLazy),
            (nameof(StorageRegressionTests.CommittedPageCacheIsSafeDuringConcurrentCommits), StorageRegressionTests.CommittedPageCacheIsSafeDuringConcurrentCommits),
            (nameof(StorageRegressionTests.BackupRestoreStreamsAndRejectsTruncation), StorageRegressionTests.BackupRestoreStreamsAndRejectsTruncation),
            (nameof(StorageRegressionTests.RemoteStorageKeepsSharedTablesAliveAndHonorsOffsets), StorageRegressionTests.RemoteStorageKeepsSharedTablesAliveAndHonorsOffsets),
            (nameof(StorageRegressionTests.RemoteStorageChunksLargeIoAndFinalFlush), StorageRegressionTests.RemoteStorageChunksLargeIoAndFinalFlush),
            (nameof(StorageRegressionTests.RemoteHandlerContainsTablePaths), StorageRegressionTests.RemoteHandlerContainsTablePaths),
            (nameof(StorageRegressionTests.RollbackRecoveryIsBoundedAndExact), StorageRegressionTests.RollbackRecoveryIsBoundedAndExact),
            (nameof(StorageRegressionTests.RestoreMissingSourceKeepsDestination), StorageRegressionTests.RestoreMissingSourceKeepsDestination),
            (nameof(StorageRegressionTests.InvalidStorageSettingsFailBeforeCreatingFiles), StorageRegressionTests.InvalidStorageSettingsFailBeforeCreatingFiles),
            (nameof(TextSearchRegressionTests.SynchronousIndexingRoundTrips), TextSearchRegressionTests.SynchronousIndexingRoundTrips),
            (nameof(TextSearchRegressionTests.WabiEnumerationAndMergesMatchReferenceModel), TextSearchRegressionTests.WabiEnumerationAndMergesMatchReferenceModel),
            (nameof(TextSearchRegressionTests.InvalidParserConfigurationFailsEarly), TextSearchRegressionTests.InvalidParserConfigurationFailsEarly),
            (nameof(TextSearchRegressionTests.CompositionHandlesMissingTermsAndReusableBlocks), TextSearchRegressionTests.CompositionHandlesMissingTermsAndReusableBlocks),
            (nameof(TextSearchRegressionTests.QueryParametersAreSinglePassAndTableScoped), TextSearchRegressionTests.QueryParametersAreSinglePassAndTableScoped),
            (nameof(TextSearchRegressionTests.ExternalRangesAreBoundedAndCanBeOneSided), TextSearchRegressionTests.ExternalRangesAreBoundedAndCanBeOneSided),
            (nameof(TextSearchRegressionTests.MutationsRemoveEmptyWordsAndBlocks), TextSearchRegressionTests.MutationsRemoveEmptyWordsAndBlocks),
            (nameof(TextSearchRegressionTests.CryptoVectorsAndEncryptedSearchRemainCompatible), TextSearchRegressionTests.CryptoVectorsAndEncryptedSearchRemainCompatible),
            (nameof(TextSearchRegressionTests.LexicalWordBatchesPreserveTriePrefixLocality), TextSearchRegressionTests.LexicalWordBatchesPreserveTriePrefixLocality),
            (nameof(TextSearchRegressionTests.MigrationValidatesAndIndexesPendingRows), TextSearchRegressionTests.MigrationValidatesAndIndexesPendingRows),
            (nameof(TextSearchRegressionTests.RandomizedCompositionMatchesSetModel), TextSearchRegressionTests.RandomizedCompositionMatchesSetModel),
            (nameof(TextSearchRegressionTests.DiskIndexReopensAndUpdates), TextSearchRegressionTests.DiskIndexReopensAndUpdates),
            (nameof(DeferredIndexerRunsInParallelAndCoalescesStarts), DeferredIndexerRunsInParallelAndCoalescesStarts),
            (nameof(DeferredIndexerShutdownPreservesPendingRows), DeferredIndexerShutdownPreservesPendingRows),
            (nameof(DeferredIndexerFailureParksDurableBatch), DeferredIndexerFailureParksDurableBatch),
            (nameof(DeferredIndexerSequenceAndDiskFormatRemainCompatible), DeferredIndexerSequenceAndDiskFormatRemainCompatible),
            (nameof(ResourcesKeepCacheAndStorageCoherent), ResourcesKeepCacheAndStorageCoherent),
            (nameof(ResourcesPersistNullAfterNegativeCache), ResourcesPersistNullAfterNegativeCache),
            (nameof(ResourcesPreserveEmptyArraysAndActiveSnapshots), ResourcesPreserveEmptyArraysAndActiveSnapshots),
            (nameof(ResourcesRemainCoherentUnderConcurrentWrites), ResourcesRemainCoherentUnderConcurrentWrites),
            (nameof(ResourcesRefreshCommittedReadRoots), ResourcesRefreshCommittedReadRoots),
            (nameof(ResourcesKeepCommittedReadRootsExclusive), ResourcesKeepCommittedReadRootsExclusive),
            (nameof(SchemeCommittedReadsRemainCoherent), SchemeCommittedReadsRemainCoherent),
            (nameof(SchemeRenamePreservesDataAndReplacementSemantics), SchemeRenamePreservesDataAndReplacementSemantics),
            (nameof(SchemeRenameReplacesDiskDestination), SchemeRenameReplacesDiskDestination),
            (nameof(SchemeRenameRejectsStorageRouteChanges), SchemeRenameRejectsStorageRouteChanges),
            (nameof(SchemeRenameWaitsForActiveTable), SchemeRenameWaitsForActiveTable),
            (nameof(RemoveAllResetsEmptyKeyState), RemoveAllResetsEmptyKeyState),
            (nameof(LianaTrieRegressionTests.RemoveAllWithFileRecreationKeepsTableReusable), LianaTrieRegressionTests.RemoveAllWithFileRecreationKeepsTableReusable),
            (nameof(LianaTrieRegressionTests.TraversalContractMatchesReferenceModel), LianaTrieRegressionTests.TraversalContractMatchesReferenceModel),
            (nameof(InsertIfAbsentPreservesNestedTable), InsertIfAbsentPreservesNestedTable),
            (nameof(NestedStructuralKeyCacheSurvivesMutationAndRename), NestedStructuralKeyCacheSurvivesMutationAndRename),
            (nameof(PartialValueRangesAreOverflowSafe), PartialValueRangesAreOverflowSafe),
            (nameof(RandomKeySorterKeepsFinalOperation), RandomKeySorterKeepsFinalOperation),
            (nameof(RandomKeySorterRollbackDropsPendingOperations), RandomKeySorterRollbackDropsPendingOperations),
            (nameof(RandomKeySorterUsesValueConversionAndNeverAutoFlushes), RandomKeySorterUsesValueConversionAndNeverAutoFlushes),
            (nameof(RandomKeySorterBorrowsValuesUntilFlush), RandomKeySorterBorrowsValuesUntilFlush),
            (nameof(RandomKeySorterSupportsFlushRollbackAndRepeatedCommits), RandomKeySorterSupportsFlushRollbackAndRepeatedCommits),
            (nameof(ObjectInsertNewEntityDoesNotDependOnRksLimit), ObjectInsertNewEntityDoesNotDependOnRksLimit),
            (nameof(ObjectIdentityRemainsBufferedUntilCommit), ObjectIdentityRemainsBufferedUntilCommit),
            (nameof(SelectDirectOnMissingTableIsEmpty), SelectDirectOnMissingTableIsEmpty),
            (nameof(MutationsAreRejectedOnAnotherThread), MutationsAreRejectedOnAnotherThread),
            (nameof(CoordinatorDoesNotLoseWakeups), CoordinatorDoesNotLoseWakeups),
            (nameof(MultiSelectMergesAndKeepsTieOrder), MultiSelectMergesAndKeepsTieOrder),
            (nameof(MultiSelectRejectsVariableLengthKeys), MultiSelectRejectsVariableLengthKeys),
            (nameof(LockedTransactionsRespectExclusiveWaiter), LockedTransactionsRespectExclusiveWaiter),
            (nameof(LockedTransactionCanBeDisposedOnAnotherThread), LockedTransactionCanBeDisposedOnAnotherThread),
            (nameof(DictionaryAndHashSetReplacementRemoveMissingKeys), DictionaryAndHashSetReplacementRemoveMissingKeys),
            (nameof(CollectionReplacementUsesDatabaseKeyEquality), CollectionReplacementUsesDatabaseKeyEquality),
            (nameof(ReadRootRefreshesAfterRapidCommits), ReadRootRefreshesAfterRapidCommits),
            (nameof(ReadVisibilityUsesCommittedNodeImages), ReadVisibilityUsesCommittedNodeImages),
            (nameof(RangeIterationStopsAtItsBoundary), RangeIterationStopsAtItsBoundary),
            (nameof(SkipTraversalMatchesReferenceModel), SkipTraversalMatchesReferenceModel),
            (nameof(DeepFullScanDoesNotUseTheCallStack), DeepFullScanDoesNotUseTheCallStack),
            (nameof(DiskDatabaseReopensWithTheSameRows), DiskDatabaseReopensWithTheSameRows),
            (nameof(AlternativeDiskTableReopens), AlternativeDiskTableReopens),
            (nameof(RandomizedOperationsMatchReferenceModel), RandomizedOperationsMatchReferenceModel),
            // Deadlock cancellation exercises forced rollback of concurrent writers. Keep it
            // last because the legacy in-memory journal storage is process-global.
            (nameof(CoordinatorCleansUpNotifyAheadFailure), CoordinatorCleansUpNotifyAheadFailure),
            (nameof(CoordinatorDetectsThreeWayDeadlock), CoordinatorDetectsThreeWayDeadlock),
        };

        try
        {
            foreach (var test in tests)
            {
                test.Test();
                Console.WriteLine($"PASS {test.Name}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static DBreezeEngine CreateMemoryEngine()
    {
        return new DBreezeEngine(new DBreezeConfiguration
        {
            Storage = DBreezeConfiguration.eStorage.MEMORY,
            NotifyAhead_WhenWriteTablePossibleDeadlock = false,
        });
    }

    private static string CreateDatabaseFolder(string scenario)
    {
        string folder = Path.Combine(DatabaseTestRoot, scenario + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void EngineLifecycleIsSafe()
    {
        AssertThrows<ArgumentNullException>(() => new DBreezeEngine((DBreezeConfiguration)null));
        AssertThrows<ArgumentNullException>(() => new DBreezeRemoteEngine(null));

        var remoteConfiguration = new DBreezeConfiguration
        {
            Storage = DBreezeConfiguration.eStorage.RemoteInstance,
        };
        var uninitializedRemote = new DBreezeRemoteEngine(remoteConfiguration);
        uninitializedRemote.Dispose();
        Assert(uninitializedRemote.Disposed, "Uninitialized remote engine was not disposed.");

        DBreezeEngine baseTypedRemote = new DBreezeRemoteEngine(new DBreezeConfiguration
        {
            Storage = DBreezeConfiguration.eStorage.MEMORY,
            NotifyAhead_WhenWriteTablePossibleDeadlock = false,
        });
        using (baseTypedRemote)
        using (var transaction = baseTypedRemote.GetTransaction())
        {
            transaction.Insert("base-remote", 1, 42);
            transaction.Commit();
        }

        var concurrentlyInitializedRemote = new DBreezeRemoteEngine(new DBreezeConfiguration
        {
            Storage = DBreezeConfiguration.eStorage.MEMORY,
            NotifyAhead_WhenWriteTablePossibleDeadlock = false,
        });
        Parallel.For(0, 32, _ =>
        {
            using var transaction = concurrentlyInitializedRemote.GetTransaction();
        });
        concurrentlyInitializedRemote.Dispose();

        for (int i = 0; i < 16; i++)
        {
            var racedRemote = new DBreezeRemoteEngine(new DBreezeConfiguration
            {
                Storage = DBreezeConfiguration.eStorage.MEMORY,
                NotifyAhead_WhenWriteTablePossibleDeadlock = false,
            });
            using var gate = new ManualResetEventSlim();
            Exception initializationError = null;
            Task initialize = Task.Run(() =>
            {
                gate.Wait();
                try
                {
                    _ = racedRemote.Scheme;
                }
                catch (Exception ex)
                {
                    initializationError = ex;
                }
            });
            Task dispose = Task.Run(() =>
            {
                gate.Wait();
                racedRemote.Dispose();
            });

            gate.Set();
            Task.WaitAll(initialize, dispose);
            Assert(initializationError == null || initializationError is ObjectDisposedException,
                $"Concurrent remote initialization/disposal failed with {initializationError?.GetType().Name}.");
            Assert(racedRemote.Disposed, "Raced remote engine was not disposed.");
        }

        Parallel.For(0, 32, _ =>
        {
            using var engine = CreateMemoryEngine();
            using var transaction = engine.GetTransaction();
        });
    }

    private static void RemoteInitializationFailureIsTerminal()
    {
        var communicator = new FailingRemoteCommunicator();
        var configuration = new DBreezeConfiguration
        {
            Storage = DBreezeConfiguration.eStorage.RemoteInstance,
            RICommunicator = communicator,
        };
        using var remote = new DBreezeRemoteEngine(configuration);

        Exception first = null;
        Exception second = null;
        try
        {
            _ = remote.Scheme;
        }
        catch (Exception ex)
        {
            first = ex;
        }

        int callsAfterFirstFailure = communicator.SendCalls;
        try
        {
            _ = remote.GetTransaction();
        }
        catch (Exception ex)
        {
            second = ex;
        }

        Assert(first != null, "Remote initialization unexpectedly succeeded.");
        Assert(ReferenceEquals(first, second),
            "A repeated remote initialization did not rethrow the original exception instance.");
        AssertEqual(callsAfterFirstFailure, communicator.SendCalls,
            "A repeated remote initialization created another remote component set.");
    }

    private static void DeferredIndexerRunsInParallelAndCoalescesStarts()
    {
        string folder = CreateDatabaseFolder(nameof(DeferredIndexerRunsInParallelAndCoalescesStarts));
        byte[] documentId = { 1, 2, 3 };

        try
        {
            using (var engine = new DBreezeEngine(folder))
            {
                int started = 0;
                int finished = 0;
                int failed = 0;
                engine.BackgroundTasksExternalNotifier = (notification, error) =>
                {
                    switch (notification)
                    {
                        case "TextDefferedIndexingHasStarted":
                            Interlocked.Increment(ref started);
                            break;
                        case "TextDefferedIndexingHasFinished":
                            Interlocked.Increment(ref finished);
                            break;
                        case "TextDefferedIndexingHasFailed":
                            Interlocked.Increment(ref failed);
                            Console.Error.WriteLine(error);
                            break;
                    }
                };

                object indexer = GetDeferredIndexer(engine);
                object indexerSync = GetPrivateField<object>(indexer, "_sync");
                bool lockTaken = false;
                try
                {
                    Monitor.Enter(indexerSync, ref lockTaken);

                    using (var transaction = engine.GetTransaction())
                    {
                        transaction.TextInsert(
                            "deferred-parallel",
                            documentId,
                            containsWords: "premium parallel indexing",
                            deferredIndexing: true);
                        transaction.Commit();
                    }

                    Task worker = GetPrivateField<Task>(indexer, "_workerTask");
                    Assert(worker != null && !worker.IsCompleted,
                        "Deferred commit waited for the background worker.");

                    for (int i = 0; i < 32; i++)
                    {
                        InvokePrivate(indexer, "StartDefferedIndexing");
                        Assert(ReferenceEquals(worker, GetPrivateField<Task>(indexer, "_workerTask")),
                            "Concurrent StartDefferedIndexing created another worker.");
                    }

                    Task unrelatedTransaction = Task.Run(() =>
                    {
                        using var transaction = engine.GetTransaction();
                        transaction.Insert("parallel-user-table", 1, "still available");
                        transaction.Commit();
                    });
                    Assert(unrelatedTransaction.Wait(TimeSpan.FromSeconds(5)),
                        "An unrelated user transaction was blocked by deferred indexing.");
                }
                finally
                {
                    if (lockTaken)
                        Monitor.Exit(indexerSync);
                }

                AssertEventually(
                    () => DeferredQueueCount(indexer) == 0 &&
                          TextSearchContains(engine, "deferred-parallel", "premium", documentId),
                    "Deferred text batch was not indexed.");
                AssertEventually(() => Volatile.Read(ref finished) != 0,
                    "Deferred indexer did not publish its finished notification.");
                Assert(Volatile.Read(ref started) != 0,
                    "Deferred indexer did not publish its started notification.");
                AssertEqual(0, Volatile.Read(ref failed),
                    "Deferred indexer unexpectedly failed during normal indexing.");
            }

            using var reopened = new DBreezeEngine(folder);
            Assert(TextSearchContains(reopened, "deferred-parallel", "premium", documentId),
                "Deferred text index was not preserved after reopen.");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void DeferredIndexerShutdownPreservesPendingRows()
    {
        string folder = CreateDatabaseFolder(nameof(DeferredIndexerShutdownPreservesPendingRows));
        byte[] documentId = { 9, 8, 7 };
        DBreezeEngine engine = null;
        Task disposeTask = null;
        object indexer = null;
        object indexerSync = null;
        bool lockTaken = false;
        int failed = 0;
        int finished = 0;

        try
        {
            engine = new DBreezeEngine(folder);
            engine.BackgroundTasksExternalNotifier = (notification, _) =>
            {
                if (notification == "TextDefferedIndexingHasFailed")
                    Interlocked.Increment(ref failed);
                else if (notification == "TextDefferedIndexingHasFinished")
                    Interlocked.Increment(ref finished);
            };

            indexer = GetDeferredIndexer(engine);
            indexerSync = GetPrivateField<object>(indexer, "_sync");
            Monitor.Enter(indexerSync, ref lockTaken);

            using (var transaction = engine.GetTransaction())
            {
                transaction.TextInsert(
                    "deferred-shutdown",
                    documentId,
                    containsWords: "retained shutdown batch",
                    deferredIndexing: true);
                transaction.Commit();
            }

            Task worker = GetPrivateField<Task>(indexer, "_workerTask");
            Assert(worker != null && !worker.IsCompleted,
                "The shutdown test did not capture an active deferred worker.");

            disposeTask = Task.Run(engine.Dispose);
            AssertEventually(() => engine.Disposed, "Engine disposal did not start.");
            Assert(!disposeTask.IsCompleted,
                "Engine disposal did not wait for the active deferred worker.");
        }
        finally
        {
            if (lockTaken)
                Monitor.Exit(indexerSync);
        }

        try
        {
            Assert(disposeTask != null && disposeTask.Wait(TimeSpan.FromSeconds(10)),
                "Engine disposal did not finish after the deferred worker left its safe boundary.");
            AssertEventually(() => Volatile.Read(ref finished) != 0,
                "Shutdown worker did not publish its finished notification.");
            AssertEqual(0, Volatile.Read(ref failed),
                "Normal shutdown was incorrectly reported as deferred-indexing failure.");
            AssertEqual(1, ReadDeferredQueueRows(folder).Count,
                "Shutdown removed a deferred row that was not processed.");

            using var reopened = new DBreezeEngine(folder);
            AssertEventually(
                () => TextSearchContains(reopened, "deferred-shutdown", "retained", documentId),
                "Pending deferred row was not recovered after reopen.");
        }
        finally
        {
            try { engine?.Dispose(); } catch { }
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void DeferredIndexerFailureParksDurableBatch()
    {
        string folder = CreateDatabaseFolder(nameof(DeferredIndexerFailureParksDurableBatch));

        try
        {
            using var engine = new DBreezeEngine(folder);
            object indexer = GetDeferredIndexer(engine);
            object indexerSync = GetPrivateField<object>(indexer, "_sync");
            Exception reportedFailure = null;
            int started = 0;
            int failed = 0;
            int finished = 0;
            engine.BackgroundTasksExternalNotifier = (notification, payload) =>
            {
                switch (notification)
                {
                    case "TextDefferedIndexingHasStarted":
                        Interlocked.Increment(ref started);
                        break;
                    case "TextDefferedIndexingHasFailed":
                        Interlocked.CompareExchange(ref reportedFailure, payload as Exception, null);
                        Interlocked.Increment(ref failed);
                        break;
                    case "TextDefferedIndexingHasFinished":
                        Interlocked.Increment(ref finished);
                        break;
                }
            };

            lock (indexerSync)
            {
                var queue = GetPrivateField<DBreeze.LianaTrie.LTrie>(indexer, "_lTrie");
                queue.Add(DateTime.UtcNow.Ticks.To_8_bytes_array_BigEndian(),
                    Enumerable.Repeat((byte)0x80, 5).ToArray());
                queue.Commit();
            }

            InvokePrivate(indexer, "StartDefferedIndexing");
            AssertEventually(() => Volatile.Read(ref failed) == 1,
                "Malformed deferred payload did not publish a failure notification.");
            AssertEventually(() => !DeferredWorkerIsRunning(indexer),
                "Deferred worker did not park after a malformed payload.");
            AssertEventually(() => Volatile.Read(ref started) >= 1 && Volatile.Read(ref finished) >= 1,
                "Deferred failure did not preserve started/finished notifications.");
            Assert(reportedFailure != null,
                "Failure notification did not contain the original exception.");
            AssertEqual(1, DeferredQueueCount(indexer),
                "Malformed durable row was removed after failure.");

            Thread.Sleep(300);
            AssertEqual(1, Volatile.Read(ref failed),
                "Deferred worker entered an automatic retry loop after failure.");

            InvokePrivate(indexer, "StartDefferedIndexing");
            AssertEventually(() => Volatile.Read(ref failed) == 2,
                "Explicit StartDefferedIndexing did not retry a parked batch.");
            AssertEventually(() => !DeferredWorkerIsRunning(indexer),
                "Deferred worker did not park after the explicit retry failed.");
            AssertEqual(1, DeferredQueueCount(indexer),
                "Explicit retry removed a malformed durable row.");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void DeferredIndexerSequenceAndDiskFormatRemainCompatible()
    {
        string folder = CreateDatabaseFolder(nameof(DeferredIndexerSequenceAndDiskFormatRemainCompatible));
        long futureTextSequence = DateTime.UtcNow.AddDays(30).Ticks;
        long futureVectorSequence = futureTextSequence + 10;
        byte[] malformedPayload = Enumerable.Repeat((byte)0x80, 5).ToArray();

        var textTask = new Dictionary<string, HashSet<uint>>(StringComparer.Ordinal)
        {
            ["format-text"] = new HashSet<uint> { 11, 12 }
        };
        var vectorTask = new Dictionary<string, HashSet<uint>>(StringComparer.Ordinal)
        {
            ["format-vector"] = new HashSet<uint> { 21, 22 }
        };
        byte[] expectedTextPayload = textTask.Encode_DICT_PROTO_STRING_UINTHASHSET();
        byte[] expectedVectorPayload = vectorTask.Encode_DICT_PROTO_STRING_UINTHASHSET();

        try
        {
            WriteDeferredQueueRows(
                folder,
                (CreateDeferredQueueKey(futureTextSequence, vectorTask: false), malformedPayload),
                (CreateDeferredQueueKey(futureVectorSequence, vectorTask: true), malformedPayload));

            using (var engine = new DBreezeEngine(folder))
            {
                object indexer = GetDeferredIndexer(engine);
                AssertEventually(() => !DeferredWorkerIsRunning(indexer),
                    "Recovery worker did not park on the injected malformed row.");

                InvokePrivate(indexer, "Add", textTask);
                InvokePrivate(indexer, "AddVectors", vectorTask);
            }

            List<(byte[] Key, byte[] Value)> rows = ReadDeferredQueueRows(folder);
            AssertEqual(4, rows.Count, "Unexpected deferred queue row count in format probe.");

            var generatedText = rows.Single(row =>
                row.Key.Length == 8 && ReadDeferredSequence(row.Key) > futureVectorSequence);
            var generatedVector = rows.Single(row =>
                row.Key.Length == 10 && ReadDeferredSequence(row.Key) > futureVectorSequence);

            AssertEqual(futureVectorSequence + 1, ReadDeferredSequence(generatedText.Key),
                "Text sequence did not continue after the greatest persisted counter.");
            AssertEqual(futureVectorSequence + 2, ReadDeferredSequence(generatedVector.Key),
                "Vector sequence did not continue after the text counter.");
            AssertEqual(8, generatedText.Key.Length, "Text deferred key length changed.");
            AssertEqual(10, generatedVector.Key.Length, "Vector deferred key length changed.");
            AssertEqual((byte)0, generatedVector.Key[0], "Vector protocol byte 0 changed.");
            AssertEqual((byte)0, generatedVector.Key[1], "Vector protocol byte 1 changed.");
            AssertSequenceEqual(expectedTextPayload, generatedText.Value,
                "Text deferred Biser payload changed.");
            AssertSequenceEqual(expectedVectorPayload, generatedVector.Value,
                "Vector deferred Biser payload changed.");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void ResourcesKeepCacheAndStorageCoherent()
    {
        string folder = CreateDatabaseFolder(nameof(ResourcesKeepCacheAndStorageCoherent));
        try
        {
            using (var engine = new DBreezeEngine(folder))
            {
                engine.Resources.Insert("coherent", "old");
                var diskOnly = new DBreezeResources.Settings
                {
                    HoldInMemory = false,
                    HoldOnDisk = true,
                };
                engine.Resources.Insert("coherent", "new", diskOnly);
                AssertEqual("new", engine.Resources.Select<string>("coherent"),
                    "Disk-only update left a stale cache value.");
            }

            using var reopened = new DBreezeEngine(folder);
            AssertEqual("new", reopened.Resources.Select<string>("coherent"),
                "Reopened resource differs from the live cache value.");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void ResourcesPersistNullAfterNegativeCache()
    {
        string folder = CreateDatabaseFolder(nameof(ResourcesPersistNullAfterNegativeCache));
        try
        {
            using (var engine = new DBreezeEngine(folder))
            {
                AssertEqual<byte[]>(null, engine.Resources.Select<byte[]>("null-key"),
                    "Missing resource did not return null.");
                engine.Resources.Insert<byte[]>("null-key", null);

                Assert(engine.Resources.SelectStartsWith<byte[]>("null").Any(x => x.Key == "null-key"),
                    "Persisted null was confused with a missing resource.");
            }

            using var reopened = new DBreezeEngine(folder);
            Assert(reopened.Resources.SelectStartsWith<byte[]>("null").Any(x => x.Key == "null-key"),
                "Persisted null disappeared after reopen.");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void ResourcesPreserveEmptyArraysAndActiveSnapshots()
    {
        string folder = CreateDatabaseFolder(nameof(ResourcesPreserveEmptyArraysAndActiveSnapshots));
        try
        {
            var engine = new DBreezeEngine(folder);
            engine.Resources.Insert("empty-array", Array.Empty<byte>());
            byte[] empty = engine.Resources.Select<byte[]>("empty-array");
            Assert(empty != null && empty.Length == 0,
                "An empty byte array was confused with the persisted-null sentinel.");

            for (int i = 0; i < 32; i++)
                engine.Resources.Insert("snapshot-" + i.ToString("D2"), new byte[] { (byte)i });

            using IEnumerator<KeyValuePair<string, byte[]>> snapshot =
                engine.Resources.SelectStartsWith<byte[]>("snapshot-").GetEnumerator();
            Assert(snapshot.MoveNext(), "Prefix snapshot unexpectedly contained no rows.");

            Task disposeTask = Task.Run(engine.Dispose);
            Assert(SpinWait.SpinUntil(() => engine.Disposed, TimeSpan.FromSeconds(5)),
                "Engine disposal did not start.");
            Assert(!disposeTask.IsCompleted,
                "Engine disposal destroyed storage while a prefix snapshot was active.");

            int count = 1;
            while (snapshot.MoveNext())
                count++;
            AssertEqual(32, count, "Active prefix snapshot was truncated during disposal.");
            snapshot.Dispose();

            Assert(disposeTask.Wait(TimeSpan.FromSeconds(5)),
                "Engine disposal did not resume after the prefix snapshot completed.");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void ResourcesRemainCoherentUnderConcurrentWrites()
    {
        string folder = CreateDatabaseFolder(nameof(ResourcesRemainCoherentUnderConcurrentWrites));
        try
        {
            var liveValues = new Dictionary<string, int>(StringComparer.Ordinal);
            using (var engine = new DBreezeEngine(folder))
            {
                Parallel.For(0, 2_000, i =>
                {
                    string key = "parallel-" + (i & 15);
                    engine.Resources.Insert(key, i);
                    _ = engine.Resources.Select<int>(key);
                });

                for (int i = 0; i < 16; i++)
                {
                    string key = "parallel-" + i;
                    liveValues[key] = engine.Resources.Select<int>(key);
                }
            }

            using var reopened = new DBreezeEngine(folder);
            foreach (KeyValuePair<string, int> pair in liveValues)
            {
                AssertEqual(pair.Value, reopened.Resources.Select<int>(pair.Key),
                    $"Concurrent cache/storage mismatch for {pair.Key}.");
            }
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void ResourcesRefreshCommittedReadRoots()
    {
        string folder = CreateDatabaseFolder(nameof(ResourcesRefreshCommittedReadRoots));
        var diskOnly = new DBreezeResources.Settings
        {
            HoldInMemory = false,
            HoldOnDisk = true,
            InsertWithVerification = false,
            FastUpdates = true,
        };

        try
        {
            using (var engine = new DBreezeEngine(folder))
            {
                engine.Resources.Insert("pooled", new byte[] { 1 }, diskOnly);
                AssertSequenceEqual(new byte[] { 1 }, engine.Resources.Select<byte[]>("pooled", diskOnly),
                    "A pooled root did not see the inserted resource.");

                engine.Resources.Insert("pooled", new byte[] { 2 }, diskOnly);
                AssertSequenceEqual(new byte[] { 2 }, engine.Resources.Select<byte[]>("pooled", diskOnly),
                    "A pooled root stayed stale after update.");

                engine.Resources.Remove("pooled");
                AssertEqual<byte[]>(null, engine.Resources.Select<byte[]>("pooled", diskOnly),
                    "A pooled root stayed stale after remove.");

                var inserted = new Dictionary<string, byte[]>(StringComparer.Ordinal)
                {
                    ["pooled-a"] = new byte[] { 3 },
                    ["pooled-b"] = new byte[] { 4 },
                };
                engine.Resources.Insert(inserted, diskOnly);
                IDictionary<string, byte[]> selected = engine.Resources.Select<byte[]>(
                    new[] { "pooled-a", "pooled-b" }, diskOnly);
                AssertSequenceEqual(inserted["pooled-a"], selected["pooled-a"],
                    "Batch select did not refresh its committed root after insert.");
                AssertSequenceEqual(inserted["pooled-b"], selected["pooled-b"],
                    "Batch select returned a stale value after insert.");
            }

            using var reopened = new DBreezeEngine(folder);
            AssertSequenceEqual(new byte[] { 3 }, reopened.Resources.Select<byte[]>("pooled-a", diskOnly),
                "A reopened resource differs from the last committed value.");
            AssertSequenceEqual(new byte[] { 4 }, reopened.Resources.Select<byte[]>("pooled-b", diskOnly),
                "A reopened batch resource differs from the last committed value.");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void ResourcesKeepCommittedReadRootsExclusive()
    {
        string folder = CreateDatabaseFolder(nameof(ResourcesKeepCommittedReadRootsExclusive));
        var diskOnly = new DBreezeResources.Settings
        {
            HoldInMemory = false,
            HoldOnDisk = true,
            InsertWithVerification = false,
            FastUpdates = true,
        };
        const int keyCount = 64;
        const int versions = 40;
        string[] keys = Enumerable.Range(0, keyCount).Select(static i => "root-" + i.ToString("D3")).ToArray();

        try
        {
            using var engine = new DBreezeEngine(folder);
            engine.Resources.Insert(CreateVersionedResources(keys, 0), diskOnly);

            using var start = new ManualResetEventSlim(false);
            Task writer = Task.Run(() =>
            {
                start.Wait();
                for (int version = 1; version <= versions; version++)
                    engine.Resources.Insert(CreateVersionedResources(keys, version), diskOnly);
            });

            Task[] readers = Enumerable.Range(0, 4).Select(readerId => Task.Run(() =>
            {
                start.Wait();
                for (int iteration = 0; iteration < 100; iteration++)
                {
                    IDictionary<string, byte[]> batch = engine.Resources.Select<byte[]>(keys, diskOnly);
                    AssertEqual(keyCount, batch.Count, "Concurrent batch read lost a resource.");

                    int batchVersion = BitConverter.ToInt32(batch[keys[0]], 0);
                    for (int keyIndex = 0; keyIndex < keyCount; keyIndex++)
                    {
                        byte[] value = batch[keys[keyIndex]];
                        AssertEqual(batchVersion, BitConverter.ToInt32(value, 0),
                            "One batch observed more than one committed generation.");
                        AssertEqual(keyIndex, BitConverter.ToInt32(value, sizeof(int)),
                            "Concurrent root use corrupted a resource value.");
                    }

                    int pointIndex = (iteration + readerId) % keyCount;
                    byte[] point = engine.Resources.Select<byte[]>(keys[pointIndex], diskOnly);
                    AssertEqual(pointIndex, BitConverter.ToInt32(point, sizeof(int)),
                        "Concurrent point reads shared a mutable read root.");
                }
            })).ToArray();

            start.Set();
            Task.WaitAll(readers.Append(writer).ToArray());

            IDictionary<string, byte[]> final = engine.Resources.Select<byte[]>(keys, diskOnly);
            foreach (string key in keys)
                AssertEqual(versions, BitConverter.ToInt32(final[key], 0),
                    "A stale pooled root survived the final commit.");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static Dictionary<string, byte[]> CreateVersionedResources(string[] keys, int version)
    {
        var resources = new Dictionary<string, byte[]>(keys.Length, StringComparer.Ordinal);
        for (int i = 0; i < keys.Length; i++)
        {
            byte[] value = new byte[sizeof(int) * 2];
            BitConverter.GetBytes(version).CopyTo(value, 0);
            BitConverter.GetBytes(i).CopyTo(value, sizeof(int));
            resources.Add(keys[i], value);
        }
        return resources;
    }

    private static void SchemeCommittedReadsRemainCoherent()
    {
        string folder = CreateDatabaseFolder(nameof(SchemeCommittedReadsRemainCoherent));
        const string stableTable = "scheme-stable";
        const string missingTable = "scheme-never-created";
        try
        {
            using (var engine = new DBreezeEngine(folder))
            {
                PutValue(engine, stableTable, 123);
                string stablePath = engine.Scheme.GetTablePathFromTableName(stableTable);
                Assert(stablePath.Length != 0, "Stable schema path is missing.");

                int publishedIteration = -1;
                using var stop = new CancellationTokenSource();
                int readerCount = Math.Min(16, Math.Max(4, Environment.ProcessorCount * 2));
                Task[] readers = Enumerable.Range(0, readerCount).Select(readerIndex => Task.Run(() =>
                {
                    while (!stop.IsCancellationRequested)
                    {
                        Assert(engine.Scheme.IfUserTableExists(stableTable),
                            "A committed stable table disappeared from the schema.");
                        AssertEqual(stablePath, engine.Scheme.GetTablePathFromTableName(stableTable),
                            "A committed stable table returned another physical path.");
                        Assert(!engine.Scheme.IfUserTableExists(missingTable),
                            "A permanently missing table appeared in the schema.");
                        AssertEqual(String.Empty, engine.Scheme.GetTablePathFromTableName(missingTable),
                            "A permanently missing table returned a physical path.");

                        int current = Volatile.Read(ref publishedIteration);
                        if (current >= 0)
                        {
                            string transient = "scheme-churn-" + current;
                            _ = engine.Scheme.IfUserTableExists(transient);
                            _ = engine.Scheme.GetTablePathFromTableName(transient);
                            _ = engine.Scheme.IfUserTableExists(transient + "-renamed");
                            _ = engine.Scheme.GetTablePathFromTableName(transient + "-renamed");
                        }
                    }
                })).ToArray();

                try
                {
                    for (int i = 0; i < 80; i++)
                    {
                        string table = "scheme-churn-" + i;
                        string renamed = table + "-renamed";
                        Volatile.Write(ref publishedIteration, i);

                        Assert(!engine.Scheme.IfUserTableExists(table), "Transient table was stale before create.");
                        PutValue(engine, table, i);
                        Assert(engine.Scheme.IfUserTableExists(table), "Created table is absent from schema.");
                        Assert(engine.Scheme.GetTablePathFromTableName(table).Length != 0,
                            "Created table has no physical path.");

                        engine.Scheme.RenameTable(table, renamed);
                        Assert(!engine.Scheme.IfUserTableExists(table), "Renamed source remained in schema.");
                        Assert(engine.Scheme.IfUserTableExists(renamed), "Renamed destination is absent.");

                        engine.Scheme.DeleteTable(renamed);
                        Assert(!engine.Scheme.IfUserTableExists(renamed), "Deleted table remained in schema.");
                        AssertEqual(String.Empty, engine.Scheme.GetTablePathFromTableName(renamed),
                            "Deleted table retained a physical path.");
                    }
                }
                finally
                {
                    stop.Cancel();
                    Task.WaitAll(readers);
                }

                Assert(!engine.Scheme.GetUserTableNamesStartingWith("scheme-churn-").Any(),
                    "Deleted transient schema records survived the stress run.");
            }

            using var reopened = new DBreezeEngine(folder);
            Assert(reopened.Scheme.IfUserTableExists(stableTable),
                "Stable schema record did not survive reopen.");
            Assert(!reopened.Scheme.IfUserTableExists(missingTable),
                "Missing schema record appeared after reopen.");
            Assert(!reopened.Scheme.GetUserTableNamesStartingWith("scheme-churn-").Any(),
                "Deleted transient schema records reappeared after reopen.");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void SchemeRenamePreservesDataAndReplacementSemantics()
    {
        using var engine = CreateMemoryEngine();

        PutValue(engine, "same", 11);
        engine.Scheme.RenameTable("same", "same");
        AssertEqual(11, GetValue(engine, "same"), "Same-name rename changed the table.");

        PutValue(engine, "existing-destination", 22);
        engine.Scheme.RenameTable("missing-source", "existing-destination");
        AssertEqual(22, GetValue(engine, "existing-destination"),
            "Missing source deleted the destination.");

        PutValue(engine, "replace-source", 33);
        PutValue(engine, "replace-destination", 44);
        engine.Scheme.RenameTable("replace-source", "replace-destination");
        Assert(!engine.Scheme.IfUserTableExists("replace-source"), "Renamed source still exists.");
        AssertEqual(33, GetValue(engine, "replace-destination"),
            "Destination replacement did not preserve source data.");
    }

    private static void SchemeRenameReplacesDiskDestination()
    {
        string folder = CreateDatabaseFolder(nameof(SchemeRenameReplacesDiskDestination));
        string replacedPath;
        try
        {
            using (var engine = new DBreezeEngine(folder))
            {
                PutValue(engine, "disk-source", 35);
                PutValue(engine, "disk-destination", 45);
                replacedPath = engine.Scheme.GetTablePathFromTableName("disk-destination");
                Assert(File.Exists(replacedPath), "Destination table file was not created.");

                engine.Scheme.RenameTable("disk-source", "disk-destination");
                Assert(!engine.Scheme.IfUserTableExists("disk-source"),
                    "Disk source metadata survived rename.");
                AssertEqual(35, GetValue(engine, "disk-destination"),
                    "Disk destination replacement did not preserve source data.");
                Assert(!File.Exists(replacedPath),
                    "Replaced destination table file was left orphaned on disk.");
            }

            using var reopened = new DBreezeEngine(folder);
            Assert(!reopened.Scheme.IfUserTableExists("disk-source"),
                "Renamed disk source reappeared after reopen.");
            AssertEqual(35, GetValue(reopened, "disk-destination"),
                "Renamed disk destination did not reopen with source data.");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void SchemeRenameRejectsStorageRouteChanges()
    {
        string folder = CreateDatabaseFolder(nameof(SchemeRenameRejectsStorageRouteChanges));
        string alternateFolder = Path.Combine(folder, "alternate");
        try
        {
            var configuration = new DBreezeConfiguration
            {
                DBreezeDataFolderName = folder,
                Storage = DBreezeConfiguration.eStorage.DISK,
                NotifyAhead_WhenWriteTablePossibleDeadlock = false,
            };
            configuration.AlternativeTablesLocations["alt*"] = alternateFolder;

            using var engine = new DBreezeEngine(configuration);
            PutValue(engine, "default-source", 55);
            PutValue(engine, "alt-destination", 66);

            AssertThrows<DBreeze.Exceptions.DBreezeException>(() =>
                engine.Scheme.RenameTable("default-source", "alt-destination"));
            AssertEqual(55, GetValue(engine, "default-source"), "Rejected rename damaged source data.");
            AssertEqual(66, GetValue(engine, "alt-destination"), "Rejected rename damaged destination data.");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void SchemeRenameWaitsForActiveTable()
    {
        string folder = CreateDatabaseFolder(nameof(SchemeRenameWaitsForActiveTable));
        try
        {
            using var engine = new DBreezeEngine(folder);
            PutValue(engine, "busy-source", 77);

            var active = engine.GetTransaction();
            Assert(active.Select<int, int>("busy-source", 1).Exists, "Busy source row is missing.");

            Task rename = Task.Run(() => engine.Scheme.RenameTable("busy-source", "busy-destination"));
            Thread.Sleep(100);
            Assert(!rename.IsCompleted, "Rename did not wait for an active disk table.");

            active.Dispose();
            Assert(rename.Wait(TimeSpan.FromSeconds(5)), "Rename did not resume after table release.");
            AssertEqual(77, GetValue(engine, "busy-destination"), "Waited rename lost source data.");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void PutValue(DBreezeEngine engine, string table, int value)
    {
        using var transaction = engine.GetTransaction();
        transaction.Insert(table, 1, value);
        transaction.Commit();
    }

    private static int GetValue(DBreezeEngine engine, string table)
    {
        using var transaction = engine.GetTransaction();
        return transaction.Select<int, int>(table, 1).Value;
    }

    private static void RandomKeySorterKeepsFinalOperation()
    {
        using var engine = CreateMemoryEngine();

        using (var transaction = engine.GetTransaction())
        {
            transaction.Insert("rks-final", 1, 10);
            transaction.Commit();
        }

        using (var transaction = engine.GetTransaction())
        {
            transaction.RandomKeySorter.Insert("rks-final", 1, 20);
            transaction.RandomKeySorter.Remove("rks-final", 1);
            transaction.Commit();
        }

        using var reader = engine.GetTransaction();
        Assert(!reader.Select<int, int>("rks-final", 1).Exists,
            "RKS Insert->Remove left the previously committed value alive.");
    }

    private static void RandomKeySorterRollbackDropsPendingOperations()
    {
        using var engine = CreateMemoryEngine();
        using var transaction = engine.GetTransaction();

        transaction.RandomKeySorter.Insert("rks-rollback", 1, 30);
        transaction.Rollback();
        transaction.Commit();

        Assert(!transaction.Select<int, int>("rks-rollback", 1).Exists,
            "RKS replayed a pending insert after Rollback followed by Commit.");
    }

    private static void RandomKeySorterUsesValueConversionAndNeverAutoFlushes()
    {
        using var engine = CreateMemoryEngine();
        byte[] mutableKey = { 9, 8, 7 };
        byte[] originalKey = mutableKey.ToArray();
        using (var transaction = engine.GetTransaction())
        {
            transaction.RandomKeySorter.AutomaticFlushLimitQuantityPerTable = 1;
            transaction.RandomKeySorter.Insert("rks-values", 1, true);
            transaction.RandomKeySorter.Insert("rks-values", 2, false);
            Assert(!transaction.Select<int, bool>("rks-values", 1).Exists,
                "The compatibility RKS limit unexpectedly triggered an automatic flush.");
            Assert(!transaction.Select<int, bool>("rks-values", 2).Exists,
                "RKS operations must stay buffered until explicit Flush or Commit.");

            transaction.RandomKeySorter.Flush("rks-values");
            Assert(transaction.Select<int, bool>("rks-values", 1).Exists,
                "Explicit RKS Flush did not apply buffered operations.");
            transaction.Commit();
        }
        using (var transaction = engine.GetTransaction())
        {
            transaction.RandomKeySorter.AutomaticFlushLimitQuantityPerTable = 1;
            transaction.RandomKeySorter.Insert("rks-values", 3, true);
            Assert(!transaction.Select<int, bool>("rks-values", 3).Exists,
                "RKS limit must remain a compatibility-only no-op.");
            transaction.Commit();
        }
        using (var transaction = engine.GetTransaction())
        {
            transaction.RandomKeySorter.Insert("rks-mutable-key", mutableKey, 42);
            mutableKey[0] = 0;
            transaction.Commit();
        }

        using var reader = engine.GetTransaction();
        Assert(reader.Select<int, bool>("rks-values", 1).Value, "RKS did not use TValue conversion for bool.");
        Assert(!reader.Select<int, bool>("rks-values", 2).Value, "Explicit RKS flush corrupted bool false.");
        Assert(reader.Select<int, bool>("rks-values", 3).Value, "RKS commit flush lost buffered data.");
        AssertEqual(42, reader.Select<byte[], int>("rks-mutable-key", originalKey).Value,
            "RKS retained a caller-owned mutable key buffer.");
        Assert(!reader.Select<byte[], int>("rks-mutable-key", mutableKey).Exists,
            "RKS stored the key after caller mutation.");
    }

    private static void RandomKeySorterBorrowsValuesUntilFlush()
    {
        using var engine = CreateMemoryEngine();
        byte[] value = { 1, 2, 3 };

        using (var transaction = engine.GetTransaction())
        {
            transaction.RandomKeySorter.Insert("rks-borrowed-value", 1, value);
            value[0] = 9;
            transaction.Commit();
        }

        using var reader = engine.GetTransaction();
        AssertSequenceEqual(new byte[] { 9, 2, 3 }, reader.Select<int, byte[]>("rks-borrowed-value", 1).Value,
            "RKS did not borrow the serialized value until Commit.");
    }

    private static void RandomKeySorterSupportsFlushRollbackAndRepeatedCommits()
    {
        using var engine = CreateMemoryEngine();
        using var transaction = engine.GetTransaction();

        transaction.RandomKeySorter.Insert("rks-cycles", 1, 10);
        transaction.RandomKeySorter.Remove("rks-cycles", 1);
        transaction.RandomKeySorter.Insert("rks-cycles", 1, 20);
        transaction.Commit();
        AssertEqual(20, transaction.Select<int, int>("rks-cycles", 1).Value,
            "RKS did not preserve last-operation-wins for Remove->Insert.");

        transaction.RandomKeySorter.Insert("rks-cycles", 2, 30);
        transaction.RandomKeySorter.Flush("rks-cycles");
        transaction.Rollback();
        Assert(!transaction.Select<int, int>("rks-cycles", 2).Exists,
            "Rollback did not undo an explicitly flushed RKS operation.");

        transaction.RandomKeySorter.Insert("rks-cycles", 3, 40);
        transaction.Commit();
        transaction.RandomKeySorter.Insert("rks-cycles", 4, 50);
        transaction.Commit();

        AssertEqual(40, transaction.Select<int, int>("rks-cycles", 3).Value,
            "First repeated Commit lost an RKS operation.");
        AssertEqual(50, transaction.Select<int, int>("rks-cycles", 4).Value,
            "Second repeated Commit lost an RKS operation.");
    }

    private static void ObjectInsertNewEntityDoesNotDependOnRksLimit()
    {
        using var engine = CreateMemoryEngine();
        using (var transaction = engine.GetTransaction())
        {
            transaction.RandomKeySorter.AutomaticFlushLimitQuantityPerTable = 1;
            transaction.ObjectInsert("rks-object", CreateObject(1, "old"));
            transaction.ObjectInsert("rks-object", CreateObject(2, "new"));
            transaction.Commit();
        }

        using var reader = engine.GetTransaction();
        var current = reader.Select<byte[], byte[]>("rks-object", 1.ToIndex(7)).ObjectGet<byte[]>();
        Assert(current != null, "Object primary index was not stored.");
        AssertSequenceEqual(new byte[] { 2 }, current.Entity,
            "Repeated NewEntity insert did not keep the final object.");
        Assert(!reader.Select<byte[], byte[]>("rks-object", 2.ToIndex("old", 7)).Exists,
            "Repeated NewEntity insert left the previous secondary index alive.");
        Assert(reader.Select<byte[], byte[]>("rks-object", 2.ToIndex("new", 7)).Exists,
            "Repeated NewEntity insert lost the final secondary index.");
    }

    private static DBreeze.Objects.DBreezeObject<byte[]> CreateObject(byte value, string secondaryIndex)
    {
        return new DBreeze.Objects.DBreezeObject<byte[]>
        {
            NewEntity = true,
            Entity = new byte[] { value },
            Indexes = new List<DBreeze.Objects.DBreezeIndex>
            {
                new DBreeze.Objects.DBreezeIndex(1, 7) { PrimaryIndex = true },
                new DBreeze.Objects.DBreezeIndex(2, secondaryIndex),
            },
        };
    }

    private static void ObjectIdentityRemainsBufferedUntilCommit()
    {
        using var engine = CreateMemoryEngine();
        using (var transaction = engine.GetTransaction())
        {
            transaction.RandomKeySorter.AutomaticFlushLimitQuantityPerTable = 1;
            AssertEqual(1L, transaction.ObjectGetNewIdentity<long>("rks-identity"), "First identity.");
            AssertEqual(2L, transaction.ObjectGetNewIdentity<long>("rks-identity"), "Second identity.");
            Assert(!transaction.Select<byte[], byte[]>("rks-identity", new byte[] { 0 }).Exists,
                "Identity counter was automatically flushed before Commit.");
            transaction.Commit();
        }

        using var reader = engine.GetTransaction();
        AssertEqual(2L, reader.Select<byte[], long>("rks-identity", new byte[] { 0 }).Value,
            "Commit did not persist the final identity counter.");
    }

    private static void SelectDirectOnMissingTableIsEmpty()
    {
        using var engine = CreateMemoryEngine();
        using var transaction = engine.GetTransaction();
        var row = transaction.SelectDirect<byte[], byte[]>("missing-direct", new byte[] { 1 });
        Assert(!row.Exists, "SelectDirect returned a row for a missing table.");
    }

    private static void MutationsAreRejectedOnAnotherThread()
    {
        using var engine = CreateMemoryEngine();
        using var transaction = engine.GetTransaction();

        // Prime the write-table cache. The old check only ran on the cache miss.
        transaction.Insert("thread-owner", 1, 1);

        AssertThrows<DBreeze.Exceptions.DBreezeException>(() =>
            Task.Run(() => transaction.Insert("thread-owner", 2, 2)).GetAwaiter().GetResult());
        AssertThrows<DBreeze.Exceptions.DBreezeException>(() =>
            Task.Run(transaction.Commit).GetAwaiter().GetResult());
        AssertThrows<DBreeze.Exceptions.DBreezeException>(() =>
            Task.Run(transaction.Rollback).GetAwaiter().GetResult());
        AssertThrows<DBreeze.Exceptions.DBreezeException>(() =>
            Task.Run(() => transaction.VectorsCount<float[]>("thread-owner-vector")).GetAwaiter().GetResult());

        transaction.Rollback();
    }

    private static void CoordinatorDoesNotLoseWakeups()
    {
        using var engine = CreateMemoryEngine();
        const int workers = 8;
        const int iterations = 40;
        using var start = new ManualResetEventSlim(false);

        Task[] tasks = Enumerable.Range(0, workers).Select(_ => Task.Factory.StartNew(() =>
        {
            start.Wait();
            for (int i = 0; i < iterations; i++)
            {
                using var transaction = engine.GetTransaction();
                transaction.SynchronizeTables("coordinator-counter");
                var row = transaction.Select<string, int>("coordinator-counter", "value");
                transaction.Insert("coordinator-counter", "value", row.Exists ? row.Value + 1 : 1);
                transaction.Commit();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        start.Set();
        Assert(Task.WaitAll(tasks, TimeSpan.FromSeconds(30)), "Coordinator waiters did not complete (possible lost wakeup).");

        using var reader = engine.GetTransaction();
        AssertEqual(workers * iterations, reader.Select<string, int>("coordinator-counter", "value").Value,
            "Coordinator lost serialized updates.");
    }

    private static void CoordinatorDetectsThreeWayDeadlock()
    {
        string folder = CreateDatabaseFolder(nameof(CoordinatorDetectsThreeWayDeadlock));
        try
        {
            using var engine = new DBreezeEngine(new DBreezeConfiguration
            {
                DBreezeDataFolderName = folder,
                NotifyAhead_WhenWriteTablePossibleDeadlock = false,
            });
            using var barrier = new Barrier(3);
            int deadlocks = 0;
            string[] first = { "deadlock-a", "deadlock-b", "deadlock-c" };
            string[] second = { "deadlock-b", "deadlock-c", "deadlock-a" };

            Task[] tasks = Enumerable.Range(0, 3).Select(index => Task.Factory.StartNew(() =>
            {
                try
                {
                    using var transaction = engine.GetTransaction();
                    transaction.Insert(first[index], index, index);
                    barrier.SignalAndWait(TimeSpan.FromSeconds(10));
                    transaction.Insert(second[index], index, index);
                }
                catch (DBreeze.Exceptions.DBreezeException)
                {
                    Interlocked.Increment(ref deadlocks);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

            Assert(Task.WaitAll(tasks, TimeSpan.FromSeconds(30)), "Three-way deadlock was not resolved.");
            Assert(deadlocks >= 1, "Three-way wait cycle was not reported as a deadlock.");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void MultiSelectMergesAndKeepsTieOrder()
    {
        string folder = CreateDatabaseFolder(nameof(MultiSelectMergesAndKeepsTieOrder));
        try
        {
            using var engine = new DBreezeEngine(folder);
            string[] tableOrder = { "merge-c", "merge-a", "merge-b" };
            foreach (string table in tableOrder)
            {
                using (var transaction = engine.GetTransaction())
                {
                    transaction.Insert(table, 1, table);
                    transaction.Insert(table, 3, table);
                    transaction.Commit();
                }
            }

            var tables = new HashSet<string>(tableOrder);
            string[] expectedTieOrder = tables.ToArray();
            using var reader = engine.GetTransaction();
            var forward = reader.Multi_SelectForwardFromTo<int, string>(tables, 1, true, 3, true).ToArray();
            AssertEqual(6, forward.Length, "Forward multi-select count.");
            Assert(forward.Take(3).All(static row => row.Key == 1), "Forward multi-select key order.");
            Assert(forward.Skip(3).All(static row => row.Key == 3), "Forward multi-select key order.");
            Assert(expectedTieOrder.SequenceEqual(forward.Take(3).Select(static row => row.TableName)),
                "Forward multi-select changed table tie order.");

            var backward = reader.Multi_SelectBackwardFromTo<int, string>(tables, 3, true, 1, true).ToArray();
            AssertEqual(6, backward.Length, "Backward multi-select count.");
            Assert(backward.Take(3).All(static row => row.Key == 3), "Backward multi-select key order.");
            Assert(expectedTieOrder.SequenceEqual(backward.Take(3).Select(static row => row.TableName)),
                "Backward multi-select changed table tie order.");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void MultiSelectRejectsVariableLengthKeys()
    {
        using var engine = CreateMemoryEngine();
        using (var transaction = engine.GetTransaction())
        {
            transaction.Insert("merge-variable-a", new byte[] { 1 }, 1);
            transaction.Insert("merge-variable-a", new byte[] { 2, 0 }, 2);
            transaction.Insert("merge-variable-b", new byte[] { 1 }, 3);
            transaction.Insert("merge-variable-b", new byte[] { 3, 0 }, 4);
            transaction.Commit();
        }

        var tables = new HashSet<string> { "merge-variable-a", "merge-variable-b" };
        using var reader = engine.GetTransaction();
        AssertThrows<DBreeze.Exceptions.DBreezeException>(() =>
            reader.Multi_SelectForwardFromTo<byte[], int>(
                tables, new byte[] { 0 }, true, new byte[] { 255, 255 }, true).ToArray());
        AssertThrows<DBreeze.Exceptions.DBreezeException>(() =>
            reader.Multi_SelectBackwardFromTo<byte[], int>(
                tables, new byte[] { 255, 255 }, true, new byte[] { 0 }, true).ToArray());
    }

    private static void LockedTransactionsRespectExclusiveWaiter()
    {
        using var engine = CreateMemoryEngine();
        var firstShared = engine.GetTransaction(eTransactionTablesLockTypes.SHARED, "locked-fairness");
        using var exclusiveStarted = new ManualResetEventSlim(false);
        using var exclusiveAcquired = new ManualResetEventSlim(false);
        using var releaseExclusive = new ManualResetEventSlim(false);
        using var secondSharedAcquired = new ManualResetEventSlim(false);

        Task exclusive = Task.Factory.StartNew(() =>
        {
            exclusiveStarted.Set();
            using var transaction = engine.GetTransaction(eTransactionTablesLockTypes.EXCLUSIVE, "locked-fairness");
            exclusiveAcquired.Set();
            releaseExclusive.Wait(TimeSpan.FromSeconds(10));
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        Assert(exclusiveStarted.Wait(TimeSpan.FromSeconds(5)), "Exclusive waiter did not start.");
        Thread.Sleep(100);

        Task secondShared = Task.Factory.StartNew(() =>
        {
            using var transaction = engine.GetTransaction(eTransactionTablesLockTypes.SHARED, "locked-fairness");
            secondSharedAcquired.Set();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        Assert(!secondSharedAcquired.Wait(TimeSpan.FromMilliseconds(200)),
            "A new shared session bypassed an earlier exclusive waiter.");
        firstShared.Dispose();
        Assert(exclusiveAcquired.Wait(TimeSpan.FromSeconds(5)), "Exclusive waiter was not granted after shared release.");
        Assert(!secondSharedAcquired.IsSet, "Shared waiter was granted before the earlier exclusive waiter released.");
        releaseExclusive.Set();
        Assert(Task.WaitAll(new[] { exclusive, secondShared }, TimeSpan.FromSeconds(10)), "Locked fairness tasks did not complete.");
    }

    private static void LockedTransactionCanBeDisposedOnAnotherThread()
    {
        using var engine = CreateMemoryEngine();
        var transaction = engine.GetTransaction(eTransactionTablesLockTypes.EXCLUSIVE, "locked-cross-thread-dispose");
        Task.Run(transaction.Dispose).GetAwaiter().GetResult();

        Task next = Task.Factory.StartNew(() =>
        {
            using var nextTransaction = engine.GetTransaction(eTransactionTablesLockTypes.EXCLUSIVE, "locked-cross-thread-dispose");
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Assert(next.Wait(TimeSpan.FromSeconds(5)), "Cross-thread Dispose leaked the locked-table session.");
    }

    private static void DictionaryAndHashSetReplacementRemoveMissingKeys()
    {
        using var engine = CreateMemoryEngine();
        using (var transaction = engine.GetTransaction())
        {
            transaction.InsertDictionary("replace-dictionary", new Dictionary<int, int>
            {
                [1] = 10,
                [2] = 20,
                [3] = 30,
            }, false);
            transaction.Commit();
        }
        using (var transaction = engine.GetTransaction())
        {
            transaction.InsertDictionary("replace-dictionary", new Dictionary<int, int>
            {
                [2] = 200,
                [4] = 400,
            }, true);
            transaction.Commit();
        }
        using (var transaction = engine.GetTransaction())
        {
            var dictionary = transaction.SelectDictionary<int, int>("replace-dictionary");
            AssertEqual(2, dictionary.Count, "Dictionary replacement count.");
            AssertEqual(200, dictionary[2], "Dictionary replacement update.");
            AssertEqual(400, dictionary[4], "Dictionary replacement insert.");
        }

        using (var transaction = engine.GetTransaction())
        {
            transaction.InsertHashSet("replace-hashset", new HashSet<int> { 1, 2, 3 }, false);
            transaction.Commit();
        }
        using (var transaction = engine.GetTransaction())
        {
            transaction.InsertHashSet("replace-hashset", new HashSet<int> { 2, 4 }, true);
            transaction.Commit();
        }
        using (var transaction = engine.GetTransaction())
        {
            Assert(new HashSet<int> { 2, 4 }.SetEquals(transaction.SelectHashSet<int>("replace-hashset")),
                "HashSet replacement did not remove missing keys.");
        }
    }

    private static void CollectionReplacementUsesDatabaseKeyEquality()
    {
        using var engine = CreateMemoryEngine();
        using (var transaction = engine.GetTransaction())
        {
            transaction.InsertDictionary("custom-dictionary", new Dictionary<string, int> { ["A"] = 1 }, false);
            transaction.InsertHashSet("custom-hashset", new HashSet<string> { "A" }, false);
            transaction.InsertDictionary("custom-nested-dictionary", 7,
                new Dictionary<string, int> { ["A"] = 1 }, 0, false);
            transaction.InsertHashSet("custom-nested-hashset", 7,
                new HashSet<string> { "A" }, 0, false);
            transaction.Commit();
        }

        using (var transaction = engine.GetTransaction())
        {
            transaction.InsertDictionary("custom-dictionary",
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["a"] = 2 }, true);
            transaction.InsertHashSet("custom-hashset",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a" }, true);
            transaction.InsertDictionary("custom-nested-dictionary", 7,
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["a"] = 2 }, 0, true);
            transaction.InsertHashSet("custom-nested-hashset", 7,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a" }, 0, true);
            transaction.Commit();
        }

        using var reader = engine.GetTransaction();
        Dictionary<string, int> dictionary = reader.SelectDictionary<string, int>("custom-dictionary");
        AssertEqual(1, dictionary.Count, "Custom-comparer dictionary replacement left a binary-distinct DB key.");
        AssertEqual(2, dictionary["a"], "Custom-comparer dictionary replacement value.");

        HashSet<string> hashSet = reader.SelectHashSet<string>("custom-hashset");
        Assert(hashSet.SetEquals(new[] { "a" }),
            "Custom-comparer HashSet replacement left a binary-distinct DB key.");

        Dictionary<string, int> nestedDictionary =
            reader.SelectDictionary<int, string, int>("custom-nested-dictionary", 7, 0);
        AssertEqual(1, nestedDictionary.Count,
            "Nested custom-comparer dictionary replacement left a binary-distinct DB key.");
        AssertEqual(2, nestedDictionary["a"], "Nested custom-comparer dictionary replacement value.");

        HashSet<string> nestedHashSet = reader.SelectHashSet<int, string>("custom-nested-hashset", 7, 0);
        Assert(nestedHashSet.SetEquals(new[] { "a" }),
            "Nested custom-comparer HashSet replacement left a binary-distinct DB key.");
    }

    private static void CoordinatorCleansUpNotifyAheadFailure()
    {
        using var engine = new DBreezeEngine(new DBreezeConfiguration
        {
            Storage = DBreezeConfiguration.eStorage.MEMORY,
            NotifyAhead_WhenWriteTablePossibleDeadlock = true,
        });

        using (var failedTransaction = engine.GetTransaction())
        {
            failedTransaction.Insert("notify-a", 1, 1);
            AssertThrows<DBreeze.Exceptions.DBreezeException>(() =>
                failedTransaction.Insert("notify-b", 1, 1));
        }

        Task writer = Task.Factory.StartNew(() =>
        {
            using var transaction = engine.GetTransaction();
            transaction.Insert("notify-a", 1, 2);
            transaction.Commit();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        Assert(writer.Wait(TimeSpan.FromSeconds(5)),
            "NotifyAhead registration failure leaked a reservation and blocked the next writer.");
        using var reader = engine.GetTransaction();
        AssertEqual(2, reader.Select<int, int>("notify-a", 1).Value,
            "NotifyAhead cleanup did not roll back the failed transaction.");
        Assert(!reader.Select<int, int>("notify-b", 1).Exists,
            "NotifyAhead cleanup persisted data from the failed transaction.");
    }

    private static void RemoveAllResetsEmptyKeyState()
    {
        using var engine = CreateMemoryEngine();
        byte[] emptyKey = Array.Empty<byte>();

        using (var transaction = engine.GetTransaction())
        {
            transaction.Insert("remove-all", emptyKey, new byte[] { 1 });
            transaction.Insert("remove-all", new byte[] { 2 }, new byte[] { 2 });
            transaction.Commit();
        }

        using (var transaction = engine.GetTransaction())
        {
            transaction.RemoveAllKeys("remove-all", false);
            Assert(!transaction.Select<byte[], byte[]>("remove-all", emptyKey).Exists,
                "RemoveAll(false) left the empty key visible.");

            transaction.Insert("remove-all", emptyKey, new byte[] { 3 });
            transaction.Commit();
        }

        using (var transaction = engine.GetTransaction())
        {
            AssertEqual(1UL, transaction.Count("remove-all"), "Record count after RemoveAll(false) and reinsertion.");
            AssertSequenceEqual(new byte[] { 3 }, transaction.Select<byte[], byte[]>("remove-all", emptyKey).Value,
                "Reinserted empty-key value.");
        }
    }

    private static void InsertIfAbsentPreservesNestedTable()
    {
        using var engine = CreateMemoryEngine();
        byte[] parentKey = { 10 };
        byte[] childKey = { 20 };
        byte[] childValue = { 30 };

        using (var transaction = engine.GetTransaction())
        {
            transaction.Insert("nested", parentKey, new byte[] { 1 });
            using var nested = transaction.InsertTable("nested", parentKey, 0);
            nested.Insert(childKey, childValue);
            transaction.Commit();
        }

        byte[] parentValueBefore;
        using (var transaction = engine.GetTransaction())
        {
            parentValueBefore = transaction.Select<byte[], byte[]>("nested", parentKey).Value;
        }

        using (var transaction = engine.GetTransaction())
        {
            transaction.Insert("nested", parentKey, new byte[] { 99 }, out _, out bool wasUpdated, true);
            Assert(wasUpdated, "Insert-if-absent did not report the existing key.");
            transaction.Commit();
        }

        using (var transaction = engine.GetTransaction())
        {
            AssertSequenceEqual(parentValueBefore, transaction.Select<byte[], byte[]>("nested", parentKey).Value,
                "Insert-if-absent changed the parent value.");

            using var nested = transaction.SelectTable("nested", parentKey, 0);
            var child = nested.Select<byte[], byte[]>(childKey);
            Assert(child.Exists, "Insert-if-absent removed the existing nested table.");
            AssertSequenceEqual(childValue, child.Value, "Nested child value after insert-if-absent.");
        }
    }

    private static void NestedStructuralKeyCacheSurvivesMutationAndRename()
    {
        using var engine = CreateMemoryEngine();
        byte[] mutableKey = { 1, 2 };
        byte[] originalKey = mutableKey.ToArray();
        byte[] renamedKey = { 3, 4 };
        byte[] childKey = { 5 };
        byte[] childValue = { 6 };

        using (var transaction = engine.GetTransaction())
        {
            transaction.Insert("nested-structural", mutableKey, new byte[] { 7 });
            using var nested = transaction.InsertTable("nested-structural", mutableKey, 0);
            nested.Insert(childKey, childValue);

            // The coordinator must own an immutable dictionary key, not this caller buffer.
            mutableKey[0] = 99;
            transaction.Commit();
        }

        using (var transaction = engine.GetTransaction())
        {
            transaction.ChangeKey("nested-structural", originalKey, renamedKey);
            transaction.Commit();
        }

        using (var transaction = engine.GetTransaction())
        {
            Assert(!transaction.Select<byte[], byte[]>("nested-structural", originalKey).Exists,
                "Old parent key remained after rename.");
            using var nested = transaction.SelectTable("nested-structural", renamedKey, 0);
            var child = nested.Select<byte[], byte[]>(childKey);
            Assert(child.Exists, "Nested table was lost after structural-key rename.");
            AssertSequenceEqual(childValue, child.Value, "Nested value after structural-key rename.");
        }
    }

    private static void PartialValueRangesAreOverflowSafe()
    {
        using var engine = CreateMemoryEngine();
        byte[] valueKey = { 1 };
        byte[] nullKey = { 2 };
        byte[] emptyKey = { 3 };
        byte[] missingKey = { 4 };
        byte[] value = Enumerable.Range(0, 10).Select(static x => (byte)x).ToArray();

        using (var transaction = engine.GetTransaction())
        {
            transaction.Insert("partial", valueKey, value);
            transaction.Insert<byte[], byte[]>("partial", nullKey, null);
            transaction.Insert("partial", emptyKey, Array.Empty<byte>());
            transaction.Commit();
        }

        foreach (bool lazyLoading in new[] { true, false })
        {
            using var transaction = engine.GetTransaction();
            transaction.ValuesLazyLoadingIsOn = lazyLoading;
            string mode = lazyLoading ? "lazy" : "eager";

            var valueRow = transaction.Select<byte[], byte[]>("partial", valueKey);
            AssertSequenceEqual(value.AsSpan(2).ToArray(), valueRow.GetValuePart(2, uint.MaxValue),
                $"{mode}: overflowing requested length.");
            AssertSequenceEqual(value.AsSpan(2).ToArray(), valueRow.GetValuePart(2),
                $"{mode}: GetValuePart(startIndex) must honor startIndex.");
            AssertSequenceEqual(value.AsSpan(5).ToArray(), valueRow.GetValuePart(5, uint.MaxValue),
                $"{mode}: uint.MaxValue length must be clamped without wrapping.");
            AssertEmpty(valueRow.GetValuePart(0, 0), $"{mode}: zero-length slice at start.");
            AssertEmpty(valueRow.GetValuePart(uint.MaxValue, 0),
                $"{mode}: zero-length slice outside the value.");
            AssertEmpty(valueRow.GetValuePart((uint)value.Length, 1),
                $"{mode}: slice starting exactly at the end.");
            AssertEmpty(valueRow.GetValuePart((uint)value.Length, uint.MaxValue),
                $"{mode}: overflowing slice starting exactly at the end.");
            Assert(valueRow.GetValuePart((uint)value.Length + 1, 1) == null,
                $"{mode}: start past the end must return null.");
            Assert(valueRow.GetValuePart(uint.MaxValue, uint.MaxValue) == null,
                $"{mode}: uint.MaxValue start must return null.");
            Assert(valueRow.GetValuePart(uint.MaxValue) == null,
                $"{mode}: one-argument uint.MaxValue start must return null.");

            var nullRow = transaction.Select<byte[], byte[]>("partial", nullKey);
            Assert(nullRow.GetValuePart(0, 0) == null, $"{mode}: null with zero length.");
            Assert(nullRow.GetValuePart(uint.MaxValue, 0) == null,
                $"{mode}: null with out-of-range zero-length slice.");
            Assert(nullRow.GetValuePart(0, uint.MaxValue) == null, $"{mode}: null with maximum length.");
            Assert(nullRow.GetValuePart(0) == null, $"{mode}: one-argument null slice.");

            var emptyRow = transaction.Select<byte[], byte[]>("partial", emptyKey);
            AssertEmpty(emptyRow.GetValuePart(0, 0), $"{mode}: empty value at origin.");
            AssertEmpty(emptyRow.GetValuePart(1, 0), $"{mode}: empty value zero-length out of range.");
            AssertEmpty(emptyRow.GetValuePart(1, 1), $"{mode}: empty value non-zero slice out of range.");
            AssertEmpty(emptyRow.GetValuePart(uint.MaxValue, uint.MaxValue),
                $"{mode}: empty value at uint limits.");
            AssertEmpty(emptyRow.GetValuePart(uint.MaxValue),
                $"{mode}: one-argument empty value at uint limit.");

            var missingRow = transaction.Select<byte[], byte[]>("partial", missingKey);
            Assert(missingRow.GetValuePart(0, 0) == null, $"{mode}: missing row with zero length.");
            Assert(missingRow.GetValuePart(0) == null, $"{mode}: missing row one-argument slice.");
        }

        using (var transaction = engine.GetTransaction())
        {
            AssertThrows<ArgumentOutOfRangeException>(() =>
                transaction.InsertPart("partial", valueKey, new byte[] { 1 }, uint.MaxValue));
        }
    }

    private static void AssertEmpty(byte[] actual, string message)
    {
        if (actual == null || actual.Length != 0)
            throw new InvalidOperationException($"{message} Expected an empty array; actual: {Format(actual)}.");
    }

    private static void ReadRootRefreshesAfterRapidCommits()
    {
        using var engine = CreateMemoryEngine();
        byte[] key = { 1 };

        using (var transaction = engine.GetTransaction())
        {
            transaction.Insert("versions", key, BitConverter.GetBytes(0));
            transaction.Commit();
        }

        using var reader = engine.GetTransaction();
        AssertEqual(0, BitConverter.ToInt32(reader.Select<byte[], byte[]>("versions", key).Value), "Initial reader value.");

        for (int i = 1; i <= 100; i++)
        {
            int expected = i;
            Task.Run(() =>
            {
                using var writer = engine.GetTransaction();
                writer.Insert("versions", key, BitConverter.GetBytes(expected));
                writer.Commit();
            }).GetAwaiter().GetResult();

            int actual = BitConverter.ToInt32(reader.Select<byte[], byte[]>("versions", key).Value);
            AssertEqual(expected, actual, "Cached read root was not refreshed after commit.");
        }
    }

    private static void ReadVisibilityUsesCommittedNodeImages()
    {
        using var engine = CreateMemoryEngine();

        using (var transaction = engine.GetTransaction())
        {
            transaction.Insert("visibility", new byte[] { 1 }, new byte[] { 11 });
            transaction.Insert("visibility", new byte[] { 2 }, new byte[] { 22 });
            transaction.Commit();
        }

        using (var transaction = engine.GetTransaction())
        {
            transaction.SynchronizeTables("visibility");

            // Materialize the committed root before overwriting nodes in the modification scope.
            AssertKeys(new byte[][] { new byte[] { 1 }, new byte[] { 2 } },
                transaction.SelectForward<byte[], byte[]>("visibility", true).Select(static row => row.Key),
                "Initial read-visibility rows.");

            transaction.RemoveKey("visibility", new byte[] { 1 });
            transaction.Insert("visibility", new byte[] { 3 }, new byte[] { 33 });

            AssertKeys(new byte[][] { new byte[] { 2 }, new byte[] { 3 } },
                transaction.SelectForward<byte[], byte[]>("visibility").Select(static row => row.Key),
                "Modification-scope rows.");
            AssertKeys(new byte[][] { new byte[] { 1 }, new byte[] { 2 } },
                transaction.SelectForward<byte[], byte[]>("visibility", true).Select(static row => row.Key),
                "Committed read-visibility rows after node overwrites.");

            transaction.Rollback();
        }
    }

    private static void RangeIterationStopsAtItsBoundary()
    {
        using var engine = CreateMemoryEngine();
        byte[][] keys =
        {
            Array.Empty<byte>(),
            new byte[] { 0 },
            new byte[] { 1 },
            new byte[] { 1, 0 },
            new byte[] { 1, 1 },
            new byte[] { 2 },
            new byte[] { 2, 0 },
            new byte[] { 3 },
        };

        using (var transaction = engine.GetTransaction())
        {
            foreach (byte[] key in keys)
                transaction.Insert("range", key, key);
            transaction.Commit();
        }

        using (var transaction = engine.GetTransaction())
        {
            AssertKeys(new byte[][] { Array.Empty<byte>(), new byte[] { 0 }, new byte[] { 1 } },
                transaction.SelectForwardFromTo<byte[], byte[]>(
                    "range", Array.Empty<byte>(), true, new byte[] { 1 }, true).Select(static row => row.Key),
                "Forward range with an empty start key.");

            AssertKeys(new byte[][] { new byte[] { 1, 0 }, new byte[] { 1, 1 } },
                transaction.SelectForwardFromTo<byte[], byte[]>(
                    "range", new byte[] { 1 }, false, new byte[] { 2 }, false).Select(static row => row.Key),
                "Exclusive forward range.");

            AssertKeys(new byte[][] { new byte[] { 1, 1 }, new byte[] { 1, 0 } },
                transaction.SelectBackwardFromTo<byte[], byte[]>(
                    "range", new byte[] { 2 }, false, new byte[] { 1 }, false).Select(static row => row.Key),
                "Exclusive backward range.");
        }
    }

    private static void DeepFullScanDoesNotUseTheCallStack()
    {
        using var engine = CreateMemoryEngine();
        byte[] first = Enumerable.Repeat((byte)7, 8_192).ToArray();
        byte[] second = first.ToArray();
        second[second.Length - 1] = 8;

        using (var transaction = engine.GetTransaction())
        {
            transaction.Insert("deep", first, new byte[] { 1 });
            transaction.Insert("deep", second, new byte[] { 2 });
            transaction.Commit();
        }

        using (var transaction = engine.GetTransaction())
        {
            AssertKeys(new[] { first, second },
                transaction.SelectForward<byte[], byte[]>("deep").Select(static row => row.Key),
                "Deep forward scan.");
            AssertKeys(new[] { second },
                transaction.SelectForwardSkip<byte[], byte[]>("deep", 1).Select(static row => row.Key),
                "Deep forward skip.");
            AssertKeys(new[] { first },
                transaction.SelectBackwardSkip<byte[], byte[]>("deep", 1).Select(static row => row.Key),
                "Deep backward skip.");
            AssertKeys(new[] { second, first },
                transaction.SelectBackward<byte[], byte[]>("deep").Select(static row => row.Key),
                "Deep backward scan.");
            AssertKeys(new[] { first, second },
                transaction.SelectForwardFromTo<byte[], byte[]>("deep", first, true, second, true)
                    .Select(static row => row.Key),
                "Deep forward range.");
            AssertKeys(new[] { second, first },
                transaction.SelectBackwardFromTo<byte[], byte[]>("deep", second, true, first, true)
                    .Select(static row => row.Key),
                "Deep backward range.");
        }
    }

    private static void SkipTraversalMatchesReferenceModel()
    {
        using var engine = CreateMemoryEngine();
        byte[][] keys =
        {
            Array.Empty<byte>(),
            new byte[] { 0 },
            new byte[] { 0, 0 },
            new byte[] { 0, 255 },
            new byte[] { 1 },
            new byte[] { 1, 0 },
            new byte[] { 1, 0, 0 },
            new byte[] { 1, 1 },
            new byte[] { 2 },
            new byte[] { 127 },
            new byte[] { 128 },
            new byte[] { 254, 255 },
            new byte[] { 255 },
            new byte[] { 255, 0 },
        };

        using (var transaction = engine.GetTransaction())
        {
            foreach (byte[] key in keys)
                transaction.Insert("skip", key, SkipTestValue(key));
            transaction.Commit();
        }

        byte[][] forward = keys.OrderBy(static key => key, ByteComparer).ToArray();
        byte[][] backward = forward.Reverse().ToArray();
        ulong[] skipCounts =
        {
            0,
            1,
            (ulong)(keys.Length / 2),
            (ulong)(keys.Length - 1),
            (ulong)keys.Length,
            (ulong)(keys.Length + 1),
            ulong.MaxValue,
        };

        foreach (bool lazyLoading in new[] { true, false })
        {
            using var transaction = engine.GetTransaction();
            transaction.ValuesLazyLoadingIsOn = lazyLoading;

            foreach (ulong skip in skipCounts)
            {
                int skipAsInt = skip >= (ulong)keys.Length ? keys.Length : (int)skip;
                var forwardRows = transaction.SelectForwardSkip<byte[], byte[]>("skip", skip).ToArray();
                var backwardRows = transaction.SelectBackwardSkip<byte[], byte[]>("skip", skip).ToArray();

                AssertSkipRows(forward.Skip(skipAsInt), forwardRows,
                    $"Forward skip={skip}, lazy={lazyLoading}.");
                AssertSkipRows(backward.Skip(skipAsInt), backwardRows,
                    $"Backward skip={skip}, lazy={lazyLoading}.");
            }
        }
    }

    private static byte[] SkipTestValue(byte[] key)
    {
        byte[] value = new byte[key.Length + 1];
        value[0] = (byte)key.Length;
        for (int i = 0; i < key.Length; i++)
            value[i + 1] = key[key.Length - i - 1];
        return value;
    }

    private static void AssertSkipRows(
        IEnumerable<byte[]> expectedKeys,
        DBreeze.DataTypes.Row<byte[], byte[]>[] actualRows,
        string message)
    {
        byte[][] expected = expectedKeys.ToArray();
        AssertEqual(expected.Length, actualRows.Length, $"{message} Count.");
        for (int i = 0; i < expected.Length; i++)
        {
            AssertSequenceEqual(expected[i], actualRows[i].Key, $"{message} Key {i}.");
            AssertSequenceEqual(SkipTestValue(expected[i]), actualRows[i].Value, $"{message} Value {i}.");
        }
    }

    private static void DiskDatabaseReopensWithTheSameRows()
    {
        string folder = CreateDatabaseFolder(nameof(DiskDatabaseReopensWithTheSameRows));

        byte[][] keys =
        {
            Array.Empty<byte>(),
            new byte[] { 1 },
            new byte[] { 1, 2, 3 },
            Enumerable.Repeat((byte)255, 512).ToArray(),
        };

        try
        {
            using (var engine = new DBreezeEngine(folder))
            using (var transaction = engine.GetTransaction())
            {
                foreach (byte[] key in keys)
                    transaction.Insert("reopen", key, key.Reverse().ToArray());
                transaction.Commit();
            }

            using (var engine = new DBreezeEngine(folder))
            using (var transaction = engine.GetTransaction())
            {
                var rows = transaction.SelectForward<byte[], byte[]>("reopen").ToArray();
                AssertEqual(keys.Length, rows.Length, "Reopened disk row count.");
                for (int i = 0; i < keys.Length; i++)
                {
                    AssertSequenceEqual(keys[i], rows[i].Key, "Reopened disk key.");
                    AssertSequenceEqual(keys[i].Reverse().ToArray(), rows[i].Value, "Reopened disk value.");
                }
            }
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void AlternativeDiskTableReopens()
    {
        string folder = CreateDatabaseFolder(nameof(AlternativeDiskTableReopens));
        string mainFolder = Path.Combine(folder, "main");
        string alternativeFolder = Path.Combine(folder, "alternative");

        DBreezeConfiguration CreateConfiguration()
        {
            var configuration = new DBreezeConfiguration
            {
                DBreezeDataFolderName = mainFolder,
                Storage = DBreezeConfiguration.eStorage.DISK,
            };
            configuration.AlternativeTablesLocations.Add("alternative*", alternativeFolder);
            return configuration;
        }

        try
        {
            using (var engine = new DBreezeEngine(CreateConfiguration()))
            using (var transaction = engine.GetTransaction())
            {
                transaction.Insert("alternative-table", 1, "stored-on-disk");
                transaction.Commit();
            }

            using (var engine = new DBreezeEngine(CreateConfiguration()))
            using (var transaction = engine.GetTransaction())
            {
                var row = transaction.Select<int, string>("alternative-table", 1);
                Assert(row.Exists, "Alternative disk table was not reopened.");
                AssertEqual("stored-on-disk", row.Value, "Alternative disk table value after reopen.");
            }
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void RandomizedOperationsMatchReferenceModel()
    {
        using var engine = CreateMemoryEngine();
        var committed = new SortedDictionary<byte[], byte[]>(ByteComparer);
        var random = new Random(0x5EED);

        for (int batch = 0; batch < 80; batch++)
        {
            var candidate = Clone(committed);
            using var transaction = engine.GetTransaction();

            for (int operation = 0; operation < 25; operation++)
            {
                byte[] key = RandomBytes(random, random.Next(0, 13));
                if (random.Next(100) < 65)
                {
                    byte[] value = RandomBytes(random, random.Next(0, 129));
                    bool insertIfAbsent = random.Next(5) == 0;
                    transaction.Insert("random", key, value, out _, out bool wasUpdated, insertIfAbsent);

                    bool existed = candidate.ContainsKey(key);
                    AssertEqual(existed, wasUpdated, "WasUpdated differs from the reference model.");
                    if (!insertIfAbsent || !existed)
                        candidate[key] = value;
                }
                else
                {
                    transaction.RemoveKey("random", key, out bool wasRemoved);
                    AssertEqual(candidate.Remove(key), wasRemoved, "WasRemoved differs from the reference model.");
                }
            }

            if (random.Next(5) == 0)
            {
                transaction.Rollback();
            }
            else
            {
                transaction.Commit();
                committed = candidate;
            }

            VerifyTable(engine, committed);
        }
    }

    private static void JournalPayloadAndCrashRecoveryRemainCompatible()
    {
        string folder = CreateDatabaseFolder(nameof(JournalPayloadAndCrashRecoveryRemainCompatible));
        var configuration = new DBreezeConfiguration
        {
            DBreezeDataFolderName = folder,
            NotifyAhead_WhenWriteTablePossibleDeadlock = false,
        };
        configuration.AlternativeTablesLocations["journal-b"] = Path.Combine(folder, "alternative");

        try
        {
            using (var engine = new DBreezeEngine(configuration))
            {
                using (var transaction = engine.GetTransaction())
                {
                    transaction.Insert("journal-a", 1, "a");
                    transaction.Commit();
                }
                using (var transaction = engine.GetTransaction())
                {
                    transaction.Insert("journal-b", 2, "b");
                    transaction.Commit();
                }

                var journalField = typeof(DBreezeEngine).GetField(
                    "_transactionsJournal",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert(journalField != null, "Transactions journal field was not found.");
                var journal = (DBreeze.Transactions.TransactionsJournal)journalField.GetValue(engine);
                ulong transactionNumber = journal.GetTransactionNumber();
                journal.AddTableForTransaction(transactionNumber, new FailingJournalTable("journal-a"));
                journal.AddTableForTransaction(transactionNumber, new FailingJournalTable("journal-b"));

                AssertThrows<InvalidOperationException>(() => journal.FinishTransaction(transactionNumber));
            }

            byte[][] payloads = ReadJournalPayloads(folder, configuration);
            AssertEqual(1, payloads.Length, "Persisted crash-recovery marker count.");
            AssertSequenceEqual(
                System.Text.Encoding.UTF8.GetBytes("<string>journal-a</string>\n<string>journal-b</string>\n"),
                payloads[0],
                "Journal payload changed.");

            using (var engine = new DBreezeEngine(configuration))
            using (var transaction = engine.GetTransaction())
            {
                AssertEqual("a", transaction.Select<int, string>("journal-a", 1).Value,
                    "Crash recovery damaged the first committed table.");
                AssertEqual("b", transaction.Select<int, string>("journal-b", 2).Value,
                    "Crash recovery damaged the second committed table.");
            }

            AssertEqual(0, ReadJournalPayloads(folder, configuration).Length,
                "Crash recovery did not clear the durable journal marker.");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static void ParallelMultiTableCommitsRemainDurable()
    {
        string folder = CreateDatabaseFolder(nameof(ParallelMultiTableCommitsRemainDurable));
        const int workers = 6;
        const int iterations = 25;

        try
        {
            using (var engine = new DBreezeEngine(new DBreezeConfiguration
            {
                DBreezeDataFolderName = folder,
                NotifyAhead_WhenWriteTablePossibleDeadlock = false,
            }))
            using (var start = new ManualResetEventSlim(false))
            {
                Task[] tasks = Enumerable.Range(0, workers).Select(worker => Task.Factory.StartNew(() =>
                {
                    string firstTable = $"parallel-journal-{worker}-a";
                    string secondTable = $"parallel-journal-{worker}-b";
                    start.Wait();
                    for (int iteration = 0; iteration < iterations; iteration++)
                    {
                        using var transaction = engine.GetTransaction();
                        transaction.Insert(firstTable, iteration, worker);
                        transaction.Insert(secondTable, iteration, worker);
                        transaction.Commit();
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

                start.Set();
                Assert(Task.WaitAll(tasks, TimeSpan.FromSeconds(30)),
                    "Parallel multi-table commits did not complete.");
            }

            using var reopened = new DBreezeEngine(folder);
            using var reader = reopened.GetTransaction();
            for (int worker = 0; worker < workers; worker++)
            {
                foreach (string suffix in new[] { "a", "b" })
                {
                    string table = $"parallel-journal-{worker}-{suffix}";
                    var rows = reader.SelectForward<int, int>(table).ToArray();
                    AssertEqual(iterations, rows.Length, $"Durable row count for {table}.");
                    Assert(rows.All(row => row.Value == worker), $"Durable values for {table}.");
                }
            }
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static byte[][] ReadJournalPayloads(string folder, DBreezeConfiguration configuration)
    {
        var storage = new DBreeze.Storage.StorageLayer(
            Path.Combine(folder, "_DBreezeTranJrnl"),
            new DBreeze.Storage.TrieSettings(),
            configuration);
        using var journal = new DBreeze.LianaTrie.LTrie(storage)
        {
            TableName = "DBreeze.TranJournal",
        };
        return journal.IterateForward(true, false)
            .Select(static row => row.GetFullValue(true))
            .ToArray();
    }

    private static object GetDeferredIndexer(DBreezeEngine engine)
    {
        var field = typeof(DBreezeEngine).GetField(
            "DeferredIndexer",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert(field != null, "Deferred indexer field was not found.");
        object indexer = field.GetValue(engine);
        Assert(indexer != null, "Deferred indexer was not initialized.");
        return indexer;
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);
        Assert(field != null, $"Field {fieldName} was not found on {instance.GetType().Name}.");
        return (T)field.GetValue(instance);
    }

    private static object InvokePrivate(object instance, string methodName, params object[] arguments)
    {
        var method = instance.GetType().GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);
        Assert(method != null, $"Method {methodName} was not found on {instance.GetType().Name}.");

        try
        {
            return method.Invoke(instance, arguments);
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static bool DeferredWorkerIsRunning(object indexer)
    {
        object indexerSync = GetPrivateField<object>(indexer, "_sync");
        lock (indexerSync)
            return GetPrivateField<bool>(indexer, "_workerRunning");
    }

    private static int DeferredQueueCount(object indexer)
    {
        object indexerSync = GetPrivateField<object>(indexer, "_sync");
        lock (indexerSync)
        {
            var queue = GetPrivateField<DBreeze.LianaTrie.LTrie>(indexer, "_lTrie");
            return checked((int)queue.Count(true));
        }
    }

    private static bool TextSearchContains(
        DBreezeEngine engine,
        string tableName,
        string word,
        byte[] expectedDocumentId)
    {
        using var transaction = engine.GetTransaction();
        return transaction.TextSearch(tableName)
            .Block(containsWords: word)
            .GetDocumentIDs()
            .Any(documentId => documentId.AsSpan().SequenceEqual(expectedDocumentId));
    }

    private static void AssertEventually(
        Func<bool> condition,
        string message,
        int timeoutMilliseconds = 10_000)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
        {
            if (condition())
                return;
            Thread.Sleep(10);
        }

        if (!condition())
            throw new InvalidOperationException(message);
    }

    private static void WriteDeferredQueueRows(
        string folder,
        params (byte[] Key, byte[] Value)[] rows)
    {
        using var configuration = new DBreezeConfiguration
        {
            DBreezeDataFolderName = folder,
            Storage = DBreezeConfiguration.eStorage.DISK,
        };
        var storage = new DBreeze.Storage.StorageLayer(
            Path.Combine(folder, "_DBreezeTextIndexer"),
            CreateInternalTrieSettings(),
            configuration);
        using var queue = new DBreeze.LianaTrie.LTrie(storage)
        {
            TableName = "DBreeze.TextIndexer",
        };

        foreach ((byte[] key, byte[] value) in rows)
            queue.Add(key, value);
        queue.Commit();
    }

    private static List<(byte[] Key, byte[] Value)> ReadDeferredQueueRows(string folder)
    {
        using var configuration = new DBreezeConfiguration
        {
            DBreezeDataFolderName = folder,
            Storage = DBreezeConfiguration.eStorage.DISK,
        };
        var storage = new DBreeze.Storage.StorageLayer(
            Path.Combine(folder, "_DBreezeTextIndexer"),
            CreateInternalTrieSettings(),
            configuration);
        using var queue = new DBreeze.LianaTrie.LTrie(storage)
        {
            TableName = "DBreeze.TextIndexer",
        };

        return queue.IterateForward(true, false)
            .Select(static row => (row.Key, row.GetFullValue(true)))
            .ToList();
    }

    private static DBreeze.Storage.TrieSettings CreateInternalTrieSettings()
    {
        var settings = new DBreeze.Storage.TrieSettings();
        var field = typeof(DBreeze.Storage.TrieSettings).GetField(
            "InternalTable",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert(field != null, "TrieSettings.InternalTable field was not found.");
        field.SetValue(settings, true);
        return settings;
    }

    private static byte[] CreateDeferredQueueKey(long sequence, bool vectorTask)
    {
        byte[] sequenceBytes = sequence.To_8_bytes_array_BigEndian();
        if (!vectorTask)
            return sequenceBytes;

        byte[] key = new byte[10];
        Buffer.BlockCopy(sequenceBytes, 0, key, 2, sequenceBytes.Length);
        return key;
    }

    private static long ReadDeferredSequence(byte[] key)
    {
        if (key.Length == 8)
            return key.To_Int64_BigEndian();
        if (key.Length == 10 && key[0] == 0 && key[1] == 0)
        {
            byte[] sequenceBytes = new byte[8];
            Buffer.BlockCopy(key, 2, sequenceBytes, 0, sequenceBytes.Length);
            return sequenceBytes.To_Int64_BigEndian();
        }

        throw new InvalidOperationException($"Unknown deferred queue key: {Format(key)}.");
    }

    private static void VerifyTable(DBreezeEngine engine, SortedDictionary<byte[], byte[]> expected)
    {
        using var transaction = engine.GetTransaction();
        var actual = transaction.SelectForward<byte[], byte[]>("random").ToArray();
        AssertEqual(expected.Count, actual.Length, "Forward iteration count.");
        AssertEqual((ulong)expected.Count, transaction.Count("random"), "Stored record count.");

        int index = 0;
        foreach (var pair in expected)
        {
            AssertSequenceEqual(pair.Key, actual[index].Key, "Forward iteration key order.");
            AssertSequenceEqual(pair.Value, actual[index].Value, "Forward iteration value.");
            index++;
        }

        var backward = transaction.SelectBackward<byte[], byte[]>("random").ToArray();
        AssertEqual(expected.Count, backward.Length, "Backward iteration count.");
        index = 0;
        foreach (var pair in expected.Reverse())
        {
            AssertSequenceEqual(pair.Key, backward[index].Key, "Backward iteration key order.");
            AssertSequenceEqual(pair.Value, backward[index].Value, "Backward iteration value.");
            index++;
        }

        if (actual.Length > 2)
        {
            byte[] start = actual[actual.Length / 3].Key;
            byte[] stop = actual[(actual.Length * 2) / 3].Key;
            byte[][] expectedRange = actual
                .Select(static row => row.Key)
                .Where(key => ByteComparer.Compare(key, start) > 0 && ByteComparer.Compare(key, stop) < 0)
                .ToArray();

            AssertKeys(expectedRange,
                transaction.SelectForwardFromTo<byte[], byte[]>("random", start, false, stop, false)
                    .Select(static row => row.Key),
                "Random-model forward range.");
            AssertKeys(expectedRange.Reverse(),
                transaction.SelectBackwardFromTo<byte[], byte[]>("random", stop, false, start, false)
                    .Select(static row => row.Key),
                "Random-model backward range.");
        }
    }

    private static SortedDictionary<byte[], byte[]> Clone(SortedDictionary<byte[], byte[]> source)
    {
        var clone = new SortedDictionary<byte[], byte[]>(ByteComparer);
        foreach (var pair in source)
            clone.Add(pair.Key.ToArray(), pair.Value.ToArray());
        return clone;
    }

    private static byte[] RandomBytes(Random random, int length)
    {
        byte[] value = new byte[length];
        random.NextBytes(value);
        return value;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}.");
    }

    private static void AssertSequenceEqual(byte[] expected, byte[] actual, string message)
    {
        if (expected == null || actual == null || !expected.AsSpan().SequenceEqual(actual))
            throw new InvalidOperationException($"{message} Expected: {Format(expected)}; actual: {Format(actual)}.");
    }

    private static void AssertKeys(IEnumerable<byte[]> expected, IEnumerable<byte[]> actual, string message)
    {
        byte[][] expectedArray = expected.ToArray();
        byte[][] actualArray = actual.ToArray();
        AssertEqual(expectedArray.Length, actualArray.Length, $"{message} Count.");
        for (int i = 0; i < expectedArray.Length; i++)
            AssertSequenceEqual(expectedArray[i], actualArray[i], $"{message} Key {i}.");
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

        throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
    }

    private static string Format(byte[] value) => value == null ? "<null>" : Convert.ToHexString(value);

    private sealed class LexicographicByteComparer : IComparer<byte[]>
    {
        public int Compare(byte[] x, byte[] y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x == null)
                return -1;
            if (y == null)
                return 1;
            return x.AsSpan().SequenceCompareTo(y);
        }
    }

    private sealed class FailingRemoteCommunicator : IRemoteInstanceCommunicator
    {
        private int _sendCalls;

        internal int SendCalls => Volatile.Read(ref _sendCalls);

        public byte[] Send(byte[] data)
        {
            Interlocked.Increment(ref _sendCalls);
            throw new InvalidOperationException("Injected remote initialization failure.");
        }
    }

    private sealed class FailingJournalTable : DBreeze.Transactions.ITransactable
    {
        internal FailingJournalTable(string tableName) => TableName = tableName;

        public string TableName { get; set; }

        public void ITRCommitFinished() => throw new InvalidOperationException("Simulated process failure.");
        public void ITRCommit() { }
        public void ITRRollBack() { }
        public void ModificationThreadId(int transactionThreadId) { }
        public void SingleCommit() { }
        public void SingleRollback() { }
        public void TransactionIsFinished(int transactionThreadId) { }
    }
}
