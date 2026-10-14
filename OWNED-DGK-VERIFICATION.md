# Независимая диагностика результата DGK

`OwnedDgkExportVerification` — внутренний getter-only verifier. Его IContext
содержит только observations/windows/retained-document check, settings snapshot,
native mapping/selection keys и busy getter. Экспорт, выделение, активация,
сохранение, закрытие или fence release через этот интерфейс невозможны.

## Доказательства и свежий контекст

Вход —9 точных полей: executionRequestId/executionReceiptSha256,
inspectionRequestId/inspectionSha256,listingRequestId/listingSha256,
windowNumber/modelId/expiresUtc. Вход detached до внешних чтений.

Проверяются frozen execution plan и complete executed_unverified receipt,
original target/set/binding/owner/checkpoint/current working bytes, полный journal
chain/aggregate return, own once claim/fence, summary и все started/returned
steps. Точные schemas/order/ID/SHA/target/path исключают подмену команды или
чужой шаг. Native provider/return-code qualifications остаются false.

Новые inspection и listing должны быть строго после aggregate command_returned,
с собственным актуальным lifetime и ожидаемым active document. Future listing,
равная/старая inspection, чужая window/model choice и изменённые properties/units
отвергаются. В retained context выполняются четыре observations — два до
открытия bundle и два при удерживаемых файлах. Проверяются world/empty selection,
idle status, exact native selected keys, mapping/settings/windows и saved hashes.

Historical TargetLease допускает истёкшие исходные refs только на момент их
подготовки, как доказательства. Recheck execution для неё всегда отвергается;
она не продлевает или оживляет исходное право команды. Fresh read expiry
проверяется отдельно по текущему времени. Mutation/export fences не разрешаются.

## Комплект и результат

Весь допустимый bundle повторно открыт с retained directory/file handles.
Membership, native identity, SHA/length и DMT decoding должны совпасть с
original summary. Timestamp нового чтения отдельно обновлён; исходное bundle
наблюдение должно быть не позже aggregate return. MAC/XML остаются opaque и
никогда не исполняются; DGK header/топология здесь не декодируются.

Успешная диагностика — `observed_dgk_bundle_context_match_unqualified` с отдельным
immutable diagnostic SHA. Original journal остаётся executed_unverified,
claim/fence сохранены. GeometryVerified/nativeProviderQualified/copyFenceReleased
false. Это совпадение файлов и наблюдаемого контекста, не full CAD equivalence,
не разрешение повтора и не завершение экспорта v1. Unknown export receipt
с неполной returned chain отвергается, даже если файл уже существует.

## Проверки и следующий шаг

28 Framework cases:4 controls/24 refusals, включая mixed set, companions и
истёкшую historical target при свежем независимом чтении. Python audit987 hashes
проверяет bytes/identity/diagnostic/source/journal chronology и неизменность
original files/fence/claims. Runner включён в SDK CI/source hash manifest.

Далее production COM provider/settings/selection layout, SDK exposure/full-mode
guards и отдельно согласованный живой DGK export/import/geometry roundtrip.
Матрица моделей К10 и пользовательская приёмка К11 остаются обязательными.
