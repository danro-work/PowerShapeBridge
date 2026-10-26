# Owned COMPCURVE principal-axis rotations

Native0.61/stagedSDK0.67 extend the same full-only tools/default-false flag with
`owned_compcurve_controls_world_principal_v2`, schema6/operation
`rotate_owned_compcurve_principal`. Only signed90/180 degrees around unit principal
X/Y/Z, including negative axis directions, and explicit WORLD pivot coordinates
bounded to±1e6 are admitted. Original Z+90/origin intent keeps schema5/v1 and its
strict definition. No new public tools, flags, queue route or safety executor.

Positions rotate around the pivot; both tangent directions rotate about zero.
Existing shared RotationSampleVerifier.Transform/ExactBounds/Envelope predicates
are reused. Signed permutations have exact extrema mapping; observed curve
minXYZ/maxXYZ are explicitly converted to paired bounds for Envelope. No general
curve bound is invented from controls, and arbitrary/oblique/fractional angles
remain unsupported for this strategy. Full curve controls and unchanged pinned
SOLID witnesses retain the prior layout/magnitude/Boolean/property policy.
SDK verifies the frozen source plan's angle, origin, unit direction, profile,
schema, operation and native axis in addition to the original lifecycle proofs.

Real CAD qualification now includes old Z90 and one Y−90 aroundWORLD[2,−3,4].
Exactly one RotateItem, separate verifier/passive reader and independent Python
point[6-z,y,2+x]/direction[-dz,dy,dx] oracle passed. Max errors3.56e-15mm/6.13e-17;
curve rangesmin[-16.2,-9,-10],max[-16.2,11,10]. Magnitudes/16flags, neighboring
SOLID samples/COG/bounds/rawmetrics,807pins and28windows preserved. Ordinary
readonly confirmed. Geometry is UNSAVED at this checkpoint; no new SaveAs/OPEN/
close/selection/mouse. Runtime backup/onepanelrestart; ordinarySDK48 unchanged.
X/half/negativeaxis cases are fixture-verified, not additional live qualification.
Full.NET1088/1088 fail0skip0; Native15/actualFrameworkSDK23/targetedSDK25/readiness
81full11readonly passed, with post-CAD runner15+23. Initial readiness caught an
advertised-version mismatch beforeCAD; source constant corrected and new fixture
passed. Control/sample proof does not certify full topology. K8–K11 remain open.

## Original Z90 profile and qualification

Native 0.60 / staged SDK 0.66 add `execute_owned_curve_rotation` and
`verify_owned_curve_rotation`. The two tools exist only on the full server,
behind separate default-false `ownedCurveRotationsExperimental`, requiring
owned rotation plans and passive curve/SOLID witnesses. Ordinary Codex SDK48
is unchanged. The passive witness reader remains available in read-only mode.

The bounded `owned_compcurve_controls_world_z90_v1` profile supports one explicit
COMPCURVE and one unchanged neighboring SOLID in an owned, idle WORLD/mm/factor1
copy with empty selection. Only +90 degrees around WORLD Z through origin is
qualified. The caller supplies complete explicit controls `1..N`, N=2..32;
`POINT.NUMBER` independently confirms completeness. Native positions and tangent
directions are actual `[N,3]` arrays; magnitudes and Boolean flags are `[N]`,
all with native lower bound 1. The old flat/3*N curve adapter is unchanged.

This strategy reuses the original disabled `rotate_entities` plan, owned rotation
binding and packet, `rotationplans` one-use claim, and existing retained-context
`OwnedTranslation` executor, fence and journal. Schema5 freezes rotation intent.
The SDK uses the existing `ExecutionTools` path and independently checks original
receipt bytes/SHA, intent dependencies, claim, journal, matching fence and geometry
postcondition. Exactly one direct `RotateItem` is issued. Return1 remains
executed_unverified until the separate fresh verifier succeeds. Unknown results
retain their original evidence and cannot trigger replay, inverse or fence repair.

Every control position and both tangent directions must map `[x,y,z]` to
`[-y,x,z]` within 1e-6 per component. Magnitudes, free flags, count, layout,
identity and other observed properties remain equal. The principal quarter turn
must also permute observed native min/max ranges exactly as axis extrema require.
This is a check on native observed ranges, not a general bound derived from
sampled points. The neighboring SOLID retains eight pinned face-name/UV positions
and normals, COG, native WORLD bounds and available raw invariants. Curve areaRaw
has unresolved units and is not described as mm². Controls and SOLID samples do
not certify full continuous geometry or topology.

On 26 October one rotation passed through actual staged SDK STDIO, the installed
worker, separate verifier and an additional passive combined reader. Independent
Python checked all four square controls (maximum component error 8.89e-16 mm),
eight tangent directions (6.13e-17), magnitudes, sixteen flags, unchanged SOLID
witnesses, original one-use claim and six-state journal. Native curve ranges were
min `[-12,-9,22.2]`, max `[8,11,22.2]`. All 751 protected pins and 28 windows were
preserved after accounting for the backed-up runtime/config deployment. Ordinary
read-only Codex independently confirmed the owncopy, empty selection and WORLD.

The rotated geometry is **not saved** at this checkpoint. No new OPEN, SaveAs,
close, selection or mouse interaction occurred. Saving and checking a separate
reopened copy is the next independent block, with no repeated rotation. Signed,
pivoted, other-axis and oblique curve rotations, uniform curve scale, mixed sets,
the full K8 matrix, K9, five-class K10 and release/personal K11 remain open.
