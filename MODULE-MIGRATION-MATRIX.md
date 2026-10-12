# Какие модули PowerShape Bridge сохранить и переписать

12 октября 2026. **Матрица принята вместе с [планом](MCP-DEVELOPMENT-PLAN.md).**
Принятие матрицы не означает завершение реализации или квалификации.
Исходная версия 0.31.0/P2.31.

«Сохранить» в первом блоке означает вызывать существующий модуль через очередь
native worker, а не скопировать его в modern MCP-host. «Перенести» — написать
C#-версию адаптерной логики с паритетом схем и поведения. «Переписать» — заменить
ручной механизм SDK или выделенным компонентом. Исторические recipes остаются
источником квалификационных сценариев; готовыми общими инструментами они не являются.

## 1. Сохранить в существующем Framework worker

| Сейчас | Решение и будущая роль | Что принять перед расширением |
|---|---|---|
| [Bridge.cs — Probe](src/Bridge.cs), `ReadInContext` | Native reader в том же COM-контексте. Общий путь использует `false,false` для fixture/capture | Другие типы/модели, raw units, ограничения наблюдаемых свойств |
| [DocumentSession.cs](src/DocumentSession.cs) | Подключение, два чтения окон/документов и проверяемая activation | Stale/cross-document/expiry; один поддержанный процесс; multi-window документы и несколько процессов пока не универсальны |
| [EntitySets.cs](src/EntitySets.cs) | Native inventory, observed sets и guard | Собственная canonical encoding, лимиты, ambiguous native keys; live surfaces/curves отдельно |
| [OwnedCopies.cs](src/OwnedCopies.cs) | Saved-byte copies, независимый ReadOnly checkpoint, native file identity и запрет aliases/reparse | Это не unsaved snapshot. Для SaveAs нужен новый owned target/result contract; не ослаблять текущие layout/identity checks |
| [DocumentBindings.cs](src/DocumentBindings.cs) | Удержание COM-пары и повторная проверка документа/файлов через `IContext` | Standalone binding не разрешает edit; новое действие повторяет guard. Контекст сейчас связан с `OwnedTranslation.IContext` |
| [NativeJournal.cs](src/NativeJournal.cs) | CreateNew/fsync, chain/sequence, transitions и writer marker | Сохранить существующие журналы. `ForVerification` рассчитан на return/open/translation, не на все interrupted states |
| [WorkingCopyOpen.cs](src/WorkingCopyOpen.cs), [OwnedOpenVerification.cs](src/OwnedOpenVerification.cs) | Один own OPEN и independent verify без replay | ASCII/path ограничения и one-use claim сохранить. General open/reopen результата — новый отдельный контракт, старый P2.29 OPEN не повторять |
| [OwnedOperationPlans.cs](src/OwnedOperationPlans.cs) | Immutable dependency packet, byte proofs и два guard-прохода в held COM context | `executionEnabled=false`, source receipts/SHA, distinct Python/native digests, минимальный lifetime |
| [OwnedTranslation.cs](src/OwnedTranslation.cs) | Первый собственный SOLID executor и отдельный verifier | Один TranslateItem, постоянный claim/fence, authoritative returnCode=1 и свежие receipts. Live принят только X ±1 одной модели |

Существующий `OwnershipLease` удерживает ownership receipt, Working и Before;
он не удерживает исходный source file всё время дальнейших операций. Защиту
источников в новом SaveAs проверять явно. Вызов `Hold` нельзя считать полным
доказательством сохранности любого исходника.

## 2. Сохранить сейчас, выделить/доработать позднее

| Сейчас | Решение | Причина |
|---|---|---|
| [Bridge.cs — Config/Program/helpers](src/Bridge.cs) | Оставить прежний entrypoint; после паритета выделить конфигурацию/utility | Файл смешивает STA, serializer, COM, UI, queue и embedded tests. Полная перепись сразу затронет квалифицированный исполнитель |
| [Bridge.cs — Panel/FileRequests/RunChild](src/Bridge.cs), [QueueOperations.cs](src/QueueOperations.cs) | Сохранить native очередь/allowlist; затем отдельно выделить router/execution control из UI | Новый SDK-host должен обращаться к ней, не создавать свой worker. Claim `.processing`, matching response, maintenance и unknown после timeout сохраняются |
| [Launcher.cs](src/Launcher.cs), `start.cmd`, `Start-*.cmd` | Сохранить существующий запуск в первом блоке; новый adapter подключать отдельно | `releases/P2.6` совместимый путь. Launcher пока требует `mcp_server.py`; изменение комплекта — самостоятельная приёмка deployment |
| [build.ps1](build.ps1), [deploy-general.ps1](deploy-general.ps1), [deploy-current.ps1](deploy-current.ps1), [panel-window.ps1](panel-window.ps1) | Сохранить native staging/preflight; добавить отдельную сборку/manifest нового host | Не собирать поверх root EXE/runtime и не применять deploy в CI. Последний native профиль явно выбирается из `config` |
| `TranslationTest.Bounds/Journal` в [TranslationTest.cs](src/TranslationTest.cs), `CheckpointTest.Open/ValidatePath/ModelPath` в [CheckpointTest.cs](src/CheckpointTest.cs) | Позже выделить используемые helpers из test-классов | Общие операции зависят от них; ModelPath проверяет фактически открытый документ. Удаление «тестовых файлов» сломает open/bounds/activation |
| [OwnedTranslationTests.cs](src/OwnedTranslationTests.cs), [OwnedOperationPlanTests.cs](src/OwnedOperationPlanTests.cs), `Bridge.SelfTests` | Сохранить synthetic cases; позднее выделить test assembly | Сохранить fake contexts, bytes/tamper/unknown cases. Нативные тесты не доказывают паритет нового MCP-клиента |

## 3. Переписать MCP-слой с использованием SDK

| Сейчас | Будущий компонент | Что меняется и что сохраняется |
|---|---|---|
| [mcp_server.py — dispatch/run/send/reply](src/mcp_server.py) | `Host` + официальный SDK | SDK берёт STDIO/JSON-RPC/negotiation/registration/cancellation notifications. Сохраняются имена tools, states, `structuredContent`, `isError`, ограничения клиента и отсутствие лишнего stdout |
| [mcp_server.py — catalog/call](src/mcp_server.py), [core_config.py](src/core_config.py) | `Tools` + профильная политика/typed contracts | Вместо монолитного dispatcher — handlers по операциям. Явный allowlist read-only/full, exact arguments и экспериментальные флаги сохраняются; SDK annotations не заменяют запреты |
| [mcp_server.py — snapshot/status/operation_result](src/mcp_server.py) | `BridgeIpc` + diagnostics/proof access | Atomic `.tmp → .json`, UUID-correlated response, serial submit, maintenance, deadline вместе с ожиданием очереди, cancel до/после submit и no replay |
| Ручные `slice_grid/validate_mutation_args` и fixture routes в [mcp_server.py](src/mcp_server.py) | Разделить generic validation и qualification clients | Строгие общие validators остаются; technological_grid/W/h/k и фиксированные jobs не попадают в общий host |

Первый host не должен публиковать все методы через неконтролируемый assembly
discovery. Для каждого профиля фиксируется явный каталог и проверяется запрет
прямого вызова скрытого метода. STDIO логи идут в stderr с самого старта.

## 4. Перенести правила Python в C#; не заменять их «возможностями SDK»

| Python-модуль | Предлагаемая C#-роль | Обязательный паритет |
|---|---|---|
| [entity_references.py](src/entity_references.py) | `ReadReferenceStore` | TTL ≤300, process/start/session/model/path identity, повторное чтение, отзыв после смены наблюдений, запрет mutation по read refs |
| [document_handles.py](src/document_handles.py) | `DocumentChoiceStore` | One-use document choice, native listing receipt/bytes SHA, consume до подачи; window ID не постоянный |
| [entity_sets.py](src/entity_sets.py) | `EntitySetStore` | Candidate и set namespaces раздельны, type/unique/document checks, лимиты и отзыв после смены контекста |
| [operation_plans.py](src/operation_plans.py) | `OperationPlanStore` | Роли/тип/disjoint intersection checks; immutable local plan, минимальный expiry dependencies, Python observation digest, raw units, disabled execution и durable prepared journal |
| [working_copies.py](src/working_copies.py) | `WorkingCopyLeaseStore` | Адаптерный lease отделён от native ownership и разрешения CAD-edit; произвольный path/copyId не заменяет handle |
| [document_bindings.py](src/document_bindings.py) | `DocumentBindingStore` | Exact native schemas/SHA, ownership/candidate-inspection receipts, повторная проверка после worker, отсутствие продления lifetime; связь с set добавляется в OwnedOperationStore |
| [owned_operation_plans.py](src/owned_operation_plans.py) | `OwnedOperationStore` | Связь local plan/set/copy/binding/packet, immutable dependencies, отдельное потребление и disabled packet |
| [working_copy_open.py](src/working_copy_open.py) | Owned open/verify facade | Однократный intent, validate/prepare/native receipt validation, независимая verification без нового OPEN |
| [owned_translation.py](src/owned_translation.py) | Owned execute/verify facade | Раздельные execute/verify, отзыв CAD leases перед submit, frozen receipt/plan, unknown/no replay; fence решает native verifier |

Исходный Python сохраняется в качестве reference implementation и regression
oracle до приёмки каждой строки. Его unit tests импортируют Python stores,
поэтому сами по себе не тестируют C#-порт. Сравнительные tests должны вызывать
оба host через публичный MCP и сопоставлять существенное поведение, а не UUID
или wall-clock timestamps, которые закономерно отличаются.

Область stores — одно подключение. Request-scoped DI потеряет handles между
вызовами; общий singleton нескольких клиентов смешает ownership. Нужна явная
session scope и синхронизация consume/revoke при конкурентных SDK callbacks.

Python digest использует sorted compact UTF-8 JSON (`ensure_ascii=False`, без
NaN); native digest имеет свою canonical/numeric encoding. В
[OWNED-OPERATION-PLANS.md](OWNED-OPERATION-PLANS.md) это разделение закреплено.
Raw receipt/plan/journal SHA считается по неизменённым bytes, не по сериализованному
заново DTO. Golden vectors обязательны до перехода к owned executor.

## 5. Сохранить проверки; доработать harness для нового host

| Сейчас | Решение |
|---|---|
| `test_*.py` | Сохранить требования/negative cases; добавить C# contract tests и параметризованный process client. Часть tests требует native synthetic staging |
| `audit_*.py`, `finalize-owned-*-evidence.ps1` | Сохранить независимые byte/journal audits; адаптировать command/envelope при неизменных proof schemas |
| `qualify_*.py`, `qualify-core-read.ps1`, `verify-entity-sets.ps1` | Сохранить как отдельно управляемые qualification harness; не запускать live-режимы из hosted CI/установки |
| `test-copy-paths.ps1`, `test-deployment-preflight.ps1`, `record-core-protection.ps1` | Переиспользовать защитные сценарии/manifest checks; staging/профиль/интерпретатор задавать явно |

Новый CI должен готовить synthetic native journal до полного unittest discover.
Путь `PSBRIDGE_TEST_STAGE` или прежний fallback `work/P2.29-open` сейчас делает
полный набор tests зависимым от локального испытательного окружения. Установка
SDK не исправляет это автоматически. Fixture logs в публичном Git должны быть
синтетическими, а не копией журналов пользовательской модели.

## 6. Отложить как qualification/технологические клиенты

| Файлы | Почему не переносить целиком в ядро |
|---|---|
| `PlannedTranslation.cs`, `SurfaceRecipes.cs`, `SliceRecipes.cs`, fixture части `TranslationTest.cs`/`CheckpointTest.cs` | Фиксированные baseline/path/геометрия; сохраняются для регрессии, общая операция получает новый параметризованный контракт |
| `SectionQualification.cs`, `BatchSectionQualification.cs`, `SurfaceSectionJob.cs` | Эталон 25 плоскостей, COPIES 24/Z0.8 и конкретные рабочие стенки. Дают алгоритмическое основание, а не общую приёмку |
| `ExchangeQualification.cs`, `GroupedExport.cs`, `ReadReferenceQualification.cs`, `DiskVerification.cs` | Конкретные квалификационные сценарии/пути и постусловия. `DiskVerification.CloseAudit` с `FILE CLOSE SELECTED YES` не является общим safe-close |
| `slice_job.py`, `surface_job.py`, `group_job.py`, `batch_section_job.py` и соответствующие `audit_*job.py` | Job orchestration/technological_grid/группировка DGK относятся к qualification либо будущему отдельному технологическому клиенту |

Заимствовать можно принцип «диапазон → явный набор секущих → одно пересечение
двух наборов → проверка всех кривых → удаление только своих temporaries».
Шаг/высоты задаёт клиент. Правило DGK по технологическому слою не становится
встроенным правилом общего intersection/export инструмента.

## 7. Чего ещё нет и что нужно разработать заново

- Guarded **SaveAs в новый собственный result target** с ownership/reservation,
  сохранением protected sources/checkpoint и независимой saved-byte verification.
- **Отдельный reopen результата** и новые document leases после него.
- **Общий recovery-reader** для прерываний до return, partial writer и non-success
  codes. Это новая квалификация; generic logger или удаление fence её не заменяют.
- Параметризованные rotate/scale, organization, plane set/array/intersection,
  объявленные форматы import/export, owned close и средства наблюдения — по
  согласованному v1 scope; image/UI availability отдельно от CAD-данных.
- Воспроизводимый SDK host build/package, manifests, клиентская установка,
  CI и rollback-пакет. Release/tag — после общей многомодельной квалификации.

Подробная последовательность и пользовательские точки приёмки — в
[MCP-DEVELOPMENT-PLAN.md](MCP-DEVELOPMENT-PLAN.md). Перенос кода/контракта
проводится отдельно от признания его универсальным и отдельно от deployment.
