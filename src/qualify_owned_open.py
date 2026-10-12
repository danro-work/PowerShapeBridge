"""Explicit owned-copy open qualification. No save/close/geometry/self-test calls.

One new model window is expected; the original document is selected again only
after known successful open/binding. An unknown outcome stops the scenario.
"""
import argparse
import json
from pathlib import Path
import sys

from qualify_core_mcp import PassiveClient, pending
from operation_plans import source_observations


def observed(snapshot):
    value = {**snapshot, 'entities': [{k: v for k, v in e.items() if k not in {'entityReference', 'candidateEntityHandle', 'entitySetSupported'}} for e in snapshot['entities']]}
    return source_observations(value)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--bridge-root', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--source', required=True, type=Path)
    parser.add_argument('--source-sha256', required=True)
    parser.add_argument('--allow-owned-copy-open', action='store_true')
    options = parser.parse_args()
    root, output = options.bridge_root.resolve(), options.output.resolve()
    settings = json.loads((root / 'bridge.config.json').read_text(encoding='utf-8-sig'))
    if not options.allow_owned_copy_open or settings.get('workingCopyOpenExperimental') is not True or pending(root):
        raise RuntimeError('Explicit owned-open permission, enabled profile and idle queue required')
    output.mkdir(parents=True, exist_ok=True)
    records, clients = [], []
    summary = {'state': 'incomplete', 'root': str(root), 'records': records,
               'geometryMutationAttempted': False, 'cadSaveAttempted': False, 'cadCloseAttempted': False}
    def call(client, name, args=None, expected=None, rpc_error=False):
        reply = client.rpc('tools/call', {'name': name, 'arguments': args or {}})
        data = reply.get('result', {}).get('structuredContent', reply.get('error'))
        path = output / ('%02d-%s.json' % (len(records) + 1, name))
        path.write_text(json.dumps(reply, ensure_ascii=False, indent=2), encoding='utf-8')
        records.append({'tool': name, 'state': data.get('state'), 'path': str(path)})
        if rpc_error:
            if data.get('code') != -32602: raise RuntimeError('Expected read-only refusal')
        elif data.get('state') != expected:
            summary['failedTool'] = name
            summary['failedBridgeRequestId'] = data.get('bridgeRequestId')
            raise RuntimeError('Unexpected ' + name + ': ' + json.dumps(data, ensure_ascii=False))
        return data
    try:
        client = PassiveClient(root / 'mcp_server.py', root); clients.append(client)
        readonly = PassiveClient(root / 'mcp_server.py', root, read_only=True); clients.append(readonly)
        foreign = PassiveClient(root / 'mcp_server.py', root); clients.append(foreign)
        catalog = client.rpc('tools/list')['result']['tools']
        assert len(catalog) == 17 and len(readonly.rpc('tools/list')['result']['tools']) == 6
        summary['toolNames'] = [tool['name'] for tool in catalog]
        before = call(client, 'list_documents', expected='documents_listed_verified')
        original = next(d for d in before['documents'] if d['isActive'])
        candidates = call(client, 'inspect_entity_candidates', expected='entity_candidates_inspected')
        working = call(client, 'prepare_working_copy', {'sourcePath': str(options.source.resolve()), 'sourceSha256': options.source_sha256}, expected='working_copy_prepared_disk')
        arguments = {'workingCopyHandle': working['workingCopyHandle'], 'candidateDocumentHandle': candidates['candidateDocumentHandle'], 'activeDocumentHandle': original['documentHandle']}
        call(readonly, 'open_working_copy', arguments, rpc_error=True)
        call(foreign, 'open_working_copy', arguments, expected='working_copy_open_rejected')
        opened = call(client, 'open_working_copy', arguments, expected='working_copy_opened_observed')
        summary.update(openOperationId=opened['operationId'], openedCopyId=working['copyId'], openedPath=opened['workingPath'], contextChangeAttempted=True)
        journal = call(readonly, 'operation_result', {'operationId': opened['operationId']}, expected='operation_receipt_read')
        assert journal['receipt']['state'] == 'executed_unverified' and journal['receipt']['retryAllowed'] is False
        call(client, 'prepare_entity_set', {'candidateDocumentHandle': candidates['candidateDocumentHandle'], 'candidateEntityHandles': [candidates['entities'][0]['candidateEntityHandle']]}, expected='entity_set_rejected')
        new_listing = call(client, 'list_documents', expected='documents_listed_verified')
        assert new_listing['windowCount'] == before['windowCount'] + 1
        own_candidates = call(client, 'inspect_entity_candidates', expected='entity_candidates_inspected')
        assert Path(own_candidates['model']['path']) == Path(opened['workingPath'])
        bound = call(client, 'bind_working_document', {'candidateDocumentHandle': own_candidates['candidateDocumentHandle'], 'workingCopyHandle': working['workingCopyHandle']}, expected='working_document_bound_observed')
        call(client, 'validate_working_document_binding', {'workingDocumentBindingHandle': bound['workingDocumentBindingHandle']}, expected='working_document_binding_validated_observed')
        supported = [e for e in own_candidates['entities'] if e['entitySetSupported']]
        assert supported
        entity_set = call(client, 'prepare_entity_set', {'candidateDocumentHandle': own_candidates['candidateDocumentHandle'], 'candidateEntityHandles': [e['candidateEntityHandle'] for e in supported]}, expected='entity_set_prepared_observed')
        call(client, 'validate_entity_set', {'entitySetHandle': entity_set['entitySetHandle']}, expected='entity_set_validated_observed')
        plan = call(client, 'prepare_operation_plan', {'operation': 'translate_entities', 'entitySetHandles': [entity_set['entitySetHandle']], 'parameters': {'vector': [1, 0, 0], 'coordinateFrame': 'world', 'lengthUnits': 'document_raw', 'mode': 'move'}}, expected='operation_plan_prepared_local')
        call(client, 'inspect_operation_plan', {'operationPlanHandle': plan['operationPlanHandle']}, expected='operation_plan_inspected_local')
        call(client, 'inspect_working_copy', {'workingCopyHandle': working['workingCopyHandle']}, expected='working_copy_inspected_disk')
        independent = call(readonly, 'inspect_active_model', expected='active_model_inspected')
        assert observed(independent) == observed(own_candidates)
        choices = call(client, 'list_documents', expected='documents_listed_verified')
        target = next(d for d in choices['documents'] if d['modelId'] == original['modelId'] and d['path'] == original['path'])
        call(client, 'activate_document', {'documentHandle': target['documentHandle']}, expected='document_active_verified')
        restored = call(readonly, 'inspect_active_model', expected='active_model_inspected')
        assert observed(restored) == observed(candidates)
        final = call(client, 'list_documents', expected='documents_listed_verified')
        identity = lambda w: {k: v for k, v in w.items() if k not in {'selectedRaw', 'nativeWindowId'}}
        assert identity(final['activeWindow']) == identity(before['activeWindow']) and final['windowCount'] == before['windowCount'] + 1
        for old in before['windows']: assert sum(identity(old) == identity(w) for w in final['windows']) == 1
        call(client, 'validate_working_document_binding', {'workingDocumentBindingHandle': bound['workingDocumentBindingHandle']}, expected='working_document_binding_rejected')
        fresh = call(client, 'inspect_entity_candidates', expected='entity_candidates_inspected')
        active = next(d for d in final['documents'] if d['isActive'])
        refused = call(client, 'open_working_copy', {'workingCopyHandle': working['workingCopyHandle'], 'candidateDocumentHandle': fresh['candidateDocumentHandle'], 'activeDocumentHandle': active['documentHandle']}, expected='working_copy_open_rejected')
        assert refused['contextChangeAttempted'] is False and 'earlier open claim' in refused['error']
        summary.update(state='passed_owned_copy_open_mcp', originalActiveDocumentRestored=True,
                       existingWindowsPreserved=True, oneOwnedCopyWindowRetained=True,
                       successfulLiveDocumentBindingQualified=True, entityTypesTested=[e['type'] for e in supported],
                       independentReadCompared=True, openJournalRecordedState='executed_unverified',
                       limitation='One saved SOLID model; geometry execution, close/save/reopen/recovery and full topology equivalence are not qualified.')
    except Exception as error:
        summary.update(state='failed_requires_inspection', error=str(error), automaticReplayAttempted=False)
        raise
    finally:
        for client in clients: client.close()
        (output / 'qualification.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
    print('PASS: one owned copy opened, live binding/entity set/local plan inspected, independent reads matched, original active model restored. No save/close/geometry.')


if __name__ == '__main__': main()
