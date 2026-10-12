"""Independent test-only oracle for actual Framework saved-reopen verification."""
import hashlib
import json
from pathlib import Path

from operation_plans import journal_events
from save_target_fixture_oracle import file_value, path_key, require, same_json
from saved_reopen_fixture_oracle import verify as prepared_proof


def verify(root, receipt, parameters):
    raw = json.loads((root / 'responses' / (receipt['bridgeRequestId'] + '.json')).read_text(encoding='utf-8-sig'))
    require(same_json(raw, receipt))
    require(receipt['state'] == 'owned_saved_reopen_verified_observed' and receipt['recordedJournalState'] == 'succeeded_verified')
    require(all(receipt[k] is False for k in ('cadSaveAttempted', 'geometryMutationAttempted', 'contextChangeAttempted', 'executionEnabled')))
    require(all(receipt[k] is True for k in ('noCommandReplay', 'savedFilesUnchanged', 'observableContentChecked')))
    operation = parameters['reopenRequestId']
    require(receipt['reopenOperationId'] == operation and receipt['reopenReceiptSha256'] == parameters['reopenReceiptSha256'] and receipt['expiresUtc'] == parameters['expiresUtc'])
    original = file_value(root / 'responses' / (operation + '.json'), parameters['reopenReceiptSha256'])
    require(original['state'] in ('owned_saved_reopen_executed_unverified', 'owned_saved_reopen_outcome_unknown'))
    require(original['contextChangeAttempted'] is True and original['cadSaveAttempted'] is False and original['geometryMutationAttempted'] is False)
    plan = file_value(root / 'copyopenplans' / (operation + '.json'), original['planSha256'])
    require(same_json(plan, original['plan']) and plan['operation'] == 'open_owned_saved_result' and plan['operationId'] == operation and plan['geometryExecutionEnabled'] is False)
    ident = plan['reopenId']
    prepared = file_value(root / 'savedreopens' / ident / 'reopen.json', plan['reopenSha256'])
    prepared_proof(root, {'reopenId': ident, 'reopenSha256': plan['reopenSha256'], 'reopen': prepared}, unclaimed=False)
    for key in ('reopenId', 'reopenSha256', 'copyId', 'ownershipSha256'):
        require(plan[key] == original[key] == receipt[key])
    require(plan['workingPath'] == prepared['workingPath'] and receipt['sourceSavedResultSha256'] == original['sourceSavedResultSha256'] == prepared['savedResult']['sha256'])
    claim = json.loads((root / 'workingcopies' / plan['copyId'] / 'open-once.json').read_text(encoding='utf-8-sig'))
    require(same_json(claim, {'operationId': operation, 'planSha256': original['planSha256'], 'copyId': plan['copyId'], 'ownershipSha256': plan['ownershipSha256'], 'automaticReplayForbidden': True}))
    journal = journal_events(root, operation)
    require(journal['state'] == 'succeeded_verified' and not journal['writerLockPresent'] and journal['eventCount'] == 6)
    returned = [event for event in journal['events'] if event['state'] == 'command_returned']
    require(len(returned) == 1 and same_json(returned[0]['details'], {'nativeRequestId': operation}))
    expected = file_value(root / 'responses' / (parameters['inspectionRequestId'] + '.json'), parameters['inspectionSha256'])
    listing = file_value(root / 'responses' / (parameters['listingRequestId'] + '.json'), parameters['listingSha256'])
    require(same_json(receipt['model'], expected['model']) and receipt['model']['id'] == parameters['modelId'])
    require(path_key(receipt['model']['path']) == path_key(plan['workingPath']) and listing['activeWindow']['modelId'] == parameters['modelId'] and listing['activeWindow']['windowNumber'] == parameters['windowNumber'])
    evidence = [{'requestId': parameters[a], 'sha256': parameters[b]} for a, b in [('reopenRequestId', 'reopenReceiptSha256'), ('inspectionRequestId', 'inspectionSha256'), ('listingRequestId', 'listingSha256')]]
    terminal = journal['events'][-1]['details']
    require(same_json(receipt['evidence'], evidence) and terminal['noCommandReplay'] is True and terminal['ownedModelId'] == parameters['modelId'] and terminal['sourceSavedResultSha256'] == receipt['sourceSavedResultSha256'])
    require(len(terminal['evidence']) == 3 and same_json(terminal['evidence'][0], evidence[0]))
    for entry in terminal['evidence']:
        file_value(root / 'responses' / (entry['requestId'] + '.json'), entry['sha256'])
    binding = file_value(root / 'documentbindings' / (receipt['bindingId'] + '.json'), receipt['bindingSha256'])
    require(receipt['bindingId'] == receipt['bridgeRequestId'] and same_json(binding, receipt['binding']))
    require(type(binding['schemaVersion']) is int and binding['schemaVersion'] == 1 and binding['executionEnabled'] is False)
    require(all(binding[k] == plan[k] for k in ('copyId', 'ownershipSha256')))
    require(all(binding[k] == parameters[k] for k in ('inspectionRequestId', 'inspectionSha256', 'expiresUtc')))
    for path, digest in original['savedFileHashesBefore'].items():
        require(hashlib.sha256(Path(path).read_bytes()).hexdigest() == digest)
