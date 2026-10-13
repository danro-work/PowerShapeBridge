"""Independent audit of actual native filesystem failures returned through MCP."""
import argparse
import json
import stat
from pathlib import Path

from audit_checkpoint_recovered_open import journal, read, require, sha

FAULTS = {'checkpoint_junction', 'intent_junction', 'partial_copy_after_working_write',
          'partial_copy_before_ownership', 'partial_copy_ownership_record', 'partial_copy_after_ownership'}


def audit(stage, evidence):
    require((stage / '.checkpoint-recovery-fixture').is_file(), 'Missing isolated stage marker')
    checked = 0
    for fault in sorted(FAULTS):
        row = read(evidence / (fault + '.json'))
        root = Path(row['root']).resolve()
        require(root.is_relative_to(stage / 'work') and row['state'] == 'passed' and row['fault'] == fault
                and row['cadConnectionAttempted'] is False, 'Wrong fixture scope')
        hashes = {Path(path).resolve(): value for path, value in row['artifactHashes'].items()}
        require(set(hashes) == {path.resolve() for path in root.rglob('*') if path.is_file()} and
                all(path.is_relative_to(root) and sha(path) == value for path, value in hashes.items()), 'Changed file inventory')
        checked += len(hashes)
        seed = row['seed']
        require(all(Path(path).resolve().is_relative_to(root) and sha(Path(path)) == value
                    for path, value in seed['protectedHashes'].items()), 'Original file changed')
        require(journal(root, seed['originId']) == ['prepared', 'execution_claimed', 'command_started'],
                'Original unknown changed')
        calls = row['nativeRequests']
        require([item['recipe'] for item in calls] == ['prepare_checkpoint_recovery', 'create_checkpoint_working_copy'],
                'Unexpected native call/replay/OPEN')
        require(len(list((root / 'requests').glob('*.processing'))) == 2 and
                not list((root / 'requests').glob('*.json')), 'Wrong queue footprint')
        for call in calls:
            request_id = call['requestId']
            envelope = read(root / 'requests' / (request_id + '.json.processing'))
            require(envelope['requestId'] == request_id and envelope['arguments']['recipeId'] == call['recipe'] and
                    read(root / 'responses' / (request_id + '.json')) == call['result'], 'Native request/receipt differs')
        result = calls[1]['result']
        copy_id = calls[1]['requestId']
        recovery_id = calls[0]['result']['recoveryId']
        partial = fault.startswith('partial_')
        require(result['state'] == ('checkpoint_copy_outcome_unknown' if partial else
                'checkpoint_copy_rejected_before_creation'), 'Wrong native phase')
        claim_path = root / 'checkpointrecoveries' / (recovery_id + '.copy-once.json')
        require(claim_path.exists() == partial, 'Wrong claim phase')
        require(not (root / 'checkpointcopyplans' / (copy_id + '.result.json')).exists(), 'Failure materialization certified')
        require(row['created']['isError'] is True and row['replay']['isError'] is True and
                'workingCopyHandle' not in row['created']['structuredContent'] and
                'workingDocumentBindingHandle' not in row['created']['structuredContent'], 'Failure granted authority')
        for field in ('powerShapeConnectionAttempted', 'geometryMutationAttempted', 'contextChangeAttempted',
                      'cadSaveAttempted', 'originalOutcomeResolved', 'retryAllowed'):
            require(result[field] is False, 'Unexpected action/authority flag')
        contexts = sorted((root / 'wire-contexts').glob('*.json'))
        require(len(contexts) == 3 and read(contexts[-1])['commandCalls'] == 0, 'Unexpected CAD command/context write')
        if not partial:
            owner = read(root / 'workingcopies' / seed['originCopyId'] / 'ownership.json')
            junction = Path(owner['checkpoint']['path']).parent if fault == 'checkpoint_junction' else root / 'checkpointrecoveries'
            require(junction.lstat().st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT and
                    junction.resolve().is_relative_to(root) and 'reparse' in result['error'].lower(), 'Reparse guard not exercised')
        else:
            claim = read(claim_path)
            require(claim['operationId'] == copy_id and claim['automaticReplayForbidden'] is True, 'Wrong persistent claim')
            folder = root / 'workingcopies' / copy_id
            work = folder / ('Working_' + copy_id + '.psmodel')
            checkpoint = folder / 'checkpoint/Before.psmodel'
            owner_path = folder / 'ownership.json'
            require(work.stat().st_size > 0, 'Partial working bytes missing')
            if fault == 'partial_copy_after_working_write':
                require(checkpoint.stat().st_size == 0 and not owner_path.exists(), 'Wrong partial byte phase')
            else:
                original_owner = read(root / 'workingcopies' / seed['originCopyId'] / 'ownership.json')
                require(sha(work) == sha(checkpoint) == original_owner['checkpoint']['sha256'] and
                        not checkpoint.stat().st_mode & stat.S_IWRITE, 'Complete bytes/readonly differ')
                require(owner_path.exists() == (fault != 'partial_copy_before_ownership'), 'Wrong owner publication phase')
                if fault == 'partial_copy_ownership_record':
                    malformed = False
                    try:
                        read(owner_path)
                    except json.JSONDecodeError:
                        malformed = True
                    require(malformed, 'Interrupted owner unexpectedly valid')
                elif fault == 'partial_copy_after_ownership':
                    require(read(owner_path)['copyId'] == copy_id, 'Completed owner identity differs')
    return {'schema': 'psbridge.checkpoint-filesystem-stdio-audit.v1', 'state': 'passed',
            'cases': 6, 'artifactHashesChecked': checked, 'requestsChecked': 12,
            'cadConnectionAttempted': False, 'newAuthorityGranted': False}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('stage', 'evidence', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.stage.resolve(), args.evidence.resolve())
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f"PASS: six native filesystem failures via SDK, {result['artifactHashesChecked']} hashes independently audited.")
