# DBreeze for LLM Agents (version 1.132 - 12366 tokens)

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
// or
List<string> tables2sync = new List<string> {"Articles*", "Users$", "Orders#/Items$"};
tran.SynchronizeTables(tables2sync);
```

Special pattern symbols:

| Symbol | Behavior |
| --- | --- |
| `*` | Matches prefixes and descendants (`Articles*` matches `Articles`, `Articles/Sub`). |
| `$` | Matches the rest until `/` (no slash). Example: `User$` matches `User1` but not `User/Audit`. |
| `#` | Matches a prefix ending with `/`; useful for nested tables like `Parent#/Child`. |


### Async / Await Constraints (CRITICAL)
**Rule for LLM:** DBreeze transactions are strictly bound to the `.NET ManagedThreadId` that opened them. **Never use `await` inside a DBreeze transaction block** if there are subsequent transaction operations. The thread context switch will cause DBreeze to throw an exception.

**BAD:**
```csharp
using(var tran = engine.GetTransaction()) {
    tran.Insert("t1", 1, "A");
    var result = await HttpClient.GetAsync("url"); // Thread context switches here!
    tran.Insert("t1", 2, result); // THROWS EXCEPTION
    tran.Commit();
}
```
**GOOD:** 
Make all preparational operations before opening transaction; in worst case - (Use .Wait() or .GetAwaiter().GetResult() to block the thread, or move the async call outside the transaction):
```csharp
using(var tran = engine.GetTransaction()) {
    tran.Insert("t1", 1, "A");
    var result = Task.Run(() => HttpClient.GetAsync("url")).GetAwaiter().GetResult();
    tran.Insert("t1", 2, result); 
    tran.Commit();
}
```
***

## 3. Public Transaction Methods (Core API)

This chapter outlines the primary methods available on the `DBreeze.Transactions.Transaction` object. Pay special attention to the high-performance batching methods (`Technical_SetTable_OverwriteIsNotAllowed` and `RandomKeySorter`) as they are critical for optimizing DBreeze in production.

### Overview of API
*   **CRUD:** `Insert`, `InsertPart`, `RemoveKey`, `RemoveAllKeys`, `ChangeKey`.
*   **DataBlocks:** `InsertDataBlock`, `InsertDataBlockWithFixedAddress`.
*   **Retrieval:** `Select`, `SelectDirect`.
*   **Aggregations:** `Count`, `Min`, `Max`.
*   **Iterators:** `SelectForward`, `SelectBackward`, `SelectForwardFromTo`, `SelectForwardStartsWith`, `SelectForwardSkip`.
*   **Advanced:** `Multi_SelectForwardFromTo`, `InsertDictionary`, `RandomKeySorter`, `Technical_SetTable_OverwriteIsNotAllowed`.

---

### 1. Basic Insert, Select, and Aggregations
Standard operations. Note the `dontUpdateIfExists` parameter, which is highly useful to avoid pre-checking existence.

```csharp
using (var tran = engine.GetTransaction())
{
    // Basic insert
    tran.Insert<int, string>("users", 100, "Bob");

    // Insert ONLY if key does not exist
    tran.Insert<int, string>("users", 100, "Alice", out byte[] refPtr, out bool wasUpdated, dontUpdateIfExists: true);
    
    tran.Commit();
}

using (var tran = engine.GetTransaction())
{
    var row = tran.Select<int, string>("users", 100);
    if (row.Exists) Console.WriteLine(row.Value);

    // Aggregations are highly optimized
    ulong count = tran.Count("users");
    var min = tran.Min<int, string>("users");
    var max = tran.Max<int, string>("users");
}
```

### 2. Partial Updates (`InsertPart`)
`InsertPart` lets you patch the bytes of an existing value without re-writing the entire value. Supply the zero-based offset where the patch should begin.

```csharp
using (var tran = engine.GetTransaction())
{
    byte[] ptr = null;
    bool updated;

    // Patches the value starting at byte 16
    tran.InsertPart<int, byte[]>(
        "files", 42, new byte[] { 0x01, 0x02, 0x03 }, 
        startIndex: 16, out ptr, out updated);

    tran.Commit();
}
```

### 3. Data Block Helpers (Blobs > 2GB)
Large or dynamically sized blobs can live outside the usual table values via data blocks. `InsertDataBlock` returns a 16-byte handle that you persist inside your value. Use `InsertDataBlockWithFixedAddress<T>` when you need a stable reference that never moves, even after updates.

```csharp
using (var tran = engine.GetTransaction())
{
    byte[] dataBlockPointer = null;
    var chunk = Encoding.UTF8.GetBytes("large text goes here");

    dataBlockPointer = tran.InsertDataBlock("docs", dataBlockPointer, chunk);
    tran.Insert<int, byte[]>("docs", 1, dataBlockPointer);
    tran.Commit();
}

using (var tran = engine.GetTransaction())
{
    var pointer = tran.Select<int, byte[]>("docs", 1).Value;
    byte[] payload = tran.SelectDataBlock("docs", pointer);
}
```

### 4. Renaming & Removing Keys
*   `ChangeKey` lets you rename an existing key while preserving its payload (skips re-writing the value).
*   `RemoveAllKeys` clears a table. Pass `withFileRecreation: true` to instantly truncate the physical file.

```csharp
using (var tran = engine.GetTransaction())
{
    tran.ChangeKey<int>("events", oldKey: 42, newKey: 100, out byte[] ptr, out bool changed);

    tran.RemoveKey<int>("events", 99, out bool wasRemoved, out byte[] deletedValue);
    
    // Deletes everything. If true, recreation happens immediately, no need to Commit()
    tran.RemoveAllKeys("events", withFileRecreation: false); 
    tran.Commit();
}
```

### 5. Iteration Matrix
Because DBreeze keys are sorted lexicographically, iterators are extremely fast. 

```csharp
using (var tran = engine.GetTransaction())
{
    // 1. Default forward: ascending keys
    foreach (var row in tran.SelectForward<int, string>("events")) { }

    // 2. Backward iteration (descending order)
    foreach (var row in tran.SelectBackward<int, string>("events")) { }

    // 3. From-to range. includeStartKey=true, includeStopKey=false
    foreach (var row in tran.SelectForwardFromTo<int, string>("events", 1, true, 3, false)) { }

    // 4. StartsWith over byte[] keys or composite sequences
    byte[] prefix = 2.To_4_bytes_array_BigEndian();
    foreach (var row in tran.SelectForwardStartsWith<byte[], string>("events", prefix)) { }
    
    // 5. Skip pagination (Skips first 100 records)
    foreach (var row in tran.SelectForwardSkip<int, string>("events", 100)) { }
}
```
*Note: `grabSomeLeadingRecords: X` can be added to `SelectForwardFromTo` to fetch records slightly before the start key (useful for overlapping time-series data).*

**Modifying during Iteration:**
If you need to delete or update keys *while* iterating through them, you MUST pass `true` as the `AsReadVisibilityScope` parameter to the iterator. This tells the iterator to read from the snapshot prior to your modifications.

```csharp
using (var tran = engine.GetTransaction())
{
    tran.SynchronizeTables("events");
    // The 'true' parameter freezes the visibility scope for the enumerator
    foreach (var row in tran.SelectForward<int, string>("events", true)) {
        if (row.Value == "EXPIRED") {
            tran.RemoveKey<int>("events", row.Key); // Safe to do inside the loop
        }
    }
    tran.Commit();
}
```

### 6. Multi-Table Selection (`Multi_SelectForwardFromTo`)
Allows reading the exact same structured keys from *different* tables simultaneously, returning them in perfectly sorted order. (e.g., merging "EventsHamburg" and "EventsBerlin").

```csharp
using (var tran = engine.GetTransaction())
{
    var tables = new HashSet<string> { "EventsHamburg", "EventsBerlin" };
    
    foreach (var el in tran.Multi_SelectForwardFromTo<int, string>(
        tables, int.MinValue, true, int.MaxValue, true))
    {
        Console.WriteLine($"Table: {el.TableName} | Key: {el.Key} | Value: {el.Value}");
    }
}
```

### 7. Dictionary & HashSet Helpers
DBreeze includes syntax sugar to directly save and load standard C# `Dictionary` and `HashSet` objects into tables (they are stored internally as nested tables). 

```csharp
using (var tran = engine.GetTransaction())
{
    var dict = new Dictionary<uint, string> { { 10, "A" }, { 11, "B" } };
    
    // The last parameter (withValuesRemove) dictates if missing keys are deleted
    tran.InsertDictionary<int, uint, string>("tblDicts", 1, dict, 0, true);
    
    var loadedDict = tran.SelectDictionary<int, uint, string>("tblDicts", 1, 0);
    tran.Commit();
}
```

### 8. High-Performance Batching (CRITICAL FOR LLM)
**Rule for LLM:** If you are inserting keys in random order, or updating a massive batch of existing rows, HDD/SSD performance will tank due to random I/O. You MUST use one of these two features.

**A. `RandomKeySorter` (For Random Inserts):**
Queues inserts in memory, sorts them by key, and writes them sequentially.

```csharp
using (var tran = engine.GetTransaction())
{
    // Automatically flushes to disk when it hits 10,000 items
    for (int i = 0; i < 100000; i++) {
        int randomKey = GetRandom();
        tran.RandomKeySorter.Insert<int, string>("t1", randomKey, "Val");
    }
    tran.Commit(); // Flushes remaining
}
```

**B. `Technical_SetTable_OverwriteIsNotAllowed` (For Massive Updates):**
Forces DBreeze to write updates to the *end* of the file sequentially rather than seeking and overwriting old byte blocks. This drastically speeds up batch updates at the cost of temporary file size bloat.

```csharp
using (var tran = engine.GetTransaction())
{
    // Turn on Sequential-only writes for this table in this transaction
    tran.Technical_SetTable_OverwriteIsNotAllowed("t1");
    
    for (int i = 0; i < 100000; i++) {
        tran.Insert<int, string>("t1", i, "Updated Value");
    }
    tran.Commit(); 
}
```

Here is the complete example fulfilling the placeholder. This demonstrates how to combine `RandomKeySorter` and `InsertDataBlockWithFixedAddress` to handle massive, out-of-order, large-payload data ingestion.

***

### Example: High-Performance IoT Sensor Logging

**Scenario:** We are receiving millions of JSON payloads from IoT sensors. Due to network latency, the timestamps (our primary keys) arrive **out of order** (randomly). Furthermore, the JSON payloads are dynamically sized. 

**The Problem:**
1. Inserting random keys causes random disk seeks (thrashing the HDD/SSD), drastically slowing down inserts.
2. Storing large, variable-length JSON strings directly inside the search tree causes fragmentation and bloat.

**The DBreeze Solution:**
1. Use `InsertDataBlockWithFixedAddress` to write the JSON payload to a continuous file on disk. It returns a tiny, fixed **16-byte pointer**.
2. Use `RandomKeySorter` to buffer the `DateTime` keys and 16-byte pointers in RAM, sorting them automatically, and writing them sequentially to the search tree in optimal batches.

#### 1. Ingestion / Writing

```csharp
using System;
using System.Text;
using DBreeze;
using DBreeze.Utils;

public void LogIoTPayloads(DBreezeEngine engine)
{
    using (var tran = engine.GetTransaction())
    {
        // Optional but good practice if multiple tables are involved
        tran.SynchronizeTables("SensorLogs");

        Random rnd = new Random();
        DateTime baseTime = DateTime.UtcNow;

        // Simulate 1,000,000 incoming IoT payloads
        for (int i = 0; i < 1000000; i++)
        {
            // 1. Generate an out-of-order (random) timestamp
            DateTime sensorTime = baseTime.AddSeconds(rnd.Next(-500000, 500000));
            
            // 2. Generate the dynamic payload
            string jsonPayload = $"{{ \"deviceId\": {i}, \"temp\": {rnd.Next(10, 40)}, \"status\": \"OK\" }}";
            byte[] payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);

            // 3. Save payload to DataBlocks, receive a 16-byte pointer
            // 'null' as the 2nd parameter means "create a new block"
            byte[] ptr16 = tran.InsertDataBlockWithFixedAddress<byte[]>("SensorLogs", null, payloadBytes);

            // 4. Hand the pointer to the RandomKeySorter
            // By default, it will auto-flush to disk sequentially every 10,000 items
            tran.RandomKeySorter.Insert<DateTime, byte[]>("SensorLogs", sensorTime, ptr16);
        }

        // 5. Commit flushes any remaining items in the RandomKeySorter buffer to disk safely
        tran.Commit();
    }
}
```

#### 2. Retrieval / Reading

When reading the data back, we iterate over the sorted tree. DBreeze provides a helper directly on the `Row` object to automatically resolve the 16-byte pointer back into the original payload.

```csharp
public void ReadIoTLogs(DBreezeEngine engine, DateTime startTime, DateTime endTime)
{
    using (var tran = engine.GetTransaction())
    {
        // Iterate over the specific time range. Keys are perfectly sorted on disk.
        foreach (var row in tran.SelectForwardFromTo<DateTime, byte[]>(
            "SensorLogs", 
            startTime, true, 
            endTime, true))
        {
            // The row.Value is just the 16-byte pointer. 
            // We use GetDataBlockWithFixedAddress to fetch the actual payload from disk.
            // (0 is the byte index where the pointer starts inside row.Value)
            byte[] payloadBytes = row.GetDataBlockWithFixedAddress<byte[]>(0);
            
            if (payloadBytes != null)
            {
                string jsonPayload = Encoding.UTF8.GetString(payloadBytes);
                Console.WriteLine($"Time: {row.Key:yyyy-MM-dd HH:mm:ss} | Data: {jsonPayload}");
            }
        }
    }
}
```

### Why this is the ultimate DBreeze pattern:
*   **Tree Health:** The main index table (`SensorLogs`) remains extremely small and fast because it only stores `8-byte` DateTime keys and `16-byte` pointers.
*   **Disk Health:** `RandomKeySorter` turns what would be millions of random disk jumps into smooth, sequential, bulk writes.
*   **Updates:** If you ever need to update a specific JSON payload later, you can pass the existing `ptr16` back into `InsertDataBlockWithFixedAddress`, and DBreeze will overwrite or relocate the payload while returning a stable pointer, requiring no changes to the search tree.


### 9. High-Speed Synchronized Cache (`engine.Resources`)
If you need an in-memory dictionary for ultra-fast access, but want it automatically synchronized to the disk without writing transaction boilerplate, use `engine.Resources`.

```csharp
// Insert/Update (Available immediately in RAM, saved to disk)
engine.Resources.Insert<string>("AppConfig_Theme", "Dark");

// Read (Reads from RAM, extremely fast)
var theme = engine.Resources.Select<string>("AppConfig_Theme");

// Prefix iteration
foreach (var item in engine.Resources.SelectStartsWith<string>("AppConfig_")) {
    Console.WriteLine($"{item.Key}: {item.Value}");
}

// Delete
engine.Resources.Remove("AppConfig_Theme");
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

Here is the highly refined and expanded chapter. I have extracted the exact byte sizes and specialized behaviors (like `bool?` being 1 byte, but `int?` being 5 bytes) directly from the `BytesProcessing.cs` source code. This level of detail is critical for an LLM to accurately calculate `Substring` offsets when parsing composite keys.

***

## 6. Byte[] Conversions & Manipulations (`using DBreeze.Utils;`)

DBreeze stores all keys and values as `byte[]`. To maintain **lexicographical sorting** (where `byte[]` comparisons match the logical ordering of the underlying data type), standard `.NET BitConverter` is strictly prohibited. You **must** `using DBreeze.Utils;`. 

By default, always use the **`_BigEndian`** variants for keys to ensure correct ascending/descending tree traversal.

### 1. Comprehensive Data Type Conversion Table

When parsing composite keys, you must know exactly how many bytes each data type consumes to calculate your `.Substring()` offsets correctly.

*Note on Nullables:* Most nullable types prepend a `1-byte` flag (`0` = null, `1` = has value), increasing the total length by 1 byte. 

| Data Type | Byte Length | To Byte[] Extension | From Byte[] Extension |
| :--- | :--- | :--- | :--- |
| **`byte`** | 1 | `val.To_1_byte_array()` | `bytes.To_Byte()` |
| **`byte?`** | 2 | `val.To_2_byte_array()` | `bytes.To_Byte_NULL()` |
| **`bool`** | 1 | `val.To_1_byte_array()` | `bytes.To_Bool()` |
| **`bool?`** | 1 | `val.To_1_byte_array()` *(2=null, 1=true, 0=false)* | `bytes.To_Bool_NULL()` |
| **`char`** | 2 | `val.To_2_byte_array()` | `bytes.To_Char()` |
| **`short` / `ushort`** | 2 | `val.To_2_bytes_array_BigEndian()` | `bytes.To_Int16_BigEndian()` |
| **`int` / `uint`** | 4 | `val.To_4_bytes_array_BigEndian()` | `bytes.To_Int32_BigEndian()` |
| **`int?` / `uint?`**| 5 | `val.To_5_bytes_array_BigEndian()` | `bytes.To_Int32_BigEndian_NULL()`|
| **`float`** | 4 | `val.To_4_bytes_array_BigEndian()` | `bytes.To_Float_BigEndian()` |
| **`long` / `ulong`** | 8 | `val.To_8_bytes_array_BigEndian()` | `bytes.To_Int64_BigEndian()` |
| **`DateTime`** | 8 | `val.To_8_bytes_array()` | `bytes.To_DateTime()` |
| **`double`** | 9 | `val.To_9_bytes_array_BigEndian()` | `bytes.To_Double_BigEndian()` |
| **`decimal`** | 15 | `val.To_15_bytes_array_BigEndian()` | `bytes.To_Decimal_BigEndian()` |

---

### 2. Composing Complex Keys (`.ToIndex()` vs `.ToBytes()`)

DBreeze provides two powerful extensions for taking multiple variables of different types and packing them into a single, perfectly sorted `byte[]`. It is critical to know the difference between them to avoid offsetting your bytes incorrectly.

#### `.ToIndex(params object[])`
Use this when building keys for the **Object Layer** or when you want to use a **1-byte index identifier** at the start of your key. It explicitly converts the *calling integer* into a **single byte**, and then serializes and concatenates the remaining parameters.

*   `5.ToIndex(100L)` -> Results in `9 bytes` total. (`1 byte` for the 5, `8 bytes` for the long).

#### `.ToBytes(params object[])`
Use this for **general-purpose concatenation** where you do not want the first element truncated to a single byte. It serializes *every* parameter according to its true data type size.

*   `5.ToBytes(100L)` -> Results in `12 bytes` total. (`4 bytes` for the int 5, `8 bytes` for the long).

#### Example: Crafting and Parsing with `.ToBytes()`

If you are designing a custom table (not using the Object Layer) and want a composite key made of a `DateTime` and an `int`, use `.ToBytes()`.

```csharp
using DBreeze.Utils;

// --- 1. COMPOSING THE KEY WITH .ToBytes() ---
DateTime timestamp = new DateTime(2023, 1, 1); // 8 bytes
int userId = 12;                               // 4 bytes

// Structure: [DateTime: 8 bytes] + [Int32: 4 bytes] = 12 bytes total
byte[] compositeKey = timestamp.ToBytes(userId); 
// Alternatively: DBreeze.Utils.BytesProcessing.ToBytes(timestamp, userId);

tran.Insert<byte[], string>("log_table", compositeKey, "System booted");

// --- 2. PARSING THE KEY ---
var row = tran.Select<byte[], string>("log_table", compositeKey);

if (row.Exists)
{
    // Extract DateTime (Offset 0, Length 8)
    DateTime parsedTimestamp = row.Key.Substring(0, 8).To_DateTime();
    
    // Extract UserId (Offset 8, Length 4)
    int parsedId = row.Key.Substring(8, 4).To_Int32_BigEndian();
    
    Console.WriteLine($"Time: {parsedTimestamp}, User: {parsedId}");
}
```

**Rule for LLM:** When writing parsing logic (like the `.Substring` calls above), always consult the **Data Type Byte Length Table** to calculate the exact starting index and length for each segment of the composite key.

---

### 3. Raw Byte Manipulation Helpers

If you are dealing with raw payloads, secondary indexes, or modifying values directly, DBreeze provides highly optimized array manipulation tools:

*   **`ConcatMany`**: Much faster than chaining LINQ `.Concat()` when merging multiple arrays.
    ```csharp
    byte[] b1 = new byte[] { 1 };
    byte[] merged = b1.ConcatMany(new byte[] { 2, 3 }, new byte[] { 4, 5 });
    ```
*   **`CopyInsideArrayCanGrow`**: Safely copies bytes into an array at a specific offset. If the offset + new data exceeds the current array, it automatically resizes it (useful for updating variable-length rows).
    ```csharp
    byte[] original = new byte[] { 1, 2, 3 };
    byte[] updated = original.CopyInsideArrayCanGrow(1, new byte[] { 5, 6, 7 }); 
    // Result: { 1, 5, 6, 7 }
    ```
*   **`ToBytesString()` / `ToByteArrayFromHex()`**: The fastest way to convert a `byte[]` to a pure HEX string (e.g., `1F0000000020`) and back. Excellent for logging, debugging, or converting pointers to JSON string properties.

---

### 4. Storing Structured Strings in Values (`To_FixedSizeColumn`)

If you want to treat a `byte[]` value like a structured SQL row (avoiding JSON serialization for raw speed), you can reserve fixed-size spaces for strings. 

*   `To_FixedSizeColumn(fixedSize, isAscii)` adds exactly **2 bytes** of overhead (for length/null flag) to your requested `fixedSize`.
*   If the text is too long, it is safely truncated.

```csharp
using DBreeze.Utils;

string username = "Alice";

// Reserves 50 bytes + 2 bytes overhead = 52 bytes total.
// true = ASCII, false = UTF-8
byte[] columnBytes = username.To_FixedSizeColumn(50, isASCII: true); 

// Retrieve it later
string restored = columnBytes.From_FixedSizeColumn(isASCII: true);
```

### 5. High-Performance Utilities (`DBreeze.Utils`)
DBreeze provides built-in utilities designed for extreme speed and memory efficiency:

*   **Deep Cloning Objects:** Standard C# serialization for cloning is slow. Use `.CloneByExpressionTree()` for blazing-fast deep copies of objects (skips delegates/events/COM objects).
    ```csharp
    var prototype = new ComplexObject();
    var newInstance = prototype.CloneByExpressionTree();
    ```
*   **Streaming Hashing:** For deduplication or ID generation of massive files, use DBreeze's 128-bit MurMurHash implementation.
    ```csharp
    using var fileStream = File.OpenRead("large_video.mp4");
    byte[] hash = DBreeze.Utils.Hash.MurMurHash.MixedMurMurHash3_128_Stream(fileStream);
    ```

## 7. Working with Memory Tables

Configure `AlternativeTablesLocations` to force specific tables into memory/alternative folders. An empty string indicates in-memory storage.
Table names may obey `Special pattern symbols`.

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

## 8. Advanced Key Crafting & Range Query Strategies (.ToIndex)

DBreeze stores and retrieves all data lexicographically by `byte[]`. To build relational concepts, composite keys, or secondary indexes within a single table, you must pack multiple data types into a single byte array. DBreeze provides the highly optimized `.ToIndex()` extension to do this cleanly without manual byte manipulation.

### Core Concept: The `.ToIndex()` Extension
The `.ToIndex(params object[])` method converts an integer prefix into a `byte`, and automatically converts and concatenates all subsequent arguments into a perfectly sortable DBreeze `byte[]` key.

**Rule of Thumb:**
*   `1.ToIndex(...)` means "Primary Index Space".
*   `2.ToIndex(...)` means "Secondary Index Space 1" (e.g., searching by Date).
*   `3.ToIndex(...)` means "Secondary Index Space 2" (e.g., searching by Category).

#### Example: Crafting and Inserting Composite Keys
Here, we build a secondary index where the key consists of: `[Index Identifier: 2] + [DateTime: Created] + [long: EntityId]`. Appending the `EntityId` ensures the composite key remains unique even if multiple entities share the exact same DateTime.

```csharp
using DBreeze.Utils;

using (var t = engine.GetTransaction())
{
    // Key structure: Index Space 2 + DateTime + long
    byte[] key1 = 2.ToIndex(new DateTime(2023, 1, 1), 100L);
    byte[] key2 = 2.ToIndex(new DateTime(2023, 5, 15), 101L);
    byte[] key3 = 2.ToIndex(new DateTime(2023, 5, 15), 102L); // Same date, different ID

    // Value could be null if it's just an index, or a pointer, or data.
    t.Insert<byte[], string>("tblSearch", key1, "Data A");
    t.Insert<byte[], string>("tblSearch", key2, "Data B");
    t.Insert<byte[], string>("tblSearch", key3, "Data C");
    
    t.Commit();
}
```

### Querying Strategies

Because composite keys are sorted lexicographically, you can use specialized DBreeze iterators to traverse them efficiently. **Never use LINQ `.Where()` to filter keys, as it causes full table scans.**

#### 1. Exact Range Queries (`SelectForwardFromTo`)
Use this when you know the exact start and end bounds of your secondary index.

```csharp
// Get everything between Jan 1 and Dec 31
byte[] startKey = 2.ToIndex(new DateTime(2023, 1, 1), long.MinValue);
byte[] endKey = 2.ToIndex(new DateTime(2023, 12, 31), long.MaxValue);

foreach (var row in t.SelectForwardFromTo<byte[], string>(
    "tblSearch", 
    startKey, true,  // true = include startKey
    endKey, true))   // true = include endKey
{
    // row.Key contains the byte[]
    // row.Value contains the string
    Console.WriteLine(row.Value); 
}
```

#### 2. Open-Ended Range Queries (The `MinValue` / `MaxValue` Trick)
If you want to find all records starting from a specific date up to "the end of time", you must fill the trailing parts of the composite key with absolute Maximums or Minimums to ensure the byte array bounds cover all possibilities.

```csharp
// Get everything from May 15th onward.
// Notice the use of long.MinValue and long.MaxValue for the trailing ID parameter!
byte[] startKey = 2.ToIndex(new DateTime(2023, 5, 15), long.MinValue);
byte[] endKey = 2.ToIndex(DateTime.MaxValue, long.MaxValue);

foreach (var row in t.SelectForwardFromTo<byte[], string>("tblSearch", startKey, true, endKey, true))
{
    // Process results...
}
```

#### 3. Prefix Matching (`SelectForwardStartsWith`)
Because of how byte arrays are structured, if you want *all* items for a specific date (ignoring the `long ID` at the end), you can search using just the prefix of the composite key.

```csharp
// We drop the 'long ID' parameter here. 
// It creates a byte[] prefix: [Index 2] + [May 15th]
byte[] prefixKey = 2.ToIndex(new DateTime(2023, 5, 15));

// Will return Data B (ID 101) and Data C (ID 102) because both start with the same Index and Date bytes
foreach (var row in t.SelectForwardStartsWith<byte[], string>("tblSearch", prefixKey))
{
    Console.WriteLine(row.Value);
}
```

#### 4. Descending/Reverse Iteration (`SelectBackward...`)
To get the most recent items first (e.g., sorting by DateTime descending), simply swap to the `Backward` equivalents. 
**Crucial Syntax Rule:** When using `SelectBackwardFromTo`, the `startKey` must be the **higher/maximum** value, and the `endKey` must be the **lower/minimum** value.

```csharp
// Note the reversed order: Max bound goes first, Min bound goes second!
byte[] maxKey = 2.ToIndex(DateTime.MaxValue, long.MaxValue);
byte[] minKey = 2.ToIndex(DateTime.MinValue, long.MinValue);

// Traverses from the newest records down to the oldest
foreach (var row in t.SelectBackwardFromTo<byte[], string>("tblSearch", maxKey, true, minKey, true))
{
    Console.WriteLine(row.Value);
}

// Similarly, to get the newest items for a specific date:
byte[] prefixKey = 2.ToIndex(new DateTime(2023, 5, 15));
foreach (var row in t.SelectBackwardStartsWith<byte[], string>("tblSearch", prefixKey))
{
    Console.WriteLine(row.Value);
}
```

## 9. Working with the Object Layer (Entity Framework Alternative)

Instead of manually maintaining separate DBreeze tables or manual composite keys for secondary indexes, DBreeze provides an "Object Layer". This allows you to store an object *once* while automatically building and maintaining multiple search indexes (up to 255 per entity) within a single table.

### Prerequisites: Global Serializer
To use the Object Layer, DBreeze must know how to serialize your objects to `byte[]`. This must be configured globally **once** before using the database.

```csharp
// Example using Biser, NetJSON, or Protobuf
DBreeze.Utils.CustomSerializator.ByteArraySerializator = (object o) => { return MySerializer.Serialize(o); };
DBreeze.Utils.CustomSerializator.ByteArrayDeSerializator = (byte[] bt, Type t) => { return MySerializer.Deserialize(bt, t); };
```

### Core Concepts & Mechanics
*   **ObjectGetNewIdentity:** Automatically generates thread-safe, monotonically growing IDs (stored at byte `0` internally).
*   **DBreezeObject<T>:** The wrapper required to insert objects. It holds the `Entity` (your class) and its `Indexes`.
*   **DBreezeIndex:** Defines an index for the object. 
    *   Index numbers go from `1` to `255`. 
    *   **Exactly one** index must have `PrimaryIndex = true`. 
    *   By default, DBreeze automatically appends the Primary Key to all Secondary Indexes to guarantee uniqueness.

### 1. Inserting Entities
When inserting a brand new entity, set `NewEntity = true` inside the wrapper to optimize speed (skips a pre-check read).

```csharp
public class User {
    public long Id { get; set; }
    public string Name { get; set; }
    public DateTime Created { get; set; }
}

using (var t = engine.GetTransaction())
{
    var user = new User { 
        Id = t.ObjectGetNewIdentity<long>("tblUsers"), // Auto-increment ID
        Name = "Alice", 
        Created = DateTime.UtcNow 
    };

    t.ObjectInsert<User>("tblUsers", new DBreeze.Objects.DBreezeObject<User>
    {
        NewEntity = true, // TRUE for brand new inserts (speed optimization)
        Entity = user,
        Indexes = new List<DBreeze.Objects.DBreezeIndex>
        {
            // Index 1: Primary Key (ID)
            new DBreeze.Objects.DBreezeIndex(1, user.Id) { PrimaryIndex = true },
            
            // Index 2: Secondary Index (Created Date)
            new DBreeze.Objects.DBreezeIndex(2, user.Created)
        }
    }, false); // Set last parameter to 'true' ONLY for massive batch inserts

    t.Commit();
}
```

### 2. Reading Entities (`ObjectGet<T>`)
Because the Object Layer stores data under index prefixes, you must use the `.ToIndex()` extension to query them, and `.ObjectGet<T>()` to deserialize the wrapper.

```csharp
using (var t = engine.GetTransaction())
{
    // A. Getting a single object by Primary Key (Index 1)
    var row = t.Select<byte[], byte[]>("tblUsers", 1.ToIndex(1L));
    if (row.Exists) {
        User u = row.ObjectGet<User>().Entity;
        Console.WriteLine(u.Name);
    }

    // B. Querying via Secondary Index (Index 2 - Dates) using Range Select
    byte[] startKey = 2.ToIndex(DateTime.MinValue, long.MinValue);
    byte[] endKey = 2.ToIndex(DateTime.MaxValue, long.MaxValue);

    foreach (var r in t.SelectForwardFromTo<byte[], byte[]>("tblUsers", startKey, true, endKey, true))
    {
        // Extract the entity from the row
        var dbObj = r.ObjectGet<User>();
        Console.WriteLine($"Found User: {dbObj.Entity.Name}");
    }
}
```

### 3. Updating Entities
To update, fetch the entity, modify it, recreate the index list with the updated values, and save it using `ObjectInsert` (omitting `NewEntity` or setting it to `false`).

```csharp
using (var t = engine.GetTransaction())
{
    // 1. Fetch
    var wrapper = t.Select<byte[], byte[]>("tblUsers", 1.ToIndex(1L)).ObjectGet<User>();
    
    // 2. Modify Entity
    wrapper.Entity.Name = "Alice Updated";
    wrapper.Entity.Created = new DateTime(2025, 1, 1);

    // 3. Define Indexes (so DBreeze knows how to update secondary indexes)
    wrapper.Indexes = new List<DBreeze.Objects.DBreezeIndex>
    {
        new DBreeze.Objects.DBreezeIndex(1, wrapper.Entity.Id) { PrimaryIndex = true },
        new DBreeze.Objects.DBreezeIndex(2, wrapper.Entity.Created) // Updated date
    };

    // 4. Save
    t.ObjectInsert<User>("tblUsers", wrapper, false);
    t.Commit();
}
```
*Note: To completely remove a specific secondary index from an entity during an update, pass `null` as the value: `new DBreezeIndex(2, null)`.*

### 4. Deleting Entities
To remove an entity from the object layer, supply the table name and the primary key formatted with `.ToIndex()`. DBreeze will automatically clean up the entity and all associated secondary indexes.

```csharp
using (var t = engine.GetTransaction())
{
    long userIdToDelete = 1L;
    
    // Pass the Primary Index pointer to the ObjectRemove function
    t.ObjectRemove("tblUsers", 1.ToIndex(userIdToDelete));
    
    t.Commit();
}
```

## 10. Working with Nested Tables (Fractal Tables)

DBreeze allows storing entire tables *inside the values* of other tables. This creates a multi-dimensional "fractal" structure. Every nested table requires a 64-byte root pointer stored inside the parent row's value array. 

**🚨 ARCHITECTURAL WARNING:** The official documentation does highlight potential memory management issues and increased complexity when using a large number of nested tables within a single transaction. **Always prefer using Composite Keys with byte prefixes (e.g., `1.ToIndex(...)`) in a single master table over Nested Tables when designing new schemas.** However, if you must interact with legacy data or specifically require this feature, follow the rules below.

### Core Mechanics & Rules
*   **Table Index:** Because a row's value can hold multiple nested tables, you must specify an `index` (0, 1, 2, etc.). 
    *   `Index 0` occupies bytes 0-63 of the value.
    *   `Index 1` occupies bytes 64-127 of the value, and so on.
*   **InsertTable vs SelectTable:** 
    *   Use `InsertTable` when you intend to modify the nested table (it creates the table if it doesn't exist). Requires `tran.SynchronizeTables("MasterTable")`.
    *   Use `SelectTable` for read-only access (returns an empty shell without creating physical files if the table doesn't exist).
*   **Commits:** Nested tables do not have their own commit. Calling `tran.Commit()` commits the master table and all its nested tables.

### 1. Creating and Inserting into Nested Tables
Use `InsertTable<TKeyResolver>` from the master transaction. The generic type represents the Key type of the master table.

```csharp
using (var tran = engine.GetTransaction())
{
    // Must lock the master table when modifying its nested tables
    tran.SynchronizeTables("MasterData");

    // Master Table: "MasterData", Master Key: 42, Nested Table Index: 0
    var nestedTbl = tran.InsertTable<int>("MasterData", 42, 0);

    // Now interact with the nested table just like a normal transaction
    nestedTbl.Insert<int, string>(1, "Sub-item A");
    nestedTbl.Insert<int, string>(2, "Sub-item B");

    // Commits both master and nested changes
    tran.Commit();
}
```

### 2. Reading from Nested Tables
Use `SelectTable` to avoid accidentally instantiating empty nested tables on disk during read operations.

```csharp
using (var tran = engine.GetTransaction())
{
    // Master Table: "MasterData", Master Key: 42, Nested Table Index: 0
    var nestedTbl = tran.SelectTable<int>("MasterData", 42, 0);

    var row = nestedTbl.Select<int, string>(1);
    if (row.Exists)
    {
        Console.WriteLine(row.Value); // Outputs: "Sub-item A"
    }
}
```

### 3. Iterating Master Rows and Extracting Nested Tables
When iterating over a master table, you can extract the nested table directly from the `Row` object using `row.GetTable(uint index)`.

```csharp
using (var tran = engine.GetTransaction())
{
    // Iterate master table (values are byte[] because they contain the 64-byte nested table roots)
    foreach (var masterRow in tran.SelectForward<int, byte[]>("MasterData"))
    {
        Console.WriteLine($"Master Key: {masterRow.Key}");

        // Extract nested table at index 0 from the current row
        // WRAPPING IN 'using' IS CRITICAL FOR MEMORY MANAGEMENT (See section 4)
        using (var nestedTbl = masterRow.GetTable(0)) 
        {
            foreach (var nestedRow in nestedTbl.SelectForward<int, string>())
            {
                Console.WriteLine($"  Nested Key: {nestedRow.Key}, Value: {nestedRow.Value}");
            }
        }
    }
}
```

### 4. Memory Management (CRITICAL)
Opening thousands of nested tables within a single transaction **will cause a massive memory leak** (e.g., memory growing to hundreds of MBs) because the engine holds them open until `tran.Commit()` or `tran.Dispose()` is called.

**Rule:** If you are looping through many rows and accessing nested tables, you MUST explicitly close them using `nestedTable.CloseTable()` or by wrapping the nested table in a `using` block (which calls `Dispose() -> CloseTable()` automatically).

**BAD (Causes Memory Bloat):**
```csharp
for (int i = 0; i < 100000; i++) {
    // Leaves 100,000 nested tables open in RAM!
    var tbl = tran.SelectTable<int>("MasterData", i, 0); 
    var row = tbl.Select<int, int>(1);
}
```

**GOOD (Memory Safe):**
```csharp
for (int i = 0; i < 100000; i++) {
    using (var tbl = tran.SelectTable<int>("MasterData", i, 0)) 
    {
        var row = tbl.Select<int, int>(1);
        // Do work...
    } // tbl is disposed and memory is released here
}
```

## 11. Working with the Text Search Layer

DBreeze has an integrated, highly optimized Text Search Engine based on a Word Aligned Bitmap Index (WABI). It maps words to a bitmap of internal document IDs. It is used not only for full-text search but also as a **high-speed multi-parameter search engine** (for tags, categories, geohashes, etc.) without needing complex relational tables.

### Core Concepts
*   **External ID:** Documents are identified by a `byte[]` ID provided by the user (usually a primary key converted via `.To_8_bytes_array_BigEndian()`).
*   **"Contains" vs "Full-Match":**
    *   **Contains:** Stored as multiple substrings (e.g., "around" is stored as "around", "round", "ound"). Searchable by partial words (default min 3 chars).
    *   **Full-Match:** Stored exactly as provided. Found *only* by exact match. Highly recommended for tags (e.g., `#CATEGORY_A`, `#STATUS_ACTIVE`) to save space and prevent dirty results.
*   **Deferred Indexing (`deferredIndexing`):** If `true`, the heavy lifting of building the WABI index happens on a background thread, making the `tran.Commit()` extremely fast. Recommended for large texts.

### 1. Inserting and Updating Text Documents
Use `tran.TextInsert`. If the same external ID is inserted again, DBreeze performs a **smart update**, automatically removing obsolete words and adding new ones.

```csharp
using DBreeze.Utils;

using (var tran = engine.GetTransaction())
{
    byte[] docId = ((long)100).To_8_bytes_array_BigEndian();
    
    string containsText = "The quick brown fox jumps over the lazy dog";
    string fullMatchTags = "#CATEGORY_NEWS #AUTHOR_JOHN #YEAR_2023";

    // TextInsert(tableName, documentId, containsWords, fullMatchWords, deferredIndexing)
    tran.TextInsert("ArticlesText", docId, containsText, fullMatchTags, deferredIndexing: true);
    
    // Other available operations:
    // tran.TextAppend(...) -> adds words to an existing document
    // tran.TextRemove(...) -> removes specific full-match words
    // tran.TextRemoveAll(...) -> completely deletes the document from the index
    
    tran.Commit();
}
```

### 2. Querying with Logical Blocks
Search logic is built using a Search Manager (`tran.TextSearch`) and chained Logical Blocks (`BlockAnd`, `BlockOr`, `Exclude`).

```csharp
using (var tran = engine.GetTransaction())
{
    var tsm = tran.TextSearch("ArticlesText");

    // Search logic: 
    // MUST contain "fox" AND "dog" (contains logic)
    // AND MUST contain EITHER "brown" OR "black" (contains logic)
    // AND MUST have tag "#YEAR_2023" (full-match logic)
    // EXCLUDING documents with tag "#CATEGORY_SPORTS" (full-match)
    
    var query = tsm.BlockAnd("fox dog", "")         // Param 1: Contains, Param 2: Full-Match
                   .And(tsm.BlockOr("brown black", ""))
                   .And("", "#YEAR_2023")
                   .Exclude("", "#CATEGORY_SPORTS");

    // Execute query and retrieve original document IDs
    foreach (byte[] docIdBytes in query.GetDocumentIDs())
    {
        long docId = docIdBytes.To_Int64_BigEndian();
        Console.WriteLine($"Found matching Document ID: {docId}");
    }
}
```

### 3. Handling Dynamic Queries (Ignore Empty Parameters)
When building search screens, users might leave some fields blank. Instead of writing complex `if/else` logic to conditionally build the query chain, use `ignoreOnEmptyParameters: true`. If the supplied strings are empty, the block is safely ignored rather than failing the search.

```csharp
string userSearchInput = "fox";
string userTagFilter = ""; // User didn't select a tag

using (var tran = engine.GetTransaction())
{
    var block = tran.TextSearch("ArticlesText")
        .BlockAnd(userSearchInput, ignoreOnEmptyParameters: true)
        .And("", userTagFilter, false, ignoreOnEmptyParameters: true); 

    var results = block.GetDocumentIDs().ToList();
}
```

### 4. Mixing Range Queries with Text Search
You cannot query native `DateTime` ranges directly inside the text index. However, if your **External IDs grow monotonically** (e.g., auto-incrementing IDs or ticks), you can limit the text search to a specific ID range.

**CRITICAL RULE for `Descending = true`:** When searching descending (newest first), `ExternalDocumentIdStart` must be the MAXIMUM ID (the upper bound) and `ExternalDocumentIdStop` must be the MINIMUM ID (the lower bound).

```csharp
using (var tran = engine.GetTransaction())
{
    var tsm = tran.TextSearch("ArticlesText");
    
    // Limit search to Document IDs between 500 and 1000
    // Because we want Descending order, Start is the HIGHER number
    tsm.ExternalDocumentIdStart = ((long)1000).To_8_bytes_array_BigEndian();
    tsm.ExternalDocumentIdStop = ((long)500).To_8_bytes_array_BigEndian();
    tsm.Descending = true; 

    var query = tsm.BlockAnd("fox", "");

    foreach (var docIdBytes in query.GetDocumentIDs())
    {
        // Will only return matching documents where ID is between 500 and 1000, 
        // ordered from 1000 down to 500.
        Console.WriteLine(docIdBytes.To_Int64_BigEndian());
    }
}
```

### 5. Multi-Parameter Object Searching (The Tagging Pattern)
Instead of building complex relational tables for objects with many properties (Language, City, Status, Skills), serialize them as `#TAGS` into the Full-Match parameter of the Text Search engine. 

*Example Insert:* `"#GENDER_MAN #CITY_HAMBURG #LANG_EN #PROF_IT"`
*Example Query:* `tsm.BlockAnd("", "#GENDER_MAN #CITY_HAMBURG").And(tsm.BlockOr("", "#LANG_EN #LANG_DE"))`

## 12. Working with the Vectors Layer (Embeddings & Similarity Search)

DBreeze includes a native Embedding Vector Database based on the highly efficient HNSW (Hierarchical Navigable Small World) algorithm. It is designed for semantic search, RAG (Retrieval-Augmented Generation) applications, and clustering. It operates entirely on-disk with intelligent memory caching.

### Core Mechanics & Rules
*   **Data Types:** Supports `float[]` and `double[]`. **Rule:** Use `float[]` (e.g., OpenAI, Mistral, LLaMA embeddings) as it provides perfectly acceptable precision, computes faster, and uses half the disk space.
*   **Dimensionality:** A single DBreeze vector table must contain vectors of the **exact same dimensionality** (e.g., all 1536 dimensions for OpenAI `text-embedding-ada-002`). DBreeze does not explicitly validate this, so the developer must enforce it.
*   **Automatic Normalization:** DBreeze automatically normalizes all inserted vectors and search queries. The returned "distance" is always between `0` and `2`, where **`0` means maximum similarity** (exact match).
*   **Concurrency:** Vector insertions build HNSW graphs in parallel using multiple logical CPUs (defaulting to ~70% of available cores).
*   **Soft Deletes / Updates:** Inserting a vector with an existing `externalId` will automatically mark the old vector as "soft-deleted" and replace it. 

### 1. Configuration (Optional)
Vector operations can take an optional `VectorTableParameters<T>` object to tune the HNSW engine. If passed as `null`, DBreeze uses default optimal settings (BucketSize = 100,000, 70% CPU usage).

```csharp
var vectorConfig = new DBreeze.Transactions.Transaction.VectorTableParameters<float[]> 
{
    BucketSize = 100000, // Number of vectors per HNSW graph bucket
    QuantityOfLogicalProcessorToCompute = Environment.ProcessorCount, // Force 100% CPU
    // GetItem = ... (Can be used if vectors are stored physically outside this DBreeze table)
};
```

### 2. Inserting and Updating Vectors
Insertions take an `IList<(long externalId, float[] vector)>`. The `externalId` connects the vector to your business entity (e.g., the Primary Key of a Document or Object).

```csharp
using (var tran = engine.GetTransaction())
{
    // Must synchronize the table for writing
    tran.SynchronizeTables("KnowledgeBaseVectors");

    var batch = new List<(long, float[])>
    {
        (1L, new float[] { 0.1f, 0.5f, 0.9f /* ... up to N dimensions */ }),
        (2L, new float[] { 0.2f, 0.4f, 0.8f }),
        (3L, new float[] { 0.1f, 0.5f, 0.9f })
    };

    // vectorTableParameters can be null to use default optimized settings
    tran.VectorsInsert("KnowledgeBaseVectors", batch, vectorTableParameters: null);
    
    // Note: If you insert ID 2L again later, the engine soft-deletes the old vector 
    // and points to the new one.
    
    tran.Commit();
}
```

### 3. Searching for Similar Vectors (Nearest Neighbors)
Supply a query vector (generated by the same Neural Network used for insertions) to find the closest matches.

```csharp
using (var tran = engine.GetTransaction())
{
    float[] queryEmbedding = new float[] { 0.15f, 0.45f, 0.85f /* ... */ };

    // quantity: How many closest neighbors to return
    // ignoreDeleted: true (default) ensures soft-deleted/updated vectors are skipped
    var results = tran.VectorsSearchSimilar(
        "KnowledgeBaseVectors", 
        queryEmbedding, 
        quantity: 10, 
        ignoreDeleted: true,
        vectorTableParameters: null
    );

    foreach (var result in results)
    {
        // distance: 0 is exact match, 2 is maximum opposite
        // externalId: The ID you supplied during insertion
        Console.WriteLine($"Entity ID: {result.externalId} | Distance: {result.distance}");
    }
}
```

### 4. Removing and Counting Vectors
You can explicitly soft-delete vectors, or check the table capacities.

```csharp
using (var tran = engine.GetTransaction())
{
    tran.SynchronizeTables("KnowledgeBaseVectors");

    // Soft-delete vectors by their external IDs
    var idsToDelete = new List<long> { 2L, 3L };
    tran.VectorsRemove<float[]>("KnowledgeBaseVectors", idsToDelete);
    
    tran.Commit();
}

using (var tran = engine.GetTransaction())
{
    // Count active (non-deleted) vectors
    long activeCount = tran.VectorsCount<float[]>("KnowledgeBaseVectors");
    
    // Count deleted vectors (useful to trigger manual compactions later)
    long deletedCount = tran.VectorsCount<float[]>("KnowledgeBaseVectors", onlyDeletedCount: true);
    
    Console.WriteLine($"Active: {activeCount}, Deleted: {deletedCount}");
}
```

### 5. Fetching Specific Vectors
If you need to retrieve the actual `float[]` data back out of the database based on the ID:

```csharp
using (var tran = engine.GetTransaction())
{
    var idsToFetch = new List<long> { 1L };
    
    var vectors = tran.VectorsGetByExternalId<float[]>("KnowledgeBaseVectors", idsToFetch, ignoreDeleted: true);
    
    foreach(var v in vectors)
    {
        // v.Item1 is the externalId (long)
        // v.Item2 is the vector data (float[])
        Console.WriteLine($"ID: {v.Item1} has {v.Item2.Length} dimensions.");
    }
}
```
