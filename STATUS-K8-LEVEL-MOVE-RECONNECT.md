# К8 — проверка переноса после переподключения

Актуально: SDK0.40.1/native0.34.8, ветка codex/level-move-reconnect-cfb,
на базе исправления CFB в draft PR54/c194a20. Native0.34.8 уже установлен
с разрешения пользователя. Один General→Surfaces перенос выполнен;
его разрешение использовано, новый перенос не выполнялся.

Реальная проверка после нового STDIO подключения прошла на изолированном SDK:
adoption исходного execution3b1a8dc3bd55434d938672fe51bc5c61 не создала
native artifacts/queue request. Verification-only handle отклонён локально
при передаче в execute до очереди. Свежие candidate/listing/levels и native
verifier повторно подтвердили Surfaces, пустое выделение, семь окон и свойства.
Исходный журнал сохранил те же6events/один вызов,20контрольных hashes прежние.
Evidence: `evidence/K8-live-level-reconnect/result.json` и отдельный file-only
`evidence/K8-live-level-reconnect/independent-audit.json` — passed.

После реального результата:44focused tests/0skips passed. Пять обычных и пять
reconnect STDIO/native scenarios passed, каждый комплект48requests/341hashes.
Evidence: `evidence/K8-reconnect-cfb-after-live-focused/level-reconnect-cfb.trx`;
`evidence/K8-reconnect-cfb-after-live-wire/wire-audit.json` и `reconnect/wire-audit.json`.
Первый packet_tamper fixture остановился по8s timeout ещё при synthetic prepare;
failure сохранён в first-attempt-timeout.txt. Четыре завершённых случая не
перезапускались; недостающий случай прошёл на новой synthetic fixture identity,
после чего выполнен отдельный reconnect комплект. Реальный CAD не затрагивался.

Сохранение/переоткрытие результата не выполнено. Следующий конкретный сценарий
в K8-LIVE-LEVEL-SAVED-RESULT-SCENARIO.md; passivepreflight passed, без SaveAs/OPEN.
Ниже описание реализации и историческая квалификация исходной ветки f6ac392.

Добавлен full-only `adopt_owned_level_move_execution` в OwnedLevelMoveTools.Create,
уже зарегистрированный через ToolCatalog. Он выдаёт opaque handle только для
verify после проверки original response SHA, frozen execution/prepared plans,
owned paths/checkpoint/working hashes, native Boolean return и journal/once.
Prepared intent не возобновляется; ни native queue, ни COM не вызываются.
Exact once claim дополнительно требует original ownedLevelMoveId и5fields.

15new unit cases прошли вместе с29existing:44passed/0skips. Проверено отдельное
соединение, истёкшие исходные leases, no execution authority, no artifact writes;
wrong/missing claim, native false/int, contradictory journal, wrong identity,
changed working bytes, writable checkpoint и unfinished writer отказывают.
Evidence: evidence/K8-level-reconnect-focused-final/level-reconnect-tests.trx.

Первый actual STDIO/native run33951 terminalPASS:5normal+5reconnect scenarios,
по48native requests/341independent hashes, false-return/adopt refusal,
lost-reply original verification без replay; no CAD. Evidence:
evidence/K8-level-reconnect-wire/reconnect/wire-audit.json.
После этого усилен exact once claim и добавлены protocol argument assertions.
Finalwire80210 terminalPASS5normal+5reconnect, по48requests/341hashchecks:
evidence/K8-level-reconnect-wire-final/reconnect/wire-audit.json. SDK86887
terminalFAIL:721passed/1failed/722total/0skips с ограниченным Windows token.
Единственный failure — старый atomic File.Replace/AccessDenied; exact failed
test independentlyPASS с обычными Windows user rights:
evidence/K8-level-reconnect-user-file-test/atomic-replace-user.trx.
Unified ordinary-user full suite ещё не запускалась; не объявлять fullPASS.
Был overlap build/client DLL: сборка дождалась release и завершилась; больше
не запускать build одновременно с клиентом из того же bin.

Живой CAD setter, save/reopen, полный scopeК8–К11 и general recovery coverage
остаются отдельными требованиями. Никакая metadata adoption не разрешает edit.
