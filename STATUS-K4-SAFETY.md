# К4 — подготовка и доказательства

12 октября 2026. Локальная проверка пройдена; clean checkout/Windows CI ещё
ожидаются. SDK-host 0.34.0-dev, native runtime 0.31.0 не менялся.
General catalog содержит 16 tools, отдельный read-only — 6 (5 без candidates).
Open/activation/translation/verify endpoints квалифицируются на К5.

Перенесены set/copy leases, immutable local plans/prepared journals,
document bindings и owned packets. Схемы/TTL/dependency checks сохранены;
native COM/held-context guards остаются в существующем Framework worker.
Python и native C# digest encoding разделены. SHA считается из original bytes,
включая BOM/CRLF/whitespace; frozen plans пишутся CreateNew + Flush(true).

Десять prepared facades явно разрешены флагами конфигурации только в general
mode. StrictTool проверяет exact fields/types/tokens, наборы/uniqueness,
qualified operation parameters и source path policy до IPC/файлов.
Schemas зафиксированы из Python catalog; SDK descriptions добавлены отдельно.
BridgeSession владеет всеми stores, ReferenceGate охватывает подготовку,
native response, dependency recheck и выдачу lease. Deadline включает очередь.

Local plan/inspect не отправляют CAD-заявку. Другие facades публикуют только
фиксированные recipe.prepare/имена и параметры из выданных этому подключению
leases. Read references не принимаются за candidates. Prepared plan/packet
сохраняет executionEnabled=false, standalone validate не разрешает edit.
Verification не дописывает journal. Прерывание writer сохраняет marker;
unknown после native submit не повторяется, .processing не удаляется.
Истечение dependency во время native callback не выдаёт binding/packet.
Отмена после claim отзывает соответствующие leases, без остановки CAD/retry.

| Проверка | Результат |
|---|---|
| Locked restore/Release build | 0 errors, 0 warnings |
| .NET tests | 141 passed, 0 failed, 0 skipped |
| Independent STDIO journal/read tests | Schema constraints, raw hashes, read namespace/scope, stale/no-revival |
| Independent prepared Python parity | 10 facades, exact private parameters; local plan и first prepared event совпали byte-for-byte |
| Framework self-tests в новом staging | 312 checks, fake retained COM context и opaque saved-file fixtures; no CAD |
| Actual Framework artifacts | Python oracle/C# verifier: 63 cases, 0 mismatches, native encoding не заменяется Python |
| Inspector CLI 2.10.1 | Read-only catalog/status; UI не открывался |

Raw evidence: evidence/K4-safety/full — sdk-tests.trx, три transcript/stderr,
fake-panel envelopes, native-fixture-tests.txt, native-oracle.json,
native-compatibility.json, Inspector results и sdk-verification.json с SHA.
Ранние artifact/lease/prepared/facade TRX — история, не текущая приёмка.

verify-sdk.ps1 создаёт fresh native stage (отказывается использовать уже
существующий), затем запускает только reviewed --self-test без COM/UI.
Повторный ручной запуск требует нового -NativeStageDirectory. CI собирает
в новом checkout и сохраняет artifact SDK-verification.
Обычный native build/deployment скрипт не изменён, second panel/worker не запускались.

К4 закрывается после clean checkout и CI. После этого К5 переносит remaining
context/open/execute/verify facade, доказывает one-use native execution на новом
owned scenario и готовит решение пользователя о default live adapter switch.
Общий v1 по-прежнему включает К6–К11; технологический модуль отдельно.
