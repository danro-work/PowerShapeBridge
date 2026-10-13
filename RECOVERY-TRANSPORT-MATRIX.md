# К7 — SDK transport fault matrix, выполняется

АКТУАЛЬНО,13 октября: К6 квалифицирован, STATUS-K6-QUALIFIED.md. К7 дополнен
14 SDK cancellation rows для четырёх owned executors и activation +2 plain SDK
controls;16 focused passed, STATUS-K7-SDK-CANCELLATION.md. Local-only cancellation
и explicit protocol notification проверяются отдельно, retained artifacts и
replay rejection обязательны. Полная К7 ещё не завершена. Ниже история первого
transport блока; pending installation0.33.2 и CAD state в конце устарели.

Ветка feat/recovery-transport-matrix основана на квалифицированном directory fix
PR11. Производственный код, versions, installed runtime и packages не меняются.
SavedReopenFixture.Execution получает opt-in publish=false: original raw proof
остаётся immutable .unpublished; polling .json не создаётся и не восстанавливается.
Default path прежних тестов остаётся тем же.

Две новые строки actual SDK in-memory client/server route:

| Executor | Fault | Доказательства |
|---|---|---|
| execute_owned_saved_reopen | Claimed request без ответа, timeout | Error unknown/context may have changed, handle consumed, .processing retained, replay rejected, один request |
| execute_owned_saved_reopen | Native fake returned/journal/claim, original reply unpublished | То же; immutable raw SHA/open-once/journal/protected bytes unchanged; новый binding не выдан |

Это synthetic native-proof fixture, а не живой OPEN. Полный перечень файлов
и SHA фиксируется до timeout и сравнивается после него и попытки old-handle
replay; replay отклоняется до queue publication. Инвентаризация доказывает
отсутствие дополнительных artifacts этого сценария, не полную CAD topology.

Focused SDK routes4/4 passed. Все14 пользователей SavedReopenFixture passed:
evidence/K7-transport-matrix/saved-reopen-qualified и fixture-regression.
Первый новый тест упал из-за неверного test-only lookup copyId в native parameters;
исправлен lookup из immutable prepared plan, failed diagnostic сохранён.

К7 ещё НЕ завершён. Дальше actual SDK cancellation propagation, такие же
transport rows для translation/SaveAs/ordinary OPEN, activation-context matrix,
дополнительные interrupted publication points и separately accepted checkpoint
recovery. Native core25 раньше qualified отдельно; ни одна часть не подменяет
полную матрицу и будущую К10. К6 waiting new installation approval0.33.2-dev;
own Working_b3d... остаётся active, SaveAs ещё не выполнялся.
