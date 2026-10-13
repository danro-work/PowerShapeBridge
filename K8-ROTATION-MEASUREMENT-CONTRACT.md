# К8 — измерения для независимой проверки поворота

Этот блок — reference predicate для наблюдаемых данных. Он ещё не является
независимым native verifier и не снимает mutation fence. Исполнитель PR29
остаётся внутренним; production COM/queue/MCP execution не включены.

## Что реализовано

Общий C#6-код RotationSampleVerifier компилируется в .NET10 host и отдельно
в .NET Framework. Вход — собственный native key, явные world/document_raw,
SHA неизменного measurement descriptor, именованные позиции и направления,
числовые rigid invariants и native paired bbox(xmin/xmax/ymin/ymax/zmin/zmax).
Точки и направления имеют отдельные уникальные anchors; порядок закреплён.
Нулевые/нечисловые/неограниченные tolerance и неоднозначные anchors отклоняются.

Проверяются:

- Координаты каждой точки после Rodrigues rotation вокруг явного pivot/axis.
- Направления после rotation без добавления pivot: normal/tangent — вектор.
- Сохранность измеренных invariants иmeasurement identity/domain.
- Exact bbox лишь при signed coordinate permutation. При остальных углах —
  прямое и обратное containment в conservative envelope. Равенство bbox
  повёрнутым углам исходного AABB не требуется.
- Конечность результатов иtolerance; overflow span не превращается в бесконечный
  допуск. Допуски выражены в raw-единицах, не выдаются за проверенные мм/мм²/мм³.

21 профильный тест прошёл в evidence/K8-rotation-sample-verification/qualified:
три типа целей, положительная/отрицательная иобратная оси, явный pivot,
unit-circle45°, неправильные points/directions/units/IDs/anchors/invariants,
нечисловые данные, bbox иtolerance. Отдельный Framework runner проверил две
аналитические задачи иотказ дляno-op, без CAD. verify-rotation-samples.ps1
включён вverify-sdk.ps1. Полная .NET регрессия session35217 завершена успешно:439/439 без skip,
evidence/K8-rotation-sample-verification/sdk-regression.

## Источники данных Autodesk

Установленный IPshapeModelDoc.htm иoffline PowershapeNT.tlb проверены повторно.
tools/ReadPowerShapeTypeLibrary.cs теперь извлекает также measurement signatures;
evidence/K8-rotation-measurement-api/typelib.json получен сREGKIND_NONE без создания
приложения/документа ибез подключения кPowerShape.

| Метод | Подтверждённый контракт | Нерешённая часть |
|---|---|---|
| GetSurfacePoints | surface_name BSTR, world_coords I4, t/u limits R8, division counts I4; returns VARIANT positions/normals/visibility grid; error0 success | Надёжное получение ranges ипроверка native array layout для разных surface representations |
| GetCompCurvePointData | curve_name BSTR, start/end I4, world_coords I4; VARIANT positions/tangent arrays; error0 success | Полный point count, строгая связь name/ID иarray layout |
| GetNurbSurfaceData | surface_name LPWSTR, index ranges I4, world_coords I4; VARIANT nested control points; error0 success | Тип surface, полные dimensions; control points нельзя считать точками внутри geometric bbox |
| GetSurfacePointData | surface_name LPWSTR, index ranges I4, world_coords I4; HRESULT иout arrays/status | Layout/out marshal иbounds полных ranges |
| GetSolidOwner | composite_id I4 →owner composite ID | Документация говорит owner **solid**; это не подтверждённый способ перечислить грани тела |

Эти read methods возвращают success0, аRotateItem — success1. Нельзя переносить
код проверки одного returnCode на другой. Неизвестные arrays/indices/ranges
должны давать unavailable, ане guessed points или изменение модели.

Product Help en_US/PWRSS2023 иinstalled OLE — разные источники. Help описывает
[Rotating objects](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-725B177C-A55E-4A64-B303-72A4069C1DE6),
ноне доказывает COM sign/frame/SAFEARRAY. Последний поиск поsolid surface IDs
вернул нерелевантные UI статьи; изнего не выводится API перечисления граней.

## До выпуска

Нужен qualified provider реальных измерений длякаждого target типа, immutable
дооперационный measurement receipt иего SHA вexecution plan. Позже verifier
считает fresh measurements вновой удерживаемой CAD-паре, сверяет process/document,
anchors/descriptor/journal/claim/fence ипоследовательность выполненных targets.
Отсутствующие измерения илинеподтверждённая native convention не дают success.
Длячастично исполненного пакета нужен отдельный recovery path без повторов.

Даже matching samples/metrics не являются доказательством полной топологической
эквивалентности. Sparse kernel не заменяет всеCAD-наборы К10 иживую квалификацию.
К7 approval pending; runtime/READY package не менялись. К8–К11 не завершены.
