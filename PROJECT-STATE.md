# PowerShape Bridge — состояние для продолжения

К5 технически квалифицирован: native 0.32.0 установлен и SDK 0.35.1-dev
published/full-readonly 21/6 проверен. Clean baseline/SDK и четыре Windows CI
cf863d2 пройдены. После backup-aware idle deploy 28 protected hashes совпали.
Новая own copy b3d16256be1a4848bdbf15f6160a0567/open
b95be76a673041cf87a8285fbdb1c3b6/translation
d8129739753d4de9ab77c6d6ef9345a2 прошла один OPEN + один SOLID world/raw mm X+1,
separate fresh verification и независимый raw artifact audit; journals по 6
events succeeded_verified, matching fence released. Source/checkpoint/working
saved bytes не менялись; original Test_Block_P2 снова активен. Сейчас пять окон,
новая Working_b3d... остаётся открытой с известным unsaved translation для К6.
Никаких save/close/inverse не было. Старый OPEN 6ed... сохраняется unknown,
claim/journal не изменены. K5-SDK-SWITCH.md — готовый review/rollback пакет;
запрошено решение пользователя по default переходу Python → SDK + отдельный
read-only server. Config.toml пока не менялся. К5 ожидает это решение,
К6–К11 обязательны. Далее сохранена история предыдущих блоков.

Предшествующий К5 блок: SDK source 0.35.1-dev, native source/staging 0.32.0;
установленный native всё ещё 0.31.0. Live OPEN на НОВОЙ copy
5fef10809eb44904ade27e2ca843a8b9/request 6ed9d4106d054b28a05e8ab0b59c5811
остановлен timeout: PowerShape показал «Working уже открыта». Три native
events до command_started; command_returned нет. Перенос/save/close не отправлены,
повторов/удаления claim/перезаписи журнала не было. Пользователь сам снял dialog.
Fresh readonly read: четыре исходных окна, Test_Block_P2 активен, source saved
bytes/наблюдаемая геометрия неизменны. OPEN остаётся unknown.
Исправления: unique schema2 working filename с совместимостью schema1,
duplicate-name refusal до OPEN и advisory heartbeat file-sharing/publication.
Последнее исправляет показанную пользователем ошибку WritePanelStatus/File.Replace;
это не ошибка launcher. Полный SDK check пройден: 162 .NET, four STDIO clients,
320 native synthetic, 78 artifact cases, Inspector; evidence/K5-owned/collision-fix.
Staged deployment и новый owned live scenario ещё впереди. STATUS-K5-OWNED.md.
Clean checkout и все четыре Windows CI исходного блока 8996eb7/PR #6 прошли.
К5 не закрыт; К6–К11 обязательны. Ниже история предшествующих блоков.

12 октября 2026: пользователь принял MCP-DEVELOPMENT-PLAN.md и матрицу,
авторизовал автономную разработку до К11 и разрешил Product Help `en_US`.
К0 закрыт. К1 локально пройден: clean checkout, 312 C# и 114 Python,
MCP-протокол, junction и deployment plan; оба Windows CI успешны, К1 закрыт.
STATUS-K1-BASELINE.md. К2 локально пройден: новый net10.0 SDK-host 0.32.0-dev,
два diagnostic tools, 45 .NET tests, independent STDIO/journal parity и Inspector.
STATUS-K2-SDK.md. К2 прошёл clean checkout и все Windows CI; К2 закрыт.
К3 локально пройден: SDK 0.33.0-dev, шесть read-only tools current general
профиля (пять без experimental candidates), connection-scoped temporal stores,
единый async read guard, admission максимум 8/duplicate ID refusal, общий
deadline с ожиданием gate. 98 .NET tests, independent Python STDIO/read lease/
digest/raw-byte parity и Inspector CLI прошли без CAD. Clean checkout и все
Windows CI успешны; К3 закрыт, К4 начат. STATUS-K3-READONLY.md.
Native runtime 0.31.0 не изменён,
live адаптер не переключался; mutation tools в новом host пока отсутствуют.
К4 локально пройден: SDK 0.34.0-dev, general/read-only catalog 16/6, десять
prepared facades подключены к тем же connection stores/ReferenceGate. Точные
схемы, native private parameters и local plan/prepared journal bytes проверены
независимым Python adapter. 141 .NET tests, три STDIO/parity clients, 312 native
synthetic tests и 63 Framework artifact cases C#/Python compatibility прошли.
Inspector CLI прошёл; все проверки без CAD. Evidence/K4-safety/full.
Clean checkout и все Windows CI К4 прошли, К4 закрыт. К5 начат: remaining
context/open/execute/verify facades, новый owned scenario и native proofs.
К5 SDK 0.35.0-dev: пять context/open/execute/verify facades подключены;
full general/read-only catalog 21/6. 159 .NET tests, four independent STDIO clients,
312 native synthetic checks, 78 actual artifact cases (0 mismatches) и Inspector
прошли. Новый read-only SDK прошёл passive pilot через установленную панель:
fresh process/queue/deployment proof, документы и активная модель прочитаны.
Open/activation/save/geometry/screenshot не выполнялись; native runtime неизменён.
Evidence/K5-owned/final и live; STATUS-K5-OWNED.md. К5 ещё не закрыт: clean
checkout/CI, новый owned live scenario и решение пользователя по default switch.
Начальная proof-часть К5 перенесена: one-use active document choice,
minimum expiry и raw open/translation receipt checks. Return=1 допускает
только executed_unverified с fence; independent verifier требует отдельного
succeeded_verified/noCommandReplay/fenceReleased. 146 .NET tests прошли;
evidence/K5-owned/K5-proof.trx. Proof helpers сами ничего не исполняют,
не удаляют fence/claim и не обращаются к COM; их five fixed facades подключены выше.
Execute/open/activation endpoints и live
переключение — на К5; native runtime 0.31.0 не изменён. STATUS-K4-SAFETY.md.
Текущие контрольные точки — CHECKPOINTS.md. SDK-host собран и проверен без CAD;
последняя CAD-квалификация остаётся P2.31. Runtime/CAD не изменялись.
Ниже сохранён журнал подготовки; его ожидание согласования К0 уже закрыто.

Подготовка SDK-разработки, 12 октября 2026: .NET SDK 10.0.401, Node 24.20.0,
npm/npx 11.19.0, MCP Inspector 2.10.1 и VS Code C# 2.160.4 (+ runtime extension
3.2.0) установлены и проверены. Отдельная console build/run и Inspector CLI help
прошли; SOFTWARE-SETUP-2026-10-12.md. Пользователь уведомлён о готовности до
начала анализа модулей. Новый код MCP/worker не разрабатывался, native tests/CAD/
deployment не выполнялись. P2.31 остаётся последней квалификационной стадией.
Подготовлены MCP-DEVELOPMENT-PLAN.md и MODULE-MIGRATION-MATRIX.md в ветке
docs/dotnet-mcp-plan; это проект для согласования, не принятая архитектура.
Следующее действие — решение пользователя по К0, затем воспроизводимая база К1
и минимальный SDK-host К2. Общий v1 и технологический клиент остаются раздельными.

Инструменты разработки, 12 октября 2026: установлен dotnet-mcp-builder
(source commit 70ddff7fab9d4d49743d221b56d761cff58048d0, 13 references),
Git for Windows 2.55.0.windows.5 / Credential Manager 2.9.1 / gh 2.102.0.
Локальный Git инициализирован на main; добавлены .gitignore/.gitattributes,
исходники/контракты отделены от локального runtime, CAD и raw evidence без удаления.
Подключение GitHub в Codex и локальный gh проверены для danro-work; auth setup-git
выполнен в пользовательском Windows-контексте (sandbox keyring изолирован).
Локальный автор — подтверждённый GitHub login danro-work
и ID-based noreply; начальный локальный commit текущей базы подготовлен,
его SHA сохраняется в receipt. Пользователь выбрал публичный
danro-work/PowerShapeBridge. Репозиторий создан, исходная база опубликована;
origin/main настроен, локальный и удалённый SHA сверены. Итоги — в receipt.
Разбор двух чатов и инвентарь среды — DEVELOPMENT-TOOLS.md; receipt —
reports/tooling-setup-2026-10-12.json. Modern .NET SDK установлен отдельным блоком выше;
новый C# MCP-host по навыку рекомендуется отдельно от существующего Framework STA-worker.
Этот блок не меняет квалификационную стадию P2.31: CAD, deployment и self-test
при настройке инструментов не выполнялись.

Первоначальная подготовка: 9 октября 2026. Продолжение P2.31, 12 октября: процессы, очередь,
компоненты и CAD проверены свежим чтением; own OPEN не повторялось.

## Цель и приоритет

Разработать общий рабочий PowerShape Bridge / MCP для AI-агента. Сначала принять универсальный MCP v1 на разных моделях, затем отдельный модуль подготовки технологических моделей поверх этого MCP. Полное покрытие PowerShape API не является условием v1.

Среда: Windows, PowerShape Ultimate 2023 23.2.2.0, C# x64 / .NET Framework 4.8+, Python MCP, VS Code. Исходники: `D:\ChatGPT\PowerShapeBridge\PS_Bridge\src`.

## Подтверждённая последняя стадия

**P2.31: ядро 0.31.0, первый общий executor одного собственного SOLID.**
Установлен config/general-owned-translations.json: general-v1, каталог 21/read-only 6.
execute_owned_translation принимает ownedOperationHandle, повторяет guard в удерживаемой
COM-паре, адресует ровно один SOLID через native/composite mapping и вызывает TranslateItem
один раз. План/packet по-прежнему passive false; отдельный native execution plan true,
постоянный local intent claim, copy fence и durable prepared/claim/start/return journal.
verify_owned_translation отдельно проверяет свежие receipts после return, bounds/COG/
прочие наблюдения и saved bytes; только после succeeded_verified снимает matching fence.
Code=1 проверяется и в response, и в authoritative command_returned event.
На manifest-owned копии X +1 и отдельно X −1 по новому read/binding/set/plan/packet
подтверждены независимо; исходные наблюдения копии восстановлены. Test_Block_P2 снова
активен; четыре окна, protected files, working saved bytes и ReadOnly checkpoint совпали.
Open/save/close/selection/screenshot не выполнялись. Own open P2.29, claim и шесть
событий журнала не изменились; неизвестный исход не повторяется и оставляет copy fenced.
114 Python, MCP-протокол, 312 C#, deployment preflight и независимый аудит 55 MCP calls
прошли: 41 native success, 8 local success, 6 local refusals; 119 exact native artifacts.
После усиления verifier выполнена только passive re-verification completed inverse,
без повторной геометрической команды или нового journal event. Initial/final binaries
проверены по deployment SHA. Итоги STATUS-P2.31-EXECUTOR.md, OWNED-TRANSLATION.md,
evidence/P2.31-executor; staging work/P2.31-executor; последний резерв
work/general-deployment-backup-20261012-063132. Старый чат-адаптер 0.22.0, свежий 0.31.0.
Live только один SOLID/world raw mm/X ±1. Полная топология, unsaved state, save/reopen,
другие модели/типы/направления и interrupted recovery не приняты. Transient fence
records после удаления реконструированы по native SHA; claims/journals сохранены.
Следом owned SaveAs/new result с guard/journal, saved-byte verification и отдельным
reopen без изменения checkpoint, затем другие модели/операции/recovery. MCP v1 не завершён.

### История предыдущих блоков (не текущее состояние runtime)

**P2.30: ядро 0.30.0, связанный пакет и passive guard собственной CAD-копии.**
Установлен config/general-owned-operation-plans.json: general-v1, каталог 19/read-only 6.
prepare_owned_operation объединяет immutable plan/set/source receipts/binding и
ownership/checkpoint/working bytes; native worker удерживает доказательства и одну
COM application/document на двух guard-проходах (четыре чтения), пишет packet и
только prepared-журнал. validate_owned_operation перепроверяет всё без записи.
В конце повторно проверяются prepared-only журналы/отсутствие writer.lock.
Пакет и планы executionEnabled=false; геометрический executor отсутствует.
Live guard принят на уже открытой собственной SOLID-копии. Test_Block_P2 снова
активен; четыре окна, наблюдения и protected saved hashes совпали. Own open P2.29
не повторялось, его claim и шесть событий журнала не изменились. Open/save/close/
selection/geometry/screenshot не выполнялись. Full topology/continuous revision,
unsaved state и lifecycle не доказаны; standalone validation не авторизует edit.
105 Python, MCP-протокол, 278 C#, deployment preflight и независимый аудит итоговых
27 MCP calls прошли: 18 native success, 3 local success, 6 local refusals;
сохранён 51 exact native artifact. Итоги STATUS-P2.30-GUARD.md,
OWNED-OPERATION-PLANS.md, evidence/P2.30-guard; staging work/P2.30-guard;
последний резерв work/general-deployment-backup-20261009-142905. Старый чат-адаптер
0.22.0, свежий 0.30.0. Следом native параметризованный executor с guard до команды,
журналом/постусловиями, затем общие файлы/recovery/несколько моделей. MCP v1 не завершён.

**P2.29: ядро 0.29.0, одно открытие собственной копии и независимая проверка исхода.**
Установлен general-v1 с экспериментами наборов/планов/копий/привязки/open: каталог
17/read-only 6. Native open удерживает COM/file leases, проверяет свежий контекст
перед фиксированной командой, пишет frozen plan, one-use claim и durable journal.
Первый same-worker результат был unknown из-за ошибочного требования сохранности
WINDOW.ID. Открытие не повторялось: независимые чтения установили новое окно,
перенумерация IDs учтена, verify_working_copy_open без CAD-команд добавил
succeeded_verified с ID/SHA доказательств, сохранив исходный unknown event.
Приняты live binding/validate собственной копии, набор SOLID, disabled local plan,
file inspection и независимое read-only чтение. Test_Block_P2 снова активен; прежние
три документа сохранились, четвёртое окно собственной копии оставлено открытым.
Save/close/selection/geometry/screenshot не выполнялись. Защищённые saved hashes
и независимый ReadOnly checkpoint сохранены; full topology/unsaved state не доказаны.
94 Python, MCP-протокол, 227 C#, deployment preflight и независимый аудит 30 MCP
вызовов пройдены: 19 native receipts (17 success, 1 refusal, 1 исходный unknown),
4 local success, 7 local refusals. Итоги: STATUS-P2.29-OPEN.md, OWNED-COPY-OPEN.md,
evidence/P2.29-open; staging work/P2.29-open; последний резерв
work/general-deployment-backup-20261009-134050. Старый чат-адаптер 0.22.0, свежий
0.29.0. Планы не принимают copy/binding и сохраняют executionEnabled=false;
геометрический executor и MCP v1 не завершены.

P2.28: 0.28.0, пассивная CAD binding в удерживаемой паре COM/file leases;
успех был синтетическим, live отказ чужого Test_Block_P2 принят. 74 Python/195 C#,
28 MCP calls; STATUS-P2.28-BINDINGS.md, evidence/P2.28-bindings/installed-mcp-final,
staging work/P2.28-bindings, резерв work/general-deployment-backup-20261009-130309.

P2.27: ядро 0.27.0, сохранённые рабочие копии/независимый checkpoint и file ownership.
Установлен general-v1 с экспериментами наборов/планов/копий: 13 инструментов,
read-only 6. Из явного сохранённого psmodel с SHA создаются два независимых файла,
checkpoint ReadOnly и immutable ownership; source не записывается. Busy/reparse/
hard-link/подмена/изменённый checkpoint и чужие target-пути отклоняются.
63 Python, MCP-протокол, 162 C#, реальный junction и deployment preflight пройдены.
Установленный MCP: 23 вызова (9 native, 4 local success, 10 refusals), независимый
аудит файловых ID/хешей/ответов и контекста. Активный Test_Block_P2, три окна,
наблюдения и защищённые хеши не изменились. Копии в CAD не открывались; unsaved
состояние не копируется. STATUS-P2.27-COPIES.md и WORKING-COPIES.md;
evidence/P2.27-copies; work/P2.27-copies; резерв
work/general-deployment-backup-20261009-122954. Чат-адаптер 0.22.0, свежий 0.27.0.
File/path predicate не является COM guard; исполнения планов нет, MCP v1 впереди.

P2.26: общее ядро 0.26.0, локальный план операции и prepared-журнал.
Установлены панель/worker/свежий адаптер; профиль general-v1 с экспериментом наборов
и планов: каталог 11, read-only 6. План переноса/пересечения фиксирует параметры,
состав/роли, хеши набора и receipt, контекст, raw-единицы, срок; executionEnabled=false.
Журнал имеет строгие переходы, fsync и запрет повторного claim неизвестного исхода.
Публично пишется только prepared; native executor/guard/владение копиями впереди.
134 C#, 55 Python, MCP-протокол и preflight пройдены. После установки выполнена
пассивная MCP-приёмка локального плана переноса одного SOLID; независимый аудит
6 native + 4 local success + 7 refusals. Окна/наблюдения/защищённые хеши совпали.
CAD не менялся. Итоги: STATUS-P2.26-PLANS.md и OPERATION-PLANS.md; evidence/P2.26-plans;
staging work/P2.26-plans; резерв work/general-deployment-backup-20261009-120257.
Старая скрытая панель штатно закрыта после проверки; исправлено её обнаружение.
Подключённый адаптер чата остаётся 0.22.0, свежий 0.26.0. MCP v1 не завершён.

P2.25: установленное общее ядро 0.25.0 и пассивная приёмка entity set одного SOLID.
Панель, worker и свежий адаптер установлены в releases/P2.6; general-v1 не зависит
от Test_Block/STEP, capture и испытательные мутации выключены. Эксперимент наборов
включён: свежий каталог — 9, read-only — 6. Проверены подготовка/валидация набора тела
через единственную пользовательскую панель, отказы чужого handle и аргументов.
Наблюдаемые свойства/окна и хеши трёх защищённых файлов совпали до/после. CAD не менялся.
130 C#, 36 Python, MCP-протокол и preflight пройдены. Итоги: STATUS-P2.25-CORE.md;
доказательства: evidence/P2.25-core. Сборка: work/P2.25-core; резерв:
work/general-deployment-backup-20261009-113054. Подключённый адаптер чата остаётся
0.22.0 до переподключения; свежий — 0.25.0. Мутации по наборам и полный v1 ещё впереди.

Продолжение 9 октября: выполнен блок отдельной конфигурации общего ядра в исходниках
и staging 0.23.0; см. `STATUS-P2.23-CORE.md`. Runtime остаётся P2.22 / 0.22.0.
Новая конфигурация проверена кодовыми тестами; её живая квалификация не принята:
прямой worker из служебного сеанса получил COM 0x800401E3. Установленная панель
при свежем чтении успешно перечислила документы и прочитала активный Test_Block_P2.
Дополнение P2.24: кандидаты и наблюдаемые entity sets реализованы экспериментально,
с повторной проверкой в worker; исходники/staging 0.24.0 — `work/P2.24-sets`.
См. `STATUS-P2.24-SETS.md` и `ENTITY-SETS.md`; 129 C#, 35 Python, MCP-протокол пройдены.
Живая приёмка новых наборов не выполнена; mutation tools их не принимают.
Standalone validation не заменяет guard непосредственно перед действием.
Общие CAD-операции ещё не реализованы. Runtime по-прежнему 0.22.0.

P2.22, по `STATUS-P2.22.md` и контрактам проекта:

| Область | Выполнено | Граница подтверждения |
|---|---|---|
| Эталонный публичный сценарий P2.19 | 54 фазы, 25 сечений, DGK по исходному слою | Тестовые рецепты не являются универсальными инструментами |
| Пакетное пересечение P2.20 | Нативный комплект 25 секущих, один вызов пересечения, сравнение после DGK-импорта и повторного открытия | Внутренняя квалификация эталона; сложные модели впереди |
| Чтение P2.21 | `inspect_active_model`, `resolve_entity_references`; тела, поверхности, составные кривые | Ссылки для чтения; наблюдаемые свойства не доказывают всю топологию |
| Документы P2.22 | `list_documents`, `activate_document`; переключения и отказы устаревшего контекста | Уже открытые модельные документы с одним окном; один экземпляр поддержанной версии |
| Режим чтения | 5 инструментов: bridge_status, list_documents, inspect_active_model, resolve_entity_references, operation_result | Команды изменения отсутствуют и отклоняются |
| Код P2.22 | 8 тестов документов, 10 тестов ссылок, MCP-протокол, 86 внутренних проверок C# | Результаты предыдущего этапа; при переносе тесты не запускались |

Свежий общий клиент на приёмке получал 15 инструментов. Прежний подключённый каталог Codex сохранял 9 до переподключения. Это историческое состояние подключения, его требуется проверить заново.

## Версии и запуск

- Корневой `PSBridge.exe` — launcher действующей панели из `releases\P2.6`.
- Установлены панель/worker/свежий адаптер 0.31.0, general-v1 с экспериментами sets/plans/copies/bindings/open/owned packet/one-SOLID translation. Старое подключение адаптера чата — 0.22.0; не выводить версию из имени каталога.
- `releases\P2.6` сохраняется ради совместимости путей, конфигурации и журналов. Не переименовывать при переносе.
- Панель запускается в обычном пользовательском Windows-сеансе рядом с PowerShape. Служебный запуск ранее получил COM 0x800401E3; это не доказательство поломки PowerShape.
- Последняя квалификация снова выбрала Test_Block_P2; сохранила три исходных документа и оставила четвёртое окно собственной копии. Не предполагать, что они открыты сейчас; PID, активный документ, очередь и версии проверить заново без изменения CAD.
- Копия P2.29: releases/P2.6/workingcopies/3a597f32ad94433a8f95cea5493bf386/Working.psmodel. Её open claim запрещает повтор OPEN; отдельный checkpoint/Before.psmodel защищён. Unknown event не удалять/не исправлять вручную.

## Пути и защита

| Назначение | Путь |
|---|---|
| Основной проект | `D:\ChatGPT\PowerShapeBridge\PS_Bridge` |
| Исходный STEP, защищён | `D:\ChatGPT\PowerShapeBridge\Test_Models\Test_Block.step` |
| Исходный проект, защищён | `D:\ChatGPT\PowerShapeBridge\Test_Projects\Test_Block.psmodel` |
| Базовая квалификационная модель | `D:\ChatGPT\PowerShapeBridge\PS_Bridge\P2_Test_Projects\Test_Block_P2.psmodel` |
| Доказательства старого чата | `D:\ChatGPT\2026-10-07\referenced-chatgpt-conversation-this-is-an\outputs` |

Не сохранять изменения поверх исходников и базовой модели. Мутации квалифицировать на собственных рабочих копиях с независимым checkpoint и журналом. Не открывать checkpoint как активный файл на этапе изменения: ранее SaveAs изменил его; квалифицирована стратегия отдельной побайтовой копии.

## Следующий блок

1. Проверить новую рабочую папку, разрешения, версии и канал подключения без изменения CAD.
2. Отделение конфигурации выполнено; расширить пассивную приёмку наборов на поверхности/кривые и смену контекста.
3. Первый one-SOLID executor/independent verification выполнен. Далее owned SaveAs в новый собственный результат, guard/one-use journal/защита target paths, независимая проверка saved bytes и отдельный reopen. Checkpoint не становится активным изменяемым файлом. Мутационные тесты на пользовательской модели автоматически не запускать; own OPEN копии P2.29 не повторять.
4. Принять параметризованные общие операции на рабочих копиях, затем общие файлы/восстановление и приёмку MCP v1 на разных моделях.

## Документы, имеющие приоритет

В исходной папке: `ARCHITECTURE.md`, `ROADMAP.md`, `MCP-V1-CONTRACT.md`, `STATUS-P2.31-EXECUTOR.md`, `OWNED-TRANSLATION.md`, `STATUS-P2.30-GUARD.md`, `OWNED-OPERATION-PLANS.md`, `STATUS-P2.29-OPEN.md`, `OWNED-COPY-OPEN.md`, `DOCUMENT-BINDINGS.md`, `WORKING-COPIES.md`, `OPERATION-PLANS.md`, `ENTITY-SETS.md`, `DOCUMENT-CHOICE.md`, `READ-REFERENCES.md`, `README.md`. STATUS-P2.22/P2.28 — подтверждённые исторические приёмки.

В README ниже актуального вступления сохранено историческое описание P0. Оно не описывает нынешний worker. В READ-REFERENCES сохранены старые числа инструментов P2.21; дополнение P2.22 имеет приоритет.

Пути `reference/` и `evidence/P2.22/` относились к комплекту первоначального переноса;
в текущем корне эти каталоги отсутствуют (проверено P2.29). Актуальные документы
находятся в корне, текущие доказательства — evidence/P2.31-executor. Исторические
доказательства искать по пути старого чата выше и указанным в STATUS-P2.22.md
журналам; не восстанавливать их поверх текущего кода по догадке.

История задачи: текущий чат и исследование ChatGPT `6ac62fb7-b234-83ed-a72e-68ee6e7da0e4`; целевая ветка агента `codex://threads/01a0fb89-03ed-7d13-94ca-14489c1c5ffb`. Здесь зафиксированы решения из текущей истории; содержимое целевой ветки в этой подготовке отдельно не перечитывалось.

