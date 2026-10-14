# К8 — independent export verification

Добавлен внутренний verifier с отдельным read-only интерфейсом. Полная цепочка
receipt/plan/target/history/claim/fence/journal/steps/summary связывается с новым
inspection/listing и retained current context. Output повторно разбирается
DmtMeshReader с identity/SHA/count/unit checks. Diagnostic unqualified; никаких
повторов, CAD изменений, fence release или succeeded_verified journal events.

PASS:21 native cases/734 independently audited hashes,
work/K8-export-verifier-qualified и evidence/K8-export-verification/qualified.
CLI marker refusal проходит до создания стенда. Historical target verification
lease не допускает Recheck для исполнения; fresh reads имеют отдельный TTL.

Регрессии изменённого target lease:30 target cases/478 hashes и26 export executor
cases/595 hashes прошли в отдельных fresh stages. Framework x64 сборка проходит.
Новый runner включён в verify-sdk для следующего доступного CI.
Источники и original artifacts retained; все tests используют собственные fake
контексты и непрозрачные saved файлы, не активную модель пользователя.

Installed runtime/готовые пакеты К7/CAD не менялись. Новый verifier и предыдущий
export executor готовятся к публикации отдельным PR. Ранее GitHub request был
остановлен approval-review usage limit; subsequent разрешённые действия и PR37
подтвердили, что доступ снова работает. Ограничение не обходилось; old blocked
state не является актуальным. Source/evidence сохранены локально.

Контракт:OWNED-GEOMETRY-EXPORT-VERIFICATION.md. Следующая работа:native provider,
mesh/transform correctness, safe own-fence resolution, SDK/transport/faults и
полный оставшийся К8–К11. Живой К7 scenario пока ожидает ответа пользователя.
