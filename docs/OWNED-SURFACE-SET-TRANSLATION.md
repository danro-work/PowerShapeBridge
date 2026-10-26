# Ordered owned SURFACE set translation: candidate profile

Source Native0.65 / SDK0.71 adds two full-only tools:
`execute_owned_surface_set_translation` and
`verify_owned_surface_set_translation`. Registration uses the existing
`ExecutionTools.Create(session)` entry in `ToolCatalog.Create`. The separate
`ownedSurfaceSetTranslationsExperimental` flag defaults to false and requires
the singleton SURFACE translation, owned translation and passive sampling flags.
Direct calls in read-only are refused before Native queue submission.

The profile accepts an original immutable `translate_entities` plan with two
to eight distinct ordered SURFACE keys in a surface-only owned model, resolved
WORLD/mm and empty selection. It retains the original vector, ownership,
checkpoint, binding, source hashes and expiry. An explicit known UV grid covers
every retained surface; domains are not inferred and bounds are not fabricated.
The first real qualification is planned for two targets among six surfaces.
The broader numeric bound is not a claim of live coverage for every set size.

The original owned translation executor retains one context, one claim/fence and
one main journal. The typed strategy freezes each step's started, authoritative
returned and observed records in a hash chain. After every return1, the complete
grid set must show exactly the completed target subset moved by the declared
vector; other positions, normals, visibility, layout, non-positional properties,
files and windows are preserved before the next call. Autodesk `TranslateItem`
operates on one composite per call, so the set operation is sequential.

A lost/non-successful return, expired lease, context change or witness drift
stops the set and retains its claim/fence and completed-step evidence. No retry,
inverse, Undo, save or assumed cleanup follows. The separate verifier requires
all original returns and immutable step records plus fresh target/non-target
reads twice before releasing only its matching fence. The SDK independently
checks the Native bytes and chain. A partial set cannot become successful from
geometry alone or through the old singleton verifier.

Internal qualification passed10 Native cases,16 actual Framework-to-SDK proof
checks,80 necessary legacy Native translation/rotation checks,30 Python config
tests and SDK mode/schema/pre-queue checks. One SDK test initially expected the
wrong exception type for the correct read-only refusal; its corrected expectation
passed separately, with the original failure retained. Actual STDIO discovery
confirmed90 full /11 read-only tools on the isolated all-feature profile; these
counts include preparation, verification and diagnostics, not90 CAD operations.

**Not live-qualified or installed yet.** The preflight detected two PowerShape
processes and stopped before activation, deployment or geometry. The supported
single-process restriction is retained; no unknown process or document was closed.
The early owned test will proceed after the user's normal session arrangement
leaves one PowerShape process. Installed Native64 / ordinary SDK48 remain unchanged.
Full new regression/CI and saved-result verification remain separate pending gates.

These are declared UV sample and observation checks, not full topology or trimming
coverage. Mixed types, POINTs, local frames and other K8/K9/five-class K10/release/
personal K11 requirements remain open. P0 acceptance does not accept general v1.
