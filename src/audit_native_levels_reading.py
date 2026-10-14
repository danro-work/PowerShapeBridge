"""Audit isolated passive level getter cases; never connects to CAD."""
import argparse
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--stage", required=True, type=Path)
parser.add_argument("--output", required=True, type=Path)
args = parser.parse_args()
source = args.stage / "reports/native-levels-fixture.json"
data = source.read_bytes()
rows = json.loads(data.decode("utf-8-sig"))
controls = {"control", "lower_bound", "double_flags"}
refusals = {"null_fields", "null_names", "bad_rank", "wrong_count", "wrong_name_type", "null_name", "long_name",
            "wrong_used_type", "fractional_flag", "nan_flag", "infinite_flag", "bad_active_bit", "null_filters",
            "wrong_filter_count", "boolean_filter", "filter_drift", "level_drift", "scalar_drift", "observed_level_mismatch",
            "missing_observed_levels", "foreign_level", "document", "windows", "windows_final", "expired", "retained"}
assert len(rows) == 29 and {row["fault"] for row in rows} == controls | refusals
for row in rows:
    assert row["cadConnectionAttempted"] is False
    assert row["state"] == ("native_levels_read_unqualified" if row["fault"] in controls else "native_levels_read_rejected")
    if row["fault"] in controls:
        assert row["error"] is None
        assert row["fieldsReads"] == row["filterReads"] == row["observations"] == 2
    else:
        assert isinstance(row["error"], str) and row["error"]
report = {"state": "passed", "caseCount": len(rows), "controls": len(controls), "refusals": len(refusals),
          "cadConnectionAttempted": False, "reportPath": str(source), "reportSha256": hashlib.sha256(data).hexdigest()}
with args.output.open("x", encoding="utf-8") as file:
    json.dump(report, file, indent=2)
print(json.dumps(report))
