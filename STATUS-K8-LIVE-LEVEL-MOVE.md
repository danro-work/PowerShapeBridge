# К8 — реальный перенос тела на слой подтверждён

15 октября2026. В собственной тестовой модели
Working_7b0765448a714a97a8f78edf46754834 тело1 (SOLID40/nativeID5)
один раз перенесено с General0 на существующий видимый Surfaces1.
Native operation3b1a8dc3bd55434d938672fe51bc5c61, Booleantrue, commandCalls1.
Bridge0.34.8, frozenSDK0.40.0; userapproval и оба backup сохранены.

## Проверенный результат

- Отдельный native verifier: owned_level_move_verified_observed;
  journal succeeded_verified,6events, exactly1started/1returned, nativeBoolean1.
- Ещё одно независимое read-only подключение повторно прочитало модель,
  свойства и мировые габариты без выделения; bounds[-10,10,-10,10,0,20]
  совпали с before. Геометрические observations и тип/id/name сохранены.
- Выделение очищено согласно API, context[1,0]→[0,0]; worldworkplane0 сохранён.
  Surfaces видим, остальные имена/активность1000slots/filters прежние;
  USED изменился только на source/target.
- Семь окон,11protectedhashes и savedworkingSHAc9060ce... сохранены.
  Собственная mutationfence снята только verifier. Claim и журнал остаются.
- Сохранение, повторное открытие, обратный перенос и screenshot не выполнялись.

Authoritative independent audit:
`evidence/K8-live-level-move/independent-audit.json` — passed.
Fresh native read: `evidence/K8-live-level-move/independent-native-result.json`.
Original operator record: `evidence/K8-live-level-move/transfer-20261015d/result.json`.

## Учтённые ошибки сценария и время ожидания

Попыткиa/b/c не подавали mutation: public/raw projection mismatch, CFBreserved
byte lock иaggregateSDK40s соответственно. Originalc позднее подтверждён как
prepared-only, без onceclaim. Никакой неизвестный setter не повторялся.
Дляd idleconfigoperationTimeout80→110 иSDK40→120, безперезапуска. Backupsettings
work/K8-level-time-budget-before.json, auditwork/K8-level-time-budget-change.json.
Nativeподготовка ипроверки занимают десятки секунд; responsebudgetэтоучитывает.

Сценарийd завершился exit1 только в конечном operatorassert: ошибочно ожидал
неизменное количество выделенных объектов. К этому моменту execute и отдельный
verifier уже succeeded. Ошибка сохранена в originalreport, corrected conclusion
получен новым независимым чтением/chain/hash audit, без повторного CAD вызова.
Futuredriverassert исправлен на заявленный context[0,0]; driverd не перезапускался.

## Границы и следующий шаг

Подтверждён только один SOLID в одной модели и перевод на существующий видимый
слой с сохранением наблюдаемых свойств/габаритов. Это не полная топологическая
эквивалентность, не saved/reopen квалификация и не закрытие общейК8–К11.
Разрешение на один transfer использовано; новые мутационные сценарии согласуются
отдельно. После реального результата целевые проверки завершились успешно:
42 executor/verifier cases и1928 hash checks;
5 STDIO/native scenarios,48 requests/341 hash checks,
14 configuration/30 admission cases. Evidence:
`evidence/K8-level-after-live-execution/native-audit.json` и
`evidence/K8-level-after-live-wire/wire-audit.json`.
Historical verification после
reconnect следующий малый read/metadata block; broad CI выполняется после блока.
