"""Audit callback journals and retained bundle bytes; never certify CAD geometry."""
import argparse
import json
from pathlib import Path

from audit_owned_dgk_targets import read, sha

CONTROLS = {'control', 'curve', 'mixed', 'reordered_selection', 'all_companions'}
FAULTS = CONTROLS | {'inactive_level', 'ambiguous_name', 'unsafe_name', 'wrong_mapping', 'selected_keys_null',
                    'unsupported_provider', 'wrong_settings_frame', 'extra_settings', 'target_sha',
                    'busy_executor', 'busy_before', 'busy_after_select', 'claimed_context', 'select_throw', 'wrong_selection', 'wrong_selected_keys',
                    'late_document', 'retained_document', 'changed_windows', 'world_after_select',
                    'settings_after_select', 'selection_collision', 'export_throw', 'lost_reply', 'no_output',
                    'output_directory', 'missing_companion', 'malformed_dmt', 'stray_output',
                    'process_after_export', 'settings_after_export', 'clear_throw', 'clear_no_effect',
                    'output_write_during_clear', 'extra_during_clear', 'existing_export_fence'}


def audit(stage):
    assert (stage / '.owned-dgk-export-fixture').is_file()
    report = read(stage / 'reports/owned-dgk-export-tests.json')
    rows = report['rows']
    assert report['state'] == 'passed' and report['cadConnectionAttempted'] is False
    assert len(rows) == len(FAULTS) and {row['fault'] for row in rows} == FAULTS
    count = 0
    for row in rows:
        fault = row['fault']
        root = Path(row['root']).resolve()
        assert root.is_relative_to(stage / 'work')
        inventory = {Path(path).resolve(): digest for path, digest in row['artifactHashes'].items()}
        assert set(inventory) == {path.resolve() for path in root.rglob('*') if path.is_file()}
        assert all(path.is_relative_to(root) and sha(path) == digest for path, digest in inventory.items())
        assert all(sha(Path(path)) == digest for path, digest in row['protectedHashes'].items())
        count += len(inventory)
        result = row['result']
        calls = row['adds'] + row['exports'] + row['clears']
        assert result['callbackCallsAttempted'] == calls
        assert result['state'] == ('owned_dgk_export_executed_unverified' if fault in CONTROLS else
                                   'owned_dgk_export_outcome_unknown' if calls else
                                   'owned_dgk_export_rejected_before_change')
        for flag in ('geometryMutationAttempted', 'cadSaveAttempted', 'geometryVerified',
                     'nativeProviderQualified', 'nativeReturnCodeQualified', 'retryAllowed', 'copyFenceReleased'):
            assert result[flag] is False
        assert row['cadConnectionAttempted'] is False and row['exports'] <= 1 and row['clears'] <= 1
        if 'planSha256' not in result:
            assert calls == 0
            continue
        plan_path = root / 'dgkexportexecutions' / (row['operationId'] + '.json')
        assert sha(plan_path) == result['planSha256']
        plan = read(plan_path)
        assert plan['operationId'] == row['operationId'] and plan['operation'] == 'export_owned_dgk'
        assert plan['targetId'] == row['targetId']
        assert plan['parameters']['coordinateFrame'] == 'world' and plan['settings']['format'] == 'dgk'
        assert plan['settings']['providerQualification'] == 'synthetic_only'
        events = sorted((root / 'operations' / row['operationId']).glob('*.json'))
        previous = None
        states = []
        for index, path in enumerate(events):
            event = read(path)
            assert event['sequence'] == index and event['previousSha256'] == previous
            assert event['operationId'] == row['operationId'] and event['planSha256'] == result['planSha256']
            previous = sha(path)
            states.append(event['state'])
        assert states[-1] == result['recordedJournalState']
        if calls:
            assert (root / 'workingcopies' / plan['copyId'] / 'geometry-export-fence.json').is_file()
            fence = read(root / 'workingcopies' / plan['copyId'] / 'geometry-export-fence.json')
            assert fence['operationId'] == row['operationId'] and fence['planSha256'] == result['planSha256']
            claim = read(root / 'dgkexports' / row['targetId'] / 'export-once.json')
            assert claim['operationId'] == row['operationId'] and claim['automaticReplayForbidden'] is True
        if fault not in CONTROLS:
            assert 'succeeded_verified' not in states and 'executed_unverified' not in states
            if calls and not row['clears']:
                assert row['selectedKeys'], 'Unknown selection cleaned up automatically'
            continue
        assert states == ['prepared', 'execution_claimed', 'command_started', 'command_returned', 'executed_unverified']
        assert row['adds'] == len(plan['targets']) and row['exports'] == row['clears'] == 1
        assert row['selectedKeys'] == []
        folder = root / 'dgkexports' / row['targetId']
        summary_path = folder / 'export-summary.json'
        assert sha(summary_path) == result['summarySha256']
        summary = read(summary_path)
        assert summary['operationId'] == row['operationId'] and summary['planSha256'] == result['planSha256']
        assert summary['geometryVerified'] is False
        commands = summary['commands']
        expected_kinds = ['add_selection'] * len(plan['targets']) + ['export_dgk', 'clear_own_selection']
        assert [command['kind'] for command in commands] == expected_kinds
        for index, command in enumerate(commands):
            start_path = folder / (f'{index:06d}.started.json')
            return_path = folder / (f'{index:06d}.returned.json')
            assert sha(start_path) == command['startedSha256'] and sha(return_path) == command['returnedSha256']
            start, returned = read(start_path), read(return_path)
            assert start['operationId'] == returned['operationId'] == row['operationId']
            assert start['planSha256'] == returned['planSha256'] == result['planSha256']
            assert start['index'] == returned['index'] == command['index'] == index
            assert returned['startedSha256'] == command['startedSha256'] and returned['callbackReturned'] is True
        bundle = summary['bundle']
        assert bundle == result['bundle'] and bundle['cadGeometryVerified'] is False
        assert bundle['companionExecutionAttempted'] is False and bundle['ownershipAuthorized'] is False
        directory = Path(bundle['directory'])
        assert directory == folder / 'artifacts'
        assert {path.name for path in directory.iterdir()} == {item['name'] for item in bundle['artifacts']}
        for item in bundle['artifacts']:
            path = directory / item['name']
            assert sha(path) == item['sha256'] and path.stat().st_size == item['length']
            assert path.stat().st_ino == int(item['fileIdentity']['fileIndex'], 16)
            assert path.stat().st_nlink == 1
        assert bundle['totalBytes'] == sum(item['length'] for item in bundle['artifacts'])
        assert len(bundle['artifacts']) == (5 if fault == 'all_companions' else 1)
    return {'state': 'passed', 'cases': len(rows), 'controls': len(CONTROLS),
            'artifactHashesChecked': count, 'cadConnectionAttempted': False,
            'geometryVerified': False, 'nativeProviderQualified': False}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--stage', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.stage.resolve())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(f"PASS: {result['cases']} DGK callback cases / {result['artifactHashesChecked']} hashes audited; no CAD.")
