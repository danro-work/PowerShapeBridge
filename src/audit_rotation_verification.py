"""Audit fresh independent analytic witness proofs without CAD or fence authority."""
import argparse
import json
import math
from pathlib import Path
from audit_checkpoint_recovered_open import journal, read, require, sha

FAULTS = {'control', 'mixed_targets', 'negative_angle', 'wrong_angle', 'wrong_samples',
          'wrong_mapping', 'late_context', 'wrong_receipt_sha', 'wrong_before_sha',
          'wrong_summary_sha', 'changed_fence', 'partial_second', 'old_inspection', 'wrong_window'}
CONTROLS = {'control', 'mixed_targets', 'negative_angle'}


def quarter(vector, degrees, axis, direction=False):
    x, y, z = vector
    px, py = (0, 0) if direction else axis[:2]
    x, y = x-px, y-py
    return [px-y, py+x, z] if degrees == 90 else [px+y, py-x, z]


def audit(stage):
    require((stage / '.rotation-verification-fixture').is_file(), 'Own verifier marker missing')
    report = read(stage / 'reports/rotation-verification-tests.json')
    require(report['state'] == 'passed' and report['cadConnectionAttempted'] is False and
            report['fenceAuthorityAvailable'] is False and len(report['rows']) == len(FAULTS) and
            {row['fault'] for row in report['rows']} == FAULTS, 'Incomplete/wrong-scope matrix')
    count = 0
    for row in report['rows']:
        root = Path(row['root']).resolve()
        require(root.is_relative_to(stage / 'work') and row['cadConnectionAttempted'] is False, 'Foreign/live root')
        hashes = {Path(path).resolve(): value for path, value in row['artifactHashes'].items()}
        require(set(hashes) == {path.resolve() for path in root.rglob('*') if path.is_file()} and
                all(path.is_relative_to(root) and sha(path) == value for path, value in hashes.items()), 'Inventory/bytes changed')
        require(all(Path(path).resolve().is_relative_to(root) and sha(Path(path)) == value
                    for path, value in row['protectedHashes'].items()), 'Verifier changed original evidence')
        count += len(hashes)
        rid = row['executionId']
        receipt = read(root / 'responses' / (rid+'.json'))
        plan = read(root / 'rotationexecutions' / (rid+'.json'))
        states = journal(root, rid)
        require(states[-1] == ('outcome_unknown' if row['fault'] == 'partial_second' else 'executed_unverified') and
                'succeeded_verified' not in states, 'Unqualified verifier changed authoritative journal')
        fence = root / 'workingcopies' / plan['copyId'] / 'mutation-fence.json'
        require(fence.is_file() and row['verificationCalls'] == 0 and row['result']['copyFenceReleased'] is False and
                row['result']['nativeConventionQualified'] is False, 'Verification executed geometry/released fence')
        expected_calls = 3 if row['fault'] == 'mixed_targets' else 2 if row['fault'] == 'partial_second' else 1
        require(row['executionCalls'] == expected_calls, 'Execution repeated')
        diagnostic_path = root / 'rotationverifications' / (row['verificationId']+'.json')
        if row['fault'] not in CONTROLS:
            require(row['result']['state'] == 'owned_rotation_verification_rejected' and not diagnostic_path.exists(), 'Rejected data gained diagnostic success')
            continue
        require(row['result']['state'] == 'observed_rotation_samples_match_unqualified' and
                sha(diagnostic_path) == row['result']['diagnosticSha256'], 'Diagnostic SHA/state differs')
        diagnostic = read(diagnostic_path)
        require(diagnostic['executionRequestId'] == rid and diagnostic['executionReceiptSha256'] == sha(root / 'responses' / (rid+'.json')) and
                diagnostic['planSha256'] == sha(root / 'rotationexecutions' / (rid+'.json')) and
                diagnostic['copyFenceReleased'] is False and len(diagnostic['measuredTargets']) == len(plan['targets']), 'Diagnostic linkage differs')
        require(sha(fence) == receipt['executionFenceSha256'], 'Original fence changed')
        for item in diagnostic['measuredTargets']:
            index = item['index']
            before_path = root / 'rotationmeasurements' / rid / f'{index:06}.before.json'
            after_path = root / 'rotationmeasurements' / rid / f'{index:06}.after.json'
            require(sha(before_path) == item['beforeMeasurementSha256'] and sha(after_path) == item['recordedAfterMeasurementSha256'], 'Measurement SHA differs')
            before, after = read(before_path)['samples'], read(after_path)['samples']
            fresh = item['freshSamples']
            require(fresh == after and [fresh['type'], fresh['nativeId']] == item['entityKey'] == plan['targets'][index]['entityKey'], 'Fresh measurement target/value differs')
            for role in ('positions', 'directions'):
                require([v['key'] for v in before[role]] == [v['key'] for v in fresh[role]], 'Anchors differ')
                for original, actual in zip(before[role], fresh[role]):
                    expected = quarter(original['value'], plan['angleDegrees'], plan['nativeAxis'], role == 'directions')
                    require(all(math.isclose(a, b, abs_tol=1e-6) for a, b in zip(expected, actual['value'])), 'Independent analytic point/direction mismatch')
    return {'state': 'passed', 'cases': len(FAULTS), 'artifactHashesChecked': count,
            'cadConnectionAttempted': False, 'fenceAuthorityAvailable': False,
            'scope': 'Independent analytic sample diagnostics; actual native convention/live provider still required'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('stage', 'output'):
        parser.add_argument('--'+name, type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.stage.resolve())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(f"PASS: 14 independent rotation verification cases / {result['artifactHashesChecked']} hashes audited; no CAD/fence release.")
