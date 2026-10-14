# К8 — пассивное чтение native selection layout

14 октября 2026. Диагностический блок завершён; квалификация production
selection/export provider не принята.

## Проверено в PowerShape 2023

Attach-only diagnostic `tools/ReadNativeSelectionLayout.cs` загрузил проверенную
staging assembly без запуска её worker/Main. Использованы только пассивные
методы retained ComContext и NativeSelectionReading.Read. Не было Exec,
изменения выделения, активации, сохранения, экспорта, screenshot или мыши.

Документ: новая копия К7 `Working_7b0765448a714a97a8f78edf46754834`,
model ID 280011459; процесс 17304/start 2026-10-08T08:38:27.4633424Z.
Это исторический снимок; перед следующим действием требуется новое чтение.

- GetModelItems: rank-1 System.Object[], lower bound 0, один Int32 composite
  `800000005`. GetItemData: тип 40, ID 5, имя `1`, уровень 0; массивы типов/ID/
  уровней Int32 и имён String. Соответствует наблюдаемому одному SOLID.
- GetSelectedItems вернул **null** при независимо наблюдаемом пустом выделении.
  Установленная документация описывает null как failure. Это неоднозначность
  getter, а не доказательство успешного чтения пустого массива.
- Строгий reader корректно отказал: `native_selection_read_rejected`,
  `Null native getter result is failure, not empty selection.` Правило отказа
  и production-код не изменены; nativeLayoutQualified остаётся false.
- Context [0,0], inventory и все семь окон одинаковы до/после. Сохранённые
  байты working copy совпали до/после; все 11 защищённых файлов К7 проверены
  отдельным аудитом и не изменились. Хеширование активного файла было
  неэксклюзивным: это point-in-time наблюдение, не execution lease.

## Устранённые ограничения диагностики

Первая попытка attach-only probe остановилась на FileShare.Read pin: PowerShape
уже держит открытый файл с доступом записи. Исходный `result.json` сохранён.
Исправлено только диагностическое чтение saved bytes на FileShare.ReadWrite;
production ownership/lease guards не ослаблены. Повтор чтения использовал новую
preflight и отдельный `result-corrected.json`; CAD-команды не выполнялись.

Перед блоком installed adapter read завершился timeout: панель отсутствовала,
PowerShape работал. После проверки отсутствия pending requests/второй панели
запущена уже установленная панель 0.33.5-dev без обновления файлов. Новое чтение
подтвердило активную копию К7. PowerShape не перезапускался.

## Доказательства и следующий шаг

- `work/K8-native-selection-live/result.json` — сохранённая ошибка file pin.
- `work/K8-native-selection-live/result-corrected.json` — actual COM layout.
- `work/K8-native-selection-live/preflight-corrected.json` — отдельное чтение.
- `work/K8-native-selection-live/independent-audit.json` — passed, 11 protected
  hashes; selectionGetterSucceeded=false и nativeLayoutQualified=false.
- `tools/audit_native_selection_layout.py` — независимый аудит.

Далее: отдельный согласованный тест непустого выделения на собственной копии;
проверка getter failure/empty ambiguity без подмены null пустым массивом;
effective DGK settings/frame/variant/companions и production provider,
SDK execution/verification, owned DGK roundtrip и полная приёмка К8–К11.
К7 успешно выполнен, но это не завершение универсального MCP v1.
