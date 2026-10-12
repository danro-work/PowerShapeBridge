# К5 — готовый пакет перехода на SDK-клиент

12 октября 2026. Технический pilot принят проверками; решение пользователя о
default connection ещё требуется по MCP-DEVELOPMENT-PLAN.md, строке К5.
Общий MCP v1 и К6–К11 ещё не завершены.

## Проверенный результат

SDK 0.35.1-dev, official ModelContextProtocol 2.2.0/.NET 10; native 0.32.0
установлен в совместимый releases/P2.6. Одна существующая панель/STA execution
lane; SDK-хосты запускают только STDIO, сами не стартуют worker/панель/CAD.
Каталоги general/read-only — 21/6. Read-only не активирует/выделяет/сохраняет/
редактирует модели и не создаёт изображения.

162 .NET tests, four independent STDIO clients, 320 Framework synthetic,
114 Python baseline, protocol/junction/deployment preflight, 78 actual artifact
cases и Inspector пройдены локально и в чистом checkout. Все четыре Windows CI
на production-code head cf863d2 PR #6 успешны. Native deployment с backup,
hash verification и idle/single-panel checks пройден; 28 protected saved-file/
ownership/claim/journal hashes до/после одинаковы.

Новая copy b3d16256be1a4848bdbf15f6160a0567:
OPEN b95be76a673041cf87a8285fbdb1c3b6, translation
d8129739753d4de9ab77c6d6ef9345a2. По одному OPEN и TranslateItem; один SOLID,
world/raw mm X+1. Separate verifier и независимый read/audit подтвердили оба
журнала succeeded_verified (6 events), matching fence release и сохранность
source/checkpoint/saved working bytes. Исходный Test_Block_P2 снова активен.
Собственная Working_b3d16256be1a4848bdbf15f6160a0567 остаётся открытой с известным
несохранённым переносом для К6. Сохранения/закрытия/inverse не было. Это не
доказательство полной топологической эквивалентности или универсальности v1.

Старый OPEN 6ed9d4106d054b28a05e8ab0b59c5811/copy
5fef10809eb44904ade27e2ca843a8b9 остаётся unknown. Он не повторялся,
не переводился в success, claim/журнал не удалялись. Unique schema2 имена и
duplicate-name guard предотвращают повтор найденного конфликта; schema1
старых копий поддерживается. Исправлена также heartbeat File.Replace collision,
из-за которой пользователь видел ошибку панели. Исходный startup-error.log
сохранён как историческое доказательство.

## Пакет и proposed configuration

Пакет: work/K5-sdk-runtime; package-manifest.json содержит SHA каждого файла
и проверенные catalogs. Published package отдельно прошёл реальный STDIO
startup/list/status для обоих modes без CAD requests.

Готовые две секции — config/codex-sdk-example.toml:
заменить powershape_bridge Python command на dotnet + published DLL и добавить
powershape_readonly с --read-only. Существующие startup/tool timeout 20/120
сохраняются, host timeout 90. Остальные пользовательские настройки не меняются.
Текущий config.toml ещё содержит прежний Python powershape_bridge.

MCP configuration хранится в ~/.codex/config.toml; формат command/args,
timeouts и desktop Restart описаны в [официальной документации OpenAI](https://learn.chatgpt.com/docs/extend/mcp?surface=cli).
После одобрения: backup прежних настроек, точечное изменение двух секций,
проверка TOML и реального STDIO старта. Сохранение config не доказывает, что
текущий подключённый MCP process уже перезапущен. Инструмента для перезапуска
MCP connection из этого чата нет; при необходимости пользователь выполняет
Restart сервера в настройках, после чего adapterVersion проверяется повторно.
Полный Codex/PowerShape restart не является частью этого пакета.

## Откат

Откат SDK connection: восстановить прежнюю powershape_bridge секцию и убрать
добавленную powershape_readonly, затем reload этого подключения. Это не меняет
CAD документы, worker, очередь или native journals. Native backup отдельный:
work/general-deployment-backup-20261012-152231. Native rollback не выполняется
автоматически вместе с SDK rollback; 0.32 исправляет найденные native ошибки.

Evidence: evidence/K5-owned/collision-fix, deployment, native32-readonly,
unique-owned-live/qualification.json и independent-audit.json;
work/K5-fix-clean/evidence/K1-baseline и K5-fix-clean.
