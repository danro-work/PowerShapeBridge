# К8 — подготовка переноса SOLID на другой слой

Source SDK0.39.8/nativePython0.34.5, установленный runtime0.33.5 без изменений.
`OwnedLevelMovePlans.Check` реализует внутренние prepare/revalidate без вызовов
изменения CAD. Документ: K8-OWNED-LEVEL-MOVE-CONTRACT.md.

Строгие параметры, singleSOLID, exact named target0–997, receipt/set/binding
SHA и общий короткий expiry, immutable detached args, once prepared journal,
удерживаемые proof files, ownership/checkpoint, working byte pin/SHA,
absence of mutation/export fence. Fresh native full slots/filters до и после
публикации; повторная проверка не меняет journal или файлы.
Текущее выделение допускается в observation: будущий явный executor обязан
учесть документированный SetItemsLevel selection clear. Сейчас callback
изменения отсутствует, executionEnabled/nativeProviderQualified=false.

40 isolated native cases passed, independent audit1177hashchecks passed:
10 controls (including held writer, nonempty selection, double flags, detached
caller args, held receipt) и30 initial refusals. Дополнительно revalidation
при tampered packet/workingbytes/levels и replay отказала; control validation
passed. Evidence: work/K8-level-move-plan-second/reports/owned-level-move-plan-tests.json,
evidence/K8-level-move-plan-second/native-audit.json.
Первый compile failure был пропущенным brace в fixture; первый запуск выявил
ошибку fixture Hashes при удержании собственного exclusive executor.lock.
Порядок fixture snapshots исправлен, protection implementation сохранён.
Independent auditor исправлен под фактический owned Working_<id>.psmodel:
он проверяет точный путь по ownership, а не вымышленное имя Working.psmodel.

Новый marker-guarded runner зарегистрирован в verify-sdk с source hashes.
Общая regression session51878 завершилась exit0/PASS:678.NET без пропусков,
531native baseline,175actual compatibility без расхождений,29native levels,
40level move cases/1177audit hashes и Inspector. Итоговый marker
`evidence/K8-level-move-sdk-final/sdk-verification.json`; source hashes сверены
перед публикацией. Проверка выполнена без CAD и deployment.
Нет public MCP prepare/execute/verify или native production runner, нового
CAD connection/deployment/mouse/selection/save. Полная К8–К11 не достигнута.
