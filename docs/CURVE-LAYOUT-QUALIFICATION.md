# Passive COMPCURVE native-layout qualification

Native0.57.1/stagedSDK0.63 add `inspect_curve_sample_layout`, separately gated by
default-false `curveLayoutExperimental` with passive entity-candidate dependency.
It is available in full and read-only; it issues no edit authority. Python's
compatibility facade validates the flag without exposing this new recipe.
Registration remains explicit: `Program.WithTools(ToolCatalog.Create(session))`,
with `..CurveLayoutTools.Create(session)` in the catalog. STDIO/.NET10 and pinned
ModelContextProtocol2.2.0 remain unchanged.

Fresh same-document COMPCURVE candidates and an explicit inclusive index range
(at most32indices,0..65535) are required. This diagnostic does not infer an index
base or retry another range. One retained COM document, repeated getter values,
observed inventory/context, original source/receipt bytes and unchanged windows
are checked. The result preserves native array ranks, dimensions, lower bounds
and finite numeric/Boolean values. Bounds and generic geometry interpretation
remain unavailable; `geometryQualified`, `indexBaseInferred`, `topologyCertified`
are false. It never selects, clears, activates, captures images, edits or saves.

The installed Autodesk2023 `file/ole/data/IPshapeModelDoc.htm`, section
GetCompCurvePointData, documents ten return fields: status, positions, before
directions/magnitudes/free flags, then after directions/magnitudes/free flags.
It lists status0success and3invalid start point. Its magnitude comment says3*N.
The actual getter was qualified separately, without assuming that array layout.

Real own-copy qualification on26October(MSK): one NEW generic OPEN and independent
verifier from a legacy owned saved/reopened square-section result whose original
SHA still matched. The active copy contains one SOLID and one COMPCURVE; previous
27windows/source/checkpoints and678protected pins were preserved. Total28windows.
Source files were not reopened for editing, no legacy slicing recipe was executed.

Separate actual read-only SDK0.63: explicit0..3was rejected by PowerShape. The
original failure is retained. Explicit1..4then succeeded: outer rank1/lower1;
positions and both direction arrays rank2[4,3]/lower[1,1]; magnitude and four free
flag arrays rank1[4]/lower[1]. Magnitudes were one value per point, rather than the
3*N documentation comment. Independent Python matched all four saved square
corners, both adjacent-edge directions at each corner, all eight magnitudes20
and all sixteen false free flags exactly. Raw length80/area400 and observed SOLID
properties/context were unchanged. This confirms this square fixture, not general
spline semantics or complete curve equivalence.

The first Native0.57 diagnostic omitted the raw status on a rejected read. The
0.57.1correction retains `getterStatusRaw`; a separate changed-error-path check
returned3for0..3without retrying OPEN or geometry. Both deployments used backups
and one panel restart each. Original success evidence/package hashes are retained.
Ordinary SDK0.48 remained unchanged and separately read the final active copy.

Before CAD:10Native fake-context guards/marker refusal,18targetedSDK cases,
23Python config tests, catalogs76full/10readonly and bad-input refusal without
Native queue. Fresh post-CAD CI runner passed10Native controls. Full.NET1063/1063passed without failures/skips. Exact
publication CI is recorded in current project state. Fake fixtures never attach
to PowerShape. No geometry/SaveAs/close/selection/mouse was used in this block.

The older RotationMeasurements.Curve adapter remains separate and unqualified for
this actual layout: it assumes flat arrays,3*N magnitudes and available bounds.
No old verifier was silently weakened. Next, implement an explicitly versioned
typed curve witness profile from the observed layout, then one owned translation
and separate persistence using the existing ownership/claim/fence/verifier cycle.
The separately gated [owned COMPCURVE translation](OWNED-CURVE-TRANSLATION.md)
subsequently qualified one complete1..N control profile with an unchanged SOLID
control. This passive diagnostic alone still supplies no edit authority. Mixed
sets, remaining K8 operations, K9, five-classK10 and release/userK11 remain open.
