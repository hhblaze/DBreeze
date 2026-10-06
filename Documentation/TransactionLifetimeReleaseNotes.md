# Transaction lifetime safeguards — unreleased

This source change adds two runtime checks across DBreeze targets. It does not change the database format or existing public method/constructor signatures. Deployment assemblies and package versions are not updated by this change.

## Nested transactions

Only one transaction can be active on a given engine and managed thread. A second call to either `GetTransaction()` overload now throws `DBreezeException` immediately:

```text
NESTED TRANSACTIONS ARE NOT ALLOWED
```

The first transaction retains its pending writes, registration and locks. The check runs before a locked transaction is constructed, avoiding waiting on the first transaction's own lock. Different engines on one thread and separate transactions on different threads remain supported.

Previously, opening another transaction on the same engine/thread removed the first transaction's registration. Applications relying on that cleanup must now dispose the previous transaction explicitly. `Commit()` and `Rollback()` keep a transaction open.

```csharp
using (var transaction = engine.GetTransaction())
{
    transaction.Insert("items", 1, "value");
    transaction.Commit();
}
// The first transaction has been disposed; the next one may now be opened.
using (var transaction = engine.GetTransaction())
{
    string value = transaction.Select<int, string>("items", 1).Value;
}
```

Helpers called inside a transaction should accept and reuse that transaction instead of opening another one.

## Rows after transaction completion

`ToList()` and `ToArray()` materialize the sequence, not lazy row values. New storage reads through a row after its originating transaction completes now throw `DBreezeException`:

```text
ROW READ REQUIRES AN ACTIVE TRANSACTION. Load the required data before disposing the transaction.
```

The same check applies after coordinator termination, including deadlock, and engine shutdown. A new transaction cannot revive rows from a completed transaction. Fully loaded `Value` results and saved `Key`, `Exists` and `TableName` remain accessible from memory. Partial value reads work after disposal if the full value has already been loaded. Physical pointers, nested tables, datablocks and object storage still require the originating transaction; loading a row's value does not materialize the data it references.

Copy independent export data inside the transaction:

```csharp
List<KeyValuePair<int, string>> snapshot;
using (var reader = engine.GetTransaction())
{
    snapshot = reader.SelectForward<int, string>("items")
        .Select(row => new KeyValuePair<int, string>(row.Key, row.Value))
        .ToList();
}
// snapshot contains keys and values, rather than deferred storage references.
```

Or retain eager rows when only their loaded values are needed:

```csharp
List<Row<int, string>> rows;
using (var reader = engine.GetTransaction())
{
    reader.ValuesLazyLoadingIsOn = false;
    rows = reader.SelectForward<int, string>("items").ToList();
}
string value = rows[0].Value;
```

Values explicitly accessed inside the transaction are also retained. Disposal does not load remaining lazy values. Complete parallel reads before disposing their transaction. The new checks enforce object lifetime and do not introduce a consistent snapshot across subsequent reads or transactions.

`TRANSACTION_NESTED_NOT_ALLOWED` and `ROW_TRANSACTION_IS_NOT_ACTIVE` are appended to `DBreezeException.eDBreezeExceptions`; existing enum values keep their numbers.

## Validation commands

The shared C# 7.3 contract tests are included in the .NET 8 regression host and the cross-target storage host:

```text
DBreeze.Net8.Tests --transaction-lifetime
DBreeze.Storage.Contracts --transaction-lifetime
DBreeze.Storage.Contracts --storage-contracts
DBreeze.Net8.Tests --transaction-lifetime-performance
```

The performance mode can also be built against a baseline DLL using `DBreezeAssemblyReference`. It reports disk/memory point reads and lazy/eager scans with elapsed time, allocations and matching checksums. One lifetime object is created per transaction and also serves as its `Dispose()` lock, replacing the previous separate lock object. Each row retains one additional reference, rather than allocating a new token. The separate `disposed` flag remains necessary: coordinator termination invalidates the token, but a later `Transaction.Dispose()` must still perform its cleanup.

## Validation results (2026-10-06)

The lifetime and full storage contract suites pass on Windows for .NET 8, .NET 6, .NET Standard 2.1 (consumed by .NET 8), .NET Core App 3.1, .NET Framework 4.7.2 and Portable/Profile111. The .NET Core App 3.1 host was run with `dotnet --roll-forward Major` because its original runtime is absent; this verifies that target's library on the installed .NET 5 runtime. It does not replace testing on the original 3.1 runtime or on other operating systems.

Public API comparison against the supplied 1.140 assembly found unchanged existing public signatures and exception enum numbers. The lifetime token has only one boolean field and retains no transaction, engine or table reference.

The full .NET 8 regression suite also passes, including coordinator/deadlock, committed reads, nested-table behavior and reading eager rows after disposal. A run concurrent with the other contract hosts hit the existing timer-sensitive idle-table eviction assertion; a separate rerun passed.

### Performance measurements

These scan measurements precede the `Dispose()` lock consolidation described below.

Release .NET 8 x64 builds were compared with the supplied signed 1.140 DLL. Each scenario reads 20,000 records with 64-byte values for 50 transactions (1,000,000 rows), after five warmup transactions. Three separate process runs per build were measured with normal tiered compilation and another three with `DOTNET_TieredCompilation=0`, alternating build order in the latter group. Every measured checksum was 64,000,000. Timing below is the median, in milliseconds; a positive change means slower.

| Storage | Scenario | 1.140, default JIT (ms) | Safeguards, default JIT (ms) | Default JIT change | Tiering disabled change |
| --- | --- | ---: | ---: | ---: | ---: |
| Memory | Point reads | 564.728 | 562.390 | -0.4% | -5.6% |
| Memory | Lazy scan | 398.766 | 332.816 | -16.5% | +10.9% |
| Memory | Eager scan | 232.559 | 205.148 | -11.8% | +15.3% |
| Disk | Point reads | 426.149 | 432.405 | +1.5% | +0.3% |
| Disk | Lazy scan | 345.549 | 328.835 | -4.8% | +3.4% |
| Disk | Eager scan | 268.586 | 266.411 | -0.8% | -5.3% |

Elapsed times varied substantially between processes and JIT modes. These measurements do not establish a stable timing cost or speedup; they are a local microbenchmark, not an application throughput guarantee. In particular, tiering-disabled memory scans showed slower medians and should be checked under the deployment workload if that mode is used.

Allocation differences for scans were stable: 8,001,600 extra bytes per 1,000,000 rows and 50 transactions. On this x64 runtime that is 8 bytes for each row's lifetime reference plus 32 bytes per transaction (one 24-byte token and one 8-byte transaction reference). No separate lifetime token is allocated for a row. Point-read totals also include variable existing trie/cache allocations.

### Dispose lock consolidation (2026-10-06)

Both transaction implementations now lock on their existing `TransactionReadLifetime` in `Dispose()`, removing the separate `sync_dispose` object and field. The `disposed` flag and cleanup order are preserved. Row checks still read the token's volatile activity flag without acquiring a lock.

Fresh Release .NET 8 DLLs built immediately before and after this change were measured on .NET 8.0.31 x64. Each of three samples measured 10,000 complete empty-transaction `GetTransaction()` / `Dispose()` cycles after 3,000 warmup cycles. Object footprints were measured separately with `RuntimeHelpers.GetUninitializedObject`, excluding owned objects.

| Allocation measurement | Before consolidation | After consolidation | Change |
| --- | ---: | ---: | ---: |
| Complete empty-transaction cycle | 776 bytes | 744 bytes | -32 bytes |
| Transaction instance, excluding owned objects | 144 bytes | 136 bytes | -8 bytes |
| Row<int, string> instance, excluding owned objects | 64 bytes | 64 bytes | 0 bytes |

Every sample produced the same allocation counts. One 24-byte lock object and one 8-byte transaction reference are removed per transaction. The lifetime token itself remains 24 bytes and replaces the equally sized former lock object; each row still retains its lifetime reference. This removes the token's additional object and transaction allocation cost relative to the pre-safeguard implementation on the measured runtime. These allocation results do not measure database throughput.

The lifetime and storage contract suites pass for all six targets after consolidation. The full .NET 8 regression suite also passes, including coordinator/deadlock, committed reads and nested tables. Additional disk/memory checks pass for repeated and concurrent `Dispose()`, cleanup after coordinator termination, release of EXCLUSIVE table sessions and rollback of pending writes. Saved loaded rows still allow disposed transactions and engines to be collected.

A separate .NET 8 check used four managed threads with their own simultaneously active transactions to read the same table. All 384,000 row reads passed across disk/memory storage and ordinary/SHARED transactions. Closing one reader preserved the other readers. SQLite benchmarks were not repeated because this change preserves the read/write algorithms and only removes a transaction allocation and reuses its disposal lock.
