"""Independent audit of isolated Framework fault artifacts; never CAD or cleanup."""
import argparse
import hashlib
import json
from pathlib import Path
import re

from operation_plans import journal_events

OPERATIONS = {'translate_owned_solid', 'save_owned_document_as',
              'open_working_copy', 'open_owned_saved_result'}
FAULTS = {'control', 'lost_return_after_effect', 'lost_response',
          'partial_journal', 'changed_context', 'non_success'}

def require(ok, reason):
    if not ok:
        raise ValueError(reason)

def audit(stage):
    stage = Path(stage).resolve()
    require((stage / '.recovery-fault-fixture').is_file(), 'Not an isolated fault stage.')
    raw = (stage / 'reports/recovery-fault-matrix.json').read_bytes()
    report = json.loads(raw.decode('utf-8-sig'))
    require(report['schema'] == 'psbridge.recovery-fault-matrix.v1', 'Wrong schema.')
    expected = {(op, fault) for op in OPERATIONS for fault in FAULTS}
    expected.add(('translate_owned_solid', 'non_success_minus_one'))
    actual = [(row['operation'], row['fault']) for row in report['rows']]
    require(len(actual) == len(expected) and set(actual) == expected, 'Incomplete/duplicate native fault cells.')
    checked = []
    for row in report['rows']:
        root = Path(row['root']).resolve()
        require(root.is_relative_to(stage / 'work'), 'Fault root is outside its stage.')
        def owned(path):
            path = Path(path).resolve()
            require(path.is_relative_to(root), 'Artifact outside its fault root.')
            return path
        operation_id = row['operationId']
        require(re.fullmatch('[0-9a-f]{32}', operation_id), 'Invalid operation identity.')
        journal = journal_events(root, operation_id)
        require(journal['planSha256'] == row['execution']['planSha256'], 'Journal/plan SHA differs.')
        plan_folder = ('ownedexecutions' if row['operation'] == 'translate_owned_solid' else
                       'saveexecutions' if row['operation'] == 'save_owned_document_as' else 'copyopenplans')
        plan_raw = (root / plan_folder / (operation_id + '.json')).read_bytes()
        require(hashlib.sha256(plan_raw).hexdigest() == journal['planSha256'], 'Raw plan hash differs.')
        plan = json.loads(plan_raw.decode('utf-8-sig'))
        require(plan['operationId'] == operation_id and plan['operation'] == row['operation'] and
                plan['copyId'] == row['copyId'], 'Wrong plan owner/operation.')
        starts = [event for event in journal['events'] if event['state'] == 'command_started']
        returns = [event for event in journal['events'] if event['state'] == 'command_returned']
        require(len(starts) == 1 and len(returns) <= 1 and type(row['commandCalls']) is int and row['commandCalls'] == 1, 'Command count/return count differs.')
        require(row['cadConnectionAttempted'] is False, 'Scope is not offline.')
        claim = owned(row['claimPath'])
        require(hashlib.sha256(claim.read_bytes()).hexdigest() == row['claimSha256'], 'Claim bytes differ.')
        claim_data = json.loads(claim.read_text(encoding='utf-8-sig'))
        require(claim_data['operationId'] == operation_id and claim_data['planSha256'] == journal['planSha256'] and
                claim_data['automaticReplayForbidden'] is True, 'Claim identity/guard differs.')
        require('reject' in row['duplicateIntentResult']['state'], 'Repeated intent was not rejected.')
        for path, sha in row['journal'].items():
            require(hashlib.sha256(owned(path).read_bytes()).hexdigest() == sha, 'Journal bytes differ.')
        for proof in row['protectedFiles']:
            require(hashlib.sha256(owned(proof['path']).read_bytes()).hexdigest() == proof['sha256'], 'Protected saved bytes differ.')
        response = root / 'responses' / (operation_id + '.json')
        if row['fault'] == 'lost_response':
            require(not response.exists(), 'Lost reply was recreated.')
            retained = owned(row['retainedUnpublishedResponse'])
            require(hashlib.sha256(retained.read_bytes()).hexdigest() ==
                    row['verificationParameters'][next(k for k in row['verificationParameters'] if k.endswith('ReceiptSha256'))], 'Retained lost reply SHA differs.')
        else:
            require(json.loads(response.read_text(encoding='utf-8-sig')) == row['execution'], 'Raw execution response differs.')
        if row['fault'] == 'control':
            require(journal['recordedState'] == 'succeeded_verified' and not journal['writerLockPresent'] and
                    row['verification']['state'].endswith('verified_observed'), 'Control not independently verified.')
        else:
            require(journal['recordedState'] != 'succeeded_verified' and
                    not row['verification']['state'].endswith('verified_observed'), 'Unresolved fault promoted to success.')
            if row['operation'] in {'translate_owned_solid', 'save_owned_document_as'}:
                fence = owned(row['fencePath'])
                require(fence.is_file(), 'Unknown mutation fence lost.')
                fence_data = json.loads(fence.read_text(encoding='utf-8-sig'))
                require(fence_data['operationId'] == operation_id and fence_data['planSha256'] == journal['planSha256'], 'Wrong retained fence.')
            if row['fault'] in {'lost_return_after_effect', 'partial_journal'}:
                require(not returns, 'Missing command return was fabricated.')
            if row['fault'] == 'partial_journal':
                require(journal['writerLockPresent'], 'Partial writer marker was removed.')
            if row['fault'] == 'non_success' and row['operation'] == 'translate_owned_solid':
                require(len(returns) == 1 and type(returns[0]['details']['returnCode']) is int and returns[0]['details']['returnCode'] == 0, 'Code0 row differs.')
            if row['fault'] == 'non_success_minus_one':
                require(len(returns) == 1 and type(returns[0]['details']['returnCode']) is int and returns[0]['details']['returnCode'] == -1, 'Code-1 row differs.')
        checked.append({'operation': row['operation'], 'fault': row['fault'],
                        'recordedState': journal['recordedState'], 'events': journal['eventCount']})
    return {'state': 'native_core_artifacts_audited', 'reportSha256': hashlib.sha256(raw).hexdigest(),
            'rows': len(checked), 'checks': checked, 'cadConnectionAttempted': False,
            'fullK7Complete': False, 'remaining': report['remaining']}

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--stage', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    result = audit(args.stage)
    output = Path(args.output).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print('PASS: %d native core fault rows independently audited; full K7 remains incomplete.' % result['rows'])
