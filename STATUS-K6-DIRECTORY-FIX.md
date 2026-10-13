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
Полный SDK/STDIO/Inspector/artifact/fault-matrix прогон выполняется отдельно.
Первоначальный full run имел1 fake-panel timeout при malformed receipt;
отдельный повтор этих случаев3/3 прошёл. Не скрывать первоначальный результат.

Source native/legacy0.33.2-dev, SDK0.37.2-dev unchanged. Build только staging.
Runtime0.33.0-dev, immutable K6 SDK0.36.3-dev/native0.33.0-dev не меняются.
Живой SaveAs/reopen НЕ выполнен; К6 не завершён. Новую установку согласовать
после законченной квалификации конкретного пакета.
