"""Official SDK STDIO saved-result facade on actual native fixtures; fake IPC, no CAD."""
import argparse
import copy
import datetime
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import threading
import time

from test_sdk_stdio import Client


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


class Panel:
    def __init__(self, root, metadata, native_prepare_worker=None):
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
        self.native_prepare_worker = native_prepare_worker
        if native_prepare_worker:
            shutil.copyfile(native_prepare_worker, root / 'PSBridge.Worker.exe')
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
                    if recipe == 'prepare_owned_saved_reopen':
                        # Run ONLY the filesystem-only recipe in this isolated
                        # fixture root. No live runtime, panel or COM attachment.
                        assert self.native_prepare_worker and set(parameters) == {'verificationRequestId', 'verificationReceiptSha256'}
                        child = subprocess.run([str(self.root / 'PSBridge.Worker.exe'), '--mcp-operation', path.stem], capture_output=True, timeout=20, check=True)
                        result = json.loads(child.stdout.decode('utf-8-sig'))
                        assert result['powerShapeConnectionAttempted'] is False
                    elif recipe in ('inspect_active_model', 'list_documents'):
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
    settings = read(Path(__file__).resolve().parents[1] / ('config/general-owned-saved-reopen.json' if args.reopen else 'config/general-owned-saves.json'))
    if args.recovery_resolution:
        settings.update(recoveryInspectionExperimental=True, recoveryResolutionExperimental=True)
    (root / 'bridge.config.json').write_text(json.dumps(settings), encoding='utf-8')
    metadata = read(markers[0])
    before = {path: hashlib.sha256(path.read_bytes()).hexdigest() for path in root.glob('operations/*/*.json')}
    panel = Panel(root, metadata, stage / 'PSBridge.Worker.exe' if args.reopen else None)
    client = Client([args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root)])
    readonly = Client([args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root), '--read-only'])
    try:
        for peer in (client, readonly):
            assert 'result' in peer.call(1, 'initialize', {'protocolVersion': '2025-11-25', 'capabilities': {}, 'clientInfo': {'name': 'saved-result-independent', 'version': '1'}})
            peer.notify('notifications/initialized')
        tools = client.call(2, 'tools/list')['result']['tools']
        assert len(tools) == (27 if args.reopen else 24) + int(args.recovery_resolution)
        by_name = {tool['name']: tool for tool in tools}
        shapes = {'prepare_owned_save_as': ['workingDocumentBindingHandle'], 'execute_owned_save_as': ['ownedSaveAsHandle'],
                  'verify_owned_save_as': ['saveRequestId', 'saveReceiptSha256', 'candidateDocumentHandle', 'activeDocumentHandle']}
        if args.reopen:
            shapes['prepare_owned_saved_reopen'] = ['savedResultHandle']
            shapes['execute_owned_saved_reopen'] = ['ownedReopenHandle', 'candidateDocumentHandle', 'activeDocumentHandle']
            shapes['verify_owned_saved_reopen'] = ['reopenRequestId', 'reopenReceiptSha256', 'candidateDocumentHandle', 'activeDocumentHandle']
        for name, required in shapes.items():
            schema = by_name[name]['inputSchema']
            assert schema['required'] == required and set(schema['properties']) == set(required) and schema['additionalProperties'] is False
            for key, value in schema['properties'].items():
                assert value['type'] == 'string' and value['pattern'] == ('^[0-9a-f]{64}$' if key.endswith('Sha256') else '^[0-9a-f]{32}$')
        assert len(readonly.call(2, 'tools/list')['result']['tools']) == 6 + int(args.recovery_resolution)
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
        prepared = None
        if args.reopen:
            prepared = client.call(8, 'tools/call', {'name': 'prepare_owned_saved_reopen', 'arguments': {'savedResultHandle': saved['savedResultHandle']}}, timeout=30)['result']
            assert not prepared['isError'], prepared
            prepared = prepared['structuredContent']
            assert prepared['state'] == 'owned_saved_reopen_prepared_files' and len(prepared['ownedReopenHandle']) == 32
            assert prepared['powerShapeConnectionAttempted'] is False and 'workingDocumentBindingHandle' not in prepared
            plan = prepared['reopen']
            paths = [plan['seed']['path'], plan['workingPath'], plan['checkpointPath']]
            assert all(hashlib.sha256(Path(path).read_bytes()).hexdigest() == saved['savedResult']['sha256'] for path in paths)
            assert Path(paths[0]).stat().st_file_attributes & 1 and Path(paths[2]).stat().st_file_attributes & 1
            assert not (root / 'operations' / prepared['reopenId']).exists()
            assert panel.calls == ['inspect_active_model', 'list_documents', 'verify_owned_save_as', 'prepare_owned_saved_reopen']
        assert not panel.errors, panel.errors
        if args.recovery_resolution:
            proof = saved['outcomeResolution']
            record = root / 'recoveryresolutions' / arguments['saveRequestId'] / (proof['verificationRequestId'] + '.json')
            assert hashlib.sha256(record.read_bytes()).hexdigest() == proof['resolutionSha256']
            assert all(proof[key] is False for key in ('executionAuthorized', 'retryAllowed', 'checkpointRestoreAuthorized'))
            history = readonly.call(90, 'tools/call', {'name': 'inspect_operation_recovery', 'arguments': {'operationId': arguments['saveRequestId']}})['result']
            assert not history['isError'], history
            recorded = history['structuredContent']['resolutionHistory']
            assert recorded['state'] == 'resolution_history_read' and any(r['verificationRequestId'] == proof['verificationRequestId'] for r in recorded['records']), recorded
        assert all(hashlib.sha256(path.read_bytes()).hexdigest() == digest for path, digest in before.items())
        evidence = Path(args.evidence).resolve()
        evidence.mkdir(parents=True, exist_ok=True)
        (evidence / ('sdk-save-resolution-stdio.json' if args.recovery_resolution else 'sdk-saved-reopen-stdio.json' if args.reopen else 'sdk-saved-result-stdio.json')).write_text(json.dumps({'state': 'passed', 'scope': 'Official SDK STDIO, fake read/verifier replies and actual filesystem-only Framework preparation in an isolated fixture; no live CAD' if args.reopen else 'Official SDK STDIO and fake verifier on actual Framework saved-result artifacts; no live CAD',
            'fullCatalog': len(tools), 'readOnlyCatalog': 6 + int(args.recovery_resolution), 'cadConnectionAttempted': False, 'runtimeDeploymentPerformed': False,
            'nativeFixtureRoot': str(root), 'nativeRecipes': panel.calls, 'journalUnchanged': True, 'savedResult': saved, 'preparedReopen': prepared, 'transcript': client.transcript}, indent=2), encoding='utf-8')
    finally:
        client.close()
        readonly.close()
        panel.stop.set()
        panel.thread.join(timeout=3)
    print('PASS: SDK saved-result STDIO, %d/%d catalogs, exact schemas, fresh proofs, one-use choice and unchanged journal%s%s; no CAD.' % (len(tools), 6 + int(args.recovery_resolution), ', actual native filesystem-only reopen preparation' if args.reopen else '', ', immutable resolution/read-only history checked' if args.recovery_resolution else ''))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('dotnet', 'host', 'stage', 'evidence'):
        parser.add_argument('--' + name, required=True)
    parser.add_argument('--reopen', action='store_true')
    parser.add_argument('--recovery-resolution', action='store_true')
    checks(parser.parse_args())
