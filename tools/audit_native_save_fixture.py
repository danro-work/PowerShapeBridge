"""Read actual Framework SaveAs fixture artifacts; no COM, queue or CAD commands."""
import argparse
import hashlib
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'src'))
from operation_plans import journal_events


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def audit(stage):
    cases = []
    for plan_path in sorted(stage.glob('work/owned-operation-self-test-*/saveexecutions/*.json')):
        root = plan_path.parent.parent
        plan = read(plan_path)
        operation_id = plan_path.stem
        journal = journal_events(root, operation_id)
        if journal['recordedState'] != 'succeeded_verified':
            continue
        assert journal['state'] == 'succeeded_verified' and journal['retryAllowed'] is False
        assert not journal['writerLockPresent'] and journal['eventCount'] == 6
        assert journal['planSha256'] == sha(plan_path)
        events = journal['events']
        assert [e['state'] for e in events] == ['prepared', 'execution_claimed', 'command_started', 'command_returned', 'executed_unverified', 'succeeded_verified']
        assert events[3]['details'] == {'nativeRequestId': operation_id, 'saveCommandReturned': True}
        receipt_path = root / 'responses' / (operation_id + '.json')
        receipt = read(receipt_path)
        assert receipt['operationId'] == operation_id and receipt['plan'] == plan
        assert receipt['planSha256'] == sha(plan_path)
        assert receipt['state'] == 'owned_save_as_executed_unverified' and receipt['commandCalls'] == 1
        assert receipt['cadSaveAttempted'] is True and receipt['saveCommandReturned'] is True
        assert receipt['geometryMutationAttempted'] is False and receipt['copyFenced'] is True
        target_path = root / 'savedresults' / plan['targetId'] / 'target.json'
        target = read(target_path)
        assert sha(target_path) == plan['targetSha256'] and target['executionEnabled'] is False
        result_path = root / 'savedresults' / plan['targetId'] / ('Result_' + plan['targetId'] + '.psmodel')
        assert Path(plan['resultPath']) == result_path and target['resultPath'] == plan['resultPath']
        assert sha(result_path) == receipt['savedResult']['sha256'] == events[-1]['details']['savedResultSha256']
        assert result_path.stat().st_size == receipt['savedResult']['length'] > 0
        claim = read(target_path.parent / 'save-once.json')
        assert claim == {'operationId': operation_id, 'planSha256': sha(plan_path), 'targetId': plan['targetId'], 'automaticReplayForbidden': True}
        copy_folder = root / 'workingcopies' / plan['copyId']
        assert not (copy_folder / 'mutation-fence.json').exists()
        assert sha(copy_folder / 'ownership.json') == plan['ownershipSha256'] == target['ownershipSha256']
        assert sha(Path(target['workingPath'])) == target['workingSha256']
        assert sha(Path(target['checkpointPath'])) == target['checkpointSha256']
        for entry in events[-1]['details']['evidence']:
            assert sha(root / 'responses' / (entry['requestId'] + '.json')) == entry['sha256']
        for saved_path, expected_sha in receipt['savedFileHashesBefore'].items():
            assert sha(Path(saved_path)) == expected_sha
        cases.append({'root': str(root), 'operationId': operation_id, 'journalSha256': journal['journalSha256'],
                      'receiptSha256': sha(receipt_path), 'resultSha256': sha(result_path), 'commandStartedEvents': 1,
                      'commandReturnedEvents': 1, 'checkpointUnchanged': True, 'claimRetained': True, 'fenceReleased': True})
    assert len(cases) == 1, 'Expected one independently verified synthetic SaveAs scenario'
    return {'state': 'passed_native_save_fixture_audit', 'scope': 'Actual opaque synthetic files/journal/raw receipt hashes; not live CAD or topology',
            'powerShapeConnectionAttempted': False, 'runtimeDeploymentPerformed': False, 'cases': cases}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--stage', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    result = audit(Path(args.stage).resolve())
    output = Path(args.output).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print('PASS: independent native SaveAs saved-byte/receipt/journal audit; no CAD.')
