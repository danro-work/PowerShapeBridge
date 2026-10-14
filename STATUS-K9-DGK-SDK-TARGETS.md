# К9 — SDK подготовка собственного DGK-комплекта

Добавлены definition/store/tool/session/catalog/feature flag и native fixed
filesystem recipe. Source native/Python0.34.2-dev,SDK0.39.5-dev. No install/CAD.
Contract:SDK-DGK-TARGETS.md;public name prepare_owned_dgk_export/full-mode only.
Native producer/provider export/execute не включён этим flag.

37 focused .NET tests pass:26 store,6 MCP routes,5 config;0 skips.
69 related DGK/owned-scale regressions passed до directory hardening;после fix
37 DGK focused passed.8 Python config tests passed. Native42/477 passed.
Actual native SDK comparison20 initially2 mismatches(directory_replaced,
bundle_replaced):SDK не сверял physical directory identity. Добавлен Windows
handle/final path identity read;fixed20/0mismatch pass. Оба reports сохранены:
evidence/K9-dgk-sdk/native-sdk-compatibility*.json. Production guards не ослаблены.

Native config matrix1 control/5 strict bool/dependency refusals passed:
evidence/K9-dgk-sdk/native-config-matrix.json. Full pipeline86800 terminal PASS:
651 .NET tests/0 skips,531 native,175 native artifact compatibility/0 mismatch
иSTDIO/faults/rotation+scale native wire/DGK/Inspector passed. Terminal marker
finished2026-10-14T14:22:42Z. После исчезновения session handle процесс не
перезапускался;marker и отсутствие процесса подтвердили завершение. Все source
fingerprints marker сверены с текущими файлами перед публикацией.

Directory/source/binding/set/owner/checkpoint/current working bytes, target/
reservation/manifest/parameter SHA/private lease/mode/cancel/no-replay guards
проверены. No public execute/verify DGK;native COM provider/live layout/settings/
geometry qualification pending. Installed0.33.5 и пользовательская CAD не менялись.

Следом terminal full pipeline,отдельная publication/CI;actual scoped native queue
STDIO wire и provider qualification,затем согласованный live own-copy DGK roundtrip.
General transforms/organization/sections/К10 five-model matrix/К11 acceptance
остаются в полном плане.
