# К6 — SDK path format исправлен и live binding выдана

Итог: source44726bf/full298/531/1750 mismatch/STDIO/Inspector/25 audit/four CI
passed,134 hashes matched. Side SDK0.37.4 verify existing OPEN без replay
выдал binding065ba2cc..., validation passed; Test_Block_P2 active/six windows.
Independent live audit passed34 files/71 protected, STATUS-K6-QUALIFIED.md.
Ниже история до квалификации.

Native0.33.3/side SDK0.37.3: own reopen d5ed3054cfa145c887e6805bb5dde541
подготовлен. OPEN45ddd3c930f843ca858486ad1c7fe36c исполнен один раз;
raw SHA полностью записан в qualification.json. Native verifier
cbf78106bcae4b7ab2d78f05e3f0e4f7 вернул owned_saved_reopen_verified_observed,
native journal OPEN succeeded_verified, новая native binding создана.
SDK не выдал scoped binding: saved_reopen_verified_current_model_differs.

Regression воспроизвёл причину: Probe model path D:/.../Working_d5...psmodel,
owned workingPath D:\...\Working_d5...psmodel. Exact JSON string equality
ошибочно считала их разными. SDK0.37.4 использует существующий NormalizePath
для двух validated owned paths; ID/window/source/hash/terminal/binding checks
сохраняются. Different-file path rejected; raw proof не переписывается.

16 focused tests passed; actual STDIO27/6 с forward-slash native snapshot/
listing/verifier replies passed. Первый standalone STDIO прошёл assertions,
но artifact не записался без evidence directory; повтор после mkdir passed.
Full qualification running,298 .NET passed. Native/default config неизменны;
panel restart не требуется.

Активна own Working_d5... model280011427, шесть окон. SaveAs/OPEN не повторяются,
документы не закрываются и не активируются до known-success проверки.
Следом qualified SDK0.37.4 side client: только fresh separate verifier original
OPEN45ddd3.../current binding, никаких новых preparation/OPEN. К6 закрывать
только после independent final raw/protected-file audit.

evidence/K6-reopen-receipt-fix/live-qualification и
evidence/K6-reopen-path-fix/red/focused/stdio-focused/sdk-full.
