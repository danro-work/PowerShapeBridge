# К7 — чтение неизвестного исхода, первый блок

Дополнение проверки: отказ oversized plan теперь возвращает
recovery_inspection_unavailable, сохраняя unknown, вместо исключения наружу.
20 domain/3 protocol recovery cases; чистый код5da2c04 прошёл262 .NET tests.
Независимый src/test_sdk_recovery_stdio.py проверил шесть успешных чтений через
STDIO: start без return, return без fresh verification и исторический success,
каждый в full/read-only. Ошибочный extra command отклонён; SHA/атрибуты всех
фиктивных файлов неизменны, requests не создан. Тест включён в verify-sdk.ps1;
внешних STDIO scripts теперь восемь. Evidence: evidence/K7-recovery/independent.
71 protected files после actual old-unknown inspection совпали с доинспекционным
snapshot по double SHA/size: reader/protected-after-inspection.json. Это проверка
сохранённых байтов в данный момент, не оценка текущей несохранённой CAD-модели.
Clean baselinee6b6588 пройден; последняя правка не меняет native/Python source.
Полный clean SDK5da2c04 прошёл:262 .NET/514 native/171 artifacts, seven прежних
STDIO scripts/Inspector; все123 recorded source/binary SHA совпали с clean files.
Новый восьмой script проверен отдельно на этой clean DLL. Evidence:
work/c7s/evidence/K7-clean-sdk; reader/clean-hash-audit.json. Exact-head CI
daba4db passed: baseline38316661689/38316666225, SDK38316661779/38316666329.
Actual SDK log подтвердил262 tests/171 artifact cases/eight STDIO scripts,
включая six external recovery cases, и Inspector. Original unknown journal tail SHA,
plan SHA и claim content повторно сверены: reader/retained-unknown-audit.json,
все совпали; eventCount3 и исход unknown сохраняются.
Draft PR8: https://github.com/danro-work/PowerShapeBridge/pull/8. К7 не завершён.

13 октября 2026. Реализован SDK0.37.0-dev `inspect_operation_recovery(operationId)`
через RecoveryTools.Create/ToolCatalog. Новый opt-in профиль
config/general-recovery-inspection.json включает recoveryInspectionExperimental;
полный/read-only catalogs28/7. Без флага прежние профили сохраняют27/6 и21/6.
Установленный runtime и опубликованный пакет К6 не изменены. Live К6 ожидает
запрошенное ранее разрешение установки; этапы К6 и К7 не завершены.

Reader не обращается к PowerShape и не создаёт requests, journal events,
claims, fences или файлы восстановления. Он читает ровно один plan из fixed
ownedexecutions/saveexecutions/copyopenplans, закрепляет SHA с recorded journal,
проверяет ownership, фиксированные рабочий/checkpoint paths, claim/fence и
authoritative return details. Рабочие сохранённые bytes и readonly checkpoint
сверяются двойным SHA/size через ReadWrite sharing без delete. Чтение не исключает
писателя, не проверяет физическую file identity или текущую CAD-геометрию.
Native operation verifier обязан отдельно повторить identity/context checks.
Произвольный source path из ownership reader не открывает.

Результат всегда executionAuthorized=false/retryAllowed=false. Классификации:

| Наблюдение | Оценка | Допустимый следующий шаг |
|---|---|---|
| Recorded start без return | started_without_authoritative_return | Unknown сохраняется; новый контекст и отдельный recovery-сценарий |
| Return code0/−1 | non_success_or_unproven_return_requires_recovery | Никакого auto inverse/Undo/cleanup; отдельное подтверждение исхода |
| Authoritative success return | returned_requires_independent_verification | Operation-specific fresh verifier, не повтор команды |
| Старый succeeded_verified | historically_verified_requires_fresh_context | Это исторический proof, новые ссылки/текущий контекст не гарантированы |
| Writer marker/partial publication | journal_writer_or_partial_publication | Исследование исходных файлов без удаления marker/«починки» цепочки |
| Changed working/checkpoint bytes | working_saved_bytes_changed/checkpoint_protection_changed | Не считать прежние файлы восстановлением; отдельная проверка роли/источника |
| Missing/foreign claim/fence/plan, invalid chain | recovery_inspection_unavailable | Unknown, без предоставления нового права исполнения |

Оценка «нет recorded start» не означает доказанное отсутствие изменения.
Даже правильный journal и одинаковые disk bytes не разрешают UNKNOWN в SUCCESS.
Reader не меняет состояние старого OPEN6ed9...: actual retained journal прочитан,
выдан started_without_authoritative_return, claim/журнал сохранены.
Evidence: evidence/K7-recovery/reader/legacy-unknown-inspection.json.

Проверены phase matrix, writer/changed files, ambiguous/tampered plans,
missing/foreign claim/fence, cancellation/deadline, codes0/−1/1, strict schemas
и opt-in gates/full-read-only SDK pipe clients. Targeted19 domain +3 protocol
tests passed; full261 .NET passed. Общий SDK/native/STDIO regression пройден:
261 .NET/514 native/171 actual artifacts, seven external STDIO и Inspector.
Evidence: evidence/K7-recovery/full. Inspector отдельно подтвердил explicit
read-only catalog7. Clean/CI этого блока ещё ожидаются. Первый полный run имел transient старого
ReadRoutesTests file-sharing failure; isolated и следующий full261 прошли.

Далее К7: отдельный журнал разрешения исхода, доказательства changed live context,
fault matrix для lost response/timeout/non-success/partial publication и отдельно
принятый restore-from-checkpoint сценарий. Они пока НЕ реализованы этим reader.
Не подменять эти требования новым reader и не объявлять К7 завершённым.

Autodesk [Recovering a model](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-3EF761A5-A110-4F0A-8D5F-5AA91EB4141E)
описывает UI recovery после crash и зависимость результата от последнего autosave/
типа solids. Это другой механизм, не proof разрешения Bridge-operation outcome.
Его настройки/диалоги/Save и deliberate crash в general reader не вызываются.
