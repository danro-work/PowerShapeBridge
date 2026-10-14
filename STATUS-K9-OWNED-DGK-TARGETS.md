# К9 — собственный DGK target/manifest, пассивная подготовка

14 октября 2026. Реализован внутренний `OwnedDgkTargets`: исходные binding/set
receipts, ownership/checkpoint/current working SHA, explicit world/empty selection,
непустой target set и строгий companion manifest. Подготовка создаёт reservation,
immutable target.json и отдельный пустой artifacts directory. Public tool/native
export provider/COM/once claim/fence resolution пока отсутствуют.

SOLID40/SURFACE21/COMPCURVE59 — initial guarded preparation types; другие типы и
local workplanes ещё квалифицируются. Прежний DMT контракт остаётся40/21 по
умолчанию. Shared dependency constructor получает internal explicit DGK flag;
прежние вызовы и их ограничения не меняются. Contract: OWNED-DGK-TARGETS.md.

42 actual Framework cases passed:6 accepted leases,22 preparation refusals и
14 late lease refusals. Parameter SHA связывает reservation/plan; future prepared
timestamp, expiry, manifest/target path/bytes/directory substitution, held working
writer, unknown fences, extra paths и coercion отвергаются. Все CAD/qualification
flags false. Caller parameters detached before proofs, current saved working
bytes дополнительно удерживаются FileShare.Read. Никакого автоматического cleanup.

Independent audit:42 cases/477 artifact hashes; source/binding/set/checkpoint/
owner/manifest/parameter digest/minimum TTL и неизменность прежних prepared
journals подтверждены. Первая версия аудитора ошибочно требовала отсутствие
исходного prepared журнала fixture; исправлена проверка отсутствия НОВЫХ events
и неизменности исходных hashes. Production guards не ослаблены.

Регрессии прежнего DMT:30 target/lease cases/478 audited hashes passed, включая
отказ curve_target. Полный native self-test531 passed, session59227 terminal0.
Stage:work/K9-dgk-targets-final; evidence/K9-owned-dgk-targets. Новые runner/audit
добавлены в verify-sdk.ps1 и source-hash manifest. Exact runner qualification
в отдельном свежем stage завершён PASS/session91899 exit0:marker refusal,
42 cases/477 hashes. Результаты в evidence/K9-owned-dgk-targets/runner.

PR41/042c71d завершил все4 Windows CI SUCCESS:38564204437/38564215371 baseline,
38564204583/38564215140 SDK. Это предыдущий bundle-byte module, не квалификация
нового target module. Новый код пока local. Installed0.33.5/CAD/К7 copy не менялись.

Следом публикация отдельного feature PR и CI, затем native guarded DGK export
recipe/provider/settings/selection/per-step once/fence, independent bundle receipt
и owned import/roundtrip. Весь К8–К11 остаётся в работе; byte/manifest guards не
доказывают геометрию или текущие export settings Autodesk.
