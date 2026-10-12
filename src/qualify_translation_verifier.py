"""Qualify hardened verification of the already completed inverse; no translation.

Only fresh document choices and two activations. No execution, save/close or replay.
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
    parser.add_argument('--translation-qualification', required=True, type=Path)
    options = parser.parse_args()
    root, output = options.bridge_root.resolve(), options.output.resolve()
    completed = json.loads(options.translation_qualification.read_text(encoding='utf-8-sig'))
    assert completed['state'] == 'passed_owned_translation_mcp' and not pending(root)
    execution = completed['executions'][-1]; assert execution['independentlyVerified'] is True
    own = json.loads((root / 'workingcopies' / completed['copyId'] / 'ownership.json').read_text())
    path = Path(own['working']['path'])
    journal = root / 'operations' / execution['operationId']
    original_hashes = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in journal.glob('*.json')}
    assert len(original_hashes) == 6
    output.mkdir(parents=True, exist_ok=True); clients, records = [], []
    summary = {'state': 'incomplete', 'records': records, 'geometryMutationAttempted': False,
               'translationCommandReplayAttempted': False, 'cadSaveAttempted': False, 'cadCloseAttempted': False,
               'executionOperationId': execution['operationId'], 'originalJournalHashes': original_hashes}

    def call(client, name, args=None, expected=None):
        reply = client.rpc('tools/call', {'name': name, 'arguments': args or {}})
        data = reply.get('result', {}).get('structuredContent', reply.get('error'))
        file = output / ('%02d-%s.json' % (len(records) + 1, name)); file.write_text(json.dumps(reply, ensure_ascii=False, indent=2), encoding='utf-8')
        record = {'tool': name, 'state': data.get('state'), 'path': str(file)}
        if data.get('bridgeRequestId'): record['nativeReceiptSha256'] = hashlib.sha256((root / 'responses' / (data['bridgeRequestId'] + '.json')).read_bytes()).hexdigest()
        records.append(record)
        if data.get('state') != expected: raise RuntimeError('Unexpected ' + name + ': ' + json.dumps(data, ensure_ascii=False))
        return data

    try:
        client = PassiveClient(root / 'mcp_server.py', root); clients.append(client)
        readonly = PassiveClient(root / 'mcp_server.py', root, read_only=True); clients.append(readonly)
        assert len(client.rpc('tools/list')['result']['tools']) == 21 and len(readonly.rpc('tools/list')['result']['tools']) == 6
        status = call(client, 'bridge_status'); assert status['adapterVersion'] == '0.31.0'
        before = call(readonly, 'inspect_active_model', expected='active_model_inspected')
        choices = call(client, 'list_documents', expected='documents_listed_verified'); target = choices['activeWindow']
        chosen = next(d for d in choices['documents'] if Path(d['path']) == path); assert not chosen['isActive']
        call(client, 'activate_document', {'documentHandle': chosen['documentHandle']}, expected='document_active_verified')
        inspection = call(client, 'inspect_entity_candidates', expected='entity_candidates_inspected')
        listing = call(client, 'list_documents', expected='documents_listed_verified')
        active = next(d for d in listing['documents'] if d['isActive']); assert Path(active['path']) == path
        verified = call(client, 'verify_owned_translation', {'executionRequestId': execution['operationId'],
            'executionReceiptSha256': execution['nativeReceiptSha256'], 'candidateDocumentHandle': inspection['candidateDocumentHandle'],
            'activeDocumentHandle': active['documentHandle']}, expected='owned_translation_verified_observed')
        assert verified['noCommandReplay'] is True and verified['recordedJournalState'] == 'succeeded_verified'
        independent = call(readonly, 'inspect_active_model', expected='active_model_inspected'); assert observed(independent) == observed(inspection)
        choices = call(client, 'list_documents', expected='documents_listed_verified')
        restore = next(d for d in choices['documents'] if d['modelId'] == target['modelId'] and d['path'] == target['modelPath'])
        call(client, 'activate_document', {'documentHandle': restore['documentHandle']}, expected='document_active_verified')
        after = call(readonly, 'inspect_active_model', expected='active_model_inspected'); assert observed(after) == observed(before)
        final = call(client, 'list_documents', expected='documents_listed_verified')
        assert window_identity(final['activeWindow']) == window_identity(target) and final['windowCount'] == choices['windowCount']
        assert {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in journal.glob('*.json')} == original_hashes
        summary.update(state='passed_hardened_translation_verifier', originalActiveDocumentRestored=True,
                       journalUnchanged=True, independentReadCompared=True)
    except Exception as error:
        summary.update(state='failed_requires_inspection', error=str(error)); raise
    finally:
        for client in clients: client.close()
        (output / 'qualification.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
    print('PASS: hardened verifier confirmed completed inverse without command replay or journal append; original active document restored.')


if __name__ == '__main__': main()
