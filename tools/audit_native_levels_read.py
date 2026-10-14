"""Audit passive native level-slot getters against live observation and protected files."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
STAGE = ROOT / "work/K8-native-levels-read"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    report = read(STAGE / "actual-object-array.json")
    assert report["state"] == "passive_level_slots_observed"
    assert report["cadCommandsAttempted"] == report["selectionChangesAttempted"] == 0
    assert report["cadSaveAttempted"] is report["workerStarted"] is False
    result = report["reader"]
    assert result["state"] == "native_levels_read_unqualified"
    assert result["nativeLayoutQualified"] is False
    for key in ("contextChangeAttempted", "geometryMutationAttempted", "cadSaveAttempted"):
        assert result[key] is False
    slots = result["levelSlots"]
    assert result["slotCount"] == len(slots) == 1000
    assert [row["number"] for row in slots] == list(range(1000))
    names, used, active = [field["value"] for field in report["fieldLayouts"]]
    assert report["fieldLayouts"][0]["elementTypes"] == ["System.String"]
    assert report["fieldLayouts"][1]["elementTypes"] == report["fieldLayouts"][2]["elementTypes"] == ["System.Double"]
    for index, row in enumerate(slots):
        assert row["name"] == names[index] and isinstance(row["name"], str)
        assert row["usedRaw"] == used[index] and row["activeRaw"] == active[index]
        assert row["usedRaw"] in (0, 1) and row["activeRaw"] in (0, 1)
        assert row["usedValueType"] == row["activeValueType"] == "System.Double"
    for sample in report["scalarSamples"]:
        row = slots[sample["number"]]
        assert sample["layout"]["value"] == [row["name"], row["usedRaw"], row["activeRaw"]]
    before, after = result["before"], result["after"]
    assert before["contextBefore"] == before["contextAfter"] == after["contextBefore"] == after["contextAfter"] == [1, 0]
    for key in ("model", "entities", "processId", "processStartedUtc", "windowsSessionId", "selectionBefore", "selectionAfter"):
        assert before[key] == after[key], key
    assert before["model"]["name"] == "Working_7b0765448a714a97a8f78edf46754834"
    assert result["windowsBefore"] == result["windowsAfter"]
    assert result["windowsBefore"]["windowCount"] == 7
    assert result["filterFlagsRaw"] == report["filterLayout"]["value"] == [0, 0, 0]
    assert report["filterLayout"]["elementTypes"] == ["System.Double"]
    hashes = []
    scenario = read(ROOT / "evidence/K7-live-checkpoint-approved20261014a/result.json")
    for path, expected in scenario["protectedHashes"].items():
        assert sha(Path(path)) == expected, path
        hashes.append({"path": path, "sha256": expected})
    working = Path(before["model"]["path"])
    selected = read(ROOT / "work/K8-native-selection-nonempty/result-corrected.json")
    assert sha(working) == selected["workingSha256Before"] == selected["workingSha256After"]
    hashes.append({"path": str(working), "sha256": sha(working)})
    for name, field in (("PSBridge.Worker.exe", "assemblySha256"), ("ReadNativeLevelLayout.exe", "diagnosticSha256")):
        path = STAGE / name
        assert sha(path) == report[field]
        hashes.append({"path": str(path), "sha256": report[field]})
    hashes.append({"path": str(STAGE / "actual-object-array.json"), "sha256": sha(STAGE / "actual-object-array.json")})
    for name in ("actual-result.json", "actual-layout.json"):
        assert read(STAGE / name)["state"] == "passive_level_reader_rejected"
        hashes.append({"path": str(STAGE / name), "sha256": sha(STAGE / name)})
    result = {"state": "passed", "slotCount": 1000, "protectedHashCount": len(scenario["protectedHashes"]),
              "hashes": hashes, "cadMutationAttempted": False, "scope": "One retained K7 document, all slot flags/names and unchanged filters/selection/windows. Allocation and write authority are not established."}
    with (STAGE / "independent-audit.json").open("x", encoding="utf-8") as file:
        json.dump(result, file, indent=2)
    print(json.dumps({key: value for key, value in result.items() if key != "hashes"}))


if __name__ == "__main__":
    main()
