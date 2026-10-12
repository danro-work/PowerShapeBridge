# P2.31 — первый общий executor собственного SOLID

12 октября 2026. Исходники, staging и установленный runtime 0.31.0.
MCP v1 **не завершён**; технологический модуль остаётся отдельным продуктом.

## Результат

Добавлены execute_owned_translation и verify_owned_translation, explicit
ownedTranslationsExperimental. Общий профиль не содержит Test_Block/STEP paths
или технологических правил. Каталог 21, read-only 6. Контракт OWNED-TRANSLATION.md.
Два прохода guard, удерживаемые COM/file proofs, direct native item mapping,
один TranslateItem, постоянный intent claim, блокировка копии до independent
verification и native durable журнал. Автоматического retry/inverse/save нет.

На manifest-owned Working.psmodel выполнены две разные команды: X +1 и затем,
после independent verification и нового чтения/binding/set/plan/packet, X −1.
Оба журнала succeeded_verified, fence сняты только verifier. Наблюдения собственной
копии восстановлены; Test_Block_P2 снова активен, четыре окна сохранены.
Source/STEP/baseline/checkpoint и saved working bytes не изменились.
OPEN/save/close/selection/screenshot не выполнялись; исходный open P2.29,
его claim и шесть событий журнала сохранены без replay.

## Проверки

| Проверка | Результат | Путь в evidence/P2.31-executor |
|---|---|---|
| C# | 312 checks без COM | code/worker-tests.txt |
| Python | 114 tests, включая 9 новых translation routes | code/*.log |
| MCP-протокол | lifecycle/allowlist/timeout/cancel/queue/image integrity | code/protocol.log |
| Deployment preflight | валидный план, corruption/version rejection | deployment-preflight.json |
| Исполнение/верификация | 43 MCP calls; ровно две геометрические команды на своей копии | installed-mcp/qualification.json |
| Усиленный verifier | 12 calls; проверка уже completed inverse, без TranslateItem и нового event | hardened-verifier/qualification.json |
| Независимый аудит | 55 calls: 41 native success, 8 local success, 6 local refusals; 119 native artifacts | independent-audit.json; native-artifacts/ |
| Финальный runtime | один idle panel, очередь пуста; stage/source/runtime SHA и protected hashes совпали | final-runtime.json; final-read/ |

После первой приёмки verifier усилен проверкой return code в authoritative
command_returned event. Синтетическая подмена response code=1 при journal code=0
отклоняется с сохранением fence. Итоговая сборка заново установлена; её verifier
подтвердил уже выполненный inverse без новой команды и без изменения журнала.
Аудитор проверил initial binaries в резерве и final installed SHA. Геометрия
в итоговой установке повторно не испытывалась; command path изменения не менялся.

Forward ID 4ab6279b48e34bbf91807fec95bbba19, plan SHA
444b71c0bbb05be90a7ab9363cb93cab5fb99066c60daea3fa1c4dcd50130084.
Inverse ID 743f6e358a5449e882361837f20215a9, plan SHA
9e8e6ad0965d52ba8bb1667881447a5f5f7b8243532a58a3c8a4ff5bce2f5d07.
ID/PID/handles из доказательств исторические; для следующего действия читать заново.

## Установка

Staging work/P2.31-executor; runtime releases/P2.6, совместимый путь сохранён.
Профиль config/general-owned-translations.json, SHA
dc886fb6bc9d0174b7f3e5c9279b57168f29cbf463b19e34fed41945e800fc91.
Initial deployment — deployment-initial.json, резерв
work/general-deployment-backup-20261012-061854. Итог — deployment-hardened.json,
резерв work/general-deployment-backup-20261012-063132. Только подтверждённая idle
панель закрывалась штатно; PowerShape не перезапускался, второй panel/worker не запускался.

verify-entity-sets.ps1 запускает только кодовые тесты; qualification scripts
отделены. qualify_owned_translation.py требует explicit --allow-owned-translation
и ранее qualified own-copy observation: отказ/unknown прекращает сценарий,
автоматического finally inverse/close/save нет. qualify_translation_verifier.py
не исполняет translation. audit_owned_translation.py и
finalize-owned-translation-evidence.ps1 читают доказательства/процессы/файлы без CAD.
Старое подключение чата остаётся 0.22.0; свежий stdio 0.31.0, 21 tools.

## Ограничения и следующий блок

Live принят один SOLID, один target, world/raw mm, X ±1. Полная топология,
unsaved state, другие направления/модели/сущности, save/reopen и общий recovery
не приняты. Bounds/COG/volume/area — наблюдаемые свойства. Unknown copy остаётся
fenced; interrupted command и non-success return этим verifier не разрешаются.
Transient fence records после удаления реконструированы и проверены по native SHA;
их исходные байты до удаления не архивировались. Claims/journals/plans сохранены.

Следующий блок: общий owned SaveAs с guard/one-use journal и защитой target paths,
сохранение в новый собственный результат, независимая проверка saved bytes и
отдельное reopening без изменения checkpoint. После него — другие модели/типы,
расширение параметризованных операций и квалификация recovery. Пользовательскую
модель и базовый Test_Block_P2 не использовать для автоматических мутационных тестов.
