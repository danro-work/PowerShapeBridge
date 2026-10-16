"""File-only independent audit of original activation/WORLD receipts. Never calls CAD."""
import argparse
import copy
import hashlib
import json
import math
from datetime import datetime
from pathlib import Path


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def same_observation(a, b):
    fields = (
        "model", "processId", "processStartedUtc", "windowsSessionId", "productVersion",
        "entities", "entityCount", "workplanes", "levelsContainingEntities",
        "contextBefore", "contextAfter", "selectionBefore", "selectionAfter",
        "lengthUnitsRaw", "lengthUnitNameRaw", "lengthUnitFactorRaw", "areaUnitNameRaw",
        "areaUnitFactorRaw", "volumeUnitNameRaw", "volumeUnitFactorRaw", "generalToleranceRaw",
    )
    for key in fields:
        assert a[key] == b[key], key


def converted_point(point, frame, source, target):
    assert len(point) == 3 and len(frame) == 9
    assert all(type(n) in (int, float) and math.isfinite(n) for n in [*point, *frame])
    origin, z, y = frame[:3], frame[3:6], frame[6:9]
    assert abs(sum(n*n for n in z)-1) <= 1e-9
    assert abs(sum(n*n for n in y)-1) <= 1e-9
    assert abs(sum(a*b for a, b in zip(z, y))) <= 1e-9
    x = [y[1]*z[2]-y[2]*z[1], y[2]*z[0]-y[0]*z[2], y[0]*z[1]-y[1]*z[0]]
    axes = [x, y, z]
    world = [origin[i]+sum(point[j]*axes[j][i] for j in range(3)) for i in range(3)] if source else point
    return [sum(axis[i]*(world[i]-origin[i]) for i in range(3)) for axis in axes] if target else world


def audit_pair(runtime, pair, baseline, independent, expected_windows, *, evidence_kind, multiple=False):
    """Pure file audit, also exercised with explicitly labelled synthetic controls."""
    assert evidence_kind in ("live_recorded_evidence", "synthetic_file_control")
    runtime = Path(runtime).resolve()
    identifiers = []
    checks = 0
    previous_after = baseline
    first_verified_at = None
    plane_id = None
    modes = ("existing", "other", "world") if multiple else ("existing", "world")
    targets = []
    for mode in modes:
        execution = pair[mode]["execution"]
        identifier = execution["operationId"]
        assert len(identifier) == 32 and all(c in "0123456789abcdef" for c in identifier)
        identifiers.append(identifier)
        receipt_path = runtime / "responses" / (identifier + ".json")
        assert sha(receipt_path) == pair[mode]["executionReceiptSha256"]
        receipt = read(receipt_path)
        assert all(execution[key] == value for key, value in receipt.items())
        assert receipt["state"] == "owned_workplane_activation_executed_unverified"
        assert receipt["setterCalls"] == 1 and receipt["sameWorkerPostconditionsPassed"] is True
        assert receipt["cadMutationAttempted"] is receipt["contextChangeAttempted"] is True
        assert receipt["geometryMutationAttempted"] is receipt["cadSaveAttempted"] is False
        plan = receipt["plan"]
        assert plan == read(runtime / "workplaneactivationexecutions" / (identifier + ".json"))
        assert sha(runtime / "workplaneactivationexecutions" / (identifier + ".json")) == receipt["planSha256"]
        assert plan["operationId"] == identifier and plan["operation"] == "activate_owned_workplane"
        same_observation(plan["before"], previous_after)
        assert plan["parameters"]["mode"] == ("world" if mode == "world" else "existing")
        capture = plan["captureBefore"]
        planes = capture["planes"] if multiple else [capture["plane"]]
        assert plan["schemaVersion"] == (2 if multiple else 1)
        assert len(planes) >= 2 if multiple else len(planes) == 1
        assert len({p["compositeId"] for p in planes}) == len({p["nativeId"] for p in planes}) == len(planes)
        for plane in planes:
            assert type(plane["compositeId"]) is int and plane["compositeId"] > 0
            assert type(plane["nativeId"]) is int and plane["nativeId"] >= 0
            converted_point([0, 0, 0], plane["worldInfo"], 0, 0)
        if mode == "world":
            target = 0
        else:
            selected = [p for p in planes if p["nativeId"] == plan["parameters"]["nativeId"]]
            assert len(selected) == 1
            target = selected[0]["compositeId"]
        source = previous_after["contextAfter"][1]
        assert (source == 0 if mode == "existing" else source == targets[-1])
        if mode == "other":
            assert target != targets[0] and target > 0
        targets.append(target)
        assert source != target
        assert capture["sourceActive"] == source and plan["targetCompositeId"] == target
        assert plan["beforeWindows"]["windowCount"] == expected_windows
        packet_path = runtime / "workplaneactivations" / (plan["activationId"] + ".json")
        assert sha(packet_path) == plan["activationSha256"]
        packet = read(packet_path)
        assert packet["operation"] == "activate_existing_workplane_or_world"
        for key in ("schemaVersion", "copyId", "ownershipSha256", "workingSha256", "windowContextSha256", "captureBefore", "targetCompositeId"):
            assert packet[key] == plan[key], key
        assert packet["arguments"]["parameters"] == plan["parameters"]
        claim_path = runtime / "workplaneactivations" / (plan["activationId"] + ".execution-once.json")
        assert sha(claim_path) == receipt["claimSha256"]
        assert read(claim_path) == dict(operationId=identifier, planSha256=receipt["planSha256"], activationId=plan["activationId"], copyId=plan["copyId"], automaticReplayForbidden=True)
        previous = None
        events = []
        for index, path in enumerate(sorted((runtime / "operations" / identifier).glob("*.json"))):
            event = read(path)
            assert path.name == f"{index:06d}.json" and event["sequence"] == index
            assert event["previousSha256"] == previous
            assert event["operationId"] == identifier and event["planSha256"] == receipt["planSha256"]
            events.append(event)
            previous = sha(path)
            checks += 1
        assert [e["state"] for e in events] == ["prepared", "execution_claimed", "command_started", "command_returned", "executed_unverified", "succeeded_verified"]
        assert events[2]["details"]["maximumSetterCalls"] == 1
        if first_verified_at is not None:
            assert datetime.fromisoformat(events[2]["timestampUtc"].replace("Z", "+00:00")) > first_verified_at
        first_verified_at = datetime.fromisoformat(events[-1]["timestampUtc"].replace("Z", "+00:00"))
        returned = events[3]["details"]
        assert returned["nativeRequestId"] == identifier and returned["setterCalls"] == 1
        setter_path = runtime / "workplaneactivationexecutions" / (identifier + ".setter-return.json")
        assert sha(setter_path) == returned["setterReceiptSha256"] == receipt["setterReceiptSha256"]
        setter = read(setter_path)
        assert setter["operationId"] == identifier and setter["planSha256"] == receipt["planSha256"]
        assert setter["setterCalls"] == 1 and setter["setterReturned"] is True
        assert setter["targetCompositeId"] == setter["activeRaw"] == target
        after = receipt["after"]
        assert after["contextBefore"] == after["contextAfter"] == [0, target]
        assert after["selectionBefore"] == after["selectionAfter"] == []
        normalized = copy.deepcopy(after)
        for observation in (plan["before"], after):
            assert observation["entityCount"] == len(observation["entities"]) == len(planes) + 1
            assert sorted(e["nativeTypeCode"] for e in observation["entities"]) == [40] + [54]*len(planes)
            assert sorted(e["nativeId"] for e in observation["entities"] if e["nativeTypeCode"] == 54) == sorted(p["nativeId"] for p in planes)
        old_body = next(e for e in plan["before"]["entities"] if e["nativeTypeCode"] == 40)
        body = next(e for e in normalized["entities"] if e["nativeTypeCode"] == 40)
        expected = old_body["geometry"]["cogRaw"]
        if source:
            expected = converted_point(expected, next(p["worldInfo"] for p in planes if p["compositeId"] == source), source, 0)
        if target:
            expected = converted_point(expected, next(p["worldInfo"] for p in planes if p["compositeId"] == target), 0, target)
        assert len(body["geometry"]["cogRaw"]) == 3
        assert all(type(n) in (int, float) and math.isfinite(n) for n in body["geometry"]["cogRaw"])
        assert all(math.isclose(a, b, rel_tol=1e-12, abs_tol=1e-9) for a, b in zip(body["geometry"]["cogRaw"], expected))
        assert body["geometry"]["coordinateFrame"] == ("world" if target == 0 else "active_workplane_unqualified")
        body["geometry"]["cogRaw"] = old_body["geometry"]["cogRaw"]
        body["geometry"]["coordinateFrame"] = old_body["geometry"]["coordinateFrame"]
        normalized["contextBefore"] = normalized["contextAfter"] = [0, source]
        same_observation(plan["before"], normalized)
        verified = pair[mode]["verification"]
        assert verified["state"] == "owned_workplane_activation_verified_observed"
        assert verified["executionOperationId"] == identifier and verified["activeWorkplaneAfter"] == target
        assert verified["copyFenceReleased"] is verified["noCommandReplay"] is True
        assert verified["recordedJournalState"] == "succeeded_verified"
        verify_request = verified["bridgeRequestId"]
        assert len(verify_request) == 32 and all(c in "0123456789abcdef" for c in verify_request)
        verify_raw = read(runtime / "responses" / (verify_request + ".json"))
        assert all(verified[key] == value for key, value in verify_raw.items())
        assert all(verified[k] is False for k in ("cadMutationAttempted", "geometryMutationAttempted", "contextChangeAttempted", "cadSaveAttempted"))
        assert verified["evidence"] == events[-1]["details"]["evidence"]
        for proof in verified["evidence"]:
            assert sha(runtime / "responses" / (proof["requestId"] + ".json")) == proof["sha256"]
            checks += 1
        assert not (runtime / "workingcopies" / plan["copyId"] / "mutation-fence.json").exists()
        previous_after = after
    assert len(set(identifiers)) == len(modes)
    same_observation(previous_after, baseline)
    assert independent["state"] == "passive_owned_workplane_observed"
    for key in ("cadMutationAttempted", "selectionChangesAttempted", "contextChangeAttempted", "cadSaveAttempted", "workerStarted"):
        assert independent[key] is False
    same_observation(independent["before"], baseline)
    same_observation(independent["after"], baseline)
    assert independent["windowsBefore"] == independent["windowsAfter"] == pair["world"]["execution"]["plan"]["beforeWindows"]
    assert independent["windowsAfter"]["windowCount"] == expected_windows
    assert independent["activeWorkplaneRaw"] == 0
    assert independent["boundsWorldRaw"] == independent["boundsWorldRawAgain"] == pair["world"]["execution"]["plan"]["captureBefore"]["bounds"][0]["boundsWorldRaw"]
    assert len(independent["frames"]) == len(planes)
    frame_key = lambda p: (p["nativeId"], p["compositeId"], tuple(p["worldInfo"]))
    assert sorted(map(frame_key, independent["frames"])) == sorted(map(frame_key, planes))
    return dict(state="passed", evidenceKind=evidence_kind, setterCalls=len(modes), distinctOperationJournals=len(modes), hashChecks=checks, cadConnectionAttempted=False, scope="existing workplane context changes and separately verified WORLD; recorded observations and protected bytes, not complete topology")


def audit_live(folder, runtime, baseline_path, qualification_path, once_path):
    report = read(folder / "result.json")
    assert report["state"] == "owned_workplane_activation_sdk_pair_independently_observed"
    assert report["executeRequested"] is True and report["setterSubmissions"] == 2
    assert report["independentReaderSubmitted"] is True
    names = [step["tool"] for step in report["steps"]]
    assert set(names) <= {"inspect_entity_candidates", "list_documents", "verify_owned_saved_reopen", "bind_working_document", "prepare_owned_workplane_activation", "execute_owned_workplane_activation", "verify_owned_workplane_activation"}
    assert names.count("execute_owned_workplane_activation") == names.count("verify_owned_workplane_activation") == 2
    assert sha(report["approvalNote"]) == report["approvalSha256"]
    once = read(once_path)
    assert once["approvalSha256"] == report["approvalSha256"] and Path(once["evidence"]).resolve() == folder.resolve()
    assert once["automaticReplayForbidden"] is True
    qualification = read(qualification_path)
    assert qualification["protectedHashes"] == report["protectedHashes"]
    project = Path(__file__).resolve().parents[1]
    for name, digest in qualification["pinnedHashes"].items():
        assert sha(project / name) == digest, name
    for name, digest in report["protectedHashes"].items():
        assert sha(name) == digest, name
    for mode in ("existing", "world"):
        submission = read(folder / (mode + "-submission.once"))
        assert submission["approvalSha256"] == report["approvalSha256"] and submission["automaticReplayForbidden"] is True
        assert submission["prepared"]["activationId"] == report[mode]["execution"]["plan"]["activationId"]
    independent = read(folder / "independent-native-result.json")
    assert independent["assemblySha256"] == sha(runtime / "PSBridge.Worker.exe")
    assert independent["diagnosticSha256"] == sha(qualification_path.parent / "native/ReadOwnedWorkplaneResult.exe")
    result = audit_pair(runtime, report, read(baseline_path)["after"], independent, 9, evidence_kind="live_recorded_evidence")
    result.update(protectedHashChecks=len(report["protectedHashes"]), packagePinChecks=len(qualification["pinnedHashes"]), reportSha256=sha(folder / "result.json"))
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("evidence", "runtime", "baseline", "qualification", "once", "output"):
        parser.add_argument("--" + name, required=True, type=Path)
    args = parser.parse_args()
    assert not args.output.exists()
    result = audit_live(args.evidence, args.runtime, args.baseline, args.qualification, args.once)
    with args.output.open("x", encoding="utf-8") as stream:
        json.dump(result, stream, indent=2)
    print(json.dumps(result))
