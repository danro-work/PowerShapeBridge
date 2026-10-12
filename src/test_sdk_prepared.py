"""Independent prepared SDK/Python byte/schema parity on a fake native panel; no CAD."""
import argparse
import copy
import datetime
import hashlib
import json
from pathlib import Path
import shutil
import threading
import uuid
from unittest.mock import patch

from mcp_server import Server
from operation_plans import OBSERVED_FIELDS, append_event, freeze, journal_events
from test_entity_references import snapshot
from test_sdk_stdio import Client
from test_sdk_readonly import init


class Panel:
    def __init__(self, root):
        self.root = root
        self.source = dict.fromkeys(OBSERVED_FIELDS)
        self.source.update(snapshot(), productVersion='23.2.2', modelScope='active_document_read_only',
            contextAfter=[0, 0], selectionBefore=[], selectionAfter=[], levelsContainingEntities=[])
        self.source.pop('bridgeRequestId', None)
        self.calls = []
        self.errors = []
        self.last = None
        self.stop = threading.Event()
        self.thread = threading.Thread(target=self.run, daemon=True)
        self.thread.start()

    def handle(self, identity, recipe, parameters):
        stamp = datetime.datetime.now(datetime.timezone.utc).isoformat()
        if recipe == 'inspect_active_model':
            value = copy.deepcopy(self.source)
            value['timestampUtc'] = stamp
            return value
        if recipe == 'list_documents':
            return dict(state='documents_listed_verified', timestampUtc=stamp,
                documents=[dict(modelId=9, windowNumber=4, activationSupported=True)])
        if recipe == 'activate_document':
            return dict(state='document_active_verified', contextChangeAttempted=True, geometryMutationAttempted=False)
        if recipe == 'open_working_copy':
            plan = dict(parameters, operationId=identity, operation='open_working_copy', geometryExecutionEnabled=False)
            sha = freeze(self.root / 'copyopenplans' / (identity + '.json'), plan)
            return dict(state='working_copy_opened_observed', operationId=identity, planSha256=sha, plan=plan,
                copyId=parameters['copyId'], ownershipSha256=parameters['ownershipSha256'],
                contextChangeAttempted=True, geometryMutationAttempted=False, cadSaveAttempted=False)
        if recipe == 'verify_working_copy_open':
            plan = json.loads((self.root / 'copyopenplans' / (parameters['openRequestId'] + '.json')).read_text())
            return dict(state='owned_open_verified_observed', openOperationId=parameters['openRequestId'],
                openReceiptSha256=parameters['openReceiptSha256'], expiresUtc=parameters['expiresUtc'],
                copyId=plan['copyId'], ownershipSha256=plan['ownershipSha256'], recordedJournalState='succeeded_verified',
                timestampUtc=stamp, contextChangeAttempted=False, geometryMutationAttempted=False)
        if recipe == 'execute_owned_translation':
            plan = dict(parameters, operationId=identity, operation='translate_owned_solid', executionEnabled=True)
            sha = freeze(self.root / 'ownedexecutions' / (identity + '.json'), plan)
            return dict(state='owned_translation_executed_unverified', operationId=identity, planSha256=sha, plan=plan,
                geometryMutationAttempted=True, contextChangeAttempted=False, cadSaveAttempted=False,
                returnCode=1, copyFenced=True, recordedJournalState='executed_unverified')
        if recipe == 'verify_owned_translation':
            return dict(state='owned_translation_verified_observed', executionOperationId=parameters['executionRequestId'],
                executionReceiptSha256=parameters['executionReceiptSha256'], recordedJournalState='succeeded_verified',
                copyFenceReleased=True, noCommandReplay=True, geometryMutationAttempted=False,
                contextChangeAttempted=False, cadSaveAttempted=False)
        if recipe == 'prepare_entity_set':
            sha = freeze(self.root / 'entitysets' / (identity + '.json'), parameters)
            return dict(state='entity_set_prepared_observed', entitySetId=identity, entitySetSha256=sha, expiresUtc=parameters['expiresUtc'])
        if recipe == 'validate_entity_set':
            return dict(state='entity_set_validated_observed', **parameters)
        if recipe == 'prepare_working_copy':
            return dict(state='working_copy_prepared_disk', copyId=identity, ownershipSha256='d' * 64, timestampUtc=stamp)
        if recipe == 'inspect_working_copy':
            return dict(state='working_copy_inspected_disk', **parameters)
        if recipe == 'bind_working_document':
            binding = dict(parameters, schemaVersion=1, bindingId=identity, bridgeVersion='0.31.0', timestampUtc=stamp,
                observedPropertiesSha256='e' * 64, windowContextSha256='f' * 64, executionEnabled=False)
            sha = freeze(self.root / 'documentbindings' / (identity + '.json'), binding)
            return dict(state='working_document_bound_observed', bindingId=identity, bindingSha256=sha, binding=binding)
        if recipe == 'validate_working_document_binding':
            binding = json.loads((self.root / 'documentbindings' / (parameters['bindingId'] + '.json')).read_text())
            return dict(state='working_document_binding_validated_observed', binding=binding, **parameters)
        if recipe == 'prepare_owned_operation':
            binding = json.loads((self.root / 'documentbindings' / (parameters['bindingId'] + '.json')).read_text())
            plan = json.loads((self.root / 'operationplans' / (parameters['operationPlanId'] + '.json')).read_text())
            packet = dict(parameters, schemaVersion=1, ownedOperationId=identity, preparedUtc=stamp,
                expiresUtc=min(plan['expiresUtc'], binding['expiresUtc']), executionEnabled=False,
                nativeObservedPropertiesSha256=binding['observedPropertiesSha256'], windowContextSha256=binding['windowContextSha256'],
                workingSha256='a' * 64, copyId=binding['copyId'], ownershipSha256=binding['ownershipSha256'])
            sha = freeze(self.root / 'ownedoperations' / (identity + '.json'), packet)
            append_event(self.root, identity, sha, 'prepared', {'executionEnabled': False}, 0)
            return dict(state='owned_operation_prepared_observed', ownedOperationId=identity, ownedOperationSha256=sha,
                ownedOperation=packet, expiresUtc=packet['expiresUtc'], executionEnabled=False,
                geometryMutationAttempted=False, contextChangeAttempted=False, cadSaveAttempted=False)
        if recipe == 'validate_owned_operation':
            packet = json.loads((self.root / 'ownedoperations' / (parameters['ownedOperationId'] + '.json')).read_text())
            return dict(state='owned_operation_validated_observed', ownedOperation=packet, expiresUtc=packet['expiresUtc'],
                executionEnabled=False, geometryMutationAttempted=False, contextChangeAttempted=False, cadSaveAttempted=False, **parameters)
        raise AssertionError('Unqualified fake recipe: ' + recipe)

    def run(self):
        while not self.stop.is_set():
            for path in (self.root / 'requests').glob('*.json'):
                claimed = path.with_name(path.name + '.processing')
                try:
                    path.rename(claimed)
                except (PermissionError, FileNotFoundError):
                    continue
                try:
                    request = json.loads(claimed.read_text())
                    assert set(request) == {'requestId', 'operation', 'arguments'} and request['operation'] == 'recipe.prepare'
                    result = self.handle(path.stem, request['arguments']['recipeId'], request['arguments']['parameters'])
                    raw = (json.dumps(result, ensure_ascii=False, indent=2) + '\r\n').encode('utf-8')
                    temporary = self.root / 'responses' / (path.stem + '.tmp')
                    temporary.write_bytes(raw)
                    temporary.rename(self.root / 'responses' / path.name)
                    result['bridgeRequestId'] = path.stem
                    self.last = result
                    self.calls.append(request)
                except Exception as error:
                    self.errors.append(repr(error))
            self.stop.wait(.01)

    def close(self):
        self.stop.set()
        self.thread.join(timeout=5)
        assert not self.thread.is_alive() and not self.errors, self.errors


def equivalent(expected, actual, mapping, key=None):
    handles = {'candidateDocumentHandle', 'candidateEntityHandle', 'entitySetHandle', 'operationPlanHandle',
               'workingCopyHandle', 'workingDocumentBindingHandle', 'ownedOperationHandle', 'documentHandle'}
    if key in handles:
        if expected not in mapping:
            assert len(actual) == 32
            mapping[expected] = actual
        assert mapping[expected] == actual
    elif key in {'candidateLifetimeSeconds', 'workingCopyLifetimeSeconds', 'bindingLifetimeSeconds', 'ownedOperationLifetimeSeconds', 'documentHandleLifetimeSeconds'}:
        assert abs(expected - actual) <= 1, (key, expected, actual)
    elif isinstance(expected, dict):
        assert set(expected) == set(actual), (set(expected) - set(actual), set(actual) - set(expected))
        for field in expected:
            equivalent(expected[field], actual[field], mapping, field)
    elif isinstance(expected, list):
        assert len(expected) == len(actual)
        for left, right in zip(expected, actual):
            equivalent(left, right, mapping, key)
    else:
        assert expected == actual, (key, expected, actual)


def checks(args):
    execution = getattr(args, 'execution', False)
    prefix = 'execution' if execution else 'prepared'
    evidence = Path(args.evidence).resolve()
    unique = uuid.uuid4().hex
    root = evidence / ('prepared-sdk-' + unique)
    old_root = evidence / ('prepared-python-' + unique)
    root.mkdir(parents=True)
    old_root.mkdir()
    profile = 'general-owned-translations.json' if execution else 'general-owned-operation-plans.json'
    settings = json.loads((Path(__file__).parents[1] / 'config' / profile).read_text())
    folders = ('responses', 'entitysets', 'documentbindings', 'ownedoperations', 'copyopenplans', 'ownedexecutions')
    for folder in folders:
        (root / folder).mkdir()
        (old_root / folder).mkdir()
    for directory in (root, old_root):
        (directory / 'bridge.config.json').write_text(json.dumps(settings), encoding='utf-8')
    (root / 'PSBridge.Worker.exe').write_text('Opaque marker, never executed.', encoding='utf-8')
    panel = Panel(root)
    legacy = Server(old_root)
    replies = []
    legacy.reply = lambda identity, result=None, error=None: replies.append((result, error))
    def old_native(cancel, operation, arguments):
        request = panel.calls[-1]
        assert operation == request['operation'] and arguments == request['arguments'], (arguments, request)
        return copy.deepcopy(panel.last)
    legacy.snapshot = old_native
    client = Client([args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root), '--timeout', '3'])
    mapping = {}
    sequence = 10
    def both(name, inputs, old_inputs=None, local=False):
        nonlocal sequence
        before = len(panel.calls)
        result = client.call(sequence, 'tools/call', {'name': name, 'arguments': inputs})['result']
        assert not result['isError'], result
        for folder in folders:
            for path in (root / folder).glob('*.json'):
                shutil.copyfile(path, old_root / folder / path.name)
        for directory in (root / 'operations').glob('*'):
            # Copy only native packet journals; local plan journals are generated independently.
            if (root / 'ownedoperations' / (directory.name + '.json')).exists():
                destination = old_root / 'operations' / directory.name
                destination.mkdir(parents=True, exist_ok=True)
                for path in directory.glob('*.json'):
                    shutil.copyfile(path, destination / path.name)
        actual = result['structuredContent']
        if name == 'prepare_operation_plan':
            forced = datetime.datetime.fromisoformat(actual['plan']['preparedUtc'])
            real_datetime = datetime.datetime
            class FixedDateTime(real_datetime):
                @classmethod
                def now(cls, tz=None):
                    return forced.astimezone(tz) if tz else forced.replace(tzinfo=None)
            with patch('operation_plans.uuid.uuid4', return_value=uuid.UUID(actual['operationId'])), patch('operation_plans.datetime.datetime', FixedDateTime):
                legacy.call(sequence, {'name': name, 'arguments': old_inputs or inputs}, threading.Event())
        else:
            legacy.call(sequence, {'name': name, 'arguments': old_inputs or inputs}, threading.Event())
        expected, error = replies[-1]
        assert error is None and not expected['isError'], (name, expected, error)
        equivalent(expected['structuredContent'], actual, mapping)
        assert len(panel.calls) == before + (0 if local else 1)
        if name == 'prepare_operation_plan':
            identity = actual['operationId']
            for relative in ('operationplans/' + identity + '.json', 'operations/' + identity + '/000000.json'):
                assert (root / relative).read_bytes() == (old_root / relative).read_bytes(), relative
            journal = journal_events(root, identity)
            assert journal['state'] == 'prepared' and journal['eventCount'] == 1 and not journal['retryAllowed']
        sequence += 1
        return actual, expected['structuredContent']
    try:
        init(client)
        tools = client.call(2, 'tools/list')['result']['tools']
        assert len(tools) == (21 if execution else 19)
        assert set(t['name'] for t in legacy.tools) == set(t['name'] for t in tools)
        for tool in tools:
            schema = copy.deepcopy(tool['inputSchema'])
            for value in schema.get('properties', {}).values():
                value.pop('description', None)
            assert schema == next(t['inputSchema'] for t in legacy.tools if t['name'] == tool['name'])
        candidate, old_candidate = both('inspect_entity_candidates', {})
        current_set, old_set = both('prepare_entity_set', {'candidateDocumentHandle': candidate['candidateDocumentHandle'], 'candidateEntityHandles': [candidate['entities'][0]['candidateEntityHandle']]},
            {'candidateDocumentHandle': old_candidate['candidateDocumentHandle'], 'candidateEntityHandles': [old_candidate['entities'][0]['candidateEntityHandle']]})
        both('validate_entity_set', {'entitySetHandle': current_set['entitySetHandle']}, {'entitySetHandle': old_set['entitySetHandle']})
        parameters = dict(vector=[1.0, 0, 0], coordinateFrame='world', lengthUnits='document_raw', mode='move')
        plan, old_plan = both('prepare_operation_plan', {'operation': 'translate_entities', 'entitySetHandles': [current_set['entitySetHandle']], 'parameters': parameters},
            {'operation': 'translate_entities', 'entitySetHandles': [old_set['entitySetHandle']], 'parameters': parameters}, local=True)
        both('inspect_operation_plan', {'operationPlanHandle': plan['operationPlanHandle']}, {'operationPlanHandle': old_plan['operationPlanHandle']}, local=True)
        working, old_working = both('prepare_working_copy', {'sourcePath': 'C:/owned/Source.psmodel', 'sourceSha256': 'b' * 64})
        both('inspect_working_copy', {'workingCopyHandle': working['workingCopyHandle']}, {'workingCopyHandle': old_working['workingCopyHandle']})
        binding, old_binding = both('bind_working_document', {'candidateDocumentHandle': candidate['candidateDocumentHandle'], 'workingCopyHandle': working['workingCopyHandle']},
            {'candidateDocumentHandle': old_candidate['candidateDocumentHandle'], 'workingCopyHandle': old_working['workingCopyHandle']})
        both('validate_working_document_binding', {'workingDocumentBindingHandle': binding['workingDocumentBindingHandle']}, {'workingDocumentBindingHandle': old_binding['workingDocumentBindingHandle']})
        packet, old_packet = both('prepare_owned_operation', {'operationPlanHandle': plan['operationPlanHandle'], 'workingDocumentBindingHandle': binding['workingDocumentBindingHandle']},
            {'operationPlanHandle': old_plan['operationPlanHandle'], 'workingDocumentBindingHandle': old_binding['workingDocumentBindingHandle']})
        path = root / 'operations' / packet['ownedOperationId'] / '000000.json'
        before = path.read_bytes()
        both('validate_owned_operation', {'ownedOperationHandle': packet['ownedOperationHandle']}, {'ownedOperationHandle': old_packet['ownedOperationHandle']})
        assert path.read_bytes() == before
        if execution:
            executed, _ = both('execute_owned_translation', {'ownedOperationHandle': packet['ownedOperationHandle']}, {'ownedOperationHandle': old_packet['ownedOperationHandle']})
            assert executed['recordedJournalState'] == 'executed_unverified' and executed['copyFenced']
            queued = len(panel.calls)
            rejected = client.call(sequence, 'tools/call', {'name': 'execute_owned_translation', 'arguments': {'ownedOperationHandle': packet['ownedOperationHandle']}})['result']
            sequence += 1
            assert rejected['isError'] and rejected['structuredContent']['state'] == 'owned_translation_rejected_before_change'
            assert len(panel.calls) == queued
            fresh, old_fresh = both('inspect_entity_candidates', {})
            listing, old_listing = both('list_documents', {})
            execution_sha = hashlib.sha256((root / 'responses' / (executed['operationId'] + '.json')).read_bytes()).hexdigest()
            both('verify_owned_translation', dict(executionRequestId=executed['operationId'], executionReceiptSha256=execution_sha,
                    candidateDocumentHandle=fresh['candidateDocumentHandle'], activeDocumentHandle=listing['documents'][0]['documentHandle']),
                dict(executionRequestId=executed['operationId'], executionReceiptSha256=execution_sha,
                    candidateDocumentHandle=old_fresh['candidateDocumentHandle'], activeDocumentHandle=old_listing['documents'][0]['documentHandle']))
            listing, old_listing = both('list_documents', {})
            opened, _ = both('open_working_copy', dict(workingCopyHandle=working['workingCopyHandle'], candidateDocumentHandle=fresh['candidateDocumentHandle'], activeDocumentHandle=listing['documents'][0]['documentHandle']),
                dict(workingCopyHandle=old_working['workingCopyHandle'], candidateDocumentHandle=old_fresh['candidateDocumentHandle'], activeDocumentHandle=old_listing['documents'][0]['documentHandle']))
            fresh, old_fresh = both('inspect_entity_candidates', {})
            listing, old_listing = both('list_documents', {})
            open_sha = hashlib.sha256((root / 'responses' / (opened['operationId'] + '.json')).read_bytes()).hexdigest()
            verified, _ = both('verify_working_copy_open', dict(openRequestId=opened['operationId'], openReceiptSha256=open_sha,
                    candidateDocumentHandle=fresh['candidateDocumentHandle'], activeDocumentHandle=listing['documents'][0]['documentHandle']),
                dict(openRequestId=opened['operationId'], openReceiptSha256=open_sha,
                    candidateDocumentHandle=old_fresh['candidateDocumentHandle'], activeDocumentHandle=old_listing['documents'][0]['documentHandle']))
            assert verified['recordedJournalState'] == 'succeeded_verified' and verified['workingCopyHandle']
            listing, old_listing = both('list_documents', {})
            both('activate_document', {'documentHandle': listing['documents'][0]['documentHandle']}, {'documentHandle': old_listing['documents'][0]['documentHandle']})
            assert sum(request['arguments']['recipeId'] == 'execute_owned_translation' for request in panel.calls) == 1
            assert sum(request['arguments']['recipeId'] == 'open_working_copy' for request in panel.calls) == 1
        else:
            assert all(request['arguments']['recipeId'] not in {'activate_document', 'execute_owned_translation', 'open_working_copy'} for request in panel.calls)
        assert not (root / 'captures').exists()
    finally:
        client.close()
        panel.close()
        (evidence / (prefix + '-stdio-transcript.json')).write_text(json.dumps(client.transcript, ensure_ascii=False, indent=2), encoding='utf-8')
        (evidence / (prefix + '-stdio-stderr.txt')).write_text(''.join(client.logs), encoding='utf-8')
        (evidence / (prefix + '-fake-panel.json')).write_text(json.dumps({'requests': panel.calls, 'errors': panel.errors}, indent=2), encoding='utf-8')
    print('PASS: SDK/Python ' + prefix + ' facade parity, exact constraints/private envelopes, local plan/journal bytes and native raw hashes. Fake panel only; no CAD.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--dotnet', required=True)
    parser.add_argument('--host', required=True)
    parser.add_argument('--evidence', required=True)
    checks(parser.parse_args())
