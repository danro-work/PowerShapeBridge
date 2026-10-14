# К8 — guarded export executor на собственном стенде

Внутренний OwnedGeometryExport реализует held guards → immutable intent/fence/claim
→ явное добавление SOLID/SURFACE → один DMT export → held file/parser/SHA → один
проверяемый ClearOwnSelection. При unknown немедленно останавливается без replay,
cleanup или fence release. NoExportFence gate включён в другие owned mutation paths.
MCP/queue/production COM adapter не добавлены; current runtime/CAD не менялись.

Qualified matrix:26 сценариев/595 independently audited file hashes,
work/K8-geometry-export-executor-qualified и evidence/K8-geometry-export-executor/qualified.
Проверены mixed targets, active levels/name safety, drift mapping/context/settings,
mutable settings alias, refused translator/tolerance, busy executor, claim-time
rejection evidence, selection/export/clear unknown, malformed/missing/colliding output,
exact callback counts, prefix selection, journal/proof/owner linkage и no replay.
CLI без marker отказывает до создания стенда. Framework x64 build без warnings.
Регрессии затронутых guards:15 rotation preparations/203 hashes,
24 batch rotations/577 hashes,30 export targets/478 hashes — PASS.
Полная native регрессия прошла531 checks без подключения к PowerShape:
evidence/K8-geometry-export-executor/native-regression.txt.

GitHub publication этого блока ещё не выполнена. Изменения находятся локально
в feat/owned-geometry-export-executor. Последующий read-only запрос GitHub был
остановлен до исполнения:automatic approval review не смог завершиться из-за
usage limit. Это review failure; оно не означает заключения о безопасности действия.
Новые операции с Git metadata/network требуют восстановления approval review;
обход проверки не применяется. Подготовка PR и локальная разработка доступны.

Предыдущие непрошедшие стенды сохранены. Исправлены тестовые ожидания replay после
pre-claim refusal и чтение hashes при собственном exclusive test lock. Для quoted
EXPORT введён свой whitelist path вместо legacy unquoted OPEN validation.
Отдельно добавлен источник evidence для rejected_before_change journal transition.

PR36/6c87ace:все четыре Windows CI completed/success:
38482413926,38482414085,38482332622,38482332733. Локальные старые access failures
и успешная focused перепроверка остаются в STATUS-K8-DMT-READER.md.

Ограничения:callback return/mesh syntax/observed context не доказывают native export
settings, unit/frame convention, topology или правильность изменения геометрии.
geometryVerified=false; файл и fence сохраняются. Контракт:OWNED-GEOMETRY-EXPORT.md.
Следующий блок:independent export verification и квалификация native provider,
SDK/transport, затем полный оставшийся К8–К11. К7 live approval пока не получено.
