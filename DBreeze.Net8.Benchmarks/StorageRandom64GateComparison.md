# Storage Random64ByteRead gate: reference vs pre-change vs candidate

Дата: 19.08.2026. Статус: **PASS**.

## Что сравнивалось

| Вариант | SHA-256 `DBreeze.dll` | Назначение |
|---|---|---|
| Reference | `29BFD8B8DE22ECB9A66CD0A9BC99DA19F122AE7CB3484C1761DAABA2592379EE` | Эталон из `D:\VS\DBreezeRealm_copy\DBreeze` |
| Pre-change | `AA7966069F46D812F60AA708A1DDFEC67FDEA754379BCEB60E2FD786840C1E05` | Текущая 32 KiB page-cache DLL до lazy admission |
| Candidate | `1C3A58A56305A99FEC1DD022F9C9BE18D669C402DDDB43AC8B00D92D7B52EF74` | Lazy buffer и admission 3/2 обращения |

Candidate сохраняет 32 KiB `[ThreadStatic]` committed-page cache и positioned `RandomAccess` I/O. При первом eligible read создаётся только metadata. Для reads до 256 байт page допускается на третьем последовательном обращении к тому же `owner/version/page`, для более крупных reads — на втором. Уже созданный buffer переиспользуется при смене candidate; `ArrayPool` не используется.

Cross-page, rollback-backed и transactional reads не участвуют в admission и остаются на exact positioned path. Публичный API и форматы disk/pointer/rollback/backup не менялись.

## Методика

- BenchmarkDotNet 0.15.8, `.NET 8.0.30`, ShortRun: 3 warmup и 3 measurement iterations.
- Intel Core i7-8700, Windows 11, high-performance power plan.
- Три независимых запуска каждого бинарника в чередующемся порядке: candidate/pre-change/reference, pre-change/reference/candidate, reference/candidate/pre-change.
- В таблицах используется median каждого запуска, затем median трёх запусков.
- Каждый 64-byte метод содержит 1024 операции; BenchmarkDotNet нормализует время и allocation до одной операции.
- Gate для random/cold/mixed: candidate не хуже reference более чем на 5%. Gate для hot/local/eight-thread/page reads: candidate не хуже pre-change более чем на 5%.

Raw BDN artifacts сохранены локально в `DBreeze.Net8.Benchmarks/bin/Random64Gate/{variant}-run{1..3}`.

## Итоговая трёхсторонняя таблица

| Benchmark | Reference median, ns/op | Pre-change median, ns/op | Candidate median, ns/op | Gate base | Candidate delta | Allocated ref/pre/cand, B/op | Gate |
|---|---:|---:|---:|---|---:|---:|---|
| `Random64ByteRead` | 2,885.99 | 2,927.74 | 2,892.67 | Reference | +0.23% | 88 / 88 / 88 | PASS |
| `ColdRandomPages64ByteRead` | 4,952.97 | 5,030.18 | 4,980.41 | Reference | +0.55% | 88 / 88 / 88 | PASS |
| `MixedTableWorker64ByteRead` | 2,892.39 | 2,923.16 | 2,906.14 | Reference | +0.48% | 88 / 88 / 88 | PASS |
| `RepeatedSamePage64ByteRead` | 74.69 | 74.36 | 44.77 | Pre-change | -39.79% | 88 / 88 / 88 | PASS |
| `Local64ByteRead` | 44.13 | 77.96 | 73.66 | Pre-change | -5.52% | 88 / 88 / 88 | PASS |
| `EightThread64ByteRead` | 70.85 | 119.67 | 75.79 | Pre-change | -36.67% | 91 / 91 / 91 | PASS |
| `CommittedRead` | 898.78 | 890.14 | 882.01 | Pre-change | -0.91% | 4,120 / 4,120 / 4,120 | PASS |
| `EightThreadCommittedRead` | 1,390.43 | 1,385.84 | 1,425.54 | Pre-change | +2.86% | 4,443 / 4,444 / 4,444 | PASS |

`RepeatedSamePage64ByteRead`, `Local64ByteRead` и `EightThread64ByteRead` показывали два sub-100 ns режима в отдельных ShortRun-запусках. Поэтому величину ускорения нельзя интерпретировать как точную; gate основан на заранее выбранном median-of-three и не показывает устойчивого ухудшения candidate. Дисковые random/cold/mixed medians существенно стабильнее.

## Medians отдельных запусков

Все значения — ns/op.

| Benchmark | Ref 1 | Ref 2 | Ref 3 | Pre 1 | Pre 2 | Pre 3 | Cand 1 | Cand 2 | Cand 3 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `CommittedRead` | 888.72 | 898.78 | 908.98 | 1,047.31 | 884.11 | 890.14 | 882.01 | 878.33 | 952.73 |
| `EightThreadCommittedRead` | 1,390.43 | 1,397.39 | 1,381.27 | 1,454.55 | 1,385.84 | 1,372.44 | 1,425.54 | 1,383.89 | 1,432.03 |
| `Random64ByteRead` | 2,926.66 | 2,885.99 | 2,822.31 | 2,948.42 | 2,927.74 | 2,888.87 | 2,892.67 | 2,862.76 | 2,926.63 |
| `ColdRandomPages64ByteRead` | 4,957.11 | 4,918.69 | 4,952.97 | 5,030.18 | 5,037.04 | 4,862.50 | 4,980.41 | 4,886.35 | 5,085.55 |
| `RepeatedSamePage64ByteRead` | 74.74 | 74.69 | 73.76 | 74.36 | 44.12 | 74.89 | 44.02 | 74.03 | 44.77 |
| `MixedTableWorker64ByteRead` | 2,956.74 | 2,892.39 | 2,839.06 | 2,923.16 | 2,913.15 | 2,928.91 | 2,906.14 | 2,922.52 | 2,853.72 |
| `Local64ByteRead` | 44.07 | 44.13 | 75.46 | 77.96 | 78.66 | 74.14 | 73.66 | 79.24 | 44.13 |
| `EightThread64ByteRead` | 70.85 | 116.40 | 70.64 | 117.24 | 125.77 | 119.67 | 75.79 | 70.54 | 119.30 |

## Correctness и memory checks

Полный `DBreeze.Net8.Tests` regression-набор прошёл. Новый test запускает каждый admission-сценарий на отдельном новом thread и через reflection проверяет:

- cold unique pages создают metadata, но `Buffer == null`;
- mixed-table worker создаёт metadata, но `Buffer == null`;
- после первых двух 64-byte same-page reads buffer отсутствует, на третьем read создаётся и заполняется;
- для 4 KiB same-page reads buffer создаётся на втором обращении;
- buffer имеет ровно 32 KiB и переиспользуется после admission.

Существующие проверки дополнительно покрывают cross-page/EOF, commit invalidation, transactional commit/rollback, reopen/restore и concurrent commit/read. Ни один benchmark не показал систематического роста `Allocated/op`; cold-only и mixed-only threads не удерживают 32 KiB buffer.

## Вывод

P1 `Random64ByteRead` закрыт. Новый admission устраняет немедленное удержание 32 KiB для cold/mixed workers, возвращает random/cold/mixed latency в пределах +0.55% от эталона и сохраняет hot page-cache envelope относительно pre-change DLL.
