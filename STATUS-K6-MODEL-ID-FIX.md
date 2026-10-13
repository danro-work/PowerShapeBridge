# К6 — SaveAs с новой identity модели, квалификация пройдена

Source native/legacy0.33.3-dev; SDK0.37.2-dev unchanged. Installed0.33.2-dev
не меняется. Live SaveAs59f5a399... остаётся outcome_unknown с retained claim/fence.

Regression воспроизвёл ложный отказ до исправления при same COM/window/new
model ID (после устранения test-only ошибки места вставки). Новый guard сохраняет
windowNumber/nativeWindowId/type, process/start/session, число окон, target/name,
посторонние документы, protected bytes и strict observed content/units.
Model ID после SaveAs может смениться; ID собственного результата обязан быть
валидным, уникальным и совпадать с original afterModel observation. Посторонние
документы могут иметь несколько неизменённых views. Старый source path
не должен оставаться в другом окне. Separate verifier дополнительно требует
совпадение original post-SaveAs model с fresh listing; subsequent model switch
не выдаёт savedResultHandle и не снимает fence.

Добавлены positive changed-ID/independent-verifier и negative later model-ID
change, snapshot/listing mismatch, invalid ID tests; existing wrong-window,
foreign-process, file tamper, wrong return и partial-journal cases сохранены.
Окончательный полный прогон прошёл:294 .NET tests/531 native checks/175 actual
Framework-Python-.NET artifact cases(137 accepted,0 mismatch), external STDIO/
Inspector,25 native fault cells+independent audit.134 recorded hashes matched,
evidence/K6-model-id-fix/sdk-qualified и qualified-hash-audit.json.
Commit341951adb74b30df72f621b1756522cf423c10c7: все четыре exact-head Windows
CI38363243640/38363252126 baseline и38363243752/38363251861 SDK passed.
Actual SDK log531/175/25 проверен. Draft PR13 прикреплён.

Предыдущий sdk-full529/175 тоже прошёл, но до дополнительного multi-view
regression; он superseded окончательным sdk-qualified531. Первый red-stage
не собрался из-за test placement; red2 после исправления placement
воспроизвёл исходную ошибку old guard (`synthetic test failed at0`).

Конкретный окончательный пакет: work/K6-native-model-id-live2,0.33.3-dev.
Первая stage work/K6-native-model-id-live — superseded diagnostic, не применять.
Build manifest и все package hashes checked; unchanged K6 SDK0.36.3 actual
STDIO27/6 catalogs checked на live2, live-package-check.json.
Plan-only work/K6-model-id-deployment-plan.json подготовлен; Apply НЕ вызван.
Installed0.33.2-dev и unknown59f5a399... claim/fence сохраняются.

Изменение не разрешает replay. Следующий живой шаг — verify original59f5a399...
с new fresh proofs; только доказанный terminal success позволит saved reopen.
Deployment readiness reader six fixtures включены в baseline CI.
