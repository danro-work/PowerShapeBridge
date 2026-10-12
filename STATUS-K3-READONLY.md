# К3 — перенос чтения, выполняется

12 октября 2026. К3 ещё не закрыт. К2 объединён PR #3 после успешного clean
checkout и SDK/baseline Windows CI. Native runtime остаётся 0.31.0.

Реализована основа: `ObservationPolicy` проверяет process/model identity,
полноту и уникальность inventory, наблюдаемые свойства/units/workplanes;
вычисляет Python-compatible observation digest и нормализует Windows path
без разрешения относительного/несохранённого пути на диске.

17 synthetic golden cases созданы сохранённой Python-политикой:
базовое наблюдение, пустой/относительный/UNC путь, разные float encodings,
Unicode names/keys, emoji, U+2028/U+2029, пустой и несортированный inventory.
Identity/observation SHA совпали. Добавлены шесть invalid-observation tests.
Первый .NET набор: 68 passed, 0 failed, 0 skipped;
`evidence/K3-readonly/K3-foundation.trx`. В sandbox две файловые проверки
получили отказ доступа к atomic fixture rename; полный повтор в обычном
пользовательском контексте прошёл. Golden cases сами прошли в обоих контекстах.

Это Python encoding, не native C# receipt digest; raw receipt SHA всегда
вычисляется из оригинальных bytes. Наблюдения не доказывают полную топологию.

Следующий блок: реализованы ReferenceStore, DocumentHandleStore, CandidateStore
и LeaseTime. Хранилища не глобальные; каждый будущий BridgeSession получит свои
экземпляры. Read reference resolve не продлевает TTL, observed switch/back не
возрождает old token; candidate handles отделены от read references.
Document choice consume однократный, неподдержанное окно получает null handle.
Lease timestamps сохраняют Python microsecond precision и offset.
17 новых checks проверяют expiry во время чтения, девять observed changes,
cross-client/document, read/candidate isolation, wrong document/duplicates,
unsupported types, one-use document choice и временную точность.
Полный набор: 85 passed, 0 failed, 0 skipped;
`evidence/K3-readonly/K3-stores.trx`, пользовательский Windows-контекст, без CAD.
Native execution и public preparation tool этими хранилищами не регистрируются.

Остаётся выполнить перед закрытием К3:

1. Подключить готовые temporal stores к BridgeSession с единым async guard
   на весь read/require/observe/issue цикл. Проверить их область подключения
   и отмену уже через SDK, включая refresh/expiry/switch/reopen/cross-client.
2. Fixed IPC routes для list_documents, inspect_active_model,
   resolve_entity_references, inspect_entity_candidates.
3. Явный readonly allowlist из шести tools текущего general-профиля, runtime
   validation и отказы для всех мутационных direct calls до очереди.
4. SDK/fake-panel и independent Python parity tests, clean checkout и Windows CI.

Публичный SDK-каталог сейчас всё ещё содержит только `bridge_status` и
`operation_result`. Никакой новой CAD-функции этим блоком не квалифицировано.
