# К6 — собственный SaveAs и reopen, выполняется

Текущий блок, 12 октября 2026: SDK 0.36.0-dev/native source 0.33.0-dev.
`prepare_owned_save_as` подключён к официальному SDK, connection binding/target
stores и единому ReferenceGate. Отдельный флаг ownedSaveTargetsExperimental;
полный новый профиль имеет 22 tools, read-only сохраняет 6. Старый установленный
профиль остаётся 21/6. После публикации cancellation означает неизвестный исход
локальной подготовки, без SaveAs/retry; потерянная binding не выдаёт новый handle.

Native OwnedSaveAs выполняет один FILE SAVEASPATH после двух retained-context
guards, pinned target/source/checkpoint proofs, fixed-path/no-collision checks,
create-once claim и durable journal. Return ещё executed_unverified/fenced.
OwnedSaveVerification отдельно проверяет authoritative return, новые inspection/
listing receipts, result SHA/file identity, наблюдаемые свойства/единицы,
неизменность прежних saved files и checkpoint; только matching fence снимается
после succeeded_verified. Повторная проверка не добавляет событие и не делает Save.
При unknown команда не повторяется; claim остаётся и после успешной проверки.
Native recipes gated флагом ownedSavesExperimental; config/general-owned-saves.json
не установлен. SDK execute/verify/saved-result leases пока не подключены.

Пройдены 188 .NET tests, 432 native synthetic checks, четыре независимых STDIO
клиента, oracle regression, Inspector и 116 actual Framework artifact cases
(97 accepted, 0 mismatches). Отдельный Python audit проверил actual native SaveAs
raw receipts, шесть journal events, один command-start/return, retained claim,
released matching fence и saved/protected SHA. Evidence:
Первоначальный прогон: evidence/K6-save-as/native-save-and-prepare. Profile-on native staging:
work/K6-save-routes-tests-1. Всё на opaque synthetic fixtures; live CAD К6
не выполнялся. Clean checkout 47a64e57deeb696094b336bf694af5786d2bff38 прошёл
baseline и полный SDK regression, включая integrated native save audit:
work/K6-save-clean/evidence/K1-baseline и evidence/K6-save-clean. В baseline
116 Python checks, MCP protocol/junction/deployment plan passed. Все четыре
Windows CI этого коммита прошли (push/PR baseline и SDK): runs 38282681431,
38282687324, 38282681422, 38282687389. Matching active/model/window identity
проверяется отдельно; неправильное имя/рассогласованный active listing дают unknown.

Ограничения: saved working file допускает внешнего писателя по прежнему ownership
контракту; изменение обнаруживается и оставляет unknown/fence. Pins не являются
атомарным CreateNew внутри PowerShape SaveAs: перед командой target повторно
проверяется в собственной уникальной папке. File Doctor/settings не меняются.
Наблюдаемые properties и SHA не доказывают полную топологию/загрузку результата.
Далее SDK execute/verify и savedResult lease, separately named byte-stable reopen,
new binding, clean/CI и own live qualification. К6 не завершён; К7–К11 обязательны.
Установленные native0.32/published K5 SDK0.35.1 и config.toml не менялись.

## Предыдущий блок — filesystem foundation

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
