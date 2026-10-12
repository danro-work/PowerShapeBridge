# Локальный план и журнал — экспериментальный контракт P2.26

P2.31, 12 октября 2026: runtime 0.31.0, каталог 21/read-only 6. Первый общий executor переносит один SOLID в manifest-owned копии: два guard-прохода в удерживаемой COM-паре, один TranslateItem, постоянный intent claim, copy fence и отдельная independent verification. Live X +1 и отдельный X −1 подтверждены; наблюдения собственной копии восстановлены, Test_Block_P2 снова активен, четыре окна и protected saved hashes сохранены. Save/open/close/selection/screenshot не выполнялись. OWNED-TRANSLATION.md и STATUS-P2.31-EXECUTOR.md; ниже история предыдущих блоков. MCP v1 не завершён. Локальный plan и passive packet остаются executionEnabled=false; explicit execute создаёт отдельный native plan с executionEnabled=true и повторяет guard до команды.

Текущее P2.27: runtime 0.27.0; добавлена подготовка/проверка saved disk copies по
WORKING-COPIES.md. С обоими прежними экспериментами каталог 13/read-only 6.
Планы остаются executionEnabled=false и не принимают workingCopyHandle;
file ownership predicate не является COM guard. Ниже исходный контракт P2.26.

9 октября 2026. Профиль general-v1, адаптер/панель/worker 0.26.0. Это подготовка
плана, без CAD-исполнителя; универсальные преобразования и пересечения не приняты.

## Интерфейс

Флаг operationPlansExperimental=true требует general-v1 и entitySetsExperimental=true.
Профиль config/general-operation-plans.json публикует 11 инструментов. Read-only
сохраняет 6: чтение кандидатов и существующего журнала доступно, подготовка/проверка
handle плана исключена, как и prepare/validate entity set и активация документа.

prepare_operation_plan принимает operation, entitySetHandles, parameters. Handles
наборов — из этого MCP-подключения; native ID, пути, SHA или произвольные команды
клиент не передаёт. Перенос требует ровно один набор; пересечение — два разных.

| Планируемая операция | Параметры | Проверяемый состав |
|---|---|---|
| translate_entities | vector=[X,Y,Z], coordinateFrame=world, lengthUnits=document_raw, mode=move | Один набор SOLID/SURFACE/COMPCURVE |
| intersect_entity_sets | offset=0 | Основной SOLID/SURFACE, второстепенный только SURFACE; общих сущностей нет |

Вектор конечный и ненулевой; каждая компонента по модулю не превышает 1 000 000
в документных raw-единицах. Bool/строка/NaN/Infinity, иные режимы/системы/единицы,
неподдержанный offset и дополнительные поля отклоняются. Ограничение raw-вектора
не является физическим пределом в мм. Роли intersection заданы порядком handles;
план относится к созданию кривых, не к булевому изменению тел. offset=0 здесь лишь
зафиксированный параметр: его нативная установка и результат ещё не квалифицированы.

Адаптер проверяет сохранённые байты набора и исходного native receipt по SHA-256,
структуру, срок до 300 секунд от исходного чтения, точное наличие ключей. Для двух
наборов совпадают процесс/документ и все записанные наблюдения: полный инвентарь,
свойства, слои, выделение, контекст, workplanes, raw-единицы/коэффициенты/допуск.
Подготовка плана не делает нового чтения CAD. Незаметное внешнее изменение сейчас
не исключается; будущий guard должен обнаружить наблюдаемое несоответствие перед
командой. Проверка отдельного набора не заменяет этот guard.

Создаётся operationplans/operationId.json через exclusive CreateNew и fsync.
Сохраняются параметры, ID/SHA каждого набора/receipt, состав по ролям, process/document
identity, raw-единицы, минимальный expiry, наблюдаемый digest и явное
executionEnabled=false. Digest использует Python JSON sorted keys; он отделён от
нативного C# digest. Ни один из них не является полной топологической ревизией.
Требования qualified operation / owned working copy / independent checkpoint /
guard in executor COM context / postconditions отмечены как ещё обязательные.
Такие планы не принимаются инструментом исполнения: его в каталоге нет.

Ответ operation_plan_prepared_local содержит operationId, planSha256,
operationPlanHandle и plan. inspect_operation_plan по handle проверяет локальные
байты/срок/исходные receipts/журнал, возвращает operation_plan_inspected_local.
Это тоже не новое чтение CAD. Handle живёт не дольше наборов; max 128 планов на
подключение. Чужой/истёкший/отозванный handle отвергается. Отзыв набора/наблюдённая
смена контекста делает прежний план недоступным через handle. Записанные файлы
сохраняются как доказательства; operation_result читает журнал по operationId.
После переподключения handles нужно получать заново; импорт плана по пути отсутствует.

## Журнал и неизвестный исход

operations/operationId/000000.json и следующие — неизменяемые события, каждое
с operationId, planSha256, sequence, previousSha256, state, timestampUtc, details.
Exclusive writer.lock исключает второго писателя; fsync завершает запись. После
аварии marker/частичная запись сохраняются, автоматически не очищаются.
Проверяются последовательность, SHA предыдущих байтов, постоянство SHA плана и
допустимый переход. Это диагностика записанного журнала, не защита от произвольного
переписывания всего каталога и не доказательство отсутствия усечения последнего
события. При чтении возвращается digest хвоста для внешнего закрепления.

Публичный путь сейчас пишет только prepared. Будущие внутренние переходы проверены
кодом: prepared → execution_claimed → command_started → command_returned →
succeeded_verified / executed_unverified / outcome_unknown / recovery_required.
После prepared/claim допустим rejected_before_change при доказательствах отсутствия
команды. Повторный claim, неверный sequence, запись в terminal state отклоняются.
Нет публичного setter журнала или инструмента исполнения.

Незавершённый claim/start/return или оставшийся writer.lock читается как
outcome_unknown, с отдельным recordedState и retryAllowed=false. Из неизвестного
исхода новый claim/start запрещён. Для succeeded_verified/recovery_required/
rejected_before_change нужны ссылки requestId/SHA на независимые доказательства.
Сами ссылки пока проверяются по форме: привязка реальных нативных постусловий
и доказательств остаётся задачей исполнителя. Эти состояния квалифицированы лишь
синтетическими тестами; они не свидетельствуют о выполненной CAD-операции.

Неиспользованный/повреждённый журнал даёт operation_journal_unavailable,
outcome=unknown, retryAllowed=false. Частичный сбой записи плана/журнала возвращает
operation_plan_rejected с известным operationId/SHA и localArtifactsMayExist;
CAD-команд в этом пути нет. Перед созданием проверяются cancellation и maintenance;
после начала локальной записи она завершается синхронно, не отменяет сделанные записи.

## Приёмка и следующий блок

На активном SOLID прошли prepare/inspect плана переноса и чтение prepared-журнала
через обычный и read-only MCP, чужой handle и отсутствующее исполнение отклонены.
Это пассивная приёмка локальных артефактов. Пересечение двух наборов, все переходы
журнала, подмена/expiry/ошибки записи проверены в коде, без CAD. См.
STATUS-P2.26-PLANS.md и evidence/P2.26-plans.

Далее native guard в собственном COM-контексте команды, владение рабочими копиями,
защита исходников/checkpoint, привязка durable журнала к нативному requestId и
независимая проверка результата. Приёмка геометрии — на собственных копиях разных
моделей. Поверхности/кривые, lifecycle/reopen entity sets требуют живой приёмки.
