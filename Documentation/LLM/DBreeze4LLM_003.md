# DBreeze for LLM Agents (Version 003)

LLM agents that consume DBreeze expect complete details about working with transactions, scheme operations, byte-level representations, memory tables, and the utility helpers in `DBreeze.Utils`. The examples use `using DBreeze;` and `using DBreeze.Utils;`.

## 1. Core Concepts

- **Byte[] Storage:** Every key/value pair is stored as a `byte[]` under the hood. Generic wrappers convert your types via `DBreeze.DataTypes.DataTypesConvertor` and `DBreeze.Utils.BytesProcessing`. These conversions preserve lexicographical ordering required by DBreeze’s trie index.
- **Transactions:** All reads and writes must occur inside a `Transaction`. You can only use a transaction from the thread that opened it; nested transactions are not allowed.
- **Scheme:** `engine.Scheme` manages metadata, table creation/deletion, and can resolve physical paths. Use it to inspect or modify the database layout.

## 2. Transaction Lifespan and Locking

```csharp
using (var tran = engine.GetTransaction())
{
    tran.Insert<int, string>("users", 1, "Alice");
    tran.Commit();
}
```

Always `Commit()` at the end of a write sequence. Disposing without committing triggers a rollback.

### Synchronize tables (prevent deadlocks)

Call `tran.SynchronizeTables(...)` once before any key modification when multiple tables will be touched. Patterns avoid deadlocks and simplify locking groups of tables.

```csharp
tran.SynchronizeTables("Articles*", "Users$", "Orders#/Items$");
```

Special pattern symbols:

| Symbol | Behavior |
| --- | --- |
| `*` | Matches prefixes and descendants (`Articles*` matches `Articles`, `Articles/Sub`). |
| `$` | Matches the rest until `/` (no slash). Example: `User$` matches `User1` but not `User/Audit`. |
| `#` | Matches a prefix ending with `/`; useful for nested tables like `Parent#/Child`. |

## 3. Public Transaction Methods (with examples)

- `Insert<TKey,TValue>`: Insert or update key/value pairs.
- `InsertPart`: Update a value in place starting at a byte offset.
- `RemoveKey`, `RemoveAllKeys`: Delete specific keys or the whole table.
- `ChangeKey`: Rename a key while preserving its value.
- `InsertTable`, `SelectTable`: Manage nested tables stored inside values.
- `InsertDataBlock`, `InsertDataBlockWithFixedAddress`: Store large, dynamic blobs outside regular values and reference them via 16-byte pointers.
- `Select`, `SelectDirect`: Fetch rows, optionally supplying raw pointer (8 bytes) returned from insert operations.
- Range iterators: `SelectForward`/`SelectBackward`, `SelectForwardFromTo`, `SelectForwardStartsWith`, `SelectForwardSkip`, and the `Multi_Select...` variants for aggregated traversal.
- Dictionary/hashset helpers: `InsertDictionary`, `SelectDictionary`, `InsertHashSet`, `SelectHashSet`.
- `ObjectInsert`, `ObjectRemove`: Store entities with multiple indexes, use `DBreeze.Objects.DBreezeObject` + `DBreezeIndex`.
- `TextInsert`, `TextSearch`: Manage text search indexes supporting logical blocks.

### Iteration Example Matrix

```
using (var tran = engine.GetTransaction())
{
    tran.Insert<int, string>("events", 1, "Start");
    tran.Insert<int, string>("events", 2, "Middle");
    tran.Insert<int, string>("events", 3, "End");
    tran.Commit();
}

using (var tran = engine.GetTransaction())
{
    // Default forward: ascending keys
    foreach (var row in tran.SelectForward<int, string>("events"))
        Console.WriteLine(row.Value); // Start, Middle, End

    // Backward iteration (descending order)
    foreach (var row in tran.SelectBackward<int, string>("events"))
        Console.WriteLine(row.Value); // End, Middle, Start

    // From-to range. includeStartKey=true, includeStopKey=false
    foreach (var row in tran.SelectForwardFromTo<int, string>("events", 1, true, 3, false))
        Console.WriteLine(row.Key); // 1,2

    // Select forward start from key=2, skip start
    foreach (var row in tran.SelectForwardStartFrom<int, string>("events", 2, false))
        Console.WriteLine(row.Key); // 3

    // Select forward with grabSomeLeadingRecords: fetch rows before key=3
    foreach (var row in tran.SelectForwardFromTo<int, string>("events", 3, true, 3, true, grabSomeLeadingRecords: 1))
        Console.WriteLine(row.Key); // 2,3

    // Select backward skip from key
    foreach (var row in tran.SelectBackwardSkipFrom<int, string>("events", 3, 1))
        Console.WriteLine(row.Key); // 2,1

    // SelectForwardStartsWith over byte[] keys or composite sequences
    byte[] prefix = 2.To_4_bytes_array_BigEndian();
    foreach (var row in tran.SelectForwardStartsWith<byte[], string>("events", prefix))
        Console.WriteLine(row.Key.ToBytesString());
}
```

The `SelectForwardFromTo` parameters `includeStartKey`/`includeStopKey` control whether the boundaries participate. `grabSomeLeadingRecords` fetches records before the start key to assist paging/overlap checks.

### Example: Simple insert + select

```csharp
using (var tran = engine.GetTransaction())
{
    tran.Insert<int, string>("users", 100, "Bob");
    tran.Commit();
}

using (var tran = engine.GetTransaction())
{
    var row = tran.Select<int, string>("users", 100);
    if (row.Exists)
        Console.WriteLine(row.Value);
}
```

## 4. Scheme API Reference (public functions)

| Method | Purpose |
| --- | --- |
| `DeleteTable(string)` | Drops the table from schema and removes physical files. Requires exclusive control. |
| `IfUserTableExists(string)` | Reports existence without creating new tables. |
| `GetUserTableNamesStartingWith(string)` | Lists tables that begin with the mask. Useful for dynamic table discovery. |
| `RenameTable(string, string)` | Safely renames a table after other threads finish reads/writes. |
| `GetTablePathFromTableName(string)` | Returns physical file path or `"MEMORY"` if the table is in RAM. |

Example:

```csharp
if (!engine.Scheme.IfUserTableExists("users"))
    engine.Scheme.DeleteTable("old_users");
```

## 5. Value Lazy Loading

```csharp
tran.ValuesLazyLoadingIsOn = true; // default
foreach (var row in tran.SelectForward<int, byte[]>("users"))
{
    // Only reading row.Key is inexpensive.
    var valueBytes = row.Value; // triggers disk read
}

tran.ValuesLazyLoadingIsOn = false; // read key+value immediately
foreach (var row in tran.SelectForward<int, byte[]>("users"))
{
    var valueBytes = row.Value; // already materialized
}
```

Switch to `false` when you plan to use `row.Value` outside the iterator, or when you know every row requires its full value.

## 6. Byte[] Conversions (`DBreeze.Utils.BytesProcessing`)

Use converters instead of `BitConverter` to ensure sortable bytes.

```csharp
using DBreeze.Utils;

int userId = 12;
DateTime timestamp = DateTime.UtcNow;
byte[] composite = 2.ToIndex(timestamp, userId); // index byte + DateTime + Id

tran.Insert<byte[], string>("events", composite, "logged");

var row = tran.Select<byte[], string>("events", composite);
var index = row.Key.To_Byte(); // returns 2
var parsedTimestamp = row.Key.Substring(1, 8).To_DateTime();
var parsedId = row.Key.Substring(9, 4).To_Int32_BigEndian();
```

Utilities to know:

- `.ToIndex(byte, params object[])`: Compose complex keys (needle for object storage).
- `.ToBytes(params object[])`: General-purpose concatenation.
- `.To_X_bytes_array_*`: Convert primitives with deterministic endianness.
- `.Concat()` / `.ConcatMany()` / `.Substring()` / `.CopyInsideArrayCanGrow()` for raw byte manipulation.

## 7. Working with Memory Tables

Configure `AlternativeTablesLocations` to force specific tables into memory/alternative folders. An empty string indicates in-memory storage.

```csharp
var conf = new DBreezeConfiguration();
conf.AlternativeTablesLocations.Add("mem_*", string.Empty);
engine = new DBreezeEngine(conf);

using (var tran = engine.GetTransaction())
{
    tran.Insert<int, string>("mem_temp", 1, "cached");
    tran.Commit();
}

Console.WriteLine(engine.Scheme.GetTablePathFromTableName("mem_temp")); // prints "MEMORY"
```

Memory tables never hit disk; they exist for fast temporary work and follow the same API semantics.

## 8. `.ToIndex()` & `.To_Byte()` Highlights

`.ToIndex()` lets you prepend an index identifier byte and append converted values, enabling object storage with secondary keys. `.To_Byte()` extracts that index.

```csharp
byte[] compound = 5.ToIndex((long)invoiceId, DateTime.UtcNow);
byte index = compound.To_Byte(); // 5
```

Use this pattern to distinguish table regions (primary indexes, secondary indexes, metadata snapshot keys).

## 9. Summary

This document covers every required focus area:
1. Public `Transaction` and `Scheme` APIs.
2. Pattern-based locking (`SynchronizeTables`).
3. Lazy-loading control (`ValuesLazyLoadingIsOn`).
4. Byte-level storage helpers in `DBreeze.Utils` (`ToIndex`, `ToBytes`, converters).
5. Working with memory tables and alternate storage.

LLM agents should reference the examples, convert values via `BytesProcessing`, and maintain the transaction patterns shown above for safe and efficient DBreeze interactions.