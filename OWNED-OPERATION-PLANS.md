# Связанный план собственной CAD-копии — экспериментальный контракт P2.30

P2.31: runtime 0.31.0/21 tools. Explicit executor одного owned SOLID и отдельная independent verification приняты по OWNED-TRANSLATION.md. Passive plan/packet executionEnabled=false сами edit не разрешают; executor создаёт отдельный execution plan и повторяет guard до команды. Own OPEN P2.29 не повторялось, claim/journal сохранены. Ниже исторический контракт этого отдельного компонента.

9 октября 2026. Панель/worker/свежий адаптер 0.30.0. Приняты пассивная подготовка
и повторная проверка связанного пакета на одной сохранённой SOLID-копии.
Геометрического executor нет; MCP v1 не завершён. STATUS-P2.30-GUARD.md.

## Публичные инструменты

Флаг ownedOperationPlansExperimental=true требует general-v1 и включённых
operationPlansExperimental/entitySetsExperimental/workingCopiesExperimental/
documentBindingsExperimental. Профиль config/general-owned-operation-plans.json
включает также own-open для проверки прежнего исхода; каталог 19, read-only 6.
Прежние профили сохраняют каталоги 17/15/13/11/9/6. Новые инструменты исключены
из отдельного read-only сервера, включая validate_owned_operation.

| Инструмент | Вход | Результат |
|---|---|---|
| prepare_owned_operation | operationPlanHandle, workingDocumentBindingHandle этого подключения | immutable native packet + prepared journal; ownedOperationHandle |
| validate_owned_operation | ownedOperationHandle этого подключения | повторные наблюдения всех зависимостей; ничего не записывает |

Нативные ID/пути/SHA, произвольные команды, read references P2.21 и дополнительные
поля на публичном входе запрещены. Адаптер извлекает private operationPlanId/SHA
и bindingId/SHA из leases. Для validation передаются только ownedOperationId/SHA.
Prepare пишет локальные доказательства, но не обращается к командам CAD.

## Неизменяемые зависимости и guard

Пакет содержит 14 полей: schemaVersion, ownedOperationId, operationPlanId/SHA,
bindingId/SHA, preparedUtc, expiresUtc, executionEnabled=false,
nativeObservedPropertiesSha256, windowContextSha256, workingSha256, copyId,
ownershipSha256. Immutable JSON создаётся через CreateNew/fsync, как и единственное
событие native prepared-журнала. Повторная запись того же ID запрещена.

Адаптер заново проверяет SHA локального плана, источников/наборов и binding,
полные наблюдения, raw-единицы, параметры, действующий prepared-журнал и leases.
Новый handle не продлевает жизнь зависимостей: минимум их сроков, не более 300 с.
Смена документа, потеря зависимости, timeout/ошибка после submission отзывают
пакеты; автоматического повторного запроса нет. При отмене/ошибке подготовки
локальные файлы могут остаться, их ID и native receipt сохраняются для проверки.

Worker удерживает executor.lock, одну COM application/document и file leases.
Файлы плана/наборов/receipts/binding и prepared-события читаются/хешируются/парсятся
из одних байтов, остаются открытыми с FileShare.Read до конца вызова. Повторный
путь в кэше проверяется и по ожидаемому SHA. Ownership lease удерживает отдельные
source/work/checkpoint; сверяет native file IDs, bytes SHA, links, размер,
ReadOnly checkpoint и точный путь активного Working.psmodel.

До и после записи пакета выполняются два прохода DocumentBindings.Check:
всего четыре свежих полных наблюдения через удерживаемый документ, сверка
окон/процесса/start/session, inventory/свойств/единиц и COM IUnknown активного
документа. Между проходами COM-пара не освобождается. В конце повторно проверяются
prepared-only состояние обоих журналов, отсутствие writer.lock/добавленных событий,
lease, список окон и срок. Validation также проверяет workingSha256 из пакета.
Изменившиеся рабочие байты требуют нового пакета, не автоматического принятия.

Успех — owned_operation_prepared_observed либо owned_operation_validated_observed;
отказ — owned_operation_rejected. Ответ указывает guardPasses=2,
sameContextObservationReads=4, heldProofArtifactCount и journalPreparedOnly=true.
geometryMutationAttempted/contextChangeAttempted/cadSaveAttempted=false.

## Параметры и ограничения

План переноса: один явный набор, ненулевой конечный вектор из трёх чисел в пределах
1e6, world/document_raw/move. План пересечения: два разных набора, основной
SOLID/SURFACE, второстепенный SURFACE, без общих сущностей, только offset=0.
Подготовка таких параметров не означает приёмку соответствующей CAD-команды.
Требуются пустое выделение и мировой workplane; неоднозначный inventory отклоняется.

Python observed SHA использует sorted JSON и перепроверяется адаптером. Native SHA
использует C# canonical encoding и независимо связывает binding с полным свежим
наблюдением. Эти кодировки различаются; SHA не объявляются взаимозаменяемыми.

Guard не блокирует пользователя или сторонний CAD-клиент и не доказывает атомарную
ревизию/полную топологию или непрерывный lifecycle. После возврата COM/file leases
освобождены: отдельная validation не разрешает последующий edit. В этом коде нет
command callback или geometry Execute. Будущий executor должен повторить guard
и удержать его контекст до команды, записать one-use claim/start/return и проверить
постусловия без replay неизвестного исхода. Save/import/export/close/recovery,
live surface/curve sets и несколько других моделей остаются впереди.

## Доказательства

evidence/P2.30-guard/code: 105 Python, 278 C#, MCP-протокол без CAD.
Синтетические проверки включают успешные translation/intersection packets,
подмену всех SHA-зависимостей, invalid numeric/roles/overlap, смену контекста между
чтениями, запрет записи/удаления удерживаемых файлов, смену working bytes и появление
writer.lock между проходами. Живая приёмка — installed-mcp-final/qualification.json;
независимый audit — independent-audit.json и 51 exact native artifact.
