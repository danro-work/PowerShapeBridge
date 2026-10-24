# Saved bytes sealed before closing an owned Result

On 24 October 2026, the existing saved-result preparation route created a verified saved-byte snapshot **before any close**. No new runtime code, package, configuration, MCP primitive or byte-copy engine was required.

The live workflow used a fresh independent verifier of the original successful OPEN to regain a current owned-copy lease, a fresh document binding, one NEW SaveAs, and separate verification of that SaveAs. PowerShape changed the model ID during SaveAs. An extra local assertion initially expected the old ID and stopped; the original returned SaveAs was then independently verified without another save.

Direct generic `prepare_working_copy` correctly refused the still-open writer with `file_busy`, before creating its reservation or owned folder. Keep that strict exclusive-reader policy. Instead, restore the current saved-result handle from its original successful SaveAs verifier, then call the existing `prepare_owned_saved_reopen`. This purpose-built route reads the verified saved result through a held physical identity, checks its expected hash and length before/after the seed copy, flushes a create-once seed, marks it read-only, and calls the unchanged strict owned-copy routine on that closed seed. It is a checked snapshot of known saved bytes, not an exclusive lock on the live CAD writer.

The resulting files have distinct physical identities and identical 819,200-byte contents:

| Role | Protection and purpose |
| --- | --- |
| A: live saved Result | Owned writer; must remain open until a qualified finalization profile exists |
| B: independent saved-byte seed | Read-only canonical output for the next close/reopen qualification |
| C: unopened owned working derivative | Editable/openable derivative, independently owned |
| D: independent checkpoint | Read-only second copy of B's saved bytes |

An independent OS reader checked physical identity, final paths, single links, two SHA reads, length, read-only attributes and frozen preparation/ownership/SaveAs lineage. Separate getter and read-only MCP observations preserved the current inventory, units, COG/raw mass properties, bounds and eight sampled XYZ points. The original user source, all 343 previously protected files and the other 25 windows remained unchanged; total windows stayed 26. There was one new SaveAs, no repeat SaveAs, and zero OPEN, close, scale or mouse actions. The seed and checkpoint have not yet been opened; these checks do not establish full topological equivalence.

The unchanged Native 0.47 / staged SDK 0.53 passed this real workflow. Ordinary Codex remains SDK 0.48. Both exact-head PR83 checks succeeded; prior .NET 987/987 remains applicable. No unchanged synthetic suite was rerun for this documentation/workflow block. A passive getter could not attach from the isolated shell; the same qualified reader succeeded from the approved user COM session with no CAD command or PowerShape restart.

The [failed strict saved-Result close](OWNED-SAVED-RESULT-CLOSE.md) remains disabled; its original unknown receipt, claim and fence remain intact. A subsequent [separate finalization and generic seed-reopen workflow](OWNED-SAVED-FINALIZATION.md) now passed on this owned SOLID: B/D/C and all other protected files/windows remained preserved while A's changed serialization was recorded separately. Do not ask the old unchanged-A SaveAs proof to certify changed A, reuse an expired prepared handle, replay the old close, or create another SaveAs merely to regain access. K8–K11 remain incomplete.
