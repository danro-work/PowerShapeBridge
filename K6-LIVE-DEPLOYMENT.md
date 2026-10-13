# К6 — готовый пакет установки и живого испытания

Обновление 13 октября 2026: явное разрешение получено, установка0.33.0-dev
применена и независимо проверена. STATUS-K6-DEPLOYMENT-033.md. Ниже сохранена
история подготовки до разрешения. Live qualification ещё не выполнялась.

13 октября 2026. Source SDK0.36.3-dev/native0.33.0-dev проверены в clean checkout
и четырёх Windows CI; STATUS-K6-SAVE-AS.md. Пакет подготовлен, runtime ещё0.32.

Native staging: `work/K6-native-live`, build-manifest.json фиксирует все файлы.
SDK package: `work/K6-sdk-runtime`, package-manifest.json: actual STDIO/status,
full/read-only catalogs27/6, SHA всех опубликованных файлов. Default MCP config
в Codex не меняется. Отдельный qualification client использует этот пакет.

Plan-only `deploy-general.ps1` создал `work/K6-live-deployment-plan.json`:
установленный runtime releases/P2.6, текущая панель24012 с creation time,
staging/build SHA и до/после SHA executable/config files. `-Apply` не запускался.
Применение создаёт резерв runtime, закрывает только проверенную панель Bridge,
обновляет файлы, запускает одну панель и проверяет версию/config SHA. При ошибке
нет автоматического rollback/restart; сначала deployment journal и process/queue.

Автоматическая проверка сначала отклонила invocation скрипта как риск закрытия
панели/изменения runtime без отдельного согласия. Команда не выполнялась.
После проверки `if(!$Apply)`/`exit0` plan-only был разрешён и выполнен. Явное
разрешение пользователя на применение установки запрошено и ещё ожидается.
Без него `-Apply` и зависимый живой сценарий не выполнять.

Защищённые CAD files, ownership/claims/checkpoints и старый unknown OPEN journal:
`evidence/K6-save-as/live/protected-before-deployment-stable.json`, 71 файл,
двойной SHA/size check. Для хеширования открытых CAD files используется held
ReadWrite sharing без delete; это point-in-time hashes, не запрет писателя.
Общий strict copy source guard не менялся. Первая partial попытка Get-FileHash
не читала пять открытых CAD файлов; сохранена как superseded diagnostic.

Свежие passive reads подтвердили process17304/start2026-10-08T08:38:27Z,
пять окон, Test_Block_P2 active, собственная Working_b3d... открыта. Перед
установкой/действиями повторить процесс, очередь, файлы, model/window/context.
Прошлые document handles не переносить.

Подготовлен `work/k6_live_qualification.py` (syntax checked, НЕ выполнен).
Он разрешает только own copy b3d16256be1a4848bdbf15f6160a0567 и:

1. Проверяет approved deployed version и live catalog27; читает baseline/context.
2. Выбирает собственную Working copy по свежему listing; сверяет её наблюдения
   с K5 verified unsaved world/raw X+1 до SaveAs. Новых transforms не делает.
3. Независимо re-verifies старый известный successful OPEN для новой scoped copy
   lease; старый unknown6ed... не исполняет/не меняет. Создаёт свежую binding.
4. Готовит новый target, один SaveAs, отдельный fresh saved-result verifier.
5. Готовит новую seed/Working/checkpoint копию, один guarded OPEN, separate
   content/units/files/windows verifier и новую binding, validate этой binding.
6. Только после известного успеха выбирает исходный Test_Block_P2 свежим handle.

Каждая команда/receipt/raw SHA записывается до/после вызова; при любом unknown
или ошибке stop без replay/save/close/inverse/автоматического восстановления
active model. Живые CAD self-tests не стартуют при обычной установке/CI.
После сценария требуется отдельный независимый raw artifact/file audit,
сравнение protected71 files и прежних окон. Screenshot/мышь не используются.
К6 не завершён; цель разработки К7–К11 сохраняется.
