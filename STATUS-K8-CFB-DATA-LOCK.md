# К8 — совместимость блокировки рабочих файлов с CFB

Актуально: Bridge0.34.8 установлен с отдельным разрешением пользователя,
новой резервной копией и одним дополнительным перезапуском панели.
`evidence/K8-live-level-move/deployment-cfb-fix.json`,
backup `work/general-deployment-backup-20261015-073928`.
Один разрешённый перенос уже выполнен и независимо подтверждён:
`evidence/K8-live-level-move/independent-audit.json` — passed.
Подробности и границы: `STATUS-K8-LIVE-LEVEL-MOVE.md`.
Целевые post-live executor и wire regressions также passed.
Широкий session30513 намеренно остановлен по новому циклу пользователя;
это не успешный полный прогон. Ниже сохранена история диагностики;
её pending/update/no-transfer записи больше не являются текущим состоянием.

Installed Bridge0.34.7 обновлён по разрешению пользователя, с backup
`work/general-deployment-backup-20261015-070839` и одним перезапуском панели.
Deployment: `evidence/K8-live-level-move/deployment.json`. Все4PR53CI passed.
Реальный SetItemsLevel не выполнялся; разрешённый один перенос остаётся впереди.

Две подготовки остановились до mutation submission:

- `transfer-20261015a`: ошибка операторского сценария — публичный ответ слоёв
  является проекцией, не raw receipt. Исправлен только driver: он проверяет
  original raw receipt по public request ID, совпадению полей и SHA. Исправление
  проверено офлайн на фактическом ответе. Не было once marker или команды.
- `transfer-20261015b`: native prepare `db6623ae041a4d0b8d0b97aa4af0564f`
  отказал `Owned working byte lock unavailable`. Ни once marker, ни execution,
  ни mutation fence не созданы. Никакой неизвестный CAD-вызов не повторялся.

## Подтверждённая причина

`.psmodel` — CFB, magic `D0CF11E0A1B11AE1`, размер753664bytes. Пассивный
Win32 probe показал: shared lock current bytes и0..1GiB succeeds; lock reserved
CFB sector `0x7FFFFF00..0x7FFFFFFF` и прежнего диапазона2^63−1 failsWin32=33.
SHA до/после тот же c9060ce2873f692d8118e26192149487742410108772b06261d686d03f814d4d.
Evidence: `work/K8-live-level-byte-lock-probe.json`, без COM или записи данных.

[Microsoft MS-CFB, section2.8](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-cfb/947df128-1f4a-4c02-b6ed-6d1d0c04c02f)
резервирует указанные offsets для блокировок и запрещает пользовательские данные
в этом секторе. [LockFileEx](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-lockfileex)
описывает shared/exclusive overlap и невозможность записи в shared range.
Вывод о конкретном конфликте основан одновременно на формате, диапазонах probe
и error33, а не только на общем сообщении «файл занят».

## Исправление в source0.34.8

`OwnedWorkingBytePin` использует общий `OwnedCopies.MaximumBytes=1GiB`:
защищены все допустимые байты документа и возможный рост в поддержанном размере;
CFB reserved lock sector лежит выше и не перекрывается. Пустые/oversized файлы
отвергаются; data-lock conflict по-прежнему отказывает, теперь с Win32 code.
Прежний размерный предел не изменён. Сохранены identity/length/SHA guards:
range lock не считается защитой от SetLength или memory-mapped writes.

Добавлены5synthetic cases к прежним9: совместимость с exclusive CFB lock,
запрет overwrite, запрет записи последнего допустимого байта, сохранение чужой
служебной блокировки и отказ пустого файла. Первый тест33511 использовал
буферизованный WriteByte, который сбросил тестовый байт после освобождения pin;
failure evidence сохранены. Fixture использует direct Write как прежние controls.
Corrected70591 passed44DGKtarget cases/503hashes и14byte-pin cases:
`evidence/K8-cfb-byte-lock-targets-corrected/native-audit.json`.

Пассивная проверка именно исправленного native constructor на текущей открытой
owncopy успешно получила/освободила pin, SHA/length unchanged, без COM/записи:
`work/K8-cfb-native-pin-live-passive.json` (assemblySHA03c5de2448304cbb...).
Это проверка файлового guard, не исполнение переноса или его квалификация.

По прямому указанию пользователя цикл изменён: малая операция → ранний ownCAD
test → независимая проверка → нужная регрессия. AGENTS.md обновлён. Широкий
session30513 намеренно остановлен (terminal−1), partial evidence сохранены;
markerNOTPASS/stopped_for_early_live_validation_after_user_workflow_change.
Не объявлять этот прогон завершённой регрессией и не возобновлять перед live.

Пакет native0.34.8 проверен18hashes/23сохранённые config fields,14pin44targets;
actual constructor именно PACKAGE успешно pin/release текущей owncopy без
COM/записи с неизменнымSHA. work/K8-cfb-live-package-audit.json и
work/K8-cfb-package-pin-live-passive.json. PlanOnly готов:
work/K8-cfb-live-deployment-plan.json. Используется frozenqualified SDK0.40.0.
Запрошено согласие только на дополнительный backup/update34.7->34.8/panelrestart;
ответ pending. Apply пока не выполнен. Уже разрешённый один перенос
General->Surfaces повторно согласовывать не нужно, если его условия сохранены.
После live result — необходимые regressions и narrow featurePR/CI.

Adoption-only code не смешан с pin fix: local commitf6ac392, отдельная ветка
`codex/level-move-reconnect-verification`; публикация/полная проверка впереди.
Общие К8–К11, saved/reopen и general recovery coverage не закрыты.
