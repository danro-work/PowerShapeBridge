"""Independent disk/request/journal audit for scoped native uniform_scale preparation."""
import argparse
import json
from pathlib import Path

from audit_checkpoint_recovered_open import journal, read, require, sha

FAULTS = {'control', 'changed_process', 'fenced_copy', 'late_context', 'lost_reply', 'packet_tamper'}


def audit(stage, evidence):
    require((stage / '.owned-uniform-scale-fixture').is_file(), 'Missing isolated marker')
    summary = read(evidence / 'uniform-scale-wire-stdio.json')
    require(summary['state'] == 'passed' and summary['cases'] == len(FAULTS) and
            summary['cadConnectionAttempted'] is False and summary['runtimeDeploymentPerformed'] is False,
            'Incomplete or wrong-scope run')
    require(sha(stage / 'PSBridge.Worker.exe') == summary['workerSha256'], 'Worker changed')
    count = 0
    for fault in sorted(FAULTS):
        row = read(evidence / (fault + '.json'))
        root = Path(row['root']).resolve()
        require(root.is_relative_to(stage / 'work') and row['state'] == 'passed' and row['fault'] == fault
                and row['commandCalls'] == 0 and row['cadConnectionAttempted'] is False, 'Wrong fixture scope')
        hashes = {Path(path).resolve(): digest for path, digest in row['artifactHashes'].items()}
        require(set(hashes) == {path.resolve() for path in root.rglob('*') if path.is_file()} and
                all(path.is_relative_to(root) and sha(path) == digest for path, digest in hashes.items()),
                'Artifact inventory/hash differs')
        count += len(hashes)
        require(all(Path(path).resolve().is_relative_to(root) and sha(Path(path)) == digest
                    for path, digest in row['seed']['protectedHashes'].items()), 'Protected seed changed')
        calls = row['nativeRequests']
        require([item['recipe'] for item in calls] == ['prepare_working_copy', 'inspect_active_model',
                'bind_working_document', 'prepare_entity_set', 'prepare_owned_uniform_scale'], 'Unexpected queue call')
        require(len(list((root / 'requests').glob('*.processing'))) == 5 and
                not list((root / 'requests').glob('*.json')), 'Extra or unclaimed request')
        for call in calls:
            rid = call['requestId']
            envelope = read(root / 'requests' / (rid + '.json.processing'))
            require(envelope['requestId'] == rid and envelope['operation'] == 'recipe.prepare' and
                    envelope['arguments']['recipeId'] == call['recipe'], 'Request identity mismatch')
            suffix = '.unpublished' if fault == 'lost_reply' and call['recipe'] == 'prepare_owned_uniform_scale' else '.json'
            require(read(root / 'responses' / (rid + suffix)) == call['result'] and
                    call['result']['bridgeRequestId'] == rid, 'Native original reply mismatch')
        contexts = sorted((root / 'wire-contexts').glob('*.json'))
        require(len(contexts) == 6 and all(read(path)['commandCalls'] == 0 for path in contexts), 'CAD command observed')
        owned = calls[-1]['result']
        for flag in ('executionEnabled', 'geometryMutationAttempted', 'contextChangeAttempted', 'cadSaveAttempted'):
            require(owned[flag] is False, 'Preparation acquired mutation authority')
        packet = root / 'ownedscales' / (calls[-1]['requestId'] + '.json')
        if fault in {'control', 'late_context', 'lost_reply', 'packet_tamper'}:
            require(packet.is_file() and journal(root, calls[-1]['requestId']) == ['prepared'], 'Prepared proof missing')
            require((sha(packet) == owned['ownedScaleSha256']) == (fault != 'packet_tamper'), 'Unexpected packet hash')
            event = read(root / 'operations' / calls[-1]['requestId'] / '000000.json')
            require(event['planSha256'] == owned['ownedScaleSha256'], 'Journal SHA mismatch')
        else:
            require(not packet.exists(), 'Preflight rejection wrote uniform_scale packet')
        reply = row['prepared']
        require(reply['structuredContent']['bridgeRequestId'] == calls[-1]['requestId'], 'SDK lost original request ID')
        require(reply['isError'] == (fault != 'control') and
                ('ownedScaleHandle' in reply['structuredContent']) == (fault == 'control'), 'Wrong scoped Issue decision')
        require((root / 'workingcopies' / row['copy']['copyId'] / 'mutation-fence.json').exists() ==
                (fault == 'fenced_copy'), 'Fixture fence altered')
        owner_path = root / 'workingcopies' / row['copy']['copyId'] / 'ownership.json'
        require(sha(owner_path) == row['copy']['ownershipSha256'] and read(owner_path) == row['copy']['ownership'], 'Owner differs')
        owner = read(owner_path)
        roles = [owner[key] for key in ('source', 'working', 'checkpoint')]
        require(len({(role['fileIdentity']['volumeSerial'], role['fileIdentity']['fileIndex']) for role in roles}) == 3,
                'Source/working/checkpoint alias')
        require(all(Path(role['path']).resolve().is_relative_to(root) and sha(Path(role['path'])) == owner['source']['sha256']
                    for role in roles), 'Saved bytes changed')
    return {'state': 'passed', 'cases': len(FAULTS), 'artifactHashesChecked': count, 'requestsChecked': 30,
            'cadConnectionAttempted': False, 'scope': 'Actual STDIO/native passive packets; fake CAD, no uniform_scale qualification'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('stage', 'evidence', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.stage.resolve(), args.evidence.resolve())
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(f"PASS: six scoped native uniform_scale cases, {result['artifactHashesChecked']} hashes / 30 requests audited.")
