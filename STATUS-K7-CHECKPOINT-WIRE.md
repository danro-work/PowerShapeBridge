# К7 — сквозная цепочка checkpoint через MCP

Source SDK0.38.1-dev/native0.33.5-dev, installed0.33.3-dev unchanged.
Новая квалификация проверяет весь путь official SDK STDIO → файловая очередь →
native worker → filesystem recovery → guarded OPEN → independent verifier →
current document binding. CAD-контекст испытательный; COM/PowerShape не вызываются.

CheckpointRecoveryWireFixture запускается только явными CLI --checkpoint-wire-seed
и --checkpoint-wire-request с отдельным marker. Произвольные recipes запрещены,
envelope/parameters проходят обычный QueueOperations.ValidateRequest. Исполняются
настоящие CheckpointRecovery, WorkingCopyOpen, OwnedOpenVerification и
DocumentBindings; свежие read receipts получаются отдельными queue requests.
Состояние fake context сохраняется отдельными immutable файлами по последовательности.

Семь сценариев:

- control: новая копия, один OPEN, отдельные fresh reads/verifier и current binding;
- lost_return_after_effect: fake OPEN изменил контекст, native возврат потерян;
- no_effect: возврат без ожидаемого нового окна;
- changed_process: process изменился во время OPEN;
- partial_open_journal: writer.lock и частичный журнал сохраняются;
- wrong_verify_model: после OPEN активна другая собственная модель;
- lost_reply: native OPEN завершён, оригинальный ответ остаётся unpublished,
  SDK timeout не выдаёт разрешение и не повторяет OPEN.

Для каждой строки consumed recovery intent и старые OPEN handles отклоняются
до queue publication. Ровно один fake OPEN, старый unknown journal/claim и protected
bytes остаются. Новые file identities независимы. Только control получает binding.
Read-only catalog не содержит filesystem/CAD mutation tools и не меняет файлы.

evidence/K7-checkpoint-wire/final: семь real STDIO/native scenarios passed.
wire-audit.json: независимо проверены413 hashes/56 actual queue requests,
original receipts, materialization proof, ownership/source lineage, journal chains,
OPEN claims и current binding. Artifact inventory сохранён отдельно для каждого
сценария. Staging work/K7-checkpoint-wire-final не развёртывать в runtime.

Два первых диагностических stage сохранены: first — File.Replace отказал при
записи fake context; qualified — неверная fault injection меняла ID той же модели
вместо выбора другого документа. Исправлены только fixture persistence/injection;
production verifier не ослаблен. Старые OPEN не повторялись.

verify-checkpoint-recovered-open.ps1 теперь проверяет отказ обоих wire CLI без
marker, семь сценариев и независимый аудит; вызван существующим verify-sdk.ps1/CI.
Full qualification завершилась PASS: evidence/K7-checkpoint-wire/sdk-full,
native stage work/K7-checkpoint-wire-sdk-full, session91892.368 .NET/531 native/
175 cases137 accepted0 mismatch/32 SDK faults/25 native executor faults/20 activation/
40 legacy STDIO/16 checkpoint+actual SDK-native creation/6 recovered OPEN proofs/
7 full wire cases413 hashes56 requests/Inspector passed. Все163 recorded hashes
сверены,0 mismatch. Native entrypoint иactual staged panel/worker SHA отдельно
зафиксированы в native-extra-hashes.json.
PR22/source7036c016 four Windows CI passed:38404008223/38403998083 baseline,
38404008280/38403997975 SDK.

Граница: real CAD/topology/live checkpoint acceptance не выполнены.
Остаются reparse/remaining partial-publication cases и отдельно согласованный
живой сценарий. К7 в целом и К8–К11 не завершены. Deployment/default MCP не менялись.
