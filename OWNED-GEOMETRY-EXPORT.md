# Контролируемый экспорт собственной геометрии — внутренний исполнитель

OwnedGeometryExport связывает disabled target из OwnedGeometryExportTargets с
selection/export callbacks и независимым DmtMeshReader. Это код квалификации:
production COM adapter, очередь и MCP-tool не подключены. Ни текущий runtime,
ни read-only сервер не получают возможности выделения/экспорта.

Вход содержит только targetId/targetSha256. Путь, имя, набор объектов, source
binding/set/inspection, ownership/files/expiry/directory pin поступают из target.
Нативный name выбирается из единственного type/name в исходном инвентаре;
управляющие символы и apostrophe отклоняются. SOLID/SURFACE должен уже находиться
на подтверждённо активном слое: implicit level activation запрещена.
Фиксированный экспортный путь предназначен для quoted recipe, имеет ASCII
whitelist и длину <260. Правила legacy unquoted OPEN не используются для EXPORT.

## Последовательность и доказательства

Global executor lock и held target/ownership/proofs остаются на всё исполнение.
Перед действием дважды проверяются процесс/документ/окна, world frame, исходная
геометрия/метаданные/единицы, прямой item mapping, saved bytes и observed settings.
Settings profile содержит built_in_local translator, положительный raw tolerance,
solid shading, edge limit/effective length, document_raw и provider qualification.
Значения копируются до публикации plan: mutable provider dictionary не может
изменить ожидаемый снимок. Getter и native смысл этого профиля ещё не квалифицированы.

Публикуются immutable execution plan, export-once claim и отдельный
workingcopies/<copyId>/geometry-export-fence.json. Fence не перезаписывает прежний
rotation mutation fence. Export preparation, SaveAs, translation и rotation
gates запрещают следующую мутацию/экспорт при наличии нового fence.
Read-only inspections/bindings остаются возможными.

Исходное выделение пустое. Каждый target добавляется один раз, с started/returned
записями. До и после callback проверяется точный накопленный prefix: типы/имена,
count и world frame. Только selection fields нормализуются для сравнения исходной
модели; остальные observed свойства менять нельзя. Затем один ExportDmt callback
в фиксированный отсутствующий output. Коллизия запрещает вызов.

После возвращения читается весь output в held single-link file, проверяются
native file identity, DMT grammar и повторный SHA. При известных собственном
выделении/контексте и корректном файле выполняется один заранее предусмотренный
ClearOwnSelection callback, затем два fresh reads подтверждают пустое выделение.
Команды, file SHA/identity и summary связаны с execution plan/journal.
Callback returned не означает квалифицированный native return code:
command_returned явно содержит nativeReturnCodeQualified=false.

Успех — только owned_geometry_export_executed_unverified, meshParsed=true,
geometryVerified=false, nativeProviderQualified=false, file_coordinates_unqualified.
Parsed mm flag является объявлением файла, а не CAD unit/convention proof.
Fence и claim сохраняются до отдельной независимой квалифицированной проверки.

## Неизвестный исход

Исключение, неверное выделение, смена settings/document/mapping, отсутствие или
невалидный файл прекращают сценарий. Нет повторного экспортирования, inverse,
очистки выделения или удаления fence по догадке. Частичные DMT/records сохраняются.
Ошибка во время запланированного ClearOwnSelection также оставляет unknown.
До первого callback состояние может быть rejected_before_change; это не разрешение
автоматического retry. Если claim уже создан, он и fence остаются даже при отказе.
При copyExportFenced=null исполнитель не установил собственный fence и не заявляет
подтверждённое отсутствие ранее существовавшего fence.

## Что требуется для живого использования

Отдельные native getters settings/active levels/selection и фиксированные selection,
export/clear recipes нужно квалифицировать на явно принадлежащей испытанию копии.
Обработчик не должен вызывать SDK AddToSelection с неявной активацией слоя.
Нужны independent receipt/file/context verifier, разрешение собственного fence,
SDK stores/каталог/transport faults, проверка units/frame/triangulation и сохранённого
результата. Полная CAD-топология и правильность transformations ещё не доказаны.
К8–К11 и общий exchange contract К9 не заменяются этим внутренним модулем.

Источники: [Autodesk PSEntity.AddToSelection](https://github.com/Autodesk/PowerShapeAndPowerMillAPI/blob/f0761e50ae833e7647423b8da3ba24bfc23bd93d/Delcam.ProductInterface.PowerShape/Entities/PSEntity.cs),
[PSSolid.WriteToDMTFile](https://github.com/Autodesk/PowerShapeAndPowerMillAPI/blob/f0761e50ae833e7647423b8da3ba24bfc23bd93d/Delcam.ProductInterface.PowerShape/Entities/PSSolid.cs),
[PowerShape 2023 Triangle/Mesh options](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-64B9FA33-C660-495A-878C-8CC67ECC5721).
SDK export helper также удаляет файл перед записью; этот побочный эффект не переносится.
