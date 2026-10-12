"""Independent saved-byte, Win32 file identity and observable translation audit.

No COM, queue or geometry calls. Numeric checks are independently expressed here.
"""
import argparse
import copy
import datetime
import hashlib
import json
from pathlib import Path

from audit_core_mcp import file_identity, read, sha
from qualify_owned_open import observed
from qualify_owned_open_recovery import window_identity


def utc(value): return datetime.datetime.fromisoformat(value.replace('Z', '+00:00'))


def check_shift(before, after, native, vector):
    expected = copy.deepcopy(observed(before)); normalized = copy.deepcopy(observed(after))
    original = next(e for e in expected['entities'] if (e['nativeTypeCode'], e['nativeId']) == (40, native))['geometry']
    moved = next(e for e in normalized['entities'] if (e['nativeTypeCode'], e['nativeId']) == (40, native))['geometry']
    assert original['coordinateFrame'] == moved['coordinateFrame'] == 'world'
    assert expected['lengthUnitsRaw'] == normalized['lengthUnitsRaw'] == 'mm'
    assert expected['lengthUnitFactorRaw'] == normalized['lengthUnitFactorRaw'] == 1
    for old, new, delta in zip(original['cogRaw'], moved['cogRaw'], vector): assert abs(new - old - delta) <= 1e-6
    moved['cogRaw'] = original['cogRaw']
    for field in ('volumeRaw', 'areaRaw'):
        assert abs(moved[field] - original[field]) <= max(1e-9, abs(original[field]) * 1e-9)
        moved[field] = original[field]
    assert expected == normalized


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--bridge-root', required=True, type=Path)
    parser.add_argument('--evidence', required=True, type=Path)
    options = parser.parse_args()
    root, evidence = options.bridge_root.resolve(), options.evidence.resolve()
    qualification = read(evidence / 'installed-mcp/qualification.json')
    assert qualification['state'] == 'passed_owned_translation_mcp'
    hardened = read(evidence / 'hardened-verifier/qualification.json')
    assert hardened['state'] == 'passed_hardened_translation_verifier' and hardened['translationCommandReplayAttempted'] is False
    counts = {'nativeReceipts': 0, 'nativeKnownSuccess': 0, 'nativeKnownRefusal': 0, 'localSuccess': 0, 'localRefusals': 0}
    artifacts, recipes, native_by_id, inspections, listings = set(), [], {}, [], []
    local_plans, packets = [], []

    def proof(folder, ident, expected):
        path = root / folder / (ident + '.json'); assert sha(path) == expected; artifacts.add(path); return read(path)

    for record in qualification['records'] + hardened['records']:
        message = read(Path(record['path']))
        if 'error' in message:
            assert message['error']['code'] == -32602; counts['localRefusals'] += 1; continue
        delivered = message['result']['structuredContent']; request = delivered.get('bridgeRequestId')
        if not request:
            counts['localRefusals' if message['result']['isError'] else 'localSuccess'] += 1
            if record['tool'] == 'prepare_operation_plan': local_plans.append(delivered)
            continue
        path = root / 'responses' / (request + '.json')
        assert sha(path) == record['nativeReceiptSha256']; native = read(path); artifacts.add(path); native_by_id[request] = native
        envelope_path = root / 'requests' / (request + '.json.processing.done'); artifacts.add(envelope_path); envelope = read(envelope_path)
        assert envelope['requestId'] == request and envelope['operation'] == 'recipe.prepare'
        recipe = envelope['arguments']['recipeId']; recipes.append(recipe)
        assert recipe in {'inspect_active_model', 'list_documents', 'activate_document', 'verify_working_copy_open', 'bind_working_document',
                          'prepare_entity_set', 'prepare_owned_operation', 'execute_owned_translation', 'verify_owned_translation', 'inspect_working_copy'}
        if recipe == 'activate_document': assert native['savedFilesUnchanged'] is True
        else: assert native['bridgeVersion'] == '0.31.0'
        adapted = copy.deepcopy(delivered)
        if recipe == 'inspect_active_model':
            adapted['state'] = 'succeeded_verified'
            adapted['entities'] = [{k: v for k, v in e.items() if k not in {'entityReference', 'candidateEntityHandle', 'entitySetSupported'}} for e in delivered['entities']]
            inspections.append(native)
        elif recipe == 'list_documents':
            adapted['documents'] = [{k: v for k, v in d.items() if k != 'documentHandle'} for d in delivered['documents']]; listings.append(native)
        for key, value in native.items():
            if key != 'entitySetQualification': assert adapted[key] == value, (request, key)
        assert native.get('cadSaveAttempted', False) is False
        assert native.get('contextChangeAttempted', False) is (recipe == 'activate_document')
        assert native.get('geometryMutationAttempted', False) is (recipe == 'execute_owned_translation')
        counts['nativeReceipts'] += 1
        counts['nativeKnownRefusal' if message['result']['isError'] else 'nativeKnownSuccess'] += 1
        assert not message['result']['isError']
        params = envelope['arguments'].get('parameters', {})
        for prefix, folder in (('inspection', 'responses'), ('listing', 'responses'), ('operationPlan', 'operationplans'),
                               ('binding', 'documentbindings'), ('ownedOperation', 'ownedoperations')):
            ident = params.get(prefix + 'RequestId', params.get(prefix + 'Id'))
            if ident: proof(folder, ident, params[prefix + 'Sha256'])
        if recipe == 'prepare_owned_operation': packets.append(native['ownedOperation'])
    assert recipes.count('execute_owned_translation') == 2 and recipes.count('verify_owned_translation') == 3
    assert recipes.count('activate_document') == 4 and 'open_working_copy' not in recipes
    assert counts['nativeKnownRefusal'] == 0 and len(local_plans) == len(packets) == 2
    for local in local_plans:
        plan = proof('operationplans', local['operationId'], local['planSha256']); assert plan == local['plan'] and plan['executionEnabled'] is False
        original_observation = None
        for entity_set in plan['entitySets']:
            set_data = proof('entitysets', entity_set['entitySetId'], entity_set['entitySetSha256'])
            assert all(set_data[k] == entity_set[k] for k in set_data)
            source = proof('responses', set_data['inspectionRequestId'], set_data['inspectionSha256'])
            current = observed(source)
            if original_observation is None: original_observation = current
            else: assert original_observation == current
        digest = hashlib.sha256(json.dumps(original_observation, sort_keys=True, ensure_ascii=False, allow_nan=False, separators=(',', ':')).encode()).hexdigest()
        assert digest == plan['observedPropertiesSha256']
        events = list((root / 'operations' / local['operationId']).glob('*.json')); assert len(events) == 1 and read(events[0])['state'] == 'prepared'; artifacts.update(events)
    for packet in packets:
        binding = proof('documentbindings', packet['bindingId'], packet['bindingSha256'])
        assert packet['executionEnabled'] is binding['executionEnabled'] is False
        assert packet['copyId'] == binding['copyId'] == qualification['copyId']
        assert packet['ownershipSha256'] == binding['ownershipSha256']
        assert packet['nativeObservedPropertiesSha256'] == binding['observedPropertiesSha256']
        assert packet['windowContextSha256'] == binding['windowContextSha256']
        proof('responses', binding['inspectionRequestId'], binding['inspectionSha256'])
        events = list((root / 'operations' / packet['ownedOperationId']).glob('*.json')); assert len(events) == 1 and read(events[0])['state'] == 'prepared'; artifacts.update(events)
    restored_pair = []
    for item in qualification['executions']:
        ident = item['operationId']; execution = proof('responses', ident, item['nativeReceiptSha256'])
        assert execution['state'] == 'owned_translation_executed_unverified' and execution['returnCode'] == 1
        assert execution['guardPasses'] == 2 and execution['sameContextObservationReads'] == 6 and execution['commandCalls'] == 1
        assert execution['savedFilesUnchanged'] is True and execution['copyFenced'] is True
        plan = proof('ownedexecutions', ident, item['planSha256']); assert plan == execution['plan'] and plan['executionEnabled'] is True
        assert plan['vectorRaw'] == item['vectorWorldRaw'] and plan['operation'] == 'translate_owned_solid' and plan['copyId'] == qualification['copyId']
        proof('ownedoperations', plan['ownedOperationId'], plan['ownedOperationSha256'])
        claim_path = root / 'operationplans' / (plan['operationPlanId'] + '.execution-once.json'); claim = read(claim_path); artifacts.add(claim_path)
        assert claim['operationId'] == ident and claim['planSha256'] == item['planSha256'] and claim['ownedOperationId'] == plan['ownedOperationId'] and claim['automaticReplayForbidden'] is True
        # The transient fence was released; reconstruct its six simple scalar fields and match the native stored SHA.
        fence_record = {'schemaVersion': 1, 'operationId': ident, 'planSha256': item['planSha256'], 'copyId': plan['copyId'],
                        'ownershipSha256': plan['ownershipSha256'], 'automaticReplayForbidden': True}
        fence_bytes = json.dumps(fence_record, ensure_ascii=False, separators=(',', ':')).encode()
        assert hashlib.sha256(fence_bytes).hexdigest() == execution['executionFenceSha256']
        reconstruction = evidence / 'reconstructed-fences' / (ident + '.json'); reconstruction.parent.mkdir(exist_ok=True)
        if reconstruction.exists(): assert reconstruction.read_bytes() == fence_bytes
        else:
            with reconstruction.open('xb') as file: file.write(fence_bytes)
        folder = root / 'operations' / ident; assert not (folder / 'writer.lock').exists()
        previous, events = None, []
        for number, path in enumerate(sorted(folder.glob('*.json'))):
            event = read(path); assert path.name == '%06d.json' % number and event['sequence'] == number and event['previousSha256'] == previous
            assert event['operationId'] == ident and event['planSha256'] == item['planSha256']
            previous = sha(path); events.append(event); artifacts.add(path)
        assert [e['state'] for e in events] == ['prepared', 'execution_claimed', 'command_started', 'command_returned', 'executed_unverified', 'succeeded_verified']
        assert events[-1]['details']['noCommandReplay'] is True
        assert events[3]['details']['returnCode'] == 1
        returned = utc(events[3]['timestampUtc']); proof_ids = []
        for evidence_ref in events[-1]['details']['evidence']:
            data = proof('responses', evidence_ref['requestId'], evidence_ref['sha256']); proof_ids.append(evidence_ref['requestId'])
            if evidence_ref['requestId'] != ident: assert utc(data['timestampUtc']) > returned
        verification = next(n for n in native_by_id.values() if n.get('executionOperationId') == ident and n['state'] == 'owned_translation_verified_observed')
        assert verification['copyFenceReleased'] is True and verification['noCommandReplay'] is True and verification['sameContextObservationReads'] == 4
        after_id = next(e['requestId'] for e in verification['evidence'] if e['requestId'] != ident and read(root / 'responses' / (e['requestId'] + '.json')).get('modelScope') == 'active_document_read_only')
        before = proof('responses', plan['inspectionRequestId'], plan['inspectionSha256']); after = read(root / 'responses' / (after_id + '.json'))
        check_shift(before, after, plan['nativeId'], plan['vectorRaw']); restored_pair.append((before, after))
        for path, expected in execution['savedFileHashesBefore'].items(): assert sha(Path(path)) == expected
    assert observed(restored_pair[0][1]) == observed(restored_pair[1][0])
    assert observed(restored_pair[0][0]) == observed(restored_pair[1][1])
    assert observed(inspections[0]) == observed(inspections[-1]) == observed(read(evidence / 'preflight/resume-20261012-inspection.json'))
    assert listings[0]['windowCount'] == listings[-1]['windowCount'] == 4
    assert window_identity(listings[0]['activeWindow']) == window_identity(listings[-1]['activeWindow'])
    for window in listings[0]['windows']: assert sum(window_identity(window) == window_identity(w) for w in listings[-1]['windows']) == 1
    own_path = root / 'workingcopies' / qualification['copyId'] / 'ownership.json'
    assert sha(own_path) == packets[0]['ownershipSha256']; ownership = read(own_path); artifacts.add(own_path); identities = []
    for field in ('source', 'working', 'checkpoint'):
        entry = ownership[field]; path = Path(entry['path']); ident, links, attributes = file_identity(path)
        assert ident == entry['fileIdentity'] and links == 1 and path.stat().st_size == entry['length']
        assert sha(path) == entry.get('sha256', entry.get('initialSha256'))
        if field == 'checkpoint': assert attributes & 1
        identities.append((ident['volumeSerial'], ident['fileIndex']))
    assert len(set(identities)) == 3 and not (own_path.parent / 'mutation-fence.json').exists()
    proof('responses', qualification['openOperationId'], qualification['openReceiptSha256'])
    claim_path = root / 'workingcopies' / qualification['copyId'] / 'open-once.json'
    previous_claim = evidence.parent / 'P2.30-guard/native-artifacts' / claim_path.relative_to(root)
    assert sha(claim_path) == sha(previous_claim); artifacts.add(claim_path)
    for name, expected in qualification['originalOpenJournalHashes'].items():
        path = root / 'operations' / qualification['openOperationId'] / name; assert sha(path) == expected; artifacts.add(path)
    final = read(evidence / 'final-runtime.json'); assert final['testedSourcesAndStageUnchanged'] is True and final['protectedSavedHashesVerified'] is True
    assert final['component']['version'] == '0.31.0' and final['component']['ownedTranslationsExperimental'] is True
    assert {p.name: sha(p) for p in (root / 'operations' / hardened['executionOperationId']).glob('*.json')} == hardened['originalJournalHashes']
    initial_deployment = read(evidence / 'deployment-initial.json'); current_deployment = read(evidence / 'deployment-hardened.json')
    backup = Path(current_deployment['rollbackDirectory'])
    for entry in initial_deployment['files']: assert sha(backup / entry['name']) == entry['afterSha256']
    for entry in current_deployment['files']: assert sha(Path(entry['destination'])) == entry['afterSha256']
    for entry in final['protectedFiles']: assert sha(Path(entry['path'])) == entry['sha256']
    artifact_hashes = []
    for path in sorted(artifacts):
        relative = path.relative_to(root); destination = evidence / 'native-artifacts' / relative; destination.parent.mkdir(parents=True, exist_ok=True)
        if destination.exists(): assert sha(destination) == sha(path)
        else:
            with destination.open('xb') as file: file.write(path.read_bytes())
        assert sha(destination) == sha(path); artifact_hashes.append({'path': str(relative), 'sha256': sha(path)})
    result = {'state': 'passed_independent_owned_translation_audit', **counts, 'twoDistinctOneUseExecutions': True,
              'independentPostconditionsVerified': True, 'journalsSucceededVerified': True, 'noCommandReplay': True,
              'ownObservedPropertiesRestored': True, 'originalActiveDocumentRestored': True, 'fourDocumentIdentitiesPreserved': True,
              'originalOpenJournalUnchanged': True, 'originalOpenClaimUnchanged': True, 'hardenedVerifierQualifiedWithoutReplay': True,
              'initialAndFinalInstalledBinariesVerified': True, 'copyFenceReleased': True, 'transientFenceRecordsReconstructedAndHashMatched': True,
              'fileIdentitiesAndReadOnlyCheckpointVerified': True, 'protectedSavedFilesUnchanged': True, 'cadConnectionAttempted': False,
              'nativeArtifactHashes': artifact_hashes, 'limitation': 'Saved evidence and observed properties only; no full topology, save/reopen or generic interrupted-command recovery.'}
    (evidence / 'independent-audit.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({k: v for k, v in result.items() if k != 'nativeArtifactHashes'}, ensure_ascii=False))


if __name__ == '__main__': main()
