# Подготовка DGK через .NET MCP

Новый `prepare_owned_dgk_export` зарегистрирован через
`OwnedDgkTargetTools.Create(session)` в `ToolCatalog.Create(session)`;
`Program.cs` подключает этот каталог через `.WithTools(ToolCatalog.Create(session))`.
Применены dotnet-mcp-builder/tool-primitive:Descriptions, structured CallToolResult,
async cancellation, SDK transport и дополнительный StrictTool input schema.

## Область инструмента

Требуются full mode и строгий bool `ownedDgkTargetsExperimental`, который зависит
от entitySetsExperimental/workingCopiesExperimental/documentBindingsExperimental.
В read-only каталог/прямые вызовы этого инструмента запрещены. Execute/verify DGK
tools не добавлены: их production COM provider/settings/selection qualification
остаётся следующим этапом. Подготовка выполняет только filesystem recipe.

Вход: private entitySetHandle/workingDocumentBindingHandle той же связи и exact
parameters из OWNED-DGK-TARGETS.md. Без raw IDs, paths, command strings, implicit
frame или выбора всей модели. Geometry_features требует явного обязательного
feature companion. MAC/XML рассматриваются как будущие файлы и не исполняются.

SDK запрашивает только fixed recipe.prepare/prepare_owned_dgk_export; native
QueueOperations проверяет flag/schema и вызывает filesystem-only Prepare.
Никакого COM подключения эта recipe не создаёт. Native/Python config rejects
coerced flags/dependencies; legacy Python tool catalogue не расширяется.

## Проверка ответа и lease

SDK проверяет original bridgeRequestId==targetId, raw target SHA/receipt equality,
exact schema, original binding/set/source context и key set, min TTL, requested
parameters/canonical digest, fixed bundle path и полный manifest, reservation,
owned layout/checkpoint ReadOnly/current saved bytes, claim/fences и пустой bundle.

Directory identity читается через Windows handles/final paths и сравнивается
с native записью. Это point-in-time SDK read; долгие retained directory/CAD
guards остаются native ответственностью. Native ownership file identities и
live context не заменяются SDK byte/layout validation.

Complete returned rotation fence допускается только при совпадении original
rotation plan/journal/aggregate/per-target returns/summary/claim и свежем source
после return. Unknown/partial/foreign drift отвергается. Fence не удаляется.

Private ownedDgkTargetHandle выдаётся только после проверки ответа и сохранённых
dependencies. TTL ограничен исходными leases; cross-client, context revocation,
expiry, proof/file drift не возобновляют handle. Handle не даёт права исполнения.
Timeout/cancel/tamper после submission дают outcome_unknown без handle/повтора;
local artifacts/исходный request ID сохраняются для диагностики.

## Версии и квалификация

Source native/Python0.34.2-dev,SDK0.39.5-dev; installed runtime0.33.5-dev не менялся.
ModelContextProtocol2.2.0/.NET10 pins/STDIO stdout discipline сохранены.
Dedicated profile:config/general-owned-dgk-targets.json, source only.

26 SDK store/6 MCP route/5 config cases passed. Actual native42 target cases/
477 hashes,SDK compatibility20 cases/0 mismatches. Первая compatibility проверка
обнаружила2 erroneous accepts replaced directories; исправлена actual identity
correlation,failed evidence сохранено. Native1 config control/5 refusals и8 Python
configuration tests passed. Full SDK pipeline включает новый compatibility check.

Далее qualified production export/settings/native selection provider, full-mode
execution/independent verification stores/routes/wire, concrete согласованный
живой own-copy DGK export/import/geometry roundtrip; остальные К8–К11 сохраняются.
