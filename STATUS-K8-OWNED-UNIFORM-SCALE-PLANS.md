# К8 — protected owned uniform scale preparation

Реализован internal OwnedUniformScalePlans с exact entity-set/binding refs и
explicit factor/centre/world/raw/in-place parameters. Никакого native ScaleItem
или command recipe не придумано. DocumentBindings.IContext содержит только
getters; public queue/MCP route и COM Run не добавлены.

PASS:32 fake-context сценария,6 controls/26 refusals; независимый Python audit
проверил395 SHA с complete file inventories, exact packet schema, dependencies,
owner/source/checkpoint, parameter alias и prepared-only journal.
Stage:work/K8-owned-uniform-scale-final-qualified.
Evidence:evidence/K8-owned-uniform-scale-plans/final-qualified/native-audit.json.
Без fixture marker CLI отказывает до создания стенда; panel/worker/launcher
собраны в staging x64/Framework, установленный runtime не менялся.
Runner/auditor/source manifest добавлены в verify-sdk для следующего CI.

В первых прогонов выявлены две тестовые/guard ошибки: текущие working bytes
ошибочно предполагались равными исторической версии, а общая OwnershipLease
использует FileShare.ReadWrite и лишь обнаруживает drift при повторном SHA.
Сценарии уточнены: законная смена bytes до lease принимается; новый дополнительный
FileShare.Read handle запрещает запись на всё время Prepare, lease.Recheck
проверяет окно получения handle. Попытка записи во время getter отказывает;
immutable packet/set/journal также удерживаются против записи. Старые неуспешные
stages qualified/final и успешный pinned сохранены, не выданы за итоговую приёмку.
Изменение saved bytes само по себе не доказывает согласованность с unsaved CAD.

Контракт:K8-OWNED-UNIFORM-SCALE-PLAN-CONTRACT.md. Во время второго guard может
появиться unresolved fence:результат rejected, ранее созданные packet/journal
сохраняются, executionEnabled остаётся false. Проверяются и file, и directory
в месте mutation-fence. Квалифицированный native measurement/provider/executor,
mapping после primitive conversion, SDK stores/wire, independent geometry,
saved-result/reopen и recovery/transport faults ещё нужны.

Native общая регрессия завершилась с exit1 на предыдущем pinned build
(session35101):worker сообщил «Отказано в доступе по данному пути», а wrapper
остановился Native regression failed. Конкретный путь/операция не установлены:
SelfTests catch печатает только Message. Нельзя объявлять общий PASS или
приписывать ошибку CAD; нужна диагностика exception stack на isolated fixtures.
Уточнение диагностики:stack установил File.InternalReplace → PublishPanelStatus.
Отдельный probe с корректным null backup получил Win32 32 при held reader и
Win32 5 даже без reader. Первый PowerShell probe передал пустую строку вместо
null и получил87; он не является доказательством ошибки File.Replace.
Evidence:native-diagnostic/stderr.txt,replace-probe-correct-null.json,replace-access.json.
ReadOnly attribute не установлен. Утверждение, что AccessDenied вызван именно
held reader, опровергнуто. Production publisher/atomicity/error gates не ослаблены.
Диагностический CLI теперь печатает Exception.ToString на stderr.

Та же diagnostic assembly проверена разрешённым user-context запуском:
PASS531 checks/no PowerShape, workerExitCode0, session59639 terminal.
Evidence:native-user-context/terminal-result.json,stdout.txt,stderr.txt.
Сборка включает final scale guard; общий PASS относится к этому запуску,
не к старым sandbox failures. Microsoft ReplaceFileW требует WRITE_DAC при
сохранении ACL; конкретное restricted-token право дополнительно не измерялось.
https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-replacefilew
Успех32 focused cases и531 native regression не являются общим CI/SDK acceptance
или доказательством полного К8/К11. SDK scale store26 tests/64 affected regression
passed:STATUS-K8-SCALE-SDK-STORE.md. Native route/live qualification ещё нужны.
Autodesk Product Help2023 Uniform scaling повторно проверен en_US:
GUID-F1C47DA1-CD1D-4DA1-AC0E-9E85B95BC7D5.
CAD не подключался, никаких мутаций/сохранений/fence releases не выполнялось.
К7 live approval pending, READY packages/runtime сохранены. Текущие изменения
локальные. Automatic approval review разрешила user-context regression;
предыдущая quota failure больше не считается общим блокером без новой проверки.
GitHub публикация ещё не выполнена; обхода не было. Полный К8–К11 сохранён.
