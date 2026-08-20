# LianaTrie modernization backlog

Этот файл отделяет последующую модернизацию от текущего набора исправлений совместимости.
Он не является основанием менять публичный API или дисковый формат.

## Обходы и глубина стека

- Основные forward/backward и range-обходы уже используют явный `Stack`; это закреплено
  regression-тестом `DeepFullScanDoesNotUseTheCallStack`.
- Перед следующей переработкой отдельно проверить все альтернативные ветви `StartFrom`,
  `FromTo`, `StartsWith`, `Skip` и `SkipFrom`, а также обходы рекурсивных nested tables.
- Удалить или актуализировать оставшиеся комментарии про `RecursiveYieldReturn` только после
  проверки соответствующего пути. В ходе текущего аудита активный саморекурсивный iterator в
  `LianaTrie` не подтверждён, поэтому его реализация не менялась.
- Для будущего изменения обязательны differential-тесты baseline/current на очень длинных
  бинарных ключах, раннее завершение enumeration и ограничение retained memory глубиной текущего
  пути, а не количеством строк.

## Унаследованное поведение, требующее отдельного контрактного решения

- Закрытие изменённой `NestedTable` до master `Commit` может не сохранить её изменения. Поведение
  воспроизводится и в baseline, и в current; в этом change set оно не меняется. Нужны отдельная
  минимальная fixture и решение о документированном lifecycle-контракте.

## Исправленные унаследованные дефекты

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
- Отсутствие stack overflow на максимальной поддерживаемой глубине и устойчивого замедления более
  5% относительно текущей реализации.
