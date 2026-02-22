# DBreeze Database: Comprehensive LLM Developer Context & Guide (v1.132+)

**Role:** You are an expert C# developer working with DBreeze (v1.132+), an ACID-compliant, embedded Key-Value, multi-paradigm database (Key-Value, Objects, NoSQL, Vector DB, TextSearch). 

**Goal:** Use this context to write highly optimized, thread-safe, correct DBreeze code. Always work inside transactions. Prioritize Scheme for simple ops, Transactions for complex. Use DBreeze.Utils for byte conversions. Leverage patterns, lazy loading, memory tables.

## 1. Core Architecture & Transactions (MANDATORY)

- **Storage:** Keys/Values as `byte[]`. Generics auto-convert types.
- **Transactions:** ALL ops in `using (var tran = engine.GetTransaction()) { ... tran.Commit(); }`. No nesting. One tran = one thread.
- **Thread Safety:** `tran.SynchronizeTables(...)` before writes to avoid deadlocks (details sec. 2).
- **Commits/Rollbacks:** `Commit()` saves; dispose auto-rollbacks.
- **Scheme vs Transactions:** Scheme for DDL (exists, delete, rename). Transactions for DML (insert/select/remove).

**Basic Example:**
```csharp
using DBreeze; using DBreeze.Utils;
var engine = new DBreezeEngine("path");
engine.Scheme.IfUserTableExists("t1"); // Check
using (var tran = engine.GetTransaction()) {
    tran.Insert<int,string>("t1", 1, "val");
    tran.Commit();
}
```

**Memory Tables:** Pure RAM (no disk).
```csharp
var cfg = new DBreezeConfiguration { Storage = DBreezeConfiguration.eStorage.MEMORY };
var memEng = new DBreezeEngine(cfg);
```

## 2. Table Synchronization & Locking Patterns (CRITICAL - DEADLOCK PREVENTION)

Writes lock tables. Parallel reads OK, but multi-table writes risk deadlocks. `SynchronizeTables` once/transaction, before mods.

**Patterns (*, $, #):**
- `*`: Any chars (e.g. "Articles*" → Articles1, Articles/Sub).
- `$`: No `/` (e.g. "Items$" → Items1, not Items/Sub).
- `#`: No `/` + `/` + char (e.g. "Items#/Pic" → Items1/Pic).

**Example (Deadlock-safe multi-thread):**
```csharp
// Thread1
using (var tran = engine.GetTransaction()) {
    tran.SynchronizeTables("t1", "t2"); // Or patterns: "Articles*", "Users$"
    tran.Insert("t1", k1, v1); tran.Insert("t2", k2, v2);
    tran.Commit();
}
// Thread2 same order → no deadlock.
```

## 3. Byte[] Conversions & DBreeze.Utils (CRITICAL - SORTABLE BYTES)

Lexico-sort requires BigEndian. Use `DBreeze.Utils` (not BitConverter).

**Key Conversions (public):**
- `int/uint/float.To_4_bytes_array_BigEndian()`
- `long/ulong/double.To_8_bytes_array_BigEndian()`
- `DateTime.To_8_bytes_array()`
- `Composite: .Concat() / .ConcatMany() / .ToIndex(params) / .ToBytes(params)`

**Example Composite Key (DateTime + int + long):**
```csharp
using DBreeze.Utils;
byte[] key = DateTime.Now.To_8_bytes_array()
    .Concat(123.To_4_bytes_array_BigEndian())
    .Concat(456L.To_8_bytes_array_BigEndian());
tran.Insert<byte[],string>("t1", key, "val");
var row = tran.Select<byte[],string>("t1", key);
DateTime dt = row.Key.Substring(0,8).To_DateTime();
int i = row.Key.Substring(8,4).To_Int32_BigEndian();
long l = row.Key.Substring(12,8).To_Int64_BigEndian();
```

**Full List (public, BigEndian prioritised):**
- NULLables: `To_*_NULL()` (1 extra byte).
- `To_FixedSizeColumn(str, size, ascii)` / `From_FixedSizeColumn(bytes, ascii)`.
- Strings: `To_AsciiBytes()` / `To_UTF8Bytes()` / `To_UnicodeBytes()`.
- Utils: `ConcatMany()`, `ToBytesString()`, `ToHexFromByteArray()`.

## 4. Querying & Iteration (NO LINQ ON KEYS - FULL SCANS)

Auto-sorted ascending. Use specific methods (fast index traversal).

**Methods:**
- `SelectForward/Backward<TKey,TVal>(table)`
- `SelectForwardStartFrom<>(table, key, incl)`
- `SelectForwardFromTo<>(table, startKey/incl, endKey/incl)` (+`grabSomeLeadingRecords`)
- `SelectForwardStartsWith<>(prefix)`
- `SelectForwardStartsWithClosestToPrefix<>(prefix)`
- `Multi_SelectForwardFromTo<>(tablesHashSet, start, end)` (multi-table merge).
- Skip: `SelectForwardSkip<>(qty)` / `SelectForwardSkipFrom<>(key, qty)`.

**Example Range:**
```csharp
foreach (var row in tran.SelectForwardFromTo<DateTime,string>("events", dtFrom, true, dtTo, true)) { ... }
```

## 5. Lazy Loading Values (OPTIMIZE I/O)

`tran.ValuesLazyLoadingIsOn = true` (default): `row.Value` lazy-loads on access.
- Keys-only: Keep true.
- Keys+Values immediate: `false` before query.

**Example Toggle:**
```csharp
tran.ValuesLazyLoadingIsOn = false; // Eager load
foreach (var row in tran.SelectForward<int,string>("t1")) {
    Console.WriteLine(row.Key + ": " + row.Value); // No extra I/O
}
tran.ValuesLazyLoadingIsOn = true; // Back to lazy
```

## 6. Scheme Operations (DDL)

`engine.Scheme` (no tran):
- `IfUserTableExists(name)`
- `DeleteTable(name)`
- `RenameTable(old, new)`
- `GetUserTableNamesStartingWith(mask)`
- `GetTablePathFromTableName(name)`

## 7. Storing Objects & Entities (EF-like)

Custom serializers: `CustomSerializator.ByteArraySerializator = ...`
- Protobuf/NetJSON/Biser/XML.

**Object Layer (Auto-indexing, up to 255 indexes):**
```csharp
public class Article { public uint Id {get;set;} [Primary? No attr] public string Name {get;set;} }
var art = new Article { Id = tran.ObjectGetNewIdentity<uint>("Articles"), Name="PC" };
tran.ObjectInsert("Articles", new DBreezeObject<Article> {
    NewEntity=true, Entity=art,
    Indexes = { new DBreezeIndex(1, art.Id) {PrimaryIndex=true}, new DBreezeIndex(2, art.Name) }
});
var obj = tran.Select<byte[],byte[]>("Articles", 1.ToIndex(art.Id)).ObjectGet<Article>();
```

CRUD auto-handles indexes (insert/remove/update).

## 8. Advanced Subsystems

**TextSearch:** `tran.TextInsert(table, docId, containsText, fullMatchTags="#TAG")`
- Blocks: `tran.TextSearch(table).BlockAnd("words","#tags").Or(...).GetDocumentIDs()`
- Logical: And/Or/Xor/Exclude. Deferred indexing.

**Vectors (HNSW Similarity/Clustering):** `tran.VectorsInsert(table, list(externalId, float[]/double[]))`
- Search: `tran.VectorsSearchSimilar(table, queryVec, qty)` → (id, distance).
- Soft-delete: `VectorsRemove`.

**DataBlocks:** Huge blobs: `byte[] ptr = tran.InsertDataBlock(table, null, data); row.GetDataBlock(offset)`.

**Resources:** `engine.Resources.Insert<T>(name, obj)` (RAM+disk sync).

**RandomKeySorter:** Batch random inserts sorted: `tran.RandomKeySorter.Insert(...)`.

**Other:** Backup, eXclusive/Shared locks, Parallel reads in tran.

**Best Practices:**
- Bulk: Sort keys ascending before insert.
- MT: Always SynchronizeTables.
- Utils for ALL byte ops.
- Lazy on by default.
- MEMORY for temp/high-speed.
- Patterns for fractal tables (e.g. "Users#/Orders").

Stay optimized, safe, complete.