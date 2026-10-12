# К6 — собственный SaveAs и reopen, выполняется

12 октября 2026. К5 technical pilot принят; user default SDK switch decision
остаётся pending. Published K5 SDK0.35.1/native0.32 и пользовательский config
не изменялись этим блоком. К6 source native0.33.0-dev собирается только в staging.
SaveAs/reopen команды, public recipes и SDK tool registration ещё не реализованы.

## Выполненный блок

OwnedSaveTargets закрепляет НОВЫЙ фиксированный Result_targetId.psmodel под
savedresults/targetId, неизменяемый target.json и одноразовую reservation.
Связь с binding/native inspection, copy ownership, рабочими saved bytes и
ReadOnly checkpoint проверяется по исходным SHA. При существующей цели,
прежнем claim, fence, чужом/изменённом binding или истечении срока — отказ.
Marker/частичные артефакты не удаляются production-кодом автоматически.

OwnedDirectoryLease держит root/parent/target directory handles, native file IDs
и конечные пути. Синтетический тест пустой папки обнаружил, что READ_ATTRIBUTES
не запрещает rename; заменено на GENERIC_READ без share-delete и проверено
фактическим Directory.Move refusal. Замена folder с теми же JSON bytes также
отвергается по directory identity. Root/target pins удерживаются до завершения
операции; source/checkpoint protections не ослаблены.

OwnedSaveTargetStore проверяет exact 21-field schema, original raw target SHA,
native binding/source/ownership dependencies, фиксированные working/checkpoint/
result paths. Handle действует только в одном подключении и до minimum dependency
expiry; повторная проверка срок не продлевает. Foreign/expired/revoked/claimed/
fenced/late-existing-target cases не публикуют IPC запрос.

Эти классы сами не вызывают COM/Exec/Save/OPEN и не регистрируют новые tools.
Сохранность target lease не означает edit authorization: следующий executor
обязан повторить guard в retained COM context, создать durable intent/fence,
вызвать один SaveAs и оставить результат unverified до отдельного verifier.

177 .NET tests и 349 native synthetic checks прошли. Actual Framework target
receipt с native directory/file metadata проверяется независимым Python oracle
и SDK checker. Oracle исправлен после найденного mismatch: bool/number JSON
types должны различаться, несмотря на Python True == 1. Четыре regression tests
oracle добавлены. 86 actual artifact cases теперь дают 0 mismatches.
Полный final regression пройден: four independent STDIO clients, четыре
type-sensitive Python oracle tests и Inspector CLI также passed;
evidence/K6-save-as/verified-targets/sdk-verification.json. Clean checkout/CI
этого нового блока: clean checkout 2e93bf0 полностью passed,
work/K6-target-clean/evidence/K6-target-clean. Оба Windows baseline jobs
успешны, SDK jobs PR #7 ещё выполняются. PR #7 stacked на К5 PR #6.

## Следующий блок

Подключить passive prepare facade с exact experimental flag/profile/schema;
затем native one-use SaveAs executor и independent verifier, savedResult leases,
byte-stable separately named reopen alias/guard/verification/new binding.
Не ослаблять общий source-copy FileShare.Read для занятого saved result — его
clone требует отдельного qualified contract. Clean checkout/Windows CI и live
сценарий К6 обязательны; К6 не завершён. Перед live читать всё заново: K5 own copy
b3d... существует, но PID/window/handles/unsaved state не считаются актуальными.
