# LianaTrie modernization backlog

Этот файл отделяет последующую модернизацию от текущего набора исправлений совместимости.
Он не является основанием менять публичный API или дисковый формат.

## Завершённые обходы и глубина стека

- Активная рекурсия и копирование `generationMapLine` удалены из `StartFrom`, `SkipFrom`,
  `StartsWith`, closest-prefix и `Min`/`Max`. Full scan, `FromTo` и обычный `Skip` сохранили
  явный `Stack`; bounded families используют общий direction-specific DFS с relation-state.
- `StartsWith` представлен интервалом `[prefix, lexicographic-successor)`, closest-prefix —
  одиночным итеративным спуском. Terminal `ValueKid` сравнивается как конец ключа, а его служебный
  `Val == 256` не участвует в лексикографическом сравнении.
- Каждый активный iterator освобождает оставшиеся enumerators через `finally`. `Min`/`Max`
  немедленно освобождают путь после первого leaf. Retained state ограничен O(depth), а прежние
  O(depth²) path copies отсутствуют.
- После отдельного call-site/cache review удалён неиспользуемый logical-path параметр из внутренних
  `ReadSelf`/`GenerationNodeRead` во всех target-specific реализациях. Committed-node cache по-прежнему
  идентифицируется только physical pointer; public API, cache epoch и disk protocol не изменились.
- Regression gate покрывает общий префикс длиной 8192, ранний `Take(1)`/manual dispose, повторное
  enumeration, lazy/eager, пустые и prefix-ending ключи, `0x00`/`0xFF`, `ulong.MaxValue`, read
  visibility и четырёхуровневую recursive nested-table hierarchy с disk reopen.
- Process differential на глубине 512 прошёл в обе стороны. В deterministic fixture base
  `293/293` и extended `325/325`; на обеих контрольных точках совпали длина и SHA-256 всех
  `24/24` LianaTrie-файлов.
- Изолированный Release A/B/B/A против current до этой переработки: 68 сценариев, три прогрева и
  по 10 измерений в каждом из двух порядков. Geomean current/pre-change — `0.6652` (current в
  `1.503×` быстрее), для целевых families — `0.5607` (`1.784×`). Checksums совпали, DB-size delta
  и retained-memory growth равны нулю. Наблюдаемый дополнительный allocation/time первой eager
  boundary-row является исправлением контракта: baseline оставлял именно эту row нематериализованной.

## Исправленные унаследованные дефекты

- Потеря изменений при раннем `Dispose`/`CloseTable` изменённой `NestedTable` устранена. Публичный
  handle уменьшает `quantityOpenReads` ровно один раз; dirty internal table с нулём handles остаётся
  во владении coordinator до успешного завершения master commit либо rollback. Между фазами
  transactional commit deferred table не освобождается. После transaction completion, `Reset`,
  recreation и terminal `Dispose` cleanup идемпотентен и выполняется вне coordinator write-lock.
- Relocation и `ChangeKey` обновляют physical pointer, root и structural key каждой retained nested
  table, поэтому deferred cleanup продолжает адресовать правильную identity после rename. Одинаковый
  lifecycle внесён во все существующие coordinator specialization: shared, .NET 8, .NET 6,
  netcoreapp3.1, netstandard2.1 и PCL.
- Закреплён пользовательский контракт: `Dispose` закрывает только handle; последующий master
  `Commit` сохраняет уже выполненные nested mutations, а `Rollback` и implicit rollback их отменяют.
  Regression gate покрывает memory/disk, double dispose, несколько handles, close/reopen в epoch,
  committed reader, rename/relocation, parent removal, `RemoveAll(true)`, recursive hierarchy и direct/
  transactional lifecycle. Двусторонний baseline/current process probe прошёл; историческая потеря
  данных является единственным reviewed behavioral difference.
- `Commit` и `Rollback` завершают write epoch, но не пользовательский `Transaction`. Внутренний
  callback coordinator переименован из двусмысленного `TransactionFinished` в
  `CommitCycleFinished`: он освобождает только deferred tables с нулём handles и не вызывает
  `Reset`/`Dispose`. Открытые handles и их `NestedTableInternal` остаются coordinated.
- Исправлена ещё одна унаследованная потеря данных: writable `NestedTable`, оставшаяся открытой
  после `Commit`/`Rollback`, теперь автоматически участвует в следующем epoch без повторного
  `InsertTable`. Handle хранит allocation-free session token master trie; token стабилен между
  промежуточными cycles и инвалидируется только при terminal `TransactionIsFinished`. Реальная
  nested mutation восстанавливает coordinator owner-thread и итеративно помечает всю parent chain
  до master; no-op insert/remove/change-key новый epoch dirty не делает. После terminal transaction
  stale handle бросает `TRANSACTION_DOESNT_EXIST`, чужой поток —
  `TRANSACTION_CANBEUSED_FROM_ONE_THREAD`.
- Regression gate повторных cycles покрывает memory/disk, insert/update/remove/change-key/
  insert-part/data-block, commit→rollback→commit, несколько handles, early dispose,
  четырёхуровневую recursive hierarchy, multi-table transactional commit, direct `LTrie`,
  committed reader, no-op, stale и cross-thread rejection. Baseline create → current two-commit →
  baseline read и обратный current create → baseline safe write → current read прошли. Полный
  Release suite — `80/80 PASS`; API — `1423→1425` без removals/changes; compile matrix прошла.
- Короткий одинаковый-work A/B (100 nested write/commit на measurement, warmup + 5) дал median
  `19.670 ms/op` baseline и `19.642 ms/op` current (`−0.14%`), allocations
  `43,402.08→17,232 B/op`, retained delta одинаковый `+224 B`. Session/re-arm path не создаёт
  объектов; полный historical benchmark suite повторно не запускался.
- Потеря dirty generation-map при `ChangeKey` была минимизирована до переключения sibling-ветки:
  внутренний `LTrieRootNode.GetKey` удалял divergent suffix без предварительного сохранения dirty
  generation nodes. Теперь `Save_GM_nodes_Starting_From(i)` выполняется до pruning, а descending
  save использует индексный цикл без iterator allocation. Исправление действует в shared и во всех
  linked targets, включая .NET 8; public API, journal и wire format не менялись.
- Regression gate покрывает существующий и отсутствующий source key, один и два colliding leaf,
  несколько `ChangeKey` разных ветвей, commit/rollback/reopen, все шесть перестановок обычных
  mutations, `ChangeKey` и nested mutation, а также rename parent key обычной и рекурсивной nested
  table. Mixed old↔current fixture читается baseline после записи current; deterministic split
  fixture по-прежнему совпадает в 24/24 файлах на каждом checkpoint.

## Gate следующего этапа

- Никаких изменений wire format и baseline public surface.
- Двусторонний old↔new disk test и raw-file identity для детерминированной fixture.
- Cache epoch и recreate остаются постоянными P0 regression-gates; это инварианты, а не открытые
  дефекты. Early nested dispose и повторная mutation через открытый handle после commit/rollback
  также остаются P0 data-loss gates после исправления.
- Отсутствие stack overflow на максимальной поддерживаемой глубине и устойчивого замедления более
  5% относительно текущей реализации; для contract-changing исправлений сравнивать одинаковый
  объём реально выполненной работы.
