"""Independent saved artifact audit of K5. No COM/queue/UI or mutations."""
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


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--bridge-root', required=True)
    parser.add_argument('--evidence', required=True)
    parser.add_argument('--protected-before', required=True)
    args = parser.parse_args()
    root, evidence = Path(args.bridge_root).resolve(), Path(args.evidence).resolve()
    report = read(evidence / 'qualification.json')
    assert report['state'] == 'passed_new_owned_sdk_scenario'
    calls, receipts = [], {}
    for record in report['records']:
        response = read(Path(record['path']))
        assert not response['result']['isError']
        identity = record.get('bridgeRequestId')
        if not identity:
            continue
        path = root / 'responses' / (identity + '.json')
        assert sha(path) == record['nativeReceiptSha256']
        native = read(path)
        envelope = read(root / 'requests' / (identity + '.json.processing.done'))
        assert envelope['requestId'] == identity and envelope['operation'] == 'recipe.prepare'
        calls.append(envelope['arguments']['recipeId'])
        receipts[identity] = native
        parameters = envelope['arguments']['parameters']
        for prefix, folder in [('inspection', 'responses'), ('listing', 'responses'), ('operationPlan', 'operationplans'),
                               ('binding', 'documentbindings'), ('ownedOperation', 'ownedoperations')]:
            key = parameters.get(prefix + 'RequestId', parameters.get(prefix + 'Id'))
            if key:
                assert sha(root / folder / (key + '.json')) == parameters[prefix + 'Sha256']
        if record['tool'] not in ('open_working_copy', 'activate_document'):
            assert native.get('contextChangeAttempted', False) is False
        assert native.get('cadSaveAttempted', False) is False
        assert native.get('geometryMutationAttempted', False) is (record['tool'] == 'execute_owned_translation')
    assert calls.count('open_working_copy') == calls.count('execute_owned_translation') == 1
    assert calls.count('verify_working_copy_open') == calls.count('verify_owned_translation') == 1
    journals = {}
    for key, folder in [('openOperationId', 'copyopenplans'), ('executionOperationId', 'ownedexecutions')]:
        identity = report[key]
        native = receipts[identity]
        assert sha(root / folder / (identity + '.json')) == native['planSha256']
        assert read(root / folder / (identity + '.json')) == native['plan']
        journal = journal_events(root, identity)
        assert journal['state'] == 'succeeded_verified' and journal['eventCount'] == 6 and not journal['retryAllowed']
        journals[identity] = journal['state']
    execution = receipts[report['executionOperationId']]
    assert type(execution['returnCode']) is int and execution['returnCode'] == 1 and execution['commandCalls'] == 1
    assert execution['recordedJournalState'] == 'executed_unverified' and execution['copyFenced']
    copy = root / 'workingcopies' / report['copyId']
    assert not (copy / 'mutation-fence.json').exists()
    ownership = read(copy / 'ownership.json')
    assert sha(copy / 'ownership.json') == receipts[report['openOperationId']]['ownershipSha256']
    assert ownership['copyId'] == report['copyId'] == execution['copyId']
    assert read(copy / 'open-once.json')['operationId'] == report['openOperationId']
    assert ownership['schemaVersion'] == 2
    assert Path(ownership['working']['path']).name == 'Working_' + report['copyId'] + '.psmodel'
    assert sha(Path(ownership['working']['path'])) == ownership['working']['initialSha256']
    assert sha(Path(ownership['checkpoint']['path'])) == ownership['checkpoint']['sha256']
    assert Path(ownership['checkpoint']['path']).stat().st_file_attributes & 1
    final_listing = read(Path(report['records'][-1]['path']))['result']['structuredContent']
    before_listing = read(Path(next(record['path'] for record in report['records'] if record['tool'] == 'list_documents')))['result']['structuredContent']
    assert final_listing['windowCount'] == before_listing['windowCount'] + 1
    active = next(document for document in final_listing['documents'] if document['isActive'])
    assert Path(active['path']) == Path(report['originalActivePath'])
    protected = read(Path(args.protected_before))
    assert all(sha(Path(path)) == expected for path, expected in protected.items())
    result = dict(state='passed', nativeReceipts=len(receipts), openCalls=1, translationCalls=1,
        journals=journals, protectedFilesUnchanged=len(protected), copyId=report['copyId'],
        savedWorkingAndCheckpointUnchanged=True, fenceReleasedByVerifier=True,
        auditCadConnectionAttempted=False, auditQueueSubmissionAttempted=False)
    (evidence / 'independent-audit.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print('PASS: independent K5 native receipt/proof/journal/claim/bytes audit; no CAD or queue submission.')
