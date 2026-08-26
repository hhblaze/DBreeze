# DBreeze for LLM Agents (v1.139.2026.0817)

Standalone, high-density usage reference. The API snapshot contains **79 public `Transaction` methods** (71 core/text methods plus 8 vector methods) and **6 public `Scheme` methods**.

Quick map: [engine initialization](#2-engine-initialization-and-lifetime) · [transaction rules](#3-transaction-lifetime-threading-and-locking) · [exact API index](#4-exact-public-transaction-api-79-methods) · [Scheme](#6-scheme-api-6-methods) · [text search](#9-text-search) · [vectors](#10-hnsw-vectors-net472--net6func)

Common namespaces:

```csharp
using DBreeze;
using DBreeze.DataTypes;
using DBreeze.Utils;
using DBreeze.Transactions;
```

## 1. Runtime and Target Notes

DBreeze is an embedded key/value database. Tables are created automatically on the first write; there is no create-table migration step. Keys are ordered lexicographically as bytes, transactions provide ACID writes, and one engine coordinates concurrent transactions.

| Target family | Core, objects, nested tables, text | HNSW vector API |
|:--------------|:-----------------------------------|:----------------|
| .NET Framework 3.5 / 4.0 and Portable | Yes | No |
| .NET Framework 4.7.2 | Yes | Yes |
| Projects compiled with `NET6FUNC` (including current Net8/Net6/NetStandard 2.1/NetCoreApp builds) | Yes | Yes |

Keep general examples compatible with older C# syntax. Tuple-based vector examples require a target/compiler that exposes the vector API.

## 2. Engine Initialization and Lifetime

### 2.1 Minimal disk engine

Create one long-lived `DBreezeEngine` for one physical database folder. Reuse it across threads; create a separate transaction for each unit of work. Do not create multiple engines over the same folder: DBreeze opens its storage files exclusively.

```csharp
private static readonly DBreezeEngine Engine =
    new DBreezeEngine(@"D:\Data\MyApplication\DBreeze");
```

The string constructor requires a non-empty folder, creates it when needed, uses disk storage, and initializes the scheme, transaction journal, recovery, resources, and deferred text indexer. Construction is synchronous. If initialization or journal recovery fails, construction throws; do not continue with a partially initialized database.

`DBreezeEngine` is normally an application singleton (or a singleton registration in a DI container), not a per-request object. Dispose it once during orderly application shutdown:

```csharp
public static void ShutdownDatabase()
{
    Engine.Dispose();
}
```

`Dispose()` is idempotent, stops background work, closes tables/journal/resources, and disposes the configuration (including `Backup`). After passing a configuration to an engine, treat that configuration as engine-owned: do not reuse or dispose it independently.

### 2.2 Full configuration

Configure process-wide serializers and encryption before the first related read/write, then construct the engine.

```csharp
// Process-wide serializer contract. Keep it stable for the lifetime of all engines
// and across application versions that must read the same stored objects.
CustomSerializator.ByteArraySerializator = delegate(object value)
{
    return MySerializer.Serialize(value);
};
CustomSerializator.ByteArrayDeSerializator = delegate(byte[] data, Type type)
{
    return MySerializer.Deserialize(data, type);
};

var textEncryptor = new DBreeze.TextSearch.WabiStreamCrypto(aesKeyBytes, aesIvBytes);

var configuration = new DBreezeConfiguration
{
    DBreezeDataFolderName = @"D:\Data\MyApplication\DBreeze",
    Storage = DBreezeConfiguration.eStorage.DISK,
    NotifyAhead_WhenWriteTablePossibleDeadlock = true,
    Backup = new DBreeze.Storage.Backup
    {
        BackupFolderName = @"E:\Backups\MyApplication",
        IncrementalBackupFileIntervalMin = 30
    },
    TextSearchConfig = new DBreezeConfiguration.TextSearchConfiguration
    {
        QuantityOfWordsInBlock = 1000,
        MinimalBlockReservInBytes = 100000,
        MaximalWordSize = 50,
        TextEncryptor = textEncryptor,
        UseTextEncryptor = true
    },
    VectorLayerConfig = new DBreezeConfiguration.VectorlayerConfiguration
    {
        Dense = 1000
    }
};

// First matching pattern wins. Avoid overlapping patterns because Dictionary
// enumeration order is not a portable precedence contract on old frameworks.
configuration.AlternativeTablesLocations.Add("cache_*", String.Empty); // memory
configuration.AlternativeTablesLocations.Add("archive_*", @"F:\DBreezeArchive");

var engine = new DBreezeEngine(configuration);
```

Configuration reference:

| Setting | Default / contract |
|:--------|:-------------------|
| `DBreezeDataFolderName` | Required for `DISK`; main scheme, journal, and normal tables reside here. |
| `Storage` | `DISK`; alternatives are `MEMORY` and `RemoteInstance`. |
| `AlternativeTablesLocations` | Pattern to folder; empty folder means memory. First enumerated match wins. |
| `Backup` | Non-null but inactive until `BackupFolderName` is successfully configured. |
| `NotifyAhead_WhenWriteTablePossibleDeadlock` | `true`; detects a likely unreserved second write table early. Keep enabled. |
| `TextSearchConfig` | Text index block sizing and optional encryptor. |
| `VectorLayerConfig.Dense` | Legacy/global vector density setting, clamped internally to 50..5000. Current HNSW calls are primarily configured per table. |
| `RICommunicator` | Required transport adapter for `RemoteInstance`. |

### 2.3 Disk, memory, and mixed placement

Fully in-memory engine (all content disappears on disposal/process exit):

```csharp
var memoryEngine = new DBreezeEngine(new DBreezeConfiguration
{
    Storage = DBreezeConfiguration.eStorage.MEMORY
});
```

Mixed placement keeps the scheme in the main database and routes matching user tables:

```csharp
var configuration = new DBreezeConfiguration
{
    DBreezeDataFolderName = @"D:\Data\Main",
    Storage = DBreezeConfiguration.eStorage.DISK
};
configuration.AlternativeTablesLocations.Add("mem_*", String.Empty);
configuration.AlternativeTablesLocations.Add("cold_*", @"F:\Data\Cold");

var engine = new DBreezeEngine(configuration);
// engine.Scheme.GetTablePathFromTableName("mem_sessions") returns "MEMORY"
```

Pattern syntax used by `AlternativeTablesLocations` and `SynchronizeTables`:

| Pattern | Meaning |
|:--------|:--------|
| `Items*` | `Items` followed by one or more arbitrary characters; the suffix after `*` is ignored. |
| `Items$` | `Items` followed by one or more non-`/` characters. |
| `Items#/Pictures` | `Items`, one or more non-`/` characters, `/Pictures`. |

Do not depend on overlapping patterns. Use exact, disjoint prefixes wherever possible.

### 2.4 Table names

Literal table names must be non-empty and may not contain DBreeze-reserved characters `*`, `#`, `$`, `@`, `\`, `^`, `~`, or `´`. `/` is allowed and is commonly used for hierarchical table names. Pattern characters belong only in synchronization/configuration patterns.

Current versions safely escape XML text characters in transaction-journal payloads. For compatibility with old NetStandard/Net8 and old Framework journal readers, keep names XML-safe as well: avoid `&`, `<`, `>`, CR, LF, and TAB. This is a legacy cross-binary compatibility recommendation, not a current-to-current limitation.

### 2.5 Incremental backup and restore

Enabling `BackupFolderName` records incremental physical changes and adds write cost. Start from an empty database or preserve an initial full snapshot, then retain ordered `dbreeze_ibp_*` files.

Restore offline: the destination must not be open by an engine.

```csharp
engine.Dispose();

var restorer = new DBreeze.Storage.BackupRestorer
{
    DataBaseFolder = @"D:\Restore\Database",
    BackupFolder = @"E:\Backups\MyApplication"
};
restorer.OnRestore += delegate(DBreeze.Storage.BackupRestorer.BackupRestorationProcess state)
{
    Console.WriteLine("Restore: " + state.ReadinessInProcent + "%");
};
restorer.StartRestoration();
```

The restorer writes all recovered physical file numbers into one selected destination. With alternative table locations, restore into a staging/main folder, recover the scheme, and while every engine is stopped move each data/`.rol`/`.rhp` triplet to its configured alternative folder. The restorer operates on physical file numbers, not logical table names.

### 2.6 Text encryption initialization

`UseTextEncryptor = true` affects new or empty text-search tables. It does not transparently rewrite existing plain-text indexes. Keep the same key/IV available for every future read. To migrate, configure the encryptor first, migrate into a new table inside a transaction, then swap tables only after the transaction has ended successfully:

```csharp
using (var tran = engine.GetTransaction())
{
    tran.SynchronizeTables("ArticlesText", "ArticlesText_encrypted");
    tran.Support_Migration_EncryptTextSearchTable(
        "ArticlesText", "ArticlesText_encrypted");
    tran.Commit();
}

engine.Scheme.DeleteTable("ArticlesText");
engine.Scheme.RenameTable("ArticlesText_encrypted", "ArticlesText");
```

Do not delete the source until the migrated table has been committed and verified.

### 2.7 Remote instance (advanced)

`DBreezeEngine` deliberately rejects `Storage = RemoteInstance`. Use `DBreezeRemoteEngine`, which initializes lazily on its first `GetTransaction()` or `Scheme` access. The application supplies transport; `IRemoteInstanceCommunicator.Send` must deliver one protocol message to a trusted server-side `RemoteTablesHandler.ParseProtocol` and return its response.

```csharp
var configuration = new DBreezeConfiguration
{
    Storage = DBreezeConfiguration.eStorage.RemoteInstance,
    DBreezeDataFolderName = "tenant-a/database-1",
    RICommunicator = myRemoteCommunicator
};

var remoteEngine = new DBreezeRemoteEngine(configuration);
using (var tran = remoteEngine.GetTransaction())
{
    // Normal Transaction API.
}
```

Secure, authenticate, frame, retry, and serialize transport calls outside DBreeze. `RestoreTableFromTheOtherFile` is not supported by the current remote storage implementation.

### 2.8 Operability and diagnostics

| Engine member | Use |
|:--------------|:----|
| `DBisOperable` | `false` after a fatal engine/storage error or disposal. |
| `DBisOperableReason` | Short reason associated with the non-operable state. Log it together with the full exception. |
| `Disposed` | Thread-safe disposal state. |
| `BackgroundTasksExternalNotifier` | Lightweight callback for background events; invoked through the thread pool. Do not block or throw. |
| `Diagnostic_GetActiveTransactionsState()` | Snapshot of active transaction/write-reservation state for diagnostics. |
| `Resources` | Engine-owned synchronized memory/disk resource dictionary. |

Never suppress an initialization/recovery exception and open another engine on the same files. Fix or restore the files first.

## 3. Transaction Lifetime, Threading, and Locking

### 3.1 Basic transaction

```csharp
using (var tran = engine.GetTransaction())
{
    tran.Insert<int, string>("users", 1, "Alice");
    tran.Commit();
}
```

- Dispose every transaction, normally with `using`.
- Without `Commit()`, uncommitted changes are rolled back on disposal.
- `Rollback()` explicitly reverts changes since the last successful `Commit()` and keeps the transaction usable.
- A transaction may execute several modify/commit cycles.
- Do not open a nested transaction on the same managed thread.

### 3.2 Thread affinity and async

A transaction records `ManagedThreadId`. State-changing operations (`Insert`, remove, commit, rollback, vector/text writes, etc.) must execute on that owner thread. Prefer one transaction per worker thread. Read APIs have limited support for parallel reads from one transaction, but separate read transactions are simpler and safer.

Do all asynchronous I/O before opening the transaction. Do not `await`, `Task.Run(...).Wait()`, or block on network I/O while holding DB locks.

```csharp
// Correct: asynchronous work has completed before the transaction exists.
HttpResponseMessage response = await client.GetAsync(url).ConfigureAwait(false);
byte[] payload = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

using (var tran = engine.GetTransaction())
{
    tran.Insert<long, byte[]>("http-cache", requestId, payload);
    tran.Commit();
}
```

Lazy sequences must be enumerated while both the transaction and enumerator are alive. Do not return `SelectForward(...)`, `VectorsGetAll(...)`, or another DBreeze enumerable from a disposed transaction. `foreach` disposes the enumerator on `break` or exception.

### 3.3 `GetTransaction` table locks versus `SynchronizeTables`

```csharp
using (var tran = engine.GetTransaction(
    eTransactionTablesLockTypes.EXCLUSIVE, "accounts", "ledger"))
{
    // The listed table session was acquired before the transaction was returned.
}
```

- `GetTransaction()` creates the normal transaction used by most code.
- `GetTransaction(EXCLUSIVE, tables)` waits until conflicting shared/exclusive sessions release those tables.
- `GetTransaction(SHARED, tables)` allows other shared sessions but waits against an exclusive session.
- `SynchronizeTables(...)` atomically reserves the complete future write set inside a transaction and prevents opposite table-acquisition order from deadlocking.

Call `SynchronizeTables` exactly once, before the first modification, when a transaction may modify more than one intersecting table/pattern:

```csharp
using (var tran = engine.GetTransaction())
{
    tran.SynchronizeTables("accounts", "ledger", "audit_*");
    tran.Insert<long, decimal>("accounts", accountId, balance);
    tran.Insert<long, decimal>("ledger", entryId, delta);
    tran.Commit();
}
```

One write table cannot form a cross-table deadlock, so explicit synchronization is normally unnecessary. Keep `NotifyAhead_WhenWriteTablePossibleDeadlock = true`: an unreserved second write table may throw before a deadlock becomes possible.

### 3.4 Read visibility and lazy values

`ValuesLazyLoadingIsOn` defaults to `true`: iterator rows carry the key plus a pointer and load `row.Value` on demand. Set it to `false` when nearly every value will be consumed or values must be materialized immediately during enumeration.

`AsReadVisibilityScope: true` requests a read-visibility cursor isolated from subsequent writes made through the current transaction. This is useful when modifying the same table during traversal:

```csharp
using (var tran = engine.GetTransaction())
{
    tran.SynchronizeTables("events");
    foreach (var row in tran.SelectForward<int, string>(
        "events", AsReadVisibilityScope: true))
    {
        if (row.Value == "expired")
            tran.RemoveKey<int>("events", row.Key);
    }
    tran.Commit();
}
```

Advanced modifiers:

- `ReadVisibilityScopeModifier_GenerateNewTableForRead = true` forces a fresh read table for the next visibility-scope request instead of reusing the cached read table.
- `ReadVisibilityScopeModifier_DirtyRead = true` only applies with the previous flag and may include uncommitted changes visible at the moment of the request.

Keep both defaults unless implementing a deliberate visibility algorithm.

## 4. Exact Public `Transaction` API (79 Methods)

The following declarations are the canonical current signatures. `Dispose` is included in the count; constructors are internal. Parameter names are shown because named arguments are common in DBreeze code.

### 4.1 Lifecycle and synchronization (5)

```csharp
public void Dispose();
public void Commit();
public void Rollback();
public void SynchronizeTables(IList<string> tablesNamesPatterns);
public void SynchronizeTables(params string[] tablesNamesPatterns);
```

### 4.2 Key/value writes (14)

```csharp
public void Insert<TKey, TValue>(string tableName, TKey key, TValue value);
public void Insert<TKey, TValue>(string tableName, TKey key, TValue value,
    out byte[] refToInsertedValue);
public void Insert<TKey, TValue>(string tableName, TKey key, TValue value,
    out byte[] refToInsertedValue, out bool WasUpdated);
public void Insert<TKey, TValue>(string tableName, TKey key, TValue value,
    out byte[] refToInsertedValue, out bool WasUpdated, bool dontUpdateIfExists);

public void InsertPart<TKey, TValue>(string tableName, TKey key, TValue value,
    uint startIndex);
public void InsertPart<TKey, TValue>(string tableName, TKey key, TValue value,
    uint startIndex, out byte[] refToInsertedValue);
public void InsertPart<TKey, TValue>(string tableName, TKey key, TValue value,
    uint startIndex, out byte[] refToInsertedValue, out bool WasUpdated);

public void RemoveKey<TKey>(string tableName, TKey key);
public void RemoveKey<TKey>(string tableName, TKey key, out bool WasRemoved);
public void RemoveKey<TKey>(string tableName, TKey key,
    out bool WasRemoved, out byte[] deletedValue);
public void RemoveAllKeys(string tableName, bool withFileRecreation);

public void ChangeKey<TKey>(string tableName, TKey oldKey, TKey newKey);
public void ChangeKey<TKey>(string tableName, TKey oldKey, TKey newKey,
    out byte[] ptrToNewKey);
public void ChangeKey<TKey>(string tableName, TKey oldKey, TKey newKey,
    out byte[] ptrToNewKey, out bool WasChanged);
```

### 4.3 Data blocks (4)

```csharp
public byte[] InsertDataBlock(string tableName, byte[] initialPointer, byte[] data);
public byte[] InsertDataBlockWithFixedAddress<TValue>(
    string tableName, byte[] initialPointer, TValue data);
public byte[] SelectDataBlock(string tableName, byte[] ptrToDataBlock);
public TValue SelectDataBlockWithFixedAddress<TValue>(
    string tableName, byte[] ptrToDataBlock);
```

### 4.4 Batch-write helpers (3)

```csharp
public void InsertRandomKeySorter<TKey, TValue>(
    string tableName, TKey key, TValue value);
public void RemoveRandomKeySorter<TKey>(string tableName, TKey key);
public void Technical_SetTable_OverwriteIsNotAllowed(string tableName);
```

### 4.5 Objects, nested tables, dictionaries, and sets (14)

```csharp
public TIdentity ObjectGetNewIdentity<TIdentity>(
    string tableName, byte[] addressOfIdentity = null, uint seed = 1);
public DBreeze.Objects.DBreezeObjectInsertResult<TObject> ObjectInsert<TObject>(
    string tableName, DBreeze.Objects.DBreezeObject<TObject> toInsert,
    bool speedUpdate = false);
public void ObjectRemove(string tableName, byte[] index, bool speedUpdate = false);
public DBreeze.Objects.DBreezeObject<TVal> ObjectGetByFixedAddress<TVal>(
    string tableName, byte[] address);

public NestedTable InsertTable<TKey>(string tableName, TKey key, uint tableIndex);
public NestedTable SelectTable<TKey>(string tableName, TKey key, uint tableIndex);

public void InsertDictionary<TTableKey, TDictionaryKey, TDictionaryValue>(
    string tableName, TTableKey key,
    Dictionary<TDictionaryKey, TDictionaryValue> value,
    uint tableIndex, bool withValuesRemove);
public void InsertDictionary<TDictionaryKey, TDictionaryValue>(
    string tableName, Dictionary<TDictionaryKey, TDictionaryValue> value,
    bool withValuesRemove);
public Dictionary<TDictionaryKey, TDictionaryValue>
    SelectDictionary<TTableKey, TDictionaryKey, TDictionaryValue>(
        string tableName, TTableKey key, uint tableIndex);
public Dictionary<TDictionaryKey, TDictionaryValue>
    SelectDictionary<TDictionaryKey, TDictionaryValue>(string tableName);

public void InsertHashSet<TTableKey, THashSetKey>(
    string tableName, TTableKey key, HashSet<THashSetKey> value,
    uint tableIndex, bool withValuesRemove);
public void InsertHashSet<THashSetKey>(
    string tableName, HashSet<THashSetKey> value, bool withValuesRemove);
public HashSet<THashSetKey> SelectHashSet<TTableKey, THashSetKey>(
    string tableName, TTableKey key, uint tableIndex);
public HashSet<THashSetKey> SelectHashSet<THashSetKey>(string tableName);
```

### 4.6 Aggregates (3)

```csharp
public ulong Count(string tableName);
public Row<TKey, TValue> Min<TKey, TValue>(string tableName);
public Row<TKey, TValue> Max<TKey, TValue>(string tableName);
```

### 4.7 Point reads (2)

```csharp
public Row<TKey, TValue> Select<TKey, TValue>(
    string tableName, TKey key, bool AsReadVisibilityScope = false);
public Row<TKey, TValue> SelectDirect<TKey, TValue>(
    string tableName, byte[] refToInsertedValue);
```

### 4.8 Traversal and multi-table merge (18)

```csharp
public IEnumerable<Row<TKey, TValue>> SelectForward<TKey, TValue>(
    string tableName, bool AsReadVisibilityScope = false);
public IEnumerable<Row<TKey, TValue>> SelectBackward<TKey, TValue>(
    string tableName, bool AsReadVisibilityScope = false);

public IEnumerable<Row<TKey, TValue>> SelectForwardStartFrom<TKey, TValue>(
    string tableName, TKey key, bool includeStartFromKey,
    bool AsReadVisibilityScope = false);
public IEnumerable<Row<TKey, TValue>> SelectBackwardStartFrom<TKey, TValue>(
    string tableName, TKey key, bool includeStartFromKey,
    bool AsReadVisibilityScope = false);

public IEnumerable<Row<TKey, TValue>> SelectForwardFromTo<TKey, TValue>(
    string tableName, TKey startKey, bool includeStartKey,
    TKey stopKey, bool includeStopKey, bool AsReadVisibilityScope = false);
public IEnumerable<Row<TKey, TValue>> SelectForwardFromTo<TKey, TValue>(
    string tableName, TKey startKey, bool includeStartKey,
    TKey stopKey, bool includeStopKey, int grabSomeLeadingRecords,
    bool AsReadVisibilityScope = false);
public IEnumerable<Row<TKey, TValue>> SelectBackwardFromTo<TKey, TValue>(
    string tableName, TKey startKey, bool includeStartKey,
    TKey stopKey, bool includeStopKey, bool AsReadVisibilityScope = false);
public IEnumerable<Row<TKey, TValue>> SelectBackwardFromTo<TKey, TValue>(
    string tableName, TKey startKey, bool includeStartKey,
    TKey stopKey, bool includeStopKey, int grabSomeLeadingRecords,
    bool AsReadVisibilityScope = false);

public IEnumerable<Row<TKey, TValue>> Multi_SelectForwardFromTo<TKey, TValue>(
    HashSet<string> tables, TKey startKey, bool includeStartKey,
    TKey stopKey, bool includeStopKey, bool AsReadVisibilityScope = false);
public IEnumerable<Row<TKey, TValue>> Multi_SelectBackwardFromTo<TKey, TValue>(
    HashSet<string> tables, TKey startKey, bool includeStartKey,
    TKey stopKey, bool includeStopKey, bool AsReadVisibilityScope = false);

public IEnumerable<Row<TKey, TValue>> SelectForwardStartsWith<TKey, TValue>(
    string tableName, TKey startWithKeyPart,
    bool AsReadVisibilityScope = false);
public IEnumerable<Row<TKey, TValue>> SelectBackwardStartsWith<TKey, TValue>(
    string tableName, TKey startWithKeyPart,
    bool AsReadVisibilityScope = false);
public IEnumerable<Row<TKey, TValue>>
    SelectForwardStartsWithClosestToPrefix<TKey, TValue>(
        string tableName, TKey startWithClosestPrefix,
        bool AsReadVisibilityScope = false);
public IEnumerable<Row<TKey, TValue>>
    SelectBackwardStartsWithClosestToPrefix<TKey, TValue>(
        string tableName, TKey startWithClosestPrefix,
        bool AsReadVisibilityScope = false);

public IEnumerable<Row<TKey, TValue>> SelectForwardSkip<TKey, TValue>(
    string tableName, ulong skippingQuantity,
    bool AsReadVisibilityScope = false);
public IEnumerable<Row<TKey, TValue>> SelectBackwardSkip<TKey, TValue>(
    string tableName, ulong skippingQuantity,
    bool AsReadVisibilityScope = false);
public IEnumerable<Row<TKey, TValue>> SelectForwardSkipFrom<TKey, TValue>(
    string tableName, TKey key, ulong skippingQuantity,
    bool AsReadVisibilityScope = false);
public IEnumerable<Row<TKey, TValue>> SelectBackwardSkipFrom<TKey, TValue>(
    string tableName, TKey key, ulong skippingQuantity,
    bool AsReadVisibilityScope = false);
```

### 4.9 Table replacement (1)

```csharp
public void RestoreTableFromTheOtherFile(
    string tableName, string newTableFullPath,
    bool sourceTableBelongsToEngine = false);
```

### 4.10 Text search (7)

```csharp
public void TextInsert(string tableName, byte[] documentId,
    string containsWords = "", string fullMatchWords = "",
    bool deferredIndexing = false, int containsMinimalLength = 3);
public void TextAppend(string tableName, byte[] documentId,
    string containsWords = "", string fullMatchWords = "",
    bool deferredIndexing = false, int containsMinimalLength = 3,
    bool encryptedTable = false);
public void TextRemove(string tableName, byte[] documentId,
    string fullMatchWords, bool deferredIndexing = false,
    int containsMinimalLength = 3);
public void TextRemoveAll(string tableName, byte[] documentId,
    bool deferredIndexing = false);
public Dictionary<byte[], HashSet<string>> TextGetDocumentsSearchables(
    string tableName, HashSet<byte[]> documentIds);
public DBreeze.TextSearch.TextSearchTable TextSearch(string tableName);
public void Support_Migration_EncryptTextSearchTable(
    string oldTableName, string newTableName);
```

### 4.11 Vectors (8; NET472 / `NET6FUNC` only)

```csharp
public long VectorsCount<TVector>(string tableName,
    VectorTableParameters<TVector> vectorTableParameters = null,
    bool onlyDeletedCount = false);
public IEnumerable<(long, TVector)> VectorsGetByExternalId<TVector>(
    string tableName, List<long> externalIds,
    VectorTableParameters<TVector> vectorTableParameters = null,
    bool ignoreDeleted = true);
public IEnumerable<(long, TVector)> VectorsGetAll<TVector>(
    string tableName,
    VectorTableParameters<TVector> vectorTableParameters = null,
    bool ignoreDeleted = true);

public void VectorsInsert(string tableName,
    IList<(long, float[])> vectors,
    VectorTableParameters<float[]> vectorTableParameters = null);
public void VectorsInsert(string tableName,
    IList<(long, double[])> vectors,
    VectorTableParameters<double[]> vectorTableParameters = null);
public void VectorsRemove<TVector>(string tableName, List<long> externalIds,
    VectorTableParameters<TVector> vectorTableParameters = null);

public IEnumerable<(long externalId, float distance)> VectorsSearchSimilar(
    string tableName, float[] queryVector, int quantity = 10,
    VectorTableParameters<float[]> vectorTableParameters = null,
    bool ignoreDeleted = true);
public IEnumerable<(long externalId, double distance)> VectorsSearchSimilar(
    string tableName, double[] queryVector, int quantity = 10,
    VectorTableParameters<double[]> vectorTableParameters = null,
    bool ignoreDeleted = true);
```

Public transaction state (not part of the 79-method count):

```csharp
public int ManagedThreadId;
public long CreatedUdt;
public RandomKeySorter RandomKeySorter;
public bool ValuesLazyLoadingIsOn { get; set; }
public bool ReadVisibilityScopeModifier_GenerateNewTableForRead;
public bool ReadVisibilityScopeModifier_DirtyRead;
```

## 5. Core Transaction Recipes and Semantics

### 5.1 Insert, select, direct pointer, and aggregates

Keys are unique inside a table. DBreeze stores neither generic type metadata nor a schema declaration; callers must use the same byte-compatible key/value types when reading.

```csharp
byte[] pointer;
bool wasUpdated;

using (var tran = engine.GetTransaction())
{
    tran.Insert<int, string>(
        "users", 100, "Alice",
        out pointer, out wasUpdated,
        dontUpdateIfExists: true);
    tran.Commit();
}

using (var tran = engine.GetTransaction())
{
    Row<int, string> row = tran.Select<int, string>("users", 100);
    if (row.Exists)
        Console.WriteLine(row.Value);

    // The 8-byte link remains useful while the physical row has not moved/recreated.
    Row<int, string> direct = tran.SelectDirect<int, string>("users", pointer);

    ulong count = tran.Count("users");
    Row<int, string> min = tran.Min<int, string>("users");
    Row<int, string> max = tran.Max<int, string>("users");
    if (min.Exists && max.Exists) { /* use min.Key / max.Key */ }
}
```

Always check `Row.Exists` before reading `Key`, `Value`, object data, or data-block links. A direct pointer is not a durable business identifier: table recreation, compaction/replacement, or row rewrites can invalidate it.

### 5.2 Partial values

`InsertPart` overwrites bytes starting at zero-based `startIndex`; the resulting value cannot exceed `Int32.MaxValue` bytes. It is intended for byte-compatible fixed layouts, not for patching an arbitrary serializer format.

```csharp
using (var tran = engine.GetTransaction())
{
    byte[] rowPointer;
    bool wasUpdated;
    tran.InsertPart<int, byte[]>(
        "fixed-layout", 42,
        new byte[] { 0x01, 0x02, 0x03 },
        16, out rowPointer, out wasUpdated);
    tran.Commit();
}
```

### 5.3 Remove, rename, and destructive clear

```csharp
using (var tran = engine.GetTransaction())
{
    bool removed;
    byte[] deletedValue;
    tran.RemoveKey<int>("events", 99, out removed, out deletedValue);

    byte[] newPointer;
    bool changed;
    tran.ChangeKey<int>("events", 42, 100, out newPointer, out changed);
    tran.Commit();
}
```

`RemoveAllKeys(table, false)` is the normal transactional clear. `RemoveAllKeys(table, true)` recreates the physical table files immediately, resets nested-table state, can disrupt concurrent readers, and must be treated as destructive/non-rollbackable maintenance. Do not use the absence of `Commit()` as a safety mechanism for the recreation form.

### 5.4 Data blocks

Data blocks keep large payload bytes outside the indexed row. A block payload is still a CLR `byte[]`/serialized `TValue`, so its practical limit is `Int32.MaxValue`, not greater than 2 GB.

- `InsertDataBlock`: returns a 16-byte pointer that may change when an update cannot overwrite in place. Persist the returned pointer again.
- `InsertDataBlockWithFixedAddress<T>`: returns a stable 16-byte address; updates keep the address stable.
- `SelectDataBlock` and `SelectDataBlockWithFixedAddress<T>` read directly from transaction plus pointer.

```csharp
byte[] fixedAddress;
using (var tran = engine.GetTransaction())
{
    fixedAddress = tran.InsertDataBlockWithFixedAddress<byte[]>(
        "documents", null, Encoding.UTF8.GetBytes("payload"));
    tran.Insert<int, byte[]>("documents", 1, fixedAddress);
    tran.Commit();
}

using (var tran = engine.GetTransaction())
{
    Row<int, byte[]> row = tran.Select<int, byte[]>("documents", 1);
    if (row.Exists)
    {
        byte[] payload1 = tran.SelectDataBlockWithFixedAddress<byte[]>(
            "documents", row.Value);
        byte[] payload2 = row.GetDataBlockWithFixedAddress<byte[]>(0);
    }
}
```

### 5.5 Random-key batches

`RandomKeySorter` buffers operations in transaction memory, sorts each table by key, then applies removals followed by inserts. `Commit()` calls `Flush()` automatically; explicit flush is useful to bound memory.

```csharp
using (var tran = engine.GetTransaction())
{
    tran.SynchronizeTables("events");
    for (int i = 0; i < 100000; i++)
        tran.InsertRandomKeySorter<int, string>("events", GetRandomKey(), "value");

    tran.RemoveRandomKeySorter<int>("events", obsoleteKey);
    tran.RandomKeySorter.Flush("events");
    tran.Commit();
}
```

The equivalent field API is `tran.RandomKeySorter.Insert(...)`, `.Remove(...)`, `.Flush(table)`, and `.Flush()`. Do not rely on `AutomaticFlushLimitQuantityPerTable`; it is retained for compatibility but current batching is flushed explicitly or by commit.

For large random updates, `Technical_SetTable_OverwriteIsNotAllowed(table)` can trade temporary file growth for sequential appends. Call it before modifying that table; it lasts only for the transaction. Ascending input keys still provide the best storage behavior.

### 5.6 Traversal selection guide

All traversal APIs are lazy and return keys in byte-lexicographic order.

| Need | Forward | Backward |
|:-----|:--------|:---------|
| Whole table | `SelectForward` | `SelectBackward` |
| Begin at a key | `SelectForwardStartFrom` | `SelectBackwardStartFrom` |
| Bounded range | `SelectForwardFromTo` | `SelectBackwardFromTo` |
| Key prefix | `SelectForwardStartsWith` | `SelectBackwardStartsWith` |
| Nearest existing prefix | `SelectForwardStartsWithClosestToPrefix` | `SelectBackwardStartsWithClosestToPrefix` |
| Skip N from edge | `SelectForwardSkip` | `SelectBackwardSkip` |
| Skip N from a key | `SelectForwardSkipFrom` | `SelectBackwardSkipFrom` |
| Merge equal schemas | `Multi_SelectForwardFromTo` | `Multi_SelectBackwardFromTo` |

For backward ranges, `startKey` is normally the high key and `stopKey` the low key.

```csharp
using (var tran = engine.GetTransaction())
{
    foreach (var row in tran.SelectForwardFromTo<int, string>(
        "events", 100, true, 200, false)) { }

    foreach (var row in tran.SelectBackwardFromTo<int, string>(
        "events", 200, true, 100, true)) { }

    foreach (var row in tran.SelectForwardSkipFrom<int, string>(
        "events", 100, 50UL)) { }

    foreach (var row in tran.SelectForwardStartsWith<string, byte[]>(
        "names", "Alex")) { }
}
```

The `grabSomeLeadingRecords` range overload adds records immediately before the forward start or immediately before the backward start in traversal order. It is useful for time intervals that may have begun outside the requested range.

Multi-table APIs merge rows from existing and missing tables into one sorted stream; `Row.TableName` identifies the source:

```csharp
var tables = new HashSet<string> { "events-eu", "events-us", "events-missing" };
using (var tran = engine.GetTransaction())
{
    foreach (var row in tran.Multi_SelectBackwardFromTo<long, string>(
        tables, Int64.MaxValue, true, Int64.MinValue, true))
    {
        Console.WriteLine(row.TableName + ": " + row.Key);
    }
}
```

Avoid `SelectForward(...).Where(...)` when a bounded/prefix key traversal can express the query; LINQ filtering usually scans more records.

### 5.7 Replacing a table file

`RestoreTableFromTheOtherFile` atomically substitutes the destination table storage while coordinating DBreeze readers. It is a destructive maintenance primitive: source files are consumed/deleted.

- `sourceTableBelongsToEngine: false`: `newTableFullPath` is a physical source table path.
- `sourceTableBelongsToEngine: true`: the argument is a source table name in the same engine; use a temporary table.

Synchronize all participating names, close unrelated handles, keep backups, and never use it as normal CRUD. It is unavailable in remote-instance storage.

## 6. Scheme API (6 Methods)

Tables do not need to be declared. `Scheme` manages existing table metadata and physical files.

```csharp
public string GetTablePathFromTableName(string userTableName);
public bool IfUserTableExists(string userTableName);
public List<string> GetUserTableNamesStartingWith(string mask);
public void DeleteTable(string userTableName);
public void RenameTable(string oldUserTableName, string newUserTableName);
public void Dispose();
```

Usage:

```csharp
if (engine.Scheme.IfUserTableExists("old-users"))
    engine.Scheme.RenameTable("old-users", "users");

List<string> shards = engine.Scheme.GetUserTableNamesStartingWith("users-");
string path = engine.Scheme.GetTablePathFromTableName("users"); // or "MEMORY"
```

- `DeleteTable` deletes metadata plus data/rollback/helper files and is not transaction rollback.
- `RenameTable` waits for active users of the source table. Renaming across disk/memory/different alternative locations is rejected.
- `GetUserTableNamesStartingWith(String.Empty)` lists all user tables.
- Do not call `engine.Scheme.Dispose()` yourself. `DBreezeEngine` owns and disposes its Scheme. The method is public because `Scheme` implements `IDisposable`.

## 7. Ordered Bytes and Composite Keys

### 7.1 Conversion rule

DBreeze compares serialized keys lexicographically. Use DBreeze BigEndian/order-preserving converters for ordered numeric key components. Platform-endian `BitConverter` is acceptable for private value formats but not as an ordered-key encoding.

| Type | Key bytes | To bytes | From bytes |
|:-----|----------:|:---------|:-----------|
| `byte`, `sbyte`, `bool` | 1 | `To_1_byte_array()` | `To_Byte()`, `To_SByte()`, `To_Bool()` |
| `char` | 2 | `To_2_byte_array()` | `To_Char()` |
| `short`, `ushort` | 2 | `To_2_bytes_array_BigEndian()` | `To_Int16_BigEndian()`, `To_UInt16_BigEndian()` |
| `int`, `uint` | 4 | `To_4_bytes_array_BigEndian()` | `To_Int32_BigEndian()`, `To_UInt32_BigEndian()` |
| `long`, `ulong` | 8 | `To_8_bytes_array_BigEndian()` | `To_Int64_BigEndian()`, `To_UInt64_BigEndian()` |
| `float` | 4 | `To_4_bytes_array_BigEndian()` | `To_Float_BigEndian()` |
| `double` | 9 | `To_9_bytes_array_BigEndian()` | `To_Double_BigEndian()` |
| `decimal` | 15 | `To_15_bytes_array_BigEndian()` | `To_Decimal_BigEndian()` |
| `DateTime` | 8 | `To_8_bytes_array()` | `To_DateTime()` |
| `Guid` | 16 | `ToByteArray()` | `new Guid(bytes)` |
| `string` / `DbUTF8` | variable | `new DbUTF8(value).GetBytes()` | `new DbUTF8(bytes).Get` |
| `DbAscii`, `DbUnicode` | variable | `.GetBytes()` | wrapper `.Get` |
| `byte[]` | variable | unchanged | unchanged |

Most nullable numeric forms prepend a one-byte null marker and use the correspondingly named 3/5/9/10/16-byte functions. `bool?` occupies one byte with three states. Keys themselves cannot be nullable.

### 7.2 `.ToIndex()` versus `.ToBytes()`

- `5.ToIndex(100L)` encodes the leading integer as one index byte, followed by the 8-byte `long`: 9 bytes total.
- `5.ToBytes(100L)` encodes `int` at its normal 4 bytes, followed by the 8-byte `long`: 12 bytes total.

Use `.ToIndex()` for several logical indexes in one table:

```csharp
byte[] primary = 1.ToIndex(entityId);
byte[] byCreated = 2.ToIndex(createdUtc, entityId);
byte[] byOwner = 3.ToIndex(ownerId, entityId);

tran.Insert<byte[], byte[]>("entities", primary, serializedEntity);
tran.Insert<byte[], byte[]>("entities", byCreated, primary);
tran.Insert<byte[], byte[]>("entities", byOwner, primary);
```

Range and prefix queries should fill trailing components deliberately:

```csharp
byte[] start = 2.ToIndex(fromUtc, Int64.MinValue);
byte[] stop = 2.ToIndex(toUtc, Int64.MaxValue);

foreach (var row in tran.SelectForwardFromTo<byte[], byte[]>(
    "entities", start, true, stop, true)) { }

byte[] exactDatePrefix = 2.ToIndex(dayUtc);
foreach (var row in tran.SelectForwardStartsWith<byte[], byte[]>(
    "entities", exactDatePrefix)) { }
```

Parse only with known component widths:

```csharp
byte index = key.Substring(0, 1).To_Byte();
DateTime created = key.Substring(1, 8).To_DateTime();
long id = key.Substring(9, 8).To_Int64_BigEndian();
```

Useful byte helpers include `ConcatMany`, `Substring`, `CopyInsideArrayCanGrow`, `ToBytesString`, `ToByteArrayFromHex`, and fixed-size string columns via `To_FixedSizeColumn` / `From_FixedSizeColumn`.

### 7.3 Serialized value wrappers

DBreeze also exposes value wrappers:

```csharp
tran.Insert<uint, DbMJSON<Article>>("articles-json", 1U, new Article());
tran.Insert<uint, DbXML<Article>>("articles-xml", 1U, new Article());

Row<uint, DbMJSON<Article>> row =
    tran.Select<uint, DbMJSON<Article>>("articles-json", 1U);
if (row.Exists)
{
    Article value = row.Value.Get;
    string serialized = row.Value.SerializedObject;
}
```

`DbCustomSerializer<T>` uses process-wide string delegates:

```csharp
CustomSerializator.Serializator = delegate(object value)
{
    return MySerializer.SerializeToString(value);
};
CustomSerializator.Deserializator = delegate(string value, Type type)
{
    return MySerializer.DeserializeFromString(value, type);
};
```

The byte-array delegates shown during engine initialization also enable transparent custom object values. Never change any of these delegates concurrently or reinterpret already persisted bytes with an incompatible serializer.

## 8. Collections, Objects, and Nested Tables

### 8.1 Dictionaries and hash sets

Top-level overloads store a collection directly in its table. Three-generic-parameter overloads store it below `(master key, tableIndex)`. With `withValuesRemove: true`, keys absent from the new collection are removed.

```csharp
var roles = new Dictionary<int, string> { { 1, "admin" }, { 2, "reader" } };
var flags = new HashSet<int> { 10, 20 };

using (var tran = engine.GetTransaction())
{
    tran.InsertDictionary<int, string>("global-roles", roles, true);
    tran.InsertHashSet<long, int>("users", userId, flags, 0, true);
    tran.Commit();
}

using (var tran = engine.GetTransaction())
{
    Dictionary<int, string> allRoles =
        tran.SelectDictionary<int, string>("global-roles");
    HashSet<int> userFlags = tran.SelectHashSet<long, int>("users", userId, 0);
}
```

### 8.2 Object layer

Configure `CustomSerializator.ByteArraySerializator` and `ByteArrayDeSerializator` once before engine initialization. The serialized bytes are part of the persistent contract; changing serializer/type layout requires an application migration.

`DBreezeObject<T>` stores one entity plus up to 255 index definitions in the same table. Exactly one index should be primary; secondary index keys automatically include the primary key for uniqueness.

```csharp
public sealed class User
{
    public long Id;
    public string Name;
    public DateTime CreatedUtc;
}

byte[] objectAddress;
using (var tran = engine.GetTransaction())
{
    long id = tran.ObjectGetNewIdentity<long>("users");
    var user = new User { Id = id, Name = "Alice", CreatedUtc = DateTime.UtcNow };

    DBreeze.Objects.DBreezeObjectInsertResult<User> result =
        tran.ObjectInsert<User>("users", new DBreeze.Objects.DBreezeObject<User>
        {
            NewEntity = true,
            Entity = user,
            Indexes = new List<DBreeze.Objects.DBreezeIndex>
            {
                new DBreeze.Objects.DBreezeIndex(1, id) { PrimaryIndex = true },
                new DBreeze.Objects.DBreezeIndex(2, user.CreatedUtc)
            }
        });
    objectAddress = result.PtrToObject;
    tran.Commit();
}

using (var tran = engine.GetTransaction())
{
    DBreeze.Objects.DBreezeObject<User> direct =
        tran.ObjectGetByFixedAddress<User>("users", objectAddress);

    Row<byte[], byte[]> row = tran.Select<byte[], byte[]>("users", 1.ToIndex(1L));
    if (row.Exists)
    {
        DBreeze.Objects.DBreezeObject<User> indexed = row.ObjectGet<User>();
    }
}
```

For update, read the wrapper, update `Entity`, rebuild its complete desired `Indexes`, then call `ObjectInsert` with `NewEntity = false`. An index with `null` value removes that secondary index. `ObjectRemove(table, primaryIndexBytes)` removes the entity and its indexes. `speedUpdate` is for deliberate large batch workflows and can grow the file.

### 8.3 Nested tables

Nested/fractal tables store table roots inside parent row values. Each root consumes a 64-byte slot (`tableIndex 0` uses bytes 0..63, index 1 uses 64..127, etc.). Prefer composite keys for new flat schemas; use nested tables when hierarchical isolation is valuable.

- `InsertTable` obtains a writable nested table and creates it if required.
- `SelectTable` obtains a read-only handle without creating physical data.
- The parent transaction commits nested writes.
- Dispose/close each nested handle in large loops; otherwise open state accumulates until transaction disposal.

```csharp
using (var tran = engine.GetTransaction())
{
    tran.SynchronizeTables("orders");
    using (NestedTable lines = tran.InsertTable<long>("orders", orderId, 0))
    {
        lines.Insert<int, string>(1, "first line");
        lines.Insert<int, string>(2, "second line");
    }
    tran.Commit();
}

using (var tran = engine.GetTransaction())
{
    foreach (Row<long, byte[]> order in tran.SelectForward<long, byte[]>("orders"))
    {
        using (NestedTable lines = order.GetTable(0))
        {
            foreach (Row<int, string> line in lines.SelectForward<int, string>()) { }
        }
    }
}
```

## 9. Text Search

DBreeze text search uses a word-aligned bitmap index and external `byte[]` document IDs.

- `containsWords`: substring-oriented indexing with configurable minimum length (default 3).
- `fullMatchWords`: exact tokens, ideal for tags.
- `TextInsert`: smart replacement of a document's searchable set.
- `TextAppend`: adds words without replacing the existing set.
- `TextRemove`: removes specified full-match words.
- `TextRemoveAll`: removes all searchable words for that document.
- `deferredIndexing: false`: index becomes available with commit; `true`: work continues in the engine background indexer.

Synchronize the text table before mutation when the transaction writes other tables too.

```csharp
byte[] documentId = 100L.To_8_bytes_array_BigEndian();
using (var tran = engine.GetTransaction())
{
    tran.TextInsert(
        "articles-text", documentId,
        "The quick brown fox",
        "#CATEGORY_NEWS #LANG_EN",
        deferredIndexing: false,
        containsMinimalLength: 3);
    tran.Commit();
}
```

Read stored searchable tokens for selected documents:

```csharp
using (var tran = engine.GetTransaction())
{
    var ids = new HashSet<byte[]> { documentId };
    Dictionary<byte[], HashSet<string>> searchable =
        tran.TextGetDocumentsSearchables("articles-text", ids);
}
```

Compose queries with logical blocks:

```csharp
using (var tran = engine.GetTransaction())
{
    DBreeze.TextSearch.TextSearchTable text = tran.TextSearch("articles-text");
    var result = text.BlockAnd("fox dog", "")
        .And(text.BlockOr("brown black", ""))
        .And("", "#LANG_EN")
        .Exclude("", "#CATEGORY_SPORTS");

    foreach (byte[] id in result.GetDocumentIDs()) { }
}
```

For optional user filters, use `ignoreOnEmptyParameters: true` on block operations. Range-limit results using `ExternalDocumentIdStart`, `ExternalDocumentIdStop`, and `Descending`; when descending, start is the high ID and stop is the low ID.

Deferred indexing means commit completion is not a search-visibility barrier. Use `BackgroundTasksExternalNotifier` or an application-level readiness policy rather than arbitrary sleeps.

## 10. HNSW Vectors (NET472 / `NET6FUNC`)

Use one dedicated DBreeze table per vector index and never mix normal application rows into it. `float[]` is normally preferred over `double[]`. All vectors and queries in one table must have identical, non-zero dimensionality; validate this in application code.

Vectors are normalized by the current API. Re-inserting an existing external ID adds the new vector and soft-deletes the old node. Deleted nodes remain for graph continuity/storage until an application compaction workflow rebuilds the table.

### 10.1 Per-table parameters

```csharp
var parameters = new Transaction.VectorTableParameters<float[]>
{
    BucketSize = 100000,
    QuantityOfLogicalProcessorToCompute = 0, // automatic, about 70% of CPUs
    NeighbourSelection =
        Transaction.VectorTableParameters<float[]>.eNeighbourSelectionHeuristic
            .NeighbourSelectSimple,
    TurboQuant = new DBreeze.HNSW.TurboQuantParams
    {
        Mode = DBreeze.HNSW.eTurboQuantMode.None,
        BitWidth = 4,
        RandomSeed = 42
    }
};
```

| Parameter | Contract |
|:----------|:---------|
| `GetItem` | Optional external vector loader `Func<long,TVector>`. When used, the index can avoid internal vector storage; pass the compatible loader on every transaction that must materialize vectors. |
| `QuantityOfLogicalProcessorToCompute` | `0` selects automatic parallelism; maximum useful explicit value is normally `Environment.ProcessorCount`. |
| `BucketSize` | Default 100000; smaller values create more independently computed buckets. |
| `NeighbourSelection` | Graph-construction choice. Treat it as immutable after the table has data. |
| `TurboQuant.Mode` | `None` disables quantization; `MSE` optimizes reconstruction; `InnerProduct` uses the residual/QJL path for inner-product estimation. |
| `TurboQuant.BitWidth` | 1..8 when enabled. `Mode`, not the default `BitWidth` field alone, determines whether quantization is enabled. |
| `TurboQuant.RandomSeed` | Deterministic quantizer seed. Keep it stable for a persistent table. |

Use the same vector type and compatible parameters every time a table is opened. Changing storage/quantization/heuristic assumptions after data exists can make results invalid even when compilation succeeds.

### 10.2 Insert, search, count, remove

```csharp
using (var tran = engine.GetTransaction())
{
    tran.SynchronizeTables("knowledge-vectors");
    tran.VectorsInsert("knowledge-vectors", new List<(long, float[])>
    {
        (1L, new float[] { 0.1f, 0.5f, 0.9f }),
        (2L, new float[] { 0.2f, 0.4f, 0.8f })
    }, parameters);
    tran.Commit();
}

using (var tran = engine.GetTransaction())
{
    foreach (var hit in tran.VectorsSearchSimilar(
        "knowledge-vectors",
        new float[] { 0.15f, 0.45f, 0.85f },
        quantity: 10,
        vectorTableParameters: parameters,
        ignoreDeleted: true))
    {
        Console.WriteLine(hit.externalId + ": " + hit.distance);
    }
}

using (var tran = engine.GetTransaction())
{
    tran.VectorsRemove<float[]>(
        "knowledge-vectors", new List<long> { 2L }, parameters);
    tran.Commit();
}
```

`VectorsCount<T>(..., onlyDeletedCount: false)` returns active count; `onlyDeletedCount: true` returns deleted count. Their sum is the total logical history currently represented by the index.

### 10.3 Fetch by ID and enumerate all

Both APIs are lazy. `ignoreDeleted: true` is the default; `false` includes soft-deleted external IDs/vectors.

```csharp
using (var tran = engine.GetTransaction())
{
    foreach (var item in tran.VectorsGetByExternalId<float[]>(
        "knowledge-vectors", new List<long> { 1L, 2L }, parameters,
        ignoreDeleted: false))
    {
        long id = item.Item1;
        float[] vector = item.Item2;
    }

    foreach (var item in tran.VectorsGetAll<float[]>(
        "knowledge-vectors", parameters, ignoreDeleted: true))
    {
        long id = item.Item1;
        float[] vector = item.Item2;
    }
}
```

The same flat `IEnumerable<(long,TVector)>` contract applies to `double[]`:

```csharp
using (var tran = engine.GetTransaction())
{
    foreach (var item in tran.VectorsGetAll<double[]>(
        "double-vectors", doubleParameters, ignoreDeleted: false))
    {
        long id = item.Item1;
        double[] vector = item.Item2;
    }
}
```

Finish enumeration before disposing the transaction. Early `break` inside `foreach` is safe because the enumerator releases its read lock.

## 11. Synchronized Engine Resources

`engine.Resources` is an engine-owned in-memory dictionary synchronized with an internal DBreeze table. It can be called inside or outside user transactions and does not need to be listed in `SynchronizeTables`.

```csharp
engine.Resources.Insert<string>("AppConfig_Theme", "Dark");
string theme = engine.Resources.Select<string>("AppConfig_Theme");
foreach (var item in engine.Resources.SelectStartsWith<string>("AppConfig_")) { }
engine.Resources.Remove("AppConfig_Theme");
```

Use Resources for small shared state/configuration, not as a substitute for high-volume transactional tables.

## 12. High-Load Checklist

- One long-lived engine per physical database; many short transactions.
- One state-changing transaction per managed thread; async work outside transaction scope.
- Reserve the complete multi-table write set once with `SynchronizeTables`.
- Keep `NotifyAhead_WhenWriteTablePossibleDeadlock = true`.
- Use ordered composite keys and bounded/prefix traversal instead of table scans.
- Enumerate lazy results inside the transaction; dispose nested/vector enumerators early.
- Use `RandomKeySorter` for large random-key batches; explicitly flush to bound memory.
- Use `ValuesLazyLoadingIsOn = true` when many values are skipped, `false` when nearly all are consumed.
- Keep serializer, text encryption, and vector table parameters stable as persistent format contracts.
- Treat `RemoveAllKeys(..., true)`, `Scheme.DeleteTable`, table replacement, restore, and text-table swaps as coordinated maintenance.
- Store durable business IDs, not DBreeze physical pointers, as external identifiers.
- Log full exception chains plus `DBisOperableReason`; never continue after failed startup recovery.
