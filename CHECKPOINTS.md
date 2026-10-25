# Контрольные точки MCP v1

Актуализировано25октября2026. Объём — docs/V1-BASELINE-PLAN-20261016.md, MCP-DEVELOPMENT-PLAN.md и MCP-V1-CONTRACT.md. Точное текущее состояние/ограничения — PROJECT-STATE.md и указанная там локальная карта. Приёмка P0 пользователем18октября подтверждена; повторная приёмка P0 не ожидается.

| Точка | Состояние | Доказательство / следующий шаг |
|---|---|---|
| К0 | Принято пользователем | План принят к работе до К11; Product Help `en_US` разрешён |
| К1 | Пройден | STATUS-K1-BASELINE.md: clean checkout, 312 C#, 114 Python; оба GitHub Windows CI успешны |
| К2 | Пройден | STATUS-K2-SDK.md: clean checkout, 45 .NET tests, STDIO/Python journal parity, Inspector; все Windows CI успешны |
| К3 | Пройден | SDK 0.33.0-dev: clean checkout, 98 .NET tests, independent Python parity/Inspector; все CI успешны |
| К4 | Пройден | SDK 0.34.0-dev full/read-only 16/6; clean checkout/CI, 141 .NET, 312 native/63 artifact cases |
| К5 | Переход на SDK выполнен | Обычный Codex использует SDK0.48; P0 прошёл через обычный клиент и принят пользователем. SDK0.62 с отдельными SURFACE rotation/uniform-scale профилями квалифицирован отдельно в staging,перезапуск обычного клиента ради него не выполнен |
| К6 | Отдельные own-file workflows квалифицированы | SaveAs/отдельный verifier/readonly saved-byte seal/qualified finalization/generic OPEN/отдельный verifier реально прошли. Изменение сериализации закрытого собственного writer фиксируется отдельно; canonical bytes защищены. Общее покрытие форматов/состояний не заявляется |
| К7 | Отдельные recovery проверены; общий этап открыт | Claims/fences/no replay и независимые proofs сохранены. Для одного SOLID настоящий callback return1 при истёкшем post-read отдельно подтверждён свежими измерениями без переписывания unknown-журнала. Это не recovery всех операций/исходов; прежние исследовательские unknown сохраняются |
| К8 | Выполняется,есть реальные saved/reopened результаты | Узкие SOLID move/rotate/uniform-scale и отдельные levels/workplanes workflows прошли. Новый−30° WORLD поворот вокруг[1,2,3] через[2,−3,4] сохранён/переоткрыт и независимо проверен по8UV/8нормалям/COG/инвариантам/габаритам. Owned sole-SOLID и single-SURFACE-among-six select/clear с отдельными verifiers прошли; один WORLD SurfaceTranslateItem с независимыми sampled proofs прошёл, его persistence теперь прошёл по всем6UV-гридам после SaveAs/seed/finalization/genericOPEN; SURFACE WORLDZ+90 также прошёл по12positions+12normals/error0, его persistence прошёл по всем шести UV-гридам на четырёх этапах; SURFACE−30° вокруг наклонной оси[1,2,3]/центра[2,−3,4] также проверен по12точкам/нормалям; его persistence прошёл на4этапах по72точкам/нормалям всех6поверхностей; один SURFACE uniform scale×1.25 вокруг[2,−3,4] с отдельными sampled proofs прошёл, его persistence также прошёл на4этапах всех6UVгридов; общие selection/матрица типов/наборов/центров ещё нужны |
| К9 | Выполняется отдельными блоками | Qualified own-writer finalization с readonly canonical seed и независимый generic reopen прошли при сохранности остальных окон/файлов. Общие plane set/array/intersections/форматы/наблюдение ещё требуют полного согласованного покрытия |
| К10 | Доказательства собираются; матрица не закрыта | На новой собственной6SURFACE прошли copy/OPEN/read-only и96point/normal snapshots; выявлены bounds/UV-grid gaps старогоадаптера. Новый passive reader отдельно прошёл readonly MCP:12точек/нормалей/видимость, точныйCOMlayout/double, безbounds и безмутаций. Новый отдельный SURFACE select/clear прошёл с72point/normal witnesses/27окнами/473pins. Один отдельный SURFACE WORLD move прошёл:12point shift/error0,other5exact,72normal/visibility/layout/479pins/27windows. Surface move persistence проверен по4этапам×72точки/нормали/503pins/27→26→27окна; Отдельный SURFACE Z90 с12point/normal witnesses прошёл,515pins/27windows сохранены; persistence поворота прошёл:4×72точки/нормали,539pins,27→26→27окна. Отдельный SURFACE−30° вокруг заданной оси/центра прошёл,559pins/27окон сохранены; сохранён/переоткрыт с583pins и4этапами всех6гридов. Один SURFACEuniformscale×1.25 прошёл по12positions/normaldirections/rawarea400→625/603pins/27окнам; сохранение/переоткрытие прошло по4×72точки/нормали/655pins/27→26→27окна. Это не полный набор SURFACEtransforms. Все обязательные операции должны пройти на пяти классах моделей/сеансов. Одна SOLID и успешные синтетические тесты не являются приёмкой v1 |
| К11 | Не достигнут | Release candidate,чистая установка/reconnect/rollback,каталог поддержанного и итоговая личная приёмка пользователя впереди |

К1 запускается в новом checkout без `releases`, командой:

```powershell
./verify-baseline.ps1 -PythonPath (Get-Command python.exe).Source
```

Python должен быть настоящим интерпретатором. Скрипт отказывается работать
поверх установленного runtime. Создаёт собственный staging, непрозрачные
файловые fixtures и искусственные native journals. `--self-test` не подключается
к PowerShape; deployment проверяется только в plan mode с пустыми process fixtures.
Производственный deploy-general.ps1 не изменён. Raw evidence остаётся локально
в `evidence/K1-baseline`, CI публикует его как artifact своего запуска.
