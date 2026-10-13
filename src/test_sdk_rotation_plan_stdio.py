"""Independent STDIO for passive K8 rotation plans; two fake read/set recipes, no CAD."""
import argparse
import hashlib
import json
from pathlib import Path
import uuid

from test_sdk_stdio import Client
from test_sdk_prepared import Panel


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def checks(args):
    project = Path(__file__).resolve().parents[1]
    root = project / 'work' / ('K8-rotation-stdio-' + uuid.uuid4().hex)
    root.mkdir(parents=True)
    for folder in ('requests', 'responses', 'entitysets'):
        (root / folder).mkdir()
    config = json.loads((project / 'config/general-rotation-plans.json').read_text(encoding='utf-8-sig'))
    (root / 'bridge.config.json').write_text(json.dumps(config), encoding='utf-8')
    (root / 'PSBridge.Worker.exe').write_text('Fake marker; never execute', encoding='utf-8')
    panel = Panel(root)
    panel.source['entities'] += [{'nativeTypeCode': 21, 'nativeId': 8, 'type': 'SURFACE', 'name': 'Plane', 'levelNumber': 0},
                                {'nativeTypeCode': 59, 'nativeId': 10, 'type': 'COMPCURVE', 'name': 'Curve', 'levelNumber': 0}]
    panel.source['entityCount'] = len(panel.source['entities'])
    command = [args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root)]
    client = Client(command)
    try:
        client.call(1, 'initialize', {'protocolVersion': '2025-11-25', 'capabilities': {}, 'clientInfo': {'name': 'rotation-plan-independent', 'version': '1'}})
        client.notify('notifications/initialized')
        catalog = client.call(2, 'tools/list')['result']['tools']
        tool = next(tool for tool in catalog if tool['name'] == 'prepare_rotation_plan')
        assert tool['annotations']['readOnlyHint'] is False and tool['inputSchema']['additionalProperties'] is False
        assert not any('execute_rotation' in item['name'] for item in catalog)
        candidates = client.call(3, 'tools/call', {'name': 'inspect_entity_candidates', 'arguments': {}})['result']['structuredContent']
        observed_set = client.call(4, 'tools/call', {'name': 'prepare_entity_set', 'arguments': {
            'candidateDocumentHandle': candidates['candidateDocumentHandle'],
            'candidateEntityHandles': [item['candidateEntityHandle'] for item in candidates['entities']]}})['result']['structuredContent']
        parameters = {'angleDegrees': 90, 'origin': [10, 20, 30], 'direction': [0, 0, 2], 'coordinateFrame': 'world', 'lengthUnits': 'document_raw'}
        response = client.call(5, 'tools/call', {'name': 'prepare_rotation_plan', 'arguments': {'entitySetHandle': observed_set['entitySetHandle'], 'parameters': parameters}})['result']
        assert not response['isError'], response
        data = response['structuredContent']
        assert data['plan']['executionEnabled'] is False and data['plan']['nativeAxis'] == [10, 20, 30, 0, 0, 1]
        assert {key[0] for key in data['plan']['entityKeys']} == {40, 21, 59}
        plan_path = root / 'rotationplans' / (data['operationId'] + '.json')
        assert sha(plan_path) == data['planSha256'] and json.loads(plan_path.read_bytes()) == data['plan']
        before = {str(path): sha(path) for path in root.rglob('*') if path.is_file()}
        rejected = client.call(6, 'tools/call', {'name': 'prepare_rotation_plan', 'arguments': {'entitySetHandle': observed_set['entitySetHandle'], 'parameters': dict(parameters, angleDegrees=0.5)}})
        assert rejected['error']['code'] == -32602
        assert before == {str(path): sha(path) for path in root.rglob('*') if path.is_file()}
        assert len(list((root / 'requests').iterdir())) == 2 and len(panel.calls) == 2
        assert not panel.errors, panel.errors
        readonly = Client(command + ['--read-only'])
        try:
            readonly.call(1, 'initialize', {'protocolVersion': '2025-11-25', 'capabilities': {}, 'clientInfo': {'name': 'rotation-readonly-denial', 'version': '1'}})
            readonly.notify('notifications/initialized')
            assert 'prepare_rotation_plan' not in {item['name'] for item in readonly.call(2, 'tools/list')['result']['tools']}
        finally:
            readonly.close()
        evidence = Path(args.evidence).resolve()
        evidence.mkdir(parents=True, exist_ok=True)
        (evidence / 'rotation-plan-stdio.json').write_text(json.dumps({'state': 'passed', 'cadConnectionAttempted': False,
            'scope': 'Official STDIO, fake native read/set; passive local plan, no execution', 'nativeRecipes': panel.calls,
            'root': str(root), 'plan': data, 'artifactHashes': {str(path): sha(path) for path in root.rglob('*') if path.is_file()},
            'transcript': client.transcript, 'hostSha256': sha(Path(args.host))}, indent=2), encoding='utf-8')
    finally:
        try:
            client.close()
        finally:
            panel.stop.set();panel.thread.join(timeout=3)
    print('PASS: mixed SOLID/SURFACE/COMPCURVE rotation plan via STDIO, exact axis/bytes, fractional/read-only refusals; no CAD.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('dotnet', 'host', 'evidence'):
        parser.add_argument('--' + name, required=True)
    checks(parser.parse_args())
