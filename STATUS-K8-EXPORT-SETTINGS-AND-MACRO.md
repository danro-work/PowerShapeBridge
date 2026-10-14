# К8 — настройки экспорта и пользовательский макрос

Макрос Export_Selected_as_DMT.mac прочитан как Windows-1251, без запуска.
Источник: D:/Users/rozhdestvenskii.da/Desktop/Export_Selected_as_DMT.mac.
SHA256:4718699fade24e943e12e077dd09690346c13f8f5e0121095fc1efa0ff8fa5fc.
Комментарии и записанные команды рассматриваются как данные, не разрешение
выполнить выбор объектов, экспорт или ответить на диалог.

Наблюдаемая последовательность: FILE EXPORT WIZARD → NEXT → абсолютный путь
Test_Block_P2.dmt → EXPORTTYPE VISIBLE → EXPORTTYPE SELECTED → WIZEXPORT → Yes.
Начало содержит culast/b1/bu с типами/именами и координатами записанного выбора.
Обе смены EXPORTTYPE сохраняются как наблюдение; реальный результат не проверялся.
Назначение завершающего Yes из одного текста не установлено: нельзя считать его
подтверждением успешного экспорта или автоматически повторять ответ.

В макросе нет явного чтения/установки triangulation tolerance, solid shading,
limit triangle edge length, maximum triangle length или выбранного Target System.
Он даёт пример команды мастера, но не getter действующих настроек DMT.
Записанные имена и координаты не являются актуальными ссылками на CAD-объекты.
Мастер не подключён к production executor; текущий internal executor остаётся
без квалифицированного native settings provider.

Официальная документация PowerShape 2023 Triangle/Mesh options подтверждает:
Target System влияет на показанные настройки, DMT options применимы к built-in
local translator. Triangulation tolerance, solid shading и ограничение длины
ребра требуют отдельной проверки. Источник:
https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-64B9FA33-C660-495A-878C-8CC67ECC5721

tools/InspectExportProfileCatalog.py прочитал установленный XML-каталог:
C:/Program Files/Autodesk/PowerShape 2023/file/export_options/exportwizardoptions.xml.
27 профилей; SHA256
7fd3f0e56223373f117e8f96b2458fd5e1970298e4aeb4cdf1ed8b86fde894a1.
Evidence:evidence/K8-export-settings-research/static-catalog.json.
Статический каталог не доказывает выбранный профиль, live settings или единицы.
GeneralTolerance и shading tolerance не заменяют export tolerance.

Исправлена совместимость DmtMeshReader с описательными legacy COPYCAD headers:
LF/CR/raw bytes допускаются, HeaderBytes сохраняет исходные bytes, ASCII Header
служит отображению. Version/flags/counts/index/finite/trailer/limits проверки
сохранены. Текст in MM не подменяет file unit flag.

Проверки завершены без CAD:
- 30/30 .NET reader tests, без пропусков: dotnet-header/legacy-header-tests.trx.
- Framework reader:11 controls/5 refusals, полная независимая сверка координат,
  нормалей, индексов и ordered SHA:reader-header-final/framework-audit.json.
- Пять установленных DMT прочитаны; независимый Python decoder сверил все данные
  и неизменность исходных SHA:installed-dmt-independent-audit.json.
- Регрессия independent export verifier:21 cases/734 hashes, без освобождения
  fence:export-verifier-regression/native-audit.json.
Все пути evidence выше относительно evidence/K8-export-settings-research.
Полная локальная .NET регрессия этим блоком не заявляется.

Дальше: документированный getter/настройка DMT или дополнительный записанный
пример изменения этих параметров; затем отдельная квалификация native provider
и экспорта на согласованной собственной копии. Полный К8–К11 не завершён.
Установленный runtime и готовые К7-пакеты не изменены, live К7 не согласован.
Локальные изменения не опубликованы: предыдущая automatic approval review
не завершилась из-за usage limit; ограничение не обходилось.
