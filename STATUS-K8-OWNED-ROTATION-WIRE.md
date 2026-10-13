# К8 — сквозная подготовка поворота

Проверен путь: официальный C# MCP STDIO → очередь файлов → native-подготовка
пакета → проверка доказательств C# → временная ссылка ownedRotationHandle.
Источник: SDK0.39.2-dev/native0.34.0-dev. Установленный runtime не обновлялся.
Живой сценарий К7 ждёт отдельного разрешения пользователя.

## Что проверено

Шесть сценариев прошли в evidence/K8-owned-rotation-wire/focused3:

- Контроль: собственная копия, три типа сущностей SOLID/SURFACE/COMPCURVE,
  связь документа, план с явной осью, native-пакет и scoped handle ≤300 секунд.
- Изменившийся processId: отказ до записи пакета.
- Заблокированная рабочая копия: отказ, fence сохранён.
- Смена контекста при последнем чтении: отказ; уже записанный prepared-пакет
  остаётся для диагностики, ссылка не выдаётся.
- Потеря native-ответа: неизвестный исход подготовки, без ссылки и повторов;
  оригинальный ответ и пакет сохранены.
- Изменение байтов пакета: проверка SHA отклоняет пакет, ссылка не выдаётся.

Во всех сценариях чужие handles и лишнее поле command отклонены до очереди.
В сервере чтения инструмент отсутствует. Независимый дисковый audit подтвердил
237 хешей/30 запросов, исходные защищённые fixtures, отдельные физические файлы
source/working/checkpoint и отсутствие новых геометрических команд в журнале.
CLI отказывается создавать fixture без явного маркера.

## Изменения и предел доказательства

EntitySets.Run делегирует прежнюю логику InContext с наблюдателем Probe.Run.
Fixture передаёт искусственный наблюдатель: это позволяет пройти настоящую
native-подготовку набора без подключения COM. Производственная проверка
аргументов, времени, receipts, наблюдений и запись набора сохранены.

OwnedRotationWireFixture использует собственные синтетические файлы. После
файлового копирования он моделирует уже открытый собственный документ.
Это не проверка OPEN, реального CAD, геометрического результата или топологии.
RotateItem/Exec/selection/save не вызываются; scoped handle остаётся disabled.
Открытый PowerShape, установленная панель и READY-пакет К7 не изменяются.

Runner verify-owned-rotation-wire.ps1 включён в verify-sdk.ps1. Промежуточные
ошибки самого fixture (отсутствовал timestamp envelope; изменение копии окна
вместо окна контекста) исправлены, новые прогоны используют отдельные каталоги.

Предыдущий PR27/e6425fcf прошёл четыре Windows CI:
38443460040/38443474063 baseline,38443460086/38443474353 SDK.
Полный прогон session5783 завершён успешно: evidence/K8-owned-rotation-wire/sdk-full,
stage work/K8-rotation-wire-full.417 .NET/531 native/175 artifact cases (137 accepted,
0 mismatch),32 SDK faults/25 native faults/20 activation/40 legacy STDIO/22 checkpoint/
6 recovered OPEN/7 checkpoint wire/6 filesystem wire/15 native rotation/15 Receipt checks/
6 rotation wire cases иInspector прошли. Manifest содержит190 хешей.
Это полный прогон SDK0.39.1 до следующего исправления диагностики.

Autodesk Product Help en_US/PWRSS2023 подтверждает влияние principal plane и
origin workspace на UI-ось: [Rotating objects](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-725B177C-A55E-4A64-B303-72A4069C1DE6).
Это основание сохранять явную ось и проверку мировой системы; статья не
доказывает знак угла/SAFEARRAY в COM. API и typelib исследованы отдельно в
K8-ROTATION-PLAN-CONTRACT.md и evidence/K8-api-signatures/typelib.json.

Исправлено в SDK0.39.2: timeout/proof-error ответ OwnedRotationTools сохраняет
bridgeRequestId исходного запроса. Полученный от IPC ID закрепляется до проверки
deadline/receipt; unknown не выдаёт handle и не повторяет команду.
15 профильных тестов прошли, включая проверки сохранности ID при потерянном
ответе и изменённых bytes packet: evidence/K8-owned-rotation-wire/request-id.
После исправления session68175 завершён успешно: шесть сквозных сценариев
с обязательной проверкой bridgeRequestId,237 hashes/30 requests иmarker refusal
прошли в evidence/K8-owned-rotation-wire/request-id-wire. Никаких повторов OPEN
или операций на CAD не было.
В полном предыдущем manifest восемь записей изменились после прогона (host code,
metadata/tests, host DLL/deps иновые wire checks); этот manifest не объявляется
доказательством полного прогона исправленного SDK. Новый source проверит Windows CI.

Далее: guarded rotation executor, независимая проверка результата, faults,
квалификация знака/оси/геометрии и сохранение/reopen. К8 и цель до К11 не завершены.
