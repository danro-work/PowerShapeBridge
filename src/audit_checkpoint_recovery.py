"""Independent audit of native checkpoint recovery; no CAD or cleanup."""
import argparse
import hashlib
import json
from pathlib import Path


FAULTS = {'prepared_only', 'control', 'working_changed_before', 'expired', 'future_intent',
          'checkpoint_writable', 'checkpoint_changed', 'checkpoint_replaced', 'checkpoint_hardlink',
          'ownership_changed', 'plan_changed', 'busy_executor', 'busy_checkpoint',
          'partial_after_claim', 'partial_after_copy', 'foreign_copy_identity'}


def require(ok, reason):
    if not ok:
        raise ValueError(reason)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def audit(stage):
    require((stage / '.checkpoint-recovery-fixture').is_file(), 'Missing isolated marker')
    report = json.loads((stage / 'reports/checkpoint-recovery-matrix.json').read_text(encoding='utf-8-sig'))
    require(report['state'] == 'passed' and report['cadConnectionAttempted'] is False, 'Wrong scope/state')
    require(len(report['rows']) == len(FAULTS) and {row['fault'] for row in report['rows']} == FAULTS, 'Incomplete fault matrix')
    checked = 0
    for row in report['rows']:
        root = Path(row['root']).resolve()
        require(root.is_relative_to(stage / 'work'), 'Root outside stage')
        hashes = {Path(path).resolve(): value for path, value in row['artifactHashes'].items()}
        require(all(path.is_relative_to(root) and path.is_file() and sha(path) == value for path, value in hashes.items()), 'Changed artifact')
        require(set(hashes) == {path.resolve() for path in root.rglob('*') if path.is_file()}, 'Inventory differs')
        checked += len(hashes)
        prepared = row['prepared']
        intent_id = prepared['recoveryId']
        intent_path = root / 'checkpointrecoveries' / (intent_id + '.json')
        # Expiry mutations deliberately refresh the injected intent hash only.
        intent_sha = row.get('parameters', {}).get('recoverySha256', prepared['recoverySha256'])
        require(sha(intent_path) == intent_sha, 'Intent SHA differs')
        plan = json.loads(intent_path.read_text(encoding='utf-8-sig'))
        require(plan['operation'] == 'copy_owned_checkpoint' and plan['cadExecutionEnabled'] is False
                and plan['originalOutcomeResolved'] is False, 'Unsafe intent flags')
        if row['fault'] == 'prepared_only':
            require(not (root / 'checkpointrecoveries' / (intent_id + '.copy-once.json')).exists(), 'Prepared seed unexpectedly claimed')
            continue
        require(all(sha(Path(path)) == value for path, value in row['protectedHashes'].items()), 'Original protected file changed')
        result = row['result']
        control = row['fault'] in {'control', 'working_changed_before'}
        partial = row['fault'] in {'partial_after_claim', 'partial_after_copy'}
        require(result['state'] == ('checkpoint_working_copy_prepared_disk' if control else
                                    'checkpoint_copy_outcome_unknown' if partial else 'checkpoint_copy_rejected_before_creation'), 'Wrong outcome')
        for flag in ['powerShapeConnectionAttempted', 'geometryMutationAttempted', 'contextChangeAttempted', 'cadSaveAttempted', 'originalOutcomeResolved', 'retryAllowed']:
            require(result[flag] is False, 'Unexpected authority/action flag')
        claim_path = root / 'checkpointrecoveries' / (intent_id + '.copy-once.json')
        require(claim_path.exists() == (control or partial), 'Wrong claim phase')
        if control or partial:
            claim = json.loads(claim_path.read_text(encoding='utf-8-sig'))
            require(claim['operationId'] == row['copyId'] and claim['recoveryId'] == intent_id
                    and claim['automaticReplayForbidden'] is True, 'Claim identity differs')
            creation_path = root / 'checkpointcopyplans' / (row['copyId'] + '.json')
            require(sha(creation_path) == claim['planSha256'], 'Creation plan SHA differs')
            creation = json.loads(creation_path.read_text(encoding='utf-8-sig'))
            require(creation['origin'] == plan['origin'] and creation['checkpoint'] == plan['checkpoint'], 'Recovery lineage differs')
        if control:
            owner_path = root / 'workingcopies' / row['copyId'] / 'ownership.json'
            require(sha(owner_path) == result['ownershipSha256'], 'New owner SHA differs')
            owner = json.loads(owner_path.read_text(encoding='utf-8-sig'))
            require(owner == result['ownership'] and owner['source'] == plan['checkpoint'] and owner['schemaVersion'] == 2, 'Owner/source role differs')
            checkpoint = Path(plan['checkpoint']['path'])
            work = Path(owner['working']['path'])
            before = Path(owner['checkpoint']['path'])
            require(work.is_relative_to(root / 'workingcopies' / row['copyId']) and before.is_relative_to(root / 'workingcopies' / row['copyId']), 'Wrong new file location')
            require(sha(checkpoint) == sha(work) == sha(before) == plan['checkpoint']['sha256'], 'Copied checkpoint bytes differ')
            identities = [record['fileIdentity']['volumeSerial'] + ':' + record['fileIdentity']['fileIndex']
                          for record in [owner['source'], owner['working'], owner['checkpoint']]]
            require(len(set(identities)) == 3, 'Aliased recovered files')
            proof_path = root / 'checkpointcopyplans' / (row['copyId'] + '.result.json')
            require(sha(proof_path) == result['materializationProofSha256'], 'Materialization proof differs')
            proof = json.loads(proof_path.read_text(encoding='utf-8-sig'))
            expected = dict(result)
            expected.pop('bridgeRequestId')
            expected.pop('materializationProofSha256')
            require(proof == expected, 'Raw materialization body differs')
    return {'schema': 'psbridge.checkpoint-recovery-audit.v1', 'state': 'passed',
            'cases': len(report['rows']), 'artifactHashesChecked': checked, 'cadConnectionAttempted': False,
            'scope': 'Native filesystem checkpoint copies/guards only; not OPEN or live CAD qualification'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--stage', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.stage.resolve())
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f"PASS: {result['cases']} native checkpoint recovery cases independently audited; no CAD.")
