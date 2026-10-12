# К6 — собственный SaveAs и отдельный reopen, проект контракта

SDK0.36.3-dev: все шесть инструментов таблицы (prepare/execute/verify SaveAs,
prepare/execute/verify saved reopen)
реализованы и проверены offline. Verify выдаёт typed savedResultHandle только
по checked native terminal proof; никакая read reference не становится edit
authorization. Handle закрепляет verificationRequestId/raw SHA, expires до
300s и не продлевается. Prepare создаёт closed readonly seed, новую Working
copy и отдельный readonly checkpoint; ownedReopenHandle сохраняет minimum TTL
source lease. Execute/verify saved reopen реализованы offline; успешный verifier
выдаёт новую current-document binding, old entity IDs не переиспользуются;
live qualification К6 ещё впереди. STATUS-K6-SAVE-AS.md.

Реализация на 12.10.2026: SDK/native SaveAs и saved-byte preparation прошли
offline checks. Native recipes включаются ownedSavesExperimental и отдельным
ownedSavedReopenExperimental; guarded OPEN/new binding проверены только offline.
Установленный runtime не обновлялся; live квалификация К6 ещё отсутствует.
Текущее доказательство и ограничения — STATUS-K6-SAVE-AS.md.

К5 technical pilot пройден; default SDK decision pending. Этот контракт начинает
К6; offline checks не объявляют live SaveAs/reopen квалифицированными.
Исходный объём — MCP-DEVELOPMENT-PLAN.md, строка К6; общий MCP v1 остаётся целью.

## Результат и границы

Сохранить текущее состояние manifest-owned документа в НОВЫЙ собственный result,
с отдельным ReadOnly checkpoint и сохранностью исходных saved bytes. Один
SaveAs; отсутствие файла/return/постусловия означает unknown, без повторного Save,
Undo, inverse, close или автоматического исправления модели. Затем отдельный
новый reopen intent для проверки сохранённых байтов независимым чтением.

Read-only server сохраняет свой прежний каталог: не готовит файловые targets,
не исполняет SaveAs/OPEN и не вызывает journal-appending verifiers. Все новые
инструменты доступны только general server по отдельному experimental flag.
MCP read references не принимаются как разрешение SaveAs.

## Предлагаемый интерфейс

| Инструмент | Вход | Результат и действие |
|---|---|---|
| prepare_owned_save_as | workingDocumentBindingHandle | Prepared target/plan handle, фиксированный НОВЫЙ result path, saved-file/checkpoint proofs; без CAD-команд |
| execute_owned_save_as | ownedSaveAsHandle | Consume/revoke до IPC; retained-COM guard; один FILE SAVEASPATH; executed_unverified или unknown |
| verify_owned_save_as | saveRequestId, saveReceiptSha256, candidateDocumentHandle, activeDocumentHandle | Fresh independent receipt/journal/file/context checks, succeeded_verified и savedResultHandle; без CAD-команд |
| prepare_owned_saved_reopen | savedResultHandle | Fixed-name отдельный byte-identical reopen alias и immutable provenance; без CAD-команд |
| execute_owned_saved_reopen | ownedReopenHandle, candidateDocumentHandle, activeDocumentHandle | One-use свежий context choice и один guarded OPEN alias; без SAVE/close |
| verify_owned_saved_reopen | reopenRequestId, reopenReceiptSha256, candidateDocumentHandle, activeDocumentHandle | Independent context/content/units/file proof и новая result document binding; без CAD-команд |

Все IDs/handles connection-scoped, TTL максимум 300s и не продлевается
проверкой. Зависимости проверяются после каждого native return. Native raw SHA,
Python observation digest и native C# digest остаются различными форматами.
Аргументы не включают произвольную команду, overwrite или произвольный target path.

## Владение и durable execution

Новая reservation и owned directory под bridge root: savedresults/resultId.
Result_resultId.psmodel и DiskReopen_reopenId.psmodel получают уникальные имена;
файлы до соответствующей команды отсутствуют. Parent/target/no-reparse/alias
проверки повторяются непосредственно перед SaveAs. Checkpoint и source/working
saved bytes входят в protected set; working saved file не является SaveAs target.
Target manifest закрепляет source copy/binding, before observation и роли файлов.

Native удерживает одну app/document COM-пару, повторяет свежий guard, проверяет
рабочий ownership, checkpoint, пустую selection, expiry, windows и active path.
prepared → execution_claimed → command_started фиксируются ДО единственного
SaveAs. После возврата требуются новый непустой result, file identity/SHA,
ожидаемый active path и наблюдения модели/единиц; это ещё executed_unverified.
Separate verifier читает authoritative journal и независимо выданные receipts.
При неизвестном результате существующий target/claim/journal сохраняются;
любая новая попытка с тем же intent/copy блокируется до явного recovery К7.

## Независимый reopen

Использовать отдельную own alias-копию результата с новым именем, как в уже
исследованном CheckpointTest: это позволяет перечитать сохранённый файл в новом
документе, сохраняя текущий own result и старые пользовательские окна. Это не
повтор исходного OPEN intent. Alias создаётся один раз с exact saved-byte SHA,
fixed owned path и отдельными native file IDs; его saved bytes проверяются снова
до/после OPEN. Если active saved result занят писателем, не ослаблять существующий
общий source-copy guard. Для saved-result clone необходим отдельный явно
квалифицированный byte-stability/ownership контракт. Он реализован отдельно:
held saved physical identity с FileShare.ReadWrite без delete, двойной SHA вокруг
create-once clone, readonly DiskReopen seed и неизменённый strict OwnedCopies
source reader. Seed, Working_<id> и Before checkpoint — разные физические файлы
с одинаковым SHA. Открывать планируется новый Working_<id>; seed/checkpoint
сохраняются закрытыми. Это не exclusive writer lock и не разрешение OPEN.

После OPEN нужны fresh references/new binding, состав/координаты/единицы модели,
explicit numeric tolerances и независимые original/result/checkpoint hashes.
Реализованный comparator требует qualified world/empty selection context,
использует abs/relative1e-9 только для доступных geometry/workplane measurements,
сохраняет строгие raw units/metadata и сравнивает multiset без native IDs.
Missing поля не считаются полным геометрическим доказательством.
Нативные IDs/ссылки из предыдущего документа не переиспользуются. Bounds/COG/
area/volume сами по себе не доказывают полную топологическую эквивалентность.
Сохранить старые окна; owned result/reopen остаются открытыми для квалификации,
если нет отдельно принятого close сценария. При unknown не восстанавливать
активный документ автоматически.

## Доказательства К6

1. Synthetic native tests: locked/tampered/alias/expired targets, command count
   zero при guard refusal; command count one при success/unknown, no replay.
2. SDK tests: strict typed schemas, read-only/foreign/stale/duplicate rejection,
   cancellation before/after publication, exact private envelopes и proofs.
3. Native artifact compatibility и независимый saved-byte/journal audit.
4. Clean checkout/Windows CI. Live только на K5 own copy b3d... после FRESH
   process/document/context/ownership checks; expected unsaved X+1 отдельно
   прочитать, прошлые PID/window numbers/handles не принимать за актуальные.
5. One SaveAs/new target, separate reopen/new binding, independent source/
   checkpoint/result bytes + состав/координаты/единицы; original document restored
   только после известного успеха. К10 расширяет квалификацию на все пять классов.

## Источники

[Saving a model — Autodesk PowerShape 2023](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-2001724D-8A62-4887-ABA4-75494643C3B2):
обычный Save перезаписывает stored version; Save As с новым именем сохраняет
отдельную модель, существующее имя может привести к overwrite. File Doctor
может показать диалог при сохранении. Поэтому target новый, а dialog/unknown
останавливает сценарий; настройки File Doctor/shared database не меняются.
Документация описывает UI-поведение, но не доказывает COM/macro return contract.
CheckpointTest.cs:145 содержит исследованный FILE SAVEASPATH; следующие строки
делают byte-identical separate reload copy. Его fixture-specific source/paths/
measurement/inverse workflow не переносятся в general core.
