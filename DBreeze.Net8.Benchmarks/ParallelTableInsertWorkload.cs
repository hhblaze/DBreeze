using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using DBreeze;
using Microsoft.Data.Sqlite;

namespace DBreeze.Net8.Benchmarks;

internal readonly record struct ParallelTableInsertSpec(
    int Records,
    int TableCount,
    int BatchSize,
    int PayloadBytes,
    string SqliteSynchronous,
    int SqliteBusyTimeoutMilliseconds = 60_000)
{
    internal int RecordsForTable(int tableIndex) =>
        Records / TableCount + (tableIndex < Records % TableCount ? 1 : 0);

    internal int TransactionsForTable(int tableIndex)
    {
        int records = RecordsForTable(tableIndex);
        return (records + BatchSize - 1) / BatchSize;
    }

    internal int ExpectedTransactions()
    {
        int total = 0;
        for (int table = 0; table < TableCount; table++)
            total += TransactionsForTable(table);
        return total;
    }

    internal long GlobalOrdinal(int tableIndex, long localKey)
    {
        long before = (long)tableIndex * (Records / TableCount) + Math.Min(tableIndex, Records % TableCount);
        return before + localKey;
    }

    internal void Validate()
    {
        if (Records <= 0)
            throw new ArgumentOutOfRangeException(nameof(Records));
        if (TableCount <= 0 || TableCount > 64 || TableCount > Records)
            throw new ArgumentOutOfRangeException(nameof(TableCount));
        if (BatchSize <= 0 || BatchSize > Records)
            throw new ArgumentOutOfRangeException(nameof(BatchSize));
        if (PayloadBytes <= 0 || PayloadBytes > 64 * 1024)
            throw new ArgumentOutOfRangeException(nameof(PayloadBytes));
        if (SqliteSynchronous is not ("FULL" or "NORMAL"))
            throw new ArgumentException("SQLite synchronous mode must be FULL or NORMAL.", nameof(SqliteSynchronous));
        if (SqliteBusyTimeoutMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(SqliteBusyTimeoutMilliseconds));
    }
}

internal sealed class ParallelTableInsertResult
{
    internal long Operations { get; init; }
    internal int Transactions { get; init; }
    internal int WorkerCount { get; init; }
    internal long Checksum { get; set; }
    internal double ElapsedMilliseconds { get; init; }
    internal double TransactionCreateMilliseconds { get; init; }
    internal double MutationMilliseconds { get; init; }
    internal double CommitMilliseconds { get; init; }
    internal double DisposeMilliseconds { get; init; }
    internal long AllocatedBytes { get; init; }
}

internal static class ParallelTableInsertWorkload
{
    internal const string Scenario = "Parallel per-table batched insert (20 tables, 50 rows/transaction)";
    private static readonly TimeSpan PreparationTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan WorkloadTimeout = TimeSpan.FromMinutes(15);

    internal static ParallelTableInsertResult RunDbreeze(
        string path,
        ParallelTableInsertSpec spec,
        byte[][] payloads,
        Action prepared = null)
    {
        spec.Validate();
        ValidatePayloads(payloads, spec.PayloadBytes);
        CreateEmptyDirectory(path);

        using var engine = new DBreezeEngine(path);
        PrepareDbreezeTables(engine, spec, payloads);
        prepared?.Invoke();
        ParallelTableInsertResult result = RunWorkers(spec, table =>
            new DbreezeWorker(engine, spec, payloads, table));
        result.Checksum = ExpectedChecksum(spec, payloads);
        VerifyDbreeze(engine, spec, payloads, result);
        return result;
    }

    private static void PrepareDbreezeTables(
        DBreezeEngine engine,
        ParallelTableInsertSpec spec,
        byte[][] payloads)
    {
        // DBreeze has no schema-only table creation API. A committed sentinel lifecycle
        // materializes each empty table before timing, matching SQLite CREATE TABLE setup
        // and keeping concurrent physical-file allocation outside the measured workload.
        for (int table = 0; table < spec.TableCount; table++)
        {
            using (var transaction = engine.GetTransaction())
            {
                transaction.Insert(TableName(table), Int64.MinValue, payloads[0]);
                transaction.Commit();
            }
            using (var transaction = engine.GetTransaction())
            {
                transaction.RemoveKey<long>(TableName(table), Int64.MinValue);
                transaction.Commit();
            }
        }
    }

    internal static ParallelTableInsertResult RunSqlite(
        string path,
        ParallelTableInsertSpec spec,
        byte[][] payloads)
    {
        spec.Validate();
        ValidatePayloads(payloads, spec.PayloadBytes);
        CreateEmptyDirectory(path);
        string file = Path.Combine(path, "database.sqlite");

        using SqliteConnection control = OpenSqlite(file, create: true, spec);
        for (int table = 0; table < spec.TableCount; table++)
            ExecuteNonQuery(control, $"CREATE TABLE {TableName(table)} (k INTEGER NOT NULL PRIMARY KEY, v BLOB NOT NULL);");

        ParallelTableInsertResult result = RunWorkers(spec, table =>
            new SqliteWorker(file, spec, payloads, table));
        result.Checksum = ExpectedChecksum(spec, payloads);
        VerifySqlite(control, spec, payloads, result);
        ExecuteNonQuery(control, "PRAGMA wal_checkpoint(TRUNCATE);");
        return result;
    }

    internal static byte[][] CreatePayloadPool(int payloadBytes)
    {
        if (payloadBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(payloadBytes));
        var result = new byte[1024][];
        for (int index = 0; index < result.Length; index++)
        {
            var value = new byte[payloadBytes];
            uint state = unchecked((uint)(20260826 + index * 2654435761u));
            for (int offset = 0; offset < value.Length; offset++)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                value[offset] = (byte)(state >> 24);
            }
            result[index] = value;
        }
        return result;
    }

    internal static long ExpectedChecksum(ParallelTableInsertSpec spec, byte[][] payloads)
    {
        long checksum = 0;
        for (int table = 0; table < spec.TableCount; table++)
        {
            int records = spec.RecordsForTable(table);
            for (long key = 0; key < records; key++)
            {
                long ordinal = spec.GlobalOrdinal(table, key);
                checksum = AddChecksum(checksum, table, key, Payload(payloads, ordinal));
            }
        }
        return checksum;
    }

    private static ParallelTableInsertResult RunWorkers(
        ParallelTableInsertSpec spec,
        Func<int, IWorker> createWorker)
    {
        var ready = new CountdownEvent(spec.TableCount);
        var completed = new CountdownEvent(spec.TableCount);
        var start = new ManualResetEventSlim(false);
        using var cancellation = new CancellationTokenSource();
        var errors = new ConcurrentQueue<Exception>();
        var metrics = new WorkerMetrics[spec.TableCount];
        var threads = new Thread[spec.TableCount];

        for (int table = 0; table < threads.Length; table++)
        {
            int workerIndex = table;
            threads[table] = new Thread(() =>
            {
                IWorker worker = null;
                bool readySignaled = false;
                bool completedSignaled = false;
                try
                {
                    worker = createWorker(workerIndex);
                    ready.Signal();
                    readySignaled = true;
                    start.Wait(cancellation.Token);
                    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                    WorkerMetrics value = worker.Execute(cancellation.Token);
                    value.AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                    metrics[workerIndex] = value;
                    completed.Signal();
                    completedSignaled = true;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    errors.Enqueue(new TimeoutException($"Parallel table worker {workerIndex} was cancelled."));
                }
                catch (Exception exception)
                {
                    errors.Enqueue(new InvalidOperationException(
                        $"Parallel table worker {workerIndex} failed.", exception));
                }
                finally
                {
                    if (!readySignaled)
                        ready.Signal();
                    if (!completedSignaled)
                        completed.Signal();
                    try { worker?.Dispose(); }
                    catch (Exception exception)
                    {
                        errors.Enqueue(new InvalidOperationException(
                            $"Parallel table worker {workerIndex} cleanup failed.", exception));
                    }
                }
            })
            {
                IsBackground = true,
                Name = "DBreeze parallel table insert " + table.ToString(CultureInfo.InvariantCulture),
            };
            threads[table].Start();
        }

        if (!ready.Wait(PreparationTimeout))
        {
            cancellation.Cancel();
            start.Set();
            JoinWorkers(threads);
            throw new TimeoutException("Parallel table workers did not prepare within one minute.");
        }
        if (!errors.IsEmpty)
        {
            cancellation.Cancel();
            start.Set();
            JoinWorkers(threads);
            throw new AggregateException(errors);
        }

        var stopwatch = Stopwatch.StartNew();
        start.Set();
        if (!completed.Wait(WorkloadTimeout))
        {
            cancellation.Cancel();
            JoinWorkers(threads);
            throw new TimeoutException("Parallel table insert exceeded the 15-minute workload timeout.");
        }
        stopwatch.Stop();
        JoinWorkers(threads);

        if (!errors.IsEmpty)
            throw new AggregateException(errors);

        long operations = 0, allocated = 0;
        int transactions = 0;
        long createTicks = 0, mutationTicks = 0, commitTicks = 0, disposeTicks = 0;
        foreach (WorkerMetrics value in metrics)
        {
            operations += value.Operations;
            transactions += value.Transactions;
            allocated += value.AllocatedBytes;
            createTicks += value.TransactionCreateTicks;
            mutationTicks += value.MutationTicks;
            commitTicks += value.CommitTicks;
            disposeTicks += value.DisposeTicks;
        }

        return new ParallelTableInsertResult
        {
            Operations = operations,
            Transactions = transactions,
            WorkerCount = spec.TableCount,
            ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
            TransactionCreateMilliseconds = ToMilliseconds(createTicks),
            MutationMilliseconds = ToMilliseconds(mutationTicks),
            CommitMilliseconds = ToMilliseconds(commitTicks),
            DisposeMilliseconds = ToMilliseconds(disposeTicks),
            AllocatedBytes = allocated,
        };
    }

    private static void JoinWorkers(IEnumerable<Thread> threads)
    {
        foreach (Thread thread in threads)
            thread.Join();
    }

    private static void VerifyDbreeze(
        DBreezeEngine engine,
        ParallelTableInsertSpec spec,
        byte[][] payloads,
        ParallelTableInsertResult result)
    {
        long count = 0, checksum = 0;
        using var transaction = engine.GetTransaction();
        transaction.ValuesLazyLoadingIsOn = false;
        for (int table = 0; table < spec.TableCount; table++)
        {
            long expectedKey = 0;
            foreach (var row in transaction.SelectForward<long, byte[]>(TableName(table)))
            {
                if (row.Key != expectedKey)
                    throw new InvalidDataException($"DBreeze table {table} key order mismatch: {row.Key} != {expectedKey}.");
                long ordinal = spec.GlobalOrdinal(table, row.Key);
                byte[] expected = Payload(payloads, ordinal);
                if (!row.Exists || row.Value == null || !row.Value.AsSpan().SequenceEqual(expected))
                    throw new InvalidDataException($"DBreeze table {table} payload mismatch at key {row.Key}.");
                checksum = AddChecksum(checksum, table, row.Key, row.Value);
                count++;
                expectedKey++;
            }
            if (expectedKey != spec.RecordsForTable(table))
                throw new InvalidDataException($"DBreeze table {table} count mismatch.");
        }
        VerifyTotals(spec, payloads, result, count, checksum, "DBreeze");
    }

    private static void VerifySqlite(
        SqliteConnection connection,
        ParallelTableInsertSpec spec,
        byte[][] payloads,
        ParallelTableInsertResult result)
    {
        long count = 0, checksum = 0;
        for (int table = 0; table < spec.TableCount; table++)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT k,v FROM {TableName(table)} ORDER BY k ASC;";
            using SqliteDataReader reader = command.ExecuteReader();
            long expectedKey = 0;
            while (reader.Read())
            {
                long key = reader.GetInt64(0);
                if (key != expectedKey)
                    throw new InvalidDataException($"SQLite table {table} key order mismatch: {key} != {expectedKey}.");
                byte[] value = (byte[])reader.GetValue(1);
                long ordinal = spec.GlobalOrdinal(table, key);
                if (!value.AsSpan().SequenceEqual(Payload(payloads, ordinal)))
                    throw new InvalidDataException($"SQLite table {table} payload mismatch at key {key}.");
                checksum = AddChecksum(checksum, table, key, value);
                count++;
                expectedKey++;
            }
            if (expectedKey != spec.RecordsForTable(table))
                throw new InvalidDataException($"SQLite table {table} count mismatch.");
        }
        VerifyTotals(spec, payloads, result, count, checksum, "SQLite");
    }

    private static void VerifyTotals(
        ParallelTableInsertSpec spec,
        byte[][] payloads,
        ParallelTableInsertResult result,
        long count,
        long checksum,
        string provider)
    {
        long expectedChecksum = ExpectedChecksum(spec, payloads);
        int expectedTransactions = spec.ExpectedTransactions();
        if (count != spec.Records || result.Operations != spec.Records)
            throw new InvalidDataException($"{provider} operation count mismatch.");
        if (result.Transactions != expectedTransactions)
            throw new InvalidDataException(
                $"{provider} transaction count mismatch: {result.Transactions} != {expectedTransactions}.");
        if (checksum != expectedChecksum || result.Checksum != expectedChecksum)
            throw new InvalidDataException($"{provider} checksum mismatch.");
    }

    private static byte[] Payload(byte[][] payloads, long globalOrdinal) =>
        payloads[(int)(globalOrdinal & 1023)];

    private static long AddChecksum(long checksum, int table, long key, byte[] value)
    {
        long identity = unchecked(((long)table << 48) ^ key);
        int middle = value.Length / 2;
        long mixed = unchecked(identity * 6364136223846793005L + value.Length * 1442695040888963407L);
        mixed ^= value[0];
        mixed = unchecked(mixed * 1099511628211L) ^ value[middle];
        mixed = unchecked(mixed * 1099511628211L) ^ value[value.Length - 1];
        return unchecked(checksum + mixed);
    }

    private static string TableName(int table) => "mt_" + table.ToString("D2", CultureInfo.InvariantCulture);

    private static void ValidatePayloads(byte[][] payloads, int payloadBytes)
    {
        if (payloads == null || payloads.Length != 1024 || payloads.Any(value => value == null || value.Length != payloadBytes))
            throw new ArgumentException("A deterministic pool of 1024 equally-sized payloads is required.", nameof(payloads));
    }

    private static void CreateEmptyDirectory(string path)
    {
        if (Directory.Exists(path))
            throw new IOException("Benchmark database path already exists: " + path);
        Directory.CreateDirectory(path);
    }

    private static SqliteConnection OpenSqlite(string file, bool create, ParallelTableInsertSpec spec)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = file,
            Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Default,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        ExecuteNonQuery(connection, "PRAGMA busy_timeout=" + spec.SqliteBusyTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture) + ";");
        if (create)
        {
            string journal = Convert.ToString(ExecuteScalar(connection, "PRAGMA journal_mode=WAL;"), CultureInfo.InvariantCulture);
            if (!String.Equals(journal, "wal", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SQLite refused WAL journal mode: " + journal);
        }
        ExecuteNonQuery(connection, "PRAGMA synchronous=" + spec.SqliteSynchronous + ";");
        return connection;
    }

    private static object ExecuteScalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static double ToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private interface IWorker : IDisposable
    {
        WorkerMetrics Execute(CancellationToken cancellationToken);
    }

    private sealed class DbreezeWorker : IWorker
    {
        private readonly DBreezeEngine _engine;
        private readonly ParallelTableInsertSpec _spec;
        private readonly byte[][] _payloads;
        private readonly int _table;

        internal DbreezeWorker(DBreezeEngine engine, ParallelTableInsertSpec spec, byte[][] payloads, int table)
        {
            _engine = engine;
            _spec = spec;
            _payloads = payloads;
            _table = table;
        }

        public WorkerMetrics Execute(CancellationToken cancellationToken)
        {
            var result = new WorkerMetrics();
            string tableName = TableName(_table);
            int records = _spec.RecordsForTable(_table);
            for (int start = 0; start < records; start += _spec.BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long ticks = Stopwatch.GetTimestamp();
                var transaction = _engine.GetTransaction();
                result.TransactionCreateTicks += Stopwatch.GetTimestamp() - ticks;
                try
                {
                    int end = Math.Min(records, start + _spec.BatchSize);
                    ticks = Stopwatch.GetTimestamp();
                    for (long key = start; key < end; key++)
                    {
                        long ordinal = _spec.GlobalOrdinal(_table, key);
                        byte[] value = Payload(_payloads, ordinal);
                        transaction.Insert(tableName, key, value);
                        result.Operations++;
                    }
                    result.MutationTicks += Stopwatch.GetTimestamp() - ticks;
                    ticks = Stopwatch.GetTimestamp();
                    transaction.Commit();
                    result.CommitTicks += Stopwatch.GetTimestamp() - ticks;
                    result.Transactions++;
                }
                finally
                {
                    ticks = Stopwatch.GetTimestamp();
                    transaction.Dispose();
                    result.DisposeTicks += Stopwatch.GetTimestamp() - ticks;
                }
            }
            return result;
        }

        public void Dispose() { }
    }

    private sealed class SqliteWorker : IWorker
    {
        private readonly ParallelTableInsertSpec _spec;
        private readonly byte[][] _payloads;
        private readonly int _table;
        private readonly SqliteConnection _connection;
        private readonly SqliteCommand _command;
        private readonly SqliteParameter _key;
        private readonly SqliteParameter _value;

        internal SqliteWorker(string file, ParallelTableInsertSpec spec, byte[][] payloads, int table)
        {
            _spec = spec;
            _payloads = payloads;
            _table = table;
            _connection = OpenSqlite(file, create: false, spec);
            _command = _connection.CreateCommand();
            _command.CommandText = $"INSERT INTO {TableName(table)}(k,v) VALUES($k,$v);";
            _key = _command.Parameters.Add("$k", SqliteType.Integer);
            _value = _command.Parameters.Add("$v", SqliteType.Blob);
            _command.Prepare();
        }

        public WorkerMetrics Execute(CancellationToken cancellationToken)
        {
            var result = new WorkerMetrics();
            int records = _spec.RecordsForTable(_table);
            for (int start = 0; start < records; start += _spec.BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long ticks = Stopwatch.GetTimestamp();
                SqliteTransaction transaction = _connection.BeginTransaction();
                _command.Transaction = transaction;
                result.TransactionCreateTicks += Stopwatch.GetTimestamp() - ticks;
                try
                {
                    int end = Math.Min(records, start + _spec.BatchSize);
                    ticks = Stopwatch.GetTimestamp();
                    for (long key = start; key < end; key++)
                    {
                        long ordinal = _spec.GlobalOrdinal(_table, key);
                        byte[] value = Payload(_payloads, ordinal);
                        _key.Value = key;
                        _value.Value = value;
                        if (_command.ExecuteNonQuery() != 1)
                            throw new InvalidDataException("SQLite insert affected an unexpected row count.");
                        result.Operations++;
                    }
                    result.MutationTicks += Stopwatch.GetTimestamp() - ticks;
                    ticks = Stopwatch.GetTimestamp();
                    transaction.Commit();
                    result.CommitTicks += Stopwatch.GetTimestamp() - ticks;
                    result.Transactions++;
                }
                finally
                {
                    ticks = Stopwatch.GetTimestamp();
                    transaction.Dispose();
                    result.DisposeTicks += Stopwatch.GetTimestamp() - ticks;
                }
            }
            return result;
        }

        public void Dispose()
        {
            _command.Dispose();
            _connection.Dispose();
        }
    }

    private sealed class WorkerMetrics
    {
        internal long Operations;
        internal int Transactions;
        internal long TransactionCreateTicks;
        internal long MutationTicks;
        internal long CommitTicks;
        internal long DisposeTicks;
        internal long AllocatedBytes;
    }
}
