# New SURFACE set translation with numerical normal checks

This is a new schema8 execution profile, not a relaxation of historical schema7.
The original strict set and its verifier keep exact-normal behavior. A used old
intent remains consumed after a separately qualified partial-stop disposition.
The two profiles reject each other's execution proofs.

`ownedSurfaceSetNumericalExperimental` defaults to false and requires the strict
set and its guarded surface/ownership dependencies. The full server registers
`execute_owned_surface_set_translation_numerical` and
`verify_owned_surface_set_translation_numerical` through `ExecutionTools.Create`.
Neither tool appears in the read-only server.

The new immutable plan freezes the ordered target set, explicit UV grid and:

| Observation | Required result |
| --- | --- |
| Target sampled positions | Declared WORLD/mm translation within 1e-6 mm |
| Target normal components | Difference at most 1e-12 raw |
| Target normal length | Both original and current lengths within 1e-9 of one |
| Other surfaces and not-yet-translated targets | Exact samples and normals |
| All layouts, visibility, inventory, metadata and context | Exact |

The shared executor retains its ownership/checkpoint/file/window checks, global
lock, one-use plan claim, matching fence and authoritative per-step hash chain.
Source target normals are checked before mutation. Each native callback runs
once; a non-success, lost return or postcondition failure stops the set without
retry, inverse, continuation, selection or save. Sequential translation is not
atomic. Separate fresh verification is required before matching fence release.

Real qualification used the existing owned six-SURFACE copy: new ordered targets
2 then 3 moved WORLD [0,1,0] mm; surface 1's prior X+1 state was an exact control.
Both callbacks returned 1. Separate Native/SDK verification and all-six public
readers confirmed 72 points, unchanged normals/layouts/visibility, four exact
non-targets and 28 unchanged windows. Position and normal errors were zero in
these observations. The original abandoned set, archive, claim and journal stayed
unchanged. Geometry remains unsaved; persistence is a separate checkpoint.

The first real verifier rejected before journal append/fence release because the
Run-to-Verify adapter omitted the new profile flag. A shared `Dispatch` now carries
the same explicit profile into both execution and verification; fixtures use that
adapter. Native 0.70.1 then verified the existing result without another geometry
callback. SDK 0.76 was qualified through STDIO; ordinary SDK 0.48 is unchanged.

The immutable real journal keeps its original legacy display description; its
binding schema8 plan, step profile and result carry the exact numerical policy.
Source now gives future journal entries the set's own display description. This
text-only cleanup was compiled after the real block and required no redeployment
or repeated CAD command.

Targeted checks cover native schema/policy/stop/replay guards, actual Framework
receipts checked independently in the SDK, strict-profile regression, separate
catalog gates and no-queue refusals. Sample observations do not prove full topology
identity, universal support for every surface, or saved/reopened persistence.
