# К8 — независимый DMT-reader

Добавлен DmtMeshReader для ограниченного version1000-профиля и отдельная CLI
ReadDmtMesh. Reader связан с .NET-host как pure library; MCP-каталог не расширен.
PowerShape, очередь, installed runtime и готовые К7-пакеты не менялись.

Проверки:

- Framework:10 independent decode controls/5 malformed refusals, собственный
  staging work/K8-dmt-reader-qualified. Сверены все координаты, нормали, индексы,
  source SHA, bounds и ordered decode SHA; включён порог индексов 65535/65536.
- .NET:27 случаев прошли без skip; SDK-host/tests Release собираются.
- Official NormalDmt,TwoBlocks,smallModel fetched LFS bytes совпали с pinned SHA/size;
  файлы и MIT license сохранены для воспроизводимого CI.
- verify-dmt-reader включён в verify-sdk; независимый auditor работает без CAD.

Доказательства:evidence/K8-dmt-reader/framework/framework-audit.json,
evidence/K8-dmt-reader/dmt-reader-tests.trx. Контракт:DMT-READER-CONTRACT.md.
У всех трёх исторических DMT fileFlags0. Надпись header 'in MM' не превращает
unknown units в подтверждённые mm. Ни world frame, ни стабильные mesh indices,
ни CAD topology/convention из чтения этих файлов не следуют.

PR35/008423c подтвердил 30 target cases/478 audited hashes и отдельную native
регрессию531. Все четыре CI completed/success:38481046321,38481046247,
38480997217,38480997210.

Следующий блок:guarded export executor с pinned settings, независимый результат
и mesh-based transform verification, отдельно native convention/units qualification.
Живые операции только после согласованного конкретного сценария. К7 scenario
пока ожидает ответа; весь объём К8–К11 остаётся незавершённым.
