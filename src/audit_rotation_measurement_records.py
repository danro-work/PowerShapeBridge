"""Independent immutable witness record rejection/byte-preservation audit."""
import argparse
import json
from pathlib import Path
from audit_checkpoint_recovered_open import read, require, sha

FAULTS = {'control', 'extra_record_field', 'wrong_schema', 'wrong_phase', 'wrong_index',
          'wrong_provenance', 'wrong_descriptor', 'extra_sample_field', 'bool_numeric',
          'duplicate_anchor', 'bad_vector', 'nonfinite_text', 'wrong_sha', 'foreign_execution',
          'rewrite', 'held_tamper'}


def audit(stage):
    require((stage / '.rotation-measurement-record-fixture').is_file(), 'Missing own marker')
    report = read(stage / 'reports/rotation-measurement-record-tests.json')
    require(report['state'] == 'passed' and report['cadConnectionAttempted'] is False and
            len(report['rows']) == 16 and {r['fault'] for r in report['rows']} == FAULTS, 'Incomplete record matrix')
    count = 0
    for row in report['rows']:
        root = Path(row['root']).resolve()
        require(root.is_relative_to(stage / 'work') and row['cadConnectionAttempted'] is False, 'Foreign/live fixture')
        hashes = {Path(path).resolve(): value for path, value in row['artifactHashes'].items()}
        require(set(hashes) == {path.resolve() for path in root.rglob('*') if path.is_file()} and
                all(path.is_relative_to(root) and sha(path) == value for path, value in hashes.items()), 'File inventory/bytes changed')
        count += len(hashes)
        require(row['accepted'] == (row['fault'] == 'control') and (row['error'] is None) == row['accepted'], 'Wrong rejection decision')
        record_path = root / 'rotationmeasurements' / row['executionId'] / '000000.before.json'
        require(sha(record_path) == row['recordSha256'], 'Own tested record was overwritten')
        if row['fault'] in {'control', 'rewrite', 'held_tamper', 'wrong_sha', 'foreign_execution'}:
            record = read(record_path)
            require(record['schemaVersion'] == 1 and record['executionRequestId'] == row['executionId'] and
                    record['phase'] == 'before' and record['index'] == 0, 'Original record identity changed')
            require(set(record['samples']) == {'type', 'nativeId', 'frame', 'units', 'descriptorSha256',
                    'positions', 'directions', 'invariants', 'boundsWorldRawPaired'}, 'Original sample schema changed')
    return {'state': 'passed', 'cases': 16, 'artifactHashesChecked': count, 'cadConnectionAttempted': False,
            'scope': 'Immutable record/held byte checks only; no geometry or verification authority'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('stage', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.stage.resolve())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(f"PASS: 16 measurement record cases / {result['artifactHashesChecked']} hashes audited; no CAD.")
