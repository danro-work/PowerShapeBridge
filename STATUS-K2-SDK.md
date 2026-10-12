# К2 — минимальный MCP SDK-хост

12 октября 2026. Локальная проверка пройдена; Windows CI ещё ожидается.
Версия нового host `0.32.0-dev`; установленный Framework worker остаётся `0.31.0`.
Установка/подключение нового live адаптера не выполнялись.

Новый `src/PowerShapeMcp.Host` использует официальный ModelContextProtocol 2.2.0
и Microsoft.Extensions.Hosting 10.0.12; `global.json` требует SDK 10.0.401.
Прямые версии закреплены точно, transitive packages — в packages.lock.json,
проверка выполняет locked restore. Новый host работает по STDIO и пишет логи
в stderr с момента запуска. COM и запуск worker/panel в host отсутствуют.

| Проверка | Локальный результат |
|---|---|
| Release build | 0 ошибок, 0 предупреждений |
| .NET tests | 45 passed, 0 failed, 0 skipped |
| Реальные SDK client/server через pipes | Протоколы 2025-03-26, 2025-11-25, 2026-07-28, каталог, ошибки схемы |
| Отдельный Python STDIO-client | JSON-RPC stdout, stderr logs, exact schema constraints и `-32602` для лишних/неверных аргументов |
| Journal parity с Python 0.31 | Шесть последовательных состояний, exact raw hashes, interrupted writer, corrupt hash chain, no retry |
| Fixed IPC/fake-panel | Atomic submit, корреляция, максимум 8 pending, очередь, cancellation/deadline, claimed .processing сохраняется |
| Inspector CLI 2.10.1 | tools/list и bridge_status успешно, без браузера и CAD |

Публичный allowlist пока содержит только `bridge_status` и `operation_result`,
оба доступны в отдельном read-only режиме. Регистрация — явный `WithTools(...)`;
runtime guard проверяет точные аргументы дополнительно к schema и SDK hints.
Статус сообщает `geometryMutations=disabled` и `panelLiveness=not_verified`.
Это два tools К2, а не завершённый каталог общего v1. Нет `model_snapshot`,
произвольной команды/пути, создания COM-канала, нового panel/worker или deployment.

IPC пока проверяется через внутренний seam на fake-panel, публичных queue tools
в К2 нет. Cancellation после submit означает unknown и не обещает остановку CAD.
Завершение команды не принимается за verified success; operation_result читает
оригинальные bytes/цепочку, не повторяет команду и не снимает fence/writer.lock.

Два явных отличия от Python dispatcher: официальный SDK пропускает повреждённую
JSON-строку с записью в stderr, без отдельного `-32700`; следующий ping работает.
Отсутствующая legacy receipt возвращает structured unknown/error, а не только
текст исключения. Публичные аргументы сохранены; добавлено описание operationId.
Эти отличия не расширяют CAD-доступ и не выдают неизвестный исход за успех.

Воспроизведение: `verify-sdk.ps1 -DotnetPath <dotnet.exe> -PythonPath <python.exe>
-InspectorPath <mcp-inspector.cmd>`. Скрипт не обновляет runtime и использует
свою Inspector session config, без изменения личного catalog. На локальной
Codex-песочнице Inspector потребовал пользовательский контекст (`spawn EPERM`);
повторный полный запуск в нём прошёл. Raw evidence — `evidence/K2-sdk`:
TRX, STDIO transcript/stderr, Inspector results и sdk-verification.json с SHA.
При новом запуске старый passed receipt заменяется на passed=false до проверок.

Следом К3 — read-only catalog из шести tools, временные references и свежие
native observations. К4 — остальные safety stores и schema/hash parity;
живая CAD-квалификация и решение о смене адаптера остаются на К5.
