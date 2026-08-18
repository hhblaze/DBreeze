# DBreeze Transaction/FSR: old vs new

Run: `20260818-TransactionFsr`  
Порог: подтверждённая регрессия median не должна превышать `5%`.

## Verdict

- **FORMAT PASS** — ранее выполненный bidirectional compatibility probe покрывает обычные transaction tables: create/read/extend, row count, checksum и file inventory совпадают; read-only открытия не изменяют файлы.
- **TRANSACTION PERFORMANCE PASS** — ни один из восьми disk Transaction workload не имеет подтверждённой median-регрессии более `5%`.
- **MEMORY PASS** — allocations/op old/new совпадают во всех восьми workload.

## Окружение и методика

| Параметр | Old | New |
|---|---|---|
| Repository | `D:\VS\DBreezeRealm_copy\DBreeze` | `D:\VS\DBreezeRealm\DBreeze` |
| DBreeze assembly | `1.138.2026.0603` | `1.139.2026.0817` |
| ShortRun root | `D:\Temp\DbreezeDbTest_copy\transaction-short-20260818` | `D:\Temp\DbreezeDbTest\transaction-short-20260818` |
| MediumRun root | `D:\Temp\DbreezeDbTest_copy\transaction-confirm-old-20260818` | `D:\Temp\DbreezeDbTest\transaction-confirm-new-20260818` |

- BenchmarkDotNet `0.15.8`, runtime `.NET 8.0.30`, SDK `10.0.111`.
- Windows 11, Intel Core i7-8700, 6 physical / 12 logical cores, AVX2, Workstation GC.
- Benchmark sources синхронизированы source-only; SHA-256 совпадает для всех 16 `.cs`/`.csproj` файлов.
- Существующий `TransactionBenchmarks` не использовался для verdict, поскольку он работает через memory storage и не достигает FSR.
- Historical 1M/10M и несвязанные workloads не запускались.

## Workloads

- Read DB содержит `65,536` записей `int → 128-byte value`; один invocation выполняет `1,024` selects.
- Измерены local/random hot reads, random reads после reopen и восемь параллельных readers по 128 selects.
- Write iteration использует свежую DB; setup, seeding, reopen, cleanup и correctness validation находятся вне измеряемого участка.
- Измерены `4,096` sequential inserts, random inserts и random updates с одним commit, а также `64` inserts с commit после каждой записи.
- Все row count, value и checksum проверки прошли. Последние DB каждого benchmark-процесса сохранены в соответствующих roots.

## Итоговое сравнение

Verdict использует median. Для методов, превысивших `+5%` в ShortRun, приведён подтверждающий MediumRun, выполненный на свежих DB в обратном порядке: new, затем old.

| Method | Run | Old mean | New mean | Old median | New median | Speedup old/new | Median delta | Old allocated | New allocated | Verdict |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| LocalPointSelectHot | MediumRun | 4.007 us | 4.791 us | 3.898 us | 4.036 us | 0.966x | +3.54% | 4.95 KB | 4.95 KB | PASS |
| RandomPointSelectHot | ShortRun | 24.258 us | 24.219 us | 24.321 us | 24.375 us | 0.998x | +0.22% | 10.49 KB | 10.49 KB | PASS |
| RandomPointSelectAfterReopen | ShortRun | 25.189 us | 25.847 us | 25.069 us | 25.876 us | 0.969x | +3.22% | 10.49 KB | 10.49 KB | PASS |
| EightThreadRandomPointSelectHot | ShortRun | 45.597 us | 44.997 us | 45.419 us | 45.037 us | 1.008x | -0.84% | 10.58 KB | 10.58 KB | PASS |
| SequentialInsertBatchAndCommit | MediumRun | 12.07 us | 12.09 us | 12.03 us | 12.14 us | 0.991x | +0.91% | 1.42 KB | 1.42 KB | PASS |
| RandomInsertBatchAndCommit | MediumRun | 16.04 us | 15.70 us | 16.06 us | 15.64 us | 1.027x | -2.62% | 8.97 KB | 8.97 KB | PASS |
| RandomUpdateBatchAndCommit | MediumRun | 283.03 us | 216.70 us | 282.39 us | 216.72 us | 1.303x | -23.26% | 10.90 KB | 10.90 KB | PASS |
| InsertCommitEach | MediumRun | 27.721 ms | 18.508 ms | 18.673 ms | 18.506 ms | 1.009x | -0.89% | 4.07 KB | 4.07 KB | PASS |

Geometric-mean median speedup составляет `0.985x` для reads, `1.075x` для writes и `1.029x` для всех восьми workloads.

## Confirmation runs

Первичный old → new ShortRun показал более 5% для local read и всех write methods. Эти результаты не воспроизвелись в более длинном new → old MediumRun: четыре из пяти методов оказались в пределах ±3.6%, а random update новой версии быстрее на 23.26%. Согласно заранее заданному правилу verdict использует подтверждающий прогон.

`InsertCommitEach` в old MediumRun имеет bimodal distribution (`mValue = 3.33`); median остаётся устойчивым критерием и отличается от new менее чем на 1%. Allocations и GC profiles совпадают.

Raw reports:

- `D:\Temp\DbreezeDbTest\comparison\20260818-TransactionFsr\old-short`
- `D:\Temp\DbreezeDbTest\comparison\20260818-TransactionFsr\new-short`
- `D:\Temp\DbreezeDbTest\comparison\20260818-TransactionFsr\old-medium`
- `D:\Temp\DbreezeDbTest\comparison\20260818-TransactionFsr\new-medium`
- `D:\Temp\DbreezeDbTest\comparison\20260818-TransactionFsr\read-short-comparison`
- `D:\Temp\DbreezeDbTest\comparison\20260818-TransactionFsr\read-medium-comparison`
- `D:\Temp\DbreezeDbTest\comparison\20260818-TransactionFsr\write-medium-comparison`

## Итог

**FORMAT PASS / TRANSACTION PERFORMANCE PASS / MEMORY PASS.** Изменения FSR не дали подтверждённой деградации базовых disk inserts/selects более 5%; write geometric mean улучшился, allocations/op не изменились.
