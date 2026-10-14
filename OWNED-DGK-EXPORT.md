# DGK — внутренний guarded executor

`OwnedDgkExport.Execute` пока является внутренним callback executor для
квалификации. Production COM adapter и публичного MCP/queue route нет.
Испытания используют fake contexts и собственные opaque fixture files.
Ни одна функция этого модуля не вызвана на пользовательской модели.

## Последовательность и guards

Вход — точные targetId/targetSha256 собственного подготовленного DGK-комплекта.
TargetLease удерживает binding/set/owner/checkpoint/working bytes, directories и
proof artifacts. Dependencies recheck отделён от initial empty/no-claim predicate:
после publication собственных fence/claim executor повторяет dependency guards,
не превращая target lease в историческое разрешение на повторную команду.

Перед каждым действием: два fresh observations, world frame, idle status,
source properties/units/inventory, exact own selection prefix, direct native
selected keys, item mapping, active levels, retained document/windows и hashes
сохранённых документов. Набор имён и keys сравнивается независимо от порядка,
но собственные ADD выполняются в заданной последовательности. Нет implicit
level activation, empty-selection model fallback или выбора посторонних объектов.

Observed settings interface задаёт точную форму snapshot:format/frame/variant/
declared triangle/features/effective options SHA/provider qualification. Принимаются
только synthetic_only/observed_unqualified. Это custom interface контракта,
не утверждение о наличии Autodesk getter. Native provider/settings по-прежнему
не квалифицированы; имя вымышленного COM getter не вводилось.

После initial guards: freeze execution plan, prepared journal, own export fence,
one-use claim, execution_claimed. Затем для каждого ADD, одного DGK export и
одного clear_own_selection пишутся отдельные started/returned artifacts.
Каждый returned ссылается на SHA своего started. Aggregate command_returned
пишется только после всей последовательности и проверки файла/контекста.

Экспорт получает только fixed own primary path и detached planned parameters.
До него artifacts directory обязан быть пустым. После него DgkBundleFiles.Lease
удерживает весь допустимый комплект, identities/bytes/directory membership до
clear и postcondition. Попытка перезаписать удерживаемый файл не проходит.
Optional отсутствия перечисляются; missing required/stray/malformed DMT запрещают
clear. MAC/XML никогда не исполняются. Primary DGK остаётся opaque byte witness.

## Исход и отсутствие повторов

Успешный callback control заканчивается `owned_dgk_export_executed_unverified` и
журналом executed_unverified. GeometryVerified/nativeProviderQualified/
nativeReturnCodeQualified/copyFenceReleased всегда false. Контрольное чтение
байтов не является полной CAD или топологической проверкой.

Любой неизвестный исход после начала действия сохраняет fence/claim/journal/
files/selection evidence и не выполняет cleanup, повтор, Undo или inverse.
Clear допустим только для независимо проверенного собственного selection set и
удерживаемого допустимого bundle. Ошибка clear также не приводит к повтору.
Повторный Execute с тем же target блокируется без callback. Исходному mutation
fence и чужим документам executor не приписывает разрешённый результат.

## Проверки и дальнейшая работа

41 Framework callback cases:5 controls и36 refusals/unknown, включая curves,
mixed/reordered selections, companions, busy, changed context/settings, lost
reply, malformed/missing output, held-file write и late directory membership.
Independent Python audit —1006 hashes, native file identity/stat, complete journal
chain и per-step evidence. Target/bundle regression42/12 и native531 прошли.

Далее отдельный getter-only receipt verifier с fresh observations и historical
proofs, затем qualified COM provider/settings/selection, SDK full/read-only
boundaries, concrete live own-copy scenario и owned import/geometry roundtrip.
Весь оставшийся К8–К11 сохраняется; данный модуль не завершает экспорт v1.
