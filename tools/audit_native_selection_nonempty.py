"""Independent audit of the manually selected single SOLID in the K7 own copy."""
import hashlib
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
STAGE = ROOT / "work/K8-native-selection-nonempty"
COPY_ID = "7b0765448a714a97a8f78edf46754834"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--corrected", action="store_true", help="Audit preserved corrected diagnostic and its fresh preflight.")
    args = parser.parse_args()
    result_name = "result-corrected.json" if args.corrected else "result.json"
    preflight_name = "preflight-corrected.json" if args.corrected else "preflight.json"
    report = read(STAGE / result_name)
    preflight = read(STAGE / preflight_name)
    assert preflight["state"] == "active_model_inspected"
    assert preflight["contextBefore"] == preflight["contextAfter"] == [1, 0]
    assert preflight["model"]["name"] == "Working_" + COPY_ID
    assert report["state"] == "passive_selection_layout_observed"
    assert report["cadCommandsAttempted"] == report["selectionChangesAttempted"] == 0
    assert report["cadSaveAttempted"] is report["workerStarted"] is report["nativeLayoutQualified"] is False
    reader = report["strictReader"]
    assert reader["state"] == "native_selection_items_read_unqualified"
    assert reader["selectionCount"] == 1
    assert reader["selectedEntityKeys"] == [[40, 5]]
    assert reader["selectedItemsCanonicalOrder"] == [{"compositeId": 800000005, "nativeTypeCode": 40, "nativeId": 5, "name": "1", "levelNumber": 0}]
    for key in ("contextChangeAttempted", "geometryMutationAttempted", "cadSaveAttempted", "nativeLayoutQualified"):
        assert reader[key] is False
    for key in ("inventoryLayout", "selectionLayout"):
        layout = report[key]
        assert layout["isNull"] is False
        assert layout["runtimeType"] == "System.Object[]" and layout["rank"] == 1
        assert layout["lowerBounds"] == [0] and layout["elementTypes"] == ["System.Int32"]
        assert layout["value"] == [800000005]
    assert [field["value"] for field in report["itemDataLayouts"]] == [[40], [5], ["1"], [0]]
    before, after = report["before"], report["after"]
    for observed in (before, after):
        assert observed["contextBefore"] == observed["contextAfter"] == [1, 0]
        assert observed["entityCount"] == 1
    for key in ("processId", "processStartedUtc", "windowsSessionId", "model", "entities", "selectionBefore", "selectionAfter"):
        assert before[key] == after[key], key
    assert report["windowsBefore"] == report["windowsAfter"]
    assert report["windowsBefore"]["windowCount"] == 7
    working = Path(before["model"]["path"])
    assert working.resolve().parent == (ROOT / "releases/P2.6/workingcopies" / COPY_ID).resolve()
    assert sha(working) == report["workingSha256Before"] == report["workingSha256After"]
    hashes = []
    old = read(ROOT / "evidence/K7-live-checkpoint-approved20261014a/result.json")
    for path, expected in old["protectedHashes"].items():
        assert sha(Path(path)) == expected, path
        hashes.append({"path": path, "sha256": expected})
    for name, field in ((preflight_name, "inputSha256"), ("ReadNativeSelectionLayout.exe", "diagnosticSha256")):
        path = STAGE / name
        assert sha(path) == report[field]
        hashes.append({"path": str(path), "sha256": report[field]})
    hashes.append({"path": str(STAGE / result_name), "sha256": sha(STAGE / result_name)})
    if args.corrected:
        original = read(STAGE / "result.json")
        assert original["state"] == "passive_layout_probe_failed" and "InvalidCastException" in original["error"]
        hashes.append({"path": str(STAGE / "result.json"), "sha256": sha(STAGE / "result.json")})
    result = {"state": "passed", "protectedHashCount": len(old["protectedHashes"]), "hashes": hashes,
              "selectionGetterSucceeded": True, "selectionCount": 1, "selectedEntityKeys": [[40, 5]],
              "scope": "One manually selected SOLID in the K7 own copy; no selection change, geometry edit, save or export. Other types and lifecycle cases remain unqualified."}
    with (STAGE / "independent-audit.json").open("x", encoding="utf-8") as file:
        json.dump(result, file, indent=2)
    print(json.dumps({key: value for key, value in result.items() if key != "hashes"}))


if __name__ == "__main__":
    main()
