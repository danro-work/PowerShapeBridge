# К8 — SDK session-bound uniform scale plans

Добавлен internal UniformScaleDefinition/UniformScalePlanStore в .NET host.
Exact JSON parameters совпадают с internal native preparation contract:
positive non-noop factor,explicit centre[3],world/document_raw/scale_in_place.
Normalised values заморожены отдельно от caller JSON. Plan фиксирует exact set,
source identity/observed digests,raw units,TTL и requirements;native method не
придуман. Connection-local handle нельзя использовать в другом экземпляре store.

Каждое Inspect проверяет set/source/dependencies, план по исходному SHA и
prepared-only journal/event count/writer lock/plan SHA. Revoke/Clear/expiry и
128-plan cap запрещают дальнейшее использование. Подготовка пишет только локальный
immutable plan/journal, не native queue. Ошибки не вызывают repair или cleanup.
Store подключён к BridgeSession и strict prepare_uniform_scale_plan tool в
SDK0.39.3-dev. Registration:UniformScalePlanTools.Create включён в ToolCatalog.Create,
а Program использует .WithTools(ToolCatalog.Create(session)). Explicit boolean
uniformScalePlansExperimental требует entitySetsExperimental/operationPlansExperimental;
read-only всегда исключает tool. RevokeCadLeases/Dispose очищают scale handles.
Status при наличии flag сообщает experimental_local_plan_only_execution_disabled.
В config/general-uniform-scale-plans.json этот отдельный профиль включён явно.
Own native queue preparation/execute/verify ещё не wired; live масштаб не выполняется.

PASS:26 SDK store tests,0 skips; host/tests .NET build без ошибок.
Evidence:evidence/K8-scale-sdk-store/dotnet/scale-plan-store.trx.
PASS:64 regression tests (RotationPlan/OwnedRotationStore/UniformScaleSamples),
0 skips:evidence/K8-scale-sdk-store/regression/scale-sdk-regression.trx.
Mixed native types сохранены,invalid bool/string/zero/negative/noop/centre/frame/
units/mode/axis lock отклонены до записи. Foreign/expired/revoked/cleared/tampered
source/plan/journal/interrupted writer rejected. Caller/result aliases не меняют
замороженные параметры. SDK verification source manifest расширен.

PASS:12 official SDK route tests/0 skips. Catalog/read-only/disabled flag,
exact nested schema/semantic factor rules, context revocation, cancellation before
write и configuration dependencies проверены. Evidence:routes/scale-plan-routes.trx.
PASS:39 store/related route regressions/0 skips:route-regression/scale-route-regression.trx.
Independent STDIO прошёл на отдельном fake panel:explicit centre/factor, mixed
SOLID/SURFACE/COMPCURVE set, full plan bytes/SHA, set/source SHA и prepared journal.
Noop/negative/bool/command/short-centre и read-only direct call отклонены;
artifact inventory unchanged, ровно2 native passive read/set requests и ни одной
scale/native mutation команды. Evidence:stdio/uniform-scale-plan-stdio.json.
Existing rotation STDIO regression passed:rotation-stdio-regression/rotation-plan-stdio.json.
Эти paths относительны evidence/K8-scale-sdk-store. verify-sdk сохраняет отдельный
exit-code gate сразу после каждого STDIO script; ошибки одного не маскируются другим.

Reference math и native coupled preparation остаются отдельными доказательствами:
37 math .NET tests;32 native fake cases/395 hashes. Native recipe/measurement/
primitive remapping/live acceptance/saved result/recovery/faults ещё нужны.
Полный К8–К11 и отдельный DGK exchange не заменяются SDK store.
CAD/runtime/READY К7 не менялись;live К7 approval pending.
