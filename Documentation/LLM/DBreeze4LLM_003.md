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

### Comprehensive Conversion Table

| Data Type | To Byte[] | Back From Byte[] | Notes |
| --- | --- | --- | --- |
| `byte`, `byte?` | `value.To_1_byte_array()` / `value.To_2_byte_array()` | `valueBytes.To_Byte()` / `To_Byte_NULL()` | Nullable versions include flag bytes. |
| `short`, `ushort` | `To_2_bytes_array_BigEndian()` / `_LittleEndian()` | `To_Int16_BigEndian()` / `To_UInt16_BigEndian()` | Nullable variants add leading flag byte (3 bytes). |
| `int`, `uint` | `To_4_bytes_array_BigEndian()` / `_LittleEndian()` | `To_Int32_BigEndian()` / `To_UInt32_BigEndian()` | Nullable: `To_5_bytes_array_*`, `To_Int32_BigEndian_NULL()`. |
| `long`, `ulong` | `To_8_bytes_array_BigEndian()` / `_LittleEndian()` | `To_Int64_BigEndian()` / `To_UInt64_BigEndian()` | Nullable versions use 9 bytes. |
| `DateTime`, `DateTime?` | `To_8_bytes_array()`, `To_DateTime()` | `.To_DateTime()` / `.To_DateTime_NULL()` | Compatibility methods `To_8_bytes_array_zCompatibility` exist for legacy conversions. |
| `float`, `float?` | `To_4_bytes_array_BigEndian()` / `To_5_bytes_array_BigEndian()` | `To_Float_BigEndian()` / `_NULL()` | Converts using sortable representation with sign/exponent. |
| `double`, `double?` | `To_9_bytes_array_BigEndian()` / `To_10_bytes_array_BigEndian()` | `To_Double_BigEndian()` / `_NULL()` | Uses custom mantissa/exponent formatting. |
| `decimal`, `decimal?` | `To_15_bytes_array_BigEndian()` / `To_16_bytes_array_BigEndian()` | `To_Decimal_BigEndian()` / `_NULL()` | Handles scale and sign for precise ordering. |

### Example: Manual conversion flow

```csharp
int score = 250;
byte[] key = score.To_4_bytes_array_BigEndian();
tran.Insert<byte[], string>("scores", key, "moderate");

var selection = tran.Select<byte[], string>("scores", key);
int restored = selection.Key.To_Int32_BigEndian();
```

For nullable data you will see extra bytes: the first byte is 0 when the value is `null`, and 1 when valid followed by the fixed-size payload.

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

### Notes on index byte usage

- The very first byte of every key created via `.ToIndex()` is reserved for the index ID (0–254). That means you can model *up to 255 logical indexes per table* by assigning a unique byte and reusing the remaining bytes for the value fields.
- When you retrieve a row, call `row.Key.To_Byte()` or `row.Key.First()` to determine whether you are looking at the primary index, a secondary index, or a metadata entry.
- Keeping the index byte consistent across `Insert`/`Select` calls is critical; change it only when intentionally switching the logical index you are addressing.

```csharp
byte[] compound = 5.ToIndex((long)invoiceId, DateTime.UtcNow);
byte index = compound.To_Byte(); // 5
```

Use this pattern to distinguish table regions (primary indexes, secondary indexes, metadata snapshot keys).

## 9. Object Layer with `ToIndex`

DBreeze’s object helpers (`ObjectInsert`, `ObjectRemove`, `ObjectSelect`, etc.) build on `ToIndex` internally. Each `DBreezeObject<T>` contains up to 255 logical indexes, so you map each index to a distinct byte.

```csharp
using var tran = engine.GetTransaction();
var customer = new Customer { Id = 42, Email = "alice@example.com", City = "Berlin" };

tran.SynchronizeTables("customers");
tran.ValuesLazyLoadingIsOn = false;

tran.ObjectInsert("customers", customer, overwriteIfExists: true);

// Secondary index by email
byte[] emailIndex = 2.ToIndex(customer.Email);
tran.Insert<byte[], int>("customers_indexes", emailIndex, customer.Id);

tran.Commit();
```

When you read back the object, inspect the first byte to know which index you are hitting:

```csharp
var iterator = tran.SelectForward<byte[], byte[]>("customers_indexes");
foreach (var row in iterator)
{
    byte index = row.Key.To_Byte();
    if (index == 2)
    {
        string email = row.Key.Substring(1).To_UTF8String();
        Console.WriteLine($"Email hit index 2: {email}");
    }
}
```

When building your own `DBreezeObject<T>` definitions, call `.ToIndex()` in the `GetRow()` overrides to serialize each secondary index consistently.

## 10. Nested Tables

Nested tables store additional tables inside a value, enabling hierarchical storage. Use `InsertTable`/`SelectTable` to work with them.

```csharp
using (var tran = engine.GetTransaction())
{
    var subTable = tran.InsertTable("orders", "line_items");
    subTable.Insert<int, decimal>("items", 1, 99.99m);
    subTable.Insert<int, decimal>("items", 2, 49.49m);
    tran.Commit();
}

using (var tran = engine.GetTransaction())
{
    var subTable = tran.SelectTable("orders", "line_items");
    foreach (var row in subTable.SelectForward<int, decimal>("items"))
        Console.WriteLine(row.Value);
}
```

Nested tables use the same bytes utilities for their keys and can hold objects or raw bytes. You can also call `InsertTable` with an object payload that contains its own internal tables, so the entire structure is serialized atomically.

## 11. Text Search Layer

DBreeze ships a full-text engine that relies on word-aligned bitmap indexes (WABI). Use `TextInsert`, `TextAppend`, and `TextRemove` from within transactions to maintain analyzable text.

```csharp
using (var tran = engine.GetTransaction())
{
    tran.SynchronizeTables("docs");
    var blocks = new BlockOr();
    blocks.Add(tran.TextInsert("docs", "doc1", "The quick brown fox jumps over the lazy dog"));
    tran.Commit();
}

using (var tran = engine.GetTransaction())
{
    var block = new BlockAnd();
    block.Add(ValueBlock.Is("quick"));
    block.Add(ValueBlock.Is("lazy"));
    var results = tran.TextSearch("docs", block);
    foreach (var docId in results)
        Console.WriteLine(docId);
}
```

`BlockAnd`, `BlockOr`, and `BlockNot` let you compose complex filters. `TextSearch` returns document identifiers previously inserted via `TextInsert`. You can mix `Contains` and `FullMatch` semantics inside a single query by combining blocks.

## 12. Vector Layer (HNSW similarity)

DBreeze includes an HNSW-based vector index. Use `VectorsInsert` to store embeddings and `VectorsSearchSimilar` to run similarity queries.

```csharp
float[] embedding = new float[] {0.1f, 0.3f, 0.2f};
var vectorMeta = new VectorLayerMeta("products_embeddings");

using (var tran = engine.GetTransaction())
{
    tran.SynchronizeTables("vectors");
    tran.VectorsInsert("vectors", "p123", embedding, vectorMeta);
    tran.Commit();
}

using (var tran = engine.GetTransaction())
{
    vectorMeta.Normalize(embedding);
    var similar = tran.VectorsSearchSimilar("vectors", embedding, 3);
    foreach (var hit in similar)
        Console.WriteLine($"{hit.Key}: {hit.Score:F3}");
}
```

`VectorsRemove` softly deletes entries from the HNSW graph; they can be reinserted later. The vector layer automatically normalizes vectors unless you opt out.

## 13. Summary

This document covers every required focus area:
1. Public `Transaction` and `Scheme` APIs.
2. Pattern-based locking (`SynchronizeTables`).
3. Lazy-loading control (`ValuesLazyLoadingIsOn`).
4. Byte-level storage helpers in `DBreeze.Utils` (`ToIndex`, `ToBytes`, converters).
5. Working with memory tables and alternate storage.

LLM agents should reference the examples, convert values via `BytesProcessing`, and maintain the transaction patterns shown above for safe and efficient DBreeze interactions.