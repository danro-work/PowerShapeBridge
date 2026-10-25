# One owned SURFACE selection with sampled witnesses

Native 0.52 / staged SDK 0.58 add a separately enabled SURFACE profile to the
existing three full-server selection tools. The original sole-SOLID profile,
its schema 1 packets and direct bounds remain supported without new parameters.
The full/readonly catalogs remain 68/9 with the current local feature flags.
Readonly has no selection preparation, execution or verification tools.

`ownedSurfaceSelectionsExperimental` defaults to false in the public profile.
Enabling it requires `ownedSelectionsExperimental`, `surfaceSamplingExperimental`
and their ownership/candidate/binding dependencies as true JSON booleans. The
legacy Python adapter does not expose the new modes. The ordinary Codex SDK 0.48
connection was not updated; qualification used the staged 0.58 host.

## Explicit modes and measurement boundary

`prepare_owned_selection` accepts two additional exact parameter objects:

```json
{
  "mode": "select_surface_single",
  "candidateEntityHandle": "<fresh same-binding SURFACE candidate>",
  "grid": {
    "tStart": 1, "tEnd": 2, "tDivisions": 2,
    "uStart": 1, "uEnd": 2, "uDivisions": 3
  }
}
```

`clear_surface_single` uses the same fields and requires exactly that surface
selected. Both modes require an owned working-document binding and an explicit
candidate from its original inspection. No raw native ID, command or path can
replace these handles. UV limits must already be known for every retained
surface; the example domain is qualified for the planar fixture only.

The new provider supports one to eight SURFACEs in mm/WORLD and zero/one stable
selection. The target name uses the existing safe ASCII grammar. It rejects
mixed entity types, multiple selection, duplicate names, no-op requests, invalid
domains and more than four divisions per axis. The capture is limited to 32 KiB.
Schema 2 explicitly distinguishes the sampled profile from the old SOLID packet.

For each retained surface, capture preserves finite point/normal triples,
visibility, exact endpoint-grid shape and native array layout. Numeric comparison
preserves original doubles, including changes smaller than the old string
conversion retained. Native bounds remain null with an explicit unavailable
status. Sample envelopes do not replace boxes or certify complete geometry.
The public passive sampling tool keeps its WORLD/empty-selection guard; selected
state witnesses are read only inside the retained owned-operation context.

## Execution and independent verification

The existing immutable preparation, once-claim, matching copy fence, original
callback-return receipt and append-only journal are reused. A fixed program
selects the explicit SURFACE or clears the already verified sole selection.
There is one callback per consumed intent. Unknown outcomes preserve evidence and
the fence; there is no automatic replay or compensating clear.

Positive verification uses unchanged strict GetSelectedItems/GetModelItems/
GetItemData checks. Empty verification uses a separate double count-zero proof
and a new inventory-only getter check; null selected-items data is not inferred
empty. The verifier checks unchanged samples of all retained surfaces, observed
properties, saved bytes and windows, then releases only the matching fence.
The existing original OPEN verifier can refresh access to the same owned copy
while selected; no additional OPEN is issued.

## Real qualification, 25 October 2026

On an owned copy containing six planar SURFACEs, staged MCP selected surface `1`,
independently verified it, cleared selection and independently verified again.
Each stage had one original callback and a complete six-event terminal journal.
The resulting selection counts were 0 → 1 → 0. All 27 windows and available
observed geometry/properties remained unchanged.

An independent audit compared 72 point and 72 normal witnesses, visibility and
array layout across both operations; all matched exactly. It checked immutable
receipts/claims/journal chains, 473 protected pins and matching fence release.
Two pinned runtime/config substitutions matched the deployment manifest; other
protected files remained unchanged. A separate ordinary readonly connection
confirmed the six-surface model, WORLD, empty selection and 27 windows afterward.
No geometry, SaveAs, OPEN, close, screenshot or mouse operation was performed.

Before live qualification, Native 9 surface cases, the old 10 SOLID lifecycle
cases, 31 targeted SDK cases, 19 Python configuration cases and catalog readiness
passed. After the real block the full .NET suite passed 1042/1042 with no failures or
skips, the new isolated Native CI runner passed 9 cases and unchanged strict
selection reading passed 21 cases. Exact-head publication CI is recorded locally. The isolated Native CI runner never connects to CAD.

This qualifies one explicit surface among a known planar set. General mixed/
multiple selection, trimmed surfaces, arbitrary UV discovery, SURFACE transforms,
topology equivalence and K8–K11 acceptance remain separate work.
