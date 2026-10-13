# К6 — SaveAs с новой identity модели, квалификация выполняется

Source native/legacy0.33.3-dev; SDK0.37.2-dev unchanged. Installed0.33.2-dev
не меняется. Live SaveAs59f5a399... остаётся outcome_unknown с retained claim/fence.

Regression воспроизвёл ложный отказ до исправления при same COM/window/new
model ID (после устранения test-only ошибки места вставки). Новый guard сохраняет
windowNumber/nativeWindowId/type, process/start/session, число окон, target/name,
посторонние документы, protected bytes и strict observed content/units.
Model ID после SaveAs может смениться; он обязан быть валидным, уникальным в
Model windows и совпадать с original afterModel observation. Старый source path
не должен оставаться в другом окне. Separate verifier дополнительно требует
совпадение original post-SaveAs model с fresh listing; subsequent model switch
не выдаёт savedResultHandle и не снимает fence.

Добавлены positive changed-ID/independent-verifier и negative later model-ID
change, snapshot/listing mismatch, invalid ID tests; existing wrong-window,
foreign-process, file tamper, wrong return и partial-journal cases сохранены.
Полный прогон выполняется;294 .NET tests уже passed, native/artifact/STDIO/
Inspector/fault matrix ещё проверяются. Ничего не объявлять готовым до конца.

Изменение не разрешает replay. Следующий живой шаг — verify original59f5a399...
с new fresh proofs; только доказанный terminal success позволит saved reopen.
Deployment readiness reader six fixtures включены в baseline CI.
