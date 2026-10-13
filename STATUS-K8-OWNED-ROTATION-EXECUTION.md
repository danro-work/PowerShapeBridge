# К8 — внутренний исполнитель пакетного поворота

Добавлен OwnedRotationExecution.Execute: один неизменный plan, собственная
working copy и checkpoint, временные proof/file leases, общий executor lock,
однократный claim и сохранённая mutation fence. Набор может содержать SOLID,
SURFACE и COMPCURVE. Для каждого объекта сохраняются отдельные started/returned/
observed записи с хешами и параметрами; полная цепочка завершает общий журнал
только состоянием executed_unverified. Геометрия не объявляется подтверждённой.

## Проверки

21 сценарий и независимый audit486 хешей прошли:
evidence/K8-owned-rotation-execution/qualified;
stage work/K8-rotation-execution-qualified, session17685 завершён успешно.
Дополнительный случай: изменение composite mapping после команды не позволяет
прочитать bounds старого объекта как результат поворота.

Контроли: один объект, смешанный набор, отрицательный угол, наклонная ось,
успешный return без изменения. Последний сценарий намеренно подтверждает:
return1 не означает подтверждённую геометрию; fence остаётся.

Отказы: исключение до/после искусственного эффекта; return0; исключение/return0
на втором объекте; изменение посторонней сущности, процесса или уже повернутого
объекта перед следующим; поздняя смена контекста и смена после записи started;
alias composite IDs, нечисловые bounds, ошибочный packet SHA, чужой fence,
занятый executor. Набор останавливается на первом неопределённом результате.
Нет продолжения оставшихся команд, обратного действия или снятия fence.
Повтор уже claimed plan отклоняется без новой геометрической команды.

Перед первым действием выполняются два held guards. Перед каждым следующим
сверяется весь предыдущий снимок. После отдельной команды допускается изменение
только geometry её конкретного target; тип/ID/name/level, остальные entities,
process/document/context/units и сохранённые файлы должны оставаться прежними.
После записи started повторяется финальная проверка: записи на диск не дают
права использовать устаревший контекст. World bounds хранятся в native paired
порядке xmin/xmax/ymin/ymax/zmin/zmax, не в SDK minXYZ/maxXYZ.

Независимый audit проверяет неизменность исходных файлов, plan/journal chains,
fence/claim, параметры и порядок targets, фактические возвращённые записи,
summary и отсутствие succeeded_verified. Ошибка первого fake test: выбор
постороннего объекта через Single из двух кандидатов сам бросал исключение.
Audit обнаружил отсутствующий returned receipt; fixture исправлен на явный
type21, затем выполнен новый полный прогон. Защитные требования не ослаблены.

## Ограничения и следующий блок

Это внутренняя реализация с fake callback. Нет native queue route, публичного
MCP execute tool или production COM adapter для этого исполнителя. CLI запускает
только искусственные сценарии и требует явный собственный marker. Ни open CAD,
ни установленный runtime, ни готовый пакет К7 не изменяются.

Фальшивый эффект тестов специально не является физическим поворотом. Этот блок
проверяет управление пакетной операцией; его результаты не доказывают направление
угла, SAFEARRAY, геометрию или топологию PowerShape. Требуются независимый verifier
и геометрические postconditions для всех targets, затем COM adapter/SDK route,
fault matrix полного транспорта и отдельно согласованный live scenario.
Сохранение и reopen остаются обязательными; остальные transforms/organization,
К9–К11 также впереди. Общая цель не завершена.

Installed Autodesk API/typelib: RotateItem принимает integer degrees иVARIANT
axis(point+direction). [Rotating objects](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-725B177C-A55E-4A64-B303-72A4069C1DE6)
описывает зависимость UI-оси отprincipal plane/workspace; код сохраняет явную
мировую ось и не выводит COM-конвенцию из UI. Product Help en_US/PWRSS2023 проверен.

PR28/sourcec37b7c234461aae0ed9fe34d1a7f70d3c6608970 прошёл все четыре Windows CI:
38446619277/38446665339 baseline,38446619585/38446665280 SDK.
Native регрессия session75721 остановилась с ошибкой доступа к собственному файлу;
артефакты сохранены, CAD не подключался. Новая проверка с обычными user permissions
session8331 завершена успешно:531 checks, stage
work/K8-rotation-execution-regression-user. Ошибка доступа в sandbox-прогоне
не воспроизвелась с обычными правами пользователя. verify-sdk.ps1 включает новый21-case runner иaudit.
