# К8 — независимая проверка равномерного масштаба

UniformScaleSampleVerifier — shared C#6/.NET mathematical reference predicate.
Он не выполняет CAD-команд, не подключается к PowerShape, не освобождает fence
и не зарегистрирован как MCP primitive. Принятие общего scale tool остаётся
требованием К8–К11; этот модуль является одним из его компонентов.

## Параметры и свидетельства

Явно задаются world centre из трёх конечных raw координат и finite factor>0,
factor!=1. Одинаковое масштабирование выполняется по всем трём осям. Отрицательные
коэффициенты требуют отдельного контракта отражения и native qualification;
не заявлены как принятая возможность или исключение из общего v1 по умолчанию.
Нет axis locks, implicit origin, active-workplane default или keep-original.

Before/after samples закрепляют native type/key, measurement descriptor SHA,
world/document_raw, уникальные упорядоченные anchors, paired bounds. Начальный
reference profile:SOLID40/SURFACE21/CURVE59; остальная матрица типов остаётся
непроверенной, а не исключённой из общего плана. Samples владеют копиями входов
и раскрывают read-only collections; constructors сами не авторизуют действие.

Позиции проверяются по p'=c+s*(p-c). Unit orientations/normals имеют явную
degree0, не меняются при положительном uniform factor и не получают перевод
центром. Derivative/displacement vectors имеют degree1 и умножаются на s.
Поставщик обязан квалифицировать смысл каждого вектора; неизвестную native
normal/derivative layout нельзя принимать только по подходящей длине массива.

Каждый скаляр имеет отдельный key и declared degree0/1/2/3:
безразмерная величина/длина/площадь/объём. Expected value = before*s^degree.
Нельзя переносить безымянные rotation invariants как dimensioned scale metrics.
Raw scalar доступен только при подтверждённой размерности getter; отсутствие
native метрики не заменяется нулём. Известный ноль допустим, но ничего не
доказывает о замкнутости/валидности CAD-топологии.

Для каждой метрики обязателен свой positive finite absolute tolerance в её
raw-размерности, exact набор keys. Position absolute tolerance задаётся отдельно;
relative tolerance finite0..1e-6. Spatial relative scale считается относительно
centre, а не абсолютных world координат. Unit orientations имеют dimensionless
relative tolerance; большой допуск длины не разрешает изменение нормали.
Пределы координат под положительным uniform scale преобразуются точно, без
rotation envelope. Все witness positions должны лежать в измеренных bounds.

Все expected values и derived tolerances должны быть конечными; overflow/
underflow scale powers/scalars отказывает. Хотя бы одно позиционное/bounds/
dimensioned свидетельство должно отличать requested motion от no change при
данных допусках. Если factor слишком близок к1 или evidence вырождено в pivot,
состояние остаётся непроверенным; успешное изменение не выводится по одному
факту возвращения native команды.

Identity/descriptor/order/dimension drift отклоняется. Autodesk предупреждает
о преобразовании primitive surfaces/solids в power surfaces/general solids:
native remapping после изменения требует отдельного доказательства. Этот
reference predicate не принимает изменение ключей автоматически.

## Границы и источник

Совпадение samples/bounds/measures не доказывает полную эквивалентность геометрии,
топологию, принадлежность экспортированного файла или стабильность параметризации.
Необходимы квалифицированный native provider, owned plan/executor/receipt,
recovery/no-replay, independent saved-result/reopen, SDK/transport и live matrix.
DGK/PSMODEL остаются CAD-форматами; DMT лишь дополнительным mesh witness.

[Autodesk PowerShape 2023:Uniform scaling](https://help.autodesk.com/view/PWRS/2023/ENU/?guid=GUID-F1C47DA1-CD1D-4DA1-AC0E-9E85B95BC7D5)
подтверждает равномерное изменение по осям, origin control и primitive conversion.
Математические scale powers и tolerance rules здесь — наш reference contract,
не документированное обещание поведения всех native getters.
