# К7 — пассивное чтение legacy activation journal

Source SDK0.37.5-dev, native/legacy0.33.4-dev. Installed native0.33.3 и
default MCP configuration не менялись.

inspect_operation_recovery теперь принимает старый journal activation:
`operations/<id>.json` плюс empty `.once` marker и наличие partial `.json.tmp`.
Проверяются identity, типы flags, phase/state combination, timestamp и неизменность
прочитанного journal/marker observation. Paths в сохранённом journal являются
данными: модель и savedFilesBefore по этим путям не открываются. Чтение сообщает
historical observation/rejection/partial publication, а outcome остаётся
not_resolved_by_recorded_inspection. Нет authoritative command-return proof,
новых bindings, execution/checkpoint restore authority, replay или journal repair.

Сохранены прежние modern append-only owned-operation paths и отдельный read-only
сервер. Нового публичного tool нет; description существующего reader уточнён,
registration остаётся RecoveryTools.Create(session) через StrictTool и прежний
recoveryInspectionExperimental flag. SDK Version обновлён вместе с csproj;
NuGet packages и транспорт не менялись.

Проверено13 unit cases: pending/unknown/completed/rejected/partial journal,
wrong phase/state/time, geometry/save flags, missing/nonempty claim и marker
directory. Даже completed не объявляется текущим успехом CAD. Files/hash inventory
сохраняется, requests directory не создаётся.

Независимый STDIO client выполнил40 cases (20 actual native activation fault
artifacts × general/read-only modes). Originals copied в собственные reader
fixtures; исходные native artifacts не изменены. Raw SHA/journal bodies сверены,
unsafe ID отклонён schema binding, interrupted pre-command publication без
journal остаётся unavailable. No queue/CAD, no flags granting authority.
Доказательства: evidence/K7-legacy-recovery/focused и focused-stdio/
activation-recovery-stdio/verification.json.

Full qualification прошла в evidence/K7-legacy-recovery/sdk-qualified,
native stage work/K7-legacy-recovery-native-20261013:passed343 .NET,
32 SDK rows/681 hashes/four negative checks,531 native/175 artifacts0 mismatch,
25 native faults+audit,20 activation faults+audit,40-case actual-native STDIO
и Inspector. Итоговый manifest passed; recorded source/binary hashes сверены,
hash-audit.json. Installed runtime/default config/CAD этим блоком не менялись.

PR18/sourceee72c68: все four Windows CI passed38391353385/38391190648 baseline,
38391353400/38391190694 SDK. Следующий обязательный К7 block — остальные
interrupted publication points и отдельный принятый checkpoint recovery.
Checkpoint recovery должен создавать NEW owned working file из защищённого
checkpoint; original unknown/claim/fence и исходные файлы сохраняются. Это
планируемая возможность, не результат этого reader.
