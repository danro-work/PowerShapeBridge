# Источники Autodesk для MCP

12 октября 2026. Пользователь разрешил англоязычный Product Help (`en_US`).
Каталог подтвердил PowerShape Standard, код `PWRSS`, выпуск `2023`.

| Источник | Установленный факт | Практическое применение |
|---|---|---|
| [How to automate PowerMill and PowerShape using API libraries](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/How-to-automate-Powermill-using-Api-libraries.html), Autodesk Product Help, прочитан 12.10.2026 | Autodesk описывает приложения с .NET Framework 4.8 и свои API-библиотеки | Существующий Framework STA-worker сохраняем; новый .NET MCP-host связываем через IPC |
| [Opening a model — PowerShape 2023](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-EC8A3F76-6087-499E-BD11-8022DDBA82FD), Product Help `en_US`, прочитан 12.10.2026 | Новый модельный документ открывается в отдельном окне; до 20 одновременно открытых моделей; read-only модель требует нового имени при Save As | К5 проверяет новое окно и сохранность прежних; К6 будет проверять отдельный saved target/reopen |

Источник не доказывает невозможность COM из .NET 10 и не заменяет живую
квалификацию методов. При новых операциях добавляем точный источник и отдельно
результаты испытания на собственной модели; экспериментальное не считаем принятым.

Поиск сообщения duplicate-name не нашёл документа с точным текстом ошибки.
Guard/unique owned filename основаны на live accessibility text PowerShape:
«Модель с таким именем: Working уже открыта», а не на предположении из Help.
Returned статьи про corrupt model/shareddb относятся к другим ошибкам; их
процедуры удаления/переименования системных моделей не применялись.
