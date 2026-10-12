# Контрольные точки MCP v1

12 октября 2026. Источник объёма — MCP-DEVELOPMENT-PLAN.md и MCP-V1-CONTRACT.md.

| Точка | Состояние | Доказательство / следующий шаг |
|---|---|---|
| К0 | Принято пользователем | План принят к работе до К11; Product Help `en_US` разрешён |
| К1 | Выполняется | verify-baseline.ps1, Windows baseline CI; результаты ещё не получены |
| К2 | Не начато | SDK STDIO-host с двумя диагностическими tools и fake-panel |
| К3 | Не начато | Шесть read-only tools, handles и запреты прямых мутационных вызовов |
| К4 | Не начато | Перенос safety stores с byte/hash parity |
| К5 | Не начато | Паритет owned open/translation; решение о live переключении |
| К6 | Не начато | SaveAs результата, saved bytes и отдельный reopen |
| К7 | Не начато | Interrupted recovery и fault matrix |
| К8 | Не начато | Общие transforms и organization |
| К9 | Не начато | Planes/intersections/exchange/observation |
| К10 | Не начато | Все обязательные операции на пяти классах моделей/сеансов |
| К11 | Не начато | Release candidate, установка/rollback и приёмка пользователя |

К1 запускается в новом checkout без `releases`, командой:

```powershell
./verify-baseline.ps1 -PythonPath (Get-Command python.exe).Source
```

Python должен быть настоящим интерпретатором. Скрипт отказывается работать
поверх установленного runtime. Создаёт собственный staging, непрозрачные
файловые fixtures и искусственные native journals. `--self-test` не подключается
к PowerShape; deployment проверяется только в plan mode с пустыми process fixtures.
Производственный deploy-general.ps1 не изменён. Raw evidence остаётся локально
в `evidence/K1-baseline`, CI публикует его как artifact своего запуска.
