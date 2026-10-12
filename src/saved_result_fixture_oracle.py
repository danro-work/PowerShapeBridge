"""Test-only independent oracle for actual Framework saved-result proofs."""
import hashlib
import json
from pathlib import Path

from operation_plans import journal_events
from save_target_fixture_oracle import file_value, path_key, require, same_json, verify as target_proof


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify(root, receipt, parameters):
    ident = receipt['bridgeRequestId']
    raw = json.loads((root / 'responses' / (ident + '.json')).read_text(encoding='utf-8-sig'))
    require(same_json(receipt, raw))
    require(receipt['state'] == 'owned_save_as_verified_observed' and receipt['recordedJournalState'] == 'succeeded_verified')
    require(all(receipt[k] is True for k in ('copyFenceReleased', 'savedFilesUnchanged', 'noCommandReplay')))
    require(all(receipt[k] is False for k in ('cadSaveAttempted', 'geometryMutationAttempted', 'contextChangeAttempted')))
    require(receipt['saveOperationId'] == parameters['saveRequestId'] and receipt['saveReceiptSha256'] == parameters['saveReceiptSha256'])
    require(receipt['expiresUtc'] == parameters['expiresUtc'])
    original = file_value(root / 'responses' / (parameters['saveRequestId'] + '.json'), parameters['saveReceiptSha256'])
    require(original['state'] in ('owned_save_as_executed_unverified', 'owned_save_as_outcome_unknown'))
    require(original['saveCommandReturned'] is True and original['cadSaveAttempted'] is True and original['copyFenced'] is True)
    require(original['geometryMutationAttempted'] is False and original['contextChangeAttempted'] is True)
    plan = file_value(root / 'saveexecutions' / (parameters['saveRequestId'] + '.json'), original['planSha256'])
    require(same_json(plan, original['plan']) and plan['executionEnabled'] is True and type(plan['schemaVersion']) is int and plan['schemaVersion'] == 1)
    require(plan['operation'] == 'save_owned_document_as' and plan['operationId'] == parameters['saveRequestId'])
    target_path = root / 'savedresults' / plan['targetId'] / 'target.json'
    target = file_value(target_path, plan['targetSha256'])
    target_proof(root, {'targetId': plan['targetId'], 'targetSha256': plan['targetSha256'], 'target': target})
    for key in ('targetId', 'targetSha256', 'copyId', 'ownershipSha256'):
        require(receipt[key] == plan[key])
    require(receipt['planSha256'] == original['planSha256'] and same_json(receipt['savedResult'], original['savedResult']))
    require(all(plan[k] == target[k] == original[k] for k in ('copyId', 'ownershipSha256', 'resultPath')))
    claim = json.loads((target_path.parent / 'save-once.json').read_text(encoding='utf-8-sig'))
    require(same_json(claim, {'operationId': parameters['saveRequestId'], 'planSha256': original['planSha256'], 'targetId': plan['targetId'], 'automaticReplayForbidden': True}))
    journal = journal_events(root, parameters['saveRequestId'])
    require(journal['state'] == 'succeeded_verified' and not journal['writerLockPresent'] and journal['eventCount'] == 6)
    require(journal['planSha256'] == original['planSha256'])
    returned = [event for event in journal['events'] if event['state'] == 'command_returned']
    require(len(returned) == 1 and same_json(returned[0]['details'], {'nativeRequestId': parameters['saveRequestId'], 'saveCommandReturned': True}))
    require(not (root / 'workingcopies' / plan['copyId'] / 'mutation-fence.json').exists())
    file_proof = receipt['savedResult']
    require(set(file_proof) == {'path', 'sha256', 'length', 'fileIdentity'})
    require(set(file_proof['fileIdentity']) == {'volumeSerial', 'fileIndex', 'creationTime'})
    path = Path(plan['resultPath'])
    require(path_key(file_proof['path']) == path_key(str(path)))
    require(type(file_proof['length']) is int and 0 < file_proof['length'] <= 1073741824 and path.stat().st_size == file_proof['length'])
    require(sha(path) == file_proof['sha256'])
    inspection = file_value(root / 'responses' / (parameters['inspectionRequestId'] + '.json'), parameters['inspectionSha256'])
    listing = file_value(root / 'responses' / (parameters['listingRequestId'] + '.json'), parameters['listingSha256'])
    require(same_json(receipt['model'], inspection['model']) and receipt['model']['id'] == parameters['modelId'])
    require(path_key(receipt['model']['path']) == path_key(str(path)))
    require(listing['activeWindow']['modelId'] == parameters['modelId'] and listing['activeWindow']['windowNumber'] == parameters['windowNumber'])
    require(listing['activeWindow']['modelName'] == receipt['model']['name'] and listing['activeWindow']['modelPath'] == receipt['model']['path'])
    evidence = [{'requestId': parameters[ident_key], 'sha256': parameters[sha_key]} for ident_key, sha_key in
                [('saveRequestId', 'saveReceiptSha256'), ('inspectionRequestId', 'inspectionSha256'), ('listingRequestId', 'listingSha256')]]
    require(same_json(receipt['evidence'], evidence))
    terminal = journal['events'][-1]['details']
    require(terminal['noCommandReplay'] is True and terminal['savedResultSha256'] == file_proof['sha256'])
    require(len(terminal['evidence']) == 3 and terminal['evidence'][0] == evidence[0])
    for item in terminal['evidence']:
        file_value(root / 'responses' / (item['requestId'] + '.json'), item['sha256'])
