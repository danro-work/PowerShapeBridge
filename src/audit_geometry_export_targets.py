"""Independent file/dependency audit. Opaque fixtures are never geometry evidence."""
import argparse
import json
from pathlib import Path

from audit_checkpoint_recovered_open import read, require, sha

FAULTS = {
    'control', 'mixed_mesh', 'rotation_returned', 'summary_tamper', 'returned_tamper', 'claim_tamper', 'unknown_fence', 'partial_rotation',
    'old_after_rotation', 'working_byte_capture', 'late_working', 'checkpoint_writable',
    'wrong_binding_sha', 'wrong_set_sha', 'extra_path', 'curve_target', 'expired',
    'nonempty_selection', 'reserved', 'existing_folder', 'lease_control', 'output_file',
    'output_directory', 'claimed', 'late_fence', 'wrong_target_path', 'wrong_keys',
    'wrong_expiry', 'replaced_directory', 'tampered_target',
}
PREPARED = {'control', 'mixed_mesh', 'rotation_returned', 'working_byte_capture',
            'late_working', 'lease_control', 'output_file', 'output_directory', 'claimed',
            'late_fence', 'wrong_target_path', 'wrong_keys', 'wrong_expiry',
            'replaced_directory', 'tampered_target'}
ACCEPTED = {'control', 'mixed_mesh', 'rotation_returned', 'working_byte_capture', 'lease_control'}


def audit(stage):
    require((stage / '.geometry-export-fixture').is_file(), 'Missing isolated marker')
    report = read(stage / 'reports/geometry-export-target-tests.json')
    require(report['state'] == 'passed' and report['cadConnectionAttempted'] is False and
            len(report['rows']) == len(FAULTS) and {row['fault'] for row in report['rows']} == FAULTS,
            'Incomplete fixture matrix')
    count = 0
    for row in report['rows']:
        fault = row['fault']
        root = Path(row['root']).resolve()
        require(root.is_relative_to(stage / 'work'), 'Fixture outside stage')
        hashes = {Path(path).resolve(): value for path, value in row['artifactHashes'].items()}
        require(set(hashes) == {path.resolve() for path in root.rglob('*') if path.is_file()} and
                all(path.is_relative_to(root) and sha(path) == value for path, value in hashes.items()),
                'Changed artifact inventory')
        for path, value in row['protectedHashes'].items():
            path = Path(path).resolve()
            require(path.is_relative_to(root), 'Protected file outside fixture')
            if not (fault == 'late_working' and path.name.startswith('Working_')):
                require(sha(path) == value, 'Unexpected protected file change')
        count += len(hashes)
        result = row['result']
        require(row['cadConnectionAttempted'] is False, 'CAD connection')
        for flag in ('executionEnabled', 'exportAttempted', 'geometryMutationAttempted',
                     'contextChangeAttempted', 'cadSaveAttempted', 'powerShapeConnectionAttempted', 'retryAllowed'):
            require(result[flag] is False, 'Unexpected execution authority')
        require(result['state'] == ('owned_geometry_export_target_prepared_files' if fault in PREPARED else
                                    'owned_geometry_export_target_rejected'), 'Wrong preparation state')
        require(row['leaseAccepted'] == (fault in ACCEPTED) if fault in PREPARED else
                row['leaseAccepted'] is None, 'Wrong lease result')
        if fault in PREPARED:
            plan = result['target']
            folder = root / 'geometryexports' / row['targetId']
            require(plan['targetPath'] == str(folder / ('Geometry_' + row['targetId'] + '.dmt')) and
                    plan['format'] == 'dmt' and plan['executionEnabled'] is False,
                    'Fixed disabled target lost')
            require(all(plan[key] == value for key, value in row['parameters'].items()), 'Input refs differ')
            require(all(key[0] in (40, 21) for key in plan['entityKeys']), 'Unsupported mesh target')
            binding_path = root / 'documentbindings' / (plan['bindingId'] + '.json')
            set_path = root / 'entitysets' / (plan['entitySetId'] + '.json')
            require(sha(binding_path) == plan['bindingSha256'] and sha(set_path) == plan['entitySetSha256'],
                    'Source references changed')
            binding = read(binding_path)
            entity_set = read(set_path)
            require(entity_set['entityKeys'] == plan['entityKeys'] and
                    min(binding['expiresUtc'], entity_set['expiresUtc']) == plan['expiresUtc'],
                    'Targets/minimum expiry differ')
            observation_path = root / 'responses' / (plan['inspectionRequestId'] + '.json')
            require(sha(observation_path) == plan['inspectionSha256'], 'Observation SHA differs')
            observation = read(observation_path)
            require(observation['contextBefore'] == observation['contextAfter'] == [0, 0] and
                    observation['selectionBefore'] == observation['selectionAfter'] == [], 'Context not empty/world')
            owner_path = root / 'workingcopies' / plan['copyId'] / 'ownership.json'
            require(sha(owner_path) == plan['ownershipSha256'], 'Owner SHA differs')
            owner = read(owner_path)
            require(plan['workingPath'] == owner['working']['path'] and
                    plan['checkpointPath'] == owner['checkpoint']['path'] and
                    sha(Path(plan['checkpointPath'])) == plan['checkpointSha256'], 'Copy/checkpoint differs')
            if fault != 'late_working':
                require(sha(Path(plan['workingPath'])) == plan['workingSha256'], 'Captured working bytes differ')
            if fault in ACCEPTED:
                require(sha(folder / 'target.json') == result['targetSha256'] and
                        read(folder / 'target.json') == plan and not Path(plan['targetPath']).exists(),
                        'Accepted target changed/export was performed')
            if fault == 'rotation_returned':
                fence = plan['fenceObservation']
                require(fence['present'] is True and fence['journalState'] == 'executed_unverified', 'Lost fence')
                events = [read(p) for p in sorted((root / 'operations' / fence['operationId']).glob('*.json'))]
                returned = next(e for e in events if e['state'] == 'command_returned')
                require(events[-1]['state'] == 'executed_unverified' and
                        returned['details']['allTargetsReturnedSuccess'] is True and
                        observation['timestampUtc'] > returned['timestampUtc'], 'Missing complete return/fresh source')
                fence_path = root / 'workingcopies' / plan['copyId'] / 'mutation-fence.json'
                require(sha(fence_path) == fence['fenceSha256'], 'Fence released/changed')
    return {'state': 'passed', 'cases': len(FAULTS), 'artifactHashesChecked': count,
            'cadConnectionAttempted': False, 'geometryExportExecuted': False}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--stage', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.stage.resolve())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f"PASS: {result['cases']} export target cases; {result['artifactHashesChecked']} hashes audited; no CAD/export.")
