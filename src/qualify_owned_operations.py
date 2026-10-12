"""Explicit passive coupled guard qualification on an already open owned copy.

Only two documented activations are allowed. Never opens, saves, closes, selects,
captures or changes geometry. Unknown activation result stops without retry.
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
    options = parser.parse_args()
    root, output = options.bridge_root.resolve(), options.output.resolve()
    original_path = root / 'responses' / (options.open_request_id + '.json')
    original_raw = original_path.read_bytes()
    if hashlib.sha256(original_raw).hexdigest() != options.open_receipt_sha256 or pending(root):
        raise RuntimeError('Verified original returned receipt and idle queue required')
    original = json.loads(original_raw.decode('utf-8-sig'))
    events = sorted((root / 'operations' / options.open_request_id).glob('*.json'))
    assert len(events) == 6 and json.loads(events[-1].read_text())['state'] == 'succeeded_verified'
    open_journal_hashes = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in events}
    claim_path = root / 'workingcopies' / original['copyId'] / 'open-once.json'
    claim_sha = hashlib.sha256(claim_path.read_bytes()).hexdigest()
    output.mkdir(parents=True, exist_ok=True)
    clients, records = [], []
    summary = {'state': 'incomplete', 'records': records, 'openOperationId': options.open_request_id,
               'openReceiptSha256': options.open_receipt_sha256, 'copyId': original['copyId'],
               'originalOpenJournalHashes': open_journal_hashes, 'openClaimSha256': claim_sha,
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
        catalog = client.rpc('tools/list')['result']['tools']; ro_catalog = readonly.rpc('tools/list')['result']['tools']
        assert len(catalog) == 19 and len(ro_catalog) == 6
        summary.update(toolNames=[t['name'] for t in catalog], readOnlyToolNames=[t['name'] for t in ro_catalog])
        status = call(client, 'bridge_status')
        assert status['adapterVersion'] == '0.30.0' and status['lastPanelReport']['version'] == '0.30.0'
        baseline = call(readonly, 'inspect_active_model', expected='active_model_inspected')
        before = call(client, 'list_documents', expected='documents_listed_verified')
        target = before['activeWindow']
        assert window_identity(target) == window_identity(original['before']['activeWindow'])
        assert baseline['model']['path'] == target['modelPath'] and baseline['model']['id'] == target['modelId']
        own_choice = next(d for d in before['documents'] if Path(d['path']) == Path(original['workingPath']))
        assert not own_choice['isActive'] and own_choice['activationSupported']
        call(client, 'activate_document', {'documentHandle': own_choice['documentHandle']}, expected='document_active_verified')
        own = call(client, 'inspect_entity_candidates', expected='entity_candidates_inspected')
        choices = call(client, 'list_documents', expected='documents_listed_verified')
        assert Path(own['model']['path']) == Path(original['workingPath'])
        active = next(d for d in choices['documents'] if d['isActive'])
        verified = call(client, 'verify_working_copy_open', {'openRequestId': options.open_request_id,
            'openReceiptSha256': options.open_receipt_sha256, 'candidateDocumentHandle': own['candidateDocumentHandle'],
            'activeDocumentHandle': active['documentHandle']}, expected='owned_open_verified_observed')
        assert verified['recordedJournalState'] == 'succeeded_verified' and verified['contextChangeAttempted'] is False
        bound = call(client, 'bind_working_document', {'candidateDocumentHandle': own['candidateDocumentHandle'],
                     'workingCopyHandle': verified['workingCopyHandle']}, expected='working_document_bound_observed')
        call(client, 'validate_working_document_binding', {'workingDocumentBindingHandle': bound['workingDocumentBindingHandle']}, expected='working_document_binding_validated_observed')
        bodies = [e for e in own['entities'] if e['entitySetSupported'] and e['nativeTypeCode'] == 40]
        assert len(bodies) == 1
        entity_set = call(client, 'prepare_entity_set', {'candidateDocumentHandle': own['candidateDocumentHandle'],
                 'candidateEntityHandles': [e['candidateEntityHandle'] for e in bodies]}, expected='entity_set_prepared_observed')
        call(client, 'validate_entity_set', {'entitySetHandle': entity_set['entitySetHandle']}, expected='entity_set_validated_observed')
        plan = call(client, 'prepare_operation_plan', {'operation': 'translate_entities', 'entitySetHandles': [entity_set['entitySetHandle']],
               'parameters': {'vector': [1, 0, 0], 'coordinateFrame': 'world', 'lengthUnits': 'document_raw', 'mode': 'move'}}, expected='operation_plan_prepared_local')
        args = {'operationPlanHandle': plan['operationPlanHandle'], 'workingDocumentBindingHandle': bound['workingDocumentBindingHandle']}
        call(foreign, 'prepare_owned_operation', args, expected='owned_operation_rejected')
        call(readonly, 'prepare_owned_operation', args, rpc_error=True)
        call(client, 'prepare_owned_operation', {**args, 'command': 'SAVE'}, rpc_error=True)
        packet = call(client, 'prepare_owned_operation', args, expected='owned_operation_prepared_observed')
        assert packet['executionEnabled'] is False and packet['guardPasses'] == 2 and packet['sameContextObservationReads'] == 4
        assert packet['copyId'] == original['copyId'] and packet['journalPreparedOnly'] is True
        packet_args = {'ownedOperationHandle': packet['ownedOperationHandle']}
        call(foreign, 'validate_owned_operation', packet_args, expected='owned_operation_rejected')
        call(readonly, 'validate_owned_operation', packet_args, rpc_error=True)
        validated = call(client, 'validate_owned_operation', packet_args, expected='owned_operation_validated_observed')
        assert validated['ownedOperationSha256'] == packet['ownedOperationSha256'] and validated['sameContextObservationReads'] == 4
        journal = call(readonly, 'operation_result', {'operationId': packet['ownedOperationId']}, expected='operation_receipt_read')
        assert journal['receipt']['recordedState'] == 'prepared' and journal['receipt']['eventCount'] == 1 and journal['receipt']['retryAllowed'] is False
        independent = call(readonly, 'inspect_active_model', expected='active_model_inspected')
        assert observed(independent) == observed(own)
        copy = call(client, 'inspect_working_copy', {'workingCopyHandle': verified['workingCopyHandle']}, expected='working_copy_inspected_disk')
        choices = call(client, 'list_documents', expected='documents_listed_verified')
        restore = next(d for d in choices['documents'] if d['modelId'] == target['modelId'] and d['path'] == target['modelPath'])
        call(client, 'activate_document', {'documentHandle': restore['documentHandle']}, expected='document_active_verified')
        restored = call(readonly, 'inspect_active_model', expected='active_model_inspected')
        assert observed(restored) == observed(baseline)
        final = call(client, 'list_documents', expected='documents_listed_verified')
        assert window_identity(final['activeWindow']) == window_identity(target) and final['windowCount'] == before['windowCount']
        for old in before['windows']: assert sum(window_identity(old) == window_identity(w) for w in final['windows']) == 1
        call(client, 'validate_owned_operation', packet_args, expected='owned_operation_rejected')
        assert {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in events} == open_journal_hashes
        assert hashlib.sha256(claim_path.read_bytes()).hexdigest() == claim_sha
        summary.update(state='passed_owned_operation_guard_mcp', originalActiveDocumentRestored=True,
                       existingDocumentIdentitiesPreserved=True, originalOpenJournalUnchanged=True,
                       liveCoupledGuardQualified=True, independentReadCompared=True, workingCopyInspection=copy,
                       ownedOperationId=packet['ownedOperationId'], ownedOperationSha256=packet['ownedOperationSha256'],
                       limitation='One saved SOLID model. Coupled passive guard only; no geometry executor, full topology, continuous CAD revision, surface/curve live qualification, save/close or general recovery.')
    except Exception as error:
        summary.update(state='failed_requires_inspection', error=str(error)); raise
    finally:
        for client in clients: client.close()
        (output / 'qualification.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
    print('PASS: coupled owned plan prepared/validated in one retained COM context; independent read, refusals and original active model restoration. No open/save/close/geometry.')


if __name__ == '__main__': main()
