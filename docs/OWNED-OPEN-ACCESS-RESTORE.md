# Restore current access after a successful owned OPEN

`restore_verified_owned_open` is a full-server-only passive observer, enabled by
the default-false `ownedOpenAccessRestoreExperimental` flag. It requires guarded
working-copy OPEN, owned copies, entity candidates and document bindings.

Inputs are the original successful generic owned OPEN verification request ID
and its exact raw SHA256, a fresh same-connection candidate, and a one-use active
listing choice. Native and SDK independently validate the original receipt,
plan, once-claim and six-event successful journal. Native retains file proofs,
source/checkpoint/clean working identities and bytes, the current COM document,
and stable current window/saved-file observations. A mutation/export fence,
stale context or wrong process/document refuses. A new short-lived copy/binding
lease is issued only after checked success; it is not execution authorization.

The original OPEN postcondition is validated against its original historical
inventory. Current access uses a new stable inventory, which may contain later
unrelated windows. The original OPEN verifier is unchanged. Nothing reopens,
activates, selects, transforms, saves, closes, releases fences or rewrites the
original journal. Saved bytes alone do not certify current in-memory topology.

Historical Native responses can omit an inline `bridgeRequestId`; IPC adds it
to the in-memory reply. The observer always requires the fixed original response
path and its exact SHA. If an inline ID exists, it must match. This compatibility
case and conflicting-inline-ID refusal are covered by the isolated fixture.

Native 0.67.1-dev / staged SDK 0.73.0-dev restored the already-open six-surface
owned copy after the window inventory changed from 27 to 28. SDK proof validation
and a separate retained-context binding check passed. All six public UV grids
matched the saved/reopened reference before the new geometry intent. Ordinary
SDK 0.48 remains installed; staged catalog is 91 full / 11 read-only.

Targeted checks: 18 Native history/context/file/refusal cases, 12 SDK gate/mode/
no-queue/schema cases, 32 Python configuration tests and three actual Framework
history/SHA checks in SDK. Staged Native also checked the actual sparse original
history before deployment. These checks do not qualify the separate ordered
surface-set transform; its first real attempt stopped as documented in
[the stop analysis](SURFACE-SET-TRANSLATION-STOP.md).
