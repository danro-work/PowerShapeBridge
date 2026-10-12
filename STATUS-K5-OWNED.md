# К5 — исполнение и независимая проверка, выполняется

Текущий блок: SDK source 0.35.1-dev / native source/staging 0.32.0.
Установленный native остаётся 0.31.0; default adapter switch не выполнен.
К5 ещё не принят. Чистый checkout 8996eb7 и все четыре Windows CI PR #6
прошли для SDK 0.35.0-dev. Live SDK pilot обнаружил два реальных дефекта:

- Новое OPEN 6ed9d4106d054b28a05e8ab0b59c5811/copy
  5fef10809eb44904ade27e2ca843a8b9 дошло до command_started, но PowerShape
  показал «Модель с таким именем: Working уже открыта». Worker timeout 80s;
  command_returned отсутствует. Команда не повторена, geometry/save/close не
  отправлялись. Пользователь сам нажал OK. Fresh read затем подтвердил четыре
  прежних документа, исходный активный Test_Block_P2 и неизменённые source bytes.
  Исходный OPEN остаётся unknown; claim/журнал не переписывались/не удалялись.
- startup-error.log 13:09:51Z показывает IOException из Panel.WritePanelStatus
  / File.Replace, а не сбой launcher. Панель продолжала работать. SDK чтение
  mutable advisory reports теперь допускает atomic replacement; immutable
  proofs/config сохраняют строгий sharing. Native writer при transient sharing
  collision оставляет последний полный heartbeat и публикует следующий tick,
  без dialog/CAD retry. Детерминированные file-lock tests добавлены.

Новые copies получают уникальное Working_copyId.psmodel/ownership schema 2,
schema 1 остаётся совместимой. До OPEN native guard проверяет совпадение имени
во всех свежих окнах, без открытия/claim/журнала при отказе. Staging:
work/K5-native-unique-copies; evidence/K5-owned/collision-fix/sdk-verification.json.
Полный SDK check пройден: 162 .NET, four STDIO clients, 320 Framework synthetic,
78 actual artifact cases (0 mismatches), Inspector. Runtime/deployment/новый
live scenario остаются следующим шагом.
Copy 2b4aaf83e1f549158659c542fec0689c остановилась до CAD из-за ошибочного
поля helper; она известна, не открывалась, сохранена как evidence. Отдельный
readonly saved-byte seed — work/K5-new-owned-seed/Source.psmodel.

12 октября 2026. SDK 0.35.0-dev: full general/read-only catalog 21/6.
Native runtime 0.31.0 не менялся. К5 не завершён; default live switch
не выполнялся. Новый SDK прошёл passive read-only pilot на существующей панели:
свежие process/queue/deployment checks, list_documents, inspect_active_model и
inspect_entity_candidates. В этом первоначальном passive pilot команды CAD не выполнялись.

Перенесён OwnedExecutionProof: open parameters потребляют document choice
после проверки candidate/copy leases; expiry — минимум dependent choices.
Verification parameters выбирают fresh source receipts и one-use choice.
Raw open/translation plans проверяются по original byte SHA и точным
переданным зависимостям. ReturnCode=1 (int, не bool), copyFenced=true и
recorded executed_unverified не становятся succeeded_verified автоматически.
Отдельный verification result требует succeeded_verified, noCommandReplay,
fenceReleased и отсутствие geometry/context/save mutation.

ExecutionTools регистрирует пять fixed facades по experimental flags и server
mode: activate_document, open_working_copy, verify_working_copy_open,
execute_owned_translation, verify_owned_translation. Все недоступны в read-only.
Общий ReferenceGate/deadline, один IPC lane и private recipe.prepare envelopes;
CAD leases потребляются до публикации команды, disk ownership остаётся отдельно.
Unknown/cancellation/lost reply никогда не вызывает replay/inverse/Undo; claimed
request сохраняется. Verifier не исполняет геометрию. Native verifier отвечает
за authoritative journal и снятие только matching fence после независимых proofs.

Проверки: 159 .NET tests passed, 0 failed, 0 skipped; четыре независимых STDIO
clients, точные схемы и SDK/Python private envelopes/receipt parity. 312 Framework
synthetic checks, 78 actual artifact cases (65 accepted, 13 rejected, 0 mismatches)
и Inspector CLI прошли. Native oracle включает original execution receipts,
unknown receipts и bool/unfenced/SHA/state tamper variants. Evidence:
evidence/K5-owned/final/sdk-verification.json, sdk-tests.trx,
native-compatibility.json; live/readonly-live-preflight.json.
В ходе полного запуска устранена гонка synthetic cancellation test: deadline
для отмены после claim отделён от короткого timeout test. Production IPC не менялся.

До закрытия К5 нужны clean checkout/CI нового блока и новый owned live scenario
без повторения P2.29 OPEN/старых intents. Live adapter switch принимается
пользователем по готовому review package и rollback, как требует принятый план.
К6–К11 остаются полным последующим объёмом общего MCP v1.
