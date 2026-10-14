# Независимое чтение DMT для общего MCP

DmtMeshReader — чистый C# reader mesh-файлов. Он доступен для дальнейшей
интеграции с проверкой геометрии, но не зарегистрирован как MCP-инструмент,
не подключается к CAD и не выполняет экспорт, исправление сетки или conversion.
Технологические правила сюда не входят. ReadDmtMesh — отдельная диагностическая CLI.

## Поддерживаемый профиль

Version 1000, little-endian, ASCII header до 256 байтов, file flags 0–3,
Header является описанием, а не magic signature:LF/CR и прочие bytes не запрещаются.
Исходные bytes сохраняются в HeaderBytes; Header использует ASCII display decoding.
block flags 0–1. File bit0 определяет float32/float64 координаты и нормали,
file bit1 объявляет миллиметры. Block bit0 означает нормали на вершинах.
Индексы треугольников uint16 при числе вершин блока ≤65535, иначе uint32.
Каждый блок заканчивается version1000; totals должны совпадать с блоками.
Неизвестные flags/version, недостающие/лишние bytes, неверные индексы,
NaN/Infinity и выход за лимиты отклоняются явно, а не пропускаются.

Лимиты одного файла:128 MiB,4096 блоков,1000000 вершин,2000000 треугольников.
Перед allocation проверяются totals и доступный payload. Требуется seekable stream
с initial position0. Caller-stream остаётся открытым. CLI удерживает input file
для чтения без разрешения записи и проверяет размер до вычисления SHA.

## Смысл результата

Сохраняются исходные координаты, нормали и индексы по блокам. Bounds имеют порядок
xmin,xmax,ymin,ymax,zmin,zmax и включают все записанные вершины, также неиспользованные.
При установленном file bit1 LengthUnits=mm_declared_by_file_flag; иначе unknown.
Header text 'in MM' не заменяет flag. Объявление единиц самим файлом ещё не является
квалификацией преобразования document_raw → mesh units при конкретном CAD-экспорте.
Coordinate frame остаётся file_coordinates_unqualified до живой квалификации.

CLI отдаёт SHA исходного файла и decodedOrderedSha256. Второй SHA кодирует
file flags/число блоков и для каждого блока flags/counts/координаты/нормали как
little-endian float64/индексы как little-endian int32. Это проверка полного
декодирования, не инвариант mesh после поворота или повторной триангуляции.
Порядок, индексы и состав треугольников могут меняться; их стабильность не заявляется.
Reader не отвергает совпавшие вершины, вырожденные треугольники или нулевые нормали
как синтаксически испорченный файл. Геометрическую пригодность должен проверять
отдельный verifier. Полная CAD-топология, watertightness и native convention не доказаны.

## Источники и испытания

Формат изучен по [официальному Autodesk DMTFileReader](https://github.com/Autodesk/PowerShapeAndPowerMillAPI/blob/f0761e50ae833e7647423b8da3ba24bfc23bd93d/Delcam.Geometry/GeometricEntities/DMTModel/DMTModelReaders/DMTFileReader.cs),
[writer](https://github.com/Autodesk/PowerShapeAndPowerMillAPI/blob/f0761e50ae833e7647423b8da3ba24bfc23bd93d/Delcam.Geometry/GeometricEntities/DMTModel/DMTModelWriters/DMTFileWriter.cs)
и [reader tests](https://github.com/Autodesk/PowerShapeAndPowerMillAPI/blob/f0761e50ae833e7647423b8da3ba24bfc23bd93d/Delcam.Geometry.Test/GeometricEntities/DMTModelReaderTest.cs).
Реализация написана отдельно; substantial исходники библиотеки не переносились.
Три бинарных эталона из этого commit сохранены с Autodesk MIT license, provenance
и SHA/size из LFS pointers в tests/PowerShapeMcp.Tests/fixtures/dmt. Они не являются
снимками текущего установленного PowerShape.

30 .NET cases включают три официальных файла, legacy LF/raw-byte/256-byte headers,
float/double и normals combinations,
порог 65535/65536, malformed metadata/index/nonfinite/trailer и каждое усечение
549-byte NormalDmt. Framework qualification независимо декодирует все vertices,
normals и indices Python struct, сверяет ordered SHA и bounds:11 controls/5 refusals.
Пять DMT из установленного PowerShape 2023 также прочитаны и полностью сверены
независимым декодером; исходные файлы не менялись. Это проверка совместимости
файлов, а не квалификация нового экспорта из текущей CAD-модели.

[Autodesk Product Help:Units and Tolerances](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-F0748270-4E08-4646-9B4C-DA03B074023E)
описывает единицы активной модели, но не доказывает связь с file flag или native raw
измерениями. Такая связь и настройки триангуляции требуют отдельного сценария.
