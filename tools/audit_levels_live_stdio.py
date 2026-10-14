"""Independent audit of public MCP level read against the actual native receipt."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
stage = ROOT / "work/K8-levels-public-read"
evidence = ROOT / "evidence/K8-levels-public-live"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


transcript = read(evidence / "transcript.json")
call_summary = read(evidence / "calls.json")
assert not call_summary["errors"] and len(call_summary["calls"]) == 1
request_id = call_summary["calls"][0]["requestId"]
assert call_summary["calls"][0]["returncode"] == 0
native = read(stage / "responses" / (request_id + ".json"))
request = read(stage / "requests" / (request_id + ".json.processing"))
assert request == {"requestId": request_id, "operation": "recipe.prepare", "arguments": {"recipeId": "inspect_model_levels", "parameters": {}}}
extra = next(frame for frame in transcript if frame.get("id") == 3)
assert "error" in extra
reply = next(frame for frame in transcript if frame.get("id") == 4)["result"]
assert not reply.get("isError", False)
data = reply["structuredContent"]
assert data["state"] == "model_levels_inspected_read_only"
assert native["state"] == "native_levels_read_unqualified"
assert data["bridgeRequestId"] == request_id
assert data["levelSlots"] == native["levelSlots"] and data["slotCount"] == len(data["levelSlots"]) == 1000
assert [row["number"] for row in data["levelSlots"]] == list(range(1000))
for field in ("filterFlagsRaw", "filterOrder", "processId", "processStartedUtc", "windowsSessionId"):
    assert data[field] == native[field], field
for field in ("model", "entities", "contextBefore", "contextAfter", "selectionBefore", "selectionAfter", "levelsContainingEntities"):
    assert native["before"][field] == native["after"][field], field
assert native["windowsBefore"] == native["windowsAfter"]
assert native["windowsBefore"]["windowCount"] == 7
assert data["model"] == read(stage / "preflight.json")["model"] == native["after"]["model"]
for flag in ("contextChangeAttempted", "geometryMutationAttempted", "cadSaveAttempted", "nativeLayoutQualified"):
    assert native[flag] is False
assert data["cadMutationAttempted"] is False
assert not any("Handle" in key or "Reference" in key for key in data)
hashes = []
scenario = read(ROOT / "evidence/K7-live-checkpoint-approved20261014a/result.json")
for path, expected in scenario["protectedHashes"].items():
    assert sha(Path(path)) == expected, path
    hashes.append({"path": path, "sha256": expected})
working = Path(data["model"]["path"])
previous = read(ROOT / "work/K8-native-selection-nonempty/result-corrected.json")
assert sha(working) == previous["workingSha256Before"] == previous["workingSha256After"]
for path in (stage / "responses" / (request_id + ".json"), stage / "requests" / (request_id + ".json.processing"), evidence / "transcript.json", evidence / "calls.json", stage / "PSBridge.Worker.exe", stage / "ReadNativeLevelRecipe.exe"):
    hashes.append({"path": str(path), "sha256": sha(path)})
result = {"state": "passed", "nativeRequests": 1, "slotCount": 1000, "protectedHashCount": len(scenario["protectedHashes"]), "cadMutationAttempted": False, "hashes": hashes,
          "scope": "Public readonly SDK tool and actual fixed native recipe on one K7 own document; no edit handles, filter/selection/geometry/save changes. Other models and K8–K11 remain unqualified."}
with (evidence / "independent-audit.json").open("x", encoding="utf-8") as file:
    json.dump(result, file, indent=2)
print(json.dumps({key: value for key, value in result.items() if key != "hashes"}))
