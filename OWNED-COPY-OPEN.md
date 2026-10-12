# Открытие собственной сохранённой копии — экспериментальный контракт P2.29

P2.31: runtime 0.31.0/21 tools. Explicit executor одного owned SOLID и отдельная independent verification приняты по OWNED-TRANSLATION.md. Passive plan/packet executionEnabled=false сами edit не разрешают; executor создаёт отдельный execution plan и повторяет guard до команды. Own OPEN P2.29 не повторялось, claim/journal сохранены. Ниже исторический контракт этого отдельного компонента.

P2.30: runtime 0.30.0/19 tools. Уже открытая копия повторно проверена без OPEN, её claim и шесть событий журнала сохранены. Native coupled guard принят отдельно по OWNED-OPERATION-PLANS.md; геометрия выключена. Ниже контракт и историческая приёмка P2.29.

9 октября 2026. Панель/worker/свежий адаптер 0.29.0, профиль general-v1.
Подтверждены одно открытие собственной сохранённой копии, независимая проверка
полученного исхода и успешная пассивная CAD-привязка. Геометрический executor
отсутствует; локальные планы сохраняют executionEnabled=false. MCP v1 не завершён.

## Конфигурация и интерфейс

workingCopyOpenExperimental=true требует general-v1, workingCopiesExperimental,
entitySetsExperimental и documentBindingsExperimental. Профиль
config/general-working-copy-open.json включает также локальные планы: каталог 17.
Read-only сохраняет 6 инструментов; open/verify отсутствуют и отклоняются до очереди.
Прежний профиль general-document-bindings.json сохраняет каталог 15/6.

| Инструмент | Публичные аргументы | Результат |
|---|---|---|
| open_working_copy | workingCopyHandle, candidateDocumentHandle, activeDocumentHandle | Одно открытие fixed Working.psmodel; opened_observed либо отказ/неизвестный исход |
| verify_working_copy_open | openRequestId, openReceiptSha256, candidateDocumentHandle, activeDocumentHandle | Проверка уже вернувшегося открытия без CAD-команд; journal succeeded_verified и новый рабочий handle |

Handles выдаются тем же подключением, действуют не более 300 секунд. Candidate
документ получают свежим inspect_entity_candidates, явный выбор активного окна —
list_documents. Document handle одноразовый. Срок нативного запроса — минимум
сроков этих двух чтений. P2.21 read references, произвольные native ID, пути, команды,
overwrite или выбор checkpoint не принимаются.

open требует workingCopyHandle от prepare_working_copy. verify принимает доказательство
конкретного ранее вернувшегося native запроса, а не copyId или произвольный файл:
SHA относится к полным байтам responses/<request>.json. Новый рабочий handle после
verify относится к ownership этого исходного открытия; источник не копируется снова.
Этот handle нужен для inspect_working_copy и пассивной bind_working_document,
но не авторизует геометрическую команду.

## Guard и одна команда открытия

Исполнитель проверяет general профиль, единственный PowerShape 23.2.2 в том же
Windows-сеансе, процесс/start/session, busy, immutable ownership, native file IDs,
независимый ReadOnly checkpoint и свежие SHA native inspection/listing receipts.
executor.lock допускает одного исполнителя Bridge. До завершения удерживаются
file leases ownership/work/checkpoint и одна COM application; перед открытием
удерживается проверенный active document. Пара наблюдений, IUnknown и стабильные
окна сверяются с receipts непосредственно перед действием. Выбор должен совпасть
с уже активной моделью; entity selection должна быть пустой. Копия ещё не открыта.

Выходной путь выводится из ownership: только собственный Working.psmodel, сейчас
ASCII local installation path без пробелов/кавычек/разделителей команд. Source и
checkpoint не открываются. Нет SAVE, CLOSE, entity selection, screenshot или geometry.
Используется фиксированная последовательность FILE OPENPATH / PATHNAME / ACCEPT.
После открытой копии заново получается active document из той же COM application;
старый документ не используется для её чтения.

После возврата проверяются ровно одно новое модельное окно, активная собственная
копия, независимый новый modelId/windowNumber, прежние windowNumber/modelId/name/path/type,
текущее отсутствие дубликатов WINDOW.ID, новое чтение модели, checkpoint/file IDs,
хеши дисковых файлов прежних документов и стабильность окон. WINDOW.ID может
перенумеровываться и повторно использоваться после открытия; его сохранность между
списками не требуется. Внутри отдельного guard/выбора сравнивается полный свежий
список, включая IDs. Окна не становятся постоянными ссылками.

## Durable журнал и неизвестный исход

До команды CreateNew/fsync фиксирует copyopenplans/<operationId>.json и SHA.
Отдельный immutable workingcopies/<copyId>/open-once.json закрепляет operationId,
plan SHA и запрет replay. Наличие claim прекращает последующее open до команды,
даже если исход прежнего запроса неизвестен. Автоматического удаления claim нет.

Журнал operations/<operationId>/000000.json... содержит hash chain, схему P2.26,
plan SHA, номера, время и детали. Запись создаёт writer.lock и новый файл, делает
Flush(true); при неполной записи marker/bytes остаются для разбора.

Нормальный путь: prepared → execution_claimed → command_started → command_returned
→ executed_unverified. Same-worker постусловия дают working_copy_opened_observed;
независимая проверка ещё необходима. Ошибка после начала команды даёт
working_copy_open_outcome_unknown, retryAllowed=false и outcome_unknown в журнале,
если запись возможна. Сначала operation_result и независимые чтения; повтор open,
автоматический save/close/cleanup недопустимы. При timeout/cancellation после подачи
или несоответствии returned plan адаптер отзывает CAD handles и working-copy leases.

verify проверяет исходный response SHA, frozen plan, open-once claim и всю цепочку
журнала. Требуется записанный command_returned: interrupted writer, неполная цепочка,
команда без возврата и неподтверждённая принадлежность отклоняются. Новые независимые
inspection/listing должны быть позже command_returned и оставаться свежими. В одном
удерживаемом COM-контексте повторяются два чтения, guard собственности, проверка
окон/сохранённых файлов и IUnknown. После этого appended succeeded_verified содержит
ID/SHA двух независимых чтений и исходного открытия. Неизвестный event не переписывается.
Повтор verification уже verified-журнала проверяет факты заново без новой terminal записи;
это не повтор CAD-команды. Общий recovery/rollback этим не реализуется.

## Приёмка и границы

В P2.29 первая команда вернулась, но same-worker проверка ошибочно потребовала
сохранить WINDOW.ID. Получен unknown; сценарий остановлен. Независимое чтение
установило одно новое окно и сохранность прежних документов. Исправлены постусловие
и синтетическая регрессия перенумерации. Уже выполненное открытие затем подтверждено
verify без повтора; live binding/validate, набор SOLID, disabled local plan и независимое
чтение прошли. Базовый Test_Block_P2 снова выбран свежим handle. Своя копия оставлена
открытой, без сохранения/закрытия. STATUS-P2.29-OPEN.md и evidence/P2.29-open.

Квалифицирован один сохранённый SOLID. Новый исправленный путь same-worker success
проверен кодом; живой исход принят через отдельную verification того же открытия.
Геометрия остальных документов в памяти/unsaved state не сертифицированы.
Объём/COG/площадь/число поверхностей не доказывают полной топологии. Raw единицы
сохраняются; area units на этом эталоне не разрешены. Guard не блокирует пользователя
или сторонний CAD-клиент и не даёт непрерывного lifecycle между запросами.
Сохранение/import/export/close, общие геометрические операции и приёмка поверхностей/кривых/других моделей
остаются впереди. P2.30 интеграция plan/set/binding принята только как passive guard.
