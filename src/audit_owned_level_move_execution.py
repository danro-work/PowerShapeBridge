"""Audit saved executor/journal/read evidence from fake CAD callbacks only."""
import argparse
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--stage", required=True, type=Path)
parser.add_argument("--output", required=True, type=Path)
args = parser.parse_args()
stage = args.stage.resolve()
source = stage / "reports/owned-level-move-execution-tests.json"
data = source.read_bytes()
matrix = json.loads(data.decode("utf-8-sig"))
rejected = {"retained", "claim_exists", "existing_journal", "preexisting_fence"}
unknown = {"throw_before", "throw_after", "return_false", "return_int", "return_null", "no_op", "wrong_level", "foreign_object",
           "selection_preserved", "geometry_change", "unit_change", "workplane_change", "bad_bounds", "windows_change",
           "mapping_change", "level_name", "filter_change", "post_read_error", "receipt_forged_success", "saved_change"}
unverified = {"control", "double_flags", "held_writer", "missing_response", "fence_tamper", "missing_fence", "stale_inspection",
              "stale_listing", "stale_levels", "level_receipt_mismatch", "capture_receipt", "expired_verifier", "journal_tamper",
              "replay_after_verify", "held_fence", "busy_verifier", "claim_tamper", "missing_claim"}
verified = {"control", "double_flags", "held_writer", "post_read_error", "replay_after_verify"}
rows = matrix["rows"]
assert matrix["state"] == "passed" and matrix["schema"] == "psbridge.owned-level-move-execution-matrix.v1"
assert matrix["cadConnectionAttempted"] is False
assert len(rows) == 42 and len({row["fault"] for row in rows}) == 42
assert {row["fault"] for row in rows} == rejected | unknown | unverified
hash_count = 0

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))

for row in rows:
    fault = row["fault"]
    root = Path(row["root"]).resolve()
    assert root.is_relative_to(stage / "work") and row["cadConnectionAttempted"] is False
    expected = "owned_level_move_rejected_before_change" if fault in rejected else "owned_level_move_outcome_unknown" if fault in unknown else "owned_level_move_executed_unverified"
    assert row["executionState"] == expected and row["calls"] == (0 if fault in rejected else 1)
    assert row["verificationCalls"] == 0
    altered = []
    for name, expected_sha in row["protectedHashes"].items():
        path = Path(name).resolve()
        assert path.is_relative_to(root)
        if sha(path) != expected_sha:
            altered.append(path)
        hash_count += 1
    if fault == "saved_change":
        assert len(altered) == 1
        owner = read(altered[0].parent / "ownership.json")
        assert altered[0] == Path(owner["working"]["path"]).resolve()
    else:
        assert not altered
    for name, expected_sha in row["artifactHashes"].items():
        path = Path(name).resolve()
        assert path.is_relative_to(root) and sha(path) == expected_sha
        hash_count += 1
    result_path = Path(row["executionResultPath"]).resolve()
    assert result_path.is_relative_to(root) and sha(result_path) == row["executionResultSha256"]
    result = read(result_path)
    assert result["state"] == expected and result["nativeProviderQualified"] is False
    assert result["cadSaveAttempted"] is result["geometryMutationAttempted"] is result["retryAllowed"] is False
    if fault in rejected:
        assert result["cadMutationAttempted"] is result["contextChangeAttempted"] is False
        assert row["verification"] is None
        continue
    assert result["cadMutationAttempted"] is result["contextChangeAttempted"] is True and result["copyFenced"] is True
    plan = result["plan"]
    plan_path = root / "levelmoveexecutions" / (row["requestId"] + ".json")
    assert sha(plan_path) == result["planSha256"] and read(plan_path) == plan
    assert plan["nativeId"] == 5 and plan["targetLevelNumber"] == 5 and plan["sourceLevelNumber"] == 0
    assert plan["before"]["contextBefore"] == [1, 0] and len(plan["before"]["selectionBefore"]) == 1
    claim = root / "ownedlevelmoves" / (plan["ownedLevelMoveId"] + ".execution-once.json")
    if fault == "missing_claim":
        assert not claim.exists() and sha(claim.with_name(claim.name + ".held-by-fixture")) == result["claimSha256"]
    elif fault == "claim_tamper":
        assert sha(claim) != result["claimSha256"] and claim.read_bytes().endswith(b"tamper")
    else:
        assert sha(claim) == result["claimSha256"]
        assert read(claim)["operationId"] == row["requestId"] and read(claim)["automaticReplayForbidden"] is True
    verification = row["verification"]
    assert verification["cadMutationAttempted"] is verification["contextChangeAttempted"] is verification["cadSaveAttempted"] is False
    accepted = fault in verified
    assert verification["state"] == ("owned_level_move_verified_observed" if accepted else "owned_level_move_verification_rejected")
    fence = root / "workingcopies" / plan["copyId"] / "mutation-fence.json"
    if accepted or fault == "held_fence":
        assert not fence.exists()
    elif fault == "missing_fence":
        assert not fence.exists() and fence.with_name(fence.name + ".held-by-fixture").is_file()
    else:
        assert fence.is_file()
    if fault == "held_fence":
        assert row["followupVerification"]["state"] == "owned_level_move_verified_observed"
        assert row["followupVerification"]["noCommandReplay"] is True
    folder = root / "operations" / row["requestId"]
    events = sorted(folder.glob("*.json"))
    previous = None
    states = []
    returned = None
    for number, path in enumerate(events):
        assert path.name == f"{number:06d}.json"
        if fault == "journal_tamper" and number == 3:
            assert path.read_bytes().endswith(b"tamper")
            break
        event = read(path)
        assert event["sequence"] == number and event["previousSha256"] == previous
        assert event["operationId"] == row["requestId"] and event["planSha256"] == result["planSha256"]
        states.append(event["state"])
        if event["state"] == "command_returned":
            returned = event["details"]
        previous = sha(path)
    assert states[:3] == ["prepared", "execution_claimed", "command_started"]
    assert not (folder / "writer.lock").exists()
    if fault != "journal_tamper":
        if fault in {"throw_before", "throw_after"}:
            assert returned is None and states[-1] == "outcome_unknown"
        else:
            assert returned is not None and returned["returnCode"] == result["returnCode"]
            assert returned["nativeReturnType"] == result["nativeReturnType"]
        assert states[-1] == ("succeeded_verified" if accepted or fault == "held_fence" else "outcome_unknown" if fault in unknown else "executed_unverified")
    if fault == "receipt_forged_success":
        assert result["returnCode"] == 0 and read(root / "responses" / (row["requestId"] + ".json"))["returnCode"] == 1
        assert "journal" in verification["error"].lower()
    if accepted:
        assert verification["copyFenceReleased"] is verification["noCommandReplay"] is True
        assert result.get("commandCalls", 1) == 1

report = {"state": "passed", "caseCount": len(rows), "preCommandRefusals": len(rejected), "unknownOutcomes": len(unknown),
          "unverifiedExecutions": len(unverified), "verifiedCases": len(verified), "metadataCleanupReverified": 1,
          "hashChecks": hash_count, "cadConnectionAttempted": False, "reportPath": str(source),
          "reportSha256": hashlib.sha256(data).hexdigest()}
args.output.parent.mkdir(parents=True, exist_ok=True)
with args.output.open("x", encoding="utf-8") as file:
    json.dump(report, file, indent=2)
print(json.dumps(report))
