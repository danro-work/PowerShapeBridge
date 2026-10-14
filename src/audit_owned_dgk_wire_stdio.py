"""Independent request/file audit for actual SDK-to-native DGK target preparation."""
import argparse
import json
from pathlib import Path

from audit_owned_dgk_targets import read, sha, canonical_sha

FAULTS = {'control', 'source_tamper', 'fenced_copy', 'lost_reply', 'target_tamper', 'directory_replaced'}


def audit(stage, evidence):
    assert (stage / '.owned-dgk-wire-fixture').is_file()
    summary = read(evidence / 'dgk-wire-stdio.json')
    assert summary['state'] == 'passed' and summary['cases'] == len(FAULTS)
    assert summary['cadConnectionAttempted'] is False and summary['runtimeDeploymentPerformed'] is False
    assert sha(stage / 'PSBridge.Worker.exe') == summary['workerSha256']
    count = 0
    for fault in sorted(FAULTS):
        row = read(evidence / (fault + '.json'))
        root = Path(row['root']).resolve()
        assert root.is_relative_to(stage / 'work') and row['commandCalls'] == 0
        hashes = {Path(path).resolve(): digest for path, digest in row['artifactHashes'].items()}
        assert set(hashes) == {path.resolve() for path in root.rglob('*') if path.is_file()}
        assert all(path.is_relative_to(root) and sha(path) == digest for path, digest in hashes.items())
        assert all(sha(Path(path)) == digest for path, digest in row['seed']['protectedHashes'].items())
        count += len(hashes)
        calls = row['nativeRequests']
        assert [call['recipe'] for call in calls] == ['prepare_working_copy', 'inspect_active_model', 'bind_working_document', 'prepare_entity_set', 'prepare_owned_dgk_export']
        assert len(list((root / 'requests').glob('*.processing'))) == 5 and not list((root / 'requests').glob('*.json'))
        for call in calls:
            rid = call['requestId']
            envelope = read(root / 'requests' / (rid + '.json.processing'))
            assert envelope['requestId'] == rid and envelope['operation'] == 'recipe.prepare'
            assert envelope['arguments']['recipeId'] == call['recipe']
            suffix = '.unpublished' if fault == 'lost_reply' and call['recipe'] == 'prepare_owned_dgk_export' else '.json'
            assert read(root / 'responses' / (rid + suffix)) == call['result']
            assert call['result']['bridgeRequestId'] == rid
        assert all(read(path)['commandCalls'] == 0 for path in (root / 'wire-contexts').glob('*.json'))
        raw = calls[-1]['result']
        for flag in ('executionEnabled', 'exportAttempted', 'geometryMutationAttempted', 'contextChangeAttempted', 'cadSaveAttempted', 'powerShapeConnectionAttempted', 'retryAllowed'):
            assert raw[flag] is False
        reply = row['prepared']
        data = reply['structuredContent']
        assert data['bridgeRequestId'] == calls[-1]['requestId']
        assert reply['isError'] is (fault != 'control')
        assert ('ownedDgkTargetHandle' in data) is (fault == 'control')
        folder = root / 'dgkexports' / calls[-1]['requestId']
        if fault in {'control', 'lost_reply', 'target_tamper', 'directory_replaced'}:
            assert raw['state'] == 'owned_dgk_target_prepared_files'
            assert (sha(folder / 'target.json') == raw['targetSha256']) is (fault != 'target_tamper')
            plan = raw['target']
            assert plan['parameters'] == row['planned'] and plan['parametersSha256'] == canonical_sha(row['planned'])
            assert plan['bundleDirectory'] == str(folder / 'artifacts') and plan['artifactManifest'][0]['role'] == 'geometry_dgk'
            assert list((folder / 'artifacts').iterdir()) == [] and not (folder / 'export-once.json').exists()
            assert not (root / 'operations' / calls[-1]['requestId']).exists()
        else:
            assert raw['state'] == 'owned_dgk_target_rejected' and not folder.exists()
        owner = row['copy']['ownership']
        assert sha(Path(owner['working']['path'])) == sha(Path(owner['checkpoint']['path'])) == sha(Path(row['seed']['sourcePath']))
        assert len({(owner[role]['fileIdentity']['volumeSerial'], owner[role]['fileIdentity']['fileIndex']) for role in ('source', 'working', 'checkpoint')}) == 3
        assert (root / 'workingcopies' / row['copy']['copyId'] / 'mutation-fence.json').exists() is (fault == 'fenced_copy')
    return {'state': 'passed', 'cases': len(FAULTS), 'artifactHashesChecked': count, 'requestsChecked': 30,
            'cadConnectionAttempted': False, 'exportAttempted': False, 'scope': 'Actual SDK/native filesystem recipe; fake prior CAD reads'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('stage', 'evidence', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.stage.resolve(), args.evidence.resolve())
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(f"PASS: six native DGK STDIO scenarios, {result['artifactHashesChecked']} hashes / 30 requests audited; no CAD.")
