# Перенос собственного SOLID — экспериментальный контракт P2.31

12 октября 2026. Runtime/панель/worker/свежий адаптер 0.31.0; каталог 21/read-only 6.
Приняты две разные однократные операции X +1 и X −1 на одной manifest-owned копии
с независимой проверкой каждого результата. MCP v1 не завершён.

## Инструменты и флаг

ownedTranslationsExperimental=true требует general-v1 и ownedOperationPlansExperimental.
Профиль config/general-owned-translations.json включает предыдущие эксперименты,
но не испытательные fixture-флаги/пути. Прежний профиль P2.30 сохраняет 19 tools.
В read-only сервере оба новых инструмента отсутствуют и отклоняются до очереди.

| Инструмент | Вход | Результат |
|---|---|---|
| execute_owned_translation | ownedOperationHandle этого подключения | один direct COM TranslateItem; executed_unverified и блокировка копии |
| verify_owned_translation | executionRequestId/SHA, свежие candidateDocumentHandle и activeDocumentHandle | независимые постусловия, succeeded_verified, снятие только своей блокировки |

На execute публично нельзя передавать native ID/path/SHA/команду. Параметры уже
закреплены локальным планом и owned packet. Verify принимает ID/SHA исходного
response как ссылку на доказательство, а не как выбор новой команды или цели.
Адаптер извлекает native dependencies, повторно проверяет leases и потребляет
все CAD references/set/plan/binding/packet handles до отправки execute. File-copy
lease остаётся для inspection и новой binding после нового чтения. Отмена до
submission сохраняет packet; неизвестный исход после submission не повторяется.

Планы и passive packet P2.26/P2.30 остаются executionEnabled=false: сами они
не запускают изменение и standalone validation не разрешает edit. Отдельный
explicit execute с включённым флагом создаёт native execution plan с
executionEnabled=true и повторяет guard в удерживаемом контексте до команды.

## Поддержанный первый executor

Только translate_entities, один набор и ровно один ключ SOLID. Требуются пустое
выделение, мировой workplane, resolved lengthUnitsRaw=mm, lengthUnitFactorRaw=1
и мировой COG с raw mm label. Вектор — три конечных числа, ненулевой, компоненты
по модулю ≤1e6; mode=move, coordinateFrame=world, lengthUnits=document_raw.
Пересечения, поверхности/кривые, несколько target-сущностей и неизвестные единицы
отклоняются до изменения. Live приняты только X ±1; другие модели/векторы впереди.

Worker удерживает executor.lock, одну COM application/document и immutable
plan/set/receipt/binding/ownership/checkpoint proofs. Guard выполняется дважды,
с четырьмя полными наблюдениями; nativeId преобразуется в ровно один compositeId
через GetModelItems/GetItemData того же удерживаемого документа. Между проходами
проверяются direct world bounds, ID mapping, prepared-only журналы и working bytes.
IUnknown активного документа, окна и срок проверяются непосредственно до команды.

Вызывается TranslateItem(compositeId, dx, dy, dz) один раз, без Exec/selection/save.
Сигнатуры/return codes и world flag GetItemBoundingBox сверены с установленным
Autodesk IPshapeModelDoc.htm для 23.2.2. Bounds до/после читаются без выделения.

После return=1 два чтения проверяют сдвиг world bounds и COG (≤1e-6 raw mm),
неизменность inventory/прочих свойств/контекста и raw volume/area (абсолютная
или относительная погрешность 1e-9). Неизвестные area units не выдаются за mm².
Saved bytes открытых документов и независимого checkpoint перепроверяются.
Успех worker — owned_translation_executed_unverified; 6 same-context observations,
guardPasses=2, postconditionReadPasses=2, commandCalls=1. Это не full topology proof.

## Однократность, журнал и неизвестный исход

Execution plan в ownedexecutions/<requestId>.json закрепляет 21 поле: исходный
planId/SHA, bindingId/SHA, owned packetId/SHA, copyId/ownershipSha, workingSha,
source inspectionId/SHA, window digest, native/composite IDs, vector, исходные
world bounds, timestamp, schema/operation/executionEnabled. CreateNew/fsync.

Перед командой создаются mutation-fence.json в owned copy и постоянный
operationplans/<localPlanId>.execution-once.json. Claim запрещает повтор того же
локального намерения даже через новый packet; fence блокирует любые новые
переносы этой копии до независимого разрешения результата. Файлы fence/claim
удерживаются с запретом записи/удаления во время команды.

Журнал: prepared → execution_claimed → command_started → command_returned →
executed_unverified → succeeded_verified. При доказанном отказе до изменения
пишется rejected_before_change, только новая собственная fence удаляется;
claim не снимается. При COM exception, non-success return, несоответствии после
команды или прерванной записи — unknown/незавершённый журнал и fence сохраняются.
Автоматических retry, inverse, save, repair/removal fence нет.

Verify требует свежие independent inspection/listing receipts после recorded
command return, их SHA и активный owned документ. Он удерживает COM/files и
перепроверяет математику сдвига, контекст/окна/working/checkpoint/saved bytes.
Return code=1 обязателен одновременно в исходном response и authoritative
command_returned event; подмена response не снимает fence. Ссылки трёх receipts
пишутся в succeeded_verified; только matching fence ID/SHA удаляется после
проверки. Повтор verify уже verified результата не добавляет событие и не
исполняет команду. Чужая/подменённая fence не удаляется.

Interrupted command без return и результаты с code 0/−1 этим verify не разрешаются.
Читать journal/receipts и выполнять отдельно проектируемое recovery; не делать
догадок о результате и не продолжать изменения. Полный recovery остаётся впереди.

## Приёмка и ограничения

114 Python, 312 C# и MCP-протокол без COM прошли. Синтетические тесты покрывают
guard/expiry/tamper, неоднозначные зависимости, unknown/no replay, нового packet
для уже claimed намерения, block copy при неизвестном исходе, interrupted call,
wrong COG, подмену fence и response returnCode при authoritative code=0.

Live использована только ранее открытая собственная копия. +1 X отдельно
проверен; затем после свежего чтения/binding/set/plan/packet выполнен −1 X и
отдельно проверен. Обратная операция не является автоматическим восстановлением
unknown. Исходные наблюдения собственной копии восстановлены; Test_Block_P2 снова
активен, четыре окна сохранены. Source/STEP/baseline/checkpoint не записывались.
Open/save/close/selection/screenshot отсутствовали; own-open P2.29 не повторялось.

Сохранённые байты рабочей копии не изменились. Dirty/unsaved state и полная топология
после forward/inverse не сертифицированы. Guard не блокирует внешнего CAD-клиента
или пользователя и не даёт непрерывной ревизии между запросами. Временные fence
после verified удалены; аудитор реконструировал их scalar records и сверил native
SHA, исходные байты до удаления отдельно не архивировались. Постоянные claims,
журналы, execution plans и native receipts сохранены.

Следом SaveAs с защитой исходников и отдельный reopen/результат, затем другие
модели/сущности/операции и interrupted recovery. STATUS-P2.31-EXECUTOR.md;
evidence/P2.31-executor содержит 119 exact native artifacts и independent audit.
