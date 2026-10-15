# SDK: перенос одного собственного SOLID между слоями

Source SDK0.40.0/nativePython0.34.7. Opt-in profile:
`config/general-owned-level-moves.json`. Default profiles не изменены.
Флаг `ownedLevelMovesExperimental` — strict boolean, только general-v1,
зависит от entity sets, owned working copies, document bindings и level reading.
Python валидирует конфигурацию; новые публичные инструменты реализованы в C# SDK.

Регистрация: `ToolCatalog.Create(session)` включает
`OwnedLevelMoveTools.Create(session)`; Program.WithTools(ToolCatalog.Create(session)).
Все три инструмента отсутствуют и отвергаются в read-only:

- `prepare_owned_level_move(entitySetHandle, workingDocumentBindingHandle, parameters)`
  принимает ровно один SOLID, fresh binding и явно заданный именованный слой0–997.
  Parameters: targetLevelNumber, targetLevelName, targetActiveRaw integer0/1,
  selectionAfterPolicy=cleared_by_documented_api, mode=move_in_place.
  Сам получает новый native level receipt, сохраняет exact raw SHA и проверяет
  source/model/context. Создаёт immutable disabled plan и session handle без CAD.
- `execute_owned_level_move(ownedLevelMoveHandle)` повторно проверяет bytes,
  зависимости и срок; потребляет handle до submission, отзывает CAD references.
  Один fixed native recipe вызывает SetItemsLevel; no save/show/select/inverse.
  Успех — executed_unverified, fence остаётся. Возвращается execution handle,
  который сохраняется и при unknown/timeout; исходный request ID не теряется.
- `verify_owned_level_move(levelMoveExecutionHandle,candidateDocumentHandle,activeDocumentHandle)`
  использует новые inspection/listing handles после return и новый level read;
  проверяет native result/plan/once claim/journal/evidence и снятие собственной
  fence. Не вызывает CAD setter. Metadata recheck не является replay команды.

SDK store ограничен этой connection, TTL не продлевается, чужие handles,
tampered packets/working bytes, coercion/extra fields и повторный intent
отвергаются. Checkpoint SHA/read-only и фиксированные owner paths проверяются;
рабочий файл читается совместно с уже существующим writer, native удерживает
собственные leases/byte/context guards. Успешная подготовка не разрешает правку.

Native recipes зарегистрированы в `QueueOperations` через
`OwnedLevelMoveOperations`; validation/config checks предшествуют COM.
Helper Execute/Verify сами удерживают executor.lock — внешнюю вложенную
блокировку не добавлять. Native level reply теперь сохраняет bridgeRequestId
в самом response файле: старый raw receipt без ID не принимается для new plan.

Проверено29focusedSDK tests, native config14cases и admission15cases enabled/
15disabled. Actual official STDIO→native ownership/preparation/execution/
verification на simulated CAD:5scenarios,48requests,341independent hashchecks.
Включены control, false native result, wrong target, lost reply и packet tamper;
после позднего ответа оригинал verified без повторной команды. Marker refusal
проверяется до создания fixture. Это не реальный CAD/OPEN/activation test.

Общая regression завершена успешно:707.NET/0skips,531native,
175compatibility/0mismatches, все прежние operation/wire gates и MCP Inspector.
Evidence: `evidence/K8-level-sdk-final-short/sdk-verification.json`;
338 hashes source/build artifacts сверены после terminal exit0 session83635.
Изолированные новые wire результаты также повторены внутри общего запуска.

Ограничения: один SOLID; live setter, saved-result/reopen, разные модели,
остальные операции К8 и полная К9–К11 ещё не приняты. Native journal/once/fence
переживают SDK restart, opaque execution handle — connection-local;
отдельный historical verification/adoption путь после reconnect ещё требуется
перед общей приёмкой v1. Не снимать native fence для обхода этого ограничения.
