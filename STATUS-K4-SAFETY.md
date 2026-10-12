# К4 — правила подготовки, выполняется

12 октября 2026. К4 ещё не завершён; SDK public catalog остаётся 0.33.0-dev
с шестью read-only tools. Native runtime/worker/CAD не изменялись.

Готов первый блок:

- PythonJson.Indented сохраняет legacy indent=2, порядок полей, Unicode и
  числовую форму из parsed JSON; raw receipt SHA не строится пересериализацией.
- ArtifactProof проверяет token/size/path и SHA оригинальных bytes, включая
  BOM/CRLF/whitespace. Freeze создаёт файл через CreateNew + Flush(true),
  повторное создание отвергается. Local prepared artifacts — отдельная схема;
  native C# digest не заменяется Python encoding.
- CandidateStore хранит observed set leases, отзывает их при observed switch
  и expiration; выдача set handle не исполняет CAD.
- WorkingCopyStore переносит saved-source argument/path policy, copy ownership
  leases и verified-open lease seam для следующей стадии. TTL не продлевается,
  чужое подключение не принимает handle. Никакой copy/open facade пока нет.

Проверки: 109 .NET tests passed, 0 failed, 0 skipped;
`evidence/K4-safety/K4-leases.trx`. Из них 98 — база К3, два artifact proof
tests и девять copy/set/source-policy cases. Предыдущий artifact-only TRX
сохранён рядом. Проверки выполнялись в обычном пользовательском Windows-контексте
с synthetic файлам; нет CAD-команд, GUI, deployment или runtime обновления.

До закрытия К4 нужно перенести read_set/source_observations/local plan
и prepared journal; binding/owned-operation stores с dependency recheck,
минимумом expiration и exact schema; SDK prepared facades и mode refusals;
независимый Python/native byte/schema parity, tamper/concurrency/fault checks,
clean checkout и Windows CI. Execute/verify/open endpoints относятся к К5.
