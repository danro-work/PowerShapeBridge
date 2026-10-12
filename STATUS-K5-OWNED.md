# К5 — исполнение и независимая проверка, выполняется

12 октября 2026. SDK 0.35.0-dev: full general/read-only catalog 21/6.
Native runtime 0.31.0 не менялся. К5 не завершён; default live switch
не выполнялся. Новый SDK прошёл passive read-only pilot на существующей панели:
свежие process/queue/deployment checks, list_documents, inspect_active_model и
inspect_entity_candidates. Open/activation/save/geometry/screenshot не выполнялись.

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
