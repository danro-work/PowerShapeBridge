# К7 — отмена запроса через SDK

13 октября 2026. Добавлены 14 строк actual SDK client/server transport tests:
translation, ordinary owned OPEN, SaveAs, saved-result OPEN и activation.
Проверяются два случая: отмена ожидания клиентом и явное уведомление
`notifications/cancelled` с исходным JSON-RPC request ID. Для SaveAs и
saved-result OPEN каждый случай дополнительно проверяется после synthetic
native return с неопубликованным исходным receipt.

Все строки требуют одного retained `.processing`, consumed/revoked handles,
отказа повторного вызова до публикации второго request, неизменного полного
перечня файлов и SHA после отмены и replay rejection. Существующие journals,
claim/fence и unpublished raw receipt сохраняются. Отмена клиента не является
доказательством native command return и не даёт новую binding.

Открытие/сохранение здесь synthetic: временные непрозрачные файлы, fake worker
marker, native COM и установленная панель не вызываются. Production code,
версии и конфигурация MCP не изменены.

Диагностика обнаружила важное различие: в нашем in-memory StreamClientTransport
с pinned SDK2.2.0 отмена `CallToolAsync` прекратила ожидание клиента, но за две
секунды plain SDK server не получил cancellation notification. Это независимо
воспроизведено без Bridge для ревизий2025-11-25 и2026-07-28. Причина внутри
клиентского SDK пока не установлена; на других клиентах/транспортах поведение
не объявляется таким же. Failed diagnostics сохранены, а не заменены green run.

Явное стандартное уведомление отмены сработало: plain SDK controls2/2 и14
Bridge matrix rows прошли. Server gate освобождается в пределах двух секунд
после explicit notification; при local-only cancellation — в пределах исходного
server deadline. Ни один случай не отменяет уже выполненную CAD-команду.
Для stdio протокол требует уведомление с исходным request ID:
https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/docs/specification/2026-07-28/basic/patterns/cancellation.mdx

Доказательства находятся в evidence/K7-transport-cancellation:
focused, plain-sdk-diagnostic, wire-diagnostic — исходные неуспешные проверки;
explicit-notification-diagnostic —2 passed;
qualified —16 passed (14 matrix rows +2 protocol controls).
Полная .NET regression в sdk-regression:314 passed,0 failed,0 skipped. Она
включает прежние298 тестов и16 новых. Native и внешние STDIO suites в этом
локальном блоке не повторялись: производственный код не менялся; их последняя
полная квалификация44726bf сохранена в STATUS-K6-QUALIFIED.md.

Source13f1c545732358d09d5096a6f18acf53bd111e55 опубликован в
feat/recovery-sdk-cancellation, draft PR16 attached:
https://github.com/danro-work/PowerShapeBridge/pull/16
Four Windows CI38385716254/38385724636 baseline и38385716200/38385724496 SDK
запущены; результат пока pending, не квалифицирован как passed.

Ограничения: это часть К7, не весь этап. Впереди remaining timeout/lost native
reply rows остальных executors, activation context faults, additional partial
publication и отдельно принятый checkpoint recovery. Переход к К8–К11 сохраняется.
