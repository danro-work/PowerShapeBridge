# Owned COMPCURVE uniform scale

The full experimental profile scales exactly one owned, closed COMPCURVE in
WORLD with a positive uniform factor and explicit centre. It reuses the existing
13-phase NOKEEP scale executor, one-use preparation/claim, journal, mutation
fence and separate verification. SOLID and SURFACE profiles retain their own
measurement contracts. The new profile is disabled by default.

On 26 October, one new factor 1.25 about WORLD [2,-3,4] succeeded on the owned
four-control curve. A separate verifier, passive curve/SOLID reader and independent
Python audit checked every control, eight tangent directions and magnitudes,
sixteen free flags, ranges and count/layout. The independent point oracle was
[-20.75, 1.25*originalX+2, -1.25*originalY-1]; maximum component error was
1.78e-15 mm. LengthRaw changed 80 to 100 and areaRaw 400 to 625. Unresolved raw
units remain raw. Pinned neighbouring SOLID face samples, normals, COG, bounds
and raw metrics were unchanged. All 932 prior protected pins and 28 windows were
preserved. This proves the declared controls and samples, not full topology.

The earlier attempt stopped after selection because the guard expected COMPCURVE
while PowerShape reported "Composite Curve". That request remains failed/unknown
with its original claim and journal. The separate
[clear-only recovery](CURVE-SCALE-STOP-RECOVERY.md) established unchanged full
controls before releasing matching fences. The successful scale used a new
intent and preparation; the old request was never replayed.

Native 0.64.0-dev / staged SDK 0.70.1-dev / ordinary SDK 0.48.0-dev were unchanged
for this successful scale. The completed full .NET run passed 1106/1106 with no
skip; applicable targeted Native, Framework-to-SDK, SDK and Python checks also
passed. Unchanged checks were not repeated for this documentation block.

SaveAs, protected saved-byte seal, owned-writer finalization and independent
reopen verification form a separate block. The [saved/reopened block](CURVE-SCALE-PERSISTENCE.md) also passed on 26 October. Other entity types, sets and the complete K8-K11
qualification remain open; this profile is not a universal v1 acceptance.
