# Контрольные точки MCP v1

12 октября 2026. Источник объёма — MCP-DEVELOPMENT-PLAN.md и MCP-V1-CONTRACT.md.

| Точка | Состояние | Доказательство / следующий шаг |
|---|---|---|
| К0 | Принято пользователем | План принят к работе до К11; Product Help `en_US` разрешён |
| К1 | Пройден | STATUS-K1-BASELINE.md: clean checkout, 312 C#, 114 Python; оба GitHub Windows CI успешны |
| К2 | Пройден | STATUS-K2-SDK.md: clean checkout, 45 .NET tests, STDIO/Python journal parity, Inspector; все Windows CI успешны |
| К3 | Пройден | SDK 0.33.0-dev: clean checkout, 98 .NET tests, independent Python parity/Inspector; все CI успешны |
| К4 | Пройден | SDK 0.34.0-dev full/read-only 16/6; clean checkout/CI, 141 .NET, 312 native/63 artifact cases |
| К5 | Технически пройден, user decision pending | SDK0.35.1/native0.32 clean/CI/deploy/live unique OPEN + one SOLID/independent audit passed; 162 .NET/320 native/78 artifacts; K5-SDK-SWITCH.md, default switch ещё не принят |
| К6 | Выполняется | SDK0.36.3 one-use saved reopen + separate verifier/new binding: 239 .NET/514 native/171 artifacts, seven STDIO/Inspector, profile27/6; clean baseline/SDK4a230a6 и четыре Windows CIef999f3 passed. Staged deployment/live К6 впереди. STATUS-K6-SAVE-AS.md |
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
