# Публичное чтение слотов слоёв

`inspect_model_levels` — параметров нет; доступен в full и read-only при
`levelReadingExperimental: true` в general-v1 profile. Отдельный
`config/general-levels-read.json`; default profiles/catalogs не расширены.
Native fixed `recipe.prepare/inspect_model_levels/{}` не допускает другие
аргументы, raw paths/commands и fixture fallback. Matching source native34.4
и SDK39.7; installed33.5 не обновлён и новый recipe не поддерживает.

Регистрация: `ToolCatalog.Create(session)` включает
`LevelReadTools.Create(session)`; Program.WithTools(ToolCatalog.Create(session)).
SDK strict empty schema, description/readOnly annotations и admission gate.

Native retained context читает1000 slots0–999 и filter flags двумя проходами,
сверяет occupied inventory, scalar samples, model/windows/lifetime.
SDK проверяет complete arrays/order/types, flags ровно0/1, model/process/window
identity, одинаковые before/after properties/selection/context, capture/mutation
refusal и root receipt timestamp. Ответ `model_levels_inspected_read_only`
с именами и raw flags; nativeLayoutQualified=false, edit handles не выдаются.

USED=0 не разрешает reuse/delete/rename: это slot observation, а не allocation
или ownership guarantee. Raw value types сохранены; Flag не принимает strings,
Bool/fractions или другой диапазон. No filters/visibility/selection/activation/
geometry/save/screenshot/mouse. Прямой tools/call с лишним аргументом отвергается.

Actual live qualification: official C# STDIO readonly host → один fixed native
request → attach-only QueueOperations.Run → existing PowerShape getters.
Ни panel, ни worker Main не запускались. Один K7 own document,1000 slots;
independent native↔SDK data audit/windows7/selection/saved bytes/11 protected
hashes passed. Другие модели и general level/workplane edits не приняты.

Evidence: `evidence/K8-levels-public-live/transcript.json`, `calls.json`,
`independent-audit.json`; `work/K8-levels-public-read/requests` и `responses`.
26 focused C# tests passed. Full SDK regression passed678.NET/531native/175compat/
29level cases/Inspector; full К8–К11
и release/user acceptance остаются обязательными.
