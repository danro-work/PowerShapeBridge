"""File-only independent audit of a completed two-setter API qualification."""
import argparse
import hashlib
import json
from pathlib import Path


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def audit(folder):
    folder = Path(folder).resolve()
    report = read(folder / "result.json")
    checked = read(folder / "verification.json")
    independent = read(folder / "independent-native-result.json")
    assert report["state"] == "owned_workplane_switch_executed_unverified"
    assert report["executionRequested"] is report["cadMutationAttempted"] is True
    assert report["geometryMutationAttempted"] is report["cadSaveAttempted"] is False
    assert checked["state"] == "owned_workplane_switch_qualification_verified"
    assert checked["resultSha256"] == sha(folder / "result.json")
    assert checked["independentReadSha256"] == sha(folder / "independent-native-result.json")
    assert checked["matchingCopyFenceReleased"] is checked["noCommandReplay"] is True
    assert checked["recordedJournalState"] == "succeeded_verified"
    assert checked["operationId"] == report["operationId"]
    pair = report["result"]
    assert pair["setterCalls"] == 2
    plan = read(folder / "plan.json")
    assert sha(folder / "plan.json") == report["planSha256"]
    for field in ("operationId", "copyId", "ownershipSha256", "sourceAuditSha256", "before"):
        assert plan[field] == report[field], field
    assert plan["maximumSetterCalls"] == 2 and plan["noAutomaticRecovery"] is True
    target = report["before"]["targetCompositeId"]
    assert target > 0
    for name, field, expected in (
        ("activation-return.json", "activationReturnSha256", target),
        ("world-return.json", "worldReturnSha256", 0),
    ):
        assert sha(folder / name) == pair[field]
        receipt = read(folder / name)
        assert receipt["setterReturned"] is True
        assert receipt["targetCompositeId"] == receipt["activeRaw"] == expected
    claim = read(report["claimPath"])
    assert sha(report["claimPath"]) == report["claimSha256"]
    assert claim["operationId"] == report["operationId"]
    assert claim["planSha256"] == report["planSha256"]
    assert claim["approvalSha256"] == plan["approvalSha256"]
    assert claim["automaticReplayForbidden"] is True
    previous = None
    events = []
    for index, path in enumerate(sorted((folder / "operations" / report["operationId"]).glob("*.json"))):
        value = read(path)
        assert path.name == f"{index:06d}.json"
        assert value["sequence"] == index and value["previousSha256"] == previous
        assert value["operationId"] == report["operationId"] and value["planSha256"] == report["planSha256"]
        previous = sha(path)
        events.append(value)
    assert [v["state"] for v in events] == [
        "prepared", "execution_claimed", "command_started", "command_returned", "executed_unverified", "succeeded_verified"
    ]
    returned = events[3]["details"]
    assert returned["nativeRequestId"] == report["operationId"]
    assert returned["worldActivationReturned"] is True and returned["setterCalls"] == 2
    for key in ("activationReturnSha256", "worldReturnSha256"):
        assert returned[key] == pair[key]
    assert events[-1]["details"]["noCommandReplay"] is True
    for path, digest in report["protectedHashes"].items():
        assert sha(path) == digest, path
    root = Path(report["runtimeRoot"])
    assert not (root / "workingcopies" / report["copyId"] / "mutation-fence.json").exists()
    assert independent["state"] == "passive_owned_workplane_observed"
    assert independent["assemblySha256"] == report["workerSha256"]
    for flag in ("cadMutationAttempted", "contextChangeAttempted", "selectionChangesAttempted", "cadSaveAttempted", "workerStarted"):
        assert independent[flag] is False
    baseline = report["before"]["observation"]
    fields = (
        "model", "processId", "processStartedUtc", "windowsSessionId", "productVersion",
        "entityCount", "entities", "countsByType", "levelsContainingEntities", "workplanes",
        "contextBefore", "contextAfter", "selectionBefore", "selectionAfter", "lengthUnitsRaw",
        "lengthUnitNameRaw", "lengthUnitFactorRaw", "areaUnitNameRaw", "areaUnitFactorRaw",
        "volumeUnitNameRaw", "volumeUnitFactorRaw", "generalToleranceRaw",
    )
    for observed in (independent["before"], independent["after"], pair["worldObservation"]["observation"]):
        for field in fields:
            assert observed[field] == baseline[field], field
        assert observed["contextBefore"] == observed["contextAfter"] == [0, 0]
        assert observed["selectionBefore"] == observed["selectionAfter"] == []
    assert independent["boundsWorldRaw"] == independent["boundsWorldRawAgain"] == report["before"]["boundsFalseRaw"]
    assert len(independent["frames"]) == 1
    assert independent["frames"][0]["worldInfo"] == report["before"]["frameRaw"]
    assert independent["frames"][0]["compositeId"] == target
    assert independent["windowsBefore"] == independent["windowsAfter"] == report["before"]["windows"]
    return {
        "state": "passed", "qualificationOnly": True, "setterCalls": 2,
        "protectedHashChecks": len(report["protectedHashes"]),
        "coordinateModesObserved": pair["coordinateModesObserved"],
        "scope": "existing workplane and WORLD on one owned model; unchanged observed WORLD data and saved files; not full topology or a published MCP feature",
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", required=True)
    args = parser.parse_args()
    folder = Path(args.evidence)
    result = audit(folder)
    with (folder / "independent-audit.json").open("x", encoding="utf-8") as stream:
        json.dump(result, stream, indent=2)
    print(json.dumps(result))
