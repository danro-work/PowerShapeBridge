# К8 — uniform scale reference predicate

Добавлен src/UniformScaleSampleVerifier.cs, shared C#6/.NET код независимой
математической проверки positive uniform scale. Явные world centre/factor,
immutable sample collections, anchored positions и degree0/1 directions,
named degree0..3 metrics с individual raw absolute tolerances, exact positive
uniform paired bounds и detectable-change criterion. Overflow/underflow,
context/unit/domain/anchor drift и неразличимый no-effect отклоняются.

PASS:37 .NET tests,0 skips, сборка host/tests без ошибок.
Evidence:evidence/K8-uniform-scale-samples/dotnet/scale-samples.trx.
PASS:Framework x64 standalone4 analytic controls/5 refusals.
Evidence:evidence/K8-uniform-scale-samples/framework/framework-qualification.json.
PASS:51 existing rotation predicate/DMT reader regression tests,0 skips.
Evidence:evidence/K8-uniform-scale-samples/regression/geometry-regression.trx.
Полная локальная .NET regression или новый Windows CI этим блоком не заявляются.
Runner verify-uniform-scale-samples.ps1 включён в verify-sdk; source hashes
добавлены в manifest pipeline. Числа здесь не являются live CAD acceptance.

Контракт:K8-UNIFORM-SCALE-MEASUREMENT-CONTRACT.md. Autodesk Product Help2023
Uniform scaling проверен в en_US:GUID-F1C47DA1-CD1D-4DA1-AC0E-9E85B95BC7D5.
Native origin defaults не применяются:centre всегда явный. Документация говорит
о primitive conversion; новое native mapping нельзя угадывать по старому key.

Модуль не соединён с native provider/executor, queue или public MCP tool.
Сейчас source включён в .NET host; Framework совместимость проверяет отдельная
CLI, а installed worker не менялся. Read-only catalog/mutation separation и
existing journal/fences остаются в прежнем состоянии. CAD не подключался.
Никакой fence не освобождён, геометрическая/topological квалификация не заявлена.

Следом:owned scale plan и native recipe/mapping qualification, независимая
проверка saved result/reopen, необходимые transport/recovery fault scenarios.
Параллельные обязательные направления полного плана сохраняются:rotation,
selection/levels/workplanes, DGK exchange/sections/intersections и К10–К11.
DMT settings не являются обязательным блокером всех этих направлений.
К7 live сценарий остаётся ожидающим согласования; runtime и READY packages
не изменены. Изменения локальные, публикация ждёт восстановления automatic
approval review после usage-limit failure; обхода не было.
