# К8 — SDK подключение passive owned rotation packet

Source SDK0.39.1-dev/native+legacy0.34.0-dev. Installed native0.33.3-dev,
READY K7 packages SDK0.38.1/native0.33.5 иdefault MCP не изменялись.
Direct human K7 live approval всё ещё pending.

Новый prepare_owned_rotation(rotationPlanHandle,workingDocumentBindingHandle)
зарегистрирован через OwnedRotationTools.Create(session) вToolCatalog.Create.
Флаг ownedRotationPlansExperimental требует rotationPlans/documentBindings/copies;
read-only иdefault profile не содержат инструмента. Exact two-token schema
запрещает arbitrary paths/native IDs/commands. Параметры native private recipe —
rotationPlanId/SHA,bindingId/SHA, после recheck scoped plan/binding dependencies.

Native queue имеет отдельный feature gate/validation иheld-context preparation
через OwnedRotationPlans.Run. Нет RotateItem/Exec/selection/save; лишь observed
binding/ownership/checkpoint guard иimmutable disabled packet/prepared journal.
Native0.34 source/staging не развёрнуты. Python metadata/config policy согласованы
с native version; новый public primitive находится только в C# SDK.

OwnedRotationStore проверяет immutable packet SHA/body/schema/flags, request ID,
rotation/binding dependencies, native digests, copy/owner identities, prepared-only
journal иminimum dependency expiry. Scoped handle не является execution authority.
Late/cancel proof отзывается только новый handle. Lost reply даёт неизвестный
исход подготовки безhandle; никаких автоматических повторов.

14 domain/official SDK client-server cases passed: cross-client/revoke/expiry,
tampered packet/binding/journal, flags/request identity, mode/feature/path gate,
actual private queue route иlost reply. evidence/K8-owned-rotation-sdk/domain,
sdk-routes. Native15 matrix203 hash audit прошли вnative.
SDK проверил Receipt proofs15 actual native cases безизменения файлов;
native-sdk-compatibility.json. Это proof compatibility; whole native transport
with scoped Issue нареальном fake native context остаётся отдельной проверкой.

Full runner session35683 completed PASS in evidence/K8-owned-rotation-sdk/sdk-full,
stage work/K8-owned-rotation-sdk-full2:417 .NET/531 native/175 cases137 accepted
0 mismatch/32 SDK faults/25 native faults/20 activation/40 legacy STDIO/22 checkpoint/
6 recovered OPEN/7 checkpoint wire/6 filesystem wire/15 owned rotation cases203 hashes/
15 native-SDK Receipt checks/rotation STDIO/Inspector passed. Все recorded hashes
сверены. Native recipe использует толькоread/preparation.
No CAD contact/rotation/runtime deployment performed during offline tests.

PR26/source3a6ff520 four CI passed38440274139/38440258495 baseline,
38440274269/38440259145 SDK. Следом full qualification/exact-source CI,
сквозная SDK/native scoped-packet interoperability, guarded executor/verifier,
rotation sign/frame/geometry/recovery иsaved-result/reopen. К7/К8–К11 incomplete.
