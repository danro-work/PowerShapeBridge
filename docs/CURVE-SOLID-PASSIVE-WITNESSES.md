# Passive COMPCURVE and SOLID witnesses

`inspect_curve_translation_witnesses` (Native59/stagedSDK65) returns the complete
explicit curve controls and measurements of the one neighboring SOLID. The separate
default-false `curveWitnessesExperimental` flag requires passive curve layout.
The tool is available in full and read-only servers and produces no edit authority.

Inputs are a fresh candidate document, its COMPCURVE candidate and an explicit
complete `1..N` range, bounded 2..32. The document must contain exactly one COMPCURVE
and one SOLID, with resolved mm/factor1, WORLD, empty selection and idle state.
This bounded profile does not certify every PowerShape model or parameter domain.

The reader reuses `OwnedCurveTranslation.Capture` and `SolidFaceMeasurements`.
It reads actual curve positions/tangents/magnitudes/free flags and independent
`POINT.NUMBER`. Eight SOLID face names/UVs are captured once and reused for a second
complete capture of positions/normals, COG, native bounds and available raw metrics.
Both captures, before/after observations and window context must match. Original
source receipt SHA, expiry and native identity remain pinned. No selection,
activation, geometry, save, cleanup or image command is called.

SDK reuses the passive layout proof validator under an explicit separate profile,
then independently validates both typed combined captures and their equality.
Original passive-layout behavior and the old unqualified flat/3*N curve adapter
remain unchanged. Curve bounds are unavailable; SOLID bounds come from its native
getter. Raw area units remain raw. Controls and sampled faces are not a complete
continuous geometry or topology certificate, nor a proof of saved-file content.

On 26 October one actual read-only SDK call on the owned already translated copy
exactly matched the earlier independently verified translation capture. Independent
Python also checked the four translated square points against the original source
oracle and the eight SOLID points against cube corners. All 708 protected pins and
28 windows were preserved after the authorized backed-up Native59 deployment.
No translation was repeated. Ordinary read-only Codex separately confirmed the same
owned copy and empty WORLD. This provides the geometric observation needed for the
separate SaveAs/sealed-result/new-copy persistence scenario; it does not itself
claim that persistence or K8-K11 acceptance is complete.
