# К8 — пассивная подготовка поворота

К7 live acceptance ожидает отдельного согласия. Этот блок — безопасная подготовка
К8 без CAD; он не закрывает К7, К8 или весь MCP v1.

Source SDK0.39.0-dev. prepare_rotation_plan(entitySetHandle,parameters) включается
только при rotationPlansExperimental=true с entitySets/operationPlans dependencies.
В режиме чтения отсутствует. Регистрация: RotationPlanTools.Create(session)
в ToolCatalog.Create. Рабочий native runtime и готовые пакеты К7 не обновляются.

Параметры: angleDegrees — целое ненулевое по модулю360 значение с |angle|≤1e6;
origin/direction — три конечных числа, |component|≤1e6, direction ненулевой;
coordinateFrame=world, lengthUnits=document_raw. Ось нормализуется с предварительным
делением на максимальную компоненту: маленькие ненулевые векторы не обнуляются
при вычислении нормы. Дробные углы не округляются.

План закрепляет byte SHA исходного set/inspection, наблюдаемый контекст, точные
entity keys, исходные raw units и срок исходного set. Поддержаны только наблюдаемые
SOLID/SURFACE/COMPCURVE keys существующего set store. Нет implicit UI pivot,
selection, native ID/path/command arguments от клиента или native IPC.

rotationplans/<id>.json и prepared journal создаются неизменно. executionEnabled=false;
новый rotationPlanHandle принадлежит одному подключению и не является owned execution
packet. Expiry/foreign/tampered artifacts/context revocation запрещают использование.
Нет public rotation executor; прежний translation executor не принимает этот plan.

## Проверенные источники Autodesk

Installed API source:
C:/Program Files/Autodesk/PowerShape 2023/file/ole/data/IPshapeModelDoc.htm.
RotateItem(long composite_id,long angle_degrees,VARIANT axis): axis — массив шести
элементов point_x/y/z,direction_x/y/z; return1 success,0 failure,-1 bad arguments.
TranslateItem принимает три double. CopyItems принимает count и composite-ID array,
возвращает count и новые composite IDs. Прямого ScaleItem в этом интерфейсе нет.
Дробные углы требуют отдельно проверенного способа, а не silent integer conversion.

[Uniform scaling](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-F1C47DA1-CD1D-4DA1-AC0E-9E85B95BC7D5)
и [Scaling objects](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-0B7FF817-2F9A-43AF-826A-65CDD16F5283),
Autodesk Product Help en_US/PWRSS2023: pivot влияет на результат, а primitive
surfaces/solids при uniform scaling могут сменить представление. Документы содержат
различающиеся UI defaults pivot; MCP должен всегда закреплять явный центр.

## До выполнения поворота

Нужны owned copy/binding/checkpoint, target mapping иworld-frame guard в удерживаемой
COM-паре, one-use claim/fence, fresh independent verification, fault matrix иживой
согласованный сценарий. Углы/знаки/axis frame итип SAFEARRAY проверяются отдельно
на установленном native интерфейсе. Математическая правая система сама по себе
не доказывает конвенцию PowerShape.

Поворот восьми углов исходного AABB даёт envelope; для произвольного тела иугла
это не точный новый bounding box. Проверка должна использовать доступные независимые
геометрические данные, а не ошибочное равенство такого envelope фактическому bbox.
Метрики не доказывают полную топологию. Saved-result/reopen ивсе К10 классы обязательны.

Focused domain/SDK routes23/23 passed; дополнительно mixed set иplan limit tests.
Final full .NET393/393 passed без skip: evidence/K8-rotation-plans/sdk-qualified.
Независимый STDIO mixed SOLID/SURFACE/COMPCURVE plan, raw file SHA/axis,
fractional/read-only refusals passed: stdio-qualified. Прежние readonly/prepared/
execution Python/C# parity checks passed: parity-readonly/prepared/execution.
Первый внешний стенд не создал responses folder; диагностические артефакты
сохранены, исправлено только создание папок. Процесс завершился до нового запуска.
Native source/runtime/ready K7 native+SDK packages не изменялись.
