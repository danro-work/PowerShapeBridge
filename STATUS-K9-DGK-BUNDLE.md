# К9 — проверка комплекта DGK, внутренний модуль

14 октября 2026. Добавлены shared `DgkBundleVerifier` и native `DgkBundleFiles`.
Контракт: DGK-BUNDLE-CONTRACT.md. DGK — основной файл CAD обмена; DMT/MAC/XML
объявляются отдельными required/optional companions. Отказ при missing required,
stray entry, ambiguous name, malformed DMT, hash/identity drift, file alias,
held writer, nested directory и выходе за supplied root. MAC/XML не исполняются.

Это file-byte witness, а не экспорт или CAD equivalence. Публичного инструмента,
COM provider, queue recipe, ownership authorization, journal/fence transition нет.
Рабочий runtime0.33.5 и открытая К7 copy не изменены этим блоком.

## Доказательства

- 38 .NET случаев, 0 пропусков: `evidence/K9-dgk-bundle/dgk-bundle.trx`.
- 12 Framework файловых случаев: 3 controls/9 refusals, включая реальный hardlink.
  Matrix: `work/K9-dgk-bundle/dgk-bundle-file-tests-b58b0ea6413e46389a55f933877272f6/matrix.json`.
- Независимый Python audit: 12 случаев/5 SHA/fileIndex/размеров/false flags,
  `evidence/K9-dgk-bundle/native-file-audit.json`.
- Restricted SDK regression:613 pass/1 failure/0 skips из614. Отказ в прежнем
  AdvisoryReportTests.File.Replace — AccessDenied. Исходное evidence сохранено
  в `evidence/K9-dgk-bundle/regression/sdk-regression.trx`.
- Повтор в разрешённом ordinary user context:614 pass/0 fail/0 skips,
  `evidence/K9-dgk-bundle/full-user-context/sdk-tests.trx`. Production atomicity
  и тест не ослаблены. Full SDK pipeline52779 terminal exit0/PASS:614 .NET,
  531 native,175 compatibility/0 mismatch,STDIO/faults и Inspector CLI.
  Terminal `sdk-verification.json`:passed true,finished2026-10-14T12:27:14Z.

Финальная source-hash inventory runner дополнена четырьмя новыми DGK файлами
после завершения локального прогона; test execution не менялся. Точный выполненный
runner сохранён в `work/K9-dgk-verified-runner.ps1`, его SHA совпадает с marker.
Публикация отдельно фиксирует source SHA; CI проверит окончательный runner.

Build совместно компилирует shared verifier в Framework/native staging и .NET10
host; 0 предупреждений .NET. verify-sdk.ps1 включает actual native file matrix и
independent Python audit, чтобы Windows CI проверял обе реализации.

Autodesk Product Help en_US/PWRSS2023 использован для companions и frame defaults;
ссылки и границы переноса — в контракте. Ни одно default-описание не принято
за getter действующих настроек. Тестовые первичные DGK намеренно synthetic bytes;
успешная проверка не говорит, что их можно импортировать как CAD.

Следом owned DGK target/manifest, explicit selection/frame/settings, per-step
native executor/once/fence, independent receipt/owned import и CAD roundtrip.
К8 scale/organization, К9 intersections/planes/import/close/observation,
матрица К10 и пользовательская приёмка К11 сохраняются в полном scope.
