# К8: одна рабочая система координат создана и независимо проверена

15 октября 2026. По прямому разрешению пользователя Bridge обновлён
0.34.8-dev → 0.35.0-dev с новой backup и одним перезапуском панели.
Резервная копия: `work/general-deployment-backup-20261015-122554`.
Deployment: `evidence/K8-owned-workplane-deployment/deployment.json`,
state=deployed_panel_verified. Обычный MCP-клиент не перенастраивался;
для испытания использовался изолированный SDK0.41.0-dev.

## Реальный результат

В собственной `Working_cde95c3e54a64c2f9264443cacb8837f` один раз создана
workplane с WORLD origin `[2,3,4]`, Z `[0,1,0]`, Y `[0,0,1]`.
PowerShape дал имя «1» и слой General(0). После автоматической активации
один раз выполнен объявленный возврат в WORLD. Активное тело «1» осталось
на Surfaces(1); выделение пустое, восемь окон, та же копия активна.
Сохранение/OPEN/закрытия/перенос тела не выполнялись.

| Доказательство | Результат |
|---|---|
| Execution ID |`ff2a5186c0244c56a8da60dd1d40b010` |
| Raw receipt SHA |`8e47eb2b8fa5bdf3fba20db8ff3091004e60f384cd0961562f9c36827cd2ae10` |
| Native вызовы |CreateWorkplane1, ActiveWorkplane=0 одинраз; commandCalls2 |
| Созданная система, исторические ID |native2 / composite1080000002 / type54 |
| Журнал |6events/succeeded_verified, одна неизменяемая creation-return квитанция |
| Независимый getter |GetWorkplanes3 вернул System.Object[] `[1080000002]`; GetWorkplaneInfo совпал дважды |
| Измерения тела |World bounds[-10,10,-10,10,0,20], свойства/raw units прежние |
| File-only independent audit |55hash checks passed; source/checkpoint/saved bytes не изменены |

Evidence: `evidence/K8-owned-workplane-approved20261015a/` содержит исходный
driver result, отдельное COM чтение и independent-audit.json. Session68566
terminal0. НЕ запускать driver повторно: разрешение использовано, submission.once
и native execution-once сохранены; только matching fence снят verifier.
Сохранённый working файл ещё не содержит новую несохранённую систему.

## Переподключение и регрессия после живого результата

Новое SDK-подключение приняло original receipt только для verification,
прочитало свежие candidate/listing и подтвердило ту же систему через штатный
native verifier. Create/World не повторялись. Оригинальные6journal files и
44protected hashes неизменны. Verification request
`6bb7f9c9b73848ffb179f581959e2ed6`.
Отдельный read-only SDK не публикует4 workplane tools; recorded recovery
inspection читает историю без CAD/новых разрешений.
Evidence: `evidence/K8-owned-workplane-live-reconnect/result.json` passed.

После реального результата141целевых .NET tests/0skips passed:
`evidence/K8-owned-workplane-after-live/workplane-after-live.trx`.
Обновлённый runner `verify-owned-workplanes.ps1` также passed: native12cases,
93hash audit; SDK/native control11requests с reconnect,88hash audit;
два SDK transport faults (lost reply/cancel after simulated effect),130hash audit.
Все synthetic effects остаются изолированными; original replies unpublished,
unknown не разрешён, claims/fences сохранены, replay=0.
`evidence/K8-owned-workplane-postlive/`. Runner добавлен в общий verify-sdk/CI.

Это принятие одной новой workplane в одном WORLD-сценарии. Сохранение/переоткрытие,
активация существующей системы, несколько систем и остальные типы/модели К8–К11
пока не приняты. Следующий практический шаг — сохранение и проверка workplane
после открытия. До него нужно обеспечить получение свежей привязки изменённой
owned-копии после reconnect: прежний OPEN verifier сравнивает с исходным saved
состоянием и не должен выдавать его за текущее изменённое содержимое.
