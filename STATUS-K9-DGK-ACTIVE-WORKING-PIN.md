# К9 — подготовка DGK при открытом working writer

## Итог исправленной полной проверки

Session56326 завершилась exit0, итоговый sdk-verification.passed=true:
652.NET/0skips,531native,175compatibility/0mismatches,actual scoped STDIO DGK
6cases/219hashes/30requests,Inspector иsource hashes. Native targets44cases/
503hashes/9 byte pin cases; native-to-SDK23receipts/0mismatches. Evidence:
`evidence/K9-dgk-pin-sdk-corrected/sdk-verification.json`. Исходный failed72663
не переопределён passed. Нет CAD/runtime/merge/release.

14 октября 2026. Source native/Python0.34.3-dev, SDK0.39.6-dev; installed0.33.5
не менялась. CAD/панель не обновлялись; ни одного реального экспорта/изменения.

## Причина и изменение

OwnedCopies уже допускает FileShare.ReadWrite для mutable working copy, но
OwnedDgkTargets добавлял FileShare.Read pin. При открытом writer PowerShape
такой pin невозможен; ошибка подтверждена прежней attach-only диагностикой.
SDK receipt store также открывал working bytes с FileShare.Read.

Добавлен OwnedWorkingBytePin: read handle с FileShare.ReadWrite без Delete,
shared LockFileEx range и fail-immediately. Другие читатели/существующий writer
остаются открыты, обычная запись данных блокируется. Identity/length/SHA
rechecks сохраняются в подготовке, TargetLease и существующих executor guards.
SDK читает active working bytes с Share.ReadWrite; checkpoint остаётся
ReadOnly/Share.Read. Source/checkpoint/receipts/protected paths не ослаблены.

Граница: EOF changes и memory-mapped writes обходят range lock. Это проверено,
а не предположено: обе мутации собственных synthetic files отклоняют native
TargetLease.Recheck. Полного/непрерывного исключения всех записей working файла
не заявляем. [Microsoft LockFileEx](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-lockfileex)
документирует shared reads/write denial и mapped-view limitation.

## Проверки

- Exact DGK target runner:44 cases,503 independent hashes,9 byte-pin cases.
  held writer допускает preparation/lease; overwrite/append/rename refused;
  EOF/mapped changes detected/rejected; exclusive lock refused; disposal releases.
- SDK store27 tests passed; active writer не блокирует Issue/Validation, но
  изменённые bytes отзывают validation. SDK только читает, не выдаёт authority.
- Executor41 cases/1006 hashes и independent verifier28/987 passed без CAD.
- Full SDK/native pipeline запущен в новых directories:
  `evidence/K9-dgk-pin-sdk-final`, `work/K9-dgk-pin-sdk-final`; session72663
  подтверждена live. Await terminal/SDK wire/Inspector/source hashes; не rerun.
  Последний вывод:652.NET/0skips и531 native baseline passed; не terminal.

Все тестовые данные принадлежат fixture; сознательные EOF/mapped edits и restore
затрагивали только synthetic files. На active working copy PowerShape этот
новый lock ещё не применялся. Live preparation/export требует своего принятого
сценария после qualification provider/settings/frame/selection.

## Сохранённые неудачные попытки

- `work/K9-dgk-working-byte-pin`: неверное предположение о блокировании truncate
  опровергнуто (synthetic length99), результаты/файлы не удалены.
- `work/K9-dgk-working-byte-pin-corrected`: File.ReadAllBytes в fixture setup
  конфликтовал с writer; исправлен только reader на Share.ReadWrite.
- `work/K9-dgk-working-byte-pin-final`: successful prior44/503; последний
  runner `evidence/K9-dgk-working-byte-pin-audited` дополнительно проверяет
  `workingChangeRejected` и9 low-level cases в независимом audit.
- `work/ProbeSharedWorkingLock.cs`: исходный probe использовал buffered
  WriteByte; его truncate отказ был следствием pending buffered write. Этот
  результат не принят как доказательство EOF exclusion.

FullК8–К11 незавершён: public level SDK/native read route, general organization,
production DGK provider/SDK execute+verify/owned import roundtrip, five-model
qualification и release/user acceptance остаются обязательными.

## Full pipeline: исправление compatibility gate

Session72663 завершилась exit1 после успешных652.NET/531native/175compat и DGK
wire6/219hashes/30requests. Native target matrix expanded44 cases, а helper
DgkTargetCheck всё ещё ожидал20 prepared receipts и6 positive controls.
Checker обновлён до23 уникальных prepared cases/9 controls; отдельная проверка
тех же native artifacts —23cases/0mismatches. Product код не менялся для этой
коррекции. `failure.json` сохранён, passed для исходного запуска не выставлен.
