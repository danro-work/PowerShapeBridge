"""Independent audit of saved native evidence; no COM, queue or CAD commands."""
import argparse
import datetime
import hashlib
import json
from pathlib import Path

from audit_core_mcp import file_identity, read, sha
from qualify_owned_open import observed
from qualify_owned_open_recovery import window_identity


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--bridge-root', required=True, type=Path)
    parser.add_argument('--evidence', required=True, type=Path)
    parser.add_argument('--qualification-folder', default='installed-mcp-final')
    options = parser.parse_args()
    root, evidence = options.bridge_root.resolve(), options.evidence.resolve()
    qualification = read(evidence / options.qualification_folder / 'qualification.json')
    assert qualification['state'] == 'passed_owned_operation_guard_mcp'
    counts = {'nativeReceipts': 0, 'nativeKnownSuccess': 0, 'nativeKnownRefusal': 0, 'localSuccess': 0, 'localRefusals': 0}
    recipes, artifacts, success = [], set(), {}
    inspections, listings = [], []
    for record in qualification['records']:
        message = read(Path(record['path']))
        if 'error' in message:
            assert message['error']['code'] == -32602; counts['localRefusals'] += 1; continue
        delivered = message['result']['structuredContent']; request = delivered.get('bridgeRequestId')
        if request is None:
            counts['localRefusals' if message['result']['isError'] else 'localSuccess'] += 1
            if record['tool'] == 'prepare_operation_plan': success[record['tool']] = delivered
            continue
        native_path = root / 'responses' / (request + '.json')
        envelope_path = root / 'requests' / (request + '.json.processing.done')
        native, envelope = read(native_path), read(envelope_path); artifacts.update((native_path, envelope_path))
        assert envelope['requestId'] == request and envelope['operation'] == 'recipe.prepare'
        recipe = envelope['arguments']['recipeId']; recipes.append(recipe)
        assert recipe in {'list_documents', 'inspect_active_model', 'activate_document', 'verify_working_copy_open',
                          'bind_working_document', 'validate_working_document_binding', 'prepare_entity_set',
                          'validate_entity_set', 'inspect_working_copy', 'prepare_owned_operation', 'validate_owned_operation'}
        if recipe == 'activate_document': assert native['state'] == 'document_active_verified' and native['savedFilesUnchanged'] is True
        else: assert native['bridgeVersion'] == '0.30.0'
        adapted = {**delivered}
        if recipe == 'inspect_active_model':
            adapted['state'] = 'succeeded_verified'
            adapted['entities'] = [{k: v for k, v in e.items() if k not in {'entityReference', 'candidateEntityHandle', 'entitySetSupported'}} for e in delivered['entities']]
            inspections.append(native)
        elif recipe == 'list_documents':
            adapted['documents'] = [{k: v for k, v in d.items() if k != 'documentHandle'} for d in delivered['documents']]
            listings.append(native)
        for key, value in native.items():
            if key != 'entitySetQualification': assert adapted[key] == value, (request, key)
        assert native.get('geometryMutationAttempted', False) is False and native.get('cadSaveAttempted', False) is False
        if recipe != 'activate_document': assert native.get('contextChangeAttempted', False) is False
        counts['nativeReceipts'] += 1
        counts['nativeKnownRefusal' if message['result']['isError'] else 'nativeKnownSuccess'] += 1
        if not message['result']['isError']: success[record['tool']] = native
        parameters = envelope['arguments'].get('parameters', {})
        for prefix, folder in (('inspection', 'responses'), ('listing', 'responses'), ('operationPlan', 'operationplans'),
                               ('binding', 'documentbindings'), ('ownedOperation', 'ownedoperations')):
            if prefix + 'RequestId' in parameters or prefix + 'Id' in parameters:
                ident = parameters.get(prefix + 'RequestId', parameters.get(prefix + 'Id'))
                path = root / folder / (ident + '.json')
                assert sha(path) == parameters[prefix + 'Sha256']; artifacts.add(path)
    assert recipes.count('activate_document') == 2 and 'open_working_copy' not in recipes
    assert counts['nativeKnownRefusal'] == 0
    prepared, validated = success['prepare_owned_operation'], success['validate_owned_operation']
    assert prepared['ownedOperationId'] == validated['ownedOperationId'] == qualification['ownedOperationId']
    assert prepared['ownedOperationSha256'] == validated['ownedOperationSha256'] == qualification['ownedOperationSha256']
    packet_path = root / 'ownedoperations' / (prepared['ownedOperationId'] + '.json')
    packet = read(packet_path)
    assert sha(packet_path) == prepared['ownedOperationSha256'] and prepared['ownedOperation'] == validated['ownedOperation'] == packet
    assert packet['executionEnabled'] is False
    for receipt in (prepared, validated):
        assert receipt['guardPasses'] == 2 and receipt['sameContextObservationReads'] == 4 and receipt['journalPreparedOnly'] is True
        assert receipt['heldProofArtifactCount'] >= 7
        assert datetime.datetime.fromisoformat(receipt['timestampUtc'].replace('Z', '+00:00')) < datetime.datetime.fromisoformat(packet['expiresUtc'].replace('Z', '+00:00'))
    binding_path = root / 'documentbindings' / (packet['bindingId'] + '.json')
    plan_path = root / 'operationplans' / (packet['operationPlanId'] + '.json')
    assert sha(binding_path) == packet['bindingSha256'] and sha(plan_path) == packet['operationPlanSha256']
    binding, plan = read(binding_path), read(plan_path)
    assert binding['executionEnabled'] is False and plan['executionEnabled'] is False
    assert packet['copyId'] == binding['copyId'] == qualification['copyId']
    assert packet['ownershipSha256'] == binding['ownershipSha256']
    assert packet['windowContextSha256'] == binding['windowContextSha256']
    assert packet['nativeObservedPropertiesSha256'] == binding['observedPropertiesSha256']
    assert plan == success['prepare_operation_plan']['plan']
    sources = []
    for entity_set in plan['entitySets']:
        set_path = root / 'entitysets' / (entity_set['entitySetId'] + '.json')
        assert sha(set_path) == entity_set['entitySetSha256']; artifacts.add(set_path)
        entity_data = read(set_path)
        assert all(entity_data[k] == entity_set[k] for k in entity_data)
        source_path = root / 'responses' / (entity_data['inspectionRequestId'] + '.json')
        assert sha(source_path) == entity_data['inspectionSha256']; artifacts.add(source_path); sources.append(read(source_path))
    source = observed(sources[0])
    assert all(observed(s) == source for s in sources)
    digest = hashlib.sha256(json.dumps(source, sort_keys=True, ensure_ascii=False, allow_nan=False, separators=(',', ':')).encode()).hexdigest()
    assert digest == plan['observedPropertiesSha256']
    assert plan['sourceIdentity'] == {k: source[k] for k in ('processId', 'processStartedUtc', 'windowsSessionId', 'productVersion', 'model')}
    binding_source = root / 'responses' / (binding['inspectionRequestId'] + '.json')
    assert sha(binding_source) == binding['inspectionSha256'] and observed(read(binding_source)) == source; artifacts.add(binding_source)
    for operation_id, plan_sha in ((packet['operationPlanId'], packet['operationPlanSha256']), (packet['ownedOperationId'], prepared['ownedOperationSha256'])):
        folder = root / 'operations' / operation_id; events = list(folder.glob('*.json'))
        assert len(events) == 1 and not (folder / 'writer.lock').exists()
        event = read(events[0]); artifacts.add(events[0])
        assert event['sequence'] == 0 and event['previousSha256'] is None and event['state'] == 'prepared'
        assert event['operationId'] == operation_id and event['planSha256'] == plan_sha and event['details']['executionEnabled'] is False
    ownership_path = root / 'workingcopies' / packet['copyId'] / 'ownership.json'
    assert sha(ownership_path) == packet['ownershipSha256']; ownership = read(ownership_path); artifacts.add(ownership_path)
    identities = []
    for field in ('source', 'working', 'checkpoint'):
        info = ownership[field]; path = Path(info['path']); ident, links, attributes = file_identity(path)
        assert ident == info['fileIdentity'] and links == 1 and path.stat().st_size == info['length']
        assert sha(path) == info.get('sha256', info.get('initialSha256'))
        if field == 'working': assert sha(path) == packet['workingSha256']
        if field == 'checkpoint': assert attributes & 1
        identities.append((ident['volumeSerial'], ident['fileIndex']))
    assert len(set(identities)) == 3
    original_path = root / 'responses' / (qualification['openOperationId'] + '.json'); artifacts.add(original_path)
    assert sha(original_path) == qualification['openReceiptSha256']
    original_events = root / 'operations' / qualification['openOperationId']
    for name, expected in qualification['originalOpenJournalHashes'].items():
        path = original_events / name; assert sha(path) == expected; artifacts.add(path)
    assert len(list(original_events.glob('*.json'))) == 6
    claim = root / 'workingcopies' / packet['copyId'] / 'open-once.json'
    assert sha(claim) == qualification['openClaimSha256']; artifacts.add(claim)
    assert observed(inspections[0]) == observed(inspections[-1])
    assert observed(inspections[1]) == observed(inspections[2]) == source
    assert observed(inspections[0]) == observed(read(evidence / 'preflight/before-deploy-inspect_active_model.json'))
    assert listings[0]['windowCount'] == listings[-1]['windowCount'] == 4
    assert window_identity(listings[0]['activeWindow']) == window_identity(listings[-1]['activeWindow'])
    for old in listings[0]['windows']: assert sum(window_identity(old) == window_identity(w) for w in listings[-1]['windows']) == 1
    final = read(evidence / 'final-runtime.json')
    assert final['testedSourcesAndStageUnchanged'] is True and final['protectedSavedHashesVerified'] is True
    assert final['component']['version'] == '0.30.0' and final['component']['ownedOperationPlansExperimental'] is True
    assert final['pendingQueueEmpty'] is True
    for protected in final['protectedFiles']: assert sha(Path(protected['path'])) == protected['sha256']
    artifact_hashes = []
    artifacts.update((packet_path, binding_path, plan_path))
    for path in sorted(artifacts):
        relative = path.relative_to(root); destination = evidence / 'native-artifacts' / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        if destination.exists(): assert sha(destination) == sha(path)
        else:
            with destination.open('xb') as file: file.write(path.read_bytes())
        assert sha(destination) == sha(path); artifact_hashes.append({'path': str(relative), 'sha256': sha(path)})
    result = {'state': 'passed_independent_owned_operation_audit', **counts, 'coupledProofHashesVerified': True,
              'twoGuardPassesFourReadsVerified': True, 'preparedJournalsOnly': True, 'noOpenReplay': True,
              'originalOpenJournalUnchanged': True, 'fileIdentitiesAndReadOnlyCheckpointVerified': True,
              'protectedSavedFilesUnchanged': True, 'originalActiveDocumentRestored': True,
              'fourDocumentIdentitiesPreserved': True, 'geometryExecutionDisabled': True,
              'cadConnectionAttempted': False, 'nativeArtifactHashes': artifact_hashes,
              'limitation': 'Saved evidence, observed properties and file bytes; no full topology or continuous CAD revision proof.'}
    (evidence / 'independent-audit.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({k: v for k, v in result.items() if k != 'nativeArtifactHashes'}, ensure_ascii=False))


if __name__ == '__main__': main()
