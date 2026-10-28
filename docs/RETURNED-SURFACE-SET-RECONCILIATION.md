# Separate reconciliation for a fully returned SURFACE-set rotation

Source/staging Native0.74 and SDK0.80 add two tools behind the default-off,
full-server-only `returnedSurfaceSetReconciliationExperimental` gate:

- `reconcile_returned_surface_set_rotation` acknowledges a qualified passive
  observer, independently reads current geometry/context/files, and archives the
  exact original fence once under a separate claim, secondary fence and journal.
- `verify_returned_surface_set_reconciliation` requires fresh post-archive
  inspection/listing, rechecks all surfaces and saved-file baselines, validates
  the separate archive journal and original immutable proofs, then can publish an
  explicit resolution and release only the two matching fences.

Neither tool calls a geometry writer, saves, opens, activates or selects CAD
objects. Neither issues binding or edit leases. Original timeout, four CAD
journal events, six step files and execution claim remain unchanged. A new
`returned_rotation_observed` resolution is distinct from the original receipt
and is not a full topology-equivalence claim.

Registration is explicit: `ToolCatalog.Create` calls
`ReturnedSurfaceSetReconciliationTools.Create`. Both tools are excluded from
readonly mode. Every artifact hash has an explicit 64-character schema.

The SDK's archived-fence source option is internal to terminal proof validation.
It requires the original execution, owned copy and exact archive hash; the
default observer source continues to require the original live fence. Both
logical source pin maps must agree. Partial archive failures retain claims and
fences and report unknown; the original CAD operation is never retried.

## Qualification boundary

The staged package passed targeted Native and independent SDK fixture checks,
public schema/mode/refusal checks, strict configuration checks and STDIO readiness.
Fake contexts cannot enter the Native release branch.

**This feature has not passed a real CAD reconciliation. It is not installed.**
Preflight stopped before MCP/COM submission: the prior PowerShape process had
closed and 23 saved CAD files differed from the previous protected snapshot,
including the fenced working file. The cause of those byte changes is not
established; file hashes alone do not prove geometry changes or preservation.
The original timeout/journal/steps/claim/fence remain unchanged. Current changed
bytes were copied to separately marked, readonly, geometry-unqualified local
snapshots without replacing the older proof baseline.

Restarting PowerShape does not restore the old process identity, 28-window
context or original saved-byte baseline. The old reconciliation scenario must
not be replayed, and guards must not be weakened to accept it. Continue through
a separately specified new-session route on new owned copies. Do not ask the
user to reconstruct the old 28 windows. Ordinary installed Native0.73/SDK0.48
and runtime configuration remain unchanged.
