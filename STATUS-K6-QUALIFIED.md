# К6 — SaveAs/reopen технически квалифицирован

13 октября 2026. Условие перехода К6 из принятого плана подтверждено:
один собственный SaveAs, отдельное открытие сохранённой копии, independent
verifiers и новая current-document binding. Пользовательская приёмка общего
v1 относится к К11; цель К7–К11 не сокращается.

Installed native0.33.3-dev, one panel7124; side SDK0.37.4-dev в
work/K6-reopen-path-sdk-runtime. Default Codex MCP config не переключён,
старые immutable K6 packages сохранены. General/read-only catalogs27/6 checked.

Операции: SaveAs59f5a399c14a42b5b6e2c80688d43f09 и
OPEN45ddd3c930f843ca858486ad1c7fe36c — ровно один command_started и один
command_returned каждый, оба native journals succeeded_verified. SaveAs
сначала дал unknown из-за ложного model-ID guard; fresh verifier разрешил
исход без replay. Native prepare46994... остался только на диске после
SDK receipt-ID отказа; повторно не открывался. Actual OPEN относится к новой
own copy d5ed3054cfa145c887e6805bb5dde541.

Последний independent verifier выдал binding065ba2cc965b48b3a889d35df0e4bfbe;
SDK validate_working_document_binding прошёл. После известного успеха fresh
document choice активировал прежний Test_Block_P2. Шесть окон, own result и
Working_d5... оставлены открытыми, модели не закрывались.

Independent audit:34 raw proof/file hashes, persisted claims, original/terminal
evidence, source/result/seed/Working/checkpoint, четыре независимых file identities,
readonly seed/checkpoint, точное совпадение наблюдаемых entity metadata/geometry/
raw units/workplanes без native-ID correspondence. Все71 защищённый файл
совпал; existing saved-file hashes before SaveAs/OPEN совпали. SaveAs/OPEN,
inverse/Undo не повторялись; fence снят только successful verifier.

Доказательства: evidence/K6-reopen-path-fix/live-final-audit.json,
live-qualification/qualification.json и transcript.json, final-live-context.json,
protected-after-live-k6.json. Deployment backups:
work/general-deployment-backup-20261013-070902 и -20261013-074123.

SDK path fix44726bf: full298 .NET/531 native/175 artifact cases137 accepted,
0 mismatches/STDIO/Inspector/25 fault rows+independent audit passed;134 recorded
hashes matched. Four exact-head CI38368557833/38368641420 baseline,
38368557972/38368641460 SDK passed; actual SDK log checked. Draft PR15 attached.
Prerequisite SDK receipt fixe3e1fc5/full296/531/175/25/four CI — draft PR14.
Native model-ID fix341951a/full294/531/175/25/four CI — draft PR13.

Граница: один qualified owned SOLID, world frame, empty selection, доступные
raw units и свойства. Это не полная topology equivalence, не универсальная
приёмка всех типов/классов моделей; broader matrix обязательна в К10.
К7 не завершён: SDK transport/cancellation всех executors, activation-context,
additional partial publication и отдельно принятый checkpoint recovery впереди.
