# P2.29 — собственная открытая копия и проверка возвращённого исхода

9 октября 2026. Исходники, staging и установленный runtime: 0.29.0.
Общий MCP v1 **не завершён**. Технологический модуль не входит в ядро.

## Результат

general-v1 с workingCopyOpenExperimental: каталог 17/read-only 6. Добавлены
open_working_copy и verify_working_copy_open, native guard, frozen open plan,
однократный claim и fsync/hash-chain журнал. verify не вызывает CAD-команд;
проверяет уже вернувшееся открытие по независимым свежим receipts и ownership.
Контракт — [OWNED-COPY-OPEN.md](OWNED-COPY-OPEN.md).

Свою сохранённую копию открыли один раз. Затем приняты live bind/validate документа,
набор одного SOLID, локальный disabled план переноса, file inspection и независимое
read-only чтение. Исходный активный Test_Block_P2 выбран снова через свежий handle;
его наблюдения совпали с исходным native чтением. Остались четыре модельных окна:
три прежних документа и собственный Working. Сохранение/закрытие/геометрия не выполнялись.

## Неизвестный исход и исправление

Первый запрос 9393ae2e42fd476cafb3ead234fd6e0e:
команда открытия вернулась, но ошибочный same-worker predicate потребовал сохранность
WINDOW.ID старых окон. PowerShape перенумеровал эти IDs при появлении нового окна.
Сценарий остановился на unknown; исходный receipt и event000004 сохранены.

Пассивные list/inspect установили один новый owned документ; прежние windowNumber,
modelId/name/path/type и сохранённые байты не изменились. WINDOW.ID не объявлен
устойчивым, постусловие исправлено, перенумерация/дубликаты/подмена покрыты кодом.
После guarded deployment verify_working_copy_open проверил исходный response/plan/
claim/journal и новые независимые receipts. Добавлен event000005 succeeded_verified
с тремя ссылками ID/SHA. Команда открытия не повторялась. Этот результат относится
к конкретному вернувшемуся открытию; interrupted command/общий recovery не принят.

## Проверки и доказательства

| Проверка | Результат | Артефакты |
|---|---|---|
| C# без PowerShape | 227 checks | evidence/P2.29-open/code/worker-tests.txt |
| Python | 94 tests; 20 open/verification, 11 bindings, 8 copies, 19 plans, 14 sets, 10 read refs, 8 documents, 4 config | evidence/P2.29-open/code/*.log |
| MCP-протокол | lifecycle/allowlist/ошибки/timeout/cancel/queue/image integrity прошли | code/protocol.log |
| Deployment preflight | валидный план, испорченный staging и неверная версия; runtime неизменён | deployment-preflight.json |
| Первый MCP сценарий | 6 вызовов; остановлен на unknown, без replay | installed-mcp/qualification.json |
| Проверка полученного исхода | 24 MCP вызова; live binding/set/plan/чтения/возврат базового документа | recovered-mcp/qualification.json |
| Независимый аудит | 19 native receipts: 17 known success, 1 known refusal, 1 сохранённый unknown; 4 local success, 7 local refusals | independent-audit.json |
| Финальная проверка | одна idle панель, очередь пуста; runtime/проверенные исходники/staging hashes совпали, защищённые saved hashes совпали | final-runtime.json; final-read/* |

Аудитор отдельно прочитал Win32 native file metadata; source/work/checkpoint имеют
три разных file IDs, links=1, размеры/хеши совпали. Checkpoint ReadOnly сохранён.
Проверены frozen plan SHA, journal chain, claim, ownership/binding/set/local plan SHA,
native request envelopes, независимые evidence receipts и существующие окна.
Сохранённые файлы источников и прежних документов не записывались.

## Установка и воспроизводимость

Staging: work/P2.29-open. Runtime: releases/P2.6, совместимый путь сохранён.
Профиль: config/general-working-copy-open.json, configuration SHA
573283d19a03d714283a085275c2e3b72e396a0e2cd78dd357e05637ff7a8110.
Две guarded установки: deployment.json (резерв work/general-deployment-backup-20261009-132423)
и deployment-corrected.json (резерв work/general-deployment-backup-20261009-134050).
Проверялись idle queue, один process/path/start/session и staged manifest; только
подтверждённая панель закрывалась штатно. PowerShape не перезапускался.

Исходный open response SHA:
b97d4727adf7ce9c9e0220ca50bb964c34cfc69843550d466b0ef9d4eef3d5f6.
Open plan SHA: bd317f50d38c841c45be9ab2080211d3e4e337634692f851c2e3fdde7fe24322.
Final journal SHA: 2ede26f25843de80ce9579b1f0a31caee43c58a534e75748c8ee4445630bfd45.
Copy ID: 3a597f32ad94433a8f95cea5493bf386; путь
releases/P2.6/workingcopies/3a597f32ad94433a8f95cea5493bf386/Working.psmodel,
checkpoint/Before.psmodel отдельно от активного файла. Копия оставлена как
артефакт испытания; принадлежность не переносится на другие окна или старые PID.

build.ps1 → verify-entity-sets.ps1 проверяют staging без CAD.
qualify_owned_open.py — явный сценарий нового открытия с --allow-owned-copy-open;
**не запускать автоматически на пользовательской модели или повторно для этой копии**.
qualify_owned_open_recovery.py — отдельная проверка существующего returned open
по ID/SHA и исходному baseline receipt, без OPEN. audit_owned_open.py и
finalize-owned-open-evidence.ps1 читают файлы/процессы, без CAD-команд.

Свежее подключение адаптера — 0.29.0/17 tools; подключённый адаптер чата всё ещё
0.22.0, подтверждено final-read/connected-adapter.json. Его passive list/inspect
получили native 0.29.0, четыре окна и Test_Block_P2. Это не обновление каталога чата.
Git-репозитория в этой папке нет. WMI/CIM discovery в sandbox недоступен; фактические
process/path/start/session получены Get-Process и native документными чтениями.

## Ограничения и следующий блок

Принят один saved SOLID и восстановление статуса вернувшегося открытия.
Исправленный same-worker success path после OPEN проверен синтетически;
живой исход принят через независимую verification без второй команды OPEN.
Нет полной топологической эквивалентности, гарантии unsaved состояния прежних
документов, непрерывного lifecycle или блокировки пользователя. Raw area units
не подтверждены. Открытие сейчас требует ASCII пути установки без пробелов.
Read-only не активирует/открывает/сохраняет/выделяет и не создаёт screenshot.

Локальный geometry plan не принимает copy/binding handle и остаётся disabled.
Следующий блок — интеграция immutable plan/set/ownership/binding в guard общего
исполнителя, повторная проверка в том же COM-контексте непосредственно перед
параметризованной командой и её native журнал/постусловия. Мутационные испытания
только на явно собственных копиях; затем поверхности/кривые, разные модели,
save/import/export/recovery и итоговая приёмка MCP v1.
