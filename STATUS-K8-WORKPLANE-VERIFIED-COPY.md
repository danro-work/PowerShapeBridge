# К8: поздняя проверка workplane и свежая ссылка на изменённую own-copy

Актуальное дополнение:35.1 установлен, реальная цепочка verification → lease → binding →
SaveAs → OPEN завершена и независимо проверена. См. STATUS-K8-WORKPLANE-SAVED-RESULT.md.
Ниже сохранён отчёт подготовки, до нового разрешения и установки.

15 октября 2026, состояние при подготовке. Установлен Native0.35.0-dev; новая workplane остаётся в памяти
Working_cde95c3e54a64c2f9264443cacb8837f. Нового сохранения, OPEN, создания,
активации или обновления панели в этом блоке не было.
Подготовлены Native0.35.1-dev и SDK0.41.1-dev; НЕ установлены.

## Обнаруженная проблема и изменение

Первый свежий SDK41.1 вызов после истечения пяти минут получил штатный
owned_workplane_verification_rejected с «Entity-set observation expired or invalid».
Raw request `efbb24bccec54a9a8724ea7d567efdfd` сохранён;
`evidence/K8-workplane-verified-copy-live/result.json`.
SaveAs/OPEN submission=false, новая lease не выдана. Это отказ чтения,
не неизвестный результат новой CAD-команды; исходный executionff2a... остаётся
succeeded_verified и не повторяется.

Причина: Postconditions применял live-lease проверку к immutable before-state.
Исправление сравнивает полный прежний ObservedDigest до/после удаления единственной
созданной workplane из сравниваемого нового снимка. Timestamp исходного снимка
и prepared expiry не переписываются. Исполнение по-прежнему проверяет original
TTL, а independent verifier требует свежие candidate/listing после command return.

После успешно проверенного native результата SDK возвращает новый
workingCopyHandle для того же manifest-owned copyId/ownershipSHA. Его срок ограничен
сроком новых независимых чтений и300с; cross-connection использование запрещено.
Это позволяет заново вызвать bind_working_document для изменённой текущей копии,
а затем подготовить следующий отдельно разрешённый шаг. Нет выдуманного нового
OPEN и нет ослабления saved-OPEN verifier, который сравнивает исходное содержимое.

## Доказательства

-32целевых .NET tests/0skips passed:24 workplane +8 fresh-copy lease cases.
  `evidence/K8-workplane-verified-copy-focused-fixed/verified-copy-lease.trx`.
  Первый запуск31/32 выявил границу микросекунд в TTL; expiry приводится к принятой
  Python precision с округлением вниз. Первоначальный failedTRX сохранён отдельно.
-Native12cases passed; control дополнен matching/changed comparisons со снимком
  двухчасовой давности, original plan bytes unchanged; independent93hash audit.
-SDK/native wire13requests passed: fresh binding после verify в исходном и новом
  подключении, independent98hash audit. Два transport faults/130hash audit passed;
  lost receipts остаются unpublished, unknown unresolved, fences/claims retained.
  `evidence/K8-workplane-historical-qualified/`.
-Точный финальный Native35.1 binary прошёл реальную пассивную проверку старого
  результата с fresh read-only receipts; age>300s, current observations и original
  six-event journal/saved files unchanged.
  `evidence/K8-workplane-historical-final-read/native-result.json`.
  Диагностика вызывает только verifier уже terminal операции без fence;
  не является опубликованной native queue receipt и не выдаёт SDK lease.

Таким образом native age-fix принят чтением реального результата, SDK lease/bind
пока принят сквозным simulated CAD test. Его реальный end-to-end путь будет
проверен после разрешённого обновления35.1 до нового SaveAs.
Производственные guards и запрет повтора неизвестной операции не ослаблялись.

## CI и выпуск

PR57 initial head6687804: оба baseline SUCCESS, оба SDK FAILED в новом native
fixture из-за одинакового UTC tick у command return и следующего fake read.
768.NETtests passed в обоих SDK jobs; полный verify-sdk не завершился.
Исправление только тестового ожидания следующего реального UTC tick вынесено
отдельно в40a410f и отправлено в PR57. Производственный after-return guard строгий.
Текущие35.1/41.1 изменения продолжаются в codex/workplane-verified-copy-lease
поверх40a410f. Не объявлять прежние failed checks успешной полной CI.

Пакет: work/WP351-live-package,18точных native files, отдельный SDK, прежний конфиг
без изменений. NativeSHA4f2eae9f051fa8c3e829e3670629b4ae71a37e037808d5e2aa3fc26c5decadc7;
SDK SHA4af4122521ffae681061991271fa8c895a30f453ce6066aed49b1ead83c08002.
Сценарий K8-LIVE-WORKPLANE-SAVED-RESULT-SCENARIO.md подготовлен.
Нужно новое согласие на backup/update35.0→35.1/одинrestart и одинSaveAs+одинownOPEN.
До него не выполнять Apply или driver--execute. Все прежние разрешения использованы.
