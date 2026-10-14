# Прямое пассивное чтение выделенных items

NativeSelectionReading использует только GetModelItems, GetSelectedItems и
GetItemData в одной удерживаемой application/document context. Методы getters
добавлены к DocumentBindings.ComContext, но новый reader не зарегистрирован в
публичном MCP/queue и не запущен на текущем CAD. nativeLayoutQualified=false.

GetSelectedItems возвращает массив composite IDs; документация явно определяет
null как failure. Поэтому null/DBNull/scalar не превращаются в empty selection.
Декодируются bounded одномерные VT_I4 arrays, включая SAFEARRAY с ненулевой
нижней границей. Пустой массив принимается; ItemData для нуля items не вызывается.
Дубликаты, foreign composite ID, invalid types/rank/count и inconsistent tuple
mapping отклоняются. Native selection order не считается постоянным:повторные
getters сравнивают sets, а original reported order сохраняется отдельно.

Все выбранные composites разрешаются в type/ID/name/level и сверяются с полным
native inventory и исходным observed snapshot. Direct selection и inventory
читаются повторно; до и после сравниваются context/properties, windows, retained
document и TTL. Никакой SelectionManager, Exec, выделения/активации/сохранения,
screenshot или export fallback в reader нет.

Нативные источники PowerShape 2023:

- IPshapeModelDoc.htm:1476–1479 — GetSelectedItems, composite array/null failure.
- IPshapeModelDoc.htm:1481–1484 — GetModelItems, composite array/null failure.
- IPshapeApp.htm:94–96 — SelectionManager:Not implemented.
- IPshapeModelDoc.htm:964–968 — PSMMExport:Deprecated, does nothing.
- IPshapeModelDoc.htm:193–195 — GeneralTolerance:general model tolerance in mm;
  это не подтверждённый getter export triangulation tolerance.

Файлы находятся в C:\Program Files\Autodesk\PowerShape 2023\file\ole\data.
Offline PowershapeNT.tlb inspection REGKIND_NONE подтверждает GetSelectedItems
dispatch141/VARIANT/no parameters, Evaluate/VARIANT/object variable и
Execute/HRESULT/command/out VT_I4 extended error. Числовое значение extended error
успеха из одной сигнатуры не следует и не угадывается. Selection/settings manager
интерфейсы в inspected typelib не обнаружены; это не доказательство отсутствия
всех возможных иных getters/preferences mechanisms.

21 fake-context сценарий:4 controls/17 refusals. Metadata и реальные installed
method signatures исследованы offline; getter layout/runtime availability и
native export settings/convention требуют отдельного live qualification.
Мутирующие испытания выполняются только по согласованному конкретному сценарию.
