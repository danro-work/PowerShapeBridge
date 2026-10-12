"""Explicit passive qualification: native reads and local entity-set artifacts only.

No activation, selection, CAD file operations, capture or geometry tools are called.
direct-worker is a bounded staging harness; panel uses the existing single panel.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import queue
import subprocess
import sys
import threading
import time


class PassiveClient:
    def __init__(self, script, root, read_only=False):
        args = [sys.executable, str(script), '--bridge-root', str(root), '--timeout', '30']
        if read_only: args.append('--read-only')
        self.process = subprocess.Popen(args, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        self.lines = queue.Queue()
        self.sequence = 0
        threading.Thread(target=self.reader, daemon=True).start()
        self.rpc('initialize', {'protocolVersion': '2025-11-25', 'capabilities': {}, 'clientInfo': {'name': 'passive-core-qualification', 'version': '1'}})
        self.send({'jsonrpc': '2.0', 'method': 'notifications/initialized'})

    def reader(self):
        for line in self.process.stdout:
            self.lines.put(json.loads(line))

    def send(self, message):
        self.process.stdin.write(json.dumps(message).encode() + b'\n')
        self.process.stdin.flush()

    def rpc(self, method, params=None):
        self.sequence += 1
        self.send({'jsonrpc': '2.0', 'id': self.sequence, 'method': method, 'params': params or {}})
        result = self.lines.get(timeout=40)
        if result.get('id') != self.sequence: raise RuntimeError('Unexpected MCP response ID')
        return result

    def close(self):
        self.process.stdin.close()
        self.process.wait(timeout=5)
        if self.process.returncode: raise RuntimeError(self.process.stderr.read().decode())


def pending(root):
    return list((root / 'requests').glob('*.json')) + list((root / 'requests').glob('*.processing'))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--bridge-root', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--transport', choices=('panel', 'direct-worker'), default='panel')
    parser.add_argument('--copy-source', type=Path)
    parser.add_argument('--copy-source-sha256')
    parser.add_argument('--copy-busy-source', type=Path)
    parser.add_argument('--copy-busy-source-sha256')
    options = parser.parse_args()
    root, output = options.bridge_root.resolve(), options.output.resolve()
    settings = json.loads((root / 'bridge.config.json').read_text(encoding='utf-8-sig'))
    if bool(options.copy_source) != bool(options.copy_source_sha256) or bool(options.copy_busy_source) != bool(options.copy_busy_source_sha256):
        raise RuntimeError('Explicit copy paths require paired source hashes')
    if (options.copy_source or options.copy_busy_source) and settings.get('workingCopiesExperimental') is not True:
        raise RuntimeError('Working-copy experiment is disabled')
    if settings.get('coreProfile') != 'general-v1' or settings.get('entitySetsExperimental') is not True:
        raise RuntimeError('Experimental general profile required')
    if pending(root): raise RuntimeError('Bridge queue is not idle')
    output.mkdir(parents=True, exist_ok=True)
    stop, pump_errors = threading.Event(), []
    native_requests = []
    def pump():
        # Only this explicit harness launches workers, never a second panel.
        while not stop.wait(.05):
            for request in (root / 'requests').glob('*.json'):
                try:
                    envelope = json.loads(request.read_text())
                    arguments = envelope.get('arguments', {})
                    if envelope.get('operation') != 'recipe.prepare' or arguments.get('recipeId') not in {'list_documents', 'inspect_active_model', 'prepare_entity_set', 'validate_entity_set', 'prepare_working_copy', 'inspect_working_copy', 'bind_working_document', 'validate_working_document_binding'}:
                        raise RuntimeError('Non-passive request rejected by qualification harness')
                    runtime = Path(__file__).parents[1] / 'releases/P2.6'
                    if runtime != root and pending(runtime): raise RuntimeError('Runtime queue is not idle')
                    claim = Path(str(request) + '.processing')
                    request.rename(claim)
                    child = subprocess.run([str(root / 'PSBridge.Worker.exe'), '--mcp-operation', request.stem], capture_output=True, encoding='utf-8', timeout=25)
                    if child.returncode: raise RuntimeError('Native worker failed: ' + child.stderr)
                    json.loads(child.stdout)
                    responses = root / 'responses'
                    responses.mkdir(exist_ok=True)
                    temporary = responses / (request.stem + '.tmp')
                    temporary.write_text(child.stdout, encoding='utf-8')
                    temporary.rename(responses / (request.stem + '.json'))
                    claim.rename(Path(str(claim) + '.done'))
                    native_requests.append(request.stem)
                except Exception as error:
                    pump_errors.append(str(error))
                    stop.set()
                    return
    pump_thread = None
    if options.transport == 'direct-worker':
        (root / 'requests').mkdir(exist_ok=True)
        pump_thread = threading.Thread(target=pump, daemon=True)
        pump_thread.start()
    records = []
    clients = []
    def call(client, name, args=None, expected=None, error_code=None):
        response = client.rpc('tools/call', {'name': name, 'arguments': args or {}})
        data = response.get('result', {}).get('structuredContent', response.get('error'))
        path = output / ('%02d-%s.json' % (len(records) + 1, name))
        path.write_text(json.dumps(response, ensure_ascii=False, indent=2), encoding='utf-8')
        records.append({'tool': name, 'state': data.get('state') if isinstance(data, dict) else None, 'path': str(path)})
        if expected and (not isinstance(data, dict) or data.get('state') != expected):
            raise RuntimeError('Unexpected ' + name + ' result: ' + json.dumps(data, ensure_ascii=False))
        if error_code and (not isinstance(data, dict) or data.get('code') != error_code):
            raise RuntimeError('Expected tool rejection: ' + name)
        return data
    summary = {'transport': options.transport, 'root': str(root), 'cadMutationAttempted': False, 'records': records, 'state': 'incomplete'}
    try:
        client = PassiveClient(root / 'mcp_server.py', root)
        clients.append(client)
        catalog = client.rpc('tools/list')['result']['tools']
        plans_enabled = settings.get('operationPlansExperimental') is True
        copies_enabled = settings.get('workingCopiesExperimental') is True
        bindings_enabled = settings.get('documentBindingsExperimental') is True
        if len(catalog) != (11 if plans_enabled else 9) + (2 if copies_enabled else 0) + (2 if bindings_enabled else 0) + (2 if settings.get('workingCopyOpenExperimental') is True else 0) + (2 if settings.get('ownedOperationPlansExperimental') is True else 0) + (2 if settings.get('ownedTranslationsExperimental') is True else 0): raise RuntimeError('Unexpected general catalog')
        summary['toolNames'] = [t['name'] for t in catalog]
        before = call(client, 'list_documents', expected='documents_listed_verified')
        candidates = call(client, 'inspect_entity_candidates', expected='entity_candidates_inspected')
        supported = [e for e in candidates['entities'] if e['entitySetSupported']]
        if not supported: raise RuntimeError('Active model has no supported candidate entities')
        args = {'candidateDocumentHandle': candidates['candidateDocumentHandle'], 'candidateEntityHandles': [supported[0]['candidateEntityHandle']]}
        prepared = call(client, 'prepare_entity_set', args, expected='entity_set_prepared_observed')
        checked = call(client, 'validate_entity_set', {'entitySetHandle': prepared['entitySetHandle']}, expected='entity_set_validated_observed')
        if checked['model'] != candidates['model']: raise RuntimeError('Native document differs')
        operation_plan = None
        if plans_enabled:
            operation_plan = call(client, 'prepare_operation_plan', {
                'operation': 'translate_entities', 'entitySetHandles': [prepared['entitySetHandle']],
                'parameters': {'vector': [1, 0, 0], 'coordinateFrame': 'world', 'lengthUnits': 'document_raw', 'mode': 'move'}},
                expected='operation_plan_prepared_local')
            inspected = call(client, 'inspect_operation_plan', {'operationPlanHandle': operation_plan['operationPlanHandle']}, expected='operation_plan_inspected_local')
            if inspected['plan'] != operation_plan['plan'] or inspected['plan']['executionEnabled'] is not False:
                raise RuntimeError('Local plan differs or enabled execution')
            call(client, 'operation_result', {'operationId': operation_plan['operationId']}, expected='operation_receipt_read')
            call(client, 'execute_operation_plan', {'operationPlanHandle': operation_plan['operationPlanHandle']}, error_code=-32602)
        foreign = PassiveClient(root / 'mcp_server.py', root)
        clients.append(foreign)
        call(foreign, 'validate_entity_set', {'entitySetHandle': prepared['entitySetHandle']}, expected='entity_set_rejected')
        if plans_enabled:
            call(foreign, 'inspect_operation_plan', {'operationPlanHandle': operation_plan['operationPlanHandle']}, expected='operation_plan_rejected')
        working_copy = None
        if options.copy_source:
            working_copy = call(client, 'prepare_working_copy', {'sourcePath': str(options.copy_source.resolve()), 'sourceSha256': options.copy_source_sha256}, expected='working_copy_prepared_disk')
            call(client, 'inspect_working_copy', {'workingCopyHandle': working_copy['workingCopyHandle']}, expected='working_copy_inspected_disk')
            call(foreign, 'inspect_working_copy', {'workingCopyHandle': working_copy['workingCopyHandle']}, expected='working_copy_rejected')
            if bindings_enabled:
                bound = call(client, 'bind_working_document', {'candidateDocumentHandle': candidates['candidateDocumentHandle'], 'workingCopyHandle': working_copy['workingCopyHandle']}, expected='working_document_binding_rejected')
                if bound.get('errorCode') != 'active_document_not_owned_working_copy' or bound.get('sameContextObservationReads') != 2 or bound.get('executionEnabled') is not False:
                    raise RuntimeError('Expected passive refusal of unowned active model after two retained-context observations')
                call(foreign, 'bind_working_document', {'candidateDocumentHandle': candidates['candidateDocumentHandle'], 'workingCopyHandle': working_copy['workingCopyHandle']}, expected='working_document_binding_rejected')
                call(client, 'validate_working_document_binding', {'workingDocumentBindingHandle': '0' * 32}, expected='working_document_binding_rejected')
        if options.copy_busy_source:
            busy = call(client, 'prepare_working_copy', {'sourcePath': str(options.copy_busy_source.resolve()), 'sourceSha256': options.copy_busy_source_sha256}, expected='working_copy_rejected')
            if busy.get('errorCode') != 'file_busy': raise RuntimeError('Expected explicit busy-file refusal')
        call(client, 'prepare_entity_set', {**args, 'candidateEntityHandles': args['candidateEntityHandles'] * 2}, error_code=-32602)
        call(client, 'prepare_entity_set', {'documentReference': candidates['candidateDocumentHandle'], 'entityReferences': args['candidateEntityHandles']}, error_code=-32602)
        readonly = PassiveClient(root / 'mcp_server.py', root, read_only=True)
        clients.append(readonly)
        readonly_catalog = readonly.rpc('tools/list')['result']['tools']
        if len(readonly_catalog) != 6 or any(t['name'] in {'activate_document', 'prepare_entity_set', 'validate_entity_set', 'model_snapshot', 'prepare_operation_plan', 'inspect_operation_plan', 'prepare_working_copy', 'inspect_working_copy', 'bind_working_document', 'validate_working_document_binding'} for t in readonly_catalog):
            raise RuntimeError('Unexpected passive catalog')
        summary['readOnlyToolNames'] = [t['name'] for t in readonly_catalog]
        call(readonly, 'prepare_entity_set', args, error_code=-32602)
        if plans_enabled:
            call(readonly, 'prepare_operation_plan', {}, error_code=-32602)
            call(readonly, 'operation_result', {'operationId': operation_plan['operationId']}, expected='operation_receipt_read')
        if working_copy:
            call(readonly, 'prepare_working_copy', {}, error_code=-32602)
            call(readonly, 'inspect_working_copy', {'workingCopyHandle': working_copy['workingCopyHandle']}, error_code=-32602)
            if bindings_enabled:
                call(readonly, 'bind_working_document', {}, error_code=-32602)
                call(readonly, 'validate_working_document_binding', {'workingDocumentBindingHandle': '0' * 32}, error_code=-32602)
        call(readonly, 'inspect_active_model', expected='active_model_inspected')
        after = call(client, 'list_documents', expected='documents_listed_verified')
        for field in ('processId', 'processStartedUtc', 'windowsSessionId', 'windows', 'activeWindow'):
            if before[field] != after[field]: raise RuntimeError('Window context changed: ' + field)
        summary.update(state='passed_passive_core_mcp', model=candidates['model'], entityTypesTested=[supported[0]['type']],
                       nativeDocumentContextUnchanged=True, geometryMutationQualified=False,
                       localOperationPlanTested=plans_enabled,
                       savedWorkingCopyTested=bool(working_copy), busySourceRejectionTested=bool(options.copy_busy_source),
                       unownedActiveDocumentBindingRefusalTested=bool(working_copy) and bindings_enabled,
                       limitation='One active model/type; no changed-context/expiry live tests or mutation acceptance.')
    except Exception as error:
        summary.update(state='failed', error=str(error))
        raise
    finally:
        for client in clients: client.close()
        stop.set()
        if pump_thread: pump_thread.join(timeout=30)
        summary['pumpErrors'] = pump_errors
        summary['nativeRequests'] = native_requests
        if pump_errors: summary['state'] = 'failed'
        (output / 'qualification.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
    if pump_errors: raise RuntimeError('; '.join(pump_errors))
    print('PASS: passive MCP core, native entity-set prepare/validate, optional local plan/journal, isolated handles and passive catalog. No CAD mutation.')


if __name__ == '__main__': main()


