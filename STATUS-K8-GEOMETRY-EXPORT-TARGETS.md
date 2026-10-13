# К8 — подготовка и защита результата экспорта геометрии

Реализованы внутренние OwnedGeometryExportTargets и TargetLease: неизменяемый
фиксированный DMT target, четыре точные ссылки binding/set, владение файлами,
сроки, empty/world snapshot, pin каталога/JSON и отказ от перезаписи/replay.
Связанный complete-return rotation fence можно наблюдать для последующей проверки,
но он не снимается. Unknown/partial, старое чтение и повреждённые proofs отклоняются.

Проверено в work/K8-geometry-export-targets-final:

- 30 native сценариев, включая complete/partial rotation, tamper summary/return/claim,
  output collisions, reserved/claimed targets, expired/foreign refs, curve rejection,
  late working/fence, semantic target tamper и замену каталога.
- Независимый Python-аудит: 478 хешей, полный инвентарь, ownership/refs/min expiry,
  защищённые checkpoint, правильная хронология и сохранённый rotation fence.
- CLI без fixture marker отказывает до создания испытательных файлов.
- Framework x64 panel/worker собираются в staging. Новый runner включён в verify-sdk.

Доказательства: evidence/K8-geometry-export-targets/final. Предыдущие непрошедшие
прогоны сохранены; утверждение PASS относится к final. В ходе разработки исправлены
ошибочные ожидания mutable working bytes и мутация объекта отчёта тестовым fault.
Производственный guard не ослаблялся для прохождения тестов.

Предыдущее исправление PR34/6164b1f7 прошло четыре Windows CI:
38479039740,38479039713,38478913268,38478913338 — completed/success.

Ограничения: нет DMT-файла/parser, CAD-export/selection executor, public MCP route,
live qualification, native rotation convention qualification или fence release.
Тесты используют собственные непрозрачные файлы и fake contexts; это не CAD geometry.
Подробный контракт: K8-GEOMETRY-EXPORT-TARGET-CONTRACT.md.

Installed runtime и подготовленные пакеты К7 не менялись; PowerShape не изменялся.
К7 live scenario требует ответа пользователя. Следующая работа: qualified export
executor/parser и проверка rotation, затем весь остальной объём К8–К11 по плану.
