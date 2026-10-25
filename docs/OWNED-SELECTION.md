# Owned selection: bounded K8 provider

The subsequent separately gated SURFACE profile is documented in
[OWNED-SURFACE-SELECTION.md](OWNED-SURFACE-SELECTION.md). The sole-SOLID provider
and its schema 1/direct-bounds contract below remain supported.

Native 0.50 / staged .NET host 0.56 add three full-server tools:
`prepare_owned_selection`, `execute_owned_selection`, `verify_owned_selection`.
`ownedSelectionsExperimental` defaults to false in the public profile. Read-only
keeps its separate eight-tool catalog and cannot change selection.

The first provider accepts an owned working copy containing exactly one SOLID,
millimetre units, WORLD and a body name containing 1–64 ASCII letters, digits or
underscores. `select_single` requires empty selection and a fresh entity candidate
from the binding's original inspection. `clear` requires that sole body selected.
Other types, multiple objects, subentities and requests already in the target
state are rejected. There is no raw command argument or mouse automation.

Preparation changes no CAD context. Execution consumes its connection-local
intent and CAD leases before IPC; Native independently validates the original
packet, ownership, saved bytes, current inventory, context and window digest.
One frozen once-claim and matching mutation fence precede one fixed callback.
The fence stays until a separate verifier checks the original command return,
fresh observation/listing and exact postcondition. An unknown result is never
replayed and never triggers automatic clear. A callback without durable return
proof cannot be declared successful from count alone.

Positive selection uses the unchanged strict native selected-items reader:
GetSelectedItems, GetModelItems and GetItemData must agree on composite ID, type,
native ID, name and level across fresh reads. Empty selection uses a distinct
explicit double SELECTION.NUMBER=0 proof with current sole-body native mapping.
A null GetSelectedItems result remains a failure; it is never interpreted as
empty by that reader.

Only selection-dependent fields may change. Observed geometry/properties,
direct body bounds, WORLD, saved files, owned checkpoint and all windows must
remain unchanged. This proves the measured scope, not full topology equivalence.
After successful verification the existing original OPEN verifier can refresh
owned-copy access from the current selected observation, followed by a new
binding. This does not issue another OPEN or introduce a general adopt tool.

Qualification status and actual command evidence are recorded in PROJECT-STATE
and the local continuation map. This provider alone does not close K8 or K11.

## Qualification completed on 25 October 2026 (Moscow)

The staged SDK completed one select, independent verification, one clear and
independent verification on the existing owned SOLID copy. Original OPEN
verification refreshed access while selected without opening another window.
A separate readonly connection and file/journal audit confirmed empty selection,
unchanged measured geometry/direct bounds and all 26 windows. The 421 file pins
remained unchanged through the CAD block; authorized runtime/config replacements
were accounted for separately during deployment. No geometry/save/OPEN/close or
mouse commands were issued. Native lifecycle 10, unchanged strict reader 21,
SDK targeted 10, Python configuration 17 and full .NET 1021/1021 passed. The
ordinary client remains on SDK 0.48; the new tools are qualified in staging.
