"""Explicit translation experiment on the already open, manifest-owned copy.

Never touches baseline/source/checkpoint geometry. Two separate reviewed vectors:
forward, independent verification, fresh plan/binding, inverse, verification.
Any unknown stops immediately without inverse, save, close, replay or restoration.
"""
import argparse
import hashlib
import json
from pathlib import Path

from qualify_core_mcp import PassiveClient, pending
from qualify_owned_open import observed
from qualify_owned_open_recovery import window_identity


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--bridge-root', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--open-request-id', required=True)
    parser.add_argument('--open-receipt-sha256', required=True)
    parser.add_argument('--owned-baseline-receipt', required=True, type=Path)
    parser.add_argument('--allow-owned-translation', action='store_true')
    options = parser.parse_args()
    root, output = options.bridge_root.resolve(), options.output.resolve()
    if not options.allow_owned_translation or pending(root): raise RuntimeError('Explicit owned experiment permission and idle queue required')
    original_path = root / 'responses' / (options.open_request_id + '.json')
    assert hashlib.sha256(original_path.read_bytes()).hexdigest() == options.open_receipt_sha256
    original = json.loads(original_path.read_text(encoding='utf-8-sig'))
    baseline_reply = json.loads(options.owned_baseline_receipt.read_text(encoding='utf-8-sig'))
    own_baseline = observed(baseline_reply['result']['structuredContent'])
    fence = root / 'workingcopies' / original['copyId'] / 'mutation-fence.json'
    assert not fence.exists()
    events = sorted((root / 'operations' / options.open_request_id).glob('*.json'))
    assert len(events) == 6 and json.loads(events[-1].read_text())['state'] == 'succeeded_verified'
    original_journal = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in events}
    output.mkdir(parents=True, exist_ok=True)
    clients, records = [], []
    summary = {'state': 'incomplete', 'records': records, 'copyId': original['copyId'],
               'openOperationId': options.open_request_id, 'openReceiptSha256': options.open_receipt_sha256,
               'originalOpenJournalHashes': original_journal, 'openCommandReplayAttempted': False,
               'cadSaveAttempted': False, 'cadCloseAttempted': False, 'geometryMutationAttempted': False,
               'plannedVectorsWorldRaw': [[1, 0, 0], [-1, 0, 0]], 'executions': []}

    def call(client, name, args=None, expected=None, rpc_error=False):
        reply = client.rpc('tools/call', {'name': name, 'arguments': args or {}})
        data = reply.get('result', {}).get('structuredContent', reply.get('error'))
        path = output / ('%02d-%s.json' % (len(records) + 1, name))
        path.write_text(json.dumps(reply, ensure_ascii=False, indent=2), encoding='utf-8')
        record = {'tool': name, 'state': data.get('state'), 'path': str(path)}
        request = data.get('bridgeRequestId')
        if request:
            record['nativeReceiptSha256'] = hashlib.sha256((root / 'responses' / (request + '.json')).read_bytes()).hexdigest()
        records.append(record)
        if name == 'execute_owned_translation' and data.get('geometryMutationAttempted') is True: summary['geometryMutationAttempted'] = True
        if (rpc_error and data.get('code') != -32602) or (not rpc_error and data.get('state') != expected):
            summary.update(failedTool=name, failedBridgeRequestId=request)
            raise RuntimeError('Unexpected ' + name + ': ' + json.dumps(data, ensure_ascii=False))
        return data, record

    def read_own():
        own, _ = call(client, 'inspect_entity_candidates', expected='entity_candidates_inspected')
        assert Path(own['model']['path']) == Path(original['workingPath'])
        choices, _ = call(client, 'list_documents', expected='documents_listed_verified')
        active = next(d for d in choices['documents'] if d['isActive'])
        assert active['path'] == own['model']['path']
        return own, active

    try:
        client = PassiveClient(root / 'mcp_server.py', root); clients.append(client)
        readonly = PassiveClient(root / 'mcp_server.py', root, read_only=True); clients.append(readonly)
        foreign = PassiveClient(root / 'mcp_server.py', root); clients.append(foreign)
        catalog = client.rpc('tools/list')['result']['tools']; ro_catalog = readonly.rpc('tools/list')['result']['tools']
        assert len(catalog) == 21 and len(ro_catalog) == 6
        summary.update(toolNames=[t['name'] for t in catalog], readOnlyToolNames=[t['name'] for t in ro_catalog])
        status, _ = call(client, 'bridge_status')
        assert status['adapterVersion'] == '0.31.0' and status['lastPanelReport']['version'] == '0.31.0'
        before, _ = call(readonly, 'inspect_active_model', expected='active_model_inspected')
        choices, _ = call(client, 'list_documents', expected='documents_listed_verified')
        target = choices['activeWindow']; original_windows = choices['windows']
        assert window_identity(target) == window_identity(original['before']['activeWindow'])
        own_choice = next(d for d in choices['documents'] if Path(d['path']) == Path(original['workingPath']))
        assert not own_choice['isActive'] and own_choice['activationSupported']
        call(client, 'activate_document', {'documentHandle': own_choice['documentHandle']}, expected='document_active_verified')
        own, active = read_own()
        assert observed(own) == own_baseline, 'Own copy observations changed since accepted baseline; no mutation will be submitted'
        verified, _ = call(client, 'verify_working_copy_open', {'openRequestId': options.open_request_id,
            'openReceiptSha256': options.open_receipt_sha256, 'candidateDocumentHandle': own['candidateDocumentHandle'],
            'activeDocumentHandle': active['documentHandle']}, expected='owned_open_verified_observed')
        working_handle = verified['workingCopyHandle']
        first_observation = observed(own)
        for index, vector in enumerate(summary['plannedVectorsWorldRaw']):
            if index: own, active = read_own()
            bound, _ = call(client, 'bind_working_document', {'candidateDocumentHandle': own['candidateDocumentHandle'],
                'workingCopyHandle': working_handle}, expected='working_document_bound_observed')
            body = [e for e in own['entities'] if e['nativeTypeCode'] == 40 and e['entitySetSupported']]
            assert len(body) == 1
            entity_set, _ = call(client, 'prepare_entity_set', {'candidateDocumentHandle': own['candidateDocumentHandle'],
                'candidateEntityHandles': [body[0]['candidateEntityHandle']]}, expected='entity_set_prepared_observed')
            plan, _ = call(client, 'prepare_operation_plan', {'operation': 'translate_entities', 'entitySetHandles': [entity_set['entitySetHandle']],
                'parameters': {'vector': vector, 'coordinateFrame': 'world', 'lengthUnits': 'document_raw', 'mode': 'move'}}, expected='operation_plan_prepared_local')
            packet, _ = call(client, 'prepare_owned_operation', {'operationPlanHandle': plan['operationPlanHandle'],
                'workingDocumentBindingHandle': bound['workingDocumentBindingHandle']}, expected='owned_operation_prepared_observed')
            args = {'ownedOperationHandle': packet['ownedOperationHandle']}
            if index == 0:
                call(readonly, 'execute_owned_translation', args, rpc_error=True)
                call(foreign, 'execute_owned_translation', args, expected='owned_translation_rejected_before_change')
                call(client, 'execute_owned_translation', {**args, 'command': 'SAVE'}, rpc_error=True)
            execution, record = call(client, 'execute_owned_translation', args, expected='owned_translation_executed_unverified')
            assert execution['geometryMutationAttempted'] is True and execution['cadSaveAttempted'] is False and execution['copyFenced'] is True
            assert execution['guardPasses'] == 2 and execution['sameContextObservationReads'] == 6 and execution['commandCalls'] == 1
            assert fence.exists()
            summary['executions'].append({'operationId': execution['operationId'], 'planSha256': execution['planSha256'],
                'nativeReceiptSha256': record['nativeReceiptSha256'], 'vectorWorldRaw': vector})
            call(client, 'execute_owned_translation', args, expected='owned_translation_rejected_before_change')
            journal, _ = call(readonly, 'operation_result', {'operationId': execution['operationId']}, expected='operation_receipt_read')
            assert journal['receipt']['recordedState'] == 'executed_unverified' and journal['receipt']['retryAllowed'] is False
            fresh, active = read_own()
            check = {'executionRequestId': execution['operationId'], 'executionReceiptSha256': record['nativeReceiptSha256'],
                     'candidateDocumentHandle': fresh['candidateDocumentHandle'], 'activeDocumentHandle': active['documentHandle']}
            if index == 0: call(readonly, 'verify_owned_translation', check, rpc_error=True)
            result, _ = call(client, 'verify_owned_translation', check, expected='owned_translation_verified_observed')
            assert result['copyFenceReleased'] is True and result['noCommandReplay'] is True and not fence.exists()
            journal, _ = call(readonly, 'operation_result', {'operationId': execution['operationId']}, expected='operation_receipt_read')
            assert journal['receipt']['recordedState'] == 'succeeded_verified' and journal['receipt']['eventCount'] == 6
            independent, _ = call(readonly, 'inspect_active_model', expected='active_model_inspected')
            assert observed(independent) == observed(fresh)
            # An inverse is a separate freshly guarded operation, permitted only after known independent success.
            summary['executions'][-1]['independentlyVerified'] = True
        final_own, _ = call(readonly, 'inspect_active_model', expected='active_model_inspected')
        assert observed(final_own) == first_observation
        copy, _ = call(client, 'inspect_working_copy', {'workingCopyHandle': working_handle}, expected='working_copy_inspected_disk')
        assert copy['workingFileChanged'] is False and copy['checkpointVerified'] is True
        choices, _ = call(client, 'list_documents', expected='documents_listed_verified')
        restore = next(d for d in choices['documents'] if d['modelId'] == target['modelId'] and d['path'] == target['modelPath'])
        call(client, 'activate_document', {'documentHandle': restore['documentHandle']}, expected='document_active_verified')
        restored, _ = call(readonly, 'inspect_active_model', expected='active_model_inspected')
        assert observed(restored) == observed(before)
        final, _ = call(client, 'list_documents', expected='documents_listed_verified')
        assert window_identity(final['activeWindow']) == window_identity(target) and final['windowCount'] == len(original_windows)
        for old in original_windows: assert sum(window_identity(old) == window_identity(w) for w in final['windows']) == 1
        assert {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in events} == original_journal
        summary.update(state='passed_owned_translation_mcp', originalActiveDocumentRestored=True,
            originalOpenJournalUnchanged=True, existingDocumentIdentitiesPreserved=True,
            ownedCopyObservationsRestored=True, independentReadCompared=True, copyFenceReleased=True,
            limitation='One saved SOLID model, world/raw mm, one target at a time. Forward/inverse observed properties only; no full topology, save/reopen, general recovery or other entity/model qualification.')
    except Exception as error:
        summary.update(state='failed_requires_inspection', error=str(error)); raise
    finally:
        for client in clients: client.close()
        (output / 'qualification.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
    print('PASS: two separate owned translations independently verified; no replay/save/close; own observations and original active document restored.')


if __name__ == '__main__': main()
