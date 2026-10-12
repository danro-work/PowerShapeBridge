"""Official SDK STDIO saved-result facade on actual native fixtures; fake IPC, no CAD."""
import argparse
import copy
import datetime
import hashlib
import json
from pathlib import Path
import threading
import time

from test_sdk_stdio import Client


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


class Panel:
    def __init__(self, root, metadata):
        self.root = root
        self.meta = metadata
        self.receipt = read(root / 'responses' / (metadata['verificationRequestId'] + '.json'))
        parameters = metadata['parameters']
        self.inspection = read(root / 'responses' / (parameters['inspectionRequestId'] + '.json'))
        self.listing = read(root / 'responses' / (parameters['listingRequestId'] + '.json'))
        self.listing['documents'] = [{'modelId': self.listing['activeWindow']['modelId'], 'windowNumber': self.listing['activeWindow']['windowNumber'], 'activationSupported': True}]
        self.calls = []
        self.errors = []
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
                except (PermissionError, FileNotFoundError):
                    continue
                try:
                    request = read(processing)
                    assert request['operation'] == 'recipe.prepare'
                    recipe = request['arguments']['recipeId']
                    parameters = request['arguments']['parameters']
                    now = datetime.datetime.now(datetime.timezone.utc).isoformat()
                    if recipe in ('inspect_active_model', 'list_documents'):
                        assert parameters == {}
                        result = copy.deepcopy(self.inspection if recipe == 'inspect_active_model' else self.listing)
                    else:
                        assert recipe == 'verify_owned_save_as', recipe
                        assert len(parameters) == 9 and parameters['saveRequestId'] == self.meta['parameters']['saveRequestId']
                        assert parameters['saveReceiptSha256'] == self.meta['parameters']['saveReceiptSha256']
                        result = copy.deepcopy(self.receipt)
                        result['expiresUtc'] = parameters['expiresUtc']
                        result['evidence'] = [{'requestId': parameters[a], 'sha256': parameters[b]} for a, b in
                                              [('saveRequestId', 'saveReceiptSha256'), ('inspectionRequestId', 'inspectionSha256'), ('listingRequestId', 'listingSha256')]]
                    result['bridgeRequestId'] = path.stem
                    result['timestampUtc'] = now
                    raw = b'\xef\xbb\xbf' + json.dumps(result, indent=2).encode('utf-8')
                    temporary = self.root / 'responses' / (path.stem + '.reply-tmp')
                    temporary.write_bytes(raw)
                    temporary.rename(self.root / 'responses' / (path.stem + '.json'))
                    self.calls.append(recipe)
                except Exception as error:
                    self.errors.append(repr(error))
                    self.stop.set()
            self.stop.wait(.01)


def checks(args):
    stage = Path(args.stage).resolve()
    markers = list(stage.glob('work/owned-operation-self-test-*/saved-result-fixture.json'))
    assert len(markers) == 1
    root = markers[0].parent
    settings = read(Path(__file__).resolve().parents[1] / 'config/general-owned-saves.json')
    (root / 'bridge.config.json').write_text(json.dumps(settings), encoding='utf-8')
    metadata = read(markers[0])
    before = {path: hashlib.sha256(path.read_bytes()).hexdigest() for path in root.glob('operations/*/*.json')}
    panel = Panel(root, metadata)
    client = Client([args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root)])
    readonly = Client([args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root), '--read-only'])
    try:
        for peer in (client, readonly):
            assert 'result' in peer.call(1, 'initialize', {'protocolVersion': '2025-11-25', 'capabilities': {}, 'clientInfo': {'name': 'saved-result-independent', 'version': '1'}})
            peer.notify('notifications/initialized')
        tools = client.call(2, 'tools/list')['result']['tools']
        assert len(tools) == 24
        by_name = {tool['name']: tool for tool in tools}
        shapes = {'prepare_owned_save_as': ['workingDocumentBindingHandle'], 'execute_owned_save_as': ['ownedSaveAsHandle'],
                  'verify_owned_save_as': ['saveRequestId', 'saveReceiptSha256', 'candidateDocumentHandle', 'activeDocumentHandle']}
        for name, required in shapes.items():
            schema = by_name[name]['inputSchema']
            assert schema['required'] == required and set(schema['properties']) == set(required) and schema['additionalProperties'] is False
            for key, value in schema['properties'].items():
                assert value['type'] == 'string' and value['pattern'] == ('^[0-9a-f]{64}$' if key.endswith('Sha256') else '^[0-9a-f]{32}$')
        assert len(readonly.call(2, 'tools/list')['result']['tools']) == 6
        for number, name in enumerate(shapes, 3):
            refused = readonly.call(number, 'tools/call', {'name': name, 'arguments': {}})
            assert refused['error']['code'] == -32602
        invalid = client.call(3, 'tools/call', {'name': 'execute_owned_save_as', 'arguments': {'ownedSaveAsHandle': True, 'path': 'C:/Source.psmodel'}})
        assert invalid['error']['code'] == -32602 and panel.calls == []
        candidate = client.call(4, 'tools/call', {'name': 'inspect_entity_candidates', 'arguments': {}})['result']
        listing = client.call(5, 'tools/call', {'name': 'list_documents', 'arguments': {}})['result']
        assert not candidate['isError'] and not listing['isError'], {'candidate': candidate, 'listing': listing, 'panelErrors': panel.errors}
        arguments = {'saveRequestId': metadata['parameters']['saveRequestId'], 'saveReceiptSha256': metadata['parameters']['saveReceiptSha256'],
                     'candidateDocumentHandle': candidate['structuredContent']['candidateDocumentHandle'], 'activeDocumentHandle': listing['structuredContent']['documents'][0]['documentHandle']}
        verified = client.call(6, 'tools/call', {'name': 'verify_owned_save_as', 'arguments': arguments})['result']
        assert not verified['isError'], verified
        saved = verified['structuredContent']
        assert len(saved['savedResultHandle']) == 32 and 0 < saved['savedResultLifetimeSeconds'] <= 300
        assert saved['state'] == 'owned_save_as_verified_observed' and saved['cadSaveAttempted'] is False
        assert 'workingDocumentBindingHandle' not in saved
        replay_choice = client.call(7, 'tools/call', {'name': 'verify_owned_save_as', 'arguments': arguments})['result']
        assert replay_choice['isError'] and panel.calls == ['inspect_active_model', 'list_documents', 'verify_owned_save_as']
        assert not panel.errors, panel.errors
        assert all(hashlib.sha256(path.read_bytes()).hexdigest() == digest for path, digest in before.items())
        evidence = Path(args.evidence).resolve()
        evidence.mkdir(parents=True, exist_ok=True)
        (evidence / 'sdk-saved-result-stdio.json').write_text(json.dumps({'state': 'passed', 'scope': 'Official SDK STDIO and fake verifier on actual Framework saved-result artifacts; no live CAD',
            'fullCatalog': 24, 'readOnlyCatalog': 6, 'cadConnectionAttempted': False, 'runtimeDeploymentPerformed': False,
            'nativeFixtureRoot': str(root), 'nativeRecipes': panel.calls, 'journalUnchanged': True, 'savedResult': saved, 'transcript': client.transcript}, indent=2), encoding='utf-8')
    finally:
        client.close()
        readonly.close()
        panel.stop.set()
        panel.thread.join(timeout=3)
    print('PASS: SDK saved-result STDIO, 24/6 catalogs, exact schemas, fresh proofs, one-use choice and unchanged journal; no CAD.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('dotnet', 'host', 'stage', 'evidence'):
        parser.add_argument('--' + name, required=True)
    checks(parser.parse_args())
