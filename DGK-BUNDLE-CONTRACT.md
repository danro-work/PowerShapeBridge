# DGK — контракт проверки комплекта файлов

DGK остаётся основным форматом обмена геометрией для общего MCP v1.
PSMODEL нужен для сохранённого результата и повторного открытия. DMT —
вспомогательное описание сетки; наличие DMT не обязательно для обычного DGK.

## Основание и явные имена

[Autodesk PowerShape 2023: таблица экспортных форматов](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-34971C9C-4FC8-40D2-A2F9-7AE6B5B247BA)
описывает DGK геометрию и сопутствующие файлы: треугольники символов могут
создавать `<stem>.dmt`, смешанные данные — `<stem>.dgk.dmt`, а Geometry + Features
— MAC/XML. Точное имя feature companion в этой странице не установлено:
вызывающий код обязан объявить его явно. Имя нельзя угадывать из DGK.

[Autodesk: экспорт модели](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-438C8FF1-50A9-4E0C-812B-0D628C04F610)
указывает default World для DGK и возможность экспорта в активную workplane
через wizard. Это описание вариантов, не getter действующих настроек.
Провайдер обязан независимо закрепить frame/settings/selection перед командой.
Активная workplane также может входить в DGK; пустое выделение может расширить
экспорт до данных уровней. Проверка файлов сама этого не устанавливает.

## Реализация

`src/DgkBundleVerifier.cs` — общий C#6/.NET компонент без COM и файловых записей.
Plan содержит detached immutable список Artifact: name, role, Required и
необязательный expected SHA256. Plan не предоставляет права на экспорт.

- Ровно один обязательный `<stem>.dgk` с ролью `geometry_dgk`.
- Не более двух DMT, только документированные имена выше, `triangles_dmt`.
- Не более одного явно названного MAC и XML: `features_mac`/`features_xml`.
- До пяти артефактов; ASCII basename без пути, ADS, device names, command text,
  неоднозначных точек и case-duplicate. Генерируемый stem до 80 символов.
- Required фиксируется до проверки. Отсутствующий обязательный файл отвергает
  комплект; отсутствующий optional перечисляется отдельно. Optional не означает
  разрешение скрыть потерю обязательного файла выбранного export-сценария.
- Лишний файл, другая капитализация, повтор имени или один Stream под двумя
  именами отвергается. Каждый файл 1..128 MiB, комплект до 256 MiB.
- Hash читает весь поток, проверяет размер и optional expected digest; DMT
  декодируется отдельно через DmtMeshReader. После всех companions повторно
  хешируется весь комплект. Потоки остаются открытыми у вызывающего кода;
  позиция потока изменяется. Ошибка не выдаёт частичный Report.
- DGK/MAC/XML остаются opaque bytes. MAC не исполняется, XML не парсится;
  external entities не загружаются. DMT units берутся только из его подтверждённого
  флага; текст заголовка не определяет единицы.

`src/DgkBundleFiles.cs` — внутренний нативный адаптер чтения отдельного каталога
ниже supplied root. Использует существующие OwnedDirectoryLease/OwnedCopies:
directory handle, FileShare.Read, final path, native file identity, single-link,
NoReparse, повторное чтение membership и удержание всех файлов до проверки.
Каталог содержит только объявленные артефакты, без подкаталогов. Это не проверка
ownership авторизации и не непрерывный контроль появления файлов.

## Значение результата

`dgk_bundle_bytes_observed_unqualified` подтверждает наблюдаемые байты и file
identity. В результате всегда false: cadGeometryVerified, exportSettingsQualified,
ownershipAuthorized, cadConnectionAttempted, companionExecutionAttempted.
DGK parser отсутствует; hash/непустой DGK не доказывают корректность формата или
топологию. DMT mesh не доказывает полную CAD эквивалентность. Для приёмки обмена
нужны отдельные qualified export provider, owned import и fresh geometric checks.

Публичного MCP tool/queue recipe для этого компонента сейчас нет. Нет execution
claim, fence release, удаления/cleanup, повторного экспорта или CAD-команд.
File observation не заменяет собственный target manifest и one-use executor.

## Проверки и следующий блок

38 .NET cases: primary/оба DMT имени/features/optional/immutable; отсутствующие,
лишние, malformed mesh, hash tamper, ambiguous names, stream aliases, изменение
раннего файла при чтении companion и byte limit. 12 actual Framework cases:
3 controls/9 refusals, включая held writer и real hardlink. Независимый Python
audit сверяет SHA, размер, native fileIndex с Windows stat, состав и false flags.
Проверки добавлены в verify-sdk.ps1; fixtures не используют настоящие DGK модели.

Далее: owned DGK target/manifest с qualified frame/settings, native per-step
export recipe/claim/fence, independent receipt verifier и owned import/roundtrip.
К8 scale/organization и остальные требования К9–К11 остаются в полном scope.
