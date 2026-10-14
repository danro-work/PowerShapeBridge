"""One public STDIO read -> fixed native recipe -> existing CAD passive getters.

Runs no panel/worker Main, mouse, selection change, activation, export or save.
"""
import json
from pathlib import Path
import subprocess
import sys
import threading

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "src"))
from test_sdk_stdio import Client

stage = ROOT / "work/K8-levels-public-read"
evidence = ROOT / "evidence/K8-levels-public-live"
assert not evidence.exists(), "Preserve first run; fresh evidence required"
evidence.mkdir(parents=True)
preflight = json.loads((stage / "preflight.json").read_text(encoding="utf-8-sig"))
assert preflight["state"] == "active_model_inspected"
assert preflight["model"]["name"] == "Working_7b0765448a714a97a8f78edf46754834"
client = Client([r"C:\Program Files\dotnet\dotnet.exe", str(ROOT / "src/PowerShapeMcp.Host/bin/Debug/net10.0/PowerShapeMcp.Host.dll"), "--bridge-root", str(stage), "--timeout", "40", "--read-only"])
stop = threading.Event()
calls, errors = [], []


def panel():
    while not stop.is_set():
        for request in (stage / "requests").glob("*.json"):
            processing = request.with_name(request.name + ".processing")
            request.rename(processing)
            try:
                envelope = json.loads(processing.read_text(encoding="utf-8-sig"))
                assert envelope["operation"] == "recipe.prepare"
                assert envelope["arguments"] == {"recipeId": "inspect_model_levels", "parameters": {}}
                result = subprocess.run([str(stage / "ReadNativeLevelRecipe.exe"), "--read-only-existing", str(stage / "PSBridge.Worker.exe"), request.stem], capture_output=True, text=True, timeout=90)
                calls.append({"requestId": request.stem, "returncode": result.returncode, "stdout": result.stdout, "stderr": result.stderr})
                assert result.returncode == 0
            except BaseException as error:
                errors.append(repr(error))
                stop.set()
        stop.wait(.02)


try:
    client.call(1, "initialize", {"protocolVersion": "2025-11-25", "capabilities": {}, "clientInfo": {"name": "levels-live-readonly", "version": "1"}})
    client.notify("notifications/initialized")
    tools = client.call(2, "tools/list")["result"]["tools"]
    tool = next(item for item in tools if item["name"] == "inspect_model_levels")
    assert tool["annotations"]["readOnlyHint"] is True and tool["inputSchema"]["additionalProperties"] is False
    rejected = client.call(3, "tools/call", {"name": "inspect_model_levels", "arguments": {"levelNumber": 0}})
    assert "error" in rejected and not list((stage / "requests").glob("*.json"))
    thread = threading.Thread(target=panel, daemon=True)
    thread.start()
    reply = client.call(4, "tools/call", {"name": "inspect_model_levels", "arguments": {}}, timeout=90)
    assert "error" not in reply and not reply["result"].get("isError", False), reply
    data = reply["result"]["structuredContent"]
    assert data["state"] == "model_levels_inspected_read_only" and data["slotCount"] == 1000
    assert data["model"] == preflight["model"] and len(calls) == 1 and not errors
    client.close()
finally:
    stop.set()
    if "thread" in globals():
        thread.join(timeout=5)
    (evidence / "transcript.json").write_text(json.dumps(client.transcript, ensure_ascii=False, indent=2), encoding="utf-8")
    (evidence / "calls.json").write_text(json.dumps({"calls": calls, "errors": errors}, indent=2), encoding="utf-8")
    (evidence / "stderr.txt").write_text("".join(client.logs), encoding="utf-8")
print(json.dumps({"state": "passed", "nativeRequests": len(calls), "slotCount": data["slotCount"], "cadMutationAttempted": False}))
