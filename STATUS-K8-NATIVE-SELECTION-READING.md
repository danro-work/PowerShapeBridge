# К8 — native selection getter и offline API evidence

Реализованы strict passive decoder/guard и реальные getter wrappers
DocumentBindings.ComContext для GetSelectedItems/GetModelItems/GetItemData.
MCP/queue/installed runtime не изменены; текущий PowerShape не подключался.

PASS:21 synthetic selection cases и независимый Python audit:4 controls/17
refusals. work/K8-native-selection-qualified, evidence/K8-native-selection.
Framework panel/worker x64 собираются. Missing marker отказывает до создания
стенда. Новый runner включён в verify-sdk для будущего доступного CI.
Регрессия другого passive provider в изменённом ComContext прошла:
28 rotation-measurement cases/280 audited hashes, без CAD.
Evidence:evidence/K8-native-selection/measurement-regression.

tools/ReadPowerShapeExportMetadata.cs offline прочитал14 type names и два
relevant dispatch interfaces из установленного PowershapeNT.tlb, без CAD,
регистрации типов или создания COM application. Evidence:
evidence/K8-native-export-metadata/typelib.json, source SHA
1f17bcc01a6a34c9aa53b6fb8571ab62a6ea45add9eb8d4d4583d78f893a33f0.

Важные ограничения:SelectionManager по installed docs Not implemented;
PSMMExport deprecated/no-op; GeneralTolerance не является export tolerance.
Выдуманный settings getter не добавляется. Метод Execute имеет out extended
error, но native success code не квалифицирован. Контракт:NATIVE-SELECTION-READING.md.

Следующий блок:qualified direct selection layout и documented export settings/
recipes, затем native export/geometry verification/SDK/faults и весь К8–К11.
Готовые К7-пакеты не менялись;live approval не получено. Новый review snapshot
готовится отдельным PR после export/scale. GitHub доступ вновь подтверждён
разрешёнными действиями;old approval quota failure не считается текущим blocker.
Native layout/live getters всё ещё требуют отдельной квалификации.
