"""Independent saved evidence/file audit; no COM connection or request submission."""
import argparse
import datetime
import json
from pathlib import Path

from audit_core_mcp import file_identity, read, sha


def identity(window):
    return {k: v for k, v in window.items() if k not in {'nativeWindowId', 'selectedRaw'}}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--bridge-root', required=True, type=Path)
    parser.add_argument('--evidence', required=True, type=Path)
    options = parser.parse_args()
    root, evidence = options.bridge_root.resolve(), options.evidence.resolve()
    qualification = read(evidence / 'recovered-mcp/qualification.json')
    assert qualification['state'] == 'passed_owned_open_verification_mcp'
    counts = {'nativeReceipts': 0, 'nativeKnownSuccess': 0, 'nativeKnownRefusal': 0,
              'nativeUnknownPreserved': 0, 'localSuccess': 0, 'localRefusals': 0}
    recipes = []
    success = {}
    for folder in ('installed-mcp', 'recovered-mcp'):
        for record in read(evidence / folder / 'qualification.json')['records']:
            message = read(Path(record['path']))
            if 'error' in message:
                assert message['error']['code'] == -32602
                counts['localRefusals'] += 1
                continue
            delivered = message['result']['structuredContent']
            request = delivered.get('bridgeRequestId')
            if request is None:
                key = 'localRefusals' if message['result']['isError'] else 'localSuccess'
                counts[key] += 1
                continue
            native = read(root / 'responses' / (request + '.json'))
            envelope = read(root / 'requests' / (request + '.json.processing.done'))
            assert envelope['requestId'] == request and envelope['operation'] == 'recipe.prepare'
            recipe = envelope['arguments']['recipeId']; recipes.append(recipe)
            assert recipe in {'list_documents', 'inspect_active_model', 'prepare_working_copy', 'open_working_copy',
                              'verify_working_copy_open', 'bind_working_document', 'validate_working_document_binding',
                              'prepare_entity_set', 'validate_entity_set', 'inspect_working_copy', 'activate_document'}
            if recipe != 'activate_document': assert native['bridgeVersion'] == '0.29.0'
            else: assert native['state'] == 'document_active_verified' and native['savedFilesUnchanged'] is True
            adapted = {**delivered}
            if recipe == 'inspect_active_model':
                adapted['state'] = 'succeeded_verified'
                adapted['entities'] = [{k: v for k, v in e.items() if k not in {'entityReference', 'candidateEntityHandle', 'entitySetSupported'}} for e in delivered['entities']]
            elif recipe == 'list_documents':
                adapted['documents'] = [{k: v for k, v in d.items() if k != 'documentHandle'} for d in delivered['documents']]
            for key, value in native.items():
                if key != 'entitySetQualification': assert adapted[key] == value, (request, key)
            assert native.get('geometryMutationAttempted', False) is False and native.get('cadSaveAttempted', False) is False
            if recipe not in {'open_working_copy', 'activate_document'}: assert native.get('contextChangeAttempted', False) is False
            counts['nativeReceipts'] += 1
            if native['state'] == 'working_copy_open_outcome_unknown': counts['nativeUnknownPreserved'] += 1
            elif message['result']['isError']: counts['nativeKnownRefusal'] += 1
            else:
                counts['nativeKnownSuccess'] += 1
                success[record['tool']] = native
            parameters = envelope['arguments'].get('parameters', {})
            for prefix in ('inspection', 'listing'):
                if prefix + 'RequestId' in parameters:
                    assert sha(root / 'responses' / (parameters[prefix + 'RequestId'] + '.json')) == parameters[prefix + 'Sha256']
    assert recipes.count('open_working_copy') == 1 and recipes.count('activate_document') == 1
    assert counts['nativeUnknownPreserved'] == 1
    opened = qualification['openOperationId']
    original_path = root / 'responses' / (opened + '.json')
    original = read(original_path)
    assert sha(original_path) == qualification['openReceiptSha256']
    plan_path = root / 'copyopenplans' / (opened + '.json')
    assert sha(plan_path) == original['planSha256'] and read(plan_path) == original['plan']
    assert original['plan']['geometryExecutionEnabled'] is False
    journal = root / 'operations' / opened
    assert not (journal / 'writer.lock').exists()
    events = []; previous = None
    for number, path in enumerate(sorted(journal.glob('*.json'))):
        event = read(path)
        assert path.name == '%06d.json' % number and event['sequence'] == number
        assert event['operationId'] == opened and event['planSha256'] == original['planSha256']
        assert event['previousSha256'] == previous
        previous = sha(path); events.append(event)
    assert [e['state'] for e in events] == ['prepared', 'execution_claimed', 'command_started', 'command_returned', 'outcome_unknown', 'succeeded_verified']
    returned = datetime.datetime.fromisoformat(events[3]['timestampUtc'].replace('Z', '+00:00'))
    assert events[-1]['details']['noCommandReplay'] is True
    for proof in events[-1]['details']['evidence']:
        path = root / 'responses' / (proof['requestId'] + '.json')
        assert sha(path) == proof['sha256']
        if proof['requestId'] != opened:
            assert datetime.datetime.fromisoformat(read(path)['timestampUtc'].replace('Z', '+00:00')) > returned
    claim_path = root / 'workingcopies' / original['copyId'] / 'open-once.json'
    claim = read(claim_path)
    assert claim['operationId'] == opened and claim['planSha256'] == sha(plan_path) and claim['automaticReplayForbidden'] is True
    copy = success['inspect_working_copy']; ownership = copy['ownership']
    own_path = root / 'workingcopies' / original['copyId'] / 'ownership.json'
    assert sha(own_path) == original['ownershipSha256'] and read(own_path) == ownership
    native_identities = []
    for field in ('source', 'working', 'checkpoint'):
        info = ownership[field]; path = Path(info['path'])
        native_id, links, attributes = file_identity(path)
        assert native_id == info['fileIdentity'] and links == 1
        assert path.stat().st_size == info['length']
        assert sha(path) == info.get('sha256', info.get('initialSha256'))
        if field == 'checkpoint': assert attributes & 1
        native_identities.append(native_id)
    assert len({(i['volumeSerial'], i['fileIndex']) for i in native_identities}) == 3
    protected = read(evidence / 'protected-after-open.json')
    for file in protected['files']: assert sha(Path(file['path'])) == file['sha256']
    for path, expected in original['savedFileHashesBefore'].items(): assert sha(Path(path)) == expected
    binding = success['bind_working_document']
    assert sha(root / 'documentbindings' / (binding['bindingId'] + '.json')) == binding['bindingSha256']
    assert read(root / 'documentbindings' / (binding['bindingId'] + '.json'))['executionEnabled'] is False
    final = success['list_documents']
    assert final['windowCount'] == original['before']['windowCount'] + 1
    assert identity(final['activeWindow']) == identity(original['before']['activeWindow'])
    for window in original['before']['windows']: assert sum(identity(window) == identity(w) for w in final['windows']) == 1
    baseline = read(evidence / 'preflight/native-inspection.json')
    restored = success['inspect_active_model']
    observation_fields = ('processId', 'processStartedUtc', 'windowsSessionId', 'productVersion', 'model',
                          'entityCount', 'entities', 'levelsContainingEntities', 'workplanes',
                          'selectionBefore', 'selectionAfter', 'contextBefore', 'contextAfter',
                          'lengthUnitsRaw', 'lengthUnitNameRaw', 'lengthUnitFactorRaw',
                          'areaUnitNameRaw', 'areaUnitFactorRaw', 'volumeUnitNameRaw',
                          'volumeUnitFactorRaw', 'generalToleranceRaw')
    for field in observation_fields: assert restored[field] == baseline[field], field
    for record in qualification['records']:
        if record['tool'] == 'prepare_operation_plan':
            result = read(Path(record['path']))['result']['structuredContent']
            local = root / 'operationplans' / (result['operationId'] + '.json')
            assert sha(local) == result['planSha256'] and read(local) == result['plan']
            assert result['plan']['executionEnabled'] is False
            local_events = list((root / 'operations' / result['operationId']).glob('*.json'))
            assert len(local_events) == 1 and read(local_events[0])['state'] == 'prepared'
            for entity_set in result['plan']['entitySets']:
                assert sha(root / 'entitysets' / (entity_set['entitySetId'] + '.json')) == entity_set['entitySetSha256']
    # Preserve exact bytes of referenced proof files alongside the human-readable calls.
    artifacts = {original_path, plan_path, own_path, claim_path, *journal.glob('*.json')}
    for folder in ('installed-mcp', 'recovered-mcp'):
        for record in read(evidence / folder / 'qualification.json')['records']:
            data = read(Path(record['path'])).get('result', {}).get('structuredContent', {})
            if data.get('bridgeRequestId'):
                request = data['bridgeRequestId']; artifacts.add(root / 'responses' / (request + '.json'))
                envelope_path = root / 'requests' / (request + '.json.processing.done'); artifacts.add(envelope_path)
                parameters = read(envelope_path)['arguments'].get('parameters', {})
                for prefix in ('inspection', 'listing'):
                    if prefix + 'RequestId' in parameters: artifacts.add(root / 'responses' / (parameters[prefix + 'RequestId'] + '.json'))
            for field, folder_name in (('bindingId', 'documentbindings'), ('entitySetId', 'entitysets')):
                if field in data: artifacts.add(root / folder_name / (data[field] + '.json'))
            if record['tool'] == 'prepare_operation_plan':
                artifacts.add(root / 'operationplans' / (data['operationId'] + '.json'))
                artifacts.update((root / 'operations' / data['operationId']).glob('*.json'))
    artifact_hashes = []
    for path in sorted(artifacts):
        relative = path.relative_to(root); destination = evidence / 'native-artifacts' / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        if destination.exists(): assert sha(destination) == sha(path)
        else:
            with destination.open('xb') as file: file.write(path.read_bytes())
        assert sha(destination) == sha(path)
        artifact_hashes.append({'path': str(relative), 'sha256': sha(path)})
    result = {'state': 'passed_independent_owned_open_audit', **counts,
              'oneOpenCommandOnly': True, 'noOpenReplay': True, 'journalEvents': [e['state'] for e in events],
              'journalSha256': previous, 'independentProofHashesVerified': True,
              'fileIdentitiesAndReadOnlyCheckpointVerified': True, 'protectedSavedFilesUnchanged': True,
              'originalActiveDocumentRestored': True, 'ownedWindowRetained': True,
              'geometryExecutionDisabled': True, 'cadConnectionAttempted': False,
              'restoredBaselineObservationsMatched': True, 'nativeArtifactHashes': artifact_hashes,
              'limitation': 'Saved evidence and bytes only; no full topology or continuous CAD lifecycle proof.'}
    (evidence / 'independent-audit.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False))


if __name__ == '__main__': main()
