"""Audit isolated native activation tests; no CAD, journal repair or cleanup."""
import argparse
import hashlib
import json
import re
from pathlib import Path


FAULTS = {"control", "already_active", "stale_process", "stale_target", "changed_before_command",
          "changed_process_after", "changed_model_after", "wrong_selected_model", "document_unavailable",
          "no_effect", "lost_return_after_effect", "saved_bytes_changed", "native_window_changed", "extra_window",
          "expired", "listing_tamper", "invalid_request_id", "invalid_argument", "publication_before_command", "publication_after_return"}
ZERO = {"already_active", "stale_process", "stale_target", "changed_before_command", "expired",
        "listing_tamper", "invalid_request_id", "invalid_argument", "publication_before_command"}


def require(ok, reason):
    if not ok:
        raise ValueError(reason)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def audit(stage):
    require((stage / ".activation-fault-fixture").is_file(), "Not an isolated activation stage")
    report = json.loads((stage / "reports/activation-fault-matrix.json").read_text(encoding="utf-8-sig"))
    require(report["schema"] == "psbridge.activation-fault-matrix.v1" and report["state"] == "passed"
            and report["cadConnectionAttempted"] is False, "Wrong report/scope")
    rows = report["rows"]
    require(len(rows) == len(FAULTS) and {row["fault"] for row in rows} == FAULTS, "Missing/duplicate fault rows")
    hashes = 0
    for row in rows:
        fault = row["fault"]
        root = Path(row["root"]).resolve()
        require(root.is_relative_to(stage / "work") and root.is_dir(), "Fault root outside stage")
        def owned(raw):
            path = Path(raw).resolve()
            require(path.is_relative_to(root), "Artifact outside root")
            return path
        require(row["cadConnectionAttempted"] is False, "Unexpected CAD flag")
        require(type(row["commandCalls"]) is int and row["commandCalls"] == (0 if fault in ZERO else 1), "Fake command count differs")
        require(row["contextFactoryCalls"] == row["contextDisposals"], "Retained context leaked")
        require(row["duplicateIntent"]["state"] == "rejected_before_context_change", "Replay was accepted")
        inventory = {owned(path): value for path, value in row["artifacts"].items()}
        require(set(inventory) == {path.resolve() for path in root.rglob("*") if path.is_file()}, "Artifact inventory differs")
        require(all(sha(path) == value for path, value in inventory.items()), "Artifact bytes changed")
        hashes += len(inventory)
        before, after = row["savedFilesBefore"], row["savedFilesAfter"]
        require(set(before) == set(after) and len(before) == 2, "Saved test file set differs")
        changed = [path for path in before if before[path] != after[path]]
        require(all(sha(owned(path)) == after[path] for path in after), "Saved file hashes differ")
        require(len(changed) == (1 if fault == "saved_bytes_changed" else 0), "Unexpected saved file change")
        if changed:
            require(Path(changed[0]).name == "B.psmodel", "Wrong opaque test file changed")
        operation = row["operationId"]
        if fault == "invalid_request_id":
            require(not re.fullmatch("[0-9a-f]{32}", operation)
                    and row["contextFactoryCalls"] == 0 and set(inventory) == {owned(path) for path in after} | {root / "responses" / (row["parameters"]["listingRequestId"] + ".json")}, "Invalid request wrote/attached")
            continue
        require(re.fullmatch("[0-9a-f]{32}", operation), "Invalid request token")
        journal = root / "operations" / (operation + ".json")
        once = root / "operations" / (operation + ".once")
        control = fault in {"control", "already_active"}
        if fault in {"publication_before_command", "publication_after_return"}:
            require(row["capturedPublicationError"] and once.is_file()
                    and (root / "operations" / (operation + ".json.tmp")).is_file(), "Interrupted publication evidence missing")
            if fault == "publication_before_command":
                require(not journal.exists(), "Fabricated pre-command journal")
            else:
                pending = json.loads(journal.read_text(encoding="utf-8-sig"))
                require(pending["phase"] == "before_activate" and pending["state"] == "context_change_outcome_pending", "Original pending journal overwritten")
        else:
            data = json.loads(journal.read_text(encoding="utf-8-sig"))
            require(data["operationId"] == operation and data["state"] == row["result"]["state"], "Legacy journal identity differs")
            require(data["geometryMutationAttempted"] is False, "Geometry flag changed")
            require(data["state"] == ("document_active_verified" if control else "context_change_outcome_unknown" if row["commandCalls"] == 1 else "rejected_before_context_change"), "Wrong observed outcome")
            if row["commandCalls"] == 1 or control:
                require(once.is_file(), "Once claim missing")
    return {"schema": "psbridge.activation-fault-audit.v1", "state": "passed", "rows": len(rows),
            "artifactHashesChecked": hashes, "cadConnectionAttempted": False,
            "scope": "Real activation core/injected context/legacy journal guard; not live CAD or full K7"}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--stage", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.stage.resolve())
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"PASS: {result['rows']} native activation rows independently audited; no CAD.")
