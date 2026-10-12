# К1 — воспроизводимая база

12 октября 2026. Локальный чистый экспорт commit
`ed39833dbb5aa6e1c8d966a981beabeae21ddde0` проверен без PowerShape.
Windows CI ещё ожидается; К1 не закрыт до его результата.

| Проверка | Результат |
|---|---|
| Framework x64 panel/worker/launcher | Сборка с явным config/general-owned-translations.json, версия 0.31.0 |
| Native synthetic tests | 312 проверок; фиктивные документы, claims, fences и журналы |
| Python правила | 114 тестов в десяти обязательных наборах |
| MCP fake protocol | Lifecycle, allowlist, malformed input, timeout/cancel, IPC envelope, image integrity — PASS |
| Реальная файловая junction | Отказ до копирования и CAD-подключения — PASS |
| Deployment plan | Valid plan; tampered artifact и compiled version mismatch отклонены — PASS |

Воспроизведение: новый checkout без `releases`, установленный Python 3.12,
Framework 4.8+; `verify-baseline.ps1 -PythonPath <полный путь python.exe>`.
Скрипт специально отказывается работать поверх существующего `releases`.
Используются только синтетические файлы; исходная модель не требуется.

Raw evidence этого запуска:
`work/K1-clean-checkout/evidence/K1-baseline/` — baseline.json, code/ с hashes,
worker-tests.txt, Python logs, protocol.log, deployment.json и junction.json.
Исходные файлы synthetic journals остаются в staging `work/K1-baseline/work`.
Все журналы формируются заново, старые файлы P2.31 не нужны.

В offline deployment test используется scoped пустой process fixture,
продуктовый deploy-general.ps1 не меняется, `-Apply` не вызывается.
Это проверка manifest/config/version, не доказательство готовности живой панели,
установки/restart или CAD-паритета. Их принимаем на следующих контрольных точках.

Workflow: `.github/workflows/baseline.yml`, Windows 2022, Python 3.12,
минимальное право contents:read, actions закреплены по SHA. CI artifact сохраняет
code evidence даже при ошибке. Runtime и CAD в этом блоке не развёртывались.
