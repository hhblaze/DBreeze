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
  дефекты. Early nested dispose также остаётся P0 data-loss gate после исправления.
- Отсутствие stack overflow на максимальной поддерживаемой глубине и устойчивого замедления более
  5% относительно текущей реализации; для contract-changing исправлений сравнивать одинаковый
  объём реально выполненной работы.
