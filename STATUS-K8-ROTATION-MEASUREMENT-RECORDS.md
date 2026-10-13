# К8 — неизменяемые измерения в журнале поворота

Внутренний план исполнения поворота теперь использует schema2. До первой команды
для каждого объекта создаётся отдельная запись
rotationmeasurements/<execution>/<index>.before.json. План закрепляет её SHA.
После успешного возврата команды и проверки контекста создаётся after.json.
Итоговый журнал связывает оба SHA с записями started/returned/observed.
Прежний schema1 без измерений не считается новым контрактом.

Запись schema1 содержит execution ID, index, phase, recordedUtc и происхождение
данных: copy/owner/working SHA, binding/inspection IDs и SHA, исходные observation/
window digests и expiry. recordedUtc — время записи файла, а не одновременного
измерения всех точек. Данные содержат native key, world/document_raw, descriptor
SHA, уникальные anchors, позиции, направления, invariants и paired bounds.
Проверяются точные поля, типы, конечность чисел и размеры массивов.

Перед командой запись before перечитывается по закреплённому SHA и сравнивается
со свежими измерениями. Затем ещё раз проверяются контекст и expiry: длительное
чтение не разрешает выполнить команду на устаревшем документе. Proof streams
удерживаются до завершения. CreateNew запрещает перезапись; частичные локальные
артефакты обозначаются localArtifactsMayExist.

## Проверки

- 24 сценария пакетного исполнителя и 577 хешей прошли независимый аудит:
  evidence/K8-rotation-receipts/execution-faults,
  stage work/K8-rotation-receipts-faults, session55131 завершён успешно.
  Испорченные before samples отклонены до команды. Изменившиеся при повторном
  чтении samples дают unknown с нулём вызовов и сохранёнными claim/fence.
  Испорченные after samples дают unknown после одного вызова.
- 16 проверок неизменяемых записей и 256 хешей прошли аудит:
  evidence/K8-rotation-receipts/records-qualified,
  stage work/K8-rotation-records-audited, session9043 завершён успешно.
  Проверены schema/phase/index/provenance/SHA/vector/type/extra fields,
  foreign execution, rewrite и held tamper. Существующие артефакты сохранены.
- 21 тест математической проверки .NET прошёл без пропусков:
  evidence/K8-rotation-receipts/kernel-regression, session26301 завершён успешно.

CLI без собственного маркера отказывается работать и не создаёт fixtures.
Новый runner включён в verify-sdk.ps1; пакетный auditor проверяет связи измерений.
Предыдущий PR31/0d11c5e108c044188305f94e07a900c1bd094c34 прошёл четыре CI:
38472860435/38472958341 baseline, 38472860388/38472958423 SDK.
Его native regression прошла 531 проверку. Полный CI нового исходного кода впереди.

## Ограничения

Fake context возвращает углы своего искусственного bbox как witnesses только
для проверки записи и связей. Эти точки не выдаются за геометрию реального тела;
синтетический эффект не является физическим поворотом. Даже noop success остаётся
executed_unverified, рабочая копия — заблокированной.

Независимый native verifier, снятие fence и production COM/queue/MCP executor ещё
не добавлены. Провайдер тела и живые форматы измерений не квалифицированы.
Нужны свежие независимые измерения после команды, проверенная native convention
и восстановление частичного пакета без повторов. Запись сама по себе не разрешает
исполнение и не подтверждает результат.

К7 ждёт разрешения пользователя. Установленный runtime, READY-пакеты и CAD не
изменялись. Полные К8 и К9–К11 не завершены. Следом — независимый verifier по
закреплённым before records, провайдер тела, transport/faults и живой сценарий.
