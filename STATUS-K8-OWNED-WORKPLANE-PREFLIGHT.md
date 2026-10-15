# К8: пакет workplane готов к первой реальной проверке

15 октября 2026. Изменения в CAD НЕ выполнялись. Установлен прежний Bridge
0.34.8-dev. Native 0.35.0-dev и SDK 0.41.0-dev подготовлены отдельно.
Разрешённые ранее SaveAs/OPEN завершены; их разрешение использовано.

## Реальные проверки только чтением

Свежее MCP-чтение и отдельный COM-процесс подтверждают текущую собственную
`Working_cde95c3e54a64c2f9264443cacb8837f`: одно тело «1» на Surfaces(1),
пустое выделение, WORLD, восемь окон, рабочих систем ещё нет.
Дважды прочитаны WORLD bounds `[-10,10,-10,10,0,20]`.
Перед будущей мутацией процесс/документ/контекст проверяются заново.

- `evidence/K8-owned-workplane-live-preflight-fixed/result.json`: passed, mutationSubmissionAttempted=false.
- `evidence/K8-owned-workplane-final/independent-native-preflight.json`: passive_owned_workplane_observed.

Первый операторский preflight остановился до мутации из-за сравнения native
entity с публичным candidate, который дополнен двумя connection-scoped полями:
candidateEntityHandle и entitySetSupported. Остальные поля полностью совпали.
Сравнение исправлено удалением только этих двух добавок; геометрические проверки
не ослаблены. Исходный отчёт `K8-owned-workplane-live-preflight/result.json` сохранён.
Повторялось только чтение, не создание/сохранение/открытие.

## Целевые проверки пакета

| Проверка | Результат |
|---|---|
| .NET workplane/recovery |66 passed, 0 failed, 0 skipped;24 workplane +42 существующих recovery |
| Native control/faults |12 passed,93 независимых hash checks; no CAD |
| Допуск |12 конфигураций и15 запросов в каждом enabled/disabled профиле; no CAD |
| Реальный SDK STDIO→native file/guard, CAD имитирован |11 native requests, Create1/World1; reconnect verify без replay |
| Независимый wire audit |88 hash checks; no CAD |
| Read-only граница |4 новых mutational tools не публикуются; recovery inspection только читает |

Доказательства: `evidence/K8-owned-workplane-recovery-qualified/workplane-recovery.trx`,
`evidence/K8-owned-workplane-final/{native-audit,admission,admission-disabled}.json`,
`evidence/K8-owned-workplane-final/wire/{result,wire-audit}.json`.
Ранние неуспешные synthetic запуски сохранены отдельно; к CAD они не подключались.
Полную регрессию/CI новой функции до раннего реального опыта не запускали.

## Пакет и ограничения

`work/WP35-live-package/native` содержит ровно18 файлов из проверенной сборки,
без fake markers, synthetic копий или reports. Конфиг отличается от установленного
только добавлением ownedWorkplanesExperimental=true.
Native worker SHA `25fa3be49a91cdf7742a9c5b862ba0365ff5240c8bc7eb1ef4027218a526bf4e`.
SDK DLL SHA `eb1cda6d17dceafa3ab1bf52368b16a892f433306493e57cd5d00a2b9d836cf7`.
`qualification.json` содержит исходники/SHA/evidence; deployment-plan подготовлен
без закрытия панели, установки, старта worker или контакта с CAD.

Текущий snapshot PR56/d326b61: все4 GitHub checks SUCCESS;
`work/K8-pr56-checks-d326b61-completed.json`. Это предыдущий завершённый блок,
не CI текущей workplane ветки. Новый draft PR пока не создавался.

Нужно одно конкретное согласие на обновление с backup/одним перезапуском и
сценарий `K8-LIVE-OWNED-WORKPLANE-SCENARIO.md`. Согласно AGENTS.md автономная
разработка не разрешает CAD-изменения, а предыдущее согласие охватывало SaveAs/OPEN.
Создание/возврат WORLD пока не подтверждены на реальном API; сохранение workplane,
существующие workplane activation, разные типы/наборы и К8–К11 не приняты.
