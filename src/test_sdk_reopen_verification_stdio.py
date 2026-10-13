"""External official SDK STDIO on actual native reopen proofs; fake reads/verifier, no CAD."""
import argparse
import copy
import datetime
import hashlib
import json
from pathlib import Path
import threading

from test_sdk_stdio import Client


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


class Panel:
    def __init__(self, root, metadata):
        self.root, self.metadata = root, metadata
        self.receipt = read(root / 'responses' / (metadata['verificationRequestId'] + '.json'))
        params = metadata['parameters']
        self.snapshot = read(root / 'responses' / (params['inspectionRequestId'] + '.json'))
        self.listing = read(root / 'responses' / (params['listingRequestId'] + '.json'))
        active = self.listing['activeWindow']
        self.listing['documents'] = [{'modelId': active['modelId'], 'windowNumber': active['windowNumber'], 'activationSupported': True}]
        self.calls, self.errors = [], []
        self.stop = threading.Event()
        (root / 'requests').mkdir(exist_ok=True)
        (root / 'PSBridge.Worker.exe').write_text('Opaque marker; never executed.', encoding='utf-8')
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
                    envelope = read(processing)
                    assert envelope['operation'] == 'recipe.prepare'
                    recipe, params = envelope['arguments']['recipeId'], envelope['arguments']['parameters']
                    now = datetime.datetime.now(datetime.timezone.utc).isoformat()
                    if recipe in ('inspect_active_model', 'list_documents'):
                        assert params == {}
                        result = copy.deepcopy(self.snapshot if recipe == 'inspect_active_model' else self.listing)
                    else:
                        assert recipe == 'verify_owned_saved_reopen'
                        assert len(params) == 9
                        assert params['reopenRequestId'] == self.metadata['parameters']['reopenRequestId']
                        assert params['reopenReceiptSha256'] == self.metadata['parameters']['reopenReceiptSha256']
                        result = copy.deepcopy(self.receipt)
                        result['expiresUtc'] = params['expiresUtc']
                        result['evidence'] = [{'requestId': params[a], 'sha256': params[b]} for a, b in [('reopenRequestId', 'reopenReceiptSha256'), ('inspectionRequestId', 'inspectionSha256'), ('listingRequestId', 'listingSha256')]]
                        binding = result['binding']
                        binding['bindingId'], binding['timestampUtc'] = path.stem, now
                        for key in ('inspectionRequestId', 'inspectionSha256', 'expiresUtc'):
                            binding[key] = params[key]
                        raw = b'\xef\xbb\xbf' + json.dumps(binding, indent=2).encode('utf-8')
                        (self.root / 'documentbindings' / (path.stem + '.json')).write_bytes(raw)
                        result['bindingId'], result['bindingSha256'] = path.stem, hashlib.sha256(raw).hexdigest()
                    result['bridgeRequestId'], result['timestampUtc'] = path.stem, now
                    temporary = self.root / 'responses' / (path.stem + '.pending')
                    temporary.write_bytes(b'\xef\xbb\xbf' + json.dumps(result, indent=2).encode('utf-8'))
                    temporary.rename(self.root / 'responses' / (path.stem + '.json'))
                    self.calls.append(recipe)
                except Exception as error:
                    self.errors.append(repr(error))
                    self.stop.set()
            self.stop.wait(.01)


def checks(args):
    stage = Path(args.stage).resolve()
    markers = list(stage.glob('work/owned-operation-self-test-*/saved-reopen-fixture.json'))
    assert len(markers) == 1
    root, metadata = markers[0].parent, read(markers[0])
    settings = read(Path(__file__).resolve().parents[1] / 'config/general-owned-saved-reopen.json')
    if args.recovery_resolution:
        settings.update(recoveryInspectionExperimental=True, recoveryResolutionExperimental=True)
    (root / 'bridge.config.json').write_text(json.dumps(settings), encoding='utf-8')
    original_journal = {path: hashlib.sha256(path.read_bytes()).hexdigest() for path in root.glob('operations/*/*.json')}
    panel = Panel(root, metadata)
    client = Client([args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root)])
    readonly = Client([args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root), '--read-only'])
    try:
        for peer in (client, readonly):
            assert 'result' in peer.call(1, 'initialize', {'protocolVersion': '2025-11-25', 'capabilities': {}, 'clientInfo': {'name': 'reopen-independent', 'version': '1'}})
            peer.notify('notifications/initialized')
        assert len(client.call(2, 'tools/list')['result']['tools']) == 27 + int(args.recovery_resolution)
        assert len(readonly.call(2, 'tools/list')['result']['tools']) == 6 + int(args.recovery_resolution)
        for number, recipe in enumerate(('execute_owned_saved_reopen', 'verify_owned_saved_reopen'), 3):
            assert readonly.call(number, 'tools/call', {'name': recipe, 'arguments': {}})['error']['code'] == -32602
        candidate = client.call(3, 'tools/call', {'name': 'inspect_entity_candidates', 'arguments': {}})['result']
        listing = client.call(4, 'tools/call', {'name': 'list_documents', 'arguments': {}})['result']
        assert not candidate['isError'] and not listing['isError'], panel.errors
        arguments = {'reopenRequestId': metadata['parameters']['reopenRequestId'], 'reopenReceiptSha256': metadata['parameters']['reopenReceiptSha256'],
                     'candidateDocumentHandle': candidate['structuredContent']['candidateDocumentHandle'], 'activeDocumentHandle': listing['structuredContent']['documents'][0]['documentHandle']}
        response = client.call(5, 'tools/call', {'name': 'verify_owned_saved_reopen', 'arguments': arguments})['result']
        assert not response['isError'], response
        result = response['structuredContent']
        assert len(result['workingDocumentBindingHandle']) == len(result['workingCopyHandle']) == 32
        assert result['model']['id'] == 500 and 0 < result['bindingLifetimeSeconds'] <= 300
        assert result['noCommandReplay'] is True and result['contextChangeAttempted'] is False
        assert [e['nativeId'] for e in candidate['structuredContent']['entities']] == [1005, 1008, 1010]
        replay = client.call(6, 'tools/call', {'name': 'verify_owned_saved_reopen', 'arguments': arguments})['result']
        assert replay['isError'] and panel.calls == ['inspect_active_model', 'list_documents', 'verify_owned_saved_reopen']
        assert not panel.errors, panel.errors
        if args.recovery_resolution:
            proof = result['outcomeResolution']
            record = root / 'recoveryresolutions' / arguments['reopenRequestId'] / (proof['verificationRequestId'] + '.json')
            assert hashlib.sha256(record.read_bytes()).hexdigest() == proof['resolutionSha256']
            assert all(proof[key] is False for key in ('executionAuthorized', 'retryAllowed', 'checkpointRestoreAuthorized'))
            history = readonly.call(90, 'tools/call', {'name': 'inspect_operation_recovery', 'arguments': {'operationId': arguments['reopenRequestId']}})['result']
            assert not history['isError'], history
            assert history['structuredContent']['outcome'] == 'not_resolved_by_recorded_inspection'
            recorded = history['structuredContent']['resolutionHistory']
            assert recorded['state'] == 'resolution_history_read' and any(r['verificationRequestId'] == proof['verificationRequestId'] for r in recorded['records']), recorded
            assert panel.calls == ['inspect_active_model', 'list_documents', 'verify_owned_saved_reopen']
        assert all(hashlib.sha256(path.read_bytes()).hexdigest() == digest for path, digest in original_journal.items())
        evidence = Path(args.evidence).resolve()
        (evidence / ('sdk-reopen-resolution-stdio.json' if args.recovery_resolution else 'sdk-reopen-verification-stdio.json')).write_text(json.dumps({'state': 'passed', 'scope': 'Actual Framework saved-reopen original/terminal proof, official SDK STDIO, synthetic fresh read/verifier/binding replies; no live CAD',
            'fullCatalog': 27 + int(args.recovery_resolution), 'readOnlyCatalog': 6 + int(args.recovery_resolution), 'cadConnectionAttempted': False, 'runtimeDeploymentPerformed': False, 'nativeFixtureRoot': str(root), 'nativeRecipes': panel.calls, 'journalUnchanged': True, 'verifiedReopen': result, 'transcript': client.transcript}, indent=2), encoding='utf-8')
    finally:
        client.close()
        readonly.close()
        panel.stop.set()
        panel.thread.join(timeout=3)
    print('PASS: SDK saved-reopen verification STDIO, actual native proofs, %d/%d catalogs, new binding/current entity IDs and unchanged journal%s; no CAD.' % (27 + int(args.recovery_resolution), 6 + int(args.recovery_resolution), ', immutable resolution/read-only history checked' if args.recovery_resolution else ''))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for key in ('dotnet', 'host', 'stage', 'evidence'):
        parser.add_argument('--' + key, required=True)
    parser.add_argument('--recovery-resolution', action='store_true')
    checks(parser.parse_args())
