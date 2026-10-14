# К8 — чтение слотов слоёв без изменения фильтров

14 октября 2026. Добавлен NativeLevelReading и retained COM getters:
имена/USED/ACTIVE слотов0–999, три текущих filter flags, независимые scalar
samples0/5/997/999. Два полных чтения, occupied inventory mapping, filter/model/
window/lifetime guards. Нет изменения visibility, filters, selection, geometry
или CAD save. Отдельный attach-only diagnostic не запускает worker Main.

## Actual COM

В собственной копии К7 при оставленном пользователем выделении [1,0] прочитаны
все 1000 slot names/flags. Наблюдались String имена и Double флаги ровно0/1.
Scalar samples совпали с batch rows. Context/selection/inventory и все семь
окон неизменны; 11 защищённых файлов и saved bytes рабочей копии совпали.
Отдельный `tools/audit_native_levels_read.py` — passed.

Не считать USED=0 разрешением переиспользовать слот: именованные, зарезервированные
или пустые слои требуют отдельной политики. Все numeric raw flags и их runtime
типы сохранены. Row IDs — номера слотов, не постоянные edit handles.
Это одно passive observation; nativeLayoutQualified=false. Public MCP recipe/
SDK tool и runtime deployment ещё не добавлены.

## Исправления по реальному чтению

- Evaluate returns Double flag values. Reader принимает только Int32/Double
  **точно0/1**, сохраняет исходные types; strings/Bool/fraction/NaN/Infinity
  отказывает. Нативный Int32 fixture остаётся проверяемым отдельно.
- EvaluateArray со string[] substitutions возвращал ложные zero arrays,
  включая имена. Это не принято за успешный read. SAFEARRAY(VARIANT) object[]
  substitutions с текстовыми индексами дал правильные имена/flags и совпал со
  scalar getters. Оба отвергнутых actual reports сохранены.
- Исходный isolated fixture cast ArrayList→Dictionary[] исправлен в test setup;
  его ошибка сохранена. Сам native selection reader не менялся.
- Restricted native baseline остановился на File.Replace/PublishPanelStatus
  с AccessDenied. Новый isolated native baseline запущен под user token;
  это не изменение production atomic replace и не CAD test.

## Доказательства

- `work/K8-native-levels-read/actual-result.json` — first strict type refusal.
- `work/K8-native-levels-read/actual-layout.json` — wrong batch layout refused.
- `work/K8-native-levels-read/actual-object-array.json` — actual read success.
- `work/K8-native-levels-read/independent-audit.json` — passed/1000 slots/11 files.
- `work/K8-native-levels-qualified/reports/native-levels-fixture.json` — 29 cases.
- `evidence/K8-native-levels-runner/native-audit.json` — exact runner passed,
  3 controls/26 refusals, marker refusal; встроен в verify-sdk.ps1/source hashes.
- Native baseline59795 terminal exit0: **515 checks**, general-core profile,
  no PowerShape connection. Это число для данного профиля, не прежние531 checks
  с experimental flags. Restricted failure76375 сохранена отдельно.

Далее source/CI publication, публичное read-only чтение слоёв через SDK/native
fixed recipe; owned level/workplane operations отдельными сценариями.
Также проверить DGK working-file pin: current OwnedDgkTargets использует extra
FileShare.Read на активной копии, а PowerShape держит writer handle. Diagnostic
подтвердил sharing conflict; protected checkpoint/source guards не ослаблять.
Полные требования К8–К11 остаются обязательными.
