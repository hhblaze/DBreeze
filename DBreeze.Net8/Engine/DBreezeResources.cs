/*
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

using DBreeze.DataTypes;
using DBreeze.Exceptions;
using DBreeze.LianaTrie;
using DBreeze.Storage;
using DBreeze.Tries;
using DBreeze.Utils;

namespace DBreeze
{
    /// <summary>
    /// DBreeze resources represents an in-memory dictionary synchronized with an internal DBreeze table.
    /// </summary>
    public class DBreezeResources : IDisposable
    {
        private readonly DBreezeEngine DBreezeEngine;
        private readonly TrieSettings LTrieSettings;
        private readonly IStorage Storage;
        private readonly LTrie LTrie;
        private const string TableFileName = "_DBreezeResources";
        private const string UserResourcePrefix = "u";

        private int disposed;
        private long _mutationVersion;

        // Cache keys are raw user resource names. A warmed point-read therefore does not allocate
        // the otherwise unavoidable "u" + resourceName string.
        private readonly ConcurrentDictionary<string, CacheEntry> _cache =
            new ConcurrentDictionary<string, CacheEntry>(StringComparer.Ordinal);

        // Separate sentinels distinguish persisted null from a known missing disk key.
        // Do not use Array.Empty<byte>() here: callers can persist that exact singleton.
        private static readonly byte[] NullSentinel = new byte[0];
        private static readonly byte[] MissingSentinel = new byte[0];

        private readonly object _activeSnapshotsChanged = new object();
        private int _activeSnapshots;

        // Cache hits remain lock-free. Only committed storage access participates in this lock.
        private readonly ReaderWriterLockSlim _storageLock =
            new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

        // LTrieRootNode keeps a mutable generation map and cannot be shared by simultaneous readers.
        // Pool entries are therefore rented exclusively and versioned by the last committed/rolled-back
        // LTrie state. The bound prevents an old concurrency spike from retaining roots indefinitely.
        private readonly ConcurrentBag<CommittedReadRoot> _committedReadRoots = new();
        private readonly int _maxCommittedReadRoots =
            Math.Min(64, Math.Max(4, Environment.ProcessorCount * 2));
        private int _committedReadRootCount;

        private readonly Settings _defaultSetting = new Settings();

        internal DBreezeResources(DBreezeEngine engine)
        {
            DBreezeEngine = engine;
            LTrieSettings = new TrieSettings { InternalTable = true };
            Storage = new StorageLayer(
                Path.Combine(engine.MainFolder, TableFileName),
                LTrieSettings,
                engine.Configuration);
            LTrie = new LTrie(Storage) { TableName = "DBreezeResources" };
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;

            _storageLock.EnterWriteLock();
            try
            {
                lock (_activeSnapshotsChanged)
                {
                    while (_activeSnapshots != 0)
                        Monitor.Wait(_activeSnapshotsChanged);
                }

                ClearCommittedReadRoots();
                LTrie.Dispose();
            }
            finally
            {
                _storageLock.ExitWriteLock();
            }
        }

        /// <summary>Settings regulating resources behaviour.</summary>
        public class Settings
        {
            public Settings()
            {
                HoldInMemory = true;
                HoldOnDisk = true;
                FastUpdates = false;
                InsertWithVerification = true;
                SortingAscending = true;
            }

            public bool HoldInMemory { get; set; }
            public bool HoldOnDisk { get; set; }
            public bool FastUpdates { get; set; }
            public bool InsertWithVerification { get; set; }
            public bool SortingAscending { get; set; }
        }

        /// <summary>Inserts a resource.</summary>
        public void Insert<TValue>(string resourceName, TValue resourceObject, Settings resourceSettings = null)
        {
            if (String.IsNullOrEmpty(resourceName))
                return;

            ThrowIfDisposed();
            resourceSettings ??= _defaultSetting;
            byte[] value = DataTypesConvertor.ConvertValue(resourceObject);

            try
            {
                InsertSingle(resourceName, value, resourceSettings);
            }
            catch (Exception ex) when (ex is not DBreezeException)
            {
                throw DBreezeException.Throw(
                    DBreezeException.eDBreezeExceptions.DBREEZE_RESOURCES_CONCERNING,
                    "in Insert",
                    ex);
            }
        }

        private void InsertSingle(string resourceName, byte[] value, Settings settings)
        {
            if (!settings.HoldOnDisk && !settings.HoldInMemory)
                return;

            CommittedReadRoot readRoot = default;
            bool readRootRented = false;
            _storageLock.EnterWriteLock();
            try
            {
                ThrowIfDisposed();

                if (!settings.HoldOnDisk)
                {
                    if (settings.InsertWithVerification &&
                        _cache.TryGetValue(resourceName, out CacheEntry memoryEntry) &&
                        CacheValueEquals(memoryEntry, value))
                    {
                        return;
                    }

                    BeginMutation();
                    _cache[resourceName] = CacheEntry.Memory(value);
                    return;
                }

                byte[] diskKey = CreateDiskKey(resourceName);
                if (settings.InsertWithVerification &&
                    DiskValueEquals(
                        resourceName,
                        diskKey,
                        value,
                        ref readRoot,
                        ref readRootRented))
                {
                    BeginMutation();
                    if (settings.HoldInMemory)
                        _cache[resourceName] = CacheEntry.Persisted(value);
                    else
                        _cache.TryRemove(resourceName, out _);
                    return;
                }

                BeginMutation();
                _cache.TryRemove(resourceName, out _);

                bool originalOverwrite = LTrie.OverWriteIsAllowed;
                try
                {
                    if (settings.FastUpdates)
                        LTrie.OverWriteIsAllowed = false;

                    LTrie.Add(diskKey, value);
                    CommitStorageMutation();
                }
                finally
                {
                    LTrie.OverWriteIsAllowed = originalOverwrite;
                }

                if (settings.HoldInMemory)
                    _cache[resourceName] = CacheEntry.Persisted(value);
            }
            finally
            {
                if (readRootRented)
                    ReturnCommittedReadRoot(readRoot);
                _storageLock.ExitWriteLock();
            }
        }

        /// <summary>Batch insert of resources converted through DataTypesConvertor.</summary>
        public void Insert<TValue>(IDictionary<string, TValue> resources, Settings resourceSettings = null)
        {
            if (resources == null || resources.Count == 0)
                return;

            ThrowIfDisposed();
            ResourceItem[] items = new ResourceItem[resources.Count];
            int count = 0;
            foreach (KeyValuePair<string, TValue> resource in resources)
            {
                if (String.IsNullOrEmpty(resource.Key))
                    continue;

                items[count++] = new ResourceItem(
                    resource.Key,
                    DataTypesConvertor.ConvertValue(resource.Value));
            }

            InsertBatch(items, count, resourceSettings ?? _defaultSetting);
        }

        /// <summary>Batch insert of byte-array resources.</summary>
        public void Insert(IDictionary<string, byte[]> resources, Settings resourceSettings = null)
        {
            if (resources == null || resources.Count == 0)
                return;

            ThrowIfDisposed();
            ResourceItem[] items = new ResourceItem[resources.Count];
            int count = 0;
            foreach (KeyValuePair<string, byte[]> resource in resources)
            {
                if (!String.IsNullOrEmpty(resource.Key))
                    items[count++] = new ResourceItem(resource.Key, resource.Value);
            }

            InsertBatch(items, count, resourceSettings ?? _defaultSetting);
        }

        private void InsertBatch(ResourceItem[] items, int count, Settings settings)
        {
            if (count == 0 || (!settings.HoldOnDisk && !settings.HoldInMemory))
                return;

            Array.Sort(items, 0, count, ResourceItemComparer.Instance);
            CommittedReadRoot readRoot = default;
            bool readRootRented = false;

            try
            {
                _storageLock.EnterWriteLock();
                try
                {
                    ThrowIfDisposed();
                    BeginMutation();

                    if (!settings.HoldOnDisk)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            ref ResourceItem item = ref items[i];
                            if (!settings.InsertWithVerification ||
                                !_cache.TryGetValue(item.Name, out CacheEntry existing) ||
                                !CacheValueEquals(existing, item.Value))
                            {
                                _cache[item.Name] = CacheEntry.Memory(item.Value);
                            }
                        }
                        return;
                    }

                    bool originalOverwrite = LTrie.OverWriteIsAllowed;
                    bool hasDiskChanges = false;
                    try
                    {
                        if (settings.FastUpdates)
                            LTrie.OverWriteIsAllowed = false;

                        for (int i = 0; i < count; i++)
                        {
                            ref ResourceItem item = ref items[i];
                            item.DiskKey = CreateDiskKey(item.Name);

                            if (settings.InsertWithVerification &&
                                DiskValueEquals(
                                    item.Name,
                                    item.DiskKey,
                                    item.Value,
                                    ref readRoot,
                                    ref readRootRented))
                            {
                                if (settings.HoldInMemory)
                                    _cache[item.Name] = CacheEntry.Persisted(item.Value);
                                else
                                    _cache.TryRemove(item.Name, out _);
                                continue;
                            }

                            _cache.TryRemove(item.Name, out _);
                            LTrie.Add(item.DiskKey, item.Value);
                            item.WasWritten = true;
                            hasDiskChanges = true;
                        }

                        if (hasDiskChanges)
                            CommitStorageMutation();
                    }
                    finally
                    {
                        LTrie.OverWriteIsAllowed = originalOverwrite;
                    }

                    if (settings.HoldInMemory)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            if (items[i].WasWritten)
                                _cache[items[i].Name] = CacheEntry.Persisted(items[i].Value);
                        }
                    }
                }
                finally
                {
                    if (readRootRented)
                        ReturnCommittedReadRoot(readRoot);
                    _storageLock.ExitWriteLock();
                }
            }
            catch (Exception ex) when (ex is not DBreezeException)
            {
                throw DBreezeException.Throw(
                    DBreezeException.eDBreezeExceptions.DBREEZE_RESOURCES_CONCERNING,
                    "in Insert batch",
                    ex);
            }
        }

        /// <summary>Removes a batch of resources.</summary>
        public void Remove(IList<string> resourcesNames)
        {
            if (resourcesNames == null || resourcesNames.Count == 0)
                return;

            ThrowIfDisposed();
            try
            {
                _storageLock.EnterWriteLock();
                try
                {
                    ThrowIfDisposed();
                    BeginMutation();
                    bool hasKeys = false;

                    foreach (string resourceName in resourcesNames)
                    {
                        if (String.IsNullOrEmpty(resourceName))
                            continue;

                        _cache.TryRemove(resourceName, out _);
                        byte[] diskKey = CreateDiskKey(resourceName);
                        LTrie.Remove(ref diskKey);
                        hasKeys = true;
                    }

                    if (hasKeys)
                        CommitStorageMutation();
                }
                finally
                {
                    _storageLock.ExitWriteLock();
                }
            }
            catch (Exception ex) when (ex is not DBreezeException)
            {
                throw DBreezeException.Throw(
                    DBreezeException.eDBreezeExceptions.DBREEZE_RESOURCES_CONCERNING,
                    "in Remove batch",
                    ex);
            }
        }

        /// <summary>Removes one resource.</summary>
        public void Remove(string resourceName)
        {
            if (String.IsNullOrEmpty(resourceName))
                return;

            ThrowIfDisposed();
            try
            {
                _storageLock.EnterWriteLock();
                try
                {
                    ThrowIfDisposed();
                    BeginMutation();
                    _cache.TryRemove(resourceName, out _);
                    byte[] diskKey = CreateDiskKey(resourceName);
                    LTrie.Remove(ref diskKey);
                    CommitStorageMutation();
                }
                finally
                {
                    _storageLock.ExitWriteLock();
                }
            }
            catch (Exception ex) when (ex is not DBreezeException)
            {
                throw DBreezeException.Throw(
                    DBreezeException.eDBreezeExceptions.DBREEZE_RESOURCES_CONCERNING,
                    "in Remove",
                    ex);
            }
        }

        /// <summary>Returns resources with the supplied prefix.</summary>
        public IEnumerable<KeyValuePair<string, TValue>> SelectStartsWith<TValue>(
            string resourceNameStartsWith,
            Settings resourceSettings = null)
        {
            if (String.IsNullOrEmpty(resourceNameStartsWith))
                yield break;

            ThrowIfDisposed();
            resourceSettings ??= _defaultSetting;

            IEnumerable<LTrieRow> snapshot;
            long snapshotVersion;
            byte[] diskPrefix = CreateDiskKey(resourceNameStartsWith);
            bool snapshotIsActive = false;

            _storageLock.EnterReadLock();
            try
            {
                ThrowIfDisposed();
                BeginSnapshot();
                snapshotIsActive = true;
                snapshotVersion = Volatile.Read(ref _mutationVersion);
                snapshot = resourceSettings.SortingAscending
                    ? LTrie.IterateForwardStartsWith(diskPrefix, true, false)
                    : LTrie.IterateBackwardStartsWith(diskPrefix, true, false);
            }
            catch
            {
                if (snapshotIsActive)
                {
                    EndSnapshot();
                    snapshotIsActive = false;
                }
                throw;
            }
            finally
            {
                _storageLock.ExitReadLock();
            }

            try
            {
                foreach (LTrieRow row in snapshot)
                {
                    byte[] diskValue = row.GetFullValue(false);
                    string resourceName = Encoding.UTF8.GetString(row.Key, 1, row.Key.Length - 1);
                    byte[] selectedValue = diskValue;

                    long beforeCacheRead = Volatile.Read(ref _mutationVersion);
                    if (beforeCacheRead == snapshotVersion &&
                        _cache.TryGetValue(resourceName, out CacheEntry cached) &&
                        Volatile.Read(ref _mutationVersion) == snapshotVersion)
                    {
                        selectedValue = cached.IsMissing ? null : cached.GetValue();
                    }
                    else if (resourceSettings.HoldInMemory &&
                             Volatile.Read(ref _mutationVersion) == snapshotVersion)
                    {
                        _cache.TryAdd(resourceName, CacheEntry.Persisted(diskValue));
                    }

                    yield return new KeyValuePair<string, TValue>(
                        resourceName,
                        selectedValue == null
                            ? default
                            : DataTypesConvertor.ConvertBack<TValue>(selectedValue));
                }
            }
            finally
            {
                if (snapshotIsActive)
                    EndSnapshot();
            }
        }

        /// <summary>Gets a batch of resources.</summary>
        public IDictionary<string, TValue> Select<TValue>(
            IList<string> resourcesNames,
            Settings resourceSettings = null)
        {
            int capacity = resourcesNames?.Count ?? 0;
            Dictionary<string, TValue> result = new Dictionary<string, TValue>(capacity, StringComparer.Ordinal);
            if (capacity == 0)
                return result;

            ThrowIfDisposed();
            resourceSettings ??= _defaultSetting;
            ResourceItem[] misses = new ResourceItem[capacity];
            int missCount = 0;

            foreach (string resourceName in resourcesNames)
            {
                if (String.IsNullOrEmpty(resourceName))
                    continue;

                if (_cache.TryGetValue(resourceName, out CacheEntry cached))
                    result[resourceName] = ConvertCacheValue<TValue>(cached);
                else
                    misses[missCount++] = new ResourceItem(resourceName, null);
            }

            if (missCount == 0)
                return result;

            Array.Sort(misses, 0, missCount, ResourceItemComparer.Instance);
            CommittedReadRoot readRoot = default;
            bool readRootRented = false;
            try
            {
                _storageLock.EnterReadLock();
                try
                {
                    ThrowIfDisposed();
                    for (int i = 0; i < missCount; i++)
                    {
                        string resourceName = misses[i].Name;
                        if (_cache.TryGetValue(resourceName, out CacheEntry cached))
                        {
                            result[resourceName] = ConvertCacheValue<TValue>(cached);
                            continue;
                        }

                        if (!readRootRented)
                        {
                            readRoot = RentCommittedReadRoot();
                            readRootRented = true;
                        }

                        CacheEntry loaded = LoadCommitted(resourceName, readRoot.Root);
                        if (resourceSettings.HoldInMemory)
                            _cache.TryAdd(resourceName, loaded);
                        result[resourceName] = ConvertCacheValue<TValue>(loaded);
                    }
                }
                finally
                {
                    if (readRootRented)
                        ReturnCommittedReadRoot(readRoot);
                    _storageLock.ExitReadLock();
                }
            }
            catch (Exception ex) when (ex is not DBreezeException)
            {
                throw DBreezeException.Throw(
                    DBreezeException.eDBreezeExceptions.DBREEZE_RESOURCES_CONCERNING,
                    "in Select batch",
                    ex);
            }

            return result;
        }

        /// <summary>Gets one resource from memory or committed storage.</summary>
        public TValue Select<TValue>(string resourceName, Settings resourceSettings = null)
        {
            if (String.IsNullOrEmpty(resourceName))
                return default;

            ThrowIfDisposed();
            resourceSettings ??= _defaultSetting;
            if (_cache.TryGetValue(resourceName, out CacheEntry cached))
                return ConvertCacheValue<TValue>(cached);

            try
            {
                _storageLock.EnterReadLock();
                try
                {
                    ThrowIfDisposed();
                    if (_cache.TryGetValue(resourceName, out cached))
                        return ConvertCacheValue<TValue>(cached);

                    CommittedReadRoot readRoot = RentCommittedReadRoot();
                    try
                    {
                        CacheEntry loaded = LoadCommitted(resourceName, readRoot.Root);
                        if (resourceSettings.HoldInMemory)
                            _cache.TryAdd(resourceName, loaded);
                        return ConvertCacheValue<TValue>(loaded);
                    }
                    finally
                    {
                        ReturnCommittedReadRoot(readRoot);
                    }
                }
                finally
                {
                    _storageLock.ExitReadLock();
                }
            }
            catch (Exception ex) when (ex is not DBreezeException)
            {
                throw DBreezeException.Throw(
                    DBreezeException.eDBreezeExceptions.DBREEZE_RESOURCES_CONCERNING,
                    "in Select",
                    ex);
            }
        }

        private CacheEntry LoadCommitted(string resourceName, ITrieRootNode readRoot)
        {
            byte[] diskKey = CreateDiskKey(resourceName);
            LTrieRow row = LTrie.GetKey(ref diskKey, readRoot, false);
            return row.Exists
                ? CacheEntry.Persisted(row.GetFullValue(false))
                : CacheEntry.Missing;
        }

        private bool DiskValueEquals(
            string resourceName,
            byte[] diskKey,
            byte[] value,
            ref CommittedReadRoot readRoot,
            ref bool readRootRented)
        {
            if (_cache.TryGetValue(resourceName, out CacheEntry cached) && cached.IsPersisted)
                return !cached.IsMissing && CacheValueEquals(cached, value);

            if (!readRootRented)
            {
                readRoot = RentCommittedReadRoot();
                readRootRented = true;
            }

            LTrieRow row = LTrie.GetKey(ref diskKey, readRoot.Root, false);
            if (!row.Exists)
                return false;

            return ByteValuesEqual(row.GetFullValue(false), value);
        }

        private static bool CacheValueEquals(CacheEntry entry, byte[] value)
        {
            return !entry.IsMissing && ByteValuesEqual(entry.GetValue(), value);
        }

        private static bool ByteValuesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null)
                return left == right;
            return left._ByteArrayEquals(right);
        }

        private static TValue ConvertCacheValue<TValue>(CacheEntry entry)
        {
            byte[] value = entry.IsMissing ? null : entry.GetValue();
            return value == null ? default : DataTypesConvertor.ConvertBack<TValue>(value);
        }

        private static byte[] CreateDiskKey(string resourceName)
        {
            return DataTypesConvertor.ConvertKey<string>(UserResourcePrefix + resourceName);
        }

        private CommittedReadRoot RentCommittedReadRoot()
        {
            long currentVersion = LTrie.DtTableFixed;
            while (_committedReadRoots.TryTake(out CommittedReadRoot readRoot))
            {
                Interlocked.Decrement(ref _committedReadRootCount);
                if (readRoot.Version == currentVersion)
                    return readRoot;
            }

            ITrieRootNode root = LTrie.GetTrieReadNode(out long version);
            if (root == null)
                throw new TableNotOperableException(LTrie.TableName);

            return new CommittedReadRoot(root, version);
        }

        private void ReturnCommittedReadRoot(CommittedReadRoot readRoot)
        {
            if (readRoot.Root == null ||
                Volatile.Read(ref disposed) != 0 ||
                readRoot.Version != LTrie.DtTableFixed)
            {
                return;
            }

            int count = Interlocked.Increment(ref _committedReadRootCount);
            if (count <= _maxCommittedReadRoots)
            {
                _committedReadRoots.Add(readRoot);
                return;
            }

            Interlocked.Decrement(ref _committedReadRootCount);
        }

        private void ClearCommittedReadRoots()
        {
            while (_committedReadRoots.TryTake(out _))
                Interlocked.Decrement(ref _committedReadRootCount);

            Interlocked.Exchange(ref _committedReadRootCount, 0);
        }

        private void CommitStorageMutation()
        {
            try
            {
                LTrie.Commit();
            }
            finally
            {
                // Commit and its rollback-on-failure both advance DtTableFixed. Clearing here also
                // releases warmed generation maps promptly instead of retaining stale pool entries.
                ClearCommittedReadRoots();
            }
        }

        private void BeginMutation()
        {
            Interlocked.Increment(ref _mutationVersion);
        }

        private void BeginSnapshot()
        {
            lock (_activeSnapshotsChanged)
                checked { _activeSnapshots++; }
        }

        private void EndSnapshot()
        {
            lock (_activeSnapshotsChanged)
            {
                if (--_activeSnapshots == 0)
                    Monitor.PulseAll(_activeSnapshotsChanged);
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
                throw new ObjectDisposedException(nameof(DBreezeResources));
        }

        private readonly struct CacheEntry
        {
            private readonly byte[] _value;
            internal readonly bool IsPersisted;

            internal bool IsMissing => ReferenceEquals(_value, MissingSentinel);

            private CacheEntry(byte[] value, bool isPersisted)
            {
                _value = value ?? NullSentinel;
                IsPersisted = isPersisted;
            }

            internal byte[] GetValue() => ReferenceEquals(_value, NullSentinel) ? null : _value;

            internal static CacheEntry Persisted(byte[] value) => new CacheEntry(value, true);
            internal static CacheEntry Memory(byte[] value) => new CacheEntry(value, false);
            internal static CacheEntry Missing => new CacheEntry(MissingSentinel, true);
        }

        private readonly struct CommittedReadRoot
        {
            internal readonly ITrieRootNode Root;
            internal readonly long Version;

            internal CommittedReadRoot(ITrieRootNode root, long version)
            {
                Root = root;
                Version = version;
            }
        }

        private struct ResourceItem
        {
            internal readonly string Name;
            internal readonly byte[] Value;
            internal byte[] DiskKey;
            internal bool WasWritten;

            internal ResourceItem(string name, byte[] value)
            {
                Name = name;
                Value = value;
                DiskKey = null;
                WasWritten = false;
            }
        }

        private sealed class ResourceItemComparer : IComparer<ResourceItem>
        {
            internal static readonly ResourceItemComparer Instance = new ResourceItemComparer();

            public int Compare(ResourceItem x, ResourceItem y)
            {
                return StringComparer.Ordinal.Compare(x.Name, y.Name);
            }
        }
    }
}
