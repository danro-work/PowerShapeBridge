# К6 — original SaveAs подтверждён, SDK reopen proof исправляется

Installed native0.33.3-dev после явного разрешения. Новый отдельный client
не повторял SaveAs59f5a399...: fresh observations/listing и original raw SHA
переданы verify_owned_save_as. Receipt fb7d6f55bc794ca8a5da9bfb942ee938,
SHA2e92c56613303a74514cfb4c853765dc94abe2019a7c9dcdb17e6345820fc70a.
Native journal original SaveAs теперь succeeded_verified; fence корректно
снят verifier, original save-once claim сохранён. Original response остаётся
immutable outcome_unknown; terminal journal разрешил исход без command replay.

Native prepare46994bf3ae764ee0b0304750e89ba7e9 успешно создал readonly seed,
own Working/checkpoint с SHA362a285...; CAD-команд не выполнял. SDK0.36.3
не выдал scoped reopen handle: saved_result_proof_invalid. OPEN не вызван.
Disk-only подготовка46994... остаётся на месте; её истёкшую подготовку не открывать.

Причина воспроизведена unit regression: native verifier raw body не содержит
transport bridgeRequestId. BridgeIpc обычно добавляет его в memory, но
OwnedReopenStore при nested чтении raw proof этого не делал. Не следует
переписывать raw receipt или менять его SHA.

Source SDK0.37.3-dev: после чтения fixed file с verified SHA добавляет
transport ID только в memory из verificationRequestId; conflicting body ID
отклоняется. Positive missing-ID/unmodified-bytes и negative conflicting-ID
проверки добавлены;17 focused/full296 SDK/531 native/175 artifacts0 mismatch/
STDIO/Inspector/25 fault audit passed;134 hashes matched. Four exact-source
e3e1fc5 CI38366357451/38366750598 baseline и38366357421/38366750799 SDK passed.
Actual STDIO fixture теперь воспроизводит native verifier body без envelope ID.
Native source/runtime0.33.3 и default Codex config не меняются; новая панель
не требуется. После qualification — отдельный опубликованный side SDK client,
fresh original SaveAs re-verification и NEW saved-reopen intent/copy.

Qualified side SDK0.37.3 позволил один new owned OPEN и native verifier
succeeded_verified, но выдача SDK binding отказала из-за slash format.
STATUS-K6-REOPEN-PATH.md. No replay/restart/cleanup; active own Working_d5...

evidence/K6-model-id-fix/live-qualification/ и
evidence/K6-reopen-receipt-fix/red/focused/sdk-full. К6 ещё не закрыт.
