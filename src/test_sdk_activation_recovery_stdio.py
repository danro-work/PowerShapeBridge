"""Real SDK STDIO reading actual isolated native activation fault artifacts."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import uuid

from test_sdk_stdio import Client


def inventory(root):
    return {str(path.relative_to(root)): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in root.rglob('*') if path.is_file()}


def checks(args):
    stage = Path(args.stage).resolve()
    report = json.loads((stage / 'reports/activation-fault-matrix.json').read_text(encoding='utf-8-sig'))
    assert report['state'] == 'passed' and len(report['rows']) == 20
    output = Path(args.evidence).resolve() / 'activation-recovery-stdio'
    output.mkdir(parents=True, exist_ok=True)
    results = []
    for row in report['rows']:
        source = Path(row['root']).resolve()
        assert source.is_relative_to(stage / 'work')
        root = output / ('fixture-' + uuid.uuid4().hex)
        shutil.copytree(source, root)
        (root / 'bridge.config.json').write_text(json.dumps({'coreProfile': 'general-v1',
            'expectedVersion': '23.2.2', 'timeoutSeconds': 20, 'operationTimeoutSeconds': 80,
            'maxEntities': 10000, 'captureViewport': False, 'recoveryInspectionExperimental': True}), encoding='utf-8')
        before = inventory(root)
        for readonly in [False, True]:
            client = Client([args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root)]
                            + (['--read-only'] if readonly else []))
            try:
                client.call(1, 'initialize', {'protocolVersion': '2025-11-25', 'capabilities': {},
                    'clientInfo': {'name': 'native-activation-recovery-proof', 'version': '1'}})
                client.notify('notifications/initialized')
                tools = client.call(2, 'tools/list')['result']['tools']
                tool = next(item for item in tools if item['name'] == 'inspect_operation_recovery')
                assert tool['annotations']['readOnlyHint'] is True
                reply = client.call(3, 'tools/call', {'name': tool['name'], 'arguments': {'operationId': row['operationId']}})
                if row['fault'] == 'invalid_request_id':
                    assert 'error' in reply
                else:
                    assert re.fullmatch('[0-9a-f]{32}', row['operationId'])
                    data = reply['result']['structuredContent']
                    if row['fault'] == 'publication_before_command':
                        assert reply['result']['isError'] and data['state'] == 'recovery_inspection_unavailable', data
                    else:
                        assert not reply['result'].get('isError', False), data
                        assert data['journalFormat'] == 'legacy_single_receipt'
                        original = (root / 'operations' / (row['operationId'] + '.json')).read_bytes()
                        assert data['journalSha256'] == hashlib.sha256(original).hexdigest()
                        assert data['journal'] == json.loads(original.decode('utf-8-sig'))
                        assert data['outcome'] == 'not_resolved_by_recorded_inspection'
                        for flag in ['checkpointRestoreAuthorized', 'commandReturnedRecorded',
                                     'authoritativeSuccessReturnRecorded', 'currentContextChecked', 'savedFilesChecked']:
                            assert data[flag] is False, (flag, data)
                    for flag in ['retryAllowed', 'executionAuthorized', 'cadConnectionAttempted', 'artifactWritesAttempted']:
                        assert data[flag] is False, (flag, data)
                results.append({'fault': row['fault'], 'readonly': readonly, 'reply': reply})
            finally:
                client.close()
            assert inventory(root) == before and not (root / 'requests').exists()
        assert inventory(source) == row['artifacts'] or {
            str(source / key): value for key, value in inventory(source).items()} == row['artifacts']
    (output / 'verification.json').write_text(json.dumps({'state': 'passed', 'cases': len(results),
        'cadConnectionAttempted': False, 'artifactsUnchanged': True, 'noQueueCreated': True,
        'hostSha256': hashlib.sha256(Path(args.host).read_bytes()).hexdigest(), 'results': results}, indent=2), encoding='utf-8')
    print(f'PASS: {len(results)} SDK activation recovery STDIO cases on actual native artifacts; no CAD or queue.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ['dotnet', 'host', 'stage', 'evidence']:
        parser.add_argument('--' + name, required=True)
    checks(parser.parse_args())
