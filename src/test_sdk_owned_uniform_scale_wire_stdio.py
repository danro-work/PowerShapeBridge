"""Official C# STDIO -> native filesystem/guards -> scoped Issue. Fake CAD only."""
import argparse
import json
from pathlib import Path

from test_sdk_stdio import Client
from test_sdk_checkpoint_wire_stdio import Panel as BasePanel, inventory, native, read, sha

FAULTS = ('control', 'changed_process', 'fenced_copy', 'late_context', 'lost_reply', 'packet_tamper')


class Panel(BasePanel):
    def run(self):
        while not self.stop.is_set():
            for path in (self.root / 'requests').glob('*.json'):
                processing = path.with_name(path.name + '.processing')
                try:
                    path.rename(processing)
                except (FileNotFoundError, PermissionError):
                    continue
                try:
                    request = read(processing)
                    recipe = request['arguments']['recipeId']
                    result = native(self.root / 'PSBridge.Worker.exe', '--uniform-scale-wire-request', path.stem)
                    self.calls.append({'recipe': recipe, 'requestId': path.stem, 'result': result})
                    destination = self.root / 'responses' / (path.stem + '.json')
                    if recipe == 'prepare_owned_uniform_scale' and self.fault == 'lost_reply':
                        destination = destination.with_suffix('.unpublished')
                    assert not destination.exists()
                    temporary = destination.with_suffix('.pending')
                    temporary.write_text(json.dumps(result), encoding='utf-8')
                    temporary.rename(destination)
                except BaseException as error:
                    self.errors.append(repr(error))
                    self.stop.set()
            self.stop.wait(.01)


def initialize(client, name):
    client.call(1, 'initialize', {'protocolVersion': '2025-11-25', 'capabilities': {},
                               'clientInfo': {'name': name, 'version': '1'}})
    client.notify('notifications/initialized')
    return client.call(2, 'tools/list')['result']['tools']


def case(args, fault):
    stage = Path(args.stage).resolve()
    seed = native(stage / 'PSBridge.Worker.exe', '--uniform-scale-wire-seed', fault)
    root = Path(seed['root']).resolve()
    assert root.is_relative_to(stage / 'work')
    panel = Panel(root, fault)
    command = [args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root), '--timeout', '8']
    client = Client(command)
    sequence = 10

    def call(name, arguments):
        nonlocal sequence
        sequence += 1
        reply = client.call(sequence, 'tools/call', {'name': name, 'arguments': arguments}, timeout=20)
        assert not panel.errors, panel.errors
        return reply

    def ok(name, arguments):
        reply = call(name, arguments)['result']
        assert not reply.get('isError', False), reply
        return reply['structuredContent']

    try:
        tools = initialize(client, 'owned-uniform_scale-native-wire')
        tool = next(item for item in tools if item['name'] == 'prepare_owned_uniform_scale')
        assert tool['annotations']['readOnlyHint'] is False
        assert tool['inputSchema']['additionalProperties'] is False
        assert not any('execute_uniform_scale' in item['name'] or 'execute_owned_uniform_scale' in item['name'] for item in tools)
        copy = ok('prepare_working_copy', {'sourcePath': seed['sourcePath'], 'sourceSha256': sha(Path(seed['sourcePath']))})
        candidate = ok('inspect_entity_candidates', {})
        binding = ok('bind_working_document', {'candidateDocumentHandle': candidate['candidateDocumentHandle'],
                     'workingCopyHandle': copy['workingCopyHandle']})
        observed_set = ok('prepare_entity_set', {'candidateDocumentHandle': candidate['candidateDocumentHandle'],
                     'candidateEntityHandles': [item['candidateEntityHandle'] for item in candidate['entities']]})
        plan = ok('prepare_uniform_scale_plan', {'entitySetHandle': observed_set['entitySetHandle'], 'parameters': {
                  'factor': 2, 'centre': [10, 20, 30],
                  'coordinateFrame': 'world', 'lengthUnits': 'document_raw', 'mode': 'scale_in_place'}})
        parameters = {'scalePlanHandle': plan['scalePlanHandle'],
                      'workingDocumentBindingHandle': binding['workingDocumentBindingHandle']}
        before = inventory(root)
        foreign = Client(command)
        try:
            initialize(foreign, 'foreign-uniform_scale-scope')
            denied = foreign.call(3, 'tools/call', {'name': 'prepare_owned_uniform_scale', 'arguments': parameters})['result']
            assert denied['isError'] and 'ownedScaleHandle' not in denied['structuredContent']
        finally:
            foreign.close()
        assert inventory(root) == before, 'Foreign handles reached native queue'
        invalid = call('prepare_owned_uniform_scale', dict(parameters, command='SCALE'))
        assert invalid['error']['code'] == -32602 and inventory(root) == before
        prepared = call('prepare_owned_uniform_scale', parameters)['result']
        data = prepared['structuredContent']
        if fault == 'control':
            assert not prepared['isError'] and len(data['ownedScaleHandle']) == 32
            assert 0 < data['ownedScaleLifetimeSeconds'] <= 300
            assert data['ownedScale']['copyId'] == copy['copyId']
            assert data['ownedScale']['parameters'] == plan['plan']['parameters']
            assert {key[0] for key in plan['plan']['entityKeys']} == {40, 21, 59}
        else:
            assert prepared['isError'] and 'ownedScaleHandle' not in data, prepared
        assert [item['recipe'] for item in panel.calls] == [
            'prepare_working_copy', 'inspect_active_model', 'bind_working_document',
            'prepare_entity_set', 'prepare_owned_uniform_scale']
        raw = panel.calls[-1]
        assert data['bridgeRequestId'] == raw['requestId'], 'Original request identity lost in SDK response'
        assert set(read(root / 'requests' / (raw['requestId'] + '.json.processing'))['arguments']['parameters']) == {
            'entitySetId', 'entitySetSha256', 'bindingId', 'bindingSha256', 'parameters'}
        if fault in ('control', 'lost_reply', 'packet_tamper'):
            assert raw['result']['state'] == 'owned_uniform_scale_prepared_observed'
        else:
            assert raw['result']['state'] == 'owned_uniform_scale_preparation_rejected'
        if fault == 'lost_reply':
            assert data['state'] == 'owned_uniform_scale_preparation_outcome_unknown'
            assert (root / 'responses' / (raw['requestId'] + '.unpublished')).is_file()
        for flag in ('executionEnabled', 'geometryMutationAttempted', 'contextChangeAttempted', 'cadSaveAttempted'):
            assert raw['result'][flag] is False
        assert read(sorted((root / 'wire-contexts').glob('*.json'))[-1])['commandCalls'] == 0
        assert all(sha(Path(path)) == digest for path, digest in seed['protectedHashes'].items())
        owner = copy['ownership']
        assert sha(Path(owner['working']['path'])) == sha(Path(seed['sourcePath']))
        assert sha(Path(owner['checkpoint']['path'])) == sha(Path(seed['sourcePath']))
        before = inventory(root)
        readonly = Client(command + ['--read-only'])
        try:
            assert 'prepare_owned_uniform_scale' not in {item['name'] for item in initialize(readonly, 'uniform_scale-readonly')}
        finally:
            readonly.close()
        assert inventory(root) == before
        return {'state': 'passed', 'fault': fault, 'root': str(root), 'seed': seed,
                'copy': copy, 'binding': binding, 'plan': plan, 'prepared': prepared,
                'nativeRequests': panel.calls, 'transcript': client.transcript,
                'cadConnectionAttempted': False, 'commandCalls': 0, 'artifactHashes': inventory(root)}
    finally:
        client.close()
        panel.close()


def checks(args):
    stage = Path(args.stage).resolve()
    assert (stage / '.owned-uniform-scale-fixture').is_file()
    evidence = Path(args.evidence).resolve()
    evidence.mkdir(parents=True, exist_ok=True)
    for fault in FAULTS:
        target = evidence / (fault + '.json')
        assert not target.exists(), 'Fresh evidence required'
        target.write_text(json.dumps(case(args, fault), indent=2), encoding='utf-8')
        print('PASS: actual native scoped uniform_scale STDIO ' + fault, flush=True)
    (evidence / 'uniform-scale-wire-stdio.json').write_text(json.dumps({'state': 'passed', 'cases': len(FAULTS),
        'scope': 'Official STDIO/native ownership guard/scoped Issue; already-open fake CAD document',
        'cadConnectionAttempted': False, 'runtimeDeploymentPerformed': False,
        'hostSha256': sha(Path(args.host)), 'workerSha256': sha(stage / 'PSBridge.Worker.exe')}, indent=2), encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('dotnet', 'host', 'stage', 'evidence'):
        parser.add_argument('--' + name, required=True)
    checks(parser.parse_args())
