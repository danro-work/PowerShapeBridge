# К8 — порядок времени в стенде и реальные данные GeneralSolid

Один SDK CI PR33/sourcea4d8559 завершился ошибкой, второй прошёл. Actual log
run38477064300 показал отказ контрольного сценария: independent inspection
получил ту же отметку UTC, что aggregate command_returned. Production verifier
правильно требует строго более позднее наблюдение.

Fixture теперь ждёт реального продвижения UTC после сохранённого времени
возврата, максимум одну секунду. Он не подставляет время из будущего и не меняет
проверяющий код. Добавлен equal_inspection: ровно равная отметка должна отклоняться.
15 сценариев/497 хешей прошли в
evidence/K8-rotation-verification/clock-qualified,
stage work/K8-rotation-verifier-clock-qualified, session78053 завершён успешно.
До расширения матрицы исправленный14-case runner также прошёл. Новый полный CI
требуется; PR33 не объявляется прошедшим четыре проверки.

## Проверка источника Autodesk

Изучен официальный репозиторий Autodesk/PowerShapeAndPowerMillAPI на commit
f0761e50ae833e7647423b8da3ba24bfc23bd93d. Выбранные исходники сохранены только
в work/K8-autodesk-api-source, SDK не установлен и не запущен.

- PSSolidPrimitive читает ORIGIN/XAXIS/YAXIS/ZAXIS через Evaluate.
- PSSolid.GetSolidType читает TYPE. GeneralSolid — отдельный тип от примитивов.
- PSSolid.MomentOfInertia возвращает только три значения MOI; это не полный
  подтверждённый inertia tensor, его единицы/базис не принимаются на веру.
- PSSolid.ToDMTModel/WriteToDMTFile вызывают AddToSelection(true) и FILE EXPORT.
  Такой экспорт нельзя объявить пассивным чтением или включить в read-only сервер.
- PSSurface даёт подтверждённые имена параметров NLATS/NLONS. Они ещё не являются
  доказательством полного параметрического диапазона GetSurfacePoints.

Источник: [PSSolid](https://github.com/Autodesk/PowerShapeAndPowerMillAPI/blob/f0761e50ae833e7647423b8da3ba24bfc23bd93d/Delcam.ProductInterface.PowerShape/Entities/Solid/PSSolid.cs),
[PSSolidPrimitive](https://github.com/Autodesk/PowerShapeAndPowerMillAPI/blob/f0761e50ae833e7647423b8da3ba24bfc23bd93d/Delcam.ProductInterface.PowerShape/Entities/Solid/PSSolidPrimitive.cs).
Исходники содержат изменения2024, поэтому наличие свойства отдельно проверено
на установленном PowerShape2023; GitHub source сам по себе не равен квалификации23.2.2.

## Пассивный probe установленного PowerShape

tools/ReadSolidPrimitiveFrame.cs — отдельный console reader, не Bridge worker.
Он использует GetActiveObject, сверяет единственный процесс/версию23.2.2,
retained document identity, world workplane, пустое выделение и inventory до/после
каждого фиксированного property read. Нет запуска приложения, Exec, выделения,
активации, сохранения, поворота, screenshot или мыши. Probe не включён в hosted CI.

Результат evidence/K8-solid-frame/passive-properties.json:
process17304/start2026-10-08T08:38:27.4633424Z, active Test_Block_P2/id280000579,
native SOLID id5/composite800000005. TYPE=GeneralSolid. Для ORIGIN/XAXIS/YAXIS/
ZAXIS возвращена строка «Тело не является примитивом»; она не принимается за вектор.
COG=[0,0,10], MOI содержит три числовых значения. Before/after context одинаков.
Это чтение доступности свойств; primitive frame и общая геометрия тела не подтверждены.

Чтение было однократным. Установленный runtime/READY К7, модели и исходный unknown
не изменялись. Для общего тела остаётся квалифицированный способ получения
геометрии: прямой getter пока не найден, export требует отдельно согласованного
сценария на own copy и отдельного полного режима. COG/MOI не подменяют этот пункт.

К8 и К9–К11 остаются незавершёнными; К7 ждёт прямого согласия пользователя.
Следом — source-derived owned geometry export и квалификация provider/convention,
после согласования живых действий, без снижения требований к общим SOLID.
