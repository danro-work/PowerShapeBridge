"""Official SDK STDIO to actual request-driven native recovery/OPEN/verifier. Fake CAD only."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import threading

from test_sdk_stdio import Client

FAULTS = ('control', 'lost_return_after_effect', 'no_effect', 'changed_process',
          'partial_open_journal', 'wrong_verify_model', 'lost_reply')


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def inventory(root):
    return {str(path): sha(path) for path in root.rglob('*') if path.is_file()}


def native(worker, *args):
    result = subprocess.run([str(worker), *args], capture_output=True, timeout=20)
    assert result.returncode == 0, result.stderr.decode('utf-8', errors='replace') + ' / exit ' + str(result.returncode)
    return json.loads(result.stdout.decode('utf-8-sig'))


class Panel:
    def __init__(self, root, fault):
        self.root, self.fault = root, fault
        self.calls, self.errors = [], []
        self.stop = threading.Event()
        (root / 'requests').mkdir(exist_ok=True)
        self.thread = threading.Thread(target=self.run, daemon=True)
        self.thread.start()

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
                    result = native(self.root / 'PSBridge.Worker.exe', '--checkpoint-wire-request', path.stem)
                    self.calls.append({'recipe': recipe, 'requestId': path.stem, 'result': result})
                    destination = self.root / 'responses' / (path.stem + '.json')
                    if recipe == 'open_working_copy' and self.fault == 'lost_reply':
                        destination = destination.with_suffix('.unpublished')
                    assert not destination.exists()
                    temporary = destination.with_suffix('.pending')
                    temporary.write_text(json.dumps(result), encoding='utf-8')
                    temporary.rename(destination)
                except BaseException as error:
                    self.errors.append(repr(error))
                    self.stop.set()
            self.stop.wait(.01)

    def close(self):
        self.stop.set()
        self.thread.join(timeout=25)
        assert not self.thread.is_alive(), 'Native fixture still running; never restart it'


def case(args, fault):
    stage = Path(args.stage).resolve()
    seed = native(stage / 'PSBridge.Worker.exe', '--checkpoint-wire-seed', fault)
    root = Path(seed['root']).resolve()
    assert root.is_relative_to(stage / 'work')
    panel = Panel(root, fault)
    client = Client([args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root), '--timeout', '8'])
    sequence = 10
    def call(name, arguments):
        nonlocal sequence
        sequence += 1
        reply = client.call(sequence, 'tools/call', {'name': name, 'arguments': arguments}, timeout=20)
        assert not panel.errors, panel.errors
        return reply['result']
    def ok(name, arguments):
        reply = call(name, arguments)
        assert not reply.get('isError', False), reply
        return reply['structuredContent']
    def fresh():
        candidate = ok('inspect_entity_candidates', {})
        listing = ok('list_documents', {})
        active = next(item for item in listing['documents'] if item['isActive'])
        return candidate, {'candidateDocumentHandle': candidate['candidateDocumentHandle'],
                           'activeDocumentHandle': active['documentHandle']}
    binding = None
    try:
        client.call(1, 'initialize', {'protocolVersion': '2025-11-25', 'capabilities': {},
                    'clientInfo': {'name': 'checkpoint-native-wire', 'version': '1'}})
        client.notify('notifications/initialized')
        names = {tool['name'] for tool in client.call(2, 'tools/list')['result']['tools']}
        assert {'prepare_checkpoint_recovery', 'create_checkpoint_working_copy', 'open_working_copy',
                'verify_working_copy_open', 'bind_working_document'} <= names
        prepared = ok('prepare_checkpoint_recovery', {'operationId': seed['originId'],
                      'operationPlanSha256': seed['originPlanSha256']})
        recovered = ok('create_checkpoint_working_copy', {'checkpointRecoveryHandle': prepared['checkpointRecoveryHandle']})
        assert recovered['originalOutcomeResolved'] is False and 'workingDocumentBindingHandle' not in recovered
        before_replay = inventory(root)
        assert call('create_checkpoint_working_copy', {'checkpointRecoveryHandle': prepared['checkpointRecoveryHandle']})['isError']
        assert before_replay == inventory(root)
        _, choice = fresh()
        open_args = dict(choice, workingCopyHandle=recovered['workingCopyHandle'])
        opened = call('open_working_copy', open_args)
        before_replay = inventory(root)
        assert call('open_working_copy', open_args)['isError']
        assert before_replay == inventory(root), 'Rejected old OPEN handles changed files'
        raw_open = next(item for item in panel.calls if item['recipe'] == 'open_working_copy')
        assert sum(item['recipe'] == 'open_working_copy' for item in panel.calls) == 1
        verified = None
        if fault != 'lost_reply':
            _, choice = fresh()
            receipt = root / 'responses' / (raw_open['requestId'] + '.json')
            verified = call('verify_working_copy_open', dict(choice, openRequestId=raw_open['requestId'], openReceiptSha256=sha(receipt)))
            if fault == 'control':
                assert not opened['isError'] and not verified['isError'], (opened, verified)
                assert verified['structuredContent']['recordedJournalState'] == 'succeeded_verified'
                current, _ = fresh()
                assert current['model']['id'] == 500
                binding = ok('bind_working_document', {'candidateDocumentHandle': current['candidateDocumentHandle'],
                             'workingCopyHandle': verified['structuredContent']['workingCopyHandle']})
                assert binding['binding']['copyId'] == recovered['copyId']
                assert binding['workingDocumentBindingHandle']
            else:
                assert verified['isError'] and 'workingCopyHandle' not in verified['structuredContent'], verified
        else:
            assert opened['isError'] and opened['structuredContent']['state'] == 'timeout_outcome_unknown', opened
            assert not (root / 'responses' / (raw_open['requestId'] + '.json')).exists()
            assert (root / 'responses' / (raw_open['requestId'] + '.unpublished')).is_file()
        context = read(sorted((root / 'wire-contexts').glob('*.json'))[-1])
        assert context['commandCalls'] == 1
        assert all(sha(Path(path)) == digest for path, digest in seed['protectedHashes'].items())
        states = [read(path)['state'] for path in sorted((root / 'operations' / seed['originId']).glob('*.json'))]
        assert states == ['prepared', 'execution_claimed', 'command_started']
        readonly = Client([args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root), '--read-only'])
        before_readonly = inventory(root)
        try:
            readonly.call(1, 'initialize', {'protocolVersion': '2025-11-25', 'capabilities': {},
                          'clientInfo': {'name': 'checkpoint-wire-readonly', 'version': '1'}})
            readonly.notify('notifications/initialized')
            readonly_names = {tool['name'] for tool in readonly.call(2, 'tools/list')['result']['tools']}
            assert not readonly_names.intersection({'prepare_checkpoint_recovery', 'create_checkpoint_working_copy',
                                                   'open_working_copy', 'verify_working_copy_open', 'bind_working_document'})
        finally:
            readonly.close()
        assert inventory(root) == before_readonly
        return {'fault': fault, 'root': str(root), 'state': 'passed', 'cadConnectionAttempted': False,
                'originOutcomeResolved': False, 'commandCalls': 1, 'seed': seed,
                'recovered': recovered, 'opened': opened, 'verified': verified, 'binding': binding,
                'nativeRequests': panel.calls, 'transcript': client.transcript, 'artifactHashes': inventory(root)}
    finally:
        client.close()
        panel.close()


def checks(args):
    stage = Path(args.stage).resolve()
    assert (stage / '.checkpoint-recovery-fixture').is_file()
    evidence = Path(args.evidence).resolve()
    evidence.mkdir(parents=True, exist_ok=True)
    for fault in FAULTS:
        destination = evidence / (fault + '.json')
        assert not destination.exists(), 'Use fresh evidence; never overwrite prior proof'
        result = case(args, fault)
        destination.write_text(json.dumps(result, indent=2), encoding='utf-8')
        print('PASS: actual SDK/native checkpoint wire scenario ' + fault, flush=True)
    (evidence / 'checkpoint-wire-stdio.json').write_text(json.dumps({'state': 'passed', 'cases': len(FAULTS),
        'scope': 'Official SDK STDIO, real queue/native filesystem/OPEN/verifier/binding; fake CAD contexts',
        'cadConnectionAttempted': False, 'runtimeDeploymentPerformed': False,
        'hostSha256': sha(Path(args.host)), 'workerSha256': sha(stage / 'PSBridge.Worker.exe')}, indent=2), encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('dotnet', 'host', 'stage', 'evidence'):
        parser.add_argument('--' + name, required=True)
    checks(parser.parse_args())
