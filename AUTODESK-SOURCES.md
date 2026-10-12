# Источники Autodesk для MCP

12 октября 2026. Пользователь разрешил англоязычный Product Help (`en_US`).
Каталог подтвердил PowerShape Standard, код `PWRSS`, выпуск `2023`.

| Источник | Установленный факт | Практическое применение |
|---|---|---|
| [How to automate PowerMill and PowerShape using API libraries](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/How-to-automate-Powermill-using-Api-libraries.html), Autodesk Product Help, прочитан 12.10.2026 | Autodesk описывает приложения с .NET Framework 4.8 и свои API-библиотеки | Существующий Framework STA-worker сохраняем; новый .NET MCP-host связываем через IPC |

Источник не доказывает невозможность COM из .NET 10 и не заменяет живую
квалификацию методов. При новых операциях добавляем точный источник и отдельно
результаты испытания на собственной модели; экспериментальное не считаем принятым.
