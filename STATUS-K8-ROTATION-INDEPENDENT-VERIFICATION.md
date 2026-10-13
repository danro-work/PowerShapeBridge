# К8 — независимая проверка наблюдаемых измерений

OwnedRotationVerification получает отдельный контекст с методами чтения.
В интерфейсе нет Rotate/Exec/save/activation. Проверяются pinned execution receipt,
plan schema2, authoritative journal и полный набор per-target return proofs.
Частичный пакет или неподтверждённый возврат API не проходят проверку.

Свежие inspection/listing должны быть записаны после общего command_returned.
Проверяются исходный owner/working SHA, fence, one-use claim, процесс/документ/
окно/выделение/единицы и неизменность остальных наблюдаемых объектов.
Before/after records читаются по закреплённым SHA с проверкой provenance и порядка.
Каждый target измеряется дважды через новый контекст; сравниваются anchors,
descriptor, позиции, направления, invariants и paired bounds. Сверяются также
записанные после команды и независимо полученные данные.

Совпадение публикуется только как observed_rotation_samples_match_unqualified.
Diagnostic record сохраняет свежие samples и ссылки на before/after/inspection/
listing/execution. Native provider/convention ещё не квалифицированы, поэтому
проверяющий не пишет succeeded_verified и не снимает fence. Ошибка или частичное
исполнение сохраняют original journal/claim/fence и не вызывают повтор команды.

## Доказательства

14 сценариев и 465 хешей прошли независимый аудит:
evidence/K8-rotation-verification/qualified,
stage work/K8-rotation-verifier-qualified, session28936 завершён успешно.

Положительные сценарии: один объект, смешанный SOLID/SURFACE/COMPCURVE набор,
отрицательный угол. Тестовый executor использует отдельные аналитические формулы
четверти оборота; verifier — общий Rodrigues predicate. Python auditor независимо
пересчитывает точки и направления и проверяет сохранённые fresh samples.

Отказы: неправильный угол/свежие samples/mapping, поздняя смена контекста,
неверные receipt/before/summary SHA, изменённый fence, partial second command,
старый inspection и неправильное окно. Verification context выполнил 0 команд;
все исходные файлы, журналы и fences сохранили байты. CLI без собственного
fixture marker отказывается работать. Runner включён в verify-sdk.ps1.

PR32/8c62a822fc5e7524548eb63e73a8bfeba24a4890 прошёл четыре Windows CI:
38475634606/38475674953 baseline, 38475634617/38475674902 SDK.
Новый source ещё должен пройти полный CI.

## Что осталось

Это проверка собственных аналитических fixtures, а не живых Autodesk samples.
Production COM/queue/MCP rotation и verification tools не включены. Для снятия
fence требуется квалифицированный источник измерений для всех target типов,
подтверждённые native sign/frame/SAFEARRAY и проверенные допуски в raw-единицах.
Текущий математический допуск 1e-6 absolute/1e-9 relative является экспериментальным.
Диагностика samples не доказывает полную топологию или правильность всех CAD-классов.

Провайдер граней тела и живой curve magnitude layout остаются незавершёнными.
Затем нужны SDK/transport/faults, saved-result/reopen и остальные операции плана.
K7 live approval pending; runtime/READY-пакеты и пользовательский CAD не изменены.
К8 и К9–К11 не завершены.
