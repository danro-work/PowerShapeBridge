"""Independent filesystem audit of real SDK/fake native transport fault rows."""
import argparse
import hashlib
import json
import shutil
from pathlib import Path


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def expected_rows():
    rows = set()
    for recipe in ("execute_owned_translation", "open_working_copy"):
        rows |= {(recipe, fault) for fault in ("control", "claimed_timeout", "returned_reply_lost", "late_receipt")}
    for recipe in ("execute_owned_save_as", "execute_owned_saved_reopen"):
        rows |= {(recipe, fault) for fault in ("claimed_timeout", "returned_reply_lost", "late_receipt")}
        rows |= {(recipe, "cancel_" + signal + "_" + phase)
                 for signal in ("local", "notified") for phase in ("pending", "returned")}
    for recipe in ("execute_owned_translation", "open_working_copy", "activate_document"):
        rows |= {(recipe, "cancel_" + signal + "_pending") for signal in ("local", "notified")}
    for recipe in ("execute_owned_translation", "open_working_copy"):
        rows |= {(recipe, "cancel_" + signal + "_returned") for signal in ("local", "notified")}
    return rows


def audit(folder):
    seen = set()
    file_count = 0
    for report_path in folder.glob("*.json"):
        row = json.loads(report_path.read_text(encoding="utf-8"))
        require(row["schema"] == "psbridge.sdk-transport-fault.v1", "Invalid row schema")
        key = (row["recipe"], row["fault"])
        require(key not in seen, "Duplicate row: " + repr(key))
        seen.add(key)
        require(row["cadConnectionAttempted"] is False and row["newAuthorityGranted"] is False
                and row["replayRejected"] is True and row["syntheticNativeProof"] is True, "Invalid scope flags")
        require(row["artifactArchive"] == row["requestId"] + "/artifacts", "Unexpected archive identity")
        root = (folder / row["artifactArchive"]).resolve()
        require(root.is_relative_to(folder) and root.is_dir(), "Expected an archived row artifact directory")
        files = {}
        for item in row["files"]:
            path = (root / item["path"]).resolve()
            require(path.is_relative_to(root) and path not in files, "Unsafe or duplicate artifact path")
            require(path.is_file() and sha(path) == item["sha256"], "Changed artifact: " + str(path))
            files[path] = item["sha256"]
        require(set(files) == {path.resolve() for path in root.rglob("*") if path.is_file()},
                "Artifact inventory differs")
        file_count += len(files)
        operation = row["requestId"]
        require(len(operation) == 32 and all(c in "0123456789abcdef" for c in operation), "Invalid operation ID")
        requests = list((root / "requests").iterdir())
        require(len(requests) == 1 and requests[0].name == operation + ".json.processing", "Request replay or claim loss")
        envelope = json.loads(requests[0].read_text(encoding="utf-8"))
        require(envelope["requestId"] == operation and envelope["operation"] == "recipe.prepare"
                and envelope["arguments"]["recipeId"] == row["recipe"], "Claimed request identity differs")
        returned = row["fault"] in ("control", "returned_reply_lost", "late_receipt") or row["fault"].endswith("_returned")
        published = row["fault"] in ("control", "late_receipt")
        response = root / "responses" / (operation + (".json" if published else ".unpublished"))
        require((root / "responses" / (operation + ".json")).is_file() == published, "Unexpected response publication")
        if returned:
            receipt = json.loads(response.read_text(encoding="utf-8"))
            require(receipt["operationId"] == operation and receipt["bridgeRequestId"] == operation, "Raw receipt identity differs")
            journal = sorted((root / "operations" / operation).glob("*.json"))
            require(len(journal) == 5, "Unexpected journal length")
            previous = None
            states = []
            for sequence, path in enumerate(journal):
                event = json.loads(path.read_text(encoding="utf-8"))
                require(event["sequence"] == sequence and event["previousSha256"] == previous
                        and event["operationId"] == operation and event["planSha256"] == receipt["planSha256"], "Journal identity/hash chain differs")
                if event["state"] == "command_returned":
                    require(event["details"]["nativeRequestId"] == operation, "Journal return identity differs")
                    if row["recipe"] == "execute_owned_translation":
                        require(type(event["details"]["returnCode"]) is int and event["details"]["returnCode"] == 1, "Journal return code differs")
                    if row["recipe"] == "execute_owned_save_as":
                        require(event["details"]["saveCommandReturned"] is True, "Journal SaveAs return differs")
                states.append(event["state"])
                previous = sha(path)
            require(states == ["prepared", "execution_claimed", "command_started", "command_returned", "executed_unverified"], "Authoritative journal phases differ")
            plans = "ownedexecutions" if row["recipe"] == "execute_owned_translation" else "saveexecutions" if row["recipe"] == "execute_owned_save_as" else "copyopenplans"
            plan_path = root / plans / (operation + ".json")
            require(sha(plan_path) == receipt["planSha256"] and json.loads(plan_path.read_text(encoding="utf-8")) == receipt["plan"], "Plan proof differs")
            claims = list(root.rglob("*.execution-once.json")) if row["recipe"] == "execute_owned_translation" else list((root / "savedresults").rglob("save-once.json")) if row["recipe"] == "execute_owned_save_as" else list((root / "workingcopies").rglob("open-once.json"))
            matching = [json.loads(path.read_text(encoding="utf-8")) for path in claims]
            matching = [claim for claim in matching if claim.get("operationId") == operation]
            require(len(matching) == 1 and matching[0]["planSha256"] == receipt["planSha256"]
                    and matching[0]["automaticReplayForbidden"] is True, "Persistent claim differs")
            if row["recipe"] in ("execute_owned_translation", "execute_owned_save_as"):
                fence = json.loads((root / "workingcopies" / receipt["plan"]["copyId"] / "mutation-fence.json").read_text(encoding="utf-8"))
                require(fence["operationId"] == operation and fence["planSha256"] == receipt["planSha256"], "Mutation fence differs")
        else:
            require(not response.exists() and not (root / "operations" / operation).exists(), "Pending row acquired invented native proof")
        expected = "client_wait_cancelled" if row["fault"].startswith("cancel_") else "owned_save_as_outcome_unknown" if row["recipe"] == "execute_owned_save_as" else "timeout_outcome_unknown"
        if row["fault"] == "control":
            expected = "owned_translation_executed_unverified" if row["recipe"] == "execute_owned_translation" else "working_copy_opened_observed"
        require(row["state"] == expected, "Client outcome differs")
    require(seen == expected_rows(), "Missing/extra rows: " + repr(seen.symmetric_difference(expected_rows())))
    return {"schema": "psbridge.sdk-transport-audit.v1", "state": "passed", "rows": len(seen),
            "artifactHashesChecked": file_count, "cadConnectionAttempted": False,
            "scope": "SDK claimed request, retained synthetic native proofs and saved artifacts; not CAD postconditions or full K7"}


def exercise_rejections(folder):
    destination = folder.parent / "transport-audit-negative"
    require(not destination.exists(), "Negative audit checks require a fresh directory")
    cases = {"missing_row": "Missing/extra rows", "changed_hash": "Changed artifact",
             "extra_artifact": "Artifact inventory differs", "forged_journal": "Journal return identity differs"}
    for fault, expected in cases.items():
        clone = destination / fault
        shutil.copytree(folder, clone)
        reports = sorted(clone.glob("*.json"))
        report_path = reports[0]
        row = json.loads(report_path.read_text(encoding="utf-8"))
        if fault == "missing_row":
            require(report_path.resolve().is_relative_to(clone.resolve()), "Unexpected deletion target")
            report_path.unlink()
        elif fault == "changed_hash":
            row["files"][0]["sha256"] = "0" * 64
            report_path.write_text(json.dumps(row), encoding="utf-8")
        elif fault == "extra_artifact":
            (clone / row["artifactArchive"] / "unexpected-file.txt").write_text("opaque negative fixture", encoding="utf-8")
        else:
            report_path, row = next((path, json.loads(path.read_text(encoding="utf-8"))) for path in reports
                                    if json.loads(path.read_text(encoding="utf-8"))["fault"] == "returned_reply_lost")
            relative = "operations/" + row["requestId"] + "/000003.json"
            path = clone / row["artifactArchive"] / relative
            event = json.loads(path.read_text(encoding="utf-8"))
            event["details"]["nativeRequestId"] = "f" * 32
            path.write_text(json.dumps(event), encoding="utf-8")
            next(item for item in row["files"] if item["path"].replace("\\", "/") == relative)["sha256"] = sha(path)
            report_path.write_text(json.dumps(row), encoding="utf-8")
        try:
            audit(clone)
        except ValueError as error:
            require(expected in str(error), f"Wrong rejection for {fault}: {error}")
        else:
            raise ValueError("Corrupted fixture accepted: " + fault)
    return len(cases)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--exercise-rejections", action="store_true")
    args = parser.parse_args()
    result = audit(args.evidence.resolve())
    if args.exercise_rejections:
        result["negativeAuditChecks"] = exercise_rejections(args.evidence.resolve())
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"PASS: {result['rows']} SDK transport rows independently audited; no CAD, full K7 remains incomplete.")
