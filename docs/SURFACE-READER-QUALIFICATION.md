# Six-surface source: real reader observations

On 25 October 2026 (Moscow), the existing staged SDK 0.56 / Native 0.50
created one owned saved-byte copy of a closed historical test result, opened it
once and independently verified that OPEN. The new copy contains six planar
SURFACEs. Existing 26 windows and saved files were preserved; there are now 27
windows. No geometry, selection, Save, close, mouse or deployment operations were
performed. The current closed source bytes were pinned separately; an older
historical SaveAs fingerprint was not recertified.

Three attach-only diagnostics preserve the original outcomes:

- `tools/ReadGuardedSurfaceSnapshots.cs` invokes the existing surface measurement
  adapter. It fails at direct bounding-box decoding before sampling.
- `tools/ReadSurfaceGetterLayout.cs` records the raw box/stored-box responses and
  a fixed [0,1] parameter request, without interpreting failures as zero geometry.
- `tools/ReadSurfaceParameterLayout.cs` checks this source's NLATS=NLONS=2 and
  records a separate explicit [1,2] parameter request. That domain is qualified
  for this fixture only; it is not inferred for arbitrary surfaces.

These are standalone diagnostics, not MCP tools or mutation authority. They
require a pinned Native assembly, fresh observation/listing and matching local
general configuration. They use the retained context and preserve files/windows.
Run each output once; retain failures and raw responses. Never replay the original
OPEN to obtain fresh access.

## Observations and limits

For all six surfaces in this case:

| Getter / request | Observed result |
|---|---|
| `GetItemBoundingBox(composite, false)` | Status −2, followed by six zeros; unavailable, not a valid box |
| `get_stored_bounding_box(composite)` | Status 0, followed by zeros; unavailable |
| `GetItemBoundingBoxFromWP(composite, 0)` | Status 0, followed by zeros; unavailable |
| `GetSurfacePoints`, [0,1] × [0,1] | Status −3 and null arrays: invalid parameter limits |
| `GetSurfacePoints`, [1,2] × [1,2], divisions 3 × 3 | Status 0; 16 positions, 16 normals and 16 true visibility flags |

Installed Autodesk 2023 OLE help documents box success as 1 and surface-point
success as 0. Its surface-grid size comment does not match this observed
3-divisions/4-points-per-axis response. The existing adapter expects a successful
box and `tCount*uCount` points. Consequently it remains unqualified for these
surfaces. No production guard or decoder was relaxed.

An independent numeric/file/window/journal audit confirmed the 96 sampled
positions and 96 normals, the expected planar fixture envelopes and outward
unit normals, raw area consistency, owned source/working/checkpoint identities,
the terminal OPEN journal and all protected files. A sampled envelope is explicitly
not a successful native bounding box or a full topology certificate. Raw area
units remain unknown and are not reported as mm².

The official Autodesk C# wrapper's entity bounding-box property changes selection.
It therefore cannot be substituted into the separate readonly server.

## Next implementation boundary

First qualify the division/point-count convention with a second explicit count,
then introduce a passive surface-grid result with parameter provenance,
positions/normals/visibility and explicit unavailable native bounds. Keep the
existing SOLID provider and strict box failure behavior intact. Surface transforms
and generalized selection require their own before/after and saved-result
qualification; these observations alone do not accept K8, K10 or K11.

The production runtime/config and ordinary SDK 0.48 were unchanged. Full .NET
1021/1021 and both PR87 checks remain applicable; no unchanged local full suite or
previous CAD scenario was repeated for this diagnostic block.
