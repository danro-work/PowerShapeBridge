# К8 — проверка переноса после переподключения

Source SDK0.40.1/native0.34.7, ветка codex/level-move-reconnect-verification.
Изменения пока не опубликованы. Frozen package PR53/b94722f (SDK0.40.0/native34.7)
прошёл все4GitHubCI; отдельное разрешение на backup/update и один General->Surfaces
перенос запрошено, ответ пока не получен. Новый source не входит в этот пакет.

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
