# К4 — правила подготовки, выполняется

12 октября 2026. К4 ещё не завершён; SDK public catalog остаётся 0.33.0-dev
с шестью read-only tools. Native runtime/worker/CAD не изменялись.

Готовы исходные хранилища для подключения к SDK:

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

OperationPlanStore перенесён: strict arguments/finite vectors/units/frame,
чтение exact source/set hashes, полный source projection, explicit intersection
roles/disjointness, disabled local plan и только prepared journal. Cache TTL
после подготовки ограничен оставшимися UTC/set leases, без продления на время IO.
PreparedJournal создаёт только первое событие. Interrupted writer.lock сохраняется;
native executor/verifier остаются владельцами последующих command transitions.

DocumentBindingStore и OwnedOperationStore перепроверяют зависимости после
native результата, exact schemas/false execution flags, raw hashes и prepared
journal. Expiration — минимум всех связанных leases; изменение/исчезновение
proof или зависимостей отвергается до дальнейшей работы. Verification читает
готовые bytes и не дописывает событие. Native и Python digest encoding разделены.

Проверки: 134 .NET tests passed, 0 failed, 0 skipped;
`evidence/K4-safety/K4-prepared.trx`. Из них 98 — база К3, два artifact proof
tests, девять copy/set/source-policy и 25 prepared-plan/binding/packet/fault cases.
Предыдущие artifact/lease TRX
сохранён рядом. Проверки выполнялись в обычном пользовательском Windows-контексте
с synthetic файлам; нет CAD-команд, GUI, deployment или runtime обновления.

До закрытия К4 нужно подключить перенесённые stores к BridgeSession/ReferenceGate,
добавить SDK prepared facades и mode refusals;
независимый Python/native byte/schema parity, tamper/concurrency/fault checks,
clean checkout и Windows CI. Execute/verify/open endpoints относятся к К5.
