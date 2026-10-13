# К7 — reparse и прерывание создания checkpoint copy

Source SDK0.38.1-dev/native0.33.5-dev; installed native0.33.3-dev unchanged.
Добавлены шесть native сценариев: checkpoint junction, intent junction,
прерывание после записи Working до записи checkpoint, до ownership,
частичный ownership JSON и после завершённого ownership до materialization proof.

Junction создаётся только внутри свежего собственного fixture root; обе
абсолютные папки проверяются до Directory.Move. Сохраняется исходный контент.
Native guard отклоняет reparse до claim; тест проверяет конкретную причину отказа.
Старые origin journal/claim, Working/source/checkpoint bytes сохраняются.

Для точного прерывания копирования добавлен внутренний CreateInterrupted,
общий CreateCore и opt-in callback. Публичный OwnedCopies.Create сохраняет
прежнюю сигнатуру и вызывает core с null callback. Стандартный native queue
не принимает phase/callback arguments. Только явно маркированные fixtures
используют callback. Guards/hash/identity/readonly правила не ослаблены.

Прерывания после claim дают unknown, сохраняют claim и частичные файлы.
Материализация не сертифицируется; новый working handle не выдаётся.
Duplicate consumed intent не создаёт новых файлов. Частичный ownership JSON
сохраняется без ремонта или cleanup.

Native matrix22/22 passed, independent355 hashes audit:
evidence/K7-checkpoint-filesystem-faults/native; stage
work/K7-checkpoint-filesystem-faults. Ранее было16 сценариев.

Шесть тех же отказов проверены official SDK STDIO → очередь → actual native worker
с opt-in injection только в собственном marked fixture. Replay отклоняется
до queue publication. Независимый аудит184 hashes/12 requests passed:
evidence/K7-checkpoint-filesystem-faults/wire иfilesystem-wire-audit.json;
stage work/K7-checkpoint-filesystem-wire. CAD context не используется для команд.

Full verify-sdk session29587 completed PASS: evidence/K7-checkpoint-filesystem-faults/sdk-full,
stage work/K7-checkpoint-filesystem-sdk-full.368 .NET/531 native/175 cases137 accepted
0 mismatch/32 SDK faults/25 native executor faults/20 activation/40 legacy STDIO/
22 checkpoint355 hashes/actual SDK-native creation/6 recovered OPEN/7 wire413 hashes
56 requests/6 filesystem wire184 hashes12 requests/Inspector passed.
Все167 recorded source/binary hashes совпали. Оба новых audit/run включены в общий runner/CI.
Native entrypoint иOwnedCopies добавлены в source hash manifest.

PR23/source81eea863 four CI passed:38407025436/38407014833 baseline,
38407025373/38407014795 SDK.

Fresh passive live read13 октября: native0.33.3, process17304, six windows,
active Test_Block_P2; inspect request992d2baa7b5f4868b832d77d714e8d0b,
one entity. Никаких OPEN/save/selection/capture/close не выполнялось.
Эти IDs не использовать как будущие mutation references: чтение повторяется.

Далее exact-source qualification/CI и отдельный конкретный live checkpoint scenario
с deployment package/backup и новым own OPEN. К7 без этой приёмки не завершён;
К8–К11 остаются обязательными.

Code241bc56d/PR24 now four CI passed38429476459/38429467416 baseline,
38429476559/38429467454 SDK. Actual SDK log confirms new filesystem fault cases/audit.
Qualified package/config/catalog preflight and plan-only prepared;
K7-LIVE-CHECKPOINT-SCENARIO.md records exact source/steps/limits.
Direct human approval requested; no runtime apply/live copy/OPEN performed.
