# DBreeze Database: LLM Developer Context & Guide

**Role:** You are an expert C# developer working with DBreeze (v1.132+), an ACID-compliant, embedded Key-Value, multi-paradigm database. 
**Goal:** Use this context to write highly optimized, thread-safe, and correct DBreeze implementation code.

## 1. Core Architecture & Transactions
*   **Storage:** DBreeze natively stores Keys and Values as `byte[]`. Generic wrappers convert standard types automatically.
*   **Transactions:** ALL operations must occur inside a transaction. 
*   **Thread Safety:** One transaction = ONE managed thread. Nested transactions are NOT allowed.
*   **Commits:** `tran.Commit()` saves changes. Disposing the transaction without committing automatically triggers a Rollback.

```csharp
using (var tran = engine.GetTransaction()) {
    tran.Insert<int, string>("TableName", 1, "Value");
    tran.Commit();
}
```

## 2. Table Synchronization & Locking Patterns (CRITICAL)
By default, parallel threads can *read* from tables simultaneously. However, if a thread writes to a table, it locks it for writing. To prevent **deadlocks** when multiple threads read/write to the same tables, you must use `tran.SynchronizeTables()`.

*   **Rule:** `SynchronizeTables` must be called *only once* per transaction, *before* any modification commands.
*   **Pattern System:** DBreeze allows locking multiple tables using wildcards instead of exact names.
    *   `*` : Matches any symbols (e.g., `Articles*` locks Articles1, Articles/Sub, etc.)
    *   `$` : Matches any symbols *except* a slash `/` (e.g., `Items$` locks Items1, but not Items/Pictures).
    *   `#` : Matches symbols (except slash) followed by a slash and another symbol (e.g., `Items#/Pic` locks Items1/Pic).

**[PLACEHOLDER: Agent to provide C# examples of avoiding deadlocks using `tran.SynchronizeTables` with exact names and complex pattern matchers (`*`, `$`, `#`).]**

## 3. Byte[] Conversions & DBreeze.Utils (CRITICAL)
Because DBreeze sorts keys lexicographically as `byte[]`, standard .NET `BitConverter` is **wrong** for DBreeze. You MUST use `DBreeze.Utils` extensions to guarantee sortable byte arrays.

*   **Namespaces:** `using DBreeze.Utils;`
*   **Key Conversions:** Use `.To_4_bytes_array_BigEndian()` (for `int`/`uint`/`float`) and `.To_8_bytes_array_BigEndian()` (for `long`/`ulong`/`double`).
*   **DateTime:** Use `.To_8_bytes_array()` and `.To_DateTime()`.
*   **Composite Keys:** Combine keys using byte concatenation (e.g., `.Concat()` or `.ConcatMany()`).
*   **Helpers:** `.ToIndex(params)` and `.ToBytes(params)` cast values perfectly for DBreeze composite indexing.

**[PLACEHOLDER: Agent to provide C# examples showing how to build a composite key (e.g., DateTime + int + long) using DBreeze.Utils extensions, insert it, and parse it back out of a byte[].]**

## 4. Querying & Iteration (NO LINQ FOR KEYS)
DBreeze automatically sorts data ascending by Key. 
*   **Rule:** NEVER use LINQ `.Where(r => r.Key > X)` on iterations. It causes full table scans.
*   **Correct Methods:** 
    *   `SelectForwardStartFrom<TKey, TValue>(table, key, match)`
    *   `SelectBackwardStartFrom<TKey, TValue>(...)`
    *   `SelectForwardFromTo<TKey, TValue>(table, startKey, matchStart, endKey, matchEnd)`
    *   `SelectForwardStartsWith<TKey, TValue>(table, prefixAsByteArray)`

## 5. Lazy Loading Values (CRITICAL)
To optimize disk I/O, DBreeze uses Lazy Loading for Values. 
*   `tran.ValuesLazyLoadingIsOn` is `true` by default. 
*   When `true`, `row.Value` executes a disk hit *at the exact moment* `.Value` is accessed. 
*   If you only need to iterate over Keys, keep it `true`.
*   If you need both Keys and Values immediately (or if passing rows outside the active iterator scope), set `tran.ValuesLazyLoadingIsOn = false` before querying to fetch everything in one disk hit.

**[PLACEHOLDER: Agent to provide C# example demonstrating the difference in execution and syntax when toggling `tran.ValuesLazyLoadingIsOn` to true vs false.]**

## 6. Storing Objects & Entities
You can serialize objects directly as values. DBreeze supports custom serializers (like Protobuf or NetJSON/Biser).
*   Initialize globally once: `DBreeze.Utils.CustomSerializator.ByteArraySerializator = ...`
*   **Entity Framework-like approach:** Use `t.ObjectInsert<T>` and `DBreeze.Objects.DBreezeObject<T>`. This allows storing one entity but defining multiple secondary indexes (up to 255) automatically handled by DBreeze.

**[PLACEHOLDER: Agent to provide a complete C# CRUD example using `tran.ObjectInsert`, `tran.ObjectGetNewIdentity`, and `DBreezeIndex` for an object with a Primary Key and at least one Secondary Index.]**

## 7. Advanced Subsystems Summary
*(Agent: Be aware these exist if requested by user)*
*   **Text Search:** Integrated engine (`tran.TextInsert`, `tran.TextSearch`). Supports logical blocks (`BlockAnd`, `BlockOr`, `Exclude`) and WABI (Word Aligned Bitmap Index). Can mix "contains" and "full-match" logics.
*   **Vector Database (HNSW):** (`tran.VectorsInsert`, `tran.VectorsSearchSimilar`). Supports `float[]` and `double[]` embeddings. Automatically normalizes vectors. Includes Soft Delete (`tran.VectorsRemove`).
*   **DataBlocks:** For massive strings/binaries exceeding normal limits, use `InsertDataBlockWithFixedAddress`. It saves the huge data to disk and returns a 16-byte pointer to store in your standard Table value.