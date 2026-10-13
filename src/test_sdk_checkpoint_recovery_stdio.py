"""Actual SDK and native filesystem-only checkpoint preparation/materialization."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import threading
import time
import uuid

from test_sdk_stdio import Client


def inventory(root):
    return {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in root.rglob('*') if path.is_file()}


def serve(root, worker, expected):
    deadline = time.monotonic() + 15
    while time.monotonic() < deadline:
        requests = list((root / 'requests').glob('*.json')) if (root / 'requests').exists() else []
        if requests:
            path = requests[0]
            claimed = path.with_name(path.name + '.processing')
            path.rename(claimed)
            envelope = json.loads(claimed.read_bytes())
            assert envelope['arguments']['recipeId'] == expected, envelope
            process = subprocess.run([str(worker), '--mcp-operation', envelope['requestId']], capture_output=True, timeout=15)
            assert process.returncode == 0, process.stderr.decode(errors='replace')
            result = json.loads(process.stdout.decode('utf-8-sig'))
            assert result['powerShapeConnectionAttempted'] is False, result
            response = root / 'responses' / (envelope['requestId'] + '.json')
            assert not response.exists()
            response.write_bytes(process.stdout)
            return envelope, result
        time.sleep(.01)
    raise AssertionError('No SDK request before fixture deadline')


def checks(args):
    stage = Path(args.stage).resolve()
    report = json.loads((stage / 'reports/checkpoint-recovery-matrix.json').read_text(encoding='utf-8-sig'))
    row = next(row for row in report['rows'] if row['fault'] == 'prepared_only')
    root = Path(row['root']).resolve()
    assert root.is_relative_to(stage / 'work') and (stage / '.checkpoint-recovery-fixture').is_file()
    worker = root / 'PSBridge.Worker.exe'
    before = inventory(root)
    client = Client([args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root)])
    results = []
    try:
        client.call(1, 'initialize', {'protocolVersion': '2025-11-25', 'capabilities': {},
            'clientInfo': {'name': 'checkpoint-native-filesystem-proof', 'version': '1'}})
        client.notify('notifications/initialized')
        tools = client.call(2, 'tools/list')['result']['tools']
        for name in ['prepare_checkpoint_recovery', 'create_checkpoint_working_copy']:
            tool = next(tool for tool in tools if tool['name'] == name)
            assert tool['annotations']['readOnlyHint'] is False
            assert tool['inputSchema']['additionalProperties'] is False
        handled = []
        def native(recipe):
            def execute():
                try:
                    handled.append(serve(root, worker, recipe))
                except BaseException as error:
                    handled.append(error)
            thread = threading.Thread(target=execute)
            thread.start()
            return thread
        thread = native('prepare_checkpoint_recovery')
        reply = client.call(3, 'tools/call', {'name': 'prepare_checkpoint_recovery', 'arguments': {
            'operationId': row['originId'], 'operationPlanSha256': row['originPlanSha256']}}, timeout=20)
        thread.join(timeout=20)
        assert not thread.is_alive() and handled and not isinstance(handled[-1], BaseException), handled
        assert not reply['result'].get('isError', False), reply
        data = reply['result']['structuredContent']
        handle = data['checkpointRecoveryHandle'];results.append(reply)
        thread = native('create_checkpoint_working_copy')
        reply = client.call(4, 'tools/call', {'name': 'create_checkpoint_working_copy',
            'arguments': {'checkpointRecoveryHandle': handle}}, timeout=20)
        thread.join(timeout=20)
        assert not thread.is_alive() and not isinstance(handled[-1], BaseException), handled
        assert not reply['result'].get('isError', False), reply
        data = reply['result']['structuredContent']
        assert data['workingCopyHandle'] and 'workingDocumentBindingHandle' not in data
        assert data['originalOutcomeResolved'] is False
        source = Path(data['ownership']['source']['path'])
        work = Path(data['ownership']['working']['path'])
        cp = Path(data['ownership']['checkpoint']['path'])
        assert work != source and cp != source and work != cp
        assert source.read_bytes() == work.read_bytes() == cp.read_bytes()
        results.append(reply)
        before_replay = inventory(root)
        replay = client.call(5, 'tools/call', {'name': 'create_checkpoint_working_copy',
            'arguments': {'checkpointRecoveryHandle': handle}})['result']
        assert replay['isError'] and inventory(root) == before_replay
        assert len(list((root / 'requests').iterdir())) == 2
    finally:
        client.close()
    assert all(Path(path).is_file() and hashlib.sha256(Path(path).read_bytes()).hexdigest() == value for path, value in before.items())
    readonly = Client([args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root), '--read-only'])
    before_readonly = inventory(root)
    try:
        readonly.call(1, 'initialize', {'protocolVersion': '2025-11-25', 'capabilities': {},
            'clientInfo': {'name': 'checkpoint-readonly-denial', 'version': '1'}})
        readonly.notify('notifications/initialized')
        names = {tool['name'] for tool in readonly.call(2, 'tools/list')['result']['tools']}
        assert not names.intersection({'prepare_checkpoint_recovery', 'create_checkpoint_working_copy'})
    finally:
        readonly.close()
    assert inventory(root) == before_readonly
    output = Path(args.evidence).resolve()
    output.mkdir(parents=True, exist_ok=True)
    (output / 'checkpoint-recovery-stdio.json').write_text(json.dumps({'state': 'passed',
        'cadConnectionAttempted': False, 'newCopyOnly': True, 'originArtifactsUnchanged': True,
        'requestCount': 2, 'replayRejected': True, 'results': results,
        'hostSha256': hashlib.sha256(Path(args.host).read_bytes()).hexdigest()}, indent=2), encoding='utf-8')
    print('PASS: SDK/native checkpoint intent and materialization, new independent copy, no CAD/OPEN/original changes/replay.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ['dotnet', 'host', 'stage', 'evidence']:
        parser.add_argument('--' + name, required=True)
    checks(parser.parse_args())
