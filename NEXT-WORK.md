# Дальнейшая работа

Текущий блок К6: saved-byte clone/preparation готов, SDK0.36.2/native source0.33.
Следом qualified one-use execute_owned_saved_reopen, fresh context choice,
guarded OPEN нового Working_<id>, separate content/units/files verifier и новая
binding с новыми наблюдениями/IDs. Prepared handle не открывает модель и не
разрешает edits. Перед OPEN повторно закрепить raw reopen/source/seed/copy proofs
и physical identities; использовать existing WorkingCopyOpen guards/one execution
lane, сохранить прежние окна и неизвестный старый OPEN. 227 .NET/479 native/
142 actual artifacts и six STDIO scenarios passed offline; evidence/K6-save-as/
reopen-preparation-full. Сначала clean checkout/CI нового блока. Runtime0.32/
published K5 SDK/config unchanged, default switch decision pending.
После полной offline qualification — own live К6 только с fresh ownership/
process/model/context checks. К6–К11 остаются обязательной целью.

Ниже история предыдущих блоков.

Текущий следующий блок К6: native byte-stable own saved-result clone и отдельно named reopen intent,
guarded OPEN, independent content/units/file checks и new binding. Не ослаблять
общий source-copy FileShare.Read; clone занятого saved result имеет отдельный
qualified contract. SDK execute/verify/savedResult leases уже реализованы:
212 .NET/432 native/124 artifacts, пять STDIO/Inspector passed offline;
clean baseline/SDK aeeeb43 и все четыре Windows CI успешны. Для Framework tests
использовать короткие staging paths (успешный clean work/n1), без повторного
использования частично созданного NativeStageDirectory.
Runtime0.32/published K5/config unchanged; default decision pending. Own live К6
только после fresh process/model/context/ownership checks. К6–К11 обязательны.

Текущий следующий блок К6: SDK execute_owned_save_as/verify_owned_save_as,
connection-scoped savedResult lease, отдельно квалифицированный clone занятого
saved result, own uniquely named reopen/new binding. Native one-save executor/
fresh verifier и SDK passive prepare уже реализованы; 188 .NET/432 native,
116 artifact cases и independent native save audit passed offline. Clean checkout
47a64e5 baseline/SDK и все четыре Windows CI прошли; далее SDK routes/proofs и reopen.
Runtime0.32/published SDK0.35.1/config не менялись, default switch pending.
К6 не завершён; дальнейший объём К7–К11 сохраняется. Перед live новые process/
document/model/ownership checks. STATUS-K6-SAVE-AS.md.

Ниже история предшествующих блоков; формулировки «ещё отсутствует» относятся
к моменту записи и не заменяют текущий статус выше.

К6 source native0.33.0-dev: filesystem target + pinned directories + SDK target
store реализованы. Runtime0.32/published K5 SDK/config unchanged. Полный
evidence/K6-save-as/verified-targets и clean 2e93bf0 passed. Блок сохранён в Git/PR #7;
SDK CI ещё выполняется, baseline jobs успешны. Далее passive
prepare facade/flag и native one-use SaveAs/independent verifier/reopen/new binding.
STATUS-K6-SAVE-AS.md. Подготовка target не авторизует команду, COM guard обязателен.

К5 technical pilot/clean/CI/deployment/independent audit пройдены; user default
switch decision pending. K5-SDK-SWITCH.md содержит proposed full + read-only
config и rollback. До ответа config.toml не менять. Если пользователь разрешит,
точечно обновить две MCP sections, backup/TOML/startup check, затем проверить
actual connected adapterVersion после reload; config write сам не доказывает reload.
Новый собственный b3d16256be1a4848bdbf15f6160a0567 остаётся открытым с известным
unsaved X+1 и неизменённым saved Working/checkpoint; original Test_Block_P2 активен.
К6 — новый SaveAs target/saved bytes/separate reopen на этой собственной копии;
перед любыми командами fresh model/queue/process/ownership proofs, handles не переносить.
OWNED-SAVE-AS.md начинает контракт/порядок К6: prepare/execute/independent verify,
отдельный byte-identical uniquely named reopen alias/new binding. Native/SDK code
К6 пока отсутствует; отдельно квалифицировать clone сохранённого занятого result,
не ослаблять общий source-copy FileShare.Read guard.
Не повторять старый unknown OPEN 6ed.../copy 5fef..., не менять его journal/claim.

К5 live pilot нашёл duplicate-name dialog и heartbeat sharing error.
Source исправлен: SDK 0.35.1-dev, native 0.32.0; runtime пока 0.31.0.
Полный evidence/K5-owned/collision-fix passed. Проверить необходимые baseline/
deployment preflight, сохранить код/PR и подготовить rollback-aware native update
при idle queue. Затем новый уникально названный owned scenario. Исходный OPEN
6ed9d4106d054b28a05e8ab0b59c5811 не повторять; journal/claim неизвестного исхода
сохранить. Copy 5fef10809eb44904ade27e2ca843a8b9 не использовать для нового OPEN.
После user OK четыре старых окна/original baseline подтверждены fresh read;
геометрический executor не запускался. Новые helper scripts tools/qualify_sdk_owned.py
и tools/prepare_k5_seed.py требуют отдельного явного запуска, не входят в CI.

12 октября: К0 принят пользователем, разработка до К11 авторизована.
К1 пройден локально и в Windows CI. К2 локально пройден: SDK-host/fake-panel,
45 .NET tests, independent STDIO/Python journal parity и Inspector; clean checkout
и все Windows CI успешны, К2 закрыт. К3 локально пройден: SDK 0.33.0-dev,
шесть read-only tools, temporal stores/единый async guard, 98 .NET tests,
independent Python read/digest/raw-byte parity и Inspector CLI. Clean checkout
и все CI успешны, К3 закрыт. Сейчас К4 — entity sets/local plans/copy/binding/owned
stores с byte/schema parity, сохраняющий native worker и separate execute/verify.
К4 локально пройден: подключены десять prepared facades, full/read-only 16/6,
141 .NET tests, independent Python byte/schema/private envelope parity,
312 Framework synthetic checks/63 actual artifact cases и Inspector CLI.
Clean checkout/CI К4 прошли; К4 закрыт. Сейчас К5 — owned context/open/translation/verify
facades и квалификация на новой собственной копии; обычный live adapter switch
принимает пользователь по готовому пакету/rollback. Native runtime пока не менять.
К5 SDK 0.35.0-dev: пять context/open/execute/verify endpoints подключены,
general/read-only 21/6. 159 .NET, four STDIO clients, 312 Framework synthetic,
78 actual artifact cases и Inspector прошли. Новый read-only SDK прошёл passive
live pilot через существующую панель; native/runtime/CAD не менялись.
Следующий блок: clean checkout/Windows CI, затем новый manifest-owned copy/open/
one SOLID translation/independent verification scenario. Непосредственно перед
командами повторить свежие process/queue/model checks: прошлые PID/handles не актуальны.
Не повторять P2.29 OPEN/intents и не трогать чужие/квалификационные модели.
Не переносить C# native digest в Python encoding: это разные схемы.
Product Help — `en_US`, отчёты на русском; CHECKPOINTS.md хранит текущий статус.
Ниже историческая запись подготовки, её ожидание К0 уже закрыто.

Перед новым кодом MCP согласовать К0: MCP-DEVELOPMENT-PLAN.md и
MODULE-MIGRATION-MATRIX.md. Программы установлены и проверены; пользователь
уведомлён до планирования. Новый host пока отсутствует. Предложены 12 контрольных
точек К0–К11, технические proofs/PR для каждого блока и отдельные решения
пользователя по архитектуре, переключению live адаптера и приёмке v1.
После принятия плана: К1 clean-checkout/synthetic baseline/CI, К2 SDK+fake-panel,
К3 read-only, К4 safety stores, К5 паритет owned execution. Далее новые SaveAs/
reopen/recovery и общие операции/многомодельная квалификация. CAD mutations и
deployment не являются частью текущего блока планирования.

Инфраструктура разработки, 12 октября: Git/gh и dotnet-mcp-builder установлены,
локальный репозиторий main подготовлен. Следующие действия инфраструктуры:
GitHub Codex/gh login и credential helper проверены; выбран публичный
danro-work/PowerShapeBridge создан, исходная база опубликована и SHA сверены.
Далее работа через ветки/PR по согласованному плану. .NET SDK/Node/Inspector/C#
установлены отдельным блоком; предложение C# STDIO-host ожидает К0.
Локальный автор задан по профилю danro-work с GitHub noreply; текущая база
подготовлена как исходный локальный commit. Для нового C# MCP-адаптера:
.NET 10 SDK + закреплённый ModelContextProtocol, затем Inspector и C# tooling.
Полный CI требует подготовленных синтетических native self-test артефактов;
CAD-квалификация остаётся отдельной. См. DEVELOPMENT-TOOLS.md.
Это не заменяет следующий продуктовый блок owned SaveAs P2.31.

Текущее P2.31, 12 октября: runtime 0.31.0 установлен, каталог 21/read-only 6.
Первый executor одного owned SOLID/world raw mm: два guard-прохода в удерживаемом
контексте, один TranslateItem, permanent intent claim, copy fence, native journal
и отдельная independent verification. X +1 и отдельный X −1 приняты на своей копии;
её наблюдения восстановлены, Test_Block_P2 снова активен, четыре окна/saved hashes
сохранены. Own OPEN/claim/journal P2.29 неизменны. 114 Python/312 C#/MCP-протокол,
preflight и audit 55 MCP calls прошли. STATUS-P2.31-EXECUTOR.md, OWNED-TRANSLATION.md.
Следом owned SaveAs: новый собственный target, guard/one-use journal, защита источников
и checkpoint, independent saved-byte verification и отдельный reopen. Далее другие
модели/сущности/направления и операции, interrupted/non-success recovery, выпуск v1.
Сначала свежие процессы/очередь/документы. Не повторять старый own OPEN; unknown
не повторять и не снимать fence вручную. Автоматические мутационные тесты на
пользовательской модели/baseline запрещены; мышь только после согласия.

История P2.30: общий профиль 0.30.0 установлен, каталог 19/read-only 6.
Связанный immutable plan/set/binding/ownership/checkpoint/working-bytes packet
подготовлен/проверен live в одной COM-паре, два guard-прохода/четыре чтения.
Журналы prepared-only, executionEnabled=false; геометрического executor нет.
105 Python/278 C#/MCP-протокол, preflight и аудит 27 MCP calls прошли.
Test_Block_P2 снова активен; четыре окна, protected saved hashes и исходный
open journal/claim сохранены. Own OPEN не повторялось. STATUS-P2.30-GUARD.md,
OWNED-OPERATION-PLANS.md. Следом общий параметризованный native executor:
guard до команды в удерживаемом контексте, one-use claim/native journal,
постусловия и независимая проверка неизвестного результата без replay.
Сначала свежие процессы/очередь/документы; мутационные испытания автоматически
на пользовательской модели не запускать. Live surface/curve sets и несколько
моделей, общие SaveAs/import/export/close/recovery/MCP v1 остаются впереди.

История P2.29: общий профиль 0.29.0 установлен, каталог 17/read-only 6.
Одна собственная копия открыта, unknown после возврата команды независимо проверен
без replay; native journal дополнен succeeded_verified. Live binding/validate,
набор SOLID/disabled local plan прошли. Test_Block_P2 снова активен, три прежних
документа и четвёртое окно собственной копии сохранены. 94 Python/227 C#/
MCP-протокол, 30 live MCP calls и независимый аудит прошли. STATUS-P2.29-OPEN.md,
OWNED-COPY-OPEN.md. Следом immutable интеграция plan/set/ownership/binding в guard
общего исполнителя непосредственно перед параметризованной командой, её native
журнал/постусловия. Геометрия выключена; на пользовательской модели автоматически
не испытывать. Сначала свежие процессы/очередь/документы; прежний own open не повторять.

История P2.28: passive binding была принята синтетически и live отказом чужого
Test_Block_P2; 74 Python/195 C#/28 calls. STATUS-P2.28-BINDINGS.md.

История P2.27: общий профиль 0.27.0 установлен. Подготовка/проверка
сохранённых файлов рабочей копии и independent ReadOnly checkpoint приняты; file
ownership predicate не заменяет COM guard. См. STATUS-P2.27-COPIES.md и
WORKING-COPIES.md. Далее открытие своей копии/привязка CAD-документа к ownership,
guard непосредственно перед командой в контексте исполнителя, нативная привязка
журнала/постусловий и пассивная приёмка остальных типов/lifecycle. Исполнения нет.
Ниже сохранена история планирования; прежняя задача развёртывания уже выполнена.

Историческое примечание переноса: ROADMAP.md задаёт продуктовый объём общего v1.
Новый порядок SDK-работ предложен в MCP-DEVELOPMENT-PLAN.md и ожидает К0;
до принятия он не заменяет согласованные требования и квалификационную базу.

| Блок | Уже есть | Что осталось и как принять |
|---|---|---|
| Запуск и диагностика | Launcher и совместимый канал панели | Единая диагностика версий компонентов, воспроизводимая инструкция; исключить дубли исполнителей |
| Публичный MCP / ошибки | Общая конфигурация, local plan/journal, native own-open journal и независимое разрешение returned unknown без replay, read-only | Native геометрический executor и его timeout/доказательства, interrupted command/partial writer recovery |
| Документы и сущности | Чтение/выбор, набор SOLID, успешная live owned binding и отказ после смены активного документа | Guard до команды общего executor, lifecycle; live наборы поверхностей/кривых |
| Общие CAD-операции | P2.31 executor одного owned SOLID/world raw mm с независимой проверкой | Параметризованные transforms, selection/levels/workplanes по согласованному v1-контракту, приёмка на рабочих копиях |
| Плоскости и пересечение | Эталон 25 секущих / одно пересечение | Общие параметры наборов; отверстия, сплайны, касания, пустые сечения, локальные системы, границы размеров |
| Файлы и восстановление | Эталонные SaveAs/reopen/DGK; disk copy/checkpoint/file leases, одно own open, verified journal и live binding | Общие import/export/save/close/recovery, другие модели и доказательства сохранённого результата |
| Выпуск MCP v1 | Контракт и частичные квалификации | Несколько нетехнологических моделей, установка, диагностика, явный перечень поддержанного и ограничений |
| Технологический модуль | Требования и испытательные примеры | После v1: правила задания, рабочие поверхности/Z-stop, ОТМ/ТМ, нарезка, DGK по элементу, AI-панель и согласование PowerMill |

Ближайший новый продуктовый блок — owned SaveAs в новый собственный результат
с guard/journal, independent saved-byte verification и отдельным reopen;
checkpoint/источники защищены. В предложенной SDK-последовательности это К6
после переноса/паритета К1–К5; порядок ещё ожидает решения К0. Не объявлять
MCP v1 готовым после одного эталона; отдельные наблюдения не доказывают
атомарной ревизии CAD.

Блок P2.23-core: отдельная конфигурация реализована и собрана в staging, runtime не
обновлён. См. STATUS-P2.23-CORE.md. Далее живая проверка общего профиля через единственную
пользовательскую панель, затем проверяемые operation entity sets в worker. Прямой
служебный запуск staging получил COM 0x800401E3; не повторять его как доказательство
неисправности CAD. Проверки геометрии на пользовательской модели не запускать.

Блок P2.24-sets: экспериментальные кандидаты/наборы и повторная проверка в worker
реализованы, 129 C# и 35 Python проверок плюс протокол прошли; staging 0.24.0.
См. STATUS-P2.24-SETS.md. Далее проверяемое развёртывание общего профиля в единственной
пользовательской панели, пассивная живая квалификация наборов/отказов, затем guard
в собственном контексте будущей операции. Наборы пока не авторизуют CAD-изменение.
