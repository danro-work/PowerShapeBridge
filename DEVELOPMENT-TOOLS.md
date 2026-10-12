# Инструменты разработки PowerShape Bridge

Проверка среды и чатов: 12 октября 2026. Текущее состояние продукта —
`PROJECT-STATE.md`, P2.31 / 0.31.0; общий MCP v1 ещё не завершён.

## Git и GitHub

Установлены и проверены Git for Windows **2.55.0.windows.5**, Git Credential
Manager **2.9.1** и GitHub CLI **2.102.0**. Инициализирован локальный репозиторий
с веткой `main`, `core.longpaths=true`, `core.autocrlf=false`; окончания строк
задаются `.gitattributes`. Навык установлен с полным каталогом `references` в
`C:\Users\rozhdestvenskii.da\.codex\skills\dotnet-mcp-builder` из commit
`70ddff7fab9d4d49743d221b56d761cff58048d0`, SHA256 `SKILL.md`:
`7860696a124e61fc1fa6bb38f9c79f6be6eda130e245a814e41f0e7030d09619`.
Он доступен Codex со следующего сообщения.

Git-репозиторий предназначен для исходников `src`, общих профилей `config`,
скриптов сборки и актуальных контрактов/отчётов в корне. `.gitignore`
сохраняет вне Git установленный `releases/P2.6`, `work`, рабочие CAD-файлы,
очереди, бинарники и сырые доказательства `evidence`. Файлы на диске не удаляются.
Отчёты STATUS и хеши остаются доступны; перед публикацией конкретных моделей
или raw evidence нужен отдельный отбор и проверка. Чистый clone не содержит
локальный runtime, защищённые модели и полный архив квалификации.
Исторические SHA256 в MANIFEST/доказательствах относятся к прежним снимкам:
после checkout с нормализованными окончаниями строк они не доказывают совпадение
содержимого текущей сборки. Нужны свежие хеши её staging manifest.

Автор локальных коммитов настроен по подтверждённому профилю GitHub:
`danro-work`, `341348152+danro-work@users.noreply.github.com`.
Применён [официальный формат GitHub noreply](https://docs.github.com/en/account-and-profile/reference/email-addresses-reference).
Настройка относится только к этому репозиторию. Remote зависит от выбранного
GitHub-репозитория. Не использовать придуманную личность автора. Не хранить
пароли и токены в Git, конфигурациях Bridge или тексте команд.

Проверка инструментов в новой PowerShell:

```powershell
git --version
gh --version
gh auth status --hostname github.com
git status --short
git remote -v
```

Если Codex выбирает свой встроенный Git, установленный Git for Windows
можно вызвать явно: `& "$env:ProgramFiles\Git\cmd\git.exe" --version`.
GitHub CLI: `& "$env:ProgramFiles\GitHub CLI\gh.exe" --version`.

Подключение GitHub в Codex подтверждено чтением профиля `danro-work`.
При проверке доступных этому подключению репозиториев список был пуст.
Это не утверждение об отсутствии любых репозиториев аккаунта: подключению
может требоваться предоставление доступа к выбранному репозиторию.

Вход выполняется пользователем через `gh auth login --hostname github.com
--git-protocol https --web`. После успешного входа `gh auth setup-git`
подключает GitHub CLI как credential helper. Подключение GitHub в Codex
и авторизация локального `gh` — два отдельных подключения.

Обычный цикл: новая ветка → изменение → необходимые проверки → commit →
push ветки → pull request → review. Создание remote или публикация проекта
не происходят без определённого пользователем репозитория.

## Что уже есть для текущей реализации

| Инструмент | Проверенное состояние | Роль |
|---|---|---|
| .NET Framework | Release 533325, версия 4.8.09037; x64 csc/MSBuild 4.8.9037.0 | Существующий WinForms/STA/COM worker |
| VS Code | 1.107.1 x64, `D:\Program Files\Microsoft VS Code` | Редактор; пользовательских расширений при проверке не найдено |
| Python | 3.12.14 во встроенном runtime Codex | Текущий MCP stdio и проверки, стандартная библиотека |
| Node.js | 24.19.0 во встроенном runtime Codex | Потенциально Inspector; npm/npx в обычном PATH не найдены |
| PowerShape 2023 | ProductVersion 23202, ожидаемая версия Bridge 23.2.2 | Лицензированный CAD в пользовательском Windows-сеансе |

В обычном PATH `python` сейчас указывает на WindowsApps alias. Рабочий Python:
`C:\Users\rozhdestvenskii.da\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe`.
Для независимой от Codex среды полезна отдельная установка Python.

В проекте пока нет `.csproj`/`.sln` и пакетных манифестов. `build.ps1` вызывает
`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` напрямую.
Для существующего worker modern .NET SDK, полный Visual Studio, отдельный
NuGet CLI, pywin32, pytest или Python-пакет MCP не обязательны.

Сборка из чистого checkout должна явно использовать отслеживаемый профиль:

```powershell
.\build.ps1 -OutputDirectory .\work\build-staging `
    -ConfigurationPath .\config\general-owned-translations.json
```

Обычный default build читает конфигурацию из локального `releases/P2.6`;
этого каталога в clone нет. Сборка создаёт staging и не развёртывает runtime.
`deploy-general.ps1 -Apply` меняет runtime/панель и является отдельной операцией.

## Дополнительные инструменты по приоритету

| Инструмент | Зачем | Статус/условие |
|---|---|---|
| .NET 10 SDK | Новый C# MCP-адаптер по dotnet-mcp-builder | SDK отсутствует; установлен только .NET runtime 8.0.25. Установка SDK — следующий шаг для C#-адаптера |
| ModelContextProtocol, стабильная 2.x | Официальный C# MCP SDK, stdio transport и схемы tools | Зависимость будущего `.csproj`, фиксировать точную проверенную версию |
| Node/npm/npx + MCP Inspector | Независимый клиент: negotiation, catalog, tool schemas, ответы/ошибки | Inspector требует Node >=22.19.0; встроенный Node подходит по версии, нужен доступный npm/npx |
| Расширение C# для VS Code | Навигация, диагностика и отладка C# | Рекомендуется; не требуется для нынешней csc-сборки |
| GitHub Actions | Воспроизводимые проверки без CAD на Windows runner | Настраивать после разделения автономных тестов и локальной квалификации |
| .NET Framework Developer Pack/Build Tools | Targeting pack, MSBuild и отладка при переходе worker к `.csproj` | При необходимости; сейчас csc уже работает |

Официальные источники:

- [dotnet-mcp-builder](https://github.com/github/awesome-copilot/blob/main/skills/dotnet-mcp-builder/SKILL.md)
- [C# SDK ModelContextProtocol](https://github.com/modelcontextprotocol/csharp-sdk)
- [Установка .NET SDK на Windows](https://learn.microsoft.com/en-us/dotnet/core/install/windows)
- [MCP Inspector](https://github.com/modelcontextprotocol/inspector)
- [Вход через GitHub CLI](https://cli.github.com/manual/gh_auth_login)

## Применение навыка и существующая архитектура

Навык рекомендует .NET 10 для нового проекта и актуальную стабильную 2.x
линию ModelContextProtocol. Применять его к отдельному современному MCP-хосту,
который обращается к существующему исполнителю через квалифицированный IPC.
Сохранять один COM-канал панели, STA, guard, одноразовые claims, durable journal,
fence и независимую проверку результата P2.31.

Это рекомендация по сохранению проверенной архитектуры, а не утверждение
о принципиальной несовместимости SDK с Framework: SDK также имеет
`netstandard2.0` target. Замена Python-адаптера C#-хостом требует отдельной
приёмки контрактов и клиентов. Перенос всего worker на modern .NET автоматически
не выполняется.

Сначала универсальный MCP v1; технологический модуль остаётся следующим
самостоятельным клиентом. Docker, Windows Service и второй COM-worker для
нынешнего процесса разработки не требуются.

## Основания из чатов и API

Прочитаны «Исследовать PowerShape API»
(`01a1165f-f18a-7053-8719-e79321590e03`) и «01 — Разработка MCP»
(`01a12001-7575-79e2-9b73-06df1c26bda9`). Первый содержит исходное ТЗ,
реестр 643 API members и квалификацию до P2.22; второй — продолжение до P2.31.
Современный MCP-процесс рядом с Framework STA-worker предусмотрен исходным ТЗ.
Проблемы COM из служебного сеанса означают необходимость пользовательского
сеанса для CAD-исполнителя, а не необходимость переустановки PowerShape.

Локальные API-материалы уже есть:

- `C:\Program Files\Autodesk\PowerShape 2023\sys\exec64\PowershapeNT.tlb`
- `C:\Program Files\Autodesk\PowerShape 2023\file\ole\help.chm`
- `C:\Program Files\Autodesk\PowerShape 2023\file\ole\data\IPshapeApp.htm`
- `C:\Program Files\Autodesk\PowerShape 2023\file\ole\data\IPshapeModelDoc.htm`

Исследованные Autodesk source commit:
`f0761e50ae833e7647423b8da3ba24bfc23bd93d`; пакет
`Autodesk.ProductInterface.PowerSHAPE 1.2.5` относится к исследованию, а текущий
worker использует dynamic COM без ссылки на Autodesk DLL. Обновлять API-пакет
без квалификации нельзя.

## Проверки и ограничения CI

Числа 114 Python / 312 C# / 55 MCP calls относятся к сохранённой приёмке P2.31;
при настройке инструментов они не запускались заново. CAD не изменяется.

Автономные проверки и тесты живого PowerShape должны быть раздельными.
`test_working_copy_open.py` имеет тест, зависящий от заранее созданного native
self-test journal в staging (`PSBRIDGE_TEST_STAGE` или `work/P2.29-open`).
Поэтому полный unittest discover не является готовым CI для чистого clone.
`test_mcp.py` запускает fake-panel проверки без live-аргументов; его live-режимы
и qualify-скрипты — отдельная разрешённая CAD-квалификация на собственных копиях.
Hosted GitHub runner не заменяет лицензированный PowerShape и его пользовательский
сеанс. Вызовы CAD и мутационные self-test не запускать при установке инструментов.

## Результат настройки Git

В индекс первого коммита подготовлены 138 файлов. Проверка исключений не нашла
в индексе CAD-моделей, runtime, work или raw evidence. `git diff --cached --check`
обнаружил 15 предупреждений о существующих пустых строках в конце файлов старой
базы; форматирование исходников при настройке инструментов не менялось.
Исходный локальный commit подготовлен с текущей базой P2.31 и настройками
инструментов; его фактический SHA записывается в receipt после создания.
Remote ещё не задан. На момент receipt отдельный вход в `gh` ожидает
пользовательской авторизации; профиль GitHub в Codex читается успешно.
Машинный PATH содержит установленные Git и GitHub CLI; уже открытая оболочка
может использовать старое окружение, поэтому выше даны явные пути.
Проверки установки и SHA сохранены в `reports/tooling-setup-2026-10-12.json`.
