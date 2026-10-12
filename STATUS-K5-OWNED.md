# К5 — исполнение и независимая проверка, выполняется

12 октября 2026. SDK public catalog пока остаётся 0.34.0-dev/general 16,
read-only 6; native runtime 0.31.0 не менялся. К5 не завершён, live switch
не выполнялся, никаких новых CAD-команд/GUI не было.

Перенесён OwnedExecutionProof: open parameters потребляют document choice
после проверки candidate/copy leases; expiry — минимум dependent choices.
Verification parameters выбирают fresh source receipts и one-use choice.
Raw open/translation plans проверяются по original byte SHA и точным
переданным зависимостям. ReturnCode=1 (int, не bool), copyFenced=true и
recorded executed_unverified не становятся succeeded_verified автоматически.
Отдельный verification result требует succeeded_verified, noCommandReplay,
fenceReleased и отсутствие geometry/context/save mutation.

Эти функции не содержат COM, исполнения/повтора команды, fence removal или
MCP attributes. Их регистрация/исполнение остаётся следующим блоком.
Проверки: 146 .NET tests passed, 0 failed, 0 skipped;
evidence/K5-owned/K5-proof.trx. Пять новых tests проверяют one-use open choice
и отказ bool/non-success/unfenced/already-verified execution receipts.

До закрытия К5 нужны five context/open/execute/verify facades, consume/revoke
и unknown/no-replay/fence parity через SDK/fake-panel; независимые Python/native
proof checks; свежие process/session/queue/model данные и новый owned scenario
без повторения P2.29 OPEN/старых intents. Live adapter switch принимается
пользователем по готовому review package и rollback, как требует принятый план.
К6–К11 остаются полным последующим объёмом общего MCP v1.
