# Owned SURFACE WORLD Z90 rotation (experimental)

Native0.54/stagedSDK0.60 add two full-only tools behind the separate default-false
`ownedSurfaceRotationsExperimental` flag. It requires owned rotation preparation
and passive surface sampling dependencies. The Python compatibility facade does
not expose these new recipes. Read-only keeps passive getters only.

Use existing candidate → explicit entity set → `prepare_rotation_plan` → fresh
owned binding → `prepare_owned_rotation`. These preparations retain the original
disabled rotate_entities plan, ownedrotations/rotationplans artifact namespaces,
exact source proofs, target, WORLD angle/axis and original lease. They are not
translation plans. `execute_owned_surface_rotation(ownedRotationHandle,surfaceGrid)`
consumes CAD leases before submission; the original rotation intent is protected
by its one-use claim. `verify_owned_surface_rotation` takes the original request
ID/SHA and fresh candidate/active listing choices; it issues no geometry command.

The qualified bounded profile is exactly +90integer degrees about WORLDZ through
[0,0,0], one explicit SURFACE among1..8SURFACEs, rawmm/factor1, idle document,
WORLD/empty selection, unique names and safe ASCII target name. Other angles,
pivots, axes, types, mixed/multiple targets are rejected before the callback.
All surfaces use the supplied exact tStart,tEnd,tDivisions,uStart,uEnd,uDivisions;
increasing explicit ranges, divisions1..4. There is no UV-domain inference.

OwnedTransformSource adapts the existing rotation packet/dependencies into the
existing single-call ownership/journal/claim/fence lifecycle. It changes the
preparation source and witness/callback strategy, without copying a second safety
executor. Original SOLID and SURFACE translation schemas/paths remain strict.
The new execution schema3 pins the original rotation packet/plan, grid, precise
before-capture, angle/axis, copy/files/windows and measurement profile
owned_surface_grid_world_z90_v1. One direct RotateItem must return1; successful
same-worker checks remain executed_unverified/fenced until separate verification.
Unknown/non-success never triggers replay, inverse, selection, save or automatic
fence repair. Before the callback at least90seconds of the original lease remain.

Target sampled positions and normal direction vectors must follow the declared
rotation within absolute2e-6 per component (positions in resolved rawmm). Other
five grids, visibility, native array layout, identity, observed non-positional
properties/raw area, units/context/windows/saved bytes are preserved exactly.
Actual before/after captures stay in immutable original receipts. Native bounds
are explicitly unavailable. Old box-dependent Surface measurement/verification
and SOLID bounds checks are not weakened or supplied fabricated sample boxes.

Registration is explicit in the existing STDIO server:
`Program: .WithTools(ToolCatalog.Create(session));`
`ToolCatalog: ..OwnedSurfaceRotationTools.Create(session)`.
MCP packages remain pinned to2.2.0 with.NET10; no transport/dependency migration.

Real qualification on25October: one surface among six rotated WORLDZ+90, separate
native verifier and additional public passive target getter passed. An independent
Python oracle used the signed permutation[-y,x,z] for both positions and normals;
all12+12 target witnesses had zero component error. Other five grids, visibility/
layout/properties,515post-deployment pins and27windows were preserved. Only two
protected runtime/config pins changed through the authorized deployment manifest.
The original six-event journal, one-shot rotation claim and matching fence release
passed independent audit; ordinary read-only confirmed the same own copy, six
SURFACEs and emptyWORLD separately. No selection/save/OPEN/close/mouse/inverse.
The rotated geometry is still UNSAVED; persistence is a subsequent qualification.

Before real CAD: Native9rotation +8priorSURFACE translation +34SOLID translation
controls,63targeted SDK tests,6actual Framework/SDK proof controls,21Python config
tests and isolated STDIO catalogs72full/9readonly. After real CAD the full.NET suite passed1052/1052 without failures/skips, and
the new isolated CI runner passed9Native plus6Framework/SDK proof cases. Exact-head
publication CI is recorded in current project state.
The CI runner uses marked fake contexts only and does not qualify real CAD.

This establishes the declared sampled Z90 rotation and observed preservation,
not full topology, trimming coverage, continuous-surface equivalence or the
complete type/operation/five-class acceptance matrix. K8–K11 remain open.
