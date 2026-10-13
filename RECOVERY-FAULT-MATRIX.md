# К7 — матрица ошибок исполнителей

Итог блока: fece6ee /draft PR10 qualified. Clean full в work/c7v/evidence/
K7-native-fault-clean passed292 .NET/514 conventional native/171 artifact cases
(0 mismatches), ten external STDIO/Inspector +separate25 native fault cells и
independent audit. Все134 recorded source/binary SHA matched clean files;
evidence/K7-fault-matrix/qualified/clean-hash-audit.json. Четыре exact-head CI
success38354243685/38354422830 baseline и38354243670/38354422888 SDK; actual
SDK log подтвердил включение25 native rows/audit. Полная К7 всё ещё не завершена:
per-executor SDK transport faults, activation-context coverage, дополнительные
partial publication points и accepted checkpoint recovery остаются впереди.
Installed runtime/K6 packages/default config/CAD не менялись. Далее — история.

Qualification update: commitfece6ee отправлен, draft PR10 создан/прикреплён
https://github.com/danro-work/PowerShapeBridge/pull/10 (base PR9). Local full SDK
passed292 .NET/514 conventional native/171 artifact cases (0 mismatch), ten
external STDIO/Inspector и separate25 native fault rows+Python audit.
Evidence: evidence/K7-fault-matrix/sdk-full. Clean fece6ee выполняется в
work/c7v/evidence/K7-native-fault-clean; exact-head CI выполняется. Activation
context faults также требуют отдельного покрытия; эти25 cells касаются четырёх
OWNED executors, не всех context-changing commands. К7 не завершён.

Первый блок: native core faults, source native/legacy adapter0.33.1-dev,
SDK0.37.2-dev. Ветка feat/recovery-fault-matrix от проверенного PR9/e6cc4e2.
Runtime и immutable K6 package0.33.0/SDK0.36.3 не изменены.

Внутренний OwnedOpenVerification.Verify(root,args,contextFactory) позволяет
испытать обычный OPEN с fake retained context. Публичный Run(requestId,args)
по-прежнему использует Program.Root и штатный COM context; девять параметров,
проверки исходных receipts, journal/claim, protected bytes и postconditions
сохранены. EntitySets.Receipt получил внутренний overload с explicit root;
прежний wrapper использует Program.Root. Нового MCP tool/аргумента нет.

RecoveryFaultTests запускается только отдельной CLI --recovery-fault-test с
явным .recovery-fault-fixture marker в stage. Каждый context является fake;
геометрия/сохранённые данные — собственные неприменимые в CAD opaque fixtures.
CLI не вызывает COM. В обычный --self-test suite не добавлен, чтобы его файлы
не вмешивались в прежний native artifact oracle.

| Исполнитель | Контроль | Lost return после fake effect | Lost response | Partial journal | Changed process | Non-success |
|---|---|---|---|---|---|---|
| translate_owned_solid | Fresh native verifier | После изменения bounds/COG | Raw reply удержан unpublished | Return publication прервана marker | Process snapshot/listing changed | Коды0 и−1 |
| save_owned_document_as | Fresh native verifier | После записи собственного result | То же | То же | То же | Ack без result file, void API |
| open_working_copy | Fresh native verifier через новый test seam | После добавления fake окна | То же | То же | То же | Ack без нового окна, void API |
| open_owned_saved_result | Fresh native verifier/new binding | После добавления fake окна | То же | То же | То же | Ack без нового окна, void API |

Всего25 cells: четыре controls,20 основных fault cells и отдельный translation−1.
Во всех сценариях — один вызов проверяемой fake команды. Повтор того же intent
явно проверяет существующий guard: нового вызова нет. Во всех faults verifier
не выдаёт success, original journal bytes/claim сохраняются; mutation fence
сохраняется для translation/SaveAs. OPEN использует persistent one-use claim,
без выдуманной geometry fence. Source/checkpoint/working saved hashes сохранены.
При missing return после наблюдаемого fake effect outcome остаётся unknown:
одних совпавших postconditions недостаточно. Lost response действительно
недоступен operation-specific verifier; его raw bytes сохранены как unpublished,
helper не публикует их заново. Числовой returnCode не приписан void APIs.

Сохраняются reports/recovery-fault-matrix.json, original plans/journals/claims/
fences и raw execution/read receipts в stage. Python audit_recovery_fault_matrix.py
независимо читает цепочку журналов, SHA plan/claim/saved bytes, оригинальный
reply, отсутствие fabricated return и marker при partial publication. Он
проверяет все25 cells, count1 и запрет duplicate intent. Это native core coverage,
не общая приёмка К7 и не live CAD evidence.

Воспроизводимый runner: verify-recovery-faults.ps1 -PythonPath <exe>
-StageDirectory <новый путь внутри work> -EvidenceDirectory <path>.
verify-sdk.ps1 добавляет отдельный stage NativeStageDirectory+'-faults' после
прежних native/oracle/STDIO checks. Marker и report запрещают смешение suite
с installed runtime. Никакого deploy/restart/переключения .codex config.

Проверено:25 native cells и independent artifact audit passed в work/kf6 и
work/kfv; evidence/K7-fault-matrix/qualified. Conventional native regression514
passed отдельно в kf6. CLI без isolated marker refused(exit3), ни fixtures, ни
COM не создавались; marker-refusal.json. Full SDK выполняется,292 .NET passed;
clean checkout и CI ещё впереди.

Диагностика собственного harness сохранена: первый build отказал при разной
metadata версии worker/legacy adapter (согласованы0.33.1); один fixture дважды
публиковал SaveAs response (Proof вызывается до его скрытия); обычному OPEN
требовался complete state/timestamp listing receipt вместо bare fixture listing.
Исправлены только fixture/setup; production guards не ослаблены. Intermediate
stages kf/kf2...kf5 не принимались за successful qualification и не стирались.

Остаток обязательного объёма К7: SDK per-executor transport timeout/cancel/lost
wire reply, остальные точки partial publication/changed context, separately
accepted checkpoint recovery и live qualification. Нельзя принимать таблицу
native core как доказательство этих отсутствующих испытаний. Для новых К8/К9
executors recovery matrix расширять отдельно.
