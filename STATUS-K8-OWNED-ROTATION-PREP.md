# К8 — native подготовка owned rotation packet

SDK0.39.0-dev/native source0.33.5-dev; installed0.33.3 unchanged.
К7 live approval остаётся pending. Готовые K7 native/SDK пакеты не пересобирались.

OwnedRotationPlans — внутренний passive backend, ещё не зарегистрированный
в native queue илиMCP catalog. Нет RotateItem/Exec/selection/save callback.
Prepare проверяет immutable rotation plan/axis/set/receipts иbinding/ownership
в одном удерживаемом контексте. Два guard passes/четыре observations, executor.lock,
readonly checkpoint/working identities, source byte SHA, world workplane, empty
selection, lifetime иexisting mutation fence проверяются до выдачи результата.

Новый ownedrotations/<id>.json закрепляет rotation plan ID/SHA, binding ID/SHA,
copy ID/ownership SHA, working SHA, native observed/window digests иminimum expiry.
executionEnabled=false; journal толькоprepared. Native иPython observation digest
encoding остаются разными: held-context guard использует native digest/immutable
source SHA, ане принимает Python digest заnative digest.

15 native fixtures прошли: controls single SOLID/mixed40/21/59, altered axis,
fractional angle, wrong targets/method/requirements, expiry/SHA, fenced copy,
wrong workplane/document, late context, checkpoint protection иbusy executor.
Независимый аудит203 hashes, packet/owner/binding lineage иprepared journals passed.
evidence/K8-owned-rotation-plans/mixed; stage work/K8-owned-rotation-mixed.

Диагностика первых stage сохранена: Framework compiler требует explicit lambda
в Max, busy-executor fixture должен делать hash snapshot доexclusive lock.
Исправлены compiler compatibility ипорядок fixture; guard не ослаблялся.

verify-owned-rotation-plans.ps1 требует fresh staging/explicit marker, проверяет
refusal безmarker иполный native matrix/audit. Добавлен кverify-sdk.ps1/Windows CI.
Full verification session19181 completed PASS в evidence/K8-owned-rotation-plans/sdk-full,
native stage work/K8-owned-rotation-sdk-full:403 .NET/531 native/175 compatibility
137 accepted0 mismatch/32 SDK faults/25 native faults/20 activation/40 legacy STDIO/
22 checkpoint/6 recovered OPEN/7 checkpoint wire/6 filesystem wire/15 owned rotation
cases203 hashes/rotation STDIO/Inspector passed. Recorded hashes сверены отдельно.

PR25/source dab9a6bf four CI passed:38436880977/38436872652 baseline,
38436880594/38436872343 SDK. Native runtime/default config/CAD не менялись.

Далее SDK scoped packet/transport wiring сactual native proof compatibility,
then native guarded rotation executor/independent verifier/fault matrix иsaved
result/reopen qualification. Standalone passive packet не разрешает будущий edit.
Native sign/frame/SAFEARRAY иfull arbitrary-angle geometry verification ещё не
квалифицированы. К7/К8 ивесь v1 доК11 не завершены.
