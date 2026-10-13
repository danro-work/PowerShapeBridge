# К7 — отдельные записи подтверждённого исхода

Final qualification: SDK0.37.2 /commit5a46c88 прошёл clean full в work/c7u/
evidence/K7-resolution-final-clean:292 .NET/514 native/171 artifact cases
(133 accepted,0 mismatch), ten external STDIO scenarios/Inspector. Все128
recorded source/binary SHA совпали с clean files; evidence/K7-recovery/resolution/
final-clean-hash-audit.json. Все четыре exact-head CI passed38349844154/
38349847213 baseline и38349844216/38349847228 SDK; actual SDK log подтверждён,
включая SaveAs25/7 и saved-reopen28/7 resolution/history modes. Draft PR9 attached.
Resolution/history block offline qualified; полный К7 всё ещё НЕ завершён.
Не выполнены full executor fault matrix, accepted checkpoint recovery и liveК6.
Installed runtime/native source/publishedK6/default config/CAD этим блоком не менялись.
Дальнейшие записи ниже — история разработки/диагностики, не текущие ограничения.

Последняя правка: clean6488d2c run отказал в прежнем SaveAs route из-за sharing
lock на original response; evidence work/c7t/evidence/K7-resolution-clean retained.
Все четыре CI6488d2c passed38349096739/38349176382 baseline и38349096738/
38349176171 SDK. Source SDK0.37.2-dev теперь продолжает poll того же original
response при Win32 sharing/lock32/33 в ИСХОДНОМ budget. Request/claim/команда не
повторяются; cancellation/timeout остаются unknown, malformed JSON/identity
по-прежнему immediate unknown. После успешного чтения повторно проверяются
cancel/deadline. Controlled release/timeout/cancel receipt-lock regression и
прежние IPC/deadline cases12 passed; receipt и single claimed request сохраняются.
Это production read-wait fix, не новый CAD executor. Далее final clean/full/CI.

Qualification update: commit6488d2c сохранён и отправлен; draft stacked PR9
https://github.com/danro-work/PowerShapeBridge/pull/9 (base PR8).
Полный локальный SDK run passed289 .NET/514 native/171 artifacts (133 accepted,
0 mismatches), ten external STDIO scenarios (8 scripts +2 resolution modes),
Inspector. Evidence: evidence/K7-resolution-qualified/sdk-verification.json.
Final source/annotations/test helper включены в этот run. Clean6488d2c в
work/c7t/evidence/K7-resolution-clean выполняется; exact-head CI выполняется.
Ранее usage-limit failure и незакоммиченный статус ниже относятся к истории.

Продолжение: штатный reviewer снова допустил тот же test, без обхода ограничения.
External saved-reopen resolution28/7 passed (actual synthetic native artifacts /
fake fresh read-verifier replies), new binding и readonly history checked; command
journal intact. Source final annotations четырёх verifiers теперь ReadOnly=false:
они пишут journal/records, хотя CAD-команд не выполняют. Separate read-only server
их по-прежнему не включает. Это уточнение интерфейса, не разрешение CAD mutation.
Первый новый full SDK run поймал transient sharing lock в старом PreparedRoutes
test. Test-only Fixture.ReadClaimedRequest теперь до5s повторяет только чтение
уже захваченного envelope при Win32 sharing/lock errors32/33; claim/команду не
повторяет. Контролируемый exclusive-lock regression +prepared routes8 passed.
Проверка fresh NativeStage перенесена в начало verify-sdk; старый work/r1 не
используется. На новом work/rj весь289 .NET passed, общий SDK/native/ten STDIO/
Inspector regression выполняется: evidence/K7-resolution-qualified.
Далее commit/push/draft stacked PR, clean checkout и exact-head CI. Native source/
installed runtime/published K6 package/default config не менялись. К7 не завершён.
Ниже сохранён журнал первоначального блока и временного ограничения допуска.

13 октября 2026. Source SDK0.37.1-dev; ветка feat/recovery-resolution-journal
от e3d1304 (PR8). Это следующий блок К7, ещё без commit/PR/clean/CI qualification.
Установленный runtime, опубликованный пакет К6 и default MCP config не менялись.

Когда operation-specific native verifier проверил исход по новым наблюдениям,
SDK может сохранить отдельную запись решения. Она отвечает на вопрос «какой
исход был доказан тогда?» и помогает найти результат после потери ответа.
Историческая запись не выдаёт текущую ссылку на документ и не разрешает повтор
команды, снятие блокировки или восстановление из checkpoint.

Включение: recoveryResolutionExperimental=true и recoveryInspectionExperimental=true.
Профиль config/general-recovery-resolution.json: full/read-only28/7; новых tools
нет. В режиме read-only writer всегда отключён. Без нового флага верификаторы
сохраняют прежнее поведение. Регистрация reader остаётся RecoveryTools.Create
через ToolCatalog; отдельный writer не является общедоступной MCP-командой.

OutcomeResolutionJournal.Record вызывается после существующей специализированной
проверки proof для verify_working_copy_open, verify_owned_translation,
verify_owned_save_as и verify_owned_saved_reopen. SDK закрепляет raw verifier
response, исходную операцию/SHA, terminal journal/plan/owner и authoritative return.
Коды translation0/−1 или отсутствие return не могут дать запись успеха даже при
terminal succeeded_verified. Saved bytes/checkpoint сверяет RecoveryInspection;
это не новое доказательство физической identity или полной топологии — их scope
определяет исходный native verifier.

Fresh inspection/listing должны быть после command_returned, до verifier response
и не старше300s, в исходном TTL. ID документа/окно и processId/start/session
должны совпадать. Snapshot identity/digest закрепляются в записи. Истечение
TTL перед публикацией запрещает запись. Исходные команды, claims, fences и
events эта функция не меняет и CAD/IPC не вызывает.

Файл: recoveryresolutions/<operationId>/<verificationRequestId>.json, CreateNew,
flush; существующий файл допускается только с тем же JSON-содержимым. Другую или
частично записанную запись не заменять и не удалять автоматически. Исходный
command journal сохранён отдельно. Файл решения не является capability.
Outcome: succeeded_verified_at_observation; executionAuthorized/retryAllowed/
checkpointRestoreAuthorized=false. Reply содержит outcomeResolution с ID и SHA.

Read-only inspect_operation_recovery добавляет resolutionHistory. Он сверяет
schema, raw verifier/source/read receipt SHA, snapshot identity/digest и journal
tail; повреждение/partial record -> resolution_history_unavailable. Даже если
история корректна, основной outcome остаётся not_resolved_by_recorded_inspection,
currentContextChecked=false. Отмена чтения/записи сохраняет запрет исполнения.
Native verifier может завершиться, а SDK-публикация решения или выдача lease — нет;
это частичный результат проверки, требующий чтения исходных файлов, не replay.

Проверено до ограничения доступа:

- 26 новых .NET cases (four-operation records, no return/codes, raw tamper,
  wrong document/process, old reads, changed files, writer marker, cancellation,
  expiry before publication, immutable/history checks, flag gates, actual SDK
  pipe success/conflicting record). Полный набор288 .NET passed.
- External SaveAs resolution25/readonly7 на actual Framework synthetic artifacts
  и fake fresh read/verifier replies passed: raw record SHA совпал, readonly
  history прочитан, старый command journal не менялся. Никакого live CAD.
- Один первый pipe test отказал из-за missing ownership SHA в fake receipt;
  fixture исправлен, production guard не ослаблен. Два независимых pipe cases
  после исправления passed.
- Evidence: evidence/K7-recovery/resolution/resolution-full-dotnet.trx,
  resolution-qualified.trx, resolution-reader-integrated.trx,
  sdk-save-resolution-stdio.json. В первой targeted qualification было25 cases;
  case expiry at publication добавлен перед полным288 run.

Не проверено: новый external saved-reopen resolution/history path; полный SDK
regression с двумя новыми режимами скриптов (8 прежних +2 дополнительных calls),
clean checkout, final source/binary hashes, Git commit/push/stacked PR и exact-head
CI. Код/скрипты в workspace сохраняются незакоммиченными. Print-only правка
Python output25/7 выполнена после passed external SaveAs run; этот вывод тогда
ошибочно показывал25/6, а assertions/report фактически проверили25/7.

Следующий test был остановлен ДО выполнения: запуск Python
src/test_sdk_reopen_verification_stdio.py с --recovery-resolution,
--stage work/c7s/work/n1, --host src/PowerShapeMcp.Host/bin/Release/net10.0/
PowerShapeMcp.Host.dll. Automatic approval review failed: usage limit;
review сообщил попробовать после4:29AM. Часовой пояс/принадлежность этого
времени reviewer не указал, поэтому не принимать его за гарантированное время
возобновления. Это failure проверки допуска, а не unsafe verdict. Не выполнять
тот же тест/child host другим способом в обход проверки. После восстановления
доступа использовать require_escalated; старый fixture считать только source
artifacts, новые candidate/listing/verifier timestamps выпускает fake panel.

Далее: завершить offline qualification, затем полноценную fault matrix К7
(lost response, timeout, non-success, partial publication, changed live context)
и отдельно принятый checkpoint recovery scenario. Новый журнал не подменяет
требования этого объёма; К6–К11 ещё не достигнуты.

Autodesk Product Help2023/en_US повторно проверен: [Recovering a model](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-3EF761A5-A110-4F0A-8D5F-5AA91EB4141E)
описывает UI recovery после crash и зависимость Parasolid recovery от autosave.
Этот механизм не доказывает исход Bridge command; deliberate crash, диалоги,
настройки autosave и сохранение восстановленной модели этим блоком не выполнялись.
