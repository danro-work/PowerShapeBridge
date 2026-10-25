# Owned SURFACE integer WORLD rotation (experimental)

Native 0.55 / staged SDK 0.61 extend the existing two full-only SURFACE rotation
tools. The same default-false flag, explicit registration, original rotation plan,
owned binding and one-call lifecycle are retained. Read-only still exposes no
executor or verifier. Ordinary Codex SDK 0.48 has not been replaced.

The executor accepts one explicit SURFACE among 1..8 SURFACEs, resolved raw mm,
WORLD, empty selection and an explicit known UV grid with at most four divisions
per axis. The source plan declares an integer angle with absolute value at most
1000000, excluding multiples of 360, a finite WORLD pivot bounded to 1000000 per
component and a nonzero direction. Strings, fractional/no-op/out-of-range angles
are rejected before the callback. Mixed types and multiple targets remain outside
this executor. The wider numerical input range is not a claim of real acceptance
for every angle, pivot, surface shape or topology.

Original schema3 receipts with `owned_surface_grid_world_z90_v1` remain verifiable
under their exact +90/origin/WORLD-Z restriction. New non-Z90 definitions use the
distinct `owned_surface_grid_world_integer_v2` witness profile. The frozen execution
angle, pivot and normalized axis are tied to the original disabled `rotate_entities`
plan and ownedrotations packet. There is no translation-plan substitute or second
safety executor. The original claim, copy fence, journal and independent verifier
remain authoritative; an unknown outcome never triggers replay or an inverse.

Native positions rotate around the declared pivot; normal directions rotate
without pivot translation. SDK verifies using its separate RotationGeometry
predicate. All target components must match within 2e-6 absolute (positions in
resolved raw mm). Other surfaces, visibility, array layout, observed non-positional
properties, saved bytes and windows must remain exact. Native bounds are unavailable
and are not fabricated from sample envelopes.

Framework JSON round trips can alter a final binary digit of a normalized axis
component. Native and SDK use the existing 1e-12 normalization tolerance for unit
direction checks against the source definition; pivot and angle remain exact.
Both artifacts retain independent SHA checks. Controls reject rehashed execution
artifacts with altered angle, pivot, direction or a mismatched witness profile.

Real qualification on 25 October: exactly one SURFACE among six rotated -30 degrees
around direction [1,2,3] through WORLD [2,-3,4], followed by a separate verifier and
additional passive target getter. Independent Python Rodrigues calculations from
the declared parameters found maximum position error 3.552713678800501e-15 mm and
normal-component error 1.249000902703301e-16 across 12 positions and 12 normals.
The other five grids, visibility, layout, properties, 559 post-deployment protected
pins and all 27 windows were preserved. Deployment substitutions were checked
against the authorized backup manifest. The six-event journal, one-use rotation
claim and matching fence release passed independent audit. Ordinary read-only
separately confirmed the same owned copy with six SURFACEs and empty WORLD.

This geometry block performed no save, OPEN, close, selection, mouse interaction,
inverse or command replay. Its result is currently unsaved; persistence requires
a separate qualification without repeating the rotation. Sources and checkpoints
remain protected. This establishes sampled behavior for the stated real case,
not full continuous geometry/topology or completion of K8-K11.

Targeted checks passed before CAD: 16 Native angle/profile/lifecycle controls,
8 existing SURFACE translation and 34 SOLID translation controls, 38 SDK cases
and isolated STDIO catalogs 72 full / 9 read-only with no Native queue. After CAD,
the fresh CI runner passed 16 Native cases; actual Framework-to-SDK proof controls
passed 20 cases, including valid mirrored artifacts before each source forgery.
The full .NET run passed 1051/1052; one File.Replace report test was denied by
the sandbox and passed its single elevated rerun. All 1052 cases therefore passed
across the original run and that scoped rerun, with original failure evidence
retained. The other 1051 cases were not repeated. Exact-head CI is recorded in
current project state.
Original live SDK bytes are retained separately; the final staged package only
clarifies the prepare_rotation_plan description, with unchanged execution logic.
