# DBreeze TextSearch: old vs new после WABI/FSR оптимизации

Run: `20260818-WabiFsr`  
Порог: подтверждённая регрессия median не должна превышать `5%`.

## Verdict

- **FORMAT PASS** — old/new сборки создают, читают, расширяют и повторно открывают TextSearch БД друг друга. Row count, logical checksum и относительный file inventory совпадают. Все read-only проверки сохранили исходные length и SHA-256.
- **PERFORMANCE PASS** — ни один из пяти TextSearch workload не деградировал более чем на 5%. Максимальная измеренная регрессия median — `+0.24%`; geometric-mean speedup — `1.131x`.
- **WABI/FSR MICRO PASS** — все WABI workloads и eight-thread 64-byte read не хуже old более чем на 5%.

## Окружение

| Параметр | Old | New |
|---|---|---|
| Repository | `D:\VS\DBreezeRealm_copy\DBreeze` | `D:\VS\DBreezeRealm\DBreeze` |
| Git HEAD | `a83424e2fa742ec05a8e4a359562d3f3a5e008c8` | `3c47ad0a9c7a23663f8b460eee45e166a3dfdfc4` + working tree |
| DBreeze assembly | `1.138.2026.0603` | `1.139.2026.0817` |
| TextSearch perf root | `D:\Temp\DbreezeDbTest_copy\perf-confirm-textsearch-old-20260818-fsr` | `D:\Temp\DbreezeDbTest\perf-confirm-textsearch-new-20260818-fsr` |

- BenchmarkDotNet `0.15.8`, runtime `.NET 8.0.30`, SDK `10.0.111`.
- Windows 11, Intel Core i7-8700, 6 physical / 12 logical cores, AVX2, Workstation GC.
- Benchmark sources синхронизированы source-only; SHA-256 совпадает для всех 14 `.cs`/`.csproj` файлов.
- Historical/10M и несвязанные Engine/LianaTrie/Transaction workloads не запускались.

## Изменения hot path

- `WABI`: отдельный no-range iterator, одноразовые boundary masks, специализация intersection для 1/2 bitmap, fused vector merge первых двух operand и early-empty AND.
- `FSR`: cached physical length вместо `RandomAccess.GetLength` на каждый read; generation-based invalidation после любой физической записи.
- Stable committed reads используют одну 8 KiB страницу на thread. Первый exact read является admission, поэтому изолированный random 64-byte read не превращается в 8 KiB I/O.
- Writer, rollback-backed, transactional transitional view и cross-page read остаются на точном positioned `RandomAccess` path. Asynchronous handle не применялся: локальный probe показал существенную регрессию малых синхронных чтений.

## Disk compatibility

| Проверка | Результат |
|---|---|
| Old создаёт base; new читает base | PASS |
| New создаёт base; old читает base | PASS |
| New расширяет old-created DB; old читает extended | PASS |
| Old расширяет new-created DB; new читает extended | PASS |
| Read-only length/SHA до и после всех verify | Unchanged (strict PASS) |
| Relative file inventory old/new | Equal |

| Состояние | Rows | Checksum | Old bytes | New bytes | Delta |
|---|---:|---:|---:|---:|---:|
| Base | 3,424 | -8,639,804,511,572,967,290 | 222,710 | 222,731 | +21 B |
| Extended | 3,504 | 8,353,551,157,804,963,282 | 372,613 | 372,634 | +21 B |

Физические различия ожидаемы и не влияют на совместимость: `10000006` в new длиннее на 21 byte; после разнонаправленного extend отличается также SHA-256 `10000006.rol`. Logical checksum, inventory и cross-read/cross-update остаются идентичными.

Raw compatibility reports:

- `D:\Temp\DbreezeDbTest\comparison\20260818-WabiFsr\compat\base-cross-compatible\disk-compatibility.md`
- `D:\Temp\DbreezeDbTest\comparison\20260818-WabiFsr\compat\extended-cross-compatible\disk-compatibility.md`
- Strict read-only reports находятся в sibling directories `*-readonly-strict`.

## TextSearch MediumRun confirmation

MediumRun: 2 launches, 10 warmups и 15 measured iterations. Порядок запуска: new, затем old; обе БД свежие. Verdict использует median.

| Method | Old mean | New mean | Old median | New median | Speedup old/new | Median delta | Old allocated | New allocated | Verdict |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| SynchronousIndexing | 870.100 us | 546.600 us | 1,002.600 us | 547.300 us | 1.832x | -45.41% | 657,910 B | 657,910 B | PASS |
| SparseAnd | 663.400 us | 663.400 us | 663.000 us | 659.600 us | 1.005x | -0.51% | 669,768 B | 669,737 B | PASS |
| DenseAnd | 13.192 ms | 13.138 ms | 13.198 ms | 13.094 ms | 1.008x | -0.79% | 24,683,551 B | 24,683,520 B | PASS |
| PrefixOr | 23.650 ms | 23.656 ms | 23.623 ms | 23.669 ms | 0.998x | +0.20% | 48,554,455 B | 48,554,424 B | PASS |
| EncryptedSearch | 24.297 ms | 24.397 ms | 24.307 ms | 24.366 ms | 0.998x | +0.24% | 48,549,437 B | 48,549,417 B | PASS |

Geometric-mean median speedup: **1.131x**. Old `SynchronousIndexing` имел multimodal distribution (`mValue=2.86`); согласованный критерий всё равно использует median, а остальные четыре search workload имеют узкие распределения и отличаются в пределах ±0.8%.

Raw performance reports:

- `D:\Temp\DbreezeDbTest\comparison\20260818-WabiFsr\old-medium`
- `D:\Temp\DbreezeDbTest\comparison\20260818-WabiFsr\new-medium`
- `D:\Temp\DbreezeDbTest\comparison\20260818-WabiFsr\medium-comparison\focused-comparison.md`

## WABI и FSR ShortRun

| Workload | Old median | New median | Speedup | Median delta | Verdict |
|---|---:|---:|---:|---:|---|
| WABI SparseEnumeration | 380.66 us | 378.21 us | 1.006x | -0.64% | PASS |
| WABI DenseEnumeration | 1,950.55 us | 1,939.61 us | 1.006x | -0.56% | PASS |
| WABI MergeAnd2 | 19.94 us | 16.38 us | 1.217x | -17.85% | PASS |
| WABI MergeAnd16 | 104.64 us | 104.37 us | 1.003x | -0.26% | PASS |
| WABI MergeOr2 | 17.11 us | 15.52 us | 1.102x | -9.28% | PASS |
| WABI MergeOr16 | 83.27 us | 83.16 us | 1.001x | -0.14% | PASS |
| FSR Random64ByteRead | 2,550.10 ns | 2,489.83 ns | 1.024x | -2.36% | PASS |
| FSR Local64ByteRead | 44.53 ns | 43.81 ns | 1.016x | -1.62% | PASS |
| FSR EightThread64ByteRead | 81.13 ns | 74.32 ns | 1.092x | -8.39% | PASS |

WABI enumeration сохранил `56 B/op`; FSR 64-byte workloads сохранили `88/88/91 B/op`. Merge allocation равна размеру результата; отличия в несколько bytes между версиями находятся в служебном объектном accounting BDN.

Raw micro reports:

- `D:\Temp\DbreezeDbTest\comparison\20260818-WabiFsr\old-micro`
- `D:\Temp\DbreezeDbTest\comparison\20260818-WabiFsr\old-or-rerun`
- `D:\Temp\DbreezeDbTest\comparison\20260818-WabiFsr\new-micro`

## Regression и build matrix

- `.NET 8` full harness: PASS, включая randomized WABI, page cache lifecycle, transactional commit/rollback, restore/recreate и concurrent commit/read cycles.
- Общий TextSearch harness через `DBreeze.Net5.Tests` / `net6.0`: PASS, 11/11.
- Release build PASS: .NET Framework 4.7.2, netcoreapp3.1, netstandard2.1, net6.0, net8.0 и Portable Profile111 (последний через Visual Studio MSBuild).
- Existing XML/analyzer warnings не связаны с этой работой; ошибок сборки нет.
- `Deployment` и его готовые бинарные артефакты не изменялись этой работой.

## Итог

**FORMAT PASS / PERFORMANCE PASS.** Формат и bidirectional compatibility сохранены. Подтверждённая search-регрессия устранена: search latency теперь соответствует old в пределах измерительного шума, indexing заметно быстрее, а allocations не выросли.

---

## Follow-up: восстановление lexical batching

Run: `20260818-LexicalBatch`  
Порог: подтверждённая регрессия median не должна превышать `5%`.

### Изменение

- В общей и локальной .NET 8 реализациях восстановлен `SortedDictionary<string, byte[]>(StringComparer.Ordinal)`.
- При `Count > 100000` выполняется промежуточный flush; после обработки документов всегда выполняется финальный flush.
- `changes` живёт между flush-батчами, поэтому дедупликация слова и агрегация изменений документов не нарушены.
- В LTrie вставляются plaintext-слова в ordinal lexical order. Для encrypted table шифрование выполняется непосредственно перед `Insert`: fixed-IV AES-CTR сохраняет равенство префиксов, поэтому соседние plaintext-префиксы остаются соседними путями ключей. Сортировка ciphertext не применяется.

### Проверки корректности

- AES prefix invariant проверен на границе AES-блока и на Unicode: `Encrypt(prefix)` является точным префиксом `Encrypt(prefix + suffix)`.
- Для plain/encrypted таблиц insertion order восстановлен через физический `LinkToValue`; внутри batch слова идут по `StringComparer.Ordinal`. Exact/prefix search и reopen — PASS.
- Отдельный disk-test с `100005` уникальными словами в обратном порядке подтвердил ordinal order отдельно внутри промежуточного и финального batch, затем reopen/search — PASS в специализированной .NET 8 и общей net6 реализации.
- Полные `.NET 8` и общие TextSearch regression harness — PASS.
- Release build matrix — PASS: .NET Framework 4.7.2, netcoreapp3.1, netstandard2.1, net6.0, net8.0 и Portable Profile111.
- Benchmark sources синхронизированы source-only; SHA-256 совпадает для всех 15 `.cs`/`.csproj` файлов.

### Disk compatibility

| Проверка | Результат |
|---|---|
| Old создаёт base; new читает base | PASS |
| New создаёт base; old читает base | PASS |
| New расширяет old-created DB; old читает extended | PASS |
| Old расширяет new-created DB; new читает extended | PASS |
| Read-only length/SHA до и после verify | Unchanged (strict PASS) |
| Relative file inventory old/new | Equal |

| Состояние | Rows | Checksum | Old bytes | New bytes |
|---|---:|---:|---:|---:|
| Base | 3,424 | -8,639,804,511,572,967,290 | 222,710 | 222,710 |
| Extended | 3,504 | 8,353,551,157,804,963,282 | 372,613 | 372,613 |

В extended DB длины и inventory совпадают. SHA-256 различается у `10000006` и `10000006.rol`, что допустимо для compatible policy; logical checksum и двунаправленное чтение/расширение совпадают.

### Indexing benchmark

BenchmarkDotNet `0.15.8`, `.NET 8.0.30`, SDK `10.0.111`, Windows 11, Intel Core i7-8700. Все прогоны использовали свежие disk roots. Verdict основан на median.

| Workload | Run | Old mean | New mean | Old median | New median | Speedup old/new | Median delta | Old allocated | New allocated | Verdict |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| SynchronousIndexing (10,000 docs) | MediumRun confirmation | 561.6 us | 552.9 us | 562.5 us | 552.6 us | 1.018x | -1.76% | 642.49 KB | 642.49 KB | PASS |
| PlainHighCardinalityIndexing (16,384 words) | ShortRun | 372.393 ms | 355.323 ms | 373.750 ms | 357.369 ms | 1.046x | -4.38% | 102.60 MB | 102.60 MB | PASS |
| EncryptedHighCardinalityIndexing (16,384 words) | ShortRun | 440.060 ms | 432.548 ms | 435.760 ms | 433.834 ms | 1.004x | -0.44% | 126.98 MB | 126.98 MB | PASS |

ShortRun показал `SynchronousIndexing` хуже old примерно на 6.25%, поэтому по плану был выполнен только его MediumRun в обратном порядке: new, затем old. Подтверждающий результат показал не регрессию, а улучшение median на `1.76%` при неизменных allocations.

Raw reports:

- `D:\Temp\DbreezeDbTest\comparison\20260818-LexicalBatch\compat`
- `D:\Temp\DbreezeDbTest\comparison\20260818-LexicalBatch\old-short`
- `D:\Temp\DbreezeDbTest\comparison\20260818-LexicalBatch\new-short`
- `D:\Temp\DbreezeDbTest\comparison\20260818-LexicalBatch\old-medium`
- `D:\Temp\DbreezeDbTest\comparison\20260818-LexicalBatch\new-medium`
- `D:\Temp\DbreezeDbTest\comparison\20260818-LexicalBatch\medium-comparison`
- `D:\Temp\DbreezeDbTest\comparison\20260818-LexicalBatch\lexical-short-comparison`

### Follow-up verdict

- **FORMAT PASS** — все четыре cross-read/extend проверки успешны; logical checksum и file inventory совпадают; read-only открытия не изменили файлы.
- **PERFORMANCE PASS** — подтверждённой деградации median более `5%` нет; allocations/op не выросли.

**FORMAT PASS / PERFORMANCE PASS.** Исторический lexical batching восстановлен одинаково во всех сборках без изменения публичного API и дискового формата.
