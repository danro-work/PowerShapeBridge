# К3 — SDK-сервер чтения

12 октября 2026. Локальные проверки пройдены; clean checkout/Windows CI ещё
ожидаются. Новый host `0.33.0-dev`, native runtime остаётся `0.31.0`.
Живое подключение/переключение адаптера и изменения CAD не выполнялись.

Публичный каталог текущего general-profile содержит шесть tools:
`bridge_status`, `operation_result`, `list_documents`, `inspect_active_model`,
`resolve_entity_references`, `inspect_entity_candidates`. При отключённом
`entitySetsExperimental` последний отсутствует, каталог содержит пять tools.
Пока full/general mode нового host тоже предоставляет только эти read tools;
мутационный каталог переносится на следующих стадиях.

Регистрация — явный ToolCatalog/WithTools. StrictTool проверяет режим/наличие,
точные аргументы, token format, array count/uniqueness и admission до вызова
метода. На подключение допускается максимум восемь pending tool calls;
повторный request ID отвергается, string и numeric IDs различаются.
Все мутационные direct-call имена, legacy model_snapshot и image arguments
отвергаются до появления заявки в очереди. SDK hints остаются advisory.

BridgeSession владеет своими ReferenceStore/DocumentHandleStore/CandidateStore
и одной IPC lane. Единый async guard охватывает require/read/observe/issue/
resolve, поэтому leases атомарно меняются в области подключения. Срок запроса
включает ожидание этого guard, в IPC передаётся оставшийся budget.
До submit тайм-аут не создаёт заявку; после submit unknown не повторяется.

Read tools публикуют только `recipe.prepare` с фиксированным `list_documents`
или `inspect_active_model` и пустыми native parameters. User references/handles
не становятся native commands. Worker не запускается самим SDK-host; существующая
панель остаётся единственным CAD-маршрутом. Borrowed native reader остаётся тем же,
`ReadInContext(false,false)` не создаёт capture. Unexpected capture receipt
отвергается, image bytes не открываются и не возвращаются из read-only.

Read references живут 300 секунд без продления при resolve. Observed change
или failed read отзывает их; switch/back не возрождает токен. Чужой/candidate
reference, отвергнутый до fresh read, не отзывает остальные допустимые leases.
Document choice — one-use; multi-window/unsupported choice получает null handle.
Candidate handles отделены от read references и привязаны к исходным receipt
bytes/SHA; runtime сверяет reread receipt с доставленными observations.
Lease timestamps сохраняют Python microsecond precision/offset. Observations
не доказывают полную топологию и не исключают необнаруженное ID reuse.

| Проверка | Локальный результат |
|---|---|
| Release build/locked restore | 0 errors, 0 warnings |
| .NET tests | 98 passed, 0 failed, 0 skipped |
| Python golden observations | 17 identity/observation cases, включая float/Unicode/paths/sorted inventory |
| Настоящий SDK/fake-panel | Каталог 5/6, exact args, mode refusals, fixed recipes, scopes/expiry/unknown/cancel |
| Отдельный STDIO/Python journal test | Schema constraints, malformed-frame recovery, six journal states, raw hashes и writer lock |
| Отдельный Python read parity | Шесть tools, mapping ephemeral tokens, stale/no-revival, cross-client/candidate отказ, raw UTF8/BOM/CRLF hashes |
| Inspector CLI 2.10.1 | Каталог из шести tools и bridge_status, без UI/CAD |

Raw evidence: `evidence/K3-readonly/integration/` — sdk-tests.trx, два STDIO
transcript/stderr, readonly-fake-panel.json, Inspector results и source/binary
hash receipt sdk-verification.json. Ранние foundation/stores TRX сохранены
рядом как история (68/85 checks). На Windows sandbox была transient filesystem
restriction; повторные полные проверки выполнялись в пользовательском контексте.
Fake-panel сначала атомарно claims собственный файл, затем читает его; retry
касается только файлового claim, никакая CAD-команда не повторяется.

Воспроизведение: verify-sdk.ps1 с настоящими dotnet.exe, Python 3.12 и
mcp-inspector.cmd; optional `-EvidenceDirectory`. По умолчанию evidence/SDK,
CI artifact SDK-verification. Первоначальные K2 proofs остаются в своих путях.
Следом К4: остальные stores/пассивные подготовленные состояния и parity;
решение о live переключении и CAD-квалификация остаются на К5.
