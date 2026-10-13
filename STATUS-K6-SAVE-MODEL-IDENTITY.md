# К6 SaveAs — новый model ID, исход остаётся unknown

13 октября 2026 installed0.33.2-dev после отдельного разрешения пользователя.
Новый owned target f238febe4cc84525b2820af5ceeaca57 подготовлен успешно.
SaveAs59f5a399c14a42b5b6e2c80688d43f09 вызван ровно один раз; native journal
содержит command_started, command_returned/saveCommandReturned=true,
outcome_unknown с причиной `SaveAs retained window identity changed.`

Native window1/windowNumber5/typeModel retained. Model ID сменился
280011363 →280011395, имя/path теперь Result_f238febe... в собственном target.
Контракт ошибочно требовал совпадение modelId до/после SaveAs вместе с window
identity. Это предположение не подтверждено Autodesk API и противоречит живому
наблюдению; modelId является наблюдением текущего документа, не вечной identity.
Удержанная COM identity проверялась двумя observations/ConfirmDocument и прошла.

Read-only forensic audit подтвердил SHA/размер результата737280 bytes,
SHA362a285f3fae9e5a63c6671b67a5f9d4ee70c053ef55193eab9354f0ab1cfc29;
наблюдаемые entity inventory/properties/units/context/workplanes совпали до/после.
Пять окон и четыре посторонние модели сохранились. Все71 protected files
совпали. Активен новый Result_f238febe...; автоматического возврата/закрытия нет.

Это НЕ terminal success и не topology certification. Claim/fence retained;
ни повтор SaveAs, ни OPEN, ни удаление fence/claim, ни inverse не выполнялись.
Следующий live шаг после квалификации исправления: separate verifier original
SaveAs с fresh receipts; только его успех позволит prepared reopen.

Доказательства: evidence/K6-directory-fix/live-qualification/,
save-unknown-fresh-context.json, save-unknown-audit.json,
protected-after-save-unknown.json и runtime operations/59f5a399.../*.json.
К6 не завершён, цель К11 сохраняется.
