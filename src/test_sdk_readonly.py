"""Independent C#/Python readonly routing and lease parity on an owned fake panel."""
import argparse
import copy
import datetime
import hashlib
import json
from pathlib import Path
import threading
import time
import uuid

from mcp_server import Server
from test_sdk_stdio import Client
from test_entity_references import snapshot


class FakePanel:
    def __init__(self, root):
        self.root = root
        self.model = snapshot()
        self.listing = {'state': 'documents_listed_verified', 'documents': [
            {'modelId': 9, 'windowNumber': 4, 'activationSupported': True},
            {'modelId': 10, 'activationSupported': False}]}
        self.calls = []
        self.receipts = {}
        self.last = None
        self.errors = []
        self.stop = threading.Event()
        self.thread = threading.Thread(target=self.run, daemon=True)
        self.thread.start()

    def run(self):
        while not self.stop.is_set():
            for path in (self.root / 'requests').glob('*.json'):
                claimed = path.with_name(path.name + '.processing')
                try:
                    path.rename(claimed)
                except (PermissionError, FileNotFoundError):
                    continue  # Only file claim is retried, never a CAD command.
                try:
                    request = json.loads(claimed.read_text(encoding='utf-8'))
                    assert set(request) == {'requestId', 'operation', 'arguments'}
                    assert request['requestId'] == path.stem and request['operation'] == 'recipe.prepare'
                    assert request['arguments']['parameters'] == {}
                    recipe = request['arguments']['recipeId']
                    assert recipe in {'list_documents', 'inspect_active_model'}
                    result = copy.deepcopy(self.listing if recipe == 'list_documents' else self.model)
                    result.pop('bridgeRequestId', None)
                    result['timestampUtc'] = datetime.datetime.now(datetime.timezone.utc).isoformat()
                    raw = (json.dumps(result, ensure_ascii=False, indent=2) + '\r\n').encode('utf-8')
                    # Alternate BOM confirms hashes include original wire bytes.
                    if len(self.calls) % 2:
                        raw = b'\xef\xbb\xbf' + raw
                    folder = self.root / 'responses'
                    folder.mkdir(exist_ok=True)
                    temporary = folder / (path.stem + '.tmp')
                    temporary.write_bytes(raw)
                    temporary.rename(folder / path.name)
                    result['bridgeRequestId'] = path.stem
                    self.last = result
                    self.receipts[path.stem] = raw
                    self.calls.append(request)
                except Exception as error:
                    self.errors.append(repr(error))
            self.stop.wait(.01)

    def close(self):
        self.stop.set()
        self.thread.join(timeout=5)
        assert not self.thread.is_alive()
        assert not self.errors, self.errors


def init(client):
    hello = client.call(1, 'initialize', {'protocolVersion': '2025-11-25', 'capabilities': {},
        'clientInfo': {'name': 'independent-readonly-parity', 'version': '1'}})
    assert hello['result']['protocolVersion'] == '2025-11-25'
    client.notify('notifications/initialized')


def compare(expected, actual, mapping, key=None):
    if key in {'documentReference', 'entityReference', 'documentHandle', 'candidateDocumentHandle', 'candidateEntityHandle'}:
        if expected is None:
            assert actual is None
        elif expected in mapping:
            assert mapping[expected] == actual
        else:
            assert isinstance(actual, str) and len(actual) == 32
            mapping[expected] = actual
        return
    if key in {'candidateLifetimeSeconds', 'documentHandleLifetimeSeconds'}:
        assert abs(expected - actual) <= 1, (key, expected, actual)
        return
    if isinstance(expected, dict):
        assert set(expected) == set(actual), (set(expected) - set(actual), set(actual) - set(expected))
        for field in expected:
            compare(expected[field], actual[field], mapping, field)
    elif isinstance(expected, list):
        assert len(expected) == len(actual)
        for left, right in zip(expected, actual):
            compare(left, right, mapping, key)
    else:
        assert expected == actual, (key, expected, actual)


def checks(args):
    evidence = Path(args.evidence).resolve()
    root = evidence / ('readonly-fixture-' + uuid.uuid4().hex)
    root.mkdir(parents=True)
    settings = {'coreProfile': 'general-v1', 'entitySetsExperimental': True,
                'expectedVersion': '23.2.2', 'timeoutSeconds': 20,
                'operationTimeoutSeconds': 80, 'maxEntities': 10000, 'captureViewport': False}
    (root / 'bridge.config.json').write_text(json.dumps(settings), encoding='utf-8')
    (root / 'PSBridge.Worker.exe').write_text('Opaque fake marker; never launched.', encoding='utf-8')
    panel = FakePanel(root)
    legacy = Server(root, read_only=True)
    legacy_results = []
    legacy.reply = lambda request_id, result=None, error=None: legacy_results.append((result, error))
    legacy.snapshot = lambda *unused: copy.deepcopy(panel.last)
    command = [args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root), '--read-only', '--timeout', '2']
    client = Client(command)
    other = None
    mapping = {}
    sequence = 10
    def both(name, sdk_args=None, python_args=None, expect_read=True):
        nonlocal sequence
        before = len(panel.calls)
        actual = client.call(sequence, 'tools/call', {'name': name, 'arguments': sdk_args or {}})['result']
        legacy.call(sequence, {'name': name, 'arguments': python_args if python_args is not None else sdk_args or {}}, threading.Event())
        expected, error = legacy_results[-1]
        assert error is None, error
        assert actual['isError'] == expected['isError'], (name, actual, expected)
        compare(expected['structuredContent'], actual['structuredContent'], mapping)
        assert len(panel.calls) == before + (1 if expect_read else 0), (name, panel.calls[before:])
        sequence += 1
        return actual['structuredContent'], expected['structuredContent']
    try:
        init(client)
        tools = client.call(2, 'tools/list')['result']['tools']
        assert sorted(t['name'] for t in tools) == sorted(t['name'] for t in legacy.tools)
        assert len(tools) == 6
        for tool in tools:
            schema = copy.deepcopy(tool['inputSchema'])
            for value in schema.get('properties', {}).values():
                value.pop('description', None)
            assert schema == next(t['inputSchema'] for t in legacy.tools if t['name'] == tool['name'])
            assert tool['annotations']['readOnlyHint'] is True
        for name in ['activate_document', 'prepare_entity_set', 'validate_entity_set', 'prepare_operation_plan',
                     'inspect_operation_plan', 'prepare_working_copy', 'inspect_working_copy', 'bind_working_document',
                     'validate_working_document_binding', 'prepare_owned_operation', 'validate_owned_operation',
                     'open_working_copy', 'verify_working_copy_open', 'execute_owned_translation', 'verify_owned_translation',
                     'model_snapshot', 'screenshot', 'execute_command']:
            result = client.call(sequence, 'tools/call', {'name': name, 'arguments': {'command': 'DELETE'}})
            assert result['error']['code'] == -32602, result
            sequence += 1
        assert not panel.calls and not (root / 'requests').exists()
        listed, old_listed = both('list_documents')
        native = panel.last
        raw = panel.receipts[native['bridgeRequestId']]
        old_handle = old_listed['documents'][0]['documentHandle']
        assert legacy.document_handles.entries[old_handle]['parameters']['listingSha256'] == hashlib.sha256(raw).hexdigest()
        issued, old_issued = both('inspect_active_model')
        actual_refs = {'documentReference': issued['documentReference'], 'entityReferences': [issued['entities'][0]['entityReference']]}
        old_refs = {'documentReference': old_issued['documentReference'], 'entityReferences': [old_issued['entities'][0]['entityReference']]}
        both('resolve_entity_references', actual_refs, old_refs)
        candidates, old_candidates = both('inspect_entity_candidates')
        candidate_id = old_candidates['candidateDocumentHandle']
        candidate = legacy.entity_sets.document_parameters(candidate_id)
        assert candidate['inspectionSha256'] == hashlib.sha256(panel.receipts[panel.last['bridgeRequestId']]).hexdigest()
        candidate_as_read = {'documentReference': candidates['candidateDocumentHandle'], 'entityReferences': [candidates['entities'][0]['candidateEntityHandle']]}
        old_candidate_as_read = {'documentReference': old_candidates['candidateDocumentHandle'], 'entityReferences': [old_candidates['entities'][0]['candidateEntityHandle']]}
        both('resolve_entity_references', candidate_as_read, old_candidate_as_read, expect_read=False)
        other = Client(command)
        init(other)
        before = len(panel.calls)
        cross = other.call(3, 'tools/call', {'name': 'resolve_entity_references', 'arguments': actual_refs})['result']
        assert cross['isError'] and cross['structuredContent']['error']['code'] == 'document_reference_unknown_or_expired'
        assert len(panel.calls) == before
        panel.model['entities'][0]['geometry']['volumeRaw'] = 2.0
        both('resolve_entity_references', actual_refs, old_refs)
        both('resolve_entity_references', actual_refs, old_refs, expect_read=False)
        panel.model = snapshot()
        both('inspect_active_model')
        both('resolve_entity_references', actual_refs, old_refs, expect_read=False)
        # Foreign native state after a failed fresh read never leaves old references live.
        panel.model['state'] = 'failed'
        both('inspect_active_model')
        assert all(request['arguments']['recipeId'] in {'list_documents', 'inspect_active_model'} for request in panel.calls)
        assert not (root / 'captures').exists()
    finally:
        if other is not None:
            other.close()
        client.close()
        panel.close()
        (evidence / 'readonly-stdio-transcript.json').write_text(json.dumps(client.transcript, ensure_ascii=False, indent=2), encoding='utf-8')
        (evidence / 'readonly-stdio-stderr.txt').write_text(''.join(client.logs), encoding='utf-8')
        (evidence / 'readonly-fake-panel.json').write_text(json.dumps({'requests': panel.calls, 'errors': panel.errors}, indent=2), encoding='utf-8')
    print('PASS: six readonly tools, exact schema constraints, fixed read recipes, Python lease/digest/raw-byte parity, cross-client/candidate separation, stale no-revival and mutation refusals. No CAD.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--dotnet', required=True)
    parser.add_argument('--host', required=True)
    parser.add_argument('--evidence', required=True)
    checks(parser.parse_args())
