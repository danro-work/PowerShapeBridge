# Passive observation after an all-returned SURFACE-set worker timeout

An ordered rotation can return from every native step and publish every observed
step, yet lose the final worker receipt to the panel timeout. The original
timeout remains authoritative as an incomplete execution result. Repeating the
rotation would apply it twice.

Native 0.73 and SDK 0.79.1 add the default-off, full-server-only
`returnedSurfaceSetObservationExperimental` gate and
`observe_returned_surface_set_rotation`. Readonly mode excludes this diagnostic.
Registration is explicit in `ToolCatalog.Create` through
`ReturnedSurfaceSetObservationTools.Create`.

The observer requires the exact original timeout and frozen schema9 plan hashes,
matching ownership, claim and mutation fence, four original journal events ending
in `command_returned`, and complete ordered started/returned/observed step proofs.
It also requires the original successful generic OPEN verification of this copy.
Missing returns, partial steps, altered chains, changed context or saved bytes
are rejected. Ordinary execution and journal-verification admission stay strict.

The timeout lost the executor's in-memory saved-byte receipt. The observer does
not invent one: it checks all current saved files against the historical verified
OPEN baseline plus the owned working file. This stronger historical precondition
must be satisfied explicitly. Native and SDK independently validate the source
histories, geometry and retained context; Native reads all surface witnesses twice.

The report grants no binding or edit rights. It changes no CAD geometry, selection,
activation, files or original journal, and leaves the matching fence in place.
It does not resolve the original outcome. A separately qualified reconciliation
and verifier remain necessary before releasing that fence.

The real two-target case passed retained reads and independent public readings
of all six surfaces: 72 positions and normal directions, four exact controls,
unchanged window inventory and source files. Sampled witnesses do not prove full
topological equivalence. No second rotation or save was performed.

The first public call found two SHA fields described as 32-character handles;
it was rejected before IPC. SDK 0.79.1 fixes those fields to 64-character hashes
and adds a schema regression check. The original rejected call is retained.

Synthetic checks run only in marked isolated fixtures without COM or CAD.
`verify-returned-surface-set-observation.ps1` checks Native refusal and corruption
cases; its `-DotnetPath` option checks the same Framework artifacts independently
with the SDK. Both CI workflows include this runner. K8–K11 remain open.
