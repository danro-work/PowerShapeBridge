# К8 — чтение журнала переноса между слоями

SDK0.40.2-dev, native остаётся0.34.8-dev. Установленная панель и конфигурация
не менялись. Это расширение existing `inspect_operation_recovery`, не новый
executor и не автоматическое восстановление модели.

## Что изменено

Reader распознаёт `levelmoveexecutions/<operationId>.json` с операцией
`move_owned_solid_to_level`. Проверяет SHA/цепочку журнала, принадлежность packet,
точный одноразовый claim, matching fence и сохранённые working/checkpoint bytes.
Успешным native return считается только code1 с типом System.Boolean.
Integer1 и false/неустановленный тип не получают authoritative success.

План переноса содержит полную таблицу1000слотов и превышает64KiB; для этого
конкретного формата используется прежний контрактный предел16MB. Остальные
форматы сохраняют64KiB limit; неоднозначное расположение плана отвергается.
Конечный plan SHA и весь журнал сверяются повторно.

Регистрация прежняя: `RecoveryTools.Create` через `ToolCatalog`; аргументы и
`ReadOnly=true` сохранены. Reader доступен отдельному read-only серверу при
`recoveryInspectionExperimental`. Он не подаёт IPC request, не связывается с CAD,
не снимает fence/claim и не выдаёт права на edit/checkpoint restore.

## Реальная проверка до регрессии

Изолированный SDK в `work/LevelRecovery42` прочитал реальную операцию
`3b1a8dc3bd55434d938672fe51bc5c61` через новую READ-ONLY STDIO connection.
Результат: `recovery_inspected_recorded`,
`historically_verified_requires_fresh_context`, journal6events/succeeded_verified,
native Boolean true. Working/checkpoint bytes совпадают, claim остаётся,
fence отсутствует после уже выполненного independent verifier.
20защищённых hashes и875служебных JSON неизменны; queue не использовалась.
Никакой текущий CAD-контекст этим файловым чтением не устанавливается.

Evidence: `evidence/K8-live-level-recovery-read/result.json`;
отдельный file-only audit `independent-audit.json` — passed. Аудитор сравнил
все6events и chain, plan/claim, saved hashes и точный recorded Boolean return.

После реального чтения прошли42focused tests/0skips:
`evidence/K8-level-recovery-focused/level-recovery.trx`.
22новых level cases покрывают6фаз, native Boolean/int/null/false,
missing/foreign/extra claim, foreign/missing/retained fence, changed packet,
wrong/ambiguous plan, oversizedplan и некорректную форму return.
20существующих recovery cases также прошли. Все отказы сохраняют файлы.

Первый PR CI на618d727 имел один baseline failure в прежнем Python protocol
fixture: повторяющиеся request IDs могли попадать на ещё не удалённый pending
entry после публикации ответа. Причина воспроизведена с контролируемым порядком:
первый invalid request даёт−32602, повтор того же pending ID−32600, новый ID−32602.
MCP2025-11-25 запрещает повтор ID в одном сеансе:
https://modelcontextprotocol.io/specification/2025-11-25/basic#requests
Исправлен только test client/invalid-case IDs; production server не менялся,
проверка−32602 и все negative cases сохранены. Protocol test после fix passed.
Evidence: evidence/K8-level-recovery-protocol-fix/reproduction.json.

## Ограничения

Reader всегда оставляет `outcome=not_resolved_by_recorded_inspection`,
`executionAuthorized=false` и `retryAllowed=false`. Для текущей модели нужны
adopt original execution и operation-specific verifier со свежими readings.
Checkpoint recovery и общая поддержка журналов rotate/scale/export остаются
отдельными незавершёнными требованиями; К8–К11 не закрыты.
