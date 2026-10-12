# P2.30 — связанный план и guard собственной CAD-копии

9 октября 2026. Исходники, staging и установленный runtime 0.30.0.
Универсальный MCP v1 **не завершён**; технологический модуль отдельный следующий продукт.

## Результат

general-v1 с ownedOperationPlansExperimental: каталог 19/read-only 6.
prepare_owned_operation объединяет immutable local plan/set receipts, binding,
ownership, независимый checkpoint и текущие working bytes. Native worker удерживает
доказательства и одну COM application/document, выполняет два guard-прохода
с четырьмя полными чтениями, пишет disabled packet и только prepared-журнал.
validate_owned_operation заново проверяет весь пакет в удерживаемом контексте
и ничего не записывает. Контракт — [OWNED-OPERATION-PLANS.md](OWNED-OPERATION-PLANS.md).

Live prepare/validate приняты на уже открытом собственном Working.psmodel.
До каждого выбора читался свежий список; исходное own OPEN не повторялось.
Его claim и шесть событий журнала P2.29 не изменились. Независимые read-only
наблюдения своей копии совпали. Test_Block_P2 снова активен; сохранены четыре окна,
наблюдения исходного активного документа и protected saved hashes.
Open/save/close/selection/geometry/screenshot не выполнялись.

## Проверки и доказательства

| Проверка | Результат | Путь в evidence/P2.30-guard |
|---|---|---|
| C# без COM | 278 checks; guard/leases/roles/final journal checks | code/worker-tests.txt |
| Python | 105 tests, включая 11 новых coupled-route tests | code/*.log |
| MCP-протокол | lifecycle/allowlist/timeout/cancel/queue/image integrity | code/protocol.log |
| Deployment preflight | valid plan, corrupt artifact/version refused; CAD/runtime не изменялись | deployment-preflight.json |
| Итоговая MCP-приёмка | 27 calls: 18 native success, 3 local success, 6 local refusals | installed-mcp-final/qualification.json |
| Независимый аудит | SHA plan/set/binding/packet/receipts/journals, Win32 file IDs, separate ReadOnly checkpoint, окна/наблюдения | independent-audit.json; native-artifacts/ |
| Финальный runtime | одна idle-панель, очередь пуста; source/stage/runtime hashes совпадают, protected saved bytes совпали | final-runtime.json; final-read/ |

Первая успешная пассивная приёмка сохранена в installed-mcp. После аудита guard
усилен повторной проверкой prepared-only журналов в конце; новая синтетическая
регрессия writer.lock между проходами прошла. Итоговая сборка заново установлена
и принята в installed-mcp-final; аудит относится именно к ней. Повторы здесь
проверяют пассивный guard; команда открытия P2.29 не повторялась.
Сохранён 51 exact native artifact итогового сценария с проверенными SHA.

Packet итоговой приёмки: 352cf3b837194e77a0fc8b26af8c3af6,
SHA 752c7bb960d1621efcb5525725c9f7a34f9d0ede8712086a4bd103fd2424853d.
Эти ID, PID и leases — исторические доказательства, не актуальные ссылки для edit.

## Установка и воспроизводимость

Staging work/P2.30-guard; совместимый runtime releases/P2.6.
Профиль config/general-owned-operation-plans.json, configuration SHA
360a6a93940f0bd26ef95d09dfe64a8a6bfba3cc165b336be86268d383903281.
Последний резерв work/general-deployment-backup-20261009-142905; первый резерв
этого блока work/general-deployment-backup-20261009-142407 хранит прежний runtime.
deployment.json описывает итоговую установку. Закрывалась только подтверждённая
idle-панель; PowerShape не перезапускался, второй worker/панель не запускались.

build.ps1 -OutputDirectory work/P2.30-guard -ConfigurationPath
config/general-owned-operation-plans.json; verify-entity-sets.ps1 запускает только
code checks. deploy-general.ps1 без Apply пишет план, Apply обновляет runtime.
qualify_owned_operations.py явно выбирает уже открытый owned документ и возвращает
исходный после известного успеха; unknown прекращает сценарий без replay.
audit_owned_operations.py и finalize-owned-operation-evidence.ps1 читают файлы/
процессы без CAD-команд. Подключённый адаптер чата по-прежнему 0.22.0;
свежий stdio — 0.30.0/19 tools. Общее подключение не считать обновлённым автоматически.

## Граница и следующий блок

Пакет и его журналы сохраняют executionEnabled=false. Геометрическая команда
не исполнялась; standalone validation не авторизует будущий edit. COM/file leases
действуют только в вызове. Полная топология, continuous CAD revision, unsaved
состояние и lifecycle не доказаны. Live принята одна saved SOLID-копия;
пересечение/roles проверены синтетически, поверхности/кривые live ещё впереди.

Следом общий параметризованный executor: guard в том же удерживаемом контексте
до команды, one-use native execution claim/journal, постусловия и независимое
разрешение неизвестного исхода без replay. Затем SaveAs/import/export/close/recovery
и приёмка нескольких нетехнологических моделей. Пользовательская модель и baseline
не предназначены для автоматических мутационных испытаний.
