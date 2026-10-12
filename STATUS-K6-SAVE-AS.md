# К6 — собственный SaveAs и reopen, выполняется

Блок saved-byte preparation: SDK0.36.2-dev, native source0.33.0-dev.
`prepare_owned_saved_reopen(savedResultHandle)` создаёт новые собственные файлы
через тот же IPC и filesystem-only native recipe; не открывает/сохраняет CAD.
OwnedSavedSource закрепляет исходные raw receipts, target/ownership/checkpoint,
authoritative terminal journal, saved native file identity и двойной SHA.
Result read допускает существующего писателя, запрещает удаление; изменение
данных вокруг копирования отклоняется. Это point-in-time byte stability, не
exclusive writer lock. Затем closed readonly DiskReopen seed передаётся в
неизменённый strict OwnedCopies.Create; Working и checkpoint имеют отдельные
native identities. Reservation/частичные файлы при отказе сохраняются, без
повтора/overwrite. SDK перепроверяет source chain, plan, copies, fixed paths,
readonly, ownership и minimum saved lease TTL; новый handle не является binding.
Флаг ownedSavedReopenExperimental зависит от ownedSaves/workingCopyOpen.
Новый полный opt-in профиль 25 tools; readonly остаётся 6.

227 .NET и 479 native synthetic checks passed, 142 actual Framework artifacts:
110 accepted, 0 Python/SDK mismatches. Старые пять external STDIO scenarios и
новый шестой проверены; шестой вызывает только filesystem-only prepare в
изолированном native fixture, read/verifier ответы синтетические. Actual source,
seed/working/checkpoint hashes совпадают, readonly сохранён, native journal
не меняется; никакого COM/live CAD. Inspector подтвердил новый 25-tool profile.
Evidence: evidence/K6-save-as/reopen-preparation-full. Первое параллельное
испытание имело один old Save route failure; diagnostic isolated pass и full
sequential regression 227/227. Test-only fake durable-reply budget 5s;
production timeouts не изменены. Clean/CI нового блока ещё ожидаются.
Guarded OPEN, separate fresh content/units verifier/new binding и live К6
ещё не выполнены. Установленные runtime/config не менялись.

## Предыдущий блок — SaveAs executor и saved-result leases

Текущий SDK-блок, 12 октября 2026: source SDK0.36.1-dev. Зарегистрированы
execute_owned_save_as и verify_owned_save_as по ownedSavesExperimental; full
save profile 24/read-only 6, прежний установленный профиль 21/6. Execute
consumes target и отзывает CAD leases до IPC; error/timeout/cancellation после
публикации не разрешает повторный SaveAs. Свежий verifier выдаёт savedResultHandle
через OwnedSavedResultStore только после original response/plan/claim/journal,
authoritative return, terminal evidence и saved-byte checks. Handle connection-
scoped, maximum/minimum dependency TTL до 300s, без продления проверкой; raw
verification receipt SHA закреплён. Native physical directory/file identity
повторно проверяется перед дальнейшим clone/open; это не разрешение изменения
модели и не доказательство топологии. Повторная свежая verification сохраняет
первый terminal journal/evidence и не добавляет Save или новый event.

Пройдены 212 .NET tests (24 новых store/official MCP pipe cases), 432 native
synthetic, 124 actual Framework artifact cases (98 accepted, 0 mismatches),
пять независимых SDK STDIO клиентов и Inspector. Новый external STDIO сценарий
использует actual Framework saved-result artifacts и fake verifier reply;
подтверждены 24/6 catalog, exact schemas, one-use document choice и неизменный
native journal. Evidence: evidence/K6-save-as/sdk-saved-results-full;
полный sdk-verification.json passed. Loaded-runner claimed timeout test отделён
от 200ms pre-publication fsync race; production timeout/IPC не изменён.
Native Save fixtures получили полный inventory для SDK read admission.

Clean checkout aeeeb43afdff91afbeacc8cb95f0fca6406af6db прошёл baseline и полный
SDK regression: work/K6-sdk-save-clean/evidence/K1-baseline и
work/K6-sdk-save-clean/evidence/K6-sdk-save-clean-short. Source/binary SHA
receipt совпадают. Первый длинный staging превысил Framework MAX_PATH;
частичный failed receipt сохранён, новый work/n1 прошёл все checks. CI на своих
коротких runner paths успешен: runs 38287279348/38287283849 (baseline push/PR)
и 38287279304/38287283845 (SDK push/PR). Все четыре CI завершены success.
Reopen/new
binding и live qualification К6 не выполнены. Native0.32/published K5 SDK0.35.1/
config.toml не менялись; default switch decision pending. К6–К11 остаются целью.

## Предыдущий блок — native SaveAs и SDK preparation

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
