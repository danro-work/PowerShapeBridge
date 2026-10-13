"""Audit recovered-copy OPEN artifacts independently; never connect to CAD."""
import argparse
import hashlib
import json
from pathlib import Path

FAULTS = {'control', 'lost_return_after_effect', 'no_effect', 'changed_process',
          'partial_open_journal', 'wrong_verify_model'}


def require(ok, reason):
    if not ok:
        raise ValueError(reason)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def journal(root, operation):
    events = sorted((root / 'operations' / operation).glob('*.json'))
    previous = None
    for index, path in enumerate(events):
        event = read(path)
        require(path.name == f'{index:06}.json' and event['sequence'] == index,
                'Journal sequence differs')
        require(event['operationId'] == operation and event['previousSha256'] == previous,
                'Journal identity/hash chain differs')
        previous = sha(path)
    return [read(path)['state'] for path in events]


def audit(stage):
    require((stage / '.checkpoint-recovery-fixture').is_file(), 'Missing isolated marker')
    report = read(stage / 'reports/checkpoint-recovered-open.json')
    require(report['schema'] == 'psbridge.checkpoint-recovered-open.v1' and
            report['state'] == 'passed' and report['cadConnectionAttempted'] is False,
            'Wrong report scope/state')
    require(len(report['rows']) == 6 and {row['fault'] for row in report['rows']} == FAULTS,
            'Incomplete or repeated scenarios')
    count = 0
    for row in report['rows']:
        root = Path(row['root']).resolve()
        require(root.is_relative_to(stage / 'work'), 'Fixture outside stage')
        hashes = {Path(path).resolve(): value for path, value in row['artifactHashes'].items()}
        actual = {path.resolve() for path in root.rglob('*') if path.is_file()}
        require(set(hashes) == actual and all(path.is_relative_to(root) and sha(path) == value
                for path, value in hashes.items()), 'Changed or unlisted artifact')
        count += len(hashes)
        require(row['commandCalls'] == 1 and row['cadConnectionAttempted'] is False,
                'OPEN count/scope differs')
        require(journal(root, row['originId']) ==
                ['prepared', 'execution_claimed', 'command_started'], 'Original unknown resolved')
        require(all(Path(path).resolve().is_relative_to(root) and sha(Path(path)) == value
                for path, value in row['originJournal'].items()), 'Original journal changed')
        origin_claim = Path(row['originClaim']).resolve()
        require(origin_claim.is_relative_to(root) and sha(origin_claim) == row['originClaimSha256'],
                'Original claim changed')
        require(read(origin_claim)['automaticReplayForbidden'] is True, 'Original replay permitted')
        recovered = row['recovered']
        require(recovered['state'] == 'checkpoint_working_copy_prepared_disk' and
                recovered['originalOutcomeResolved'] is False, 'Wrong copy authority')
        owner_path = root / 'workingcopies' / row['copyId'] / 'ownership.json'
        require(sha(owner_path) == recovered['ownershipSha256'] and
                read(owner_path) == recovered['ownership'], 'Recovered ownership differs')
        owner = read(owner_path)
        roles = [owner[key] for key in ('source', 'working', 'checkpoint')]
        paths = [Path(role['path']).resolve() for role in roles]
        require(all(path.is_relative_to(root) for path in paths) and len(set(paths)) == 3,
                'File scope/role alias')
        require(len({(role['fileIdentity']['volumeSerial'], role['fileIdentity']['fileIndex'])
                     for role in roles}) == 3, 'Native physical identity alias')
        require(all(sha(path) == roles[0]['sha256'] for path in paths), 'Checkpoint bytes changed')
        opened, verified = row['open'], row['verification']
        for result in (opened, verified):
            require(result['cadSaveAttempted'] is False and result['geometryMutationAttempted'] is False
                    and result['retryAllowed'] is False, 'Unexpected save/geometry/retry')
        for receipt_id, body in ((row['openId'], opened), (row['verificationId'], verified)):
            require(read(root / 'responses' / (receipt_id + '.json')) == body, 'Receipt differs')
        claim = read(root / 'workingcopies' / row['copyId'] / 'open-once.json')
        require(claim['operationId'] == row['openId'] and claim['copyId'] == row['copyId'] and
                claim['automaticReplayForbidden'] is True, 'Recovered OPEN claim differs')
        states = journal(root, row['openId'])
        if row['fault'] == 'control':
            require(states[-1] == 'succeeded_verified' and
                    verified['state'] == 'owned_open_verified_observed', 'Control lacks independent verification')
            require(verified['observedModel']['id'] == 500 and
                    Path(verified['observedModel']['path']).resolve() == paths[1], 'Wrong current model')
            binding = row['binding']
            require(binding['state'] == 'working_document_bound_observed' and
                    binding['binding']['copyId'] == row['copyId'] and
                    binding['binding']['ownershipSha256'] == recovered['ownershipSha256'], 'Wrong binding')
            require(read(root / 'responses' / (row['bindingId'] + '.json')) == binding, 'Binding receipt differs')
        else:
            require(verified['state'] == 'owned_open_verification_rejected' and row['binding'] is None
                    and 'succeeded_verified' not in states, 'Failure granted success/binding')
        require((root / 'operations' / row['openId'] / 'writer.lock').exists() ==
                (row['fault'] == 'partial_open_journal'), 'Partial writer marker lost or invented')
    return {'schema': 'psbridge.checkpoint-recovered-open-audit.v1', 'state': 'passed',
            'cases': 6, 'artifactHashesChecked': count, 'cadConnectionAttempted': False,
            'scope': 'Actual native recovery/OPEN/verifier artifacts with fake CAD; live acceptance pending'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--stage', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.stage.resolve())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f"PASS: six recovered OPEN scenarios, {result['artifactHashesChecked']} hashes audited; no CAD.")
