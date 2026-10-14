"""Independent STDIO for passive K8 uniform-scale plans; two fake read/set recipes, no CAD."""
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
    root = project / 'work' / ('K8-uniform-scale-stdio-' + uuid.uuid4().hex)
    root.mkdir(parents=True)
    for folder in ('requests', 'responses', 'entitysets'):
        (root / folder).mkdir()
    config = json.loads((project / 'config/general-uniform-scale-plans.json').read_text(encoding='utf-8-sig'))
    (root / 'bridge.config.json').write_text(json.dumps(config), encoding='utf-8')
    (root / 'PSBridge.Worker.exe').write_text('Fake marker; never execute', encoding='utf-8')
    panel = Panel(root)
    panel.source['entities'] += [{'nativeTypeCode': 21, 'nativeId': 8, 'type': 'SURFACE', 'name': 'Plane', 'levelNumber': 0},
                                {'nativeTypeCode': 59, 'nativeId': 10, 'type': 'COMPCURVE', 'name': 'Curve', 'levelNumber': 0}]
    panel.source['entityCount'] = len(panel.source['entities'])
    command = [args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root)]
    client = Client(command)
    try:
        client.call(1, 'initialize', {'protocolVersion': '2025-11-25', 'capabilities': {}, 'clientInfo': {'name': 'uniform-scale-plan-independent', 'version': '1'}})
        client.notify('notifications/initialized')
        catalog = client.call(2, 'tools/list')['result']['tools']
        tool = next(tool for tool in catalog if tool['name'] == 'prepare_uniform_scale_plan')
        assert tool['annotations']['readOnlyHint'] is False and tool['inputSchema']['additionalProperties'] is False
        assert not any('execute_uniform_scale' in item['name'] for item in catalog)
        candidates = client.call(3, 'tools/call', {'name': 'inspect_entity_candidates', 'arguments': {}})['result']['structuredContent']
        observed_set = client.call(4, 'tools/call', {'name': 'prepare_entity_set', 'arguments': {
            'candidateDocumentHandle': candidates['candidateDocumentHandle'],
            'candidateEntityHandles': [item['candidateEntityHandle'] for item in candidates['entities']]}})['result']['structuredContent']
        parameters = {'factor': 2, 'centre': [10, 20, 30], 'coordinateFrame': 'world', 'lengthUnits': 'document_raw', 'mode': 'scale_in_place'}
        response = client.call(5, 'tools/call', {'name': 'prepare_uniform_scale_plan', 'arguments': {'entitySetHandle': observed_set['entitySetHandle'], 'parameters': parameters}})['result']
        assert not response['isError'], response
        data = response['structuredContent']
        assert data['plan']['executionEnabled'] is False and data['plan']['parameters'] == parameters and data['plan']['nativeProviderQualified'] is False
        assert {key[0] for key in data['plan']['entityKeys']} == {40, 21, 59}
        plan_path = root / 'scaleplans' / (data['operationId'] + '.json')
        assert sha(plan_path) == data['planSha256'] and json.loads(plan_path.read_bytes()) == data['plan']
        dependency = data['plan']['entitySet']
        set_path = root / 'entitysets' / (dependency['entitySetId'] + '.json')
        assert sha(set_path) == dependency['entitySetSha256']
        native_set = json.loads(set_path.read_bytes())
        source_path = root / 'responses' / (native_set['inspectionRequestId'] + '.json')
        assert sha(source_path) == native_set['inspectionSha256']
        event = json.loads((root / 'operations' / data['operationId'] / '000000.json').read_bytes())
        assert event['state'] == 'prepared' and event['sequence'] == 0 and event['planSha256'] == data['planSha256']
        before = {str(path): sha(path) for path in root.rglob('*') if path.is_file()}
        rejected = client.call(6, 'tools/call', {'name': 'prepare_uniform_scale_plan', 'arguments': {'entitySetHandle': observed_set['entitySetHandle'], 'parameters': dict(parameters, factor=1)}})
        assert rejected['error']['code'] == -32602
        assert before == {str(path): sha(path) for path in root.rglob('*') if path.is_file()}
        for request_id, bad in enumerate((dict(parameters, factor=-2), dict(parameters, factor=True),
                dict(parameters, command='NeverExecute'), dict(parameters, centre=[0, 0])), 7):
            rejected = client.call(request_id, 'tools/call', {'name': 'prepare_uniform_scale_plan',
                'arguments': {'entitySetHandle': observed_set['entitySetHandle'], 'parameters': bad}})
            assert rejected['error']['code'] == -32602
            assert before == {str(path): sha(path) for path in root.rglob('*') if path.is_file()}
        assert len(list((root / 'requests').iterdir())) == 2 and len(panel.calls) == 2
        assert not panel.errors, panel.errors
        readonly = Client(command + ['--read-only'])
        try:
            readonly.call(1, 'initialize', {'protocolVersion': '2025-11-25', 'capabilities': {}, 'clientInfo': {'name': 'uniform-scale-readonly-denial', 'version': '1'}})
            readonly.notify('notifications/initialized')
            assert 'prepare_uniform_scale_plan' not in {item['name'] for item in readonly.call(2, 'tools/list')['result']['tools']}
            denied = readonly.call(3, 'tools/call', {'name': 'prepare_uniform_scale_plan',
                'arguments': {'entitySetHandle': observed_set['entitySetHandle'], 'parameters': parameters}})
            assert 'error' in denied
        finally:
            readonly.close()
        evidence = Path(args.evidence).resolve()
        evidence.mkdir(parents=True, exist_ok=True)
        (evidence / 'uniform-scale-plan-stdio.json').write_text(json.dumps({'state': 'passed', 'cadConnectionAttempted': False,
            'scope': 'Official STDIO, fake native read/set; passive local plan, no execution', 'nativeRecipes': panel.calls,
            'root': str(root), 'plan': data, 'artifactHashes': {str(path): sha(path) for path in root.rglob('*') if path.is_file()},
            'transcript': client.transcript, 'hostSha256': sha(Path(args.host))}, indent=2), encoding='utf-8')
    finally:
        try:
            client.close()
        finally:
            panel.stop.set();panel.thread.join(timeout=3)
    print('PASS: mixed SOLID/SURFACE/COMPCURVE uniform-scale plan via STDIO, explicit centre/factor/bytes, noop/read-only refusals; no CAD.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('dotnet', 'host', 'evidence'):
        parser.add_argument('--' + name, required=True)
    checks(parser.parse_args())
