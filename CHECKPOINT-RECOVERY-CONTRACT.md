# К7 — контракт восстановления из checkpoint, к реализации

Это следующий блок общего MCP v1. Текущий recovery-reader не реализует его и
никогда не выдаёт разрешение на restore. Контракт не означает выполненную CAD
квалификацию или согласие на живое испытание.

Результат восстановления — новая независимая owned рабочая копия сохранённых
байтов checkpoint. Исходная working copy, source, checkpoint, journal, claim/fence
и старый unknown outcome сохраняются. Новая копия не исправляет и не разрешает
исходный неизвестный результат. Несохранённые изменения после checkpoint в неё
не входят. Сохранение/закрытие старой модели и Undo/inverse не входят в операцию.

1. Пользователь/клиент выбирает исходную owned operation по ID. Legacy activation
   не содержит ownership/checkpoint и не является допустимым источником.
2. Подготовка pin-ит исходный native plan SHA, copy ID и ownership SHA, фиксированные
   роли working/checkpoint и checkpoint SHA/size/native file identity. Arbitrary
   path, source вместо checkpoint и другая owned copy отклоняются.
3. Отдельный one-use recovery intent не изменяет исходный claim. Он привязан к
   клиенту, имеет срок жизни и потребляется до публикации изменения. Unknown
   preparation/OPEN не повторяется автоматически по старому intent.
4. Native filesystem guard проверяет readonly checkpoint, fixed layout, отсутствие
   alias/reparse/hard-link, independent file identities и idle single executor.
   Изменившиеся checkpoint bytes/identity/protection запрещают создание копии.
5. Создаются новый unique working file и отдельный readonly checkpoint с ownership
   schema2; применяются существующие OwnedCopies byte/identity guards. Старые
   файлы не перезаписываются. После interrupted publication partial artifacts
   сохраняются, новая working-copy authority не выдаётся по догадке.
6. Полученный файл сначала имеет только disk-copy qualification. Открытие требует
   свежих process/document/listing proofs, одного guarded owned OPEN и separate
   verifier; current document binding создаётся только после проверки новой модели.
7. Сравнение наблюдаемого содержимого с checkpoint выполняется лишь при наличии
   отдельно квалифицированного baseline saved-state proof. Saved-byte equality
   сама по себе не доказывает unsaved-state/topology equivalence.

Режим чтения может проверять origin/plan/owner и checkpoint metadata, но не
создаёт intent с execution authority, working copy, selection или screenshots.
Filesystem preparation и OPEN размещаются в general server с явным feature flag;
existing read-only catalog остаётся отдельным.

Обязательная матрица: valid owned checkpoint; foreign operation/owner; expired/
cross-client/consumed intent; hash/identity/readonly loss; alias/hard-link/reparse;
busy executor/source; partial target/ownership publication; lost response/timeout/
cancel; one OPEN/replay rejection; fresh independent verify/new binding. Для всех
faults сохраняются оригинальные evidence и source/checkpoint bytes.

Живой контрольный сценарий согласуется отдельно по принятому
MCP-DEVELOPMENT-PLAN.md: исходная operation/copy/checkpoint, fresh CAD context,
новая копия, ожидаемый сохранённый baseline, один OPEN, fresh reads/verifier и
исходный unknown без изменения. Проверять на явно собственных тестовых копиях.
Установку подготовленной native версии и перезапуск панели согласовывать после
полной offline/CI квалификации конкретного пакета, без незавершённой очереди.
