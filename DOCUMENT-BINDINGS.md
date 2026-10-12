# Привязка собственного CAD-документа — экспериментальный контракт P2.28

P2.31, 12 октября 2026: runtime 0.31.0, каталог 21/read-only 6. Первый общий executor переносит один SOLID в manifest-owned копии: два guard-прохода в удерживаемой COM-паре, один TranslateItem, постоянный intent claim, copy fence и отдельная independent verification. Live X +1 и отдельный X −1 подтверждены; наблюдения собственной копии восстановлены, Test_Block_P2 снова активен, четыре окна и protected saved hashes сохранены. Save/open/close/selection/screenshot не выполнялись. OWNED-TRANSLATION.md и STATUS-P2.31-EXECUTOR.md; ниже история предыдущих блоков. MCP v1 не завершён. Локальный plan и passive packet остаются executionEnabled=false; explicit execute создаёт отдельный native plan с executionEnabled=true и повторяет guard до команды.

Ниже контракт, введённый в P2.28; тогда успех был синтетическим, live отказ чужой
пользовательской модели принят. Полный MCP v1 не завершён.

## Инструменты и конфигурация

documentBindingsExperimental=true требует general-v1, workingCopiesExperimental
и entitySetsExperimental. config/general-document-bindings.json включает также
локальные планы: каталог 15, read-only 6. Прежние профили сохраняют свои каталоги.

bind_working_document принимает только candidateDocumentHandle из свежего
inspect_entity_candidates и workingCopyHandle из этого подключения. Нативные ID,
пути, SHA, read references P2.21, команда и overwrite на публичном входе запрещены.
В worker отправляются фиксированные copyId/ownershipSha256 и
inspectionRequestId/inspectionSha256/expiresUtc, извлечённые из локальных leases.
Модель должна быть уже активной и иметь точный путь принадлежащего копии
Working.psmodel. Источник, checkpoint и чужие документы не допускаются.

validate_working_document_binding принимает workingDocumentBindingHandle того же
подключения и отправляет только bindingId/bindingSha256. Ни один инструмент не
вызывает Exec, выделение, активацию, открытие/сохранение/закрытие модели или screenshot.
Подготовка пишет только неизменяемый локальный документ привязки; validate его не меняет.
Read-only исключает оба инструмента: выдача handle требует подготовки в этом сеансе.

## Нативный контекст и файловые leases

Проверяются SHA именно прочитанных байтов исходного native receipt, его срок и
полнота наблюдения. executor.lock исключает второго исполнителя Bridge. PowerShape
должен быть единственным экземпляром в текущем Windows-сеансе, версии 23.2.2 и
не busy. GetActiveObject подключается к запущенному приложению; CAD не запускается.

Удерживаются один RCW приложения и один RCW ActiveDocument. Probe.ReadInContext
использует переданные объекты и не переподключается/не освобождает их. Два полных
наблюдения сверяются с исходным receipt: process/start/session/version, модель,
инвентарь/доступные свойства, selection/workplane, слои и raw-единицы. Стабильный
перечень окон читается до/между окончательными проверками; окно активной модели
должно быть единственным для неё и согласованным с наблюдением. На успешном пути
активный COM-документ проверяется также по IUnknown; busy/context/expiry меняют результат
на отказ. RCW освобождаются владельцем при выходе, а не между наблюдениями.

OwnedCopies.OwnershipLease удерживает ownership, working и checkpoint до окончания
проверки/записи привязки. Замена/удаление рабочей копии и ownership, запись/удаление
checkpoint запрещены sharing. Рабочий файл допускает открытого писателя CAD;
его SHA наблюдается до и повторно в конце, различие даёт отказ. Проверяются native
ID/creation, отдельные файлы без aliases/reparse/hard-links, checkpoint ReadOnly/SHA.
File attributes и байты рабочих писателей не становятся атомарной CAD-ревизией.

## Артефакт, handles и отказы

documentbindings/bindingId.json создаётся CreateNew + fsync. Schema 1 содержит
исходные пять параметров, binding ID, версию/время, observedPropertiesSha256,
windowContextSha256 и executionEnabled=false. SHA закрепляет байты документа.
Validate проверяет этот файл, исходный receipt, прежние окна и новые наблюдения.
Нельзя заменить binding или повторно подготовить его под прежним request ID.

Успех: working_document_bound_observed / working_document_binding_validated_observed.
Публичный handle хранится в памяти подключения, максимум 128; срок ограничен
исходным наблюдением и обеими локальными leases, не продлевается validate. Они
проверяются повторно после native ответа. Активация и наблюдённая смена контекста,
утрата candidate/copy lease, отказ/timeout/отмена отзывают binding handle; нет
автоматического повторения запроса или восстановления handle из произвольного файла.

Отказ: working_document_binding_rejected. Для чужого активного документа —
errorCode=active_document_not_owned_working_copy. Диагностика после двух чтений
включает observedModel, digest наблюдений/окон и sameContextObservationReads=2;
при отказе привязка не выдаётся. При транспортном неизвестном исходе могут остаться
локальные файлы: сначала request/response и диск, без replay или автоматического cleanup.

## Границы и следующая интеграция

Совпадение наблюдений/COM-указателей в одном запросе не доказывает полной топологии,
непрерывного lifecycle или отсутствия ненаблюдённого закрытия/повторного открытия
между запросами. Измерения не атомарны. executor.lock не блокирует пользователя,
другой CAD-клиент или вмешательство администратора в файловую систему.

Standalone binding/validate освобождает COM и file leases. Будущий исполнитель обязан
повторить проверки в своём удерживаемом контексте непосредственно перед командой,
связать план/наборы с владением, вести нативный durable журнал и проверять постусловия.
Сейчас prepare_operation_plan не принимает binding/copy handle, executionEnabled=false;
Геометрический CAD executor отсутствует. P2.29 отдельно принял own open и live
binding. Сохранение, replacement policy, general recovery и MCP v1 впереди.

Доказательства: STATUS-P2.28-BINDINGS.md и evidence/P2.28-bindings.
