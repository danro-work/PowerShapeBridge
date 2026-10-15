# К8 — executor и независимая проверка переноса между слоями

Source SDK0.39.9/nativePython0.34.6. Установленный Bridge0.33.5 не изменялся.
`OwnedLevelMoveExecution.Execute` вызывает один SetItemsLevel в удерживаемом
guard контексте подготовленного плана; `Verify` использует только IReadContext.
В ComContext добавлен typed native setter, но production queue/SDK admission
ещё отсутствует, реальный вызов SetItemsLevel не выполнялся.

Guard сохраняет executor lock, proof leases, ownership/checkpoint и working
byte pin; план перепроверяется до единственного callback. Исполнитель
получает composite заново, сверяет bounds/context/windows/saved bytes,
пишет frozen execution plan, once claim, mutation fence и append-only journal.
Успех принимает только native Boolean true; int1/null/false не трактуются как
успех. Самостоятельно не сохраняет, не включает слой и не возвращает выделение.

`LevelMovePostconditions` принимает только изменение levelNumber у точного
SOLID, предусмотренное снятие выделения и occupied levels/USED в двух
затронутых slots. Остальные объекты, geometry observations, direct bounds,
workplane, units, filters/names/active flags/windows/saved files проверяются.
Свойства и bounds не являются полной топологической эквивалентностью.

Verifier требует post-command inspection/listing/level receipts, авторитетный
command_returned в journal, точные plan/once claim/fence proofs и два свежих
пассивных чтения. Только после проверки дописывает succeeded_verified и снимает
собственную fence. Не вызывает setter. При частичной metadata cleanup возможна
повторная проверка без команды; отсутствующий/испорченный once claim запрещает
снятие fence. Чужая/неразрешённая export fence также запрещает завершение.

Ранний stage `work/K8-level-executor-first` прошёл33cases. Второй запуск
выявил неверное ожидание fixture: отказ до создания claim не потребляет intent,
и новый request может законно его выполнить. Проверка исправлена: повтор того
же отклонённого request не исполняется; после созданного claim новый request
с тем же intent также не исполняется. Product once guard не ослаблялся.
Stage `work/K8-level-executor-third` прошёл40cases и independent1834hashchecks.
Новая итоговая матрица42cases добавляет missing/tampered claim до завершения.
Общая regression session4764 terminal exit0/PASS:
678.NET без пропусков,531native,175actual compatibility без расхождений,
все прежние operation/wire gates,40level-plan cases/1177audit hashes,
42executor/verifier cases/1928audit hashes и Inspector. Итоговый marker:
`evidence/K8-level-executor-sdk-final/sdk-verification.json`.
Сверены326source hashes перед публикацией. CAD connection/deployment=false.

`nativeProviderQualified=false`. Это внутренние source components, не
общедоступная квалифицированная MCP операция. Остались SDK wiring, live own-copy
qualification, saved result/reopen, другие операции К8 и полная К9–К11.
