# К8 — слой тела сохранён и проверен после открытия

15 октября2026. По отдельному прямому разрешению пользователя выполнены:
один SaveAs и один OPEN новой собственной копии. Разрешение использовано.
Повторный перенос тела, повтор SaveAs/OPEN, закрытие или переключение назад
не выполнялись. Native0.34.8 и изолированный SDK0.40.1; обновления панели не было.

## Подтверждённый результат

В новой активной `Working_cde95c3e54a64c2f9264443cacb8837f` одно тело «1»
остаётся на видимом `Surfaces(1)`. Выделение пустое, система мировая.
Измеренные свойства и world bounds `[-10,10,-10,10,0,20]` прежние.
Открыто восемь окон; новое окно принадлежит испытательной копии.

| Доказательство | Результат |
|---|---|
| SaveAs request | `290b1258e99d46d58f6a1626eff57a35`, journal6events/succeeded_verified, commandCalls1 |
| OPEN request | `796c423be17f41a194398b385aae519f`, journal6events/succeeded_verified, ровно1command_started/returned |
| Новый copyId | `cde95c3e54a64c2f9264443cacb8837f` |
| Новый modelId, только историческая запись | `280011523`; перед будущими действиями читать заново |
| Новый binding | `acfbfa87baa04d11a8f06eb70713aa31`, выдан отдельным verifier |
| Saved result / seed / working / checkpoint | SHA `a0f13da5235f5ba414f8bd40eb1b3cc3e316f1b2bafded867d4df8c28961f5ff`,1179648bytes |
| Защищённые файлы и исходный журнал переноса |24hashes неизменны; original SetItemsLevel не повторялся |

Saved result находится в
`releases/P2.6/savedresults/6c23ff45abc347368aa235f016f38c3c/Result_6c23ff45abc347368aa235f016f38c3c.psmodel`.
Исходный working7b076 и старый checkpoint не перезаписаны.
Все четыре новых saved/seed/working/checkpoint — разные физические файлы;
seed и checkpoint ReadOnly. Own SaveAs fence освобождён независимым verifier;
claims и журналы сохраняются.

## Независимая проверка

После native SaveAs verifier и отдельного OPEN verifier выполнено ещё одно
пассивное COM-чтение текущей модели, состава и world bounds дважды.
Нативный ID тела взят из нового чтения, не из прежней ссылки.
Первый диагностический запуск в restricted service context не увидел COM
(MK_E_UNAVAILABLE); его failed report сохранён. Повторялось только чтение
в пользовательском сеансе, без CAD-команд; оно успешно.

Evidence: `evidence/K8-level-saved-result-approved20261015a/result.json`,
`independent-native-user-result.json` и `independent-audit.json` — passed.
Отдельный file-only audit проверил обе цепочки6events, планы/claims/SHA,
новый binding, четыре файла и их физическую раздельность, защиту checkpoint,
слой/пустое выделение/единицы/измеренные свойства/габариты и восемь окон.

После реального результата прошли75целевых SaveAs/reopen tests,0skips:
`evidence/K8-level-saved-result-after-live-focused/level-saved-result.trx`.
Это принятие одного SOLID в одном сценарии, не полной топологии или общейК8.
Другие типы/наборы, workplanes, часть transforms и К9–К11 остаются в плане.
