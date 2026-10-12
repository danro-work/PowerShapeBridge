# P2.27 — сохранённые рабочие копии и файловое владение

9 октября 2026. Панель/worker/свежий MCP-адаптер 0.27.0 установлены в releases/P2.6;
general-v1 с экспериментами наборов, планов и копий. Каталог 13, read-only 6.
MCP v1, CAD-исполнитель и guard в его COM-контексте ещё не приняты.

## Результат

Добавлены prepare_working_copy(sourcePath, sourceSha256) и inspect_working_copy по
opaque handle. Сохранённый psmodel удерживается с запретом записи/удаления, сверяется
SHA; создаются независимые Working.psmodel и ReadOnly checkpoint/Before.psmodel,
разные файловые идентификаторы и immutable ownership SHA. Источник не записывается.
File/target policy отвергает source/checkpoint/чужую модель, reparse/junction,
hard-link копий, подмену файла/receipt и изменённый checkpoint. Только working bytes
могут меняться при сохранении без замены файловой идентичности; это наблюдение,
не атомарный fingerprint. Контракт: WORKING-COPIES.md.

На первой пассивной проверке выявлена занятость Test_Block_P2: строгий FileShare.Read
отклонён, без fallback или закрытия CAD. Успешная приёмка выполнена из явно указанного
сохранённого исходника D:/ChatGPT/PowerShapeBridge/Test_Projects/Test_Block.psmodel
с закреплённым SHA. Несохранённое состояние активного документа не копируется.

Через установленную единственную панель прошли 23 MCP-вызова: 9 native ответов
(6 read/set, 2 copy success, 1 busy refusal), 4 локальных plan/journal success,
10 локальных отказов. Независимый аудит сверил envelopes, исходные ответы,
set/receipt/plan/journal/ownership SHA, source/work/checkpoint bytes, разные file IDs
и checkpoint ReadOnly через отдельный Windows API. Три окна, активный Test_Block_P2
и наблюдаемые свойства совпали до/после; хеши трёх защищённых файлов не изменились.
CAD-открытие, активация, выделение, сохранение, геометрия и screenshot не выполнялись.

## Проверки и развёртывание

8 новых Python-тестов + 55 регрессионных = 63; MCP-протокол; 162 C# проверки без CAD.
Нативные file tests меняют только собственные opaque fixtures. Проверены параметры,
fixed worker envelopes, чужие/истёкшие handles, read-only/config gate, отмена до
очереди, timeout/failed inspection без повторного запроса, native receipt mismatch;
busy source, checkpoint/hash/path/ID/conflict, working bytes change, replacement,
hard-link и реальный junction. Копии не сертифицированы как загруженные CAD-модели.

Сборка work/P2.27-copies, manifest включает OwnedCopies в бинарниках и working_copies.py.
После добавления errorCode файловых отказов worker пересобран, 162 C# проверки и
junction/preflight повторены; Python source workflows не менялись. Исходный полный
кодовый прогон сохранён в code/code-verification.json, окончательные байты/результат
worker — code-final-verification.json и source-manifest.json. Исправлен синтаксис
скобок первой staging-компиляции до успешной сборки; runtime тогда не затрагивался.

Deployment preflight отклонил corrupt artifact и wrong compiled version; развёртывание
по work/P2.27-deployment-plan.json успешно с резервом
work/general-deployment-backup-20261009-122954. Штатно закрыта только проверенная
панель Bridge; новая скрытая панель 0.27.0 одна, config SHA/heartbeat проверены.
Процесс PowerShape/его окна не закрывались. Очередь пуста, worker завершён.

## Доказательства и следующий блок

evidence/P2.27-copies: preflight native reads; code/code-verification.json и логи,
worker-tests-after-error-code.txt; code-final-verification.json; junction-preflight.json;
deployment-preflight.json/deployment.json; installed-mcp/qualification.json и
independent-audit.json; protected-before/after.json; source-manifest.json и сведения
двух адаптеров. Путь принятой рабочей копии — в copy-creation.json/нативном ownership.
Reservation, ownership и созданные копии сохранены, не удаляются при обновлении runtime.

Подключённый адаптер чата проверяется отдельно: до переподключения остаётся 0.22.0,
свежий CLI — 0.27.0. Регистрация MCP/Codex не менялась. File handles — временные
доказательства; старые PID/документы/entity refs нужно перепроверять.

Открытие/повторное открытие рабочей копии и привязка нового CAD-документа к ownership
ещё впереди. CheckTarget — file/path predicate, освобождает handles и не заменяет
guard непосредственно в контексте команды. План пока не принимает workingCopyHandle,
executionEnabled=false. Далее общий native executor/журнал/постусловия, тесты на
собственных копиях и live приёмка поверхностей/кривых/lifecycle. Технологический модуль
остаётся отдельным продуктом после MCP v1.
