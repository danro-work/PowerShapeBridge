# К7 — подготовка новой копии из checkpoint

Source SDK0.38.0-dev/native+legacy0.33.5-dev. Installed native0.33.3-dev,
default MCP config и CAD не менялись. Живое восстановление/OPEN не запускалось.

Новые general-only tools:

- prepare_checkpoint_recovery(operationId, operationPlanSha256) pin-ит один owned
  origin plan, ownership и protected checkpoint; возвращает client-scoped intent.
- create_checkpoint_working_copy(checkpointRecoveryHandle) потребляет intent до
  IPC и создаёт NEW schema2 working copy/readonly checkpoint из сохранённых байтов.

Регистрация: CheckpointRecoveryTools.Create(session) в ToolCatalog.Create.
Обязательные descriptions/exact token schemas, no arbitrary paths, feature flag
checkpointRecoveryExperimental с workingCopies/recoveryInspection dependencies.
Режим чтения не содержит этих primitives. Native queue принимает только две
fixed recipes после feature/config/parameter validation. Python legacy adapter
согласован по native metadata/config policy; новые tools находятся в SDK server.

Native global executor lock сериализует файловую подготовку; OwnCopies.Hold
проверяет physical identity, fixed roles, readonly checkpoint, hash/size и отсутствие
alias/reparse/hard-link. Existing original journal/claim/fence сохраняются.
Recovery intent и materialization plan/once/result proof имеют собственные paths;
expired/future/foreign intent отклоняется. Partial copy после claim сохраняется
как unknown, новый handle не выдаётся. Copy creation не разрешает исходный
unknown. Existing guarded OPEN/verifier/new binding — отдельный следующий шаг.

SDK проверяет immutable intent/origin/owner/checkpoint и creation/once/raw result
proof, copied bytes/roles/declared independent identities. Native physical identity
guard отдельно проверяется реальным filesystem кодом. Saved working-copy lease
выдаётся только после проверенного materialization result; CAD binding отсутствует.
Late deadline/cancel revokes только вновь выданный handle, а не чужие leases.

Проверено18 .NET cases: client scope/TTL/one-use, changed dependencies/materialized
proofs, read-only/feature gating/extra paths, actual SDK create route, timeout/lost
reply/replay rejection и cancellation before publication. Все passed.
Native16 cases+independent249-hash audit passed: controls/changed working bytes,
expired/future intents, checkpoint protection/bytes/replacement/hard-link,
owner/plan tamper, busy executor/checkpoint, partial after claim/copy и foreign ID.
CLI marker refusal проверен без fixtures/COM. Prepared-only seed используется
для отдельного actual SDK/native filesystem integration через --mcp-operation.
Два actual queue requests подготовили intent и новую копию; original artifacts
unchanged, bytes совпали, distinct native file identities и replay refusal.
Read-only catalog не содержит новых tools. Доказательства в
evidence/K7-checkpoint-recovery/sdk-routes, native-qualified и stdio.

Full qualification прошла: evidence/K7-checkpoint-recovery/sdk-full,
native stage work/K7-checkpoint-sdk-native.361 .NET/531 conventional native/
175 artifact cases137 accepted0 mismatch/32 SDK transport rows+681 hashes/four
negative checks/25 native executor faults/20 activation/40 legacy STDIO/16 checkpoint
cases+249-hash audit/actual SDK-native disk creation/Inspector passed. Все156
recorded source/binary hashes matched. Initial native compile diagnostic CS0136
сохранён, исправлено имя lambda variable для Framework compiler.

Оставшийся объём CHECKPOINT-RECOVERY-CONTRACT.md: additional fault coverage,
guarded OPEN recovered copy/independent verification/current binding и separately
accepted live checkpoint scenario. Disk copy сама по себе не является CAD/topology
qualification. Остальные К7 publication cases и К8–К11 обязательны.
PR19/sourcefe2cf8c four CI passed38393634228/38393486124 baseline,
38393634042/38393486387 SDK.
