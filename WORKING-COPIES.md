# Сохранённые рабочие копии — экспериментальный контракт P2.27

К5, 12 октября 2026: source/staging native 0.32.0 создаёт ownership schema 2:
`workingcopies/copyId/Working_copyId.psmodel`, где copyId — 32 lowercase hex.
Имя уникально для каждой новой копии. Schema 1 с `Working.psmodel` продолжает
проверяться по прежнему фиксированному layout; существующие files/manifests
не переименовываются. Checkpoint остаётся `checkpoint/Before.psmodel`.
Native open guard до claim/journal/COM OPEN отвергает имя, уже присутствующее
в свежем window listing (case-insensitive), даже если путь другой. Исходный
OPEN, вызвавший dialog «Working уже открыта», остаётся unknown и не повторяется.
Установленный runtime пока 0.31.0; новый layout ещё не квалифицирован live.

P2.31, 12 октября 2026: runtime 0.31.0, каталог 21/read-only 6. Первый общий executor переносит один SOLID в manifest-owned копии: два guard-прохода в удерживаемой COM-паре, один TranslateItem, постоянный intent claim, copy fence и отдельная independent verification. Live X +1 и отдельный X −1 подтверждены; наблюдения собственной копии восстановлены, Test_Block_P2 снова активен, четыре окна и protected saved hashes сохранены. Save/open/close/selection/screenshot не выполнялись. OWNED-TRANSLATION.md и STATUS-P2.31-EXECUTOR.md; ниже история предыдущих блоков. MCP v1 не завершён.

9 октября 2026. Runtime 0.27.0. Подготовка/проверка файлов и предикат владения целевым
путём реализованы; открытие CAD, SaveAs, выполнение плана и COM guard ещё впереди.

## Публичный интерфейс

workingCopiesExperimental=true допускается только в general-v1. Профиль
config/general-working-copies.json включает также наборы/планы: 13 инструментов;
read-only сохраняет 6. Без нового флага остаются каталоги P2.26. Копирование
независимо от entity sets и не требует технологических настроек.

prepare_working_copy принимает sourcePath и sourceSha256. Источник — явно заданный
сохранённый .psmodel, абсолютный путь локального диска; SHA-256 закрепляет его байты.
Нет произвольного выходного пути, overwrite, STEP-конвертации или Execute. Copy из
STEP с переименованием в psmodel не допускается: импорт потребует отдельного CAD
инструмента. Это копирование дискового файла, не снимок активного документа:
несохранённые изменения CAD не включаются, читаемость/геометрия копии в CAD не
сертифицируются. Модель не открывается, не выбирается, не сохраняется и не закрывается.

Worker открывает source с FileShare.Read: запись/удаление запрещены до завершения
копирования. Если файл занят писателем, возвращается working_copy_rejected,
errorCode=file_busy; ослабления sharing, закрытия CAD или повторного запроса нет.
Нативный файл/его конечный путь и все существующие предки проверяются: streams,
device/UNC, относительные/неоднозначные пути, symlink/junction/reparse отвергаются.
Размер источника: 1 byte..1 GiB; длительность на медленном диске не гарантируется.

Из одного удерживаемого источника создаются два новых файла:

- workingcopies/copyId/Working_copyId.psmodel — будущая рабочая модель schema 2;
- прежний workingcopies/copyId/Working.psmodel — совместимый layout schema 1;
- workingcopies/copyId/checkpoint/Before.psmodel — независимый исходный checkpoint.

CreateNew, разные native file IDs, собственные файловые данные, fsync обоих файлов;
source/work/checkpoint хешируются и равны ожидаемому SHA. После записи checkpoint
получает ReadOnly. ownership.json создаётся неизменяемым: copyId, версия/время,
saved_disk_bytes_only, executionEnabled=false, три пути, начальные SHA/размеры,
volume serial/file index/creation time. Source никогда не записывается. Reservation
workingcopies/copyId.reservation.json предотвращает повторное использование ID;
папка/файлы/marker при частичном сбое сохраняются, автоматически не удаляются.

Успех working_copy_prepared_disk возвращает ownershipSha256, ownership,
workingCopyHandle; срок handle не более 300 секунд в этом MCP-подключении, лимит 128.
Документная активация сама по себе не отзывает владение файлом: оно отделено от
текущего CAD-контекста. Ошибка/тайм-аут/отмена copy workflow отзывает leases.
После переподключения файлы сохраняются; публичной повторной выдачи handle пока нет.
По произвольному copyId/SHA/пути inspect вызывать нельзя.

inspect_working_copy принимает рабочий handle и передаёт worker фиксированные
copyId/ownershipSha256. Проверяются байты ownership, фиксированная раскладка путей,
отсутствие reparse и hard-link у копий, сохранность нативного ID/creation time,
неизменённый SHA/размер и ReadOnly checkpoint. Рабочие байты могут изменяться при
сохранении в тот же файл: возвращаются workingSha256 и workingFileChanged. Рабочий
файл читается с ReadWrite sharing, чтобы проверять уже открытые рабочие файлы;
этот хеш — наблюдение в момент чтения, не атомарная ревизия. Checkpoint читается
с запретом записи/удаления. Source после создания не требуется открывать заново.

Read-only исключает оба инструмента: подготовка пишет файлы, а inspect handle
выдаётся только подготовкой в том же подключении. Действующие 6 read tools
сохраняются, CAD-команд/изображений в них нет.

## Предикат целевого пути и ограничения

OwnedCopies.CheckTarget проверяет receipt/файлы и допускает только точный
Working.psmodel данного владельца. Source, Before.psmodel, чужой файл и замена
под тем же именем отвергаются. Native file ID — наблюдаемая файловая идентичность,
не постоянный ID CAD-документа; сохранение через replacement/rename может сменить
ID и потребует отдельно квалифицированного обновления владения. Полный hostile
filesystem/вмешательство администратора этим механизмом не исключается.

Это предикат файлов/пути, а не guard в COM-контексте команды. Его текущий вызов
освобождает файловые handles; никакая CAD-операция этот путь пока не исполняет.
Подготовленный план P2.26 по-прежнему executionEnabled=false и не связан с рабочим
handle. В будущем исполнитель обязан проверить активный process/document/context,
план/наборы, владение и checkpoint непосредственно перед командой, вести durable
журнал нативного requestId и проверять результат независимо.

При неизвестном результате проверяются request/response и reservation/ownership/
частичные файлы, затем диск. Повторное исполнение/cleanup не запускается. Ответ
при отказе содержит copyId и conservative localArtifactsMayExist даже при отказе
до создания папки. Тайм-аут/отмена после очереди означает неизвестный исход именно
локальных файлов; CAD-сохранения/геометрии в этом пути нет.

## Приёмка

Публично приняты prepare/inspect копий сохранённого исходного Test_Block.psmodel,
отказ занятого Test_Block_P2 до создания файлов, чужого handle и двух вызовов в
read-only. Независимый аудитор сверил нативные envelopes/результаты, ownership SHA,
байты source/work/checkpoint и native file metadata через отдельный Windows API.
Три исходных окна/активная модель/наблюдения/защищённые хеши не менялись. Копии
оставлены как доказательства, в CAD не открывались.

Кодовые испытания на собственных opaque-файлах проверили изменение рабочих байтов,
отказы источника/checkpoint/чужого target, замену файла, hard-link, изменение/потерю
защиты checkpoint, подмену ownership, конфликт ID, busy source и неправильные пути.
Реальный junction отдельно отклонён до копирования. Эти opaque-файлы не являются
CAD-моделями; проверка владения не объявляется геометрической квалификацией.
Итоги: STATUS-P2.27-COPIES.md, evidence/P2.27-copies.
