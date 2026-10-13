# К7 — transport timeout, потерянный и поздний ответ

Добавлены12 SDK client/server rows: translation и ordinary owned OPEN ×
control/claimed timeout/native returned but reply lost/late original receipt;
SaveAs × три fault cases; saved-result OPEN × late original receipt. Они дополняют
предыдущие14 cancellation rows и две saved-result timeout/lost-reply rows.

28 строк проверяются в единой machine-readable matrix:

| MCP route | Control | Timeout | Lost returned reply | Late reply | Local/explicit cancellation |
|---|---|---|---|---|---|
| execute_owned_translation | Да | Да | Да | Да | Pending request |
| open_working_copy | Да | Да | Да | Да | Pending request |
| execute_owned_save_as | Прежний route test | Да | Да | Да | Pending и synthetic returned |
| execute_owned_saved_reopen | Прежний route test | Да | Да | Да | Pending и synthetic returned |
| activate_document | Прежний route test | Отдельно впереди | Отдельно впереди | Отдельно впереди | Pending request |

Для каждого timeout оригинальный `.processing` сохраняется, исходный intent
потреблён, CAD leases отозваны и replay отклоняется до второго request. Returned
fixtures удерживают plan, hash-chain journal, claim/fence и оригинальный raw reply.
Late receipt публикуется только один раз перемещением исходного файла, его SHA
не меняется. Истёкший client result остаётся unknown; новая binding или saved
result authority не появляется автоматически.

LostReplyFixture даёт synthetic durable proofs, не native command implementation
или CAD postconditions. Translation/ordinary OPEN controls отдельно проверяют,
что SDK принимает полную схему receipt; native guard/verify coverage остаётся
отдельной25-cell suite. Ни один новый тест не подключается к PowerShape.

При PSBRIDGE_TEST_EVIDENCE тесты сохраняют row JSON и полную копию artifacts
за пределами исходного fixture. SHA каждого archive file сверяется с оригиналом.
Архивы входят в CI artifacts и позволяют повторить byte/journal audit после
удаления ephemeral test roots. Auditor читает весь перечень файлов, один claimed
request, recipe/ID, immutable plan, авторитетный return ID/code, journal chain,
claim и matching fence. Это filesystem audit; consumption/replay rejection
дополнительно проверяется живыми SDK тестами в процессе.

verify-sdk.ps1 выделяет fresh transport evidence, восстанавливает прежнюю env
настройку и запускает independent Python auditor до остальных native/STDIO
checks. Четыре negative audit cases требуют отказ при missing row, changed hash,
extra artifact и forged native return ID даже с обновлённым file hash manifest.

Проверено:12 focused passed; matrix28/28 passed +independent audit599 file hashes;
первый полный .NET326/326 passed. Full runner с archived rows в
evidence/K7-sdk-lost-reply/sdk-full завершился:326 .NET/531 native/175 artifacts
(137 accepted,0 mismatch)/28 SDK rows и599 archived artifact hashes/25 native
fault cells с independent audit/внешние STDIO/Inspector passed. Четыре negative
auditor checks passed отдельной командой, transport-fault-audit-with-rejections.json;
CLI --exercise-rejections добавлен в runner для следующих полных запусков.
Все138 recorded source/binary hashes сверены после завершения, hash-audit.json.
Исходные failed diagnostics (xUnit analyzer rule и неверное ожидание внешнего
SaveAs state вместо wrapped owned_save_as_outcome_unknown/nativeState timeout)
сохранены; production guards не менялись.

Предыдущий block8b38435/PR16: все четыре exact-head Windows CI passed:
38385919412/38385910802 baseline и38385919132/38385910778 SDK. Actual SDK log
проверен:314 .NET/531 native/175 artifacts0 mismatch/25 native fault rows и
independent audit/STDIO/Inspector. Source13f1c54 CI тоже passed отдельно.

К7 ещё не закрыт: нужны activation-context faults, дополнительные partial
publication points и отдельно принятый checkpoint recovery scenario. Для
translation/ordinary OPEN cancellation после synthetic native return также
расширить matrix; текущие cancellation rows не выдаются за такое покрытие.
Installed native0.33.3, source SDK0.37.4 и default MCP config не менялись.
