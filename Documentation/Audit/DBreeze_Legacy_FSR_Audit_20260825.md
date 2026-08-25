# Hardened baseline FSR audit — 2026-08-25

## Verdict

**PASS для распространения текущего lower-platform design без изменений FSR.**

- `DBreeze/Storage/FSR.cs` уже является hardened baseline implementation: один per-table `lock`, один cursor-based `FileStream` с 8 KiB buffer, без `RandomAccess`, `ReaderWriterLockSlim`, page cache и mutation versions.
- `NETPortable/Storage/FSR.cs` сохраняет тот же concurrency contract через `IFileStream`.
- `DBreeze.Net8/Storage/FSR.cs` остаётся отдельной modern implementation.
- Exact baseline hot path переносить не требуется: изолированные current FSR reads не регрессируют и заметно уменьшают allocations.
- В рамках аудита ни один из трёх `FSR.cs` не изменён.

Current source: `0033f5bc7f8ca0279aaf69fc61907d75cd2d545e`; baseline: `a83424e2fa742ec05a8e4a359562d3f3a5e008c8`.

## Effective source matrix

| Platform | Effective FSR | Result |
|---|---|---|
| .NET 6 | `DBreeze/Storage/FSR.cs` | PASS |
| .NET Core App 3.1 | `DBreeze/Storage/FSR.cs` | PASS |
| .NET Standard 2.1 consumer | `DBreeze/Storage/FSR.cs` | PASS |
| .NET Framework 3.5–4.7.2 | `DBreeze/Storage/FSR.cs` | BUILD PASS |
| Portable/Profile111 | `NETPortable/Storage/FSR.cs` | PASS |
| .NET 8 | `DBreeze.Net8/Storage/FSR.cs` | separate modern path retained |

Storage contracts passed on Net6, NetStandard consumer, NetCoreApp 3.1 (runtime roll-forward), .NET Framework 4.7.2 and Portable. Each run covered commit/rollback, overlapping auto-flushed updates, bounded/truncated recovery, restore/recreate/reopen, backup and concurrent 2/8 readers + writer.

Build matrix passed for net35, net40, net452, net461, net462, net47, net472, netcoreapp1.0/1.1/2.0/3.1, netstandard1.6/2.0/2.1, Portable and net6. Local reference packs were present; no target is reported as a synthetic PASS.

## File compatibility

Three independent workers were built against exact `a83424e` Net6, current hardened Net6 and current modern Net8. The following chains passed:

- old create → hardened read-only verify → Net8 extend → old verify;
- hardened create → old read-only verify → old extend → Net8 verify;
- Net8 create → old read-only verify → hardened extend → Net8 verify;
- Net6-created extended file was also verified by .NET Framework 4.7.2 and Portable.

Every read-only verification preserved file length and SHA-256. Counts/checksums and database sizes matched after extensions.

## Isolated FSR results

Five paired exact-EOF missing rounds and three paired existing rounds, 1 000 000 operations each:

| Runtime / scenario | a83424e median | current median | Delta | Allocation |
|---|---:|---:|---:|---:|
| Net6 `StoragePointExisting` | 77.34 ms | 70.44 ms | **−8.9%** | 168 → 88 B/op |
| Net6 `StoragePointMissing` | 1726.38 ms | 27.47 ms | **−98.4%** | 104 → 24 B/op |
| net472 `StoragePointExisting` | 2756.62 ms | 2695.28 ms | **−2.2%** | unavailable |
| net472 `StoragePointMissing` | 2366.17 ms | 32.18 ms | **−98.6%** | unavailable |

Baseline throws for offsets strictly beyond EOF because it attempts a negative-sized result; current correctly returns an empty array. For comparable timing the missing microbenchmark therefore uses exact EOF. Beyond-EOF remains a correctness contract, not a baseline speed datum.

## Representative engine scenarios

The 10 000-record three-pair suite showed no confirmed Net6 gate after two confirmation pairs for noisy candidates. Important medians: forward scan −20%, prefix −37%, skip −27%, update −17%, rollback −11%; confirmed allocation regressions: none.

On net472, forward/prefix/skip/update/rollback/restore improved. A 100 000-operation engine-level `PointMissing` follow-up was +7.5% (4/5 pairs above 5%). The isolated `StorageLayer` missing path improved by 98.6%, so this delta is outside FSR and must not be “fixed” by restoring unsafe baseline storage code. It is recorded for a separate LianaTrie/runtime audit.

## Preserved hardening

The passing contracts exercise the current exact-read loops, bounded journal parser, checked offsets, overlapping rollback ranges, safe missing-source restore, lifecycle cleanup, atomic transactional flag reset, writer buffer accounting and direct sequential-buffer backup writes. Replacing either lower FSR with the a83424e file would lose these guarantees and is rejected by the audit.

## Additional observation

The common suite intentionally probed current Net8 too. An out-of-scope pattern with hundreds of still-buffered one-byte writes followed by one wide overlapping buffered write exposes last-write ordering differences in modern FSR writer view. Existing lower-platform contracts pass. This audit does not change Net8 FSR; the case should be handled as a separate targeted issue.

Reproduction CLI and target mapping are documented in `DBreeze.Storage.Contracts/README.md`.
