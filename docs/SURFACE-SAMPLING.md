# Passive surface samples through MCP

Native 0.51.2-dev / staged SDK 0.57.0-dev introduce the experimental
`inspect_surface_samples` tool. It is available in both full and readonly modes
only when `surfaceSamplingExperimental` and passive entity candidates are enabled.
The public profile keeps the new flag false. The legacy Python catalog and the
ordinary Codex SDK 0.48 connection do not acquire this tool automatically.

## Workflow and result

1. Read `inspect_entity_candidates` in the same connection. Choose an explicit
   SURFACE candidate belonging to that exact candidate document.
2. Supply its two candidate handles and an explicit, previously established UV
   range. The grid has exactly six fields: `tStart`, `tEnd`, `tDivisions`,
   `uStart`, `uEnd`, `uDivisions`. Divisions are integers 1 through 32; finite
   parameter limits must increase and remain within ±1,000,000.
3. Read positions, normals, visibility, observed document units, original receipt
   hash and before/after context. Points retain native reported order. This tool
   issues no mutation handles and does not infer a parameter domain.

The retained reader requires WORLD, empty selection and an idle document. It
performs two passive getters and checks exact sample/layout agreement plus
unchanged document identity, inventory, process and windows. It never clears
selection, switches workplanes, selects entities, activates documents, saves,
captures an image or calls a bounding-box fallback. The SDK independently checks
the immutable original response, its pinned source inspection and returned arrays.

## Real qualification on 25 October 2026

The owned six-planar-SURFACE copy remained active throughout the block, with all
27 windows preserved. Surface 21/2, name `1`, was read over the fixture's explicit
UV [1,2] × [1,2]. A second division-count profile established 2 divisions → 3
points per axis. The final staged readonly MCP call used 2 × 3 divisions and
returned 12 positions, 12 normals and 12 true visibility flags.

Real COM arrays differ from ordinary synthetic vectors:

| Value | Native dimensions | Lower bounds |
|---|---|---|
| Response | 4 | 1 |
| Positions / normals | 3 × 4 × 3 | 1, 1, 1 |
| Visibility | 3 × 4 | 1, 1 |

The decoder accepts exact endpoint-grid dimensions, preserves layout metadata,
and enumerates native scalar order into coordinate triples. Swapped dimensions,
old division-product counts, nonfinite coordinates, non-BOOL visibility and
inconsistent repeat reads are rejected. The local number conversion preserves
the original doubles; it does not round through the older text conversion helper.

Two initial outcomes remain preserved: the first reader rejected the COM rank;
the next Native read succeeded but the SDK correctly rejected rounded decoded
coordinates. Each fix was tested before a new live read. No original response was
rewritten and no CAD mutation was attempted. Three installations each used a new
backup and one verified panel restart, with an idle queue.

An independent audit checked the original sample receipt, exact decoded/raw
numbers, normals, visibility, unchanged context/windows and 465 protected file
pins. Four pinned runtime/config substitutions across the three deployments
matched their authorized manifests; other protected files remained unchanged.
An ordinary readonly connection independently confirmed the active six-SURFACE
model in WORLD with empty selection after the block.

## Limits

`boundsWorldRawPaired` is null and `boundsStatus` explicitly reports unavailable
bounds. A sample envelope is not a successful native box or full geometry proof.
The result does not certify trimming coverage, arbitrary UV domains, topology,
general surface selection or transformations. Raw area units remain unknown.
The old box-dependent `RotationMeasurements.Surface` adapter remains unchanged
and unqualified for this source; the SOLID provider is unchanged.

Local qualification: 14 Native shape/precision/context cases, 12 targeted SDK
cases, 18 Python configuration cases, catalog readiness 68 full / 9 readonly,
then the real MCP block and independent audit. The subsequent full .NET suite passed 1033/1033 with no failures or skips;
21 strict-selection and 28 existing measurement cases also passed. Exact-head
CI is recorded in the local publication state. Native tests run in an
isolated fake context through `verify-surface-sampling.ps1`; CI never contacts CAD.
K8, K10 and K11 remain open.

The first CI run exposed equal coarse Windows clock ticks in the existing owned
selection fixture. Its synthetic verification reads now use a logical timestamp
strictly after the recorded successful callback return, with an explicit tie
control. Unknown-return cases retain their rejection path. Production chronology
guards, receipts and CAD behavior are unchanged; the 10 lifecycle cases passed
after this test-only fix.
