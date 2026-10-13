# Исправление пути каталога — Bridge0.33.2-dev

Живой К6 client на0.33.0-dev дошёл до new binding собственной Working copy,
но filesystem-only подготовка SaveAs отклонила неизменённый runtime каталог.
Program.Root=AppDomain.BaseDirectory заканчивается разделителем, Windows
GetFinalPathNameByHandle для вложенного каталога возвращает путь без него.
Прежнее строгое сравнение ошибочно трактовало это как перемещение/alias.

OwnedDirectoryLease теперь сначала проверяет исходный local path, затем
нормализует только завершающие directory separators с обеих сторон сравнения.
Проверки reparse, физической identity, held handle и запрет rename/delete
сохраняются. Не меняется file identity или допускаемые файловые пути.

Regression сначала воспроизвёл отказ до исправления. После исправления
520 native checks passed: два написания пути дают одну physical identity;
rename под удержанными handles отклоняется, traversal отклоняется;
Prepare и TargetLease проходят с runtime-shaped trailing root.
Полный локальный SDK/STDIO/Inspector/artifact/fault-matrix прогон прошёл:
292 SDK tests,520 native checks,171 Framework/Python/.NET artifact cases,
133 accepted/0 mismatches, external STDIO scenarios и Inspector,25 native
fault rows с independent audit.134 recorded source/binary hashes совпали.
Evidence: evidence/K6-directory-fix/sdk-qualified и qualified-hash-audit.json.
Exact source commit5fea6621e5adab56d773486bc49093f17b905ee4: четыре Windows CI
38356844677/38356892992 baseline и38356844798/38356892957 SDK passed.
SDK log отдельно подтвердил520/171/25/audit; draft PR11 attached.
Первоначальный full run имел1 fake-panel timeout при malformed receipt;
отдельный повтор этих случаев3/3 прошёл. Не скрывать первоначальный результат.

Source native/legacy0.33.2-dev, SDK0.37.2-dev unchanged. Build только staging.
Runtime0.33.0-dev, immutable K6 SDK0.36.3-dev/native0.33.0-dev не меняются.
Живой SaveAs/reopen НЕ выполнен; К6 не завершён. Новую установку согласовать
после законченной квалификации конкретного пакета.

Конкретный пакет теперь готов: work/K6-native-directory-live0.33.2-dev,
build-manifest.json; configuration SHA прежнего general-v1 профиля совпал.
Immutable K6 SDK0.36.3-dev проверен actual STDIO27/6 catalogs на этом staging;
hashes обоих пакетов совпали, live-package-check.json. Plan-only подготовлен
в work/K6-directory-deployment-plan.json для текущей единственной панели.
Apply не запускался. Для живого продолжения нужна установка именно этого
пакета с новой резервной копией и перезапуском панели. Старый остановленный
pilot не повторять; после установки нужен новый fresh owned intent/evidence.
