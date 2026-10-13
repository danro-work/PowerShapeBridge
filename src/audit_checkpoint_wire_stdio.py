"""Independently inspect native artifacts from the full SDK checkpoint wire workflow."""
import argparse
import json
from pathlib import Path

from audit_checkpoint_recovered_open import journal, read, require, sha

FAULTS = {'control', 'lost_return_after_effect', 'no_effect', 'changed_process',
          'partial_open_journal', 'wrong_verify_model', 'lost_reply'}


def audit(stage, evidence):
    require((stage / '.checkpoint-recovery-fixture').is_file(), 'Missing stage marker')
    require(read(evidence / 'checkpoint-wire-stdio.json')['cases'] == 7, 'Incomplete wire run')
    count, requests = 0, 0
    for fault in sorted(FAULTS):
        row = read(evidence / (fault + '.json'))
        root = Path(row['root']).resolve()
        require(root.is_relative_to(stage / 'work') and row['fault'] == fault and
                row['state'] == 'passed' and row['cadConnectionAttempted'] is False, 'Wrong fixture scope')
        hashes = {Path(path).resolve(): value for path, value in row['artifactHashes'].items()}
        require(set(hashes) == {path.resolve() for path in root.rglob('*') if path.is_file()} and
                all(path.is_relative_to(root) and sha(path) == value for path, value in hashes.items()),
                'File inventory or hashes changed')
        count += len(hashes)
        seed = row['seed']
        require(all(Path(path).resolve().is_relative_to(root) and sha(Path(path)) == value
                    for path, value in seed['protectedHashes'].items()), 'Original protected bytes changed')
        require(journal(root, seed['originId']) == ['prepared', 'execution_claimed', 'command_started'],
                'Original unknown resolved')
        origin_plan = root / 'copyopenplans' / (seed['originId'] + '.json')
        require(sha(origin_plan) == seed['originPlanSha256'], 'Origin plan changed')
        calls = row['nativeRequests']
        expected = ['prepare_checkpoint_recovery', 'create_checkpoint_working_copy',
                    'inspect_active_model', 'list_documents', 'open_working_copy']
        if fault != 'lost_reply':
            expected += ['inspect_active_model', 'list_documents', 'verify_working_copy_open']
        if fault == 'control':
            expected += ['inspect_active_model', 'list_documents', 'bind_working_document']
        require([call['recipe'] for call in calls] == expected, 'Unexpected call/retry/cleanup')
        require(len(list((root / 'requests').glob('*.processing'))) == len(calls) and
                not list((root / 'requests').glob('*.json')), 'Unclaimed or extra request')
        requests += len(calls)
        for call in calls:
            request_id = call['requestId']
            envelope = read(root / 'requests' / (request_id + '.json.processing'))
            require(envelope['requestId'] == request_id and envelope['operation'] == 'recipe.prepare' and
                    envelope['arguments']['recipeId'] == call['recipe'], 'Request identity differs')
            suffix = '.unpublished' if fault == 'lost_reply' and call['recipe'] == 'open_working_copy' else '.json'
            receipt = root / 'responses' / (request_id + suffix)
            require(read(receipt) == call['result'] and call['result']['bridgeRequestId'] == request_id,
                    'Native original receipt differs')
        recovered = row['recovered']
        require(recovered['originalOutcomeResolved'] is False and row['originOutcomeResolved'] is False and
                'workingDocumentBindingHandle' not in recovered, 'Disk step granted CAD authority')
        copy_id = recovered['copyId']
        require(copy_id not in {seed['originId'], seed['originCopyId'], recovered['recoveryId']}, 'Copy identity reused')
        owner_path = root / 'workingcopies' / copy_id / 'ownership.json'
        require(sha(owner_path) == recovered['ownershipSha256'] and read(owner_path) == recovered['ownership'],
                'New owner differs')
        owner = read(owner_path)
        origin_owner = read(root / 'workingcopies' / seed['originCopyId'] / 'ownership.json')
        require(owner['source'] == origin_owner['checkpoint'], 'New source is not original checkpoint')
        roles = [owner[key] for key in ('source', 'working', 'checkpoint')]
        require(len({(role['fileIdentity']['volumeSerial'], role['fileIdentity']['fileIndex'])
                     for role in roles}) == 3, 'Physical alias')
        require(all(Path(role['path']).resolve().is_relative_to(root) and sha(Path(role['path'])) == roles[0]['sha256']
                    for role in roles), 'New file bytes/roles differ')
        raw = read(root / 'checkpointcopyplans' / (copy_id + '.result.json'))
        native_copy = calls[1]['result'].copy()
        proof_sha = native_copy.pop('materializationProofSha256')
        native_copy.pop('bridgeRequestId')
        require(raw == native_copy and sha(root / 'checkpointcopyplans' / (copy_id + '.result.json')) == proof_sha,
                'Materialization raw proof differs')
        opened = calls[4]
        claim = read(root / 'workingcopies' / copy_id / 'open-once.json')
        require(claim['operationId'] == opened['requestId'] and claim['copyId'] == copy_id and
                claim['automaticReplayForbidden'] is True, 'OPEN claim differs')
        states = journal(root, opened['requestId'])
        contexts = sorted((root / 'wire-contexts').glob('*.json'))
        require(len(contexts) == len(calls) + 1 and read(contexts[-1])['commandCalls'] == 1,
                'Context sequence or OPEN count differs')
        if fault == 'control':
            verified = row['verified']['structuredContent']
            require(states[-1] == 'succeeded_verified' and row['verified']['isError'] is False and
                    verified['copyId'] == copy_id and verified['observedModel']['id'] == 500,
                    'Control lacks independent current verification')
            require(row['binding']['workingDocumentBindingHandle'] and row['binding']['binding']['copyId'] == copy_id,
                    'Current binding missing/wrong')
        else:
            require(row['binding'] is None and 'succeeded_verified' not in states, 'Failure granted CAD binding')
            if fault == 'lost_reply':
                require(row['opened']['structuredContent']['state'] == 'timeout_outcome_unknown' and
                        not (root / 'responses' / (opened['requestId'] + '.json')).exists(), 'Lost reply restored/invented')
            else:
                require(row['verified']['isError'] is True and
                        'workingCopyHandle' not in row['verified']['structuredContent'], 'Failed verification granted lease')
        require((root / 'operations' / opened['requestId'] / 'writer.lock').exists() ==
                (fault == 'partial_open_journal'), 'Interrupted marker repaired/lost')
    return {'state': 'passed', 'schema': 'psbridge.checkpoint-wire-audit.v1', 'cases': 7,
            'artifactHashesChecked': count, 'requestsChecked': requests, 'cadConnectionAttempted': False,
            'scope': 'Actual STDIO/native queue proofs with fake CAD; live acceptance pending'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('stage', 'evidence', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.stage.resolve(), args.evidence.resolve())
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f"PASS: seven SDK/native wire cases, {result['artifactHashesChecked']} hashes/{result['requestsChecked']} requests independently audited.")
