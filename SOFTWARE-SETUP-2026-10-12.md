# Установленная среда для PowerShape MCP

12 октября 2026. Программы установлены и проверены **до начала планирования**;
пользователь об этом уведомлён отдельно. Новая реализация MCP пока не создавалась.

| Компонент | Установлено | Проверка |
|---|---|---|
| Git for Windows / Credential Manager | 2.55.0.windows.5 / 2.9.1 | Команды версии; локальный репозиторий и GitHub publication уже проверены |
| GitHub CLI | 2.102.0 | Вход `danro-work`, API user и credential helper в пользовательском Windows-контексте |
| .NET SDK x64 | **10.0.401** | `--version`/`--list-sdks`; отдельная `net10.0` console-сборка и Hello World: 0 ошибок, 0 предупреждений |
| Node.js LTS | **24.20.0** | Версия установленного `C:\Program Files\nodejs\node.exe` |
| npm / npx | **11.19.0 / 11.19.0** | Обе команды версии; официальный Inspector установлен через npm |
| MCP Inspector | **2.10.1** | npm global list, launcher help и CLI-client help; build-файлы всех пяти clients присутствуют |
| VS Code | 1.107.1 x64 | Существующий редактор совместим с C# extension `^1.106.0` |
| C# extension | **ms-dotnettools.csharp 2.160.4** | VS Code CLI list-extensions/show-versions и manifest |
| .NET Install Tool extension | **3.2.0** | Автоматическая зависимость C#; CLI/manifest |
| dotnet-mcp-builder | Commit `70ddff7fab9d4d49743d221b56d761cff58048d0` | SKILL.md SHA и 13 reference files проверены ранее; skill доступен |

.NET Framework worker сохраняет прежнюю среду: registry Release **533325**.
Новая SDK установлена рядом с ней. Код, launcher, существующий runtime и CAD
этим блоком не развёртывались и не запускались.

В новой PowerShell можно проверить:

```powershell
dotnet --version
node --version
npm.cmd --version
npx.cmd --version
mcp-inspector.cmd --help
mcp-inspector.cmd --cli --help
code --list-extensions --show-versions
```

Codex может раньше system PATH выбирать свой встроенный Node. Явные пути:

- `.NET`: `C:\Program Files\dotnet\dotnet.exe`
- `Node/npm/npx`: `C:\Program Files\nodejs\node.exe`, `npm.cmd`, `npx.cmd`
- `Inspector`: `D:\Users\rozhdestvenskii.da\AppData\Roaming\npm\mcp-inspector.cmd`
- `VS Code CLI`: `D:\Program Files\Microsoft VS Code\bin\code.cmd`

System PATH содержит Node, user PATH содержит npm global prefix. Перезапуск
PowerShape или Bridge для этого не нужен. Проверки Inspector использовали help
без target server; браузер/слушающий сервер/подключение к CAD не запускались.
Работу с реальным MCP-хостом проверяем в соответствующей контрольной точке плана.

Новый npm сообщил о пропущенном root postinstall Inspector. Прочитанный официальный
script завершает работу сразу при установке под `node_modules`; опубликованный
tarball уже содержит готовые client builds. Установку из исходников он обслуживает
отдельно, поэтому blanket allowScripts и повторный запуск script не требовались.
CLI-client help подтвердил загрузку необходимых dependencies.

Локальные доказательства, исключённые из публичного Git:

- `reports/sdk-install-check-2026-10-12.json`
- `reports/inspector-install-check-2026-10-12.json`
- `reports/tooling-setup-2026-10-12.json`
- SDK smoke project в собственном `work/toolchain-smoke-*`; это не MCP-проект.

Проверены доступные стабильные NuGet-кандидаты **ModelContextProtocol 2.2.0** и
**Microsoft.Extensions.Hosting 10.0.12**. Это зависимости будущего проекта;
в проект они пока не добавлены. Их compile/protocol приёмка входит в К2.
Полный Visual Studio, Dev Kit, новый Autodesk API-пакет и Framework Developer
Pack для выбранного первого блока не потребовались; native сборка остаётся прежней.

Официальные источники: [Microsoft .NET installation](https://learn.microsoft.com/en-us/dotnet/core/install/windows),
[Node.js](https://nodejs.org/en/download), [MCP Inspector](https://github.com/modelcontextprotocol/inspector),
[Microsoft C# extension](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csharp),
[ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol/2.2.0),
[Microsoft.Extensions.Hosting](https://www.nuget.org/packages/Microsoft.Extensions.Hosting/10.0.12).
