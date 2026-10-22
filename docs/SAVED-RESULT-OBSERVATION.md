# Пассивная проверка сохранённого результата после переподключения

Статус: Native core собран в staging и проверен реальным чтением одного уже сохранённого SOLID/mm. Это первый этап отдельного reconnect-механизма. MCP tool, новый SDK savedResultHandle и последующее переоткрытие ещё не реализованы. Установленный runtime и настройка клиента не менялись.

Обычный `verify_owned_save_as` проверяет исходную команду и её список окон. Повторное соединение теряет краткоживущие SDK handles; последующая управляемая уборка окон также меняет контекст этой исходной проверки. Повторять SaveAs для получения доступа к уже проверенному файлу не требуется.

`SavedResultObservation.Observe` принимает SHA исходного успешного verifier, свежие inspection/listing receipts, явный активный документ и ограниченный срок наблюдения. `OwnedSavedSource(..., historicalProof: true)` удерживает исходную цепочку, оригинальные source/checkpoint/working dependencies, terminal journal/once claim и физический saved file с проверкой длины, identity и SHA. Исторический срок разрешения не продлевается: его доказательства используются как история успешного сохранения.

Новый retained context проверяет тот же процесс/сеанс/документ, доступные свойства/единицы, WORLD и пустое выделение. Два чтения проверяют неизменность текущих свойств и окон; saved hashes проверяются до и после. Несвязанные текущие окна могут отличаться от исторических. Исходный журнал и прежняя проверка окон не переписываются и не объявляются проверенными повторно.

Выход — отдельный frozen report со scope `current_saved_result_same_process_document_observation_v1`. Только sealed Native COM context даёт `nativeProviderQualified=true`; fake context всегда unqualified. В обоих случаях `executionAuthority=false`, `sdkLeaseIssued=false`. Нет CAD-команд, снятия fences, выбора, активации, сохранения или закрытия. Входные параметры отделяются от вызывающего кода до callbacks.

Локальная проверка: 20 собственных synthetic scenarios / 78 проверок, marker refusal, сборка Framework caller и отказ без аргументов. Реальное чтение финального core подтвердило сохранённый результат при уже истёкшем исходном verifier lease. Отдельный read-only сервер и независимый аудит подтвердили наблюдения, исходную журнальную цепочку и неизменность защищённых/сохранённых файлов. Это проверка доступных свойств; full topology, persistent face/UV correspondence и нормали не сертифицируются.

`verify-saved-result-observation.ps1` подключён в `verify-sdk.ps1`. Существующие verifier, NativeJournal, SDK-каталог и read-only режим не ослаблены. CLI `--saved-result-observation-test` работает только с собственной fixture marker и не подключается к CAD.

Следующий блок: отдельный full-only MCP admission с default-false flag, строгими схемами/receipt checks и новым ограниченным saved-result lease. Downstream preparation/execution должны явно понимать новый scope, сохраняя первоначальные verification receipts и запрет повтора команд. До реального изменения окон проверить весь provenance путь; затем qualified owned discard и один новый saved-result OPEN. Текущий лимит20 не повышался, при25 моделях OPEN остаётся запрещённым.
