# К7 — отмена checkpoint и потеря ответа подготовки

Изменены только испытания C# MCP: production SDK0.38.1-dev/native0.33.5-dev
не изменялись. Установленный native0.33.3-dev не обновлялся; CAD не подключён.

Шесть новых сценариев через официальный SDK client/server:

- Отмена после claim до materialization: local-only cancellation и явное
  notifications/cancelled проверяются отдельно.
- Отмена после materialization, когда ответ не опубликован: оба вида отмены.
- Timeout подготовки без native ответа: intent не выдан.
- Потерянный ответ уже подготовленного intent: исходный plan и unpublished
  receipt остаются, intent handle клиенту не выдан.

При отмене server gate освобождается по протокольной отмене или собственному
deadline. Consumed intent не оживает; replay отклоняется до новой публикации.
Snapshot полного набора файлов до отказа сравнивается после отказа и replay.
Один .processing request, не опубликованный response и materialization proof
(если он уже был создан) сохраняются. Автоматического повторного prepare нет.

Focused13/13 CheckpointRecoveryRoutesTests passed в
evidence/K7-checkpoint-cancellation/sdk-routes. Общая .NET regression368/368
passed без skip в evidence/K7-checkpoint-cancellation/sdk-regression.
Native physical identity guards этими synthetic declared-identity tests не
заменены: их последняя квалификация находится в STATUS-K7-RECOVERED-OPEN.md.

PR21/source c408c7c9 прошёл четыре Windows CI:38403047040/38403035444
baseline и38403046747/38403035458 SDK. В actual SDK CI log подтверждены
362 .NET,531 native,6 recovered OPEN scenarios253 hashes иSDK checker/Inspector.

До закрытия К7 остаются reparse/остальные partial-publication cases, полный
recovered OPEN workflow через MCP и отдельно согласованный live scenario.
К8–К11 остаются обязательными.
