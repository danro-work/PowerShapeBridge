# К7 — предлагаемый живой сценарий checkpoint

Сценарий подготовлен, но не выполнен. Требуется согласование по принятому
MCP-DEVELOPMENT-PLAN.md: «Мутационные испытания проводятся в пределах отдельно
согласованного конкретного сценария и рабочих копий».

## Что предлагается

1. Применить native Bridge0.33.3-dev →0.33.5-dev с новой резервной копией файлов,
   штатным закрытием только известной панели Bridge и запуском одной новой панели.
   PowerShape не закрывается. Конфигурация сохраняет прежние flags и добавляет
   checkpointRecoveryExperimental/recoveryInspectionExperimental. Default MCP
   Codex не переключается; испытание использует отдельный SDK0.38.1-dev клиент.
2. Повторить пассивное чтение и сверить ожидаемый процесс/старт/документ/шесть окон,
   idle queue, исходный plan/owner/checkpoint и protected hashes. Изменившийся
   контекст останавливает сценарий до OPEN.
3. Подготовить один recovery intent для исходной unknown operation
   6ed9d4106d054b28a05e8ab0b59c5811, создать одну НОВУЮ собственную рабочую копию
   и отдельный ReadOnly checkpoint из сохранённых байтов.
4. Один раз открыть новый Working_<newCopyId>.psmodel через guarded MCP OPEN.
   После fresh reads выполнить independent verify и создать current binding.
   Новый документ станет активным; ожидается седьмое окно. Файлы, модели и
   журнал исходной operation сохраняются. Старый unknown не разрешается.
5. Сверить байты новой пары с исходным checkpoint, все защищённые хеши, сохранность
   прежних шести документов и исходных journal/claim. Оставить новую копию открытой
   для просмотра. При неизвестном исходе сохраняются артефакты; OPEN не повторяется.

Нет Save/SaveAs, закрытия CAD-документов, выделения, геометрических команд,
Undo/inverse, screenshot, мыши или проверки через crash. Несохранённые изменения
после checkpoint в новую копию не входят. Приёмка относится к saved bytes,
owned OPEN и наблюдаемому контексту; полной топологической эквивалентности не заявляет.

## Исходные доказательства и пакет

- Origin copy:5fef10809eb44904ade27e2ca843a8b9, schema1.
- Origin plan SHA:711b1b1ea6e1a250fdd7f3f8400198e9a7be22463ea9dab364802c21184581ae.
- Owner SHA:56003d5d6a73f6b3b10b7e9d24f8e3409357d22c67ff93021e81cb68823ae845.
- Checkpoint:releases/P2.6/workingcopies/5fef10809eb44904ade27e2ca843a8b9/checkpoint/Before.psmodel;
 753664 bytes, ReadOnly, SHA c9060ce2873f692d8118e26192149487742410108772b06261d686d03f814d4d.
- Исходный журнал содержит prepared/execution_claimed/command_started и сохраняет
  open-once claim. Он не является подтверждением результата исходного OPEN.
- Пассивное чтение13 октября: native0.33.3, PowerShape process17304,
  start2026-10-08T08:38:27.4633424Z, active Test_Block_P2, six windows. Это снимок,
  а не будущие mutation references; проверка повторяется перед действием.
- Native package:work/K7-live-checkpoint-package; это копии именно квалифицированных
  binaries/modules из full stage, с отдельно проверенной merged конфигурацией.
  Fixture markers и каталоги испытаний в пакет не включены.
- SDK package:work/K7-live-checkpoint-sdk; DLL SHA совпадает с квалифицированной DLL.
- Reviewed deployment script:deploy-general.ps1, plan-only файл
  work/K7-live-checkpoint-deployment-plan.json. Apply ещё не запускался.
- Prepared client:work/K7-live-checkpoint-client.py. Требует explicit approved token,
  создаёт global scenario-once claim до подготовки intent и не содержит retry.
  Синтаксис проверен; живой запуск возможен только после согласования.

Код241bc56d920bad613b4f939b49f5984be99f3d2f/PR24 прошёл full local qualification
иfour Windows CI:38429476459/38429467416 baseline,38429476559/38429467454 SDK.
Новые filesystem failures подтверждены в actual SDK CI log.
STATUS-K7-CHECKPOINT-FILESYSTEM.md содержит полный scope/evidence.

Autodesk Product Help en_US/PWRSS2023 проверен:
[Opening a model](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-EC8A3F76-6087-499E-BD11-8022DDBA82FD)
описывает окна моделей; [Recovering a model](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-3EF761A5-A110-4F0A-8D5F-5AA91EB4141E)
описывает встроенное autosave recovery. Наш сценарий использует явно защищённый
checkpoint и не вызывает этот диалог/не меняет autosave настройки.
