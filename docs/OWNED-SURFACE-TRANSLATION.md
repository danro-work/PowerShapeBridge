# Owned SURFACE translation (experimental)

Native 0.53 / SDK 0.59 add two full-only tools, gated separately by
`ownedSurfaceTranslationsExperimental` (public configuration defaults to false).
The flag requires owned translation and passive surface sampling dependencies.
The Python compatibility facade does not expose these new recipes. Read-only
keeps passive sampling; it cannot prepare or execute this geometry operation.

Use existing candidate → explicit entity set → disabled `translate_entities`
plan → owned working-document binding → coupled owned operation preparation.
`execute_owned_surface_translation(ownedOperationHandle,surfaceGrid)` consumes
the connection's CAD leases before submission. The immutable coupled plan fixes
the single SURFACE target and WORLD vector; the native execution plan additionally
freezes the explicit grid and all before-witnesses before any callback.
`verify_owned_surface_translation` takes the original execution ID/SHA and fresh
candidate/active listing choices. It issues no geometry command.

The additive provider supports exactly one explicit target in a model containing
1..8 SURFACEs only, resolved mm units, WORLD, idle document, empty selection,
unique surface names and a safe ASCII target name. The exact six grid fields are
tStart,tEnd,tDivisions,uStart,uEnd,uDivisions; ranges increase, divisions1..4.
This grid is applied to every surface; its valid domain must be supplied explicitly.
No automatic domain inference or bounding-box fallback is performed.

The existing SOLID translation lifecycle owns the retained document/ownership
leases, immutable artifact proofs, original expiry, one-use claim, authoritative
TranslateItem return, journal, saved-file/window checks and matching fence.
An original lease reserve of at least90seconds is required before the SURFACE
callback; the lease is never extended. There is one direct TranslateItem call,
without selection, macro execution, save, OPEN, close, inverse or automatic retry.
Non-success or unknown outcomes preserve the original claim and fence. The
independent verifier needs successful original return evidence and post-return
observations. It can release only the matching fence after observed success.

For each endpoint-inclusive UV sample, target WORLD positions must equal the
original plus the vector within absolute1e-6 raw mm per component. Target normals,
visibility, native array layout and every other surface's samples are preserved
exactly. Non-positional observation properties (including available raw area),
inventory, units, workplanes, selection, process/document/windows and protected
saved bytes must remain unchanged. Actual before/after captures are retained in
the original execution/verification receipts for independent audit.

Native bounds remain unavailable. These checks prove the declared sampled
translation and observed preservation, not full topology, trimming coverage or
equivalence of the entire continuous geometry. Save/reopen persistence is a
separate qualification row. Mixed types, multiple targets, arbitrary names,
non-WORLD frames and other SURFACE transforms remain outside this profile.
The original SOLID/direct-bounds contract and the old box-dependent Surface
adapter remain separate.

The real block on25October used six SURFACEs: one target was translated WORLD
by[+1,0,0]mm, then verified separately and checked with a public passive getter.
All12 target samples had zero component error against the declared vector;
the other five grids, all72 normals/visibility/layout witnesses, non-positional
properties,27windows and479 protected post-deployment file pins were preserved.
The six-event journal and one-use claim passed independent audit. Two protected
runtime/config pins were replaced only by the authorized deployment manifest.
A subsequent [saved-result block](SURFACE-TRANSLATION-PERSISTENCE.md) preserved
all six grids through SaveAs, sealed bytes, owned-writer finalization and one new
generic OPEN with separate verification. Ordinary read-only confirmed the active own copy,
empty selection and WORLD separately. Full regression/publication status is kept
in the current project state. Legacy base observation-read counters exclude the
additional reads inside the surface witness capture; they are not total COM counts.

The isolated
`--surface-translation-test` exercises fake contexts only and requires its fixture
marker. It never qualifies the real PowerShape process.
