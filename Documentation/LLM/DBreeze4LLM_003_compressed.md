# DBreeze for LLM Agents (v1.132)

Uses `using DBreeze;` and `using DBreeze.Utils;`.

## 1. Core Concepts

- **Byte[] Storage:** All keys/values stored as `byte[]`. Conversions via `DataTypesConvertor` and `BytesProcessing` preserve lexicographical ordering for trie index.
- **Transactions:** All reads/writes require a `Transaction`. Thread-bound; no nesting.
- **Scheme:** `engine.Scheme` manages metadata, table creation/deletion, physical paths.

## 2. Transaction Lifespan & Locking

```csharp
using (var tran = engine.GetTransaction())
{
    tran.Insert<int, string>("users", 1, "Alice");
    tran.Commit();
}
```

Disposing without `Commit()` triggers rollback.

### SynchronizeTables (deadlock prevention)

Call once before any key modification when touching multiple tables.

```csharp
tran.SynchronizeTables("Articles*", "Users$", "Orders#/Items$");
// or
tran.SynchronizeTables(new List<string> { "Articles*", "Users$", "Orders#/Items$" });
```

| Symbol | Behavior |
|:-------|:---------|
| `*`    | Matches prefixes and descendants (`Articles*` → `Articles`, `Articles/Sub`) |
| `$`    | Matches rest until `/` (`User$` → `User1` but not `User/Audit`) |
| `#`    | Matches prefix ending with `/` for nested tables (`Parent#/Child`) |

### Async/Await (CRITICAL)

Transactions bound to `.NET ManagedThreadId`. **Never `await` inside a transaction block** with subsequent operations.

```csharp
// BAD – thread context switch after await causes exception
using(var tran = engine.GetTransaction()) 
{
    tran.Insert("t1", 1, "A");
    var result = await HttpClient.GetAsync("url"); // BREAKS
    tran.Insert("t1", 2, result);
    tran.Commit();
}

// GOOD – block the thread instead
using(var tran = engine.GetTransaction()) 
{
    tran.Insert("t1", 1, "A");
    var result = Task.Run(() => HttpClient.GetAsync("url")).GetAwaiter().GetResult();
    tran.Insert("t1", 2, result);
    tran.Commit();
}
```

## 3. Transaction API

### API Overview

- **CRUD:** `Insert`, `InsertPart`, `RemoveKey`, `RemoveAllKeys`, `ChangeKey`
- **DataBlocks:** `InsertDataBlock`, `InsertDataBlockWithFixedAddress`
- **Retrieval:** `Select`, `SelectDirect`
- **Aggregations:** `Count`, `Min`, `Max`
- **Iterators:** `SelectForward`, `SelectBackward`, `SelectForwardFromTo`, `SelectForwardStartsWith`, `SelectForwardSkip`
- **Advanced:** `Multi_SelectForwardFromTo`, `InsertDictionary`, `RandomKeySorter`, `Technical_SetTable_OverwriteIsNotAllowed`

### 1. Insert, Select & Aggregations

```csharp
using (var tran = engine.GetTransaction())
{
    tran.Insert<int, string>("users", 100, "Bob");
    
    // Insert ONLY if key doesn't exist
    tran.Insert<int, string>("users", 100, "Alice", out byte[] refPtr, out bool wasUpdated, dontUpdateIfExists: true);
    tran.Commit();
}

using (var tran = engine.GetTransaction())
{
    var row = tran.Select<int, string>("users", 100);
    if (row.Exists) Console.WriteLine(row.Value);

    ulong count = tran.Count("users");
    var min = tran.Min<int, string>("users");
    var max = tran.Max<int, string>("users");
}
```

### 2. Partial Updates (`InsertPart`)

Patch bytes of existing value at zero-based offset without rewriting entire value.

```csharp
using (var tran = engine.GetTransaction())
{
    tran.InsertPart<int, byte[]>("files", 42, new byte[] { 0x01, 0x02, 0x03 }, startIndex: 16, out byte[] ptr, out bool updated);
    tran.Commit();
}
```

### 3. Data Blocks (Blobs > 2GB)

`InsertDataBlock` returns a 16-byte handle. `InsertDataBlockWithFixedAddress<T>` gives a stable reference that never moves after updates.

```csharp
using (var tran = engine.GetTransaction())
{
    byte[] dataBlockPointer = null;
    dataBlockPointer = tran.InsertDataBlock("docs", dataBlockPointer, Encoding.UTF8.GetBytes("large text"));
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

```csharp
using (var tran = engine.GetTransaction())
{
    tran.ChangeKey<int>("events", oldKey: 42, newKey: 100, out byte[] ptr, out bool changed);
    tran.RemoveKey<int>("events", 99, out bool wasRemoved, out byte[] deletedValue);
    
    // withFileRecreation: true truncates immediately, no Commit() needed
    tran.RemoveAllKeys("events", withFileRecreation: false);
    tran.Commit();
}
```

### 5. Iteration

```csharp
using (var tran = engine.GetTransaction())
{
    // Default forward: ascending keys
    foreach (var row in tran.SelectForward<int, string>("events")) { }
    
    // API Signature Reference:
    // IEnumerable<Row<TKey, TValue>> SelectForwardStartFrom<TKey, TValue>(string tableName, TKey key, bool includeStartFromKey, bool AsReadVisibilityScope = false);

    // From-to range. includeStartKey=true, includeStopKey=false
    foreach (var row in tran.SelectForwardFromTo<int, string>("events", 1, true, 3, false)) { }
    
    // StartsWith over byte[] keys or composite sequences
    byte[] prefix = 2.To_4_bytes_array_BigEndian();
    foreach (var row in tran.SelectForwardStartsWith<byte[], string>("events", prefix)) { }
    
    // Skip pagination (Skips first 100 records)
    foreach (var row in tran.SelectForwardSkip<int, string>("events", 100)) { }

    // API Signature Reference:
    // IEnumerable<Row<TKey, TValue>> SelectForwardSkipFrom<TKey, TValue>(string tableName, TKey key, ulong skippingQuantity, bool AsReadVisibilityScope = false);
    
    // Note: If we have in a table keys: "check", "sam", "slash", "what"; our search prefix is "slap", we will get: "slam", "slash"
    // API Signature Reference:
    // IEnumerable<Row<TKey, TValue>> SelectForwardStartsWithClosestToPrefix<TKey, TValue>(string tableName, TKey startWithClosestPrefix, bool AsReadVisibilityScope = false);
    // IEnumerable<Row<TKey, TValue>> SelectBackward<TKey, TValue>(string tableName, bool AsReadVisibilityScope = false);
    
    foreach (var row in tran.SelectBackwardFromTo<int, string>("events", 3, true, 1, false)) { }
    
    // Other Backward Operations (all support AsReadVisibilityScope):
    // - SelectBackwardStartFrom
    // - SelectBackwardSkip
    // - SelectBackwardStartsWith    
    // - SelectBackwardStartsWithClosestToPrefix
    // - SelectBackwardSkipFrom
}
```

*`grabSomeLeadingRecords: X` on `SelectForwardFromTo` fetches records slightly before the start key (useful for overlapping time-series).*

**Modifying during iteration:** Pass `true` as `AsReadVisibilityScope` to read from a snapshot prior to modifications (available for all selection/iteration types via `AsReadVisibilityScope: true`, default is `false`).

```csharp
using (var tran = engine.GetTransaction())
{
    tran.SynchronizeTables("events");
    foreach (var row in tran.SelectForward<int, string>("events", AsReadVisibilityScope: true)) 
    {
        if (row.Value == "EXPIRED")
            tran.RemoveKey<int>("events", row.Key);
    }
    tran.Commit();
}
```

### 6. Multi-Table Selection

Reads same-structured keys from different tables in perfectly sorted order.

```csharp
using (var tran = engine.GetTransaction())
{
    var tables = new HashSet<string> { "EventsHamburg", "EventsBerlin" };
    foreach (var el in tran.Multi_SelectForwardFromTo<int, string>(tables, int.MinValue, true, int.MaxValue, true))
        Console.WriteLine($"Table: {el.TableName} | Key: {el.Key} | Value: {el.Value}");
}
```

### 7. Dictionary & HashSet Helpers

Stores `Dictionary`/`HashSet` in ordinary or nested tables. `withValuesRemove` (last param) deletes missing keys.

```csharp
var dict = new Dictionary<uint, string> { 
    { 10, "Hello, my friends" }, 
    { 11, "Sehr gut!" } 
};
var myHashSet = new HashSet<uint> { 1, 2, 3 };

using (var tran = engine.GetTransaction())
{
    // 1. Insert into a Master Table Row
    // Signature: InsertDictionary<MasterKeyType, DictKeyType, DictValueType>("tableName", masterKey, dictionary, nestedTableIndex, withValuesRemove)
    tran.InsertDictionary<int, uint, string>("t1", 1, dict, 0, true);
    
    // 2. Insert into a Nested Table (Chained)
    tran.InsertTable<int>("t1", 15, 0)
        .InsertDictionary<int, uint, string>(10, dict, 0, true);

    // HashSets
    tran.InsertHashSet<int, uint>("tableName", 1, myHashSet, 0, true);

    tran.Commit();

    // 3. Select from Master table
    Dictionary<uint, string> masterDict = tran.SelectDictionary<int, uint, string>("t1", 1, 0);

    // 4. Select from Nested table
    Dictionary<uint, string> nestedDict = tran.SelectTable<int>("t1", 15, 0)
                                              .SelectDictionary<int, uint, string>(10, 0);

    // HashSets Select
    HashSet<uint> returnedHashSet = tran.SelectHashSet<int, uint>("tableName", 1, 0);
}
```

### 8. High-Performance Batching (CRITICAL)

**Rule:** Random-order inserts or massive batch updates tank HDD/SSD. Use these features.

**A. `RandomKeySorter`** — queues inserts in memory, sorts by key, writes sequentially:

```csharp
using (var tran = engine.GetTransaction())
{
    for (int i = 0; i < 100000; i++)
        tran.RandomKeySorter.Insert<int, string>("t1", GetRandom(), "Val"); // auto-flushes at 10K
    tran.Commit();
}
```

**B. `Technical_SetTable_OverwriteIsNotAllowed`** — writes updates to the end of file sequentially (trades temporary file bloat for speed):

```csharp
using (var tran = engine.GetTransaction())
{
    tran.Technical_SetTable_OverwriteIsNotAllowed("t1");
    for (int i = 0; i < 100000; i++)
        tran.Insert<int, string>("t1", i, "Updated Value");
    tran.Commit();
}
```

<details>
<summary><strong>Example: High-Performance IoT Sensor Logging (RandomKeySorter + DataBlocks)</strong></summary>

**Problem:** Millions of JSON payloads arrive out-of-order (random timestamps). Large variable-length payloads cause fragmentation.

**Solution:** `InsertDataBlockWithFixedAddress` stores JSON, returns 16-byte pointer. `RandomKeySorter` buffers DateTime keys + pointers, writes sequentially.

**Ingestion:**
```csharp
public void LogIoTPayloads(DBreezeEngine engine)
{
    using (var tran = engine.GetTransaction())
    {
        tran.SynchronizeTables("SensorLogs");
        Random rnd = new Random();
        DateTime baseTime = DateTime.UtcNow;

        for (int i = 0; i < 1000000; i++)
        {
            DateTime sensorTime = baseTime.AddSeconds(rnd.Next(-500000, 500000));
            string jsonPayload = $"{{ \"deviceId\": {i}, \"temp\": {rnd.Next(10, 40)}, \"status\": \"OK\" }}";
            byte[] payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);

            // null = create new block; returns 16-byte pointer
            byte[] ptr16 = tran.InsertDataBlockWithFixedAddress<byte[]>("SensorLogs", null, payloadBytes);
            tran.RandomKeySorter.Insert<DateTime, byte[]>("SensorLogs", sensorTime, ptr16);
        }
        tran.Commit();
    }
}
```

**Retrieval:**
```csharp
public void ReadIoTLogs(DBreezeEngine engine, DateTime startTime, DateTime endTime)
{
    using (var tran = engine.GetTransaction())
    {
        foreach (var row in tran.SelectForwardFromTo<DateTime, byte[]>(
            "SensorLogs", startTime, true, endTime, true))
        {
            // 0 = byte index where pointer starts inside row.Value
            byte[] payloadBytes = row.GetDataBlockWithFixedAddress<byte[]>(0);
            if (payloadBytes != null)
                Console.WriteLine($"Time: {row.Key:yyyy-MM-dd HH:mm:ss} | Data: {Encoding.UTF8.GetString(payloadBytes)}");
        }
    }
}
```

**Why optimal:** Index stays small (8-byte keys + 16-byte pointers). `RandomKeySorter` eliminates random seeks. Updating payloads via existing `ptr16` keeps the pointer stable—no tree changes needed.
</details>

### 9. Synchronized Cache (`engine.Resources`)

In-memory dictionary auto-synced to disk.

```csharp
engine.Resources.Insert<string>("AppConfig_Theme", "Dark");
var theme = engine.Resources.Select<string>("AppConfig_Theme");
foreach (var item in engine.Resources.SelectStartsWith<string>("AppConfig_")) { }
engine.Resources.Remove("AppConfig_Theme");
```

## 4. Scheme API

| Method                                    | Purpose                                                   |
|:------------------------------------------|:----------------------------------------------------------|
| `DeleteTable(string)`                     | Drops table + physical files. Requires exclusive control. |
| `IfUserTableExists(string)`               | Check existence without creating.                         |
| `GetUserTableNamesStartingWith(string)`   | List tables by prefix.                                    |
| `RenameTable(string, string)`             | Safe rename after other threads finish.                   |
| `GetTablePathFromTableName(string)`       | Returns physical path or `"MEMORY"`.                      |

```csharp
if (!engine.Scheme.IfUserTableExists("users"))
    engine.Scheme.DeleteTable("old_users");
```

## 5. Value Lazy Loading

```csharp
tran.ValuesLazyLoadingIsOn = true;  // default: row.Value triggers disk read on access
tran.ValuesLazyLoadingIsOn = false; // key+value materialized immediately
```

Set `false` when using `row.Value` outside an iterator or when every row needs its value.

## 6. Byte[] Conversions (`using DBreeze.Utils;`)

**Never use `.NET BitConverter`** — always use BigEndian variants for keys to ensure correct sort order.

### Data Type Byte Lengths

When parsing composite keys, you must know exactly how many bytes each data type consumes to calculate your `.Substring()` offsets correctly.

*Note on Nullables:* Most nullable types prepend a `1-byte` flag (`0` = null, `1` = has value), increasing the total length by 1 byte. 

| Type       | Bytes    | To `byte[]`                        | From `byte[]`                    |
|:-----------|:---------|:-----------------------------------|:---------------------------------|
| `byte`     | 1        | `.To_1_byte_array()`               | `.To_Byte()`                     |
| `byte?`    | 2        | `.To_2_byte_array()`               | `.To_Byte_NULL()`                |
| `sbyte`    | 1        | `.To_1_byte_array()`               | `.To_SByte()`                    |
| `sbyte?`   | 2        | `.To_2_byte_array()`               | `.To_SByte_NULL()`               |
| `bool`     | 1        | `.To_1_byte_array()`               | `.To_Bool()`                     |
| `bool?`    | 1        | `.To_1_byte_array()` *(0, 1, 2)*   | `.To_Bool_NULL()`                |
| `char`     | 2        | `.To_2_byte_array()`               | `.To_Char()`                     |
| `char?`    | 3        | `.To_3_byte_array()`               | `.To_Char_NULL()`                |
| `short`    | 2        | `.To_2_bytes_array_BigEndian()`    | `.To_Int16_BigEndian()`          |
| `short?`   | 3        | `.To_3_bytes_array_BigEndian()`    | `.To_Int16_BigEndian_NULL()`     |
| `ushort`   | 2        | `.To_2_bytes_array_BigEndian()`    | `.To_UInt16_BigEndian()`         |
| `ushort?`  | 3        | `.To_3_bytes_array_BigEndian()`    | `.To_UInt16_BigEndian_NULL()`    |
| `int`      | 4        | `.To_4_bytes_array_BigEndian()`    | `.To_Int32_BigEndian()`          |
| `int?`     | 5        | `.To_5_bytes_array_BigEndian()`    | `.To_Int32_BigEndian_NULL()`     |
| `uint`     | 4        | `.To_4_bytes_array_BigEndian()`    | `.To_UInt32_BigEndian()`         |
| `uint?`    | 5        | `.To_5_bytes_array_BigEndian()`    | `.To_UInt32_BigEndian_NULL()`    |
| `long`     | 8        | `.To_8_bytes_array_BigEndian()`    | `.To_Int64_BigEndian()`          |
| `long?`    | 9        | `.To_9_bytes_array_BigEndian()`    | `.To_Int64_BigEndian_NULL()`     |
| `ulong`    | 8        | `.To_8_bytes_array_BigEndian()`    | `.To_UInt64_BigEndian()`         |
| `ulong?`   | 9        | `.To_9_bytes_array_BigEndian()`    | `.To_UInt64_BigEndian_NULL()`    |
| `float`    | 4        | `.To_4_bytes_array_BigEndian()`    | `.To_Float_BigEndian()`          |
| `float?`   | 5        | `.To_5_bytes_array_BigEndian()`    | `.To_Float_BigEndian_NULL()`     |
| `double`   | 9        | `.To_9_bytes_array_BigEndian()`    | `.To_Double_BigEndian()`         |
| `double?`  | 10       | `.To_10_bytes_array_BigEndian()`   | `.To_Double_BigEndian_NULL()`    |
| `decimal`  | 15       | `.To_15_bytes_array_BigEndian()`   | `.To_Decimal_BigEndian()`        |
| `decimal?` | 16       | `.To_16_bytes_array_BigEndian()`   | `.To_Decimal_BigEndian_NULL()`   |
| `DateTime` | 8        | `.To_8_bytes_array()`              | `.To_DateTime()`                 |
| `DateTime?`| 9        | `.To_9_bytes_array()`              | `.To_DateTime_NULL()`            |
| `Guid`     | 16       | `.ToByteArray()`                   | `new Guid(dt)`                   |
| `string`   | Variable | `new DbUTF8(data).GetBytes()`      | `new DbUTF8(dt).Get`             |
| `DbUTF8`   | Variable | `.GetBytes()`                      | `new DbUTF8(dt)`                 |
| `DbAscii`  | Variable | `.GetBytes()`                      | `new DbAscii(dt)`                |
| `DbUnicode`| Variable | `.GetBytes()`                      | `new DbUnicode(dt)`              |
| `byte[]`   | Variable | no cast                            | no cast                          |

**Extra supported DataTypes:**

- **`DbMJSON` (Json serializer) & `DbXML` (Xml serializer):**
  ```csharp
  tran.Insert<uint, DbMJSON<Article>>("Articles", 1, new Article());
  tran.Insert<uint, DbXML<Article>>("Articles2", 1, new Article());
  
  foreach (var row in tran.SelectForward<uint, DbMJSON<Article>>("Articles"))
  {
      // row.Value returns a DbMJSON<Article> object
      Article a = row.Value.Get;
      
      // Or access its serialized string representation directly
      string aSerialized = row.Value.SerializedObject;
  }
  ```

- **`DbCustomSerializer` acts the same as `DbMJSON` when specified globally:**
  ```csharp
  DBreeze.Utils.CustomSerializator.Serializator = (Func<object, string>)...;
  DBreeze.Utils.CustomSerializator.Deserializator = (Func<string, Type, object>)...;
  ```

- **Raw Byte Custom Serializers (transparent serialization):**
  ```csharp
  DBreeze.Utils.CustomSerializator.ByteArraySerializator = (Func<object, byte[]>)...;
  DBreeze.Utils.CustomSerializator.ByteArrayDeSerializator = (Func<byte[], Type, object>)...;
  
  // Usage becomes completely transparent:
  tran.Insert<uint, Article>("Articles", 1, new Article());
  var row = tran.Select<uint, Article>("Articles", 1);
  if (row.Exists) 
  {
      Article a = row.Value;
  }
  ```

### `.ToIndex()` vs `.ToBytes()`

- **`.ToIndex(params object[])`** — converts calling integer to **1 byte**, then concatenates remaining params. `5.ToIndex(100L)` → 9 bytes (1+8).
- **`.ToBytes(params object[])`** — serializes every param at full type size. `5.ToBytes(100L)` → 12 bytes (4+8).

**Composite key example with `.ToBytes()`:**

```csharp
DateTime timestamp = new DateTime(2023, 1, 1);     // 8 bytes
int userId = 12;                                   // 4 bytes
byte[] compositeKey = timestamp.ToBytes(userId);   // 12 bytes total

tran.Insert<byte[], string>("log_table", compositeKey, "System booted");

// Parsing:
var row = tran.Select<byte[], string>("log_table", compositeKey);
if (row.Exists)
{
    DateTime parsedTimestamp = row.Key.Substring(0, 8).To_DateTime();
    int parsedId = row.Key.Substring(8, 4).To_Int32_BigEndian();
}
```

**Rule:** Always consult the byte length table to calculate `.Substring()` offsets.

### Raw Byte Helpers

- **`ConcatMany`**: `b1.ConcatMany(new byte[]{2,3}, new byte[]{4,5})` — fast multi-array merge.
- **`CopyInsideArrayCanGrow`**: `original.CopyInsideArrayCanGrow(1, new byte[]{5,6,7})` — copies at offset, auto-resizes if needed.
- **`ToBytesString()` / `ToByteArrayFromHex()`**: Fast hex conversion for logging/debugging.

### Fixed-Size String Columns (`To_FixedSizeColumn`)

Reserve fixed byte space for strings (adds 2-byte overhead). Truncates if too long.

```csharp
byte[] col = "Alice".To_FixedSizeColumn(50, isASCII: true); // 52 bytes
string restored = col.From_FixedSizeColumn(isASCII: true);
```

### Utilities

- **`.CloneByExpressionTree()`**: Fast deep clone (skips delegates/events/COM).
- **`Hash.MurMurHash.MixedMurMurHash3_128_Stream(stream)`**: 128-bit streaming hash for deduplication.

## 7. Memory Tables

Empty string in `AlternativeTablesLocations` = in-memory. Table names support pattern symbols.

```csharp
var conf = new DBreezeConfiguration();
conf.AlternativeTablesLocations.Add("mem_*", string.Empty);
engine = new DBreezeEngine(conf);

using (var tran = engine.GetTransaction())
{
    tran.Insert<int, string>("mem_temp", 1, "cached");
    tran.Commit();
}
engine.Scheme.GetTablePathFromTableName("mem_temp"); // Returns "MEMORY"
```

## 8. Composite Key Crafting & Range Queries (`.ToIndex`)

### `.ToIndex()` Pattern

`1.ToIndex(...)` = Primary Index, `2.ToIndex(...)` = Secondary Index 1, `3.ToIndex(...)` = Secondary Index 2, etc.

```csharp
byte[] key1 = 2.ToIndex(new DateTime(2023, 1, 1), 100L);
byte[] key2 = 2.ToIndex(new DateTime(2023, 5, 15), 101L);
byte[] key3 = 2.ToIndex(new DateTime(2023, 5, 15), 102L); // same date, unique by ID

t.Insert<byte[], string>("tblSearch", key1, "Data A");
t.Insert<byte[], string>("tblSearch", key2, "Data B");
t.Insert<byte[], string>("tblSearch", key3, "Data C");
t.Commit();
```

**Never use LINQ `.Where()` to filter keys — causes full table scans.**

### Query Strategies

**Exact range (`SelectForwardFromTo`):**
```csharp
byte[] startKey = 2.ToIndex(new DateTime(2023, 1, 1), long.MinValue);
byte[] endKey = 2.ToIndex(new DateTime(2023, 12, 31), long.MaxValue);
foreach (var row in t.SelectForwardFromTo<byte[], string>("tblSearch", startKey, true, endKey, true)) { }
```

**Open-ended range** — fill trailing parts with `MinValue`/`MaxValue`:
```csharp
byte[] startKey = 2.ToIndex(new DateTime(2023, 5, 15), long.MinValue);
byte[] endKey = 2.ToIndex(DateTime.MaxValue, long.MaxValue);
foreach (var row in t.SelectForwardFromTo<byte[], string>("tblSearch", startKey, true, endKey, true)) { }
```

**Prefix matching (`SelectForwardStartsWith`)** — omit trailing components:
```csharp
byte[] prefixKey = 2.ToIndex(new DateTime(2023, 5, 15)); // no ID → matches all IDs for that date
foreach (var row in t.SelectForwardStartsWith<byte[], string>("tblSearch", prefixKey)) { }
```

**Descending (`SelectBackwardFromTo`)** — **startKey must be HIGHER, endKey LOWER:**
```csharp
byte[] maxKey = 2.ToIndex(DateTime.MaxValue, long.MaxValue);
byte[] minKey = 2.ToIndex(DateTime.MinValue, long.MinValue);
foreach (var row in t.SelectBackwardFromTo<byte[], string>("tblSearch", maxKey, true, minKey, true)) { }

// Backward with prefix:
foreach (var row in t.SelectBackwardStartsWith<byte[], string>("tblSearch", 2.ToIndex(new DateTime(2023, 5, 15)))) { }
```

## 9. Object Layer (Entity Framework Alternative)

Stores object once, auto-maintains up to 255 search indexes per entity in a single table.

### Prerequisites: Global Serializer (configure once)

```csharp
DBreeze.Utils.CustomSerializator.ByteArraySerializator = (object o) => MySerializer.Serialize(o);
DBreeze.Utils.CustomSerializator.ByteArrayDeSerializator = (byte[] bt, Type t) => MySerializer.Deserialize(bt, t);
```

### Concepts

- **`ObjectGetNewIdentity`**: Thread-safe auto-increment IDs (stored at byte `0`).
- **`DBreezeObject<T>`**: Wrapper holding `Entity` and `Indexes`. Set `NewEntity = true` for new inserts (speed optimization).
- **`DBreezeIndex`**: Index 1–255. Exactly one must have `PrimaryIndex = true`. Primary key auto-appended to secondary indexes for uniqueness.

### Insert

```csharp
public class User { public long Id; public string Name; public DateTime Created; }

using (var t = engine.GetTransaction())
{
    var user = new User {
        Id = t.ObjectGetNewIdentity<long>("tblUsers"),
        Name = "Alice", 
        Created = DateTime.UtcNow
    };

    t.ObjectInsert<User>("tblUsers", new DBreeze.Objects.DBreezeObject<User>
    {
        NewEntity = true,
        Entity = user,
        Indexes = new List<DBreeze.Objects.DBreezeIndex>
        {
            new DBreeze.Objects.DBreezeIndex(1, user.Id) { PrimaryIndex = true },
            new DBreeze.Objects.DBreezeIndex(2, user.Created)
        }
    }, false); // last param true ONLY for massive batch inserts
    
    t.Commit();
}
```

### Read (`ObjectGet<T>`)

```csharp
using (var t = engine.GetTransaction())
{
    // By primary key (index 1)
    var row = t.Select<byte[], byte[]>("tblUsers", 1.ToIndex(1L));
    if (row.Exists) { User u = row.ObjectGet<User>().Entity; }

    // By secondary index (index 2) range
    byte[] startKey = 2.ToIndex(DateTime.MinValue, long.MinValue);
    byte[] endKey = 2.ToIndex(DateTime.MaxValue, long.MaxValue);
    foreach (var r in t.SelectForwardFromTo<byte[], byte[]>("tblUsers", startKey, true, endKey, true))
    {
        var dbObj = r.ObjectGet<User>();
    }
}
```

### Update

Fetch, modify, rebuild indexes, save with `NewEntity` omitted/false:

```csharp
using (var t = engine.GetTransaction())
{
    var wrapper = t.Select<byte[], byte[]>("tblUsers", 1.ToIndex(1L)).ObjectGet<User>();
    wrapper.Entity.Name = "Alice Updated";
    wrapper.Entity.Created = new DateTime(2025, 1, 1);
    
    wrapper.Indexes = new List<DBreeze.Objects.DBreezeIndex>
    {
        new DBreeze.Objects.DBreezeIndex(1, wrapper.Entity.Id) { PrimaryIndex = true },
        new DBreeze.Objects.DBreezeIndex(2, wrapper.Entity.Created)
    };
    
    t.ObjectInsert<User>("tblUsers", wrapper, false);
    t.Commit();
}
```

*To remove a secondary index: `new DBreezeIndex(2, null)`.*

### Delete

```csharp
t.ObjectRemove("tblUsers", 1.ToIndex(userIdToDelete)); // auto-cleans all secondary indexes
t.Commit();
```

## 10. Nested Tables (Fractal Tables)

Each nested table requires a 64-byte root in the parent value. Index 0 = bytes 0–63, index 1 = bytes 64–127, etc.

**⚠️ Prefer composite keys with byte prefixes over nested tables for new schemas.** Nested tables have memory management complexity.

- **`InsertTable`**: For writes (creates if needed). Requires `SynchronizeTables`.
- **`SelectTable`**: Read-only (no physical creation).
- `tran.Commit()` commits master and all nested tables.

```csharp
// Writing
using (var tran = engine.GetTransaction())
{
    tran.SynchronizeTables("MasterData");
    var nestedTbl = tran.InsertTable<int>("MasterData", 42, 0);
    nestedTbl.Insert<int, string>(1, "Sub-item A");
    nestedTbl.Insert<int, string>(2, "Sub-item B");
    tran.Commit();
}

// Reading
var nestedTbl = tran.SelectTable<int>("MasterData", 42, 0);
var row = nestedTbl.Select<int, string>(1);

// Iterating with GetTable from Row
foreach (var masterRow in tran.SelectForward<int, byte[]>("MasterData"))
{
    using (var nestedTable = masterRow.GetTable(0))
    {
        foreach (var nestedRow in nestedTable.SelectForward<int, string>()) { }
    }
}
```

### Memory Management (CRITICAL)

Opening many nested tables leaks memory until `Commit()`/`Dispose()`. **Must** close via `using` or `.CloseTable()`.

```csharp
// BAD – 100K nested tables open in RAM
for (int i = 0; i < 100000; i++) 
{
    var tbl = tran.SelectTable<int>("MasterData", i, 0);
    var row = tbl.Select<int, int>(1);
}

// GOOD
for (int i = 0; i < 100000; i++) 
{
    using (var tbl = tran.SelectTable<int>("MasterData", i, 0)) 
    {
        var row = tbl.Select<int, int>(1);
    }
}
```

## 11. Text Search Layer

Integrated Word Aligned Bitmap Index (WABI). Maps words to a bitmap of document IDs. Usable for full-text search and multi-parameter tag search.

### Concepts

- **External ID:** `byte[]` (usually primary key via `.To_8_bytes_array_BigEndian()`).
- **Contains:** Stored as substrings ("around"→"around","round","ound"…). Partial-word searchable (min 3 chars).
- **Full-Match:** Exact match only. Use for tags (`#CATEGORY_A`). Saves space, prevents dirty results.
- **`deferredIndexing`:** `true` = WABI built on background thread, fast `Commit()`.

### Insert

Re-inserting the same external ID performs a smart update (removes obsolete words, adds new).

```csharp
byte[] docId = 100L.To_8_bytes_array_BigEndian();
tran.TextInsert("ArticlesText", docId,
    "The quick brown fox jumps over the lazy dog",  // containsWords
    "#CATEGORY_NEWS #AUTHOR_JOHN #YEAR_2023",       // fullMatchWords
    deferredIndexing: true);
tran.Commit();

// Also: tran.TextAppend(...), tran.TextRemove(...), tran.TextRemoveAll(...)
```

### Query with Logical Blocks

```csharp
var tsm = tran.TextSearch("ArticlesText");

// MUST contain "fox" AND "dog", AND ("brown" OR "black"), AND tag #YEAR_2023, EXCLUDING #CATEGORY_SPORTS
var query = tsm.BlockAnd("fox dog", "")           // (containsWords, fullMatchWords)
               .And(tsm.BlockOr("brown black", ""))
               .And("", "#YEAR_2023")
               .Exclude("", "#CATEGORY_SPORTS");

foreach (byte[] docIdBytes in query.GetDocumentIDs())
    Console.WriteLine(docIdBytes.To_Int64_BigEndian());
```

### Dynamic Queries (empty params)

`ignoreOnEmptyParameters: true` safely skips empty blocks instead of failing.

```csharp
var block = tran.TextSearch("ArticlesText")
    .BlockAnd(userSearchInput, ignoreOnEmptyParameters: true)
    .And("", userTagFilter, false, ignoreOnEmptyParameters: true);
```

### Range-Limited Text Search

Limit by external ID range. **Descending:** `Start` = MAX, `Stop` = MIN.

```csharp
var tsm = tran.TextSearch("ArticlesText");
tsm.ExternalDocumentIdStart = 1000L.To_8_bytes_array_BigEndian();
tsm.ExternalDocumentIdStop = 500L.To_8_bytes_array_BigEndian();
tsm.Descending = true;

var query = tsm.BlockAnd("fox", "");
foreach (var docIdBytes in query.GetDocumentIDs()) { }
```

### Multi-Parameter Tagging Pattern

Serialize properties as full-match tags:
- Insert: `"#GENDER_MAN #CITY_HAMBURG #LANG_EN #PROF_IT"`
- Query: `tsm.BlockAnd("", "#GENDER_MAN #CITY_HAMBURG").And(tsm.BlockOr("", "#LANG_EN #LANG_DE"))`

## 12. Vectors Layer (HNSW Embeddings & Similarity Search)

Native HNSW-based vector database for semantic search / RAG.

### Rules

- **Use `float[]`** over `double[]` (sufficient precision, faster, half disk space).
- All vectors in a table must have **identical dimensionality** (not validated by DBreeze).
- Auto-normalizes vectors. Distance: `0` = exact match, `2` = max opposite.
- Parallel graph building (~70% cores default).
- Re-inserting same `externalId` soft-deletes old vector, replaces with new.

### Configuration (optional)

```csharp
var vectorConfig = new DBreeze.Transactions.Transaction.VectorTableParameters<float[]> {
    BucketSize = 100000,
    QuantityOfLogicalProcessorToCompute = Environment.ProcessorCount
};
// Pass null for defaults
```

### Insert/Update

```csharp
using (var tran = engine.GetTransaction())
{
    tran.SynchronizeTables("KnowledgeBaseVectors");
    var batch = new List<(long, float[])> {
        (1L, new float[] { 0.1f, 0.5f, 0.9f }),
        (2L, new float[] { 0.2f, 0.4f, 0.8f }),
        (3L, new float[] { 0.1f, 0.5f, 0.9f })
    };
    tran.VectorsInsert("KnowledgeBaseVectors", batch, vectorTableParameters: null);
    tran.Commit();
}
```

### Search (Nearest Neighbors)

```csharp
using (var tran = engine.GetTransaction())
{
    float[] queryEmbedding = new float[] { 0.15f, 0.45f, 0.85f };
    var results = tran.VectorsSearchSimilar("KnowledgeBaseVectors", queryEmbedding,
        quantity: 10, ignoreDeleted: true, vectorTableParameters: null);
        
    foreach (var result in results)
        Console.WriteLine($"ID: {result.externalId} | Distance: {result.distance}");
}
```

### Remove & Count

```csharp
using (var tran = engine.GetTransaction())
{
    tran.SynchronizeTables("KnowledgeBaseVectors");
    tran.VectorsRemove<float[]>("KnowledgeBaseVectors", new List<long> { 2L, 3L });
    tran.Commit();

    long active = tran.VectorsCount<float[]>("KnowledgeBaseVectors");
    long deleted = tran.VectorsCount<float[]>("KnowledgeBaseVectors", onlyDeletedCount: true);
}
```

### Fetch Vectors by ID

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