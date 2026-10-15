"""Independently check isolated level move preparation evidence; no CAD access."""
import argparse
import hashlib
import json
import re
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--stage", required=True, type=Path)
parser.add_argument("--output", required=True, type=Path)
args = parser.parse_args()
stage = args.stage.resolve()
source = stage / "reports/owned-level-move-plan-tests.json"
data = source.read_bytes()
matrix = json.loads(data.decode("utf-8-sig"))
controls = {"control", "double_flags", "nonempty_selection", "held_writer", "caller_alias", "held_proofs",
            "validation_tamper", "validation_working_change", "validation_levels_change", "replay"}
refusals = {"target_bool", "target_string", "target_fraction", "target_negative", "construction998", "construction999",
            "empty_name", "name_mismatch", "active_bool", "active_mismatch", "implicit_clear", "extra_parameter", "no_op",
            "multiple_targets", "surface_target", "set_sha", "binding_sha", "levels_sha", "receipt_document", "receipt_windows",
            "receipt_future", "receipt_missing", "expired", "mutation_fence", "export_fence", "late_fence", "changed_document",
            "late_levels", "saved_change_during_guard", "busy_executor"}
rows = matrix["rows"]
assert matrix["state"] == "passed" and matrix["schema"] == "psbridge.owned-level-move-plan-matrix.v1"
assert matrix["cadConnectionAttempted"] is False
assert len(rows) == 40 and len({r["fault"] for r in rows}) == 40
assert {r["fault"] for r in rows} == controls | refusals
hash_count = 0
flags = ("executionEnabled", "nativeProviderQualified", "geometryMutationAttempted", "contextChangeAttempted", "cadSaveAttempted")

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

for row in rows:
    fault, result = row["fault"], row["result"]
    root = Path(row["root"]).resolve()
    assert root.is_relative_to(stage / "work")
    assert row["cadConnectionAttempted"] is False and all(result[k] is False for k in flags)
    accepted = fault in controls
    assert result["state"] == ("owned_level_move_prepared_observed" if accepted else "owned_level_move_preparation_rejected")
    if not accepted:
        assert result["error"] and row["validation"] is None
    altered = []
    for name, sha in row["protectedHashes"].items():
        path = Path(name).resolve()
        assert path.is_relative_to(root)
        if digest(path) != sha:
            altered.append(path)
        hash_count += 1
    if fault in {"saved_change_during_guard", "validation_working_change"}:
        assert len(altered) == 1
        ownership = json.loads((altered[0].parent / "ownership.json").read_text(encoding="utf-8-sig"))
        assert altered[0] == Path(ownership["working"]["path"]).resolve()
    else:
        assert not altered
    for name, sha in row["artifactHashes"].items():
        path = Path(name).resolve()
        assert path.is_relative_to(root) and digest(path) == sha
        hash_count += 1
    if accepted:
        packet = result["ownedLevelMove"]
        assert packet["arguments"] == row["submitted"] and packet["entityKeys"] == [[40, 5]]
        assert packet["sourceLevelNumber"] == 0 and packet["operation"] == "move_entity_to_level"
        assert packet["executionEnabled"] is packet["nativeProviderQualified"] is False
        assert re.fullmatch(r"[0-9a-f]{32}", result["ownedLevelMoveId"])
        packet_path = root / "ownedlevelmoves" / (result["ownedLevelMoveId"] + ".json")
        if fault == "validation_tamper":
            assert digest(packet_path) != result["ownedLevelMoveSha256"] and packet_path.read_bytes().endswith(b"tamper")
        else:
            assert digest(packet_path) == result["ownedLevelMoveSha256"]
            assert json.loads(packet_path.read_text(encoding="utf-8-sig")) == packet
        record = root / "operations" / result["ownedLevelMoveId"] / "000000.json"
        event = json.loads(record.read_text(encoding="utf-8-sig"))
        assert event["state"] == "prepared" and event["planSha256"] == result["ownedLevelMoveSha256"]
        assert event["sequence"] == 0 and event["previousSha256"] is None
        assert len(list(record.parent.glob("*.json"))) == 1
        receipt_path = root / "responses" / (packet["arguments"]["levelInspectionRequestId"] + ".json")
        assert digest(receipt_path) == packet["arguments"]["levelInspectionSha256"]
        receipt = json.loads(receipt_path.read_text(encoding="utf-8-sig"))
        assert receipt["slotCount"] == len(receipt["levelSlots"]) == 1000
        assert receipt["levelSlots"][5]["name"] == "Named unused"
        assert receipt["windowsBefore"] == receipt["windowsAfter"]
        assert receipt["before"]["selectionBefore"] == receipt["after"]["selectionAfter"]
        validation = row["validation"]
        assert all(validation[k] is False for k in flags)
        denied = fault.startswith("validation_") or fault == "replay"
        assert validation["state"] == ("owned_level_move_preparation_rejected" if denied else "owned_level_move_validated_observed")
        if denied:
            assert validation["error"]
        if fault == "nonempty_selection":
            assert receipt["before"]["contextBefore"] == [1, 0] and len(receipt["before"]["selectionBefore"]) == 1
        if fault == "held_proofs":
            assert row["blockedWrites"] >= 1

report = {"state": "passed", "caseCount": len(rows), "controls": len(controls), "refusals": len(refusals),
          "hashChecks": hash_count, "cadConnectionAttempted": False,
          "reportPath": str(source), "reportSha256": hashlib.sha256(data).hexdigest()}
args.output.parent.mkdir(parents=True, exist_ok=True)
with args.output.open("x", encoding="utf-8") as file:
    json.dump(report, file, indent=2)
print(json.dumps(report))
