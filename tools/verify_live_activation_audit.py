"""Exercise the live file auditor against existing explicitly synthetic wire artifacts."""
import argparse
import copy
import json
from pathlib import Path
from audit_live_workplane_activation import audit_pair, read, sha


def verify(folder, output):
    wire = read(folder / "result.json")
    assert wire["state"] == "passed" and wire["cadConnectionAttempted"] is False
    runtime = Path(wire["root"]).resolve()
    original = {str(p): sha(p) for p in runtime.rglob("*") if p.is_file()}
    baseline = wire["existing"]["execution"]["plan"]["before"]
    final = wire["world"]["execution"]["after"]
    capture = wire["world"]["execution"]["plan"]["captureBefore"]
    windows = wire["world"]["execution"]["plan"]["beforeWindows"]
    independent = dict(
        state="passive_owned_workplane_observed", before=copy.deepcopy(final), after=copy.deepcopy(final),
        windowsBefore=copy.deepcopy(windows), windowsAfter=copy.deepcopy(windows),
        activeWorkplaneRaw=0, boundsWorldRaw=capture["bounds"][0]["boundsWorldRaw"],
        boundsWorldRawAgain=capture["bounds"][0]["boundsWorldRaw"], frames=[capture["plane"]],
        cadMutationAttempted=False, selectionChangesAttempted=False, contextChangeAttempted=False,
        cadSaveAttempted=False, workerStarted=False,
    )
    pair = {}
    for mode in ("existing", "world"):
        pair[mode] = copy.deepcopy(wire[mode])
        identifier = pair[mode]["execution"]["operationId"]
        pair[mode]["executionReceiptSha256"] = sha(runtime / "responses" / (identifier + ".json"))
    cases = []
    for fault in ("control", "wrong_final_cog", "wrong_window_count", "wrong_direct_frame", "same_operation_twice", "altered_receipt_sha"):
        data, reader = copy.deepcopy(pair), copy.deepcopy(independent)
        if fault == "wrong_final_cog":
            next(e for e in reader["after"]["entities"] if e["nativeTypeCode"] == 40)["geometry"]["cogRaw"] = [999, 0, 0]
        if fault == "wrong_window_count":
            reader["windowsAfter"]["windowCount"] += 1
        if fault == "wrong_direct_frame":
            reader["frames"][0]["worldInfo"][0] += 1
        if fault == "same_operation_twice":
            data["world"] = copy.deepcopy(data["existing"])
        if fault == "altered_receipt_sha":
            data["existing"]["executionReceiptSha256"] = "0" * 64
        accepted = True
        result = None
        try:
            result = audit_pair(runtime, data, baseline, reader, 1, evidence_kind="synthetic_file_control")
        except AssertionError:
            accepted = False
        assert accepted is (fault == "control"), fault
        cases.append(dict(fault=fault, accepted=accepted, result=result))
    assert original == {str(p): sha(p) for p in runtime.rglob("*") if p.is_file()}
    result = dict(state="passed", cases=cases, sourceKind="synthetic_file_control", cadConnectionAttempted=False, nativeArtifactsUnchanged=True, sourceReportSha256=sha(folder / "result.json"))
    with output.open("x", encoding="utf-8") as stream:
        json.dump(result, stream, indent=2)
    print(json.dumps(dict(state="passed", cases=len(cases), cadConnectionAttempted=False, nativeArtifactsUnchanged=True)))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    assert not args.output.exists()
    verify(args.evidence, args.output)
