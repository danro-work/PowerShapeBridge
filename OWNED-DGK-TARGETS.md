# Подготовка собственного DGK-комплекта

Внутренний filesystem-only модуль `OwnedDgkTargets` подготавливает отключённый
план экспорта и отдельный пустой каталог артефактов. Нет COM, выделения,
активации, экспорта, сохранения, claim исполнения, изменения журнала или снятия
fence. Нет публичного MCP инструмента/queue recipe. Подготовка не разрешает CAD.

## Вход и состав

Точные bindingId/bindingSha256/entitySetId/entitySetSha256 и объект parameters:
coordinateFrame=`world`, exportVariant=`geometry` или `geometry_features`,
triangleCompanions, featureCompanions, settingsQualification=`planned_unqualified`.
Это запрос будущего сценария, не чтение действующих настроек Autodesk.

Primary DGK обязателен. DMT companions объявляются через документированные
suffix `.dmt`/`.dgk.dmt` и строгий bool required. Feature companions имеют явные
basename, роль features_mac/features_xml и required. Для geometry они запрещены;
для geometry_features требуется хотя бы один обязательный feature файл.
Имена feature files не угадываются. Общие ограничения DGK-BUNDLE-CONTRACT.md
применяются до резервирования. Expected SHA будущих файлов пуст до экспорта.

Initial guarded types — SOLID40, SURFACE21, COMPCURVE59; до128 объектов из явного
непустого набора. Это область подготовленного контракта, не live приёмка типов.
Другие DGK types и local workplane квалифицируются отдельно; полный scope v1
не сокращён. Существующий DMT контракт по умолчанию остаётся SOLID/SURFACE.

## Файлы и защита

Layout: `dgkexports/<targetId>/target.json` и отдельный
`dgkexports/<targetId>/artifacts/Geometry_<targetId>.dgk` с объявленными companions.
Reservation находится рядом с target directory и связывает binding и canonical
parameters SHA. Target SHA фиксирует все параметры, manifest и dependencies.

Binding/set/source receipts, TTL=min, наблюдаемые properties, world/empty selection,
ownership, checkpoint ReadOnly/identity/SHA и текущие saved working bytes связаны
проверками. Рабочие байты могут законно отличаться от initial checkpoint до
подготовки; новый captured SHA повторно проверяется через удерживаемые handles.
Активная собственная working copy допускает уже открытый writer PowerShape:
FileShare.ReadWrite (без Delete) плюс shared byte-range lock запрещает обычную
запись данных. Изменение EOF и mapped views не блокируются; identity/length/SHA
guards отклоняют наблюдаемое изменение. Непрерывное исключение всех способов
записи не заявляется. Источник/checkpoint сохраняют прежние ReadOnly/sharing
guards; SDK только повторно читает working bytes, не выдавая write authority.
Directory identities, final paths, reparse/hard-link guards
и удержание proof artifacts предотвращают подмену в пределах проверки.

После подготовки TargetLease повторно проверяет точную схему, параметры/reservation,
весь manifest и фиксированные пути, directory identities, исходные receipts,
expiry/prepared timestamp, held working bytes и пустоту artifacts. Любой файл,
подкаталог или export-once claim приводит к отказу до действия.

Unresolved export fence запрещён. Mutation fence разрешается наблюдать только
по прежнему строгому контракту полной returned rotation chain с новым source
receipt после aggregate return. Fence не разрешается и не удаляется. Иные
unknown/partial/foreign fences блокируют подготовку.

## Основание и границы

[Autodesk: экспортные форматы](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-34971C9C-4FC8-40D2-A2F9-7AE6B5B247BA)
описывает DGK geometry, triangle/feature companions и поведение выделения/уровней.
[Autodesk: экспорт модели](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-438C8FF1-50A9-4E0C-812B-0D628C04F610)
описывает World default DGK и вариант active workplane. Defaults не заменяют
getter действующих настроек. Активная workplane может попасть в DGK: initial
preparation требует world context, без скрытого выбора уровней или всей модели.

44 синтетических Framework сценария и 9 low-level byte-pin cases, независимый
аудит файлов/dependencies; held writer разрешает preparation/lease, но изменённые
working bytes (включая truncate/mapped writes) отклоняют повторную проверку;
реального экспорта/импорта и топологической проверки здесь нет. executor должен
получить fresh retained CAD context, qualified settings/provider, guarded exact
selection, once/fence/per-step evidence и independent bundle/geometry verifier.
MAC/XML не исполняются. Планы не заменяют дальнейшую квалификацию К8–К11.
