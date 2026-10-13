# К8 — пассивный провайдер измерений

RotationMeasurements добавляет чтение GetSurfacePoints иGetCompCurvePointData
в удерживаемом DocumentBindings.ComContext. Методы вызываются напрямую с
world_coords=1, имя берётся только из проверенного native key иединственного
совпадающего элемента inventory. Нет selection/activation/Exec/save/RotateItem.
Production queue/MCP tool для этого провайдера пока не добавлены.

Перед getter и после него выполняются по два чтения исходного документа,
проверяются expiry/process/document/контекст/units/наблюдаемые свойства, пустое
выделение и мировой workplane. Повторно сверяются окно, composite mapping
и paired world bounds. Сменившийся контекст не даёт samples.

## Формат и доказательства

Для surface domain требуются явные t/u limits и grid divisions. Не подставляются
предполагаемые ranges0..1: такие значения есть только в собственном fake fixture.
Decoder требует документированный response4 сsuccess0, плоскимиpositions/normals
3*N иBOOL visibility N. Имя/ID,domain,raw units иvisibility закреплены SHA descriptor.

Для curves требуются явные inclusive start/end. Decoder response10 проверяет
positions и tangent vectors, magnitude arrays, freedom flags. Installed HTML
описывает magnitudes3*N; scalar N сейчас отклоняется. Это намеренно незавершённая
квалификация layout, ане доказанная ошибка PowerShape. Нет догадки оpoint count,
индексной базе илиподмены массива при ошибке. Magnitudes/flags входят вpinned
descriptor; поведение при floating drift ещё должно быть принято поlive data.

28 cases/280 protected file hashes прошли в
evidence/K8-rotation-measurements/qualified,
stage work/K8-rotation-measurements-qualified, session61157 завершён успешно.
Контроли обоих типов иотказы: native error code, NaN/bool numeric, array rank,
short positions/normals/tangents, numeric BOOL, changed process/mapping,
expiry, ambiguous names, invalid domain, wrong type иscalar magnitude.
На malformed getter ответ нет повторного запроса. Native regression session76791
завершена:531 checks безCAD, evidence/K8-rotation-measurements/native-regression.txt.
CLI требует explicit own marker; marker refusal не создаёт fixtures.
verify-rotation-measurements.ps1 включён вverify-sdk.ps1.

## Границы

28-case runner использует fake contexts/getter arrays. Реальный ComContext
adapter компилируется, но новые getters ещё не вызываются живым сценарием.
Нет независимого native verifier/fence release, owned before-measurement receipts,
SDK route илиqualification COM sign/frame. Solid face measurements ещё не
реализованы; COG/metrics не подменяют этот пункт. К8–К11 не завершены.

Пассивное чтение установленным MCP подтвердило process17304/23.2.2,
active Test_Block_P2/id280000579, один SOLID/id5,selection/workplane0/0.
Raw snapshot сохранён вevidence/K8-rotation-measurements/passive-active-model.json.
Это снимок состояния, ане принятая ссылка изменения. Из него нельзя вывести
доступность live surface/curve arrays илиsolid face enumeration. Original CAD,
installed runtime иREADY K7 package не менялись; K7 approval всё ещё pending.

Installed OLE/typelib подтверждает signatures иразницу success0 getters против
success1 RotateItem/GetItemBoundingBox. Product Help en_US/PWRSS2023 поsolid face
names опять вернул только UI статьи; они не используются как API enumeration.
Нормативный источник этого decoding — installed IPshapeModelDoc.htm:
GetSurfacePoints/GetCompCurvePointData, аmetadata —
evidence/K8-rotation-measurement-api/typelib.json.

PR30/be7995dede23da10b837236af91636547b090024 прошёл четыре Windows CI:
38470536033/38470575953 baseline,38470536049/38470576050 SDK.
Далее immutable measurement receipts/independent verifier, solid provider,
квалификация getters на собственных CAD-моделях по согласованному сценарию,
production adapter/SDK/faults/saved result иостальные операции доК11.
