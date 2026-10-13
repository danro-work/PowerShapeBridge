# К7 — проверка открытия новой копии из checkpoint

Source SDK0.38.1-dev/native0.33.5-dev. Установленная версия0.33.3-dev
не обновлялась. Эти испытания не подключаются к PowerShape и не квалифицируют
содержимое реальной CAD-модели.

Проверяется существующая цепочка CheckpointRecovery.Prepare/Create →
WorkingCopyOpen.Execute → OwnedOpenVerification.Verify → DocumentBindings.Prepare.
Файлы, планы, журналы и квитанции создаёт настоящий native код; CAD context
заменён испытательным объектом. Используются собственные непрозрачные файлы.

Шесть сценариев: успех, потерянный возврат после действия, отсутствие эффекта,
смена процесса, прерывание публикации журнала, неверный выбор модели для проверки.
Каждый допускает только один вызов OPEN. Повтор отклоняется с сохранением
всех артефактов. Исходный трёхсобытийный unknown journal и open-once claim
не изменяются. Новая рабочая копия и её checkpoint побайтово совпадают с исходным
checkpoint, имеют отдельные native file identities.

Только успешный сценарий получает independent verified OPEN и binding новой
модели500. Ошибочные сценарии не получают binding. Частичный writer.lock
сохраняется; автоматического ремонта, закрытия, Save, Undo или повтора нет.

Native CLI --checkpoint-recovered-open-test требует отдельный marker в staging.
verify-checkpoint-recovered-open.ps1 проверяет отказ без marker, шесть сценариев,
независимый Python аудит журналов/квитанций/файлов и SDK proof compatibility.
Runner включён в verify-sdk.ps1 и существующий Windows SDK CI.

Первичная проверка: evidence/K7-recovered-open/sdk-proof-qualified.json,
native-open-audit.json — шесть сценариев и253 artifact hashes passed.
Focused SDK regression:19/19 passed. SDK теперь требует новый copy ID,
отличный от origin/intent, и полный обязательный inventory исходных файлов.
Негативный тест пересчитывает SHA подменённой квитанции с пустым inventory:
SDK отклоняет её и не выдаёт рабочее разрешение.

Полный набор проверок завершён в evidence/K7-recovered-open/sdk-full,
staging work/K7-recovered-open-sdk-full:362 .NET,531 native,175 compatibility
cases137 accepted0 mismatch,32 SDK transport rows,25 native executor faults,
20 activation faults,40 legacy STDIO,16 checkpoint cases иactual SDK/native
disk creation,6 recovered OPEN cases253 hashes/SDK proof checker иInspector
passed. Все160 source/binary hashes сверены с файлами,0 mismatch.
Предыдущий checkpoint block source d5c32c26 прошёл четыре Windows CI:
38399467709/38399421276 baseline и38399467788/38399421157 SDK.

Граница: SDK checker вызывает настоящие stores на native proofs, а не весь
recovered OPEN wire workflow. Остаются квалификация маршрута через MCP,
дополнительные cancellation/reparse/partial-publication cases и отдельно
согласованный live checkpoint scenario. К7 целиком и К8–К11 не завершены.

Autodesk Product Help en_US/PWRSS2023 проверен13 октября:
[Opening a model](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-EC8A3F76-6087-499E-BD11-8022DDBA82FD)
описывает отдельное окно открытой модели и возможность нескольких окон модели.
Поэтому число окон само по себе не устанавливает принадлежность документа:
наша проверка связывает новую модель с точным working path и native context.
[Recovering a model](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-3EF761A5-A110-4F0A-8D5F-5AA91EB4141E)
описывает встроенное восстановление после crash, зависящее от сохранённого состояния.
Наш checkpoint является явно защищённым сохранённым файлом; документация не
подтверждает восстановление несохранённых изменений нашим механизмом или
разрешение unknown результата. Параметры autosave, recovery dialog и процессы
PowerShape в этом блоке не изменялись.
