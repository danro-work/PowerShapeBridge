"""Audit the narrow, empty-selection passive PowerShape observation.

This does not contact CAD or qualify a null getter as a successful selection read.
"""
import hashlib
import json
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[1]
evidence = root / "work/K8-native-selection-live"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    report = read(evidence / "result-corrected.json")
    before, after = report["before"], report["after"]
    assert report["state"] == "passive_layout_observed_reader_rejected"
    assert report["strictReader"]["state"] == "native_selection_read_rejected"
    assert "Null native getter" in report["strictReader"]["error"]
    assert report["nativeLayoutQualified"] is False
    assert report["cadCommandsAttempted"] == report["selectionChangesAttempted"] == 0
    assert report["cadSaveAttempted"] is report["workerStarted"] is False
    for key in ("model", "processId", "processStartedUtc", "windowsSessionId", "entities"):
        assert before[key] == after[key], key
    for observed in (before, after):
        assert observed["contextBefore"] == observed["contextAfter"] == [0, 0]
        assert observed["selectionBefore"] == observed["selectionAfter"] == []
    assert report["windowsBefore"] == report["windowsAfter"]
    assert report["windowsBefore"]["windowCount"] == 7
    assert report["selectionLayout"]["isNull"] is True
    assert report["selectionLayout"]["value"] is None
    inventory = report["inventoryLayout"]
    assert inventory["runtimeType"] == "System.Object[]" and inventory["lowerBounds"] == [0]
    assert inventory["elementTypes"] == ["System.Int32"]
    assert inventory["value"] == [800000005]
    fields = report["itemDataLayouts"]
    assert [item["value"] for item in fields] == [[40], [5], ["1"], [0]]
    assert [item["elementTypes"] for item in fields] == [["System.Int32"], ["System.Int32"], ["System.String"], ["System.Int32"]]
    hashes = []
    scenario = read(root / "evidence/K7-live-checkpoint-approved20261014a/result.json")
    for path, expected in scenario["protectedHashes"].items():
        assert sha(Path(path)) == expected, path
        hashes.append({"path": path, "sha256": expected})
    working = Path(before["model"]["path"])
    assert sha(working) == report["workingSha256Before"] == report["workingSha256After"]
    inputs = [evidence / "result.json", evidence / "result-corrected.json", evidence / "preflight-corrected.json",
              evidence / "ReadNativeSelectionLayout.exe", root / "tools/ReadNativeSelectionLayout.cs",
              root / "work/K9-dgk-wire-runner/PSBridge.Worker.exe"]
    assert sha(inputs[2]) == report["inputSha256"]
    assert sha(inputs[-1]) == report["assemblySha256"]
    hashes.extend({"path": str(path), "sha256": sha(path)} for path in inputs)
    result = {"state": "passed", "protectedHashCount": len(scenario["protectedHashes"]), "hashes": hashes,
              "selectionGetterSucceeded": False, "nativeLayoutQualified": False,
              "scope": "Passive empty-selection diagnostic only; null remains unavailable and must not grant execution."}
    output = evidence / "independent-audit.json"
    with output.open("x", encoding="utf-8") as file:
        json.dump(result, file, indent=2)
    print(json.dumps({key: value for key, value in result.items() if key != "hashes"}))


if __name__ == "__main__":
    main()
