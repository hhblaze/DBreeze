using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using DBreeze;
using DBreeze.DataTypes;

namespace DBreeze.Net8.Benchmarks;

internal sealed class HistoricalBenchmarkSuite
{
    private const string SequentialTable = "t1";
    private const string DateTimeTable = "t2";
    private const string UpdateTable = "t3";
    private const string RandomTable = "t5";
    private const long DateStepTicks = TimeSpan.TicksPerSecond * 7;

    private static readonly DateTime BaseDate = new(1970, 1, 1);

    private readonly HistoricalBenchmarkOptions _options;
    private readonly string _runDirectory;
    private readonly string _databaseDirectory;
    private readonly string _progressPath;
    private readonly HistoricalBenchmarkReport _report;
    private readonly int _oneMillion;
    private readonly int _tenMillion;
    private readonly int _hundredThousand;
    private readonly int _twoHundredThousand;
    private readonly int[] _randomBulkKeys;
    private readonly int[] _randomCommitKeys;
    private readonly long _randomBulkUniqueCount;
    private readonly long _randomCommitUniqueCount;

    private HistoricalBenchmarkSuite(HistoricalBenchmarkOptions options)
    {
        _options = options;
        _runDirectory = Path.Combine(options.RootPath, options.RunId);
        _databaseDirectory = Path.Combine(_runDirectory, "databases");
        _progressPath = Path.Combine(_runDirectory, "progress.log");

        if (Directory.Exists(_runDirectory))
            throw new IOException($"Run directory already exists and will not be overwritten: {_runDirectory}");

        Directory.CreateDirectory(_databaseDirectory);
        _report = new HistoricalBenchmarkReport
        {
            Metadata = HistoricalBenchmarkMetadata.Create(options, _runDirectory),
        };

        _oneMillion = options.Smoke ? 10_000 : 1_000_000;
        _tenMillion = options.Smoke ? 100_000 : 10_000_000;
        _hundredThousand = options.Smoke ? 1_000 : 100_000;
        _twoHundredThousand = options.Smoke ? 2_000 : 200_000;
        if (options.SkipOnly)
        {
            _randomBulkKeys = Array.Empty<int>();
            _randomCommitKeys = Array.Empty<int>();
        }
        else
        {
            _randomBulkKeys = CreateRandomKeys(_oneMillion, _oneMillion, 42);
            _randomCommitKeys = _randomBulkKeys.AsSpan(0, _twoHundredThousand).ToArray();
            _randomBulkUniqueCount = _randomBulkKeys.Distinct().LongCount();
            _randomCommitUniqueCount = _randomCommitKeys.Distinct().LongCount();
        }
    }

    internal static int Run(string[] args)
    {
        HistoricalBenchmarkOptions options;
        try
        {
            options = HistoricalBenchmarkOptions.Parse(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        HistoricalBenchmarkSuite suite;
        try
        {
            suite = new HistoricalBenchmarkSuite(options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }

        return suite.RunCore();
    }

    private int RunCore()
    {
        try
        {
            string suiteName = _options.SkipOnly ? "historical-skip" : "historical-core";
            Log($"START {suiteName}; smoke={_options.Smoke}; repetitions={_options.Repetitions}; run={_runDirectory}");
            PersistReports();

            if (_options.SkipOnly)
            {
                RunSkipOnlyScenarios(PrepareSkipDatabase());
            }
            else
            {
                RunBulkIntInsert();
                RunBulkDateTimeInsert("BulkInsertDateTime1M", _oneMillion);
                string readDatabasePath = RunBulkDateTimeInsert("BulkInsertDateTime10M", _tenMillion);
                RunUpdate(commitEach: false);
                RunRandomInsert(commitEach: false);
                RunReadScenarios(readDatabasePath);
                RunParallelWriters();
                if (_options.SkipDurableCommits)
                {
                    Log("SKIP durable commit-per-record workloads");
                }
                else
                {
                    // Durable commit-per-record workloads are intentionally last. They take orders of
                    // magnitude longer and must not delay the read/iteration baseline or its reports.
                    RunCommitEachInsert();
                    RunUpdate(commitEach: true);
                    RunRandomInsert(commitEach: true);
                }
            }

            _report.Metadata.CompletedUtc = DateTime.UtcNow;
            PersistReports();
            Log("COMPLETE " + suiteName);
            return 0;
        }
        catch (Exception ex)
        {
            _report.Metadata.CompletedUtc = DateTime.UtcNow;
            _report.Metadata.Failure = ex.ToString();
            PersistReports();
            Log("FAILED " + ex);
            return 1;
        }
    }

    private void RunBulkIntInsert()
    {
        RunMutatingScenario("Write.Sequential", "BulkInsertInt1M", _oneMillion, databasePath =>
        {
            var engine = new DBreezeEngine(databasePath);
            var transaction = engine.GetTransaction();

            return new PreparedHistoricalOperation(
                execute: () =>
                {
                    for (int i = 0; i < _oneMillion; i++)
                        transaction.Insert<int, byte[]>(SequentialTable, i, null);
                    transaction.Commit();
                    return new HistoricalOperationOutcome(_oneMillion, _oneMillion);
                },
                verify: outcome => EnsureCount(transaction, SequentialTable, _oneMillion, outcome),
                dispose: () =>
                {
                    transaction.Dispose();
                    engine.Dispose();
                });
        });
    }

    private string RunBulkDateTimeInsert(string scenario, int recordCount)
    {
        string lastMeasuredPath = null;
        RunMutatingScenario("Write.Sequential", scenario, recordCount, databasePath =>
        {
            var engine = new DBreezeEngine(databasePath);
            var transaction = engine.GetTransaction();

            return new PreparedHistoricalOperation(
                execute: () =>
                {
                    for (int i = 0; i < recordCount; i++)
                        transaction.Insert<DateTime, byte[]>(DateTimeTable, DateAt(i), null);
                    transaction.Commit();
                    return new HistoricalOperationOutcome(recordCount, recordCount);
                },
                verify: outcome => EnsureCount(transaction, DateTimeTable, recordCount, outcome),
                dispose: () =>
                {
                    transaction.Dispose();
                    engine.Dispose();
                });
        }, measuredPath => lastMeasuredPath = measuredPath);

        return lastMeasuredPath
            ?? throw new InvalidOperationException($"No measured database was produced for {scenario}.");
    }

    private string PrepareSkipDatabase()
    {
        string databasePath = CreateDatabasePath("Setup", "DateTime10M", "prepared");
        Log($"PREPARE skip database; rows={_tenMillion:N0}; path={databasePath}");
        using (var engine = new DBreezeEngine(databasePath))
        using (var transaction = engine.GetTransaction())
        {
            for (int i = 0; i < _tenMillion; i++)
                transaction.Insert<DateTime, byte[]>(DateTimeTable, DateAt(i), null);
            transaction.Commit();
            Ensure((long)transaction.Count(DateTimeTable) == _tenMillion,
                $"Prepared Skip database must contain {_tenMillion} rows.");
        }

        Log("PREPARED skip database");
        return databasePath;
    }

    private void RunSkipOnlyScenarios(string databasePath)
    {
        Log($"READ DATABASE {databasePath}");
        using var engine = new DBreezeEngine(databasePath);
        using (var transaction = engine.GetTransaction())
        {
            Ensure((long)transaction.Count(DateTimeTable) == _tenMillion,
                $"Read database must contain {_tenMillion} rows.");
        }

        RunSkipScenarios(engine, databasePath, _hundredThousand);
    }

    private void RunCommitEachInsert()
    {
        RunMutatingScenario("Write.Sequential", "InsertCommitEach100K", _hundredThousand, databasePath =>
        {
            var engine = new DBreezeEngine(databasePath);
            var transaction = engine.GetTransaction();

            return new PreparedHistoricalOperation(
                execute: () =>
                {
                    for (int i = 0; i < _hundredThousand; i++)
                    {
                        transaction.Insert<DateTime, byte[]>(UpdateTable, DateAt(i), null);
                        transaction.Commit();
                    }

                    return new HistoricalOperationOutcome(_hundredThousand, _hundredThousand);
                },
                verify: outcome => EnsureCount(transaction, UpdateTable, _hundredThousand, outcome),
                dispose: () =>
                {
                    transaction.Dispose();
                    engine.Dispose();
                });
        });
    }

    private void RunUpdate(bool commitEach)
    {
        string scenario = commitEach ? "UpdateCommitEach100K" : "BulkUpdate100K";
        RunMutatingScenario("Write.Update", scenario, _hundredThousand, databasePath =>
        {
            var engine = new DBreezeEngine(databasePath);
            using (var seedTransaction = engine.GetTransaction())
            {
                for (int i = 0; i < _hundredThousand; i++)
                    seedTransaction.Insert<DateTime, byte[]>(UpdateTable, DateAt(i), null);
                seedTransaction.Commit();
            }

            var transaction = engine.GetTransaction();
            return new PreparedHistoricalOperation(
                execute: () =>
                {
                    for (int i = 0; i < _hundredThousand; i++)
                    {
                        transaction.Insert<DateTime, byte[]>(UpdateTable, DateAt(i), null);
                        if (commitEach)
                            transaction.Commit();
                    }

                    if (!commitEach)
                        transaction.Commit();
                    return new HistoricalOperationOutcome(_hundredThousand, _hundredThousand);
                },
                verify: outcome => EnsureCount(transaction, UpdateTable, _hundredThousand, outcome),
                dispose: () =>
                {
                    transaction.Dispose();
                    engine.Dispose();
                });
        });
    }

    private void RunRandomInsert(bool commitEach)
    {
        int[] keys = commitEach ? _randomCommitKeys : _randomBulkKeys;
        long expectedCount = commitEach ? _randomCommitUniqueCount : _randomBulkUniqueCount;
        string scenario = commitEach ? "RandomInsertCommitEach200K" : "RandomBulkInsert1M";

        RunMutatingScenario("Write.Random", scenario, keys.LongLength, databasePath =>
        {
            var engine = new DBreezeEngine(databasePath);
            var transaction = engine.GetTransaction();
            return new PreparedHistoricalOperation(
                execute: () =>
                {
                    foreach (int key in keys)
                    {
                        transaction.Insert<int, byte[]>(RandomTable, key, null);
                        if (commitEach)
                            transaction.Commit();
                    }

                    if (!commitEach)
                        transaction.Commit();
                    return new HistoricalOperationOutcome(keys.LongLength, expectedCount);
                },
                verify: outcome =>
                {
                    Ensure(outcome.Checksum == expectedCount,
                        $"Unexpected deterministic unique-key count for {scenario}: {outcome.Checksum}.");
                    Ensure((long)transaction.Count(RandomTable) == expectedCount,
                        $"Unexpected table count for {scenario}.");
                },
                dispose: () =>
                {
                    transaction.Dispose();
                    engine.Dispose();
                });
        });
    }

    private void RunReadScenarios(string databasePath)
    {
        Log($"READ DATABASE {databasePath}");
        using var engine = new DBreezeEngine(databasePath);
        using (var transaction = engine.GetTransaction())
        {
            Ensure((long)transaction.Count(DateTimeTable) == _tenMillion,
                $"Read database must contain {_tenMillion} rows.");
        }

        int[] takeCounts = _options.Smoke
            ? new[] { 10, 100, 1_000, 10_000 }
            : new[] { 1_000, 10_000, 100_000, 1_000_000 };

        foreach (int take in takeCounts)
        {
            foreach (bool forward in new[] { true, false })
            {
                foreach (bool readValue in new[] { false, true })
                {
                    string scenario = $"{Direction(forward)}Take{take}{ValueSuffix(readValue)}";
                    RunReadScenario(engine, databasePath, "Read.Scan", scenario, take, () =>
                    {
                        var transaction = engine.GetTransaction();
                        return new PreparedHistoricalOperation(
                            execute: () => Consume(
                                forward
                                    ? transaction.SelectForward<DateTime, byte[]>(DateTimeTable)
                                    : transaction.SelectBackward<DateTime, byte[]>(DateTimeTable),
                                take,
                                readValue),
                            verify: outcome => EnsureOutcomeCount(outcome, take, scenario),
                            dispose: transaction.Dispose);
                    });
                }
            }
        }

        (string Label, DateTime Value)[] starts = GetStartDates();
        int readTake = _hundredThousand;
        foreach ((string label, DateTime start) in starts)
        {
            foreach (bool forward in new[] { true, false })
            {
                long available = forward ? ForwardAvailable(start, includeExact: true) : BackwardAvailable(start, includeExact: true);
                long expected = Math.Min(readTake, available);
                string scenario = $"{Direction(forward)}StartFrom{label}Take{readTake}Value";
                RunReadScenario(engine, databasePath, "Read.StartFrom", scenario, expected, () =>
                {
                    var transaction = engine.GetTransaction();
                    return new PreparedHistoricalOperation(
                        execute: () => Consume(
                            forward
                                ? transaction.SelectForwardStartFrom<DateTime, byte[]>(DateTimeTable, start, true)
                                : transaction.SelectBackwardStartFrom<DateTime, byte[]>(DateTimeTable, start, true),
                            readTake,
                            readValue: true),
                        verify: outcome => EnsureOutcomeCount(outcome, expected, scenario),
                        dispose: transaction.Dispose);
                });
            }
        }

        RunRangeScenarios(engine, databasePath, starts, readTake);
        RunSkipScenarios(engine, databasePath, readTake);
        RunSkipFromScenarios(engine, databasePath, starts, readTake);
        RunPointReadScenarios(engine, databasePath);
    }

    private void RunRangeScenarios(
        DBreezeEngine engine,
        string databasePath,
        IEnumerable<(string Label, DateTime Value)> starts,
        int take)
    {
        foreach ((string label, DateTime start) in starts)
        {
            foreach (bool forward in new[] { true, false })
            {
                DateTime stop = _options.Smoke
                    ? DateAt(ClampIndex(IndexAtOrAfter(start) + (forward ? _tenMillion / 20 : -_tenMillion / 20)))
                    : start.AddMonths(forward ? 1 : -1);
                long rangeCount = RangeCount(forward ? start : stop, forward ? stop : start);

                foreach (bool takeOnly in new[] { false, true })
                {
                    int? takeCount = takeOnly ? take : null;
                    long expected = takeOnly ? Math.Min(take, rangeCount) : rangeCount;
                    string scenario = $"{Direction(forward)}FromTo{label}{(takeOnly ? $"Take{take}" : "Full")}Value";
                    RunReadScenario(engine, databasePath, "Read.Range", scenario, expected, () =>
                    {
                        var transaction = engine.GetTransaction();
                        return new PreparedHistoricalOperation(
                            execute: () => Consume(
                                forward
                                    ? transaction.SelectForwardFromTo<DateTime, byte[]>(DateTimeTable, start, true, stop, true)
                                    : transaction.SelectBackwardFromTo<DateTime, byte[]>(DateTimeTable, start, true, stop, true),
                                takeCount,
                                readValue: true),
                            verify: outcome => EnsureOutcomeCount(outcome, expected, scenario),
                            dispose: transaction.Dispose);
                    });
                }
            }
        }
    }

    private void RunSkipScenarios(DBreezeEngine engine, string databasePath, int take)
    {
        long[] skipCounts = _options.Smoke
            ? new[] { _tenMillion * 3L / 10, _tenMillion * 6L / 10, _tenMillion * 9L / 10 }
            : new[] { 3_000_000L, 6_000_000L, 9_000_000L };

        foreach (long skip in skipCounts)
        {
            foreach (bool forward in new[] { true, false })
            {
                foreach (bool readValue in new[] { false, true })
                {
                    long expected = Math.Min(take, Math.Max(0, _tenMillion - skip));
                    string scenario = $"{Direction(forward)}Skip{skip}Take{take}{ValueSuffix(readValue)}";
                    RunReadScenario(engine, databasePath, "Read.Skip", scenario, skip + expected, () =>
                    {
                        var transaction = engine.GetTransaction();
                        return new PreparedHistoricalOperation(
                            execute: () => Consume(
                                forward
                                    ? transaction.SelectForwardSkip<DateTime, byte[]>(DateTimeTable, (ulong)skip)
                                    : transaction.SelectBackwardSkip<DateTime, byte[]>(DateTimeTable, (ulong)skip),
                                take,
                                readValue),
                            verify: outcome => EnsureOutcomeCount(outcome, expected, scenario),
                            dispose: transaction.Dispose);
                    });
                }
            }
        }
    }

    private void RunSkipFromScenarios(
        DBreezeEngine engine,
        string databasePath,
        IEnumerable<(string Label, DateTime Value)> starts,
        int take)
    {
        long[] skipCounts = _options.Smoke
            ? new[] { _tenMillion / 100L, _tenMillion / 10L }
            : new[] { 100_000L, 1_000_000L };

        foreach ((string label, DateTime start) in starts)
        {
            foreach (long skip in skipCounts)
            {
                foreach (bool forward in new[] { true, false })
                {
                    long available = forward
                        ? ForwardAvailable(start, includeExact: false)
                        : BackwardAvailable(start, includeExact: false);
                    long expected = Math.Min(take, Math.Max(0, available - skip));
                    string scenario = $"{Direction(forward)}SkipFrom{label}Skip{skip}Take{take}Value";
                    RunReadScenario(engine, databasePath, "Read.SkipFrom", scenario, skip + expected, () =>
                    {
                        var transaction = engine.GetTransaction();
                        return new PreparedHistoricalOperation(
                            execute: () => Consume(
                                forward
                                    ? transaction.SelectForwardSkipFrom<DateTime, byte[]>(DateTimeTable, start, (ulong)skip)
                                    : transaction.SelectBackwardSkipFrom<DateTime, byte[]>(DateTimeTable, start, (ulong)skip),
                                take,
                                readValue: true),
                            verify: outcome => EnsureOutcomeCount(outcome, expected, scenario),
                            dispose: transaction.Dispose);
                    });
                }
            }
        }
    }

    private void RunPointReadScenarios(DBreezeEngine engine, string databasePath)
    {
        int[] requestCounts = _options.Smoke
            ? new[] { 100, 1_000, 10_000 }
            : new[] { 10_000, 100_000, 1_000_000 };
        DateTime[] keys = CreatePointReadKeys(requestCounts[^1]);

        foreach (int requestCount in requestCounts)
        {
            foreach (bool readValue in new[] { false, true })
            {
                string scenario = $"PointRead{requestCount}{ValueSuffix(readValue)}";
                RunReadScenario(engine, databasePath, "Read.Point", scenario, requestCount, () =>
                {
                    var transaction = engine.GetTransaction();
                    return new PreparedHistoricalOperation(
                        execute: () =>
                        {
                            long found = 0;
                            long checksum = 0;
                            for (int i = 0; i < requestCount; i++)
                            {
                                Row<DateTime, byte[]> row = transaction.Select<DateTime, byte[]>(DateTimeTable, keys[i]);
                                if (!row.Exists)
                                    continue;
                                found++;
                                if (readValue)
                                {
                                    byte[] value = row.Value;
                                    checksum += value?.Length ?? 1;
                                }
                                else
                                {
                                    checksum++;
                                }
                            }

                            return new HistoricalOperationOutcome(found, checksum);
                        },
                        verify: outcome => EnsureOutcomeCount(outcome, requestCount, scenario),
                        dispose: transaction.Dispose);
                });
            }
        }
    }

    private void RunParallelWriters()
    {
        const int workerCount = 6;
        int recordsPerWorker = _oneMillion;
        long totalOperations = (long)workerCount * recordsPerWorker;

        RunMutatingScenario("Write.Parallel", "SixTablesBulkInsert", totalOperations, databasePath =>
        {
            var engine = new DBreezeEngine(databasePath);
            var ready = new CountdownEvent(workerCount);
            var start = new ManualResetEventSlim(false);
            var errors = new ConcurrentQueue<Exception>();
            var threads = new Thread[workerCount];

            for (int workerIndex = 0; workerIndex < workerCount; workerIndex++)
            {
                int capturedIndex = workerIndex;
                threads[workerIndex] = new Thread(() =>
                {
                    ready.Signal();
                    start.Wait();
                    try
                    {
                        using var transaction = engine.GetTransaction();
                        string table = "parallel-" + capturedIndex.ToString(CultureInfo.InvariantCulture);
                        for (int i = 0; i < recordsPerWorker; i++)
                            transaction.Insert<int, int>(table, i, i);
                        transaction.Commit();
                    }
                    catch (Exception ex)
                    {
                        errors.Enqueue(ex);
                    }
                })
                {
                    IsBackground = true,
                    Name = "DBreeze benchmark writer " + capturedIndex.ToString(CultureInfo.InvariantCulture),
                };
                threads[workerIndex].Start();
            }

            if (!ready.Wait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("Parallel benchmark workers did not become ready in time.");

            return new PreparedHistoricalOperation(
                execute: () =>
                {
                    start.Set();
                    foreach (Thread thread in threads)
                        thread.Join();
                    if (!errors.IsEmpty)
                        throw new AggregateException(errors);
                    return new HistoricalOperationOutcome(totalOperations, totalOperations);
                },
                verify: outcome =>
                {
                    using var transaction = engine.GetTransaction();
                    for (int i = 0; i < workerCount; i++)
                    {
                        string table = "parallel-" + i.ToString(CultureInfo.InvariantCulture);
                        Ensure((long)transaction.Count(table) == recordsPerWorker,
                            $"Parallel table {table} has an unexpected row count.");
                    }
                },
                dispose: () =>
                {
                    start.Set();
                    foreach (Thread thread in threads)
                    {
                        if (thread.IsAlive)
                            thread.Join();
                    }
                    ready.Dispose();
                    start.Dispose();
                    engine.Dispose();
                });
        });
    }

    private void RunMutatingScenario(
        string category,
        string scenario,
        long operations,
        Func<string, PreparedHistoricalOperation> prepare,
        Action<string> measuredDatabaseCreated = null)
    {
        for (int run = 0; run <= _options.Repetitions; run++)
        {
            bool warmup = run == 0;
            string phase = warmup ? "warmup" : "measure-" + run.ToString(CultureInfo.InvariantCulture);
            string databasePath = CreateDatabasePath(category, scenario, phase);
            ExecuteMeasurement(category, scenario, phase, warmup, run, operations, databasePath, prepare);
            if (!warmup)
                measuredDatabaseCreated?.Invoke(databasePath);
        }
    }

    private void RunReadScenario(
        DBreezeEngine engine,
        string databasePath,
        string category,
        string scenario,
        long operations,
        Func<PreparedHistoricalOperation> prepare)
    {
        _ = engine;
        for (int run = 0; run <= _options.Repetitions; run++)
        {
            bool warmup = run == 0;
            string phase = warmup ? "warmup" : "measure-" + run.ToString(CultureInfo.InvariantCulture);
            ExecuteMeasurement(category, scenario, phase, warmup, run, operations, databasePath, _ => prepare());
        }
    }

    private void ExecuteMeasurement(
        string category,
        string scenario,
        string phase,
        bool warmup,
        int iteration,
        long operations,
        string databasePath,
        Func<string, PreparedHistoricalOperation> prepare)
    {
        var measurement = new HistoricalBenchmarkMeasurement
        {
            Category = category,
            Scenario = scenario,
            Phase = phase,
            IsWarmup = warmup,
            Iteration = iteration,
            Operations = operations,
            DatabasePath = databasePath,
            StartedUtc = DateTime.UtcNow,
        };

        PreparedHistoricalOperation prepared = null;
        Exception failure = null;
        try
        {
            prepared = prepare(databasePath);
            CollectGarbage();
            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
            int gen0Before = GC.CollectionCount(0);
            int gen1Before = GC.CollectionCount(1);
            int gen2Before = GC.CollectionCount(2);

            HistoricalOperationOutcome outcome = default;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                outcome = prepared.Execute();
            }
            finally
            {
                stopwatch.Stop();
                measurement.ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                measurement.AllocatedBytes = Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore);
                measurement.Gen0Collections = GC.CollectionCount(0) - gen0Before;
                measurement.Gen1Collections = GC.CollectionCount(1) - gen1Before;
                measurement.Gen2Collections = GC.CollectionCount(2) - gen2Before;
            }

            measurement.ReturnedCount = outcome.Count;
            measurement.Checksum = outcome.Checksum;
            measurement.OperationsPerSecond = measurement.ElapsedMilliseconds > 0
                ? operations * 1000d / measurement.ElapsedMilliseconds
                : 0;
            prepared.Verify(outcome);
            measurement.Succeeded = true;
        }
        catch (Exception ex)
        {
            failure = ex;
            measurement.Error = ex.ToString();
            measurement.Succeeded = false;
        }
        finally
        {
            try
            {
                prepared?.Dispose();
            }
            catch (Exception ex)
            {
                failure ??= ex;
                measurement.Succeeded = false;
                measurement.Error = string.IsNullOrEmpty(measurement.Error)
                    ? ex.ToString()
                    : measurement.Error + Environment.NewLine + ex;
            }

            measurement.DatabaseBytes = GetDirectorySize(databasePath);
            Record(measurement);
        }

        if (failure != null)
            throw new InvalidOperationException($"Benchmark scenario failed: {scenario} / {phase}", failure);
    }

    private static HistoricalOperationOutcome Consume(
        IEnumerable<Row<DateTime, byte[]>> rows,
        int? take,
        bool readValue)
    {
        IEnumerable<Row<DateTime, byte[]>> selected = take.HasValue ? rows.Take(take.Value) : rows;
        long count = 0;
        long checksum = 0;
        foreach (Row<DateTime, byte[]> row in selected)
        {
            count++;
            if (readValue)
            {
                byte[] value = row.Value;
                checksum += value?.Length ?? 1;
            }
            else
            {
                checksum++;
            }
        }

        return new HistoricalOperationOutcome(count, checksum);
    }

    private DateTime[] CreatePointReadKeys(int count)
    {
        var random = new Random(42);
        var keys = new DateTime[count];
        for (int i = 0; i < keys.Length; i++)
            keys[i] = DateAt(random.Next(_tenMillion));
        return keys;
    }

    private (string Label, DateTime Value)[] GetStartDates()
    {
        if (!_options.Smoke)
        {
            return new[]
            {
                ("1970-07-01", new DateTime(1970, 7, 1)),
                ("1971-06-01", new DateTime(1971, 6, 1)),
                ("1972-01-01", new DateTime(1972, 1, 1)),
            };
        }

        return new[]
        {
            ("P25", DateAt(_tenMillion / 4)),
            ("P50", DateAt(_tenMillion / 2)),
            ("P75", DateAt(_tenMillion * 3 / 4)),
        };
    }

    private long ForwardAvailable(DateTime start, bool includeExact)
    {
        long index = IndexAtOrAfter(start);
        if (!includeExact && IsExactKey(start))
            index++;
        return Math.Max(0, _tenMillion - Math.Clamp(index, 0, _tenMillion));
    }

    private long BackwardAvailable(DateTime start, bool includeExact)
    {
        long index = IndexAtOrBefore(start);
        if (!includeExact && IsExactKey(start))
            index--;
        return Math.Max(0, Math.Min(index, _tenMillion - 1L) + 1);
    }

    private long RangeCount(DateTime lower, DateTime upper)
    {
        long first = Math.Max(0, IndexAtOrAfter(lower));
        long last = Math.Min(_tenMillion - 1L, IndexAtOrBefore(upper));
        return last < first ? 0 : last - first + 1;
    }

    private static long IndexAtOrAfter(DateTime value)
    {
        long delta = value.Ticks - BaseDate.Ticks;
        if (delta <= 0)
            return delta == 0 ? 0 : -((-delta) / DateStepTicks);
        return (delta + DateStepTicks - 1) / DateStepTicks;
    }

    private static long IndexAtOrBefore(DateTime value)
    {
        long delta = value.Ticks - BaseDate.Ticks;
        if (delta >= 0)
            return delta / DateStepTicks;
        return -((-delta + DateStepTicks - 1) / DateStepTicks);
    }

    private static bool IsExactKey(DateTime value)
    {
        long delta = value.Ticks - BaseDate.Ticks;
        return delta >= 0 && delta % DateStepTicks == 0;
    }

    private long ClampIndex(long index) => Math.Clamp(index, 0, _tenMillion - 1L);

    private static DateTime DateAt(long index) => BaseDate.AddTicks(checked(index * DateStepTicks));

    private static int[] CreateRandomKeys(int count, int exclusiveUpperBound, int seed)
    {
        var random = new Random(seed);
        var keys = new int[count];
        for (int i = 0; i < keys.Length; i++)
            keys[i] = random.Next(exclusiveUpperBound);
        return keys;
    }

    private string CreateDatabasePath(string category, string scenario, string phase)
    {
        string path = Path.Combine(
            _databaseDirectory,
            SanitizePathSegment(category),
            SanitizePathSegment(scenario),
            SanitizePathSegment(phase));
        if (Directory.Exists(path))
            throw new IOException($"Benchmark database path already exists: {path}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string SanitizePathSegment(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private void Record(HistoricalBenchmarkMeasurement measurement)
    {
        _report.Measurements.Add(measurement);
        PersistReports();
        Log(string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1}/{2} {3}: {4:F3} ms, {5:N0} ops/s, returned={6:N0}, allocated={7:N0} bytes",
            measurement.Succeeded ? "PASS" : "FAIL",
            measurement.Category,
            measurement.Scenario,
            measurement.Phase,
            measurement.ElapsedMilliseconds,
            measurement.OperationsPerSecond,
            measurement.ReturnedCount,
            measurement.AllocatedBytes));
    }

    private void PersistReports() => HistoricalBenchmarkReportWriter.Write(_report, _runDirectory);

    private void Log(string message)
    {
        string line = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + " " + message;
        Console.WriteLine(line);
        File.AppendAllText(_progressPath, line + Environment.NewLine);
    }

    private static void CollectGarbage()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static long GetDirectorySize(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return 0;

        long size = 0;
        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                size = checked(size + new FileInfo(file).Length);
            }
            catch (FileNotFoundException)
            {
            }
        }
        return size;
    }

    private static void EnsureCount(
        DBreeze.Transactions.Transaction transaction,
        string table,
        long expected,
        HistoricalOperationOutcome outcome)
    {
        Ensure(outcome.Count > 0, $"Scenario {table} reported no processed records.");
        Ensure((long)transaction.Count(table) == expected,
            $"Table {table} contains {transaction.Count(table)} records; expected {expected}.");
    }

    private static void EnsureOutcomeCount(HistoricalOperationOutcome outcome, long expected, string scenario)
    {
        Ensure(outcome.Count == expected,
            $"{scenario} returned {outcome.Count} rows; expected {expected}.");
        Ensure(outcome.Checksum == expected,
            $"{scenario} checksum is {outcome.Checksum}; expected {expected}.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static string Direction(bool forward) => forward ? "Forward" : "Backward";
    private static string ValueSuffix(bool readValue) => readValue ? "Value" : "Lazy";
}
