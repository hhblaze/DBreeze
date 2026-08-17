using DBreeze;
using DBreeze.Utils;

internal static class Program
{
    private static readonly IComparer<byte[]> ByteComparer = new LexicographicByteComparer();
    private static readonly string DatabaseTestRoot = @"D:\Temp\DbreezeDbTest";

    private static int Main()
    {
        (string Name, Action Test)[] tests =
        {
            // This test injects a durable journal marker directly and therefore must run before
            // the legacy process-global in-memory journal has been created and disposed.
            (nameof(JournalPayloadAndCrashRecoveryRemainCompatible), JournalPayloadAndCrashRecoveryRemainCompatible),
            (nameof(RemoveAllResetsEmptyKeyState), RemoveAllResetsEmptyKeyState),
            (nameof(InsertIfAbsentPreservesNestedTable), InsertIfAbsentPreservesNestedTable),
            (nameof(NestedStructuralKeyCacheSurvivesMutationAndRename), NestedStructuralKeyCacheSurvivesMutationAndRename),
            (nameof(PartialValueRangesAreOverflowSafe), PartialValueRangesAreOverflowSafe),
            (nameof(RandomKeySorterKeepsFinalOperation), RandomKeySorterKeepsFinalOperation),
            (nameof(RandomKeySorterRollbackDropsPendingOperations), RandomKeySorterRollbackDropsPendingOperations),
            (nameof(RandomKeySorterUsesValueConversionAndNeverAutoFlushes), RandomKeySorterUsesValueConversionAndNeverAutoFlushes),
            (nameof(ObjectInsertNewEntityDoesNotDependOnRksLimit), ObjectInsertNewEntityDoesNotDependOnRksLimit),
            (nameof(ObjectIdentityRemainsBufferedUntilCommit), ObjectIdentityRemainsBufferedUntilCommit),
            (nameof(SelectDirectOnMissingTableIsEmpty), SelectDirectOnMissingTableIsEmpty),
            (nameof(MutationsAreRejectedOnAnotherThread), MutationsAreRejectedOnAnotherThread),
            (nameof(CoordinatorDoesNotLoseWakeups), CoordinatorDoesNotLoseWakeups),
            (nameof(MultiSelectMergesAndKeepsTieOrder), MultiSelectMergesAndKeepsTieOrder),
            (nameof(LockedTransactionsRespectExclusiveWaiter), LockedTransactionsRespectExclusiveWaiter),
            (nameof(LockedTransactionCanBeDisposedOnAnotherThread), LockedTransactionCanBeDisposedOnAnotherThread),
            (nameof(DictionaryAndHashSetReplacementRemoveMissingKeys), DictionaryAndHashSetReplacementRemoveMissingKeys),
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
        byte[] key = { 1 };
        byte[] value = Enumerable.Range(0, 10).Select(static x => (byte)x).ToArray();

        using (var transaction = engine.GetTransaction())
        {
            transaction.Insert("partial", key, value);
            transaction.Insert<byte[], byte[]>("partial", new byte[] { 2 }, null);
            transaction.Commit();
        }

        using (var transaction = engine.GetTransaction())
        {
            var lazyRow = transaction.Select<byte[], byte[]>("partial", key);
            AssertSequenceEqual(value.AsSpan(2).ToArray(), lazyRow.GetValuePart(2, uint.MaxValue),
                "Lazy partial read with overflowing requested length.");
            Assert(lazyRow.GetValuePart(uint.MaxValue, uint.MaxValue) == null,
                "Out-of-range lazy partial read must return null.");
            AssertEqual(0, lazyRow.GetValuePart(uint.MaxValue, 0).Length,
                "Zero-length partial read must remain empty.");
            Assert(transaction.Select<byte[], byte[]>("partial", new byte[] { 2 }).Value == null,
                "Lazy null value was not preserved.");
        }

        using (var transaction = engine.GetTransaction())
        {
            transaction.ValuesLazyLoadingIsOn = false;
            var eagerRow = transaction.Select<byte[], byte[]>("partial", key);
            AssertSequenceEqual(value.AsSpan(2).ToArray(), eagerRow.GetValuePart(2),
                "Eager GetValuePart(startIndex) ignored startIndex.");
            Assert(eagerRow.GetValuePart(uint.MaxValue, uint.MaxValue) == null,
                "Out-of-range eager partial read must return null.");
            Assert(transaction.Select<byte[], byte[]>("partial", new byte[] { 2 }).Value == null,
                "Eager null value was not preserved.");
        }

        using (var transaction = engine.GetTransaction())
        {
            AssertThrows<ArgumentOutOfRangeException>(() =>
                transaction.InsertPart("partial", key, new byte[] { 1 }, uint.MaxValue));
        }
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
