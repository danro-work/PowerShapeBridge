# К6 — живой сценарий остановлен до SaveAs

13 октября 2026 после установки0.33.0-dev запущен отдельный SDK0.36.3-dev client.
Исходный Test_Block_P2 прочитан, свежим выбором активирована только собственная
копия b3d16256be1a4848bdbf15f6160a0567. Наблюдения совпали с К5 unsaved X+1;
known-success OPEN независимо перепроверен, создана свежая binding.

prepare_owned_save_as request22ee98d71c47495f9c1fdea31ddc0aa4 отклонён:
`Owned directory was moved or aliased.` Receipt явно сообщает cadSaveAttempted,
contextChangeAttempted, geometryMutationAttempted, powerShapeConnectionAttempted
false. Нет savedresults directory entries. Ни execute SaveAs, ни saved reopen
не вызваны; сценарий не повторялся. Результат определён как подготовка отклонена,
а не unknown исполненного SaveAs. Ошибка и transcript сохранены.

При остановке собственная Working_b3d... остаётся активной. Автоматическое
восстановление активной модели после ошибки не выполнялось. 71 защищённый
файл совпал до/после; исходная модель не сохранялась и геометрия не изменялась.

Код использует Program.Root=AppDomain.CurrentDomain.BaseDirectory с завершающим
разделителем. OwnedDirectoryLease сравнивает этот путь с Windows final path,
который для вложенного каталога возвращается без завершающего разделителя.
Обычный root всех прежних synthetic fixtures не покрывал runtime spelling.
Исправление квалифицируется в отдельной ветке и staging; installed0.33.0-dev
не заменять новым пакетом без отдельной согласованной установки.

Доказательства: evidence/K6-save-as/live/qualification/qualification.json,
10-prepare_owned_save_as.json, transcript.json и protected-after-stopped-pilot.json.
К6 не закрыт; цель К11 сохраняется.
