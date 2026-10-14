# К8 — чтение одного выделенного SOLID

Подготовлен 14 октября 2026. Сценарий ещё не выполнен; ожидается сообщение
пользователя после ручного выделения.

1. Пользователь вручную выделяет единственное тело в уже открытой собственной
   копии `Working_7b0765448a714a97a8f78edf46754834` и сообщает «выделено».
2. Повторное inspect_active_model должно подтвердить эту копию, один SOLID
   типа 40/ID 5/имя `1`, world context [1,0], idle, неизменённые process/model
   identity и пустую очередь. Другой документ/набор не подменять ожидаемым.
3. Новое наблюдение сохранить в `work/K8-native-selection-nonempty/preflight.json`.
4. Однократно запустить отдельный attach-only diagnostic из этого каталога с
   флагом `--read-only-existing`, staging assembly
   `work/K9-dgk-wire-runner/PSBridge.Worker.exe`, свежей preflight и новым
   `work/K8-native-selection-nonempty/result.json`. Существующий result не
   перезаписывать. Main/worker из staging assembly не запускать.
5. `tools/audit_native_selection_nonempty.py` независимо проверяет getter и
   ItemData, точный выбранный native key/composite, контекст и семь окон,
   saved bytes, 11 защищённых файлов К7, хеши preflight и diagnostic binary.

Автоматизация только читает. Не меняет выделение, слои, активный документ,
геометрию; не сохраняет, не экспортирует и не пользуется мышью. Выделение,
сделанное пользователем, остаётся на месте. Если требуется его снять, это
отдельное действие пользователя; никаких скрытых CLEARLIST/restore.

Успех доказывает только чтение одного выбранного SOLID в этой копии.
Пустой getter=null, другие типы, selection mutation, сохранение/переключение/
переоткрытие, DGK export и общий MCP v1 не считаются квалифицированными.
