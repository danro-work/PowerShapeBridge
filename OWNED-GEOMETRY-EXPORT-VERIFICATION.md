# Независимая диагностическая проверка экспорта

OwnedGeometryExportVerification использует отдельный IContext только с getters:
Windows/Observation/ConfirmDocument, ExportSettings и Composite. Selection,
ExportDmt, ClearOwnSelection и прочих mutation callbacks в интерфейсе нет.
Код не подключён к production COM, очереди или публичному MCP-каталогу.

Вход содержит девять точных ссылок:executionRequestId/executionReceiptSha256,
inspectionRequestId/inspectionSha256,listingRequestId/listingSha256,
windowNumber,modelId,expiresUtc. Путь результата или текст команд не принимается.

Проверяются complete executed_unverified receipt, immutable execution/target
plans, historical binding/set/source и ownership/checkpoint/working bytes,
native journal chain, original export fence/once claim, порядок всех add/export/
clear callbacks и SHA started/returned/summary records. Unknown или partial receipt
отклоняется; это не автоматическое разрешение неизвестного исхода.

Для исходной target-истории добавлен отдельный verification lease:старые зависимости
проверяются на preparedUtc, а не используются как текущая авторизация. Его Recheck
для исполнения всегда запрещён. Новые inspection/listing обязаны быть строго позже
aggregate command_returned и иметь собственный действующий TTL. Current document,
окна, source observed digest, прямой mapping/settings и saved bytes проверяются
через отдельную retained read context до и после чтения файла:четыре observations.

Файл открывается read-only с запретом записи/удаления, проверяется его native
single-link identity относительно execution summary, повторные byte SHA,
DMT grammar/counts/declared units. Совпадение SHA само по себе не доказывает
association с CAD-геометрией; native export/settings/unit/frame требуют квалификации.

Результат только observed_export_file_context_match_unqualified. Публикуется
неизменяемый diagnostic в geometryexportverifications/<verificationId>.json,
с точными ссылками на execution, свежие чтения и output SHA/identity.
geometryVerified=false,nativeProviderQualified=false,copyFenceReleased=false.
Execution journal остаётся executed_unverified; исходные claim/fence не меняются.
Reader не переэкспортирует, не выделяет, не очищает выделение и не сохраняет CAD.

Квалификация:21 fake-context сценарий,734 независимо проверенных file hashes;
отказы по receipt/target/summary/step/fence/claim tamper, изменённому/заменённому/
отсутствующему файлу, старому/equal-time чтению, expiry, settings/mapping/context,
retained document, active choice и unknown export. Это evidence внутреннего
verifier, а не live PowerShape acceptance или полная CAD topology equivalence.

Перед выпуском нужны native provider qualification, независимая геометрическая
проверка transformations, безопасное разрешение собственного fence, SDK stores,
transport/fault qualification и согласованные живые сценарии по К8–К11.
