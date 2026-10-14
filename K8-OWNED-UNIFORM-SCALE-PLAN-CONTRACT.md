# К8 — подготовка равномерного масштаба собственной копии

OwnedUniformScalePlans.Prepare — internal backend preparation с getter-only
DocumentBindings.IContext. В source0.34.1/SDK0.39.4 добавлены gated passive
prepare_owned_uniform_scale recipe/COM Run/queue/MCP primitive;не установлены и
не квалифицированы в живом CAD. Параметры и ссылки проходят проверку до context observations.
Пакет executionEnabled=false/nativeProviderQualified=false не разрешает изменение.

## Вход и параметры

Exact input:entitySetId/entitySetSha256,bindingId/bindingSha256,parameters.
IDs32 lowercase hex/SHA64. Parameters exact:factor,centre,coordinateFrame,
lengthUnits,mode. Числа только JSON numeric, не bool/string; finite factor>0,
factor!=1,finite centre[3],world/document_raw/scale_in_place. Нет implicit origin,
axis locks, arbitrary command или keep-original. Reflections и другие профили
ещё не квалифицированы; этот experimental positive profile не сокращает общий v1.
Все значения копируются до external getter callbacks, включая centre vector.

Entity set фиксирует не более128 явных unique native keys SOLID40/SURFACE21/
CURVE59 и исходный inspection SHA. Binding фиксирует свою исходную inspection,
ownership и window context. Два источника должны иметь одинаковые наблюдаемые
документ/свойства/метаданные; keys встречаются в inventory ровно один раз.
Expiry=min(set,binding), остаётся связанным с исходными timestamp/TTL. World frame
и empty selection обязательны; никаких выделения/активации в Prepare нет.

## Guard и immutable packet

Global executor lock; held source/set/binding/checkpoint/ownership proofs.
OwnedCopies.Hold проверяет file identity, distinct source/working/checkpoint,
ReadOnly независимый checkpoint. Current working bytes могут законно отличаться
от initial ownership bytes до нового guard. Для Prepare отдельный read handle с
FileShare.Read запрещает запись/удаление working file на всё время проверок;
lease.Recheck закрывает окно между получением lease и этого handle.
Если CAD/другой writer уже держит несовместимый доступ, Prepare отказывает.
Это блокировка дискового файла, не доказательство неизменности unsaved CAD state.

Проверяются отсутствие mutation-fence и geometry-export-fence, затем первый
retained guard (2 observations). Перед публикацией отсутствие fences проверяется
снова. Immutable ownedscales/<id>.json закрепляет параметры, keys, оба inspection
SHA, set/binding/owner/working SHA, observed/window digests, raw units/factor и TTL.
NativeJournal содержит только prepared, plan SHA совпадает с SHA пакета.
Пакет и prepared event удерживаются против записи. Второй retained guard даёт
ещё2 observations; затем lease/document/windows/expiry/fences проверяются снова.

Успех:owned_uniform_scale_prepared_observed,guardPasses2,observationReads4.
Никаких once claim/execution callback/mutation fence Prepare не создаёт.
При позднем отказе созданный immutable packet/journal сохраняются:returned state
остаётся rejected, поздний артефакт не является разрешением изменения.
При любой ошибке нет cleanup/replay/repair и изменения защищённых исходников.

## Ещё требуется

Fixed qualified native uniform recipe и measurements, exact target guards,
primitive conversion/remapping proof, once claim/fence, independent scale
postconditions, saved-result/reopen, recovery/transport faults. SDK session store/
wire прошли offline квалификацию STATUS-K8-OWNED-SCALE-SDK-WIRE.md, но не native
scale execution. K8-UNIFORM-SCALE-MEASUREMENT-CONTRACT.md задаёт reference predicate,
но не заменяет эти доказательства. Selection/levels/workplanes и полный К8–К11
остаются обязательными. DGK/PSMODEL — CAD exchange/result; DMT вспомогателен.

[Autodesk PowerShape 2023 Uniform scaling](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-F1C47DA1-CD1D-4DA1-AC0E-9E85B95BC7D5)
подтверждает роль центра и conversion primitive surfaces/solids. Этот контракт
не предполагает документированный ScaleItem getter/method:ничего такого не
изобретено и actual native command не вызвана.
