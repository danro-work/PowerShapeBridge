# К8 — публичные инструменты переноса по слоям

Итог: full session83635 terminal exit0/PASS,15 октября06:50:59UTC.
707.NET/0skips,531native,175compatibility/0mismatches,40plan/1177hashes,
42executor/verifier/1928hashes,5wire/48requests/341hashes,14config/30admission
и MCP Inspector passed. 338source/build hashes сверены. Authoritative marker:
evidence/K8-level-sdk-final-short/sdk-verification.json.
Ранние failure evidence сохранены; записи о живом83635 ниже — история.
Deployment plan без Apply создан: work/K8-level-live-deployment-plan.json.
18package artifacts/21previous config fields verified; added flags только2.
Установка и CAD mutation не выполнялись. Требуется отдельное согласие после CI.

Реализованы C# store/prepare/execute/verify и строгий full-only feature flag,
native recipe/config validation+typed runner. SourceSDK40.0/nativePython34.7,
installed33.5 без изменений. SDK-OWNED-LEVEL-MOVES.md содержит схемы/границы.

Фактический STDIO/native fixture wire прошёл5scenarios/48requests,
independent341hashchecks,14configuration parity и30native admission cases.
Evidence: evidence/K8-level-sdk-wire-final/wire-audit.json.
29 focused C# tests passed: evidence/K8-level-sdk-focused-corrected/level-sdk-tests.trx.
Compile correction добавила namespace StreamClientTransport в тест; один failed
fixture пытался File.ReadAllBytes при уже открытом writer. Native response
сформирован до fixture writer lease, затем проверены реальные store reads;
protection implementation не ослаблялся. Failure evidence сохранена отдельно.

Устранено обнаруженное raw native response mismatch: bridgeRequestId ранее
добавлялся только SDK, теперь сохраняется QueueOperations.Run для level read.
Unknown response сохраняет оригинальный request ID и execution handle;
проверка/подготовка не объявляется возможной CAD mutation. Remaining budget
проверяется до IPC, отрицательный timeout не передаётся в WaitAsync.

Первый общий запуск707tests:706passed, один старый DGK fixture read получил
Windows sharing violation при чтении уже опубликованного request. Report:
evidence/K8-level-sdk-regression-final (terminalFAIL сохранён). Исправлен только
bounded read того же immutable request (HResult32/33, максимум1sec), без нового
handler/command invocation. Focused29new+6DGK=35tests passed:
evidence/K8-level-sdk-dgk-corrected/level-sdk-dgk-tests.trx.
Второй full pipeline63536:707.NET/0skips passed, затем старый geometry-export
fixture превысил Framework MAX_PATH из-за длинного staging prefix. Failure
сохранена в evidence/K8-level-sdk-regression-corrected. Production code не менялся.
Третий pipeline83635 использует короткий staging work/L40 и отдельный evidence
K8-level-sdk-final-short;707.NET/0skips уже passed, остальные проверки выполняются.
Не перезапускать живой процесс и не менять source до terminal/hash gate.

Пассивный фактический preflight прочитал1000slots, сохранил selectedSOLID и7окон;
independent11protected hashes/current working SHA passed. Candidate target:
уже существующий именованный видимый слой1 Surfaces; без активации/создания слоя.
work/K8-level-live-passive-read/independent-audit.json. Реальный setter не вызывался.

Проверен idempotent public verify_working_copy_open по original8482bf2...:
новая SDK connection получает fresh handle уже открытой K7 owncopy7b076...
без нового OPEN и без изменения selected body/original journal. Evidence:
work/K8-level-existing-copy-adoption.json. Это не historical adoption level
execution: последний остаётся отдельным незавершённым требованием v1.

Подготовлен work/qualify-live-level-transfer.py: по умолчанию read/prepare only,
execute требует отдельного recorded approval, свежего evidence directory и
durable once marker до единственного submission. Сценарий: body1 General0 ->
существующий видимый Surfaces1, selection clear declared; no save/newOPEN/inverse.
Скрипт ещё не запущен, только проверен синтаксис. Deployment Apply не разрешён.

Общая regression успешно завершена (см. итог выше). Нет установки/runtime update, живого
SetItemsLevel, selection/save/activation/mouse. К8–К11 не достигнуты.
PR52/a2dec5a all4CI SUCCESS, включая SDK38687957999/38687952121.
