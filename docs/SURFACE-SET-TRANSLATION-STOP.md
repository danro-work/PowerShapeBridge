# First real ordered SURFACE-set stop

The current schema7 / `owned_surface_set_grid_translation_v1` profile requires
exact target normals, visibility, layout and nontarget witnesses. Its first
real two-surface WORLD[1,0,0]mm attempt stopped after the first callback returned
1 and before the second callback began. No repeat, inverse, selection, SaveAs,
OPEN or close followed. The original main journal remains `outcome_unknown`;
its claim and mutation fence are retained.

A separate read-only connection sampled all six surfaces. Independent local
math and artifact checks found:

- First target positions match the declared translation within
  8.881784197001252e-16mm.
- Two components of that target's normals differ by at most
  2.220446049250313e-16 in raw normal units. The exact equality check rejected it.
- All five other surfaces' positions and normals are exact; all six layouts,
  shapes and visibility arrays are exact.
- The first per-step authoritative return is preserved. There is no second
  started record and no complete main command-return record.
- All 28 window identities, 1,278 protected files and nine stop artifacts were
  unchanged by diagnosis. The geometry is unsaved; this is not a topology proof.

The magnitude is consistent with floating-point roundoff, but the old v1
contract must not be relaxed retroactively. A regression now locks its refusal
before the second callback for this small target-normal difference.

Next: define a new explicitly versioned numerical-normal contract with a tight,
declared target-only tolerance, independent SDK validation and unchanged exact
nontarget/visibility/layout guards. Preserve this stopped copy and its fence.
Before any later geometry command, design a separate owned-copy disposition or
fresh-copy scenario with the document/window policy checked explicitly. Do not
resume the old set, infer complete success from samples, or remove its fence.
