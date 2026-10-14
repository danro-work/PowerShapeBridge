# К8 — own scale SDK/native preparation wire

SDK source0.39.4-dev / native source0.34.1-dev; installed runtime не обновлялся.
prepare_owned_uniform_scale registered через OwnedUniformScaleTools.Create в
ToolCatalog.Create → Program.WithTools(ToolCatalog.Create(session)). Config:
general-owned-uniform-scale-plans.json, explicit ownedUniformScalePlansExperimental.
Host requires local scale plans/sets/copies/bindings; native/Python require general
sets/copies/bindings. Native ParseConfig строго проверяет JSON boolean нового флага
и enabled dependencies, не полагается на coercion JavaScriptSerializer.
Нативный Run использует retained getter context и passive Prepare; scale/selection/
activation/save/screenshot callbacks отсутствуют. Production recipe не запускался.

OwnedUniformScaleStore сверяет original bridgeRequestId, exact packet/schema/SHA,
параметры/set/binding/inspection refs, required qualification list, flags false,
two guards/four observations, prepared-only journal/plan SHA, source/raw units,
TTL=min(set,binding) и monotonic private handles. Ownership bytes/SHA/fixed layout,
current working bytes, independent ReadOnly checkpoint и отсутствие fences также
проверяются. File identity/CAD retained-context остаются обязанностью native guard;
SDK byte reads не выдаются за continuous identity/topology proof. Validation handle
повторяет проверки, Revoke/Clear и session context change инвалидируют handles.
Пакет по-прежнему executionEnabled=false/nativeProviderQualified=false.

PASS:25 store .NET tests/0 skips, с настоящими own fixture bytes, без CAD.
PASS:7 official SDK route tests/0 skips:full/read-only/disabled catalog, exact wire,
lost reply/tamper/changed working bytes, original request ID/no replay/no handle.
PASS:53 related SDK regressions/0 skips;7 Python core-config tests passed.
PASS:32 native preparation cases/395 independently checked hashes и4 strict-config
refusals;marker denial до fixture creation. Full wire final built after config fix.

PASS:6 independent STDIO scenarios through actual x64 Framework parser/owned files/
guards and SDK scoped Issue. control,changed_process,fenced_copy,late_context,
lost_reply,packet_tamper. Independent audit237 hashes/30 exact requests. Exactly5
native passive recipes per case (copy/read/bind/set/owned preparation);local SDK
scale plan не создаёт native request. Protected seed/source/checkpoint сохранены,
commandCalls0, foreign handles/extra command/read-only refused. Missing reply
сохраняет prepared artifacts/unpublished reply и не повторяет запрос.
Evidence:evidence/K8-owned-scale-sdk/store/routes/regression/config-native/
full-wire-final;TRX и wire-audit.json. Старый full-wire сохранён как earlier run.

Полная .NET/native regression в разрешённом user context, session97044 terminal:
PASS576 .NET tests/0 skips и531 native checks, оба exit0. Это новый результат
на source SDK0.39.4/native0.34.1;не перенесён с прошлого build.
Evidence:full-regression/all-sdk-tests.trx,native-stdout.txt,terminal-result.json.
Runner/source hashes включены в verify-sdk; new CI этого локального блока ещё нет.

Контракт:K8-OWNED-UNIFORM-SCALE-PLAN-CONTRACT.md. Нужны квалифицированные recipe/
native measurements/conversion mapping, once claim/fence, independent geometry,
saved-result/reopen, recovery/faults и live матрица. Общий К8–К11 не завершён.
DGK/PSMODEL сохраняют роль CAD exchange/result, DMT supplemental only.
Autodesk Product Help2023 Uniform scaling проверен en_US:
https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-F1C47DA1-CD1D-4DA1-AC0E-9E85B95BC7D5
Primitive conversion требует fresh identity/mapping proof, а не старого key.
Официальный Autodesk PSEntityScaler.ScaleUniform изучен по pinned commit
f0761e50ae833e7647423b8da3ba24bfc23bd93d, Helpers/Scaler/PSEntityScaler.cs,
SHA256 d745518caee4ed49cb5f12d6a939d95118c08369a89fd97b618d24b15baf6ffa.
https://github.com/Autodesk/PowerShapeAndPowerMillAPI/blob/f0761e50ae833e7647423b8da3ba24bfc23bd93d/Delcam.ProductInterface.PowerShape/Helpers/Scaler/PSEntityScaler.cs
Helper меняет selection, вызывает EDIT SCALE/UNIFORM/VALUE, задаёт axis locks,
optional SCALEORIGIN, затем APPLY/DISMISS для major>11. Нет явного NOKEEP и
независимой проверки результата; factor concatenation и Point.ToString не
доказывают invariant numeric locale. AddToSelection также может активировать
слой (ранее изученный PSEntity). Эти побочные эффекты не переносить вслепую:
нужны explicit keep/locks/centre/selection guards и fault cases каждого шага,
без заявления, что apply — единственная возможная точка изменения/preview.
Исходник не запускался и хранится только в ignored work как research snapshot.
К7 live approval pending;READY packages/runtime/CAD не менялись. Текущие scale/
export/selection изменения локальные, header PR37/f7b5d67 all4 CI passed separately.
