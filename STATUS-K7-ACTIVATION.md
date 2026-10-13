# К7 — контекст активации и отмена после возврата

Source native/legacy0.33.4-dev, SDK0.37.4-dev. Installed native0.33.3-dev,
панель, модели и default MCP configuration не менялись.

DocumentSession.Activate получил internal root/context seam. Public wrapper
по-прежнему использует Program.Root, штатный Attach/config, double stable native
listing и один `Sel_Window @<integer> Front`. Сохранены lease SHA/expiry, process,
model/window identity, одно окно целевого документа, повторный guard до команды,
selected model confirmation, saved bytes, executor lock и replay guard. Новый
request-ID guard отклоняет non-hex/non32 token до любых filesystem/COM действий.
Context освобождается при каждом выходе из guard. Схема MCP tool не менялась.

Native20-row suite проверяет actual activation core на fake contexts/opaque files:
control и already-active no-op; stale process/target, changed immediately before
command, process/model/window/extra-window после команды, selected-model mismatch,
unavailable active document, no effect, lost return after effect, changed saved
bytes, expired lease, tampered listing, invalid ID/argument и interrupted journal
publication до команды/после возврата. Duplicate request не достигает factory
или команды, исходные artifacts сохраняются. Own B.psmodel изменяется лишь
в явно отрицательном changed-bytes fixture; никаких настоящих CAD-файлов нет.

CLI --activation-fault-test требует собственного .activation-fault-fixture marker,
не опубликован как MCP tool. verify-activation-faults.ps1 проверяет marker refusal,
новый stage,20 rows и independent Python audit110 artifact hashes. Без marker
CLI завершился exit3 без создания fixtures/COM. Первое fixture execution было
отклонено из-за неверной test-only сериализации Windows list; typed test snapshot
исправлен, diagnostic stage сохранён.

Журнал activation пока legacy `<id>.json`/`.once`, не NativeJournal append-only.
При прерывании completion сохранились original before_activate journal и `.tmp`;
native publication exception не выдаётся за success. Этот блок не добавляет
recovery-reader authority для legacy activation и не объявляет К7 завершённым.

SDK matrix дополнена четырьмя returned-cancellation rows для translation/ordinary
OPEN: local-only и explicit notification после synthetic native return. Теперь32
combined rows; independent archived audit681 hashes и four negative auditor
checks passed в evidence/K7-activation/failed-run-transport-audit.json. Файл
называется failed-run, потому что общий первый SDK прогон остановился на другом
тесте:329/330 passed, IPC WaitingCallsIncludeQueueTimeAndObserveCancellation
не увидел request в своём0.4s budget на фоне параллельных fsync/archive fixtures.
Его исходные budgets/assertions и production timeout не менялись: IpcTests
перенесён в непараллельную timing collection, все12 IPC tests прошли отдельно.

Full qualification после этого изменения прошла в
evidence/K7-activation/sdk-qualified, native stage
work/K7-activation-sdk-qualified-20261013:330 .NET/531 conventional native/175
artifacts137 accepted0 mismatch/32 SDK rows+681 archived hashes/four negative
auditor cases/25 native fault cells+audit/20 activation rows+110 hashes/STDIO/
Inspector. Все143 recorded source/binary SHA сверены, hash-audit.json. Предыдущий
partial run сохранён отдельно. После этого — source commit/PR/exact-head CI.

Предыдущий block483a3d6/PR17: four CI passed38388165365/38388159785 baseline,
38388165408/38388159724 SDK. Сохранён actual SDK CI artifact:
evidence/K7-sdk-lost-reply/ci-38388165408, manifest passed,28-row audit599 hashes
и four negative checks подтверждены. Состояние той версии не расширяется до32.

Далее для полной К7: остальные interrupted publication points, legacy activation
recovery observation без выдачи разрешений и отдельно принятый checkpoint recovery
с новым owned working file; сохранять исходный unknown/claim и защиту checkpoint.
Live qualification и последующие К8–К11 остаются обязательными.
