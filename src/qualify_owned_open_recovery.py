"""Explicit verification of an already returned open; never issues an open command.

Read receipts and the original journal establish the result before selecting the
original document again. No save, close, selection, capture or geometry calls.
"""
import argparse
import hashlib
import json
from pathlib import Path

from qualify_core_mcp import PassiveClient, pending
from qualify_owned_open import observed


def window_identity(window):
    return {k: v for k, v in window.items() if k not in {'selectedRaw', 'nativeWindowId'}}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--bridge-root', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--open-request-id', required=True)
    parser.add_argument('--open-receipt-sha256', required=True)
    parser.add_argument('--baseline-receipt', required=True, type=Path)
    options = parser.parse_args()
    root, output = options.bridge_root.resolve(), options.output.resolve()
    original_raw = (root / 'responses' / (options.open_request_id + '.json')).read_bytes()
    if hashlib.sha256(original_raw).hexdigest() != options.open_receipt_sha256 or pending(root):
        raise RuntimeError('Original receipt SHA and idle queue required')
    original = json.loads(original_raw.decode('utf-8-sig'))
    baseline = json.loads(options.baseline_receipt.read_text(encoding='utf-8-sig'))
    assert original['contextChangeAttempted'] is True and original['geometryMutationAttempted'] is False
    assert original['cadSaveAttempted'] is False
    target = original['before']['activeWindow']
    assert target['modelId'] == baseline['model']['id'] and target['modelPath'] == baseline['model']['path']
    output.mkdir(parents=True, exist_ok=True)
    clients, records = [], []
    summary = {'state': 'incomplete', 'records': records, 'openOperationId': options.open_request_id,
               'copyId': original['copyId'], 'openReceiptSha256': options.open_receipt_sha256,
               'openCommandReplayAttempted': False, 'geometryMutationAttempted': False,
               'cadSaveAttempted': False, 'cadCloseAttempted': False}

    def call(client, name, args=None, expected=None, rpc_error=False):
        reply = client.rpc('tools/call', {'name': name, 'arguments': args or {}})
        data = reply.get('result', {}).get('structuredContent', reply.get('error'))
        path = output / ('%02d-%s.json' % (len(records) + 1, name))
        path.write_text(json.dumps(reply, ensure_ascii=False, indent=2), encoding='utf-8')
        records.append({'tool': name, 'state': data.get('state'), 'path': str(path)})
        if (rpc_error and data.get('code') != -32602) or (not rpc_error and data.get('state') != expected):
            summary.update(failedTool=name, failedBridgeRequestId=data.get('bridgeRequestId'))
            raise RuntimeError('Unexpected ' + name + ': ' + json.dumps(data, ensure_ascii=False))
        return data

    try:
        client = PassiveClient(root / 'mcp_server.py', root); clients.append(client)
        readonly = PassiveClient(root / 'mcp_server.py', root, read_only=True); clients.append(readonly)
        foreign = PassiveClient(root / 'mcp_server.py', root); clients.append(foreign)
        catalog = client.rpc('tools/list')['result']['tools']
        ro_catalog = readonly.rpc('tools/list')['result']['tools']
        assert len(catalog) == 17 and len(ro_catalog) == 6
        summary.update(toolNames=[t['name'] for t in catalog], readOnlyToolNames=[t['name'] for t in ro_catalog])
        before = call(client, 'list_documents', expected='documents_listed_verified')
        own = call(client, 'inspect_entity_candidates', expected='entity_candidates_inspected')
        assert Path(own['model']['path']) == Path(original['workingPath'])
        active = next(d for d in before['documents'] if d['isActive'])
        proof = {'openRequestId': options.open_request_id, 'openReceiptSha256': options.open_receipt_sha256,
                 'candidateDocumentHandle': own['candidateDocumentHandle'], 'activeDocumentHandle': active['documentHandle']}
        call(readonly, 'verify_working_copy_open', proof, rpc_error=True)
        call(foreign, 'verify_working_copy_open', proof, expected='owned_open_verification_rejected')
        rejected = call(client, 'verify_working_copy_open', {**proof, 'openReceiptSha256': '0' * 64}, expected='owned_open_verification_rejected')
        assert rejected['contextChangeAttempted'] is False
        choices = call(client, 'list_documents', expected='documents_listed_verified')
        proof['activeDocumentHandle'] = next(d for d in choices['documents'] if d['isActive'])['documentHandle']
        verified = call(client, 'verify_working_copy_open', proof, expected='owned_open_verified_observed')
        assert verified['recordedJournalState'] == 'succeeded_verified' and verified['contextChangeAttempted'] is False
        assert verified['copyId'] == original['copyId'] and verified['savedFilesUnchanged'] is True
        journal = call(readonly, 'operation_result', {'operationId': options.open_request_id}, expected='operation_receipt_read')
        assert journal['receipt']['state'] == 'succeeded_verified' and journal['receipt']['retryAllowed'] is False
        binding_args = {'candidateDocumentHandle': own['candidateDocumentHandle'], 'workingCopyHandle': verified['workingCopyHandle']}
        call(foreign, 'bind_working_document', binding_args, expected='working_document_binding_rejected')
        bound = call(client, 'bind_working_document', binding_args, expected='working_document_bound_observed')
        call(client, 'validate_working_document_binding', {'workingDocumentBindingHandle': bound['workingDocumentBindingHandle']}, expected='working_document_binding_validated_observed')
        supported = [e for e in own['entities'] if e['entitySetSupported']]
        assert supported
        entity_set = call(client, 'prepare_entity_set', {'candidateDocumentHandle': own['candidateDocumentHandle'], 'candidateEntityHandles': [e['candidateEntityHandle'] for e in supported]}, expected='entity_set_prepared_observed')
        call(client, 'validate_entity_set', {'entitySetHandle': entity_set['entitySetHandle']}, expected='entity_set_validated_observed')
        call(foreign, 'validate_entity_set', {'entitySetHandle': entity_set['entitySetHandle']}, expected='entity_set_rejected')
        plan = call(client, 'prepare_operation_plan', {'operation': 'translate_entities', 'entitySetHandles': [entity_set['entitySetHandle']], 'parameters': {'vector': [1, 0, 0], 'coordinateFrame': 'world', 'lengthUnits': 'document_raw', 'mode': 'move'}}, expected='operation_plan_prepared_local')
        assert plan['plan']['executionEnabled'] is False
        call(client, 'inspect_operation_plan', {'operationPlanHandle': plan['operationPlanHandle']}, expected='operation_plan_inspected_local')
        call(readonly, 'operation_result', {'operationId': plan['operationId']}, expected='operation_receipt_read')
        copy = call(client, 'inspect_working_copy', {'workingCopyHandle': verified['workingCopyHandle']}, expected='working_copy_inspected_disk')
        independent = call(readonly, 'inspect_active_model', expected='active_model_inspected')
        assert observed(independent) == observed(own)
        choices = call(client, 'list_documents', expected='documents_listed_verified')
        restore = next(d for d in choices['documents'] if d['modelId'] == target['modelId'] and d['path'] == target['modelPath'])
        call(client, 'activate_document', {'documentHandle': restore['documentHandle']}, expected='document_active_verified')
        restored = call(readonly, 'inspect_active_model', expected='active_model_inspected')
        assert observed(restored) == observed(baseline)
        final = call(client, 'list_documents', expected='documents_listed_verified')
        assert window_identity(final['activeWindow']) == window_identity(target) and final['windowCount'] == original['before']['windowCount'] + 1
        for old in original['before']['windows']:
            assert sum(window_identity(old) == window_identity(w) for w in final['windows']) == 1
        call(client, 'validate_working_document_binding', {'workingDocumentBindingHandle': bound['workingDocumentBindingHandle']}, expected='working_document_binding_rejected')
        summary.update(state='passed_owned_open_verification_mcp', originalActiveDocumentRestored=True,
                       existingDocumentIdentitiesPreserved=True, oneOwnedCopyWindowRetained=True,
                       successfulLiveDocumentBindingQualified=True, entityTypesTested=[e['type'] for e in supported],
                       independentReadCompared=True, openJournalState='succeeded_verified', workingCopyInspection=copy,
                       nativeWindowIdsRenumbered=verified['nativeWindowIdsRenumbered'],
                       limitation='One saved SOLID model. Local geometry plan remains disabled and separate from ownership. No full topology, save/close, geometry execution or general recovery qualification.')
    except Exception as error:
        summary.update(state='failed_requires_inspection', error=str(error))
        raise
    finally:
        for client in clients: client.close()
        (output / 'qualification.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
    print('PASS: original returned open independently verified without replay; owned binding, entity set/local plan and independent read passed; original active model restored. No save/close/geometry.')


if __name__ == '__main__': main()
