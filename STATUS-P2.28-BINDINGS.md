# P2.28 — пассивная привязка рабочего CAD-документа

9 октября 2026. Установлены панель/worker/свежий MCP 0.28.0 в releases/P2.6;
general-v1 с экспериментами наборов/планов/копий/привязки. Каталог 15, read-only 6.
Успешная привязка проверена синтетически; в CAD принят отказ чужого активного
документа. Открытие рабочей копии, исполнение геометрии и MCP v1 ещё не приняты.

## Реализация

bind_working_document принимает два opaque handles, validate — отдельный binding
handle. Нативный worker проверяет исходный receipt и срок, дважды читает модель
через одну удерживаемую пару COM application/document, сверяет стабильные окна,
активный документ и владение Working.psmodel. На успешном пути удерживаемые
ownership/work/checkpoint handles защищают от замены и записи checkpoint до
записи immutable CreateNew/fsync binding. План/наблюдения и окно закреплены SHA.
Это пассивный путь без CAD-команд; DOCUMENT-BINDINGS.md описывает ограничения.

## Проверки и приёмка

74 Python-теста (11 новых), MCP-протокол, 195 C# проверок без CAD. Проверены
синтетическая успешная подготовка/валидация, смена process/document/контекста,
изменение между чтениями, expiry, source/checkpoint/foreign targets, реальные
file leases, readonly loss, подмена binding, duplicate ID, фиксированные envelopes,
foreign handles, отзыв источников до/после native ответа, cancellation/timeout
без replay, конфигурация и read-only. Opaque file fixtures не являются CAD-моделями.
Реальный junction и deployment preflight (corrupt artifact/wrong version) пройдены.

Первый кодовый прогон выявил недостающий timestamp в новом Python fixture и
неучтённый UnauthorizedAccessException в проверке защищённого checkpoint;
исправлены до приёмки. После проверки срока зависимости при native ответе добавлен
один тест и выполнены окончательная сборка/полный прогон/развёртывание/приёмка.
Исходная и окончательная успешные приёмки сохраняются отдельно.

Установленный MCP: 28 вызовов, 10 native ответов, 4 local plan/journal success,
14 local refusals. Активный Test_Block_P2 не является новой owned копией;
bind отклонён с active_document_not_owned_working_copy после двух наблюдений в
одном COM-контексте. Binding artifact/handle не создан. Независимый аудит сверил
envelopes/ответы, исходный receipt SHA, модель/digests наблюдений, самостоятельно
вычисленный digest окон, файлы копий/checkpoint и native file IDs. Три окна,
активная модель, наблюдаемые свойства и хеши трёх защищённых файлов совпали.
CAD-открытие, активация, выделение, сохранение, геометрия и screenshot не выполнялись.

## Развёртывание и доказательства

Сборка work/P2.28-bindings; config/general-document-bindings.json. Проверенный
work/P2.28-deployment-plan.json, штатное закрытие только известной панели Bridge,
одна скрытая новая панель, версии/config SHA/heartbeat и пустая очередь проверены.
Окончательный резерв work/general-deployment-backup-20261009-130309;
первый резерв work/general-deployment-backup-20261009-130010 сохранён.

evidence/P2.28-bindings: preflight native reads/старый адаптер; protected-before/after;
code/code-verification.json и логи; junction-preflight; deployment-preflight;
deployment-initial/deployment; installed-mcp и installed-mcp-final с qualification/
independent-audit; source-manifest и свежая диагностика. История чата не заменяет
эти новые чтения. Старый подключённый адаптер 0.22.0 отделён от свежего 0.28.0.

Далее открытие собственной копии без изменения чужих документов и успешная живая
привязка; затем план/наборы/ownership и guard в контексте реальной команды,
нативный durable журнал/постусловия и общие операции на разных собственных моделях.
Технологический модуль остаётся отдельным продуктом после MCP v1.
