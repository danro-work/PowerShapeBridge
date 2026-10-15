"""Real C# STDIO/native helpers, with an explicitly simulated CAD document."""
import argparse
import json
from pathlib import Path

from test_sdk_stdio import Client
from test_sdk_owned_uniform_scale_wire_stdio import initialize
from test_sdk_checkpoint_wire_stdio import Panel as BasePanel, inventory, native, read, sha

FAULTS = ('control', 'return_false', 'wrong_level', 'lost_reply', 'packet_tamper')


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
                    result = native(self.root / 'PSBridge.Worker.exe', '--level-move-wire-request', path.stem)
                    self.calls.append({'recipe': recipe, 'requestId': path.stem, 'result': result})
                    destination = self.root / 'responses' / (path.stem + '.json')
                    if recipe == 'execute_owned_level_move' and self.fault == 'lost_reply':
                        destination = destination.with_suffix('.unpublished')
                    temporary = destination.with_suffix('.pending')
                    assert not destination.exists()
                    temporary.write_text(json.dumps(result), encoding='utf-8')
                    temporary.rename(destination)
                except BaseException as error:
                    self.errors.append(repr(error))
                    self.stop.set()
            self.stop.wait(.01)


def case(args, fault):
    stage = Path(args.stage).resolve()
    seed = native(stage / 'PSBridge.Worker.exe', '--level-move-wire-seed', fault)
    root = Path(seed['root']).resolve()
    assert root.is_relative_to(stage / 'work')
    panel = Panel(root, fault)
    command = [args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root), '--timeout', '8']
    client = Client(command)
    sequence = 10

    def call(name, arguments):
        nonlocal sequence
        sequence += 1
        result = client.call(sequence, 'tools/call', {'name': name, 'arguments': arguments}, timeout=25)
        assert not panel.errors, panel.errors
        return result

    def ok(name, arguments):
        result = call(name, arguments)['result']
        assert not result.get('isError', False), result
        return result['structuredContent']

    try:
        tools = initialize(client, 'owned-level-move-native-wire')
        names = {item['name'] for item in tools}
        operations = {'prepare_owned_level_move', 'execute_owned_level_move', 'verify_owned_level_move'}
        assert operations <= names
        assert all(item['inputSchema']['additionalProperties'] is False for item in tools if item['name'] in operations)
        copy = ok('prepare_working_copy', {'sourcePath': seed['sourcePath'], 'sourceSha256': sha(Path(seed['sourcePath']))})
        candidate = ok('inspect_entity_candidates', {})
        binding = ok('bind_working_document', {'candidateDocumentHandle': candidate['candidateDocumentHandle'], 'workingCopyHandle': copy['workingCopyHandle']})
        selected = ok('prepare_entity_set', {'candidateDocumentHandle': candidate['candidateDocumentHandle'], 'candidateEntityHandles': [item['candidateEntityHandle'] for item in candidate['entities'] if item['nativeTypeCode'] == 40]})
        parameters = {'entitySetHandle': selected['entitySetHandle'], 'workingDocumentBindingHandle': binding['workingDocumentBindingHandle'],
                      'parameters': {'targetLevelNumber': 5, 'targetLevelName': 'Named unused', 'targetActiveRaw': 0,
                                     'selectionAfterPolicy': 'cleared_by_documented_api', 'mode': 'move_in_place'}}
        before = inventory(root)
        invalid = call('prepare_owned_level_move', dict(parameters, command='MOVE'))
        assert invalid['error']['code'] == -32602 and inventory(root) == before
        foreign = Client(command)
        try:
            initialize(foreign, 'foreign-level-move')
            denied = foreign.call(3, 'tools/call', {'name': 'prepare_owned_level_move', 'arguments': parameters})['result']
            assert denied['isError']
        finally:
            foreign.close()
        assert inventory(root) == before
        prepared = call('prepare_owned_level_move', parameters)['result']
        execution = verification = None
        if fault == 'packet_tamper':
            assert prepared['isError'] and 'ownedLevelMoveHandle' not in prepared['structuredContent']
        else:
            assert not prepared.get('isError', False), prepared
            handle = prepared['structuredContent']['ownedLevelMoveHandle']
            execution = call('execute_owned_level_move', {'ownedLevelMoveHandle': handle})['result']
            output = execution['structuredContent']
            assert len(output['levelMoveExecutionHandle']) == 32
            before_retry = inventory(root)
            repeated = call('execute_owned_level_move', {'ownedLevelMoveHandle': handle})['result']
            assert repeated['isError'] and inventory(root) == before_retry
            if fault == 'lost_reply':
                assert execution['isError']
                original = next(item for item in panel.calls if item['recipe'] == 'execute_owned_level_move')
                assert output['bridgeRequestId'] == original['requestId'] and output['retryAllowed'] is False
                unpublished = root / 'responses' / (original['requestId'] + '.unpublished')
                assert unpublished.is_file()
                unpublished.rename(root / 'responses' / (original['requestId'] + '.json'))
            post = ok('inspect_entity_candidates', {})
            listing = ok('list_documents', {})
            verification = call('verify_owned_level_move', {'levelMoveExecutionHandle': output['levelMoveExecutionHandle'],
                                'candidateDocumentHandle': post['candidateDocumentHandle'], 'activeDocumentHandle': listing['documents'][0]['documentHandle']})['result']
            if fault in ('control', 'lost_reply'):
                assert not verification.get('isError', False), verification
                assert verification['structuredContent']['state'] == 'owned_level_move_verified_observed'
                assert not (root / 'workingcopies' / copy['copyId'] / 'mutation-fence.json').exists()
            else:
                assert verification['isError'] and (root / 'workingcopies' / copy['copyId'] / 'mutation-fence.json').is_file()
        state = read(sorted((root / 'wire-contexts').glob('*.json'))[-1])
        if verification is not None:
            assert verification['structuredContent'].get('cadMutationMayHaveOccurred', False) is False
        assert state['commandCalls'] == (0 if fault == 'packet_tamper' else 1)
        assert sum(item['recipe'] == 'execute_owned_level_move' for item in panel.calls) == (0 if fault == 'packet_tamper' else 1)
        assert all(sha(Path(path)) == digest for path, digest in seed['protectedHashes'].items())
        assert sha(Path(copy['ownership']['working']['path'])) == sha(Path(seed['sourcePath']))
        assert sha(Path(copy['ownership']['checkpoint']['path'])) == sha(Path(seed['sourcePath']))
        before_readonly = inventory(root)
        readonly = Client(command + ['--read-only'])
        try:
            assert not operations & {item['name'] for item in initialize(readonly, 'level-move-readonly')}
            blocked = readonly.call(3, 'tools/call', {'name': 'execute_owned_level_move', 'arguments': {'ownedLevelMoveHandle': 'a' * 32}})
            assert 'error' in blocked
        finally:
            readonly.close()
        assert inventory(root) == before_readonly
        return {'state': 'passed', 'fault': fault, 'root': str(root), 'seed': seed, 'copy': copy, 'prepared': prepared,
                'execution': execution, 'verification': verification, 'nativeRequests': panel.calls,
                'transcript': client.transcript, 'artifactHashes': inventory(root), 'commandCalls': state['commandCalls'],
                'cadConnectionAttempted': False}
    finally:
        client.close()
        panel.close()


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('dotnet', 'host', 'stage', 'evidence'):
        parser.add_argument('--' + name, required=True)
    parser.add_argument('--fault', choices=FAULTS)
    args = parser.parse_args()
    evidence = Path(args.evidence).resolve()
    evidence.mkdir(parents=True, exist_ok=True)
    for fault in ((args.fault,) if args.fault else FAULTS):
        destination = evidence / (fault + '.json')
        assert not destination.exists(), 'Fresh evidence required'
        destination.write_text(json.dumps(case(args, fault), indent=2), encoding='utf-8')
        print('PASS: SDK/native level move wire ' + fault, flush=True)
