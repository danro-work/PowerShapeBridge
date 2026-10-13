"""Independent ownership/plan/journal audit for fake-context native rotation preparation."""
import argparse
import json
from pathlib import Path

from audit_checkpoint_recovered_open import journal, read, require, sha

FAULTS = {'control', 'mixed_targets', 'wrong_axis', 'fractional_angle', 'wrong_targets', 'wrong_native_method',
          'wrong_requirements', 'expired', 'plan_sha', 'fenced_copy', 'changed_workplane',
          'changed_document', 'late_context', 'checkpoint_writable', 'busy_executor'}


def audit(stage):
    require((stage / '.owned-rotation-fixture').is_file(), 'Missing isolated fixture marker')
    report = read(stage / 'reports/owned-rotation-plan-tests.json')
    require(report['state'] == 'passed' and report['cadConnectionAttempted'] is False and
            len(report['rows']) == 15 and {row['fault'] for row in report['rows']} == FAULTS, 'Incomplete matrix/scope')
    count = 0
    for row in report['rows']:
        root = Path(row['root']).resolve()
        require(root.is_relative_to(stage / 'work'), 'Fixture outside stage')
        hashes = {Path(path).resolve(): value for path, value in row['artifactHashes'].items()}
        require(set(hashes) == {path.resolve() for path in root.rglob('*') if path.is_file()} and
                all(path.is_relative_to(root) and sha(path) == value for path, value in hashes.items()), 'Changed artifact inventory')
        require(all(Path(path).resolve().is_relative_to(root) and sha(Path(path)) == value
                    for path, value in row['protectedHashes'].items()), 'Original protected bytes changed')
        count += len(hashes)
        result = row['result']
        for flag in ('executionEnabled', 'geometryMutationAttempted', 'contextChangeAttempted', 'cadSaveAttempted'):
            require(result[flag] is False, 'Unexpected execution/action authority')
        require(result['state'] == ('owned_rotation_prepared_observed' if row['fault'] in {'control', 'mixed_targets'} else
                'owned_rotation_preparation_rejected'), 'Wrong state')
        if result.get('ownedRotationSha256'):
            packet_path = root / 'ownedrotations' / (row['requestId'] + '.json')
            require(sha(packet_path) == result['ownedRotationSha256'], 'Packet SHA differs')
            packet = read(packet_path)
            require(packet['executionEnabled'] is False and packet['ownedRotationId'] == row['requestId'], 'Wrong passive packet')
            require(all(packet[key] == value for key, value in row['parameters'].items()), 'Dependency IDs differ')
            require(journal(root, row['requestId']) == ['prepared'], 'Packet journal advanced beyond prepared')
            owner_path = root / 'workingcopies' / packet['copyId'] / 'ownership.json'
            require(sha(owner_path) == packet['ownershipSha256'], 'Owner SHA differs')
            owner = read(owner_path)
            work = Path(owner['working']['path']).resolve()
            checkpoint = Path(owner['checkpoint']['path']).resolve()
            require(work.is_relative_to(root) and checkpoint.is_relative_to(root) and
                    sha(work) == packet['workingSha256'] and sha(checkpoint) == owner['checkpoint']['sha256'],
                    'Owned working/checkpoint bytes differ')
            binding_path = root / 'documentbindings' / (packet['bindingId'] + '.json')
            require(sha(binding_path) == packet['bindingSha256'], 'Binding SHA differs')
        if row['fault'] in {'control', 'mixed_targets'}:
            require(result['sameContextObservationReads'] == 4 and result['guardPasses'] == 2,
                    'Missing repeated held-context guards')
            require(read(root / 'ownedrotations' / (row['requestId'] + '.json')) == result['ownedRotation'],
                    'Raw packet differs from receipt')
            if row['fault'] == 'mixed_targets':
                plan = read(root / 'rotationplans' / (row['parameters']['rotationPlanId'] + '.json'))
                require({key[0] for key in plan['entityKeys']} == {40, 21, 59}, 'Mixed target roles lost')
    return {'schema': 'psbridge.owned-rotation-plan-audit.v1', 'state': 'passed', 'cases': 15,
            'artifactHashesChecked': count, 'cadConnectionAttempted': False, 'nativeRotationExecuted': False}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--stage', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.stage.resolve())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f"PASS: 15 native owned rotation preparations, {result['artifactHashesChecked']} hashes audited; no CAD/execution.")
