/*
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using DBreeze.LianaTrie;
using DBreeze.Storage;
using DBreeze.Utils;

namespace DBreeze.TextSearch
{
    /// <summary>
    /// Durable queue and single background worker for deferred text indexing.
    /// </summary>
    internal class TextDeferredIndexer : IDisposable
    {
        private const string TableFileName = "_DBreezeTextIndexer";
        private const int MaximalTextTasksPerRound = 10;
        private const int MaximalVectorsPerRound = 500;

        private static readonly byte[] OtherIndexersPrefix = { 0 };

        private readonly DBreezeEngine _engine;
        private readonly object _sync = new object();

        private LTrie _lTrie;
        private long _sequence;
        private Task _workerTask;
        private bool _workerRunning;
        private bool _restartRequested;
        private int _stopRequested;
        private int _disposed;

        public TextDeferredIndexer(DBreezeEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));

            try
            {
                OpenQueue();

                if (_lTrie.Storage.Length > 100000 && _lTrie.Count(true) == 0)
                {
                    _lTrie.Storage.RecreateFiles();
                    _lTrie.Dispose();
                    _lTrie = null;
                    OpenQueue();
                }

                _sequence = ReadInitialSequence();
            }
            catch
            {
                try { _lTrie?.Dispose(); } catch { }
                _lTrie = null;
                throw;
            }
        }

        private void OpenQueue()
        {
            var settings = new TrieSettings
            {
                InternalTable = true
            };
            var storage = new StorageLayer(
                Path.Combine(_engine.MainFolder, TableFileName),
                settings,
                _engine.Configuration);
            _lTrie = new LTrie(storage)
            {
                TableName = "DBreeze.TextIndexer"
            };
        }

        /// <summary>
        /// Stops accepting work and asks the active worker to leave at the next safe boundary.
        /// The durable queue is kept intact for the next engine start.
        /// </summary>
        internal void RequestStop()
        {
            lock (_sync)
            {
                Volatile.Write(ref _stopRequested, 1);
                _restartRequested = false;
            }
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                return;

            Task workerTask;
            lock (_sync)
            {
                Volatile.Write(ref _stopRequested, 1);
                _restartRequested = false;
                workerTask = _workerTask;
            }

            try
            {
                workerTask?.GetAwaiter().GetResult();
            }
            finally
            {
                lock (_sync)
                {
                    _lTrie?.Dispose();
                    _lTrie = null;
                }
            }
        }

        /// <summary>
        /// Adds tables and their internal document IDs for parallel indexing of the Text Engine.
        /// </summary>
        public void Add(Dictionary<string, HashSet<uint>> defferedDocIds)
        {
            Enqueue(defferedDocIds, vectorTask: false);
        }

        /// <summary>
        /// Queues the legacy vector-work record format (protocol 0,0).
        /// </summary>
        /// <remarks>
        /// Public VectorsInsert overloads build HNSW synchronously and never call this method.
        /// VectorsDoIndexing is currently a compatibility no-op, so this queue path does not perform
        /// HNSW indexing and must not be used as a production indexing path.
        /// </remarks>
        public void AddVectors(Dictionary<string, HashSet<uint>> defferedDocIds)
        {
            Enqueue(defferedDocIds, vectorTask: true);
        }

        private void Enqueue(Dictionary<string, HashSet<uint>> defferedDocIds, bool vectorTask)
        {
            if (defferedDocIds == null || defferedDocIds.Count == 0)
                return;

            byte[] payload = DBreeze.Utils.Biser.Encode_DICT_PROTO_STRING_UINTHASHSET(
                defferedDocIds,
                Compression.eCompressionMethod.NoCompression);

            lock (_sync)
            {
                ThrowIfNotAcceptingWork();

                byte[] key = CreateUniqueKey(vectorTask);
                _lTrie.Add(key, payload);
                _lTrie.Commit();
            }
        }

        /// <summary>
        /// Schedules indexing. Calls are coalesced while the single worker is active.
        /// </summary>
        public void StartDefferedIndexing()
        {
            lock (_sync)
            {
                if (IsStopping)
                    return;

                if (_workerRunning)
                {
                    _restartRequested = true;
                    return;
                }

                if (!HasPendingRows())
                    return;

                StartWorker();
            }
        }

        private void StartWorker()
        {
            _workerRunning = true;
            _restartRequested = false;

            try
            {
                _workerTask = Task.Run(WorkerEntry);
            }
            catch
            {
                _workerRunning = false;
                _workerTask = null;
                throw;
            }
        }

        private void WorkerEntry()
        {
            _engine.BackgroundNotify("TextDefferedIndexingHasStarted", null);

            try
            {
                Indexer();
            }
            catch (Exception ex)
            {
                if (!IsStopping)
                    _engine.BackgroundNotify("TextDefferedIndexingHasFailed", ex);
            }
            finally
            {
                try
                {
                    _engine.BackgroundNotify("TextDefferedIndexingHasFinished", null);
                }
                finally
                {
                    lock (_sync)
                    {
                        _workerRunning = false;

                        bool restart = _restartRequested;
                        _restartRequested = false;
                        if (!IsStopping && restart && HasPendingRows())
                            StartWorker();
                    }
                }
            }
        }

        private void Indexer()
        {
            var batch = new WorkBatch();

            while (!IsStopping)
            {
                lock (_sync)
                {
                    if (IsStopping)
                        return;

                    batch.Clear();
                    ReadNextBatch(batch);
                }

                if (batch.Kind == WorkKind.None || IsStopping)
                    return;

                if (batch.Kind == WorkKind.Vector)
                    ProcessVectorBatch(batch);
                else
                    ProcessTextBatch(batch);

                if (IsStopping)
                    return;

                lock (_sync)
                {
                    if (IsStopping)
                        return;

                    RemoveProcessedRows(batch.Keys);
                }
            }
        }

        private void ReadNextBatch(WorkBatch batch)
        {
            int currentIteration = 0;
            int vectorsCount = 0;

            foreach (LTrieRow row in _lTrie.IterateForwardStartsWith(OtherIndexersPrefix, true, false))
            {
                if (row.Key == null || row.Key.Length != 10 || row.Key[1] != 0)
                    throw new InvalidDataException("Unknown protocol in TextDeferredIndexer.Indexer().");

                batch.Kind = WorkKind.Vector;
                batch.Keys.Add(row.Key);
                DecodePayload(row.GetFullValue(true), batch.Decoded);
                MergeVectorTask(batch);

                // Preserves the legacy batching rule (number of table entries, not IDs).
                vectorsCount += batch.Decoded.Count;
                currentIteration++;
                if (vectorsCount > MaximalVectorsPerRound)
                    break;
            }

            if (currentIteration != 0)
                return;

            foreach (LTrieRow row in _lTrie.IterateForward(true, false))
            {
                if (currentIteration == MaximalTextTasksPerRound)
                    break;
                if (row.Key == null || row.Key.Length != 8)
                    throw new InvalidDataException("Unknown protocol in TextDeferredIndexer.Indexer().");

                batch.Kind = WorkKind.Text;
                batch.Keys.Add(row.Key);
                DecodePayload(row.GetFullValue(true), batch.Decoded);
                MergeTextTask(batch);
                currentIteration++;
            }
        }

        private static void DecodePayload(
            byte[] payload,
            Dictionary<string, HashSet<uint>> destination)
        {
            destination.Clear();
            if (payload == null || payload.Length == 0)
                throw new InvalidDataException("Deferred indexer payload is empty.");

            DBreeze.Utils.Biser.Decode_DICT_PROTO_STRING_UINTHASHSET(
                payload,
                destination,
                Compression.eCompressionMethod.NoCompression);

            if (destination.Count == 0)
                throw new InvalidDataException("Deferred indexer payload is malformed.");
        }

        private static void MergeTextTask(WorkBatch batch)
        {
            foreach (KeyValuePair<string, HashSet<uint>> table in batch.Decoded)
            {
                if (!batch.TextTables.TryGetValue(table.Key, out TextSearchHandler.ITS textTable))
                {
                    textTable = new TextSearchHandler.ITS();
                    batch.TextTables.Add(table.Key, textTable);
                    batch.TableNames.Add(table.Key);
                }

                if (table.Value == null)
                    continue;

                foreach (uint documentId in table.Value)
                    textTable.ChangedDocIds.Add((int)documentId);
            }
        }

        private static void MergeVectorTask(WorkBatch batch)
        {
            foreach (KeyValuePair<string, HashSet<uint>> table in batch.Decoded)
            {
                if (!batch.VectorTables.TryGetValue(table.Key, out HashSet<int> documentIds))
                {
                    documentIds = new HashSet<int>();
                    batch.VectorTables.Add(table.Key, documentIds);
                    batch.TableNames.Add(table.Key);
                }

                if (table.Value == null)
                    continue;

                foreach (uint documentId in table.Value)
                    documentIds.Add((int)documentId);
            }
        }

        private void ProcessTextBatch(WorkBatch batch)
        {
            using var transaction = _engine.GetTransaction();
            transaction.tsh = new TextSearchHandler(transaction);
            transaction.SynchronizeTables(batch.TableNames);
            transaction.tsh.DoIndexing(transaction, batch.TextTables);
            transaction.Commit();
        }

        private void ProcessVectorBatch(WorkBatch batch)
        {
            using var transaction = _engine.GetTransaction();
            transaction.SynchronizeTables(batch.TableNames);

            foreach (KeyValuePair<string, HashSet<int>> table in batch.VectorTables)
            {
                batch.SortedVectorIds.Clear();
                foreach (int documentId in table.Value)
                    batch.SortedVectorIds.Add(documentId);
                batch.SortedVectorIds.Sort();
                // Compatibility placeholder only: this call currently performs no HNSW work.
                // Public VectorsInsert overloads build HNSW synchronously and never use this queue.
                transaction.VectorsDoIndexing(table.Key, batch.SortedVectorIds);
            }

            transaction.Commit();
        }

        private void RemoveProcessedRows(List<byte[]> keys)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                byte[] key = keys[i];
                _lTrie.Remove(ref key);
            }

            _lTrie.Commit();
        }

        private long ReadInitialSequence()
        {
            long sequence = DateTime.UtcNow.Ticks;

            foreach (LTrieRow row in _lTrie.IterateForward(true, false))
            {
                if (!TryReadSequence(row.Key, out long persistedSequence))
                    continue;
                if (persistedSequence > sequence)
                    sequence = persistedSequence;
            }

            return sequence;
        }

        private byte[] CreateUniqueKey(bool vectorTask)
        {
            while (true)
            {
                if (_sequence == long.MaxValue)
                    throw new InvalidOperationException("Deferred indexer sequence is exhausted.");

                _sequence++;
                byte[] key = CreateKey(_sequence, vectorTask);
                if (!_lTrie.GetKey(key, false, true).Exists)
                    return key;
            }
        }

        private static byte[] CreateKey(long sequence, bool vectorTask)
        {
            byte[] key = new byte[vectorTask ? 10 : 8];
            Span<byte> sequenceBytes = key.AsSpan(vectorTask ? 2 : 0, 8);

            // DBreeze Int64 keys are stored as an unsigned BE value biased by Int64.MinValue.
            ulong sortableSequence = unchecked((ulong)(sequence - long.MinValue));
            BinaryPrimitives.WriteUInt64BigEndian(sequenceBytes, sortableSequence);
            return key;
        }

        private static bool TryReadSequence(byte[] key, out long sequence)
        {
            ReadOnlySpan<byte> sequenceBytes;
            if (key != null && key.Length == 8)
            {
                sequenceBytes = key;
            }
            else if (key != null && key.Length == 10 && key[0] == 0 && key[1] == 0)
            {
                sequenceBytes = key.AsSpan(2, 8);
            }
            else
            {
                sequence = 0;
                return false;
            }

            ulong sortableSequence = BinaryPrimitives.ReadUInt64BigEndian(sequenceBytes);
            sequence = unchecked((long)(sortableSequence - 0x8000000000000000UL));
            return true;
        }

        private bool HasPendingRows()
        {
            return _lTrie != null && _lTrie.Count(true) != 0;
        }

        private void ThrowIfNotAcceptingWork()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(TextDeferredIndexer));
            if (Volatile.Read(ref _stopRequested) != 0)
                throw new InvalidOperationException("Deferred indexer is stopping.");
        }

        private bool IsStopping =>
            Volatile.Read(ref _stopRequested) != 0 ||
            Volatile.Read(ref _disposed) != 0 ||
            _engine.Disposed;

        private enum WorkKind
        {
            None,
            Text,
            Vector
        }

        private sealed class WorkBatch
        {
            internal WorkKind Kind;
            internal readonly List<byte[]> Keys = new List<byte[]>(MaximalTextTasksPerRound);
            internal readonly List<string> TableNames = new List<string>();
            internal readonly Dictionary<string, HashSet<uint>> Decoded =
                new Dictionary<string, HashSet<uint>>(StringComparer.Ordinal);
            internal readonly Dictionary<string, TextSearchHandler.ITS> TextTables =
                new Dictionary<string, TextSearchHandler.ITS>(StringComparer.Ordinal);
            internal readonly Dictionary<string, HashSet<int>> VectorTables =
                new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            internal readonly List<int> SortedVectorIds = new List<int>();

            internal void Clear()
            {
                Kind = WorkKind.None;
                Keys.Clear();
                TableNames.Clear();
                Decoded.Clear();
                TextTables.Clear();
                VectorTables.Clear();
                SortedVectorIds.Clear();
            }
        }
    }
}
