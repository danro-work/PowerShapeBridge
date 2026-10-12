"""Independent passive evidence audit. No CAD connection or queue submission."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import ctypes
from ctypes import wintypes
import os


def file_identity(path):
    """Independent native file metadata read, without the Bridge ownership helper."""
    class Time(ctypes.Structure):
        _fields_ = [('low', wintypes.DWORD), ('high', wintypes.DWORD)]
    class Info(ctypes.Structure):
        _fields_ = [('attributes', wintypes.DWORD), ('creation', Time), ('access', Time), ('write', Time),
                    ('volume', wintypes.DWORD), ('sizeHigh', wintypes.DWORD), ('sizeLow', wintypes.DWORD),
                    ('links', wintypes.DWORD), ('indexHigh', wintypes.DWORD), ('indexLow', wintypes.DWORD)]
    api = ctypes.WinDLL('kernel32', use_last_error=True)
    api.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
    api.CreateFileW.restype = wintypes.HANDLE
    api.GetFileInformationByHandle.argtypes = [wintypes.HANDLE, ctypes.POINTER(Info)]
    api.GetFinalPathNameByHandleW.argtypes = [wintypes.HANDLE, wintypes.LPWSTR, wintypes.DWORD, wintypes.DWORD]
    api.CloseHandle.argtypes = [wintypes.HANDLE]
    handle = api.CreateFileW(str(path), 0x80000000, 7, None, 3, 0, None)
    if handle == ctypes.c_void_p(-1).value: raise ctypes.WinError(ctypes.get_last_error())
    try:
        info = Info()
        if not api.GetFileInformationByHandle(handle, ctypes.byref(info)): raise ctypes.WinError(ctypes.get_last_error())
        final = ctypes.create_unicode_buffer(32768)
        length = api.GetFinalPathNameByHandleW(handle, final, len(final), 0)
        assert 0 < length < len(final)
        assert os.path.normcase(final.value.removeprefix('\\\\?\\')) == os.path.normcase(str(path))
        return {'volumeSerial': '%08x' % info.volume, 'fileIndex': '%08x%08x' % (info.indexHigh, info.indexLow),
                'creationTime': '%08x%08x' % (info.creation.high, info.creation.low)}, info.links, info.attributes
    finally:
        api.CloseHandle(handle)


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--evidence', required=True, type=Path)
    parser.add_argument('--before', required=True, type=Path)
    parser.add_argument('--expected-version', default='0.25.0')
    options = parser.parse_args()
    evidence = options.evidence.resolve()
    qualification = read(evidence / 'qualification.json')
    root = Path(qualification['root'])
    assert qualification['state'] == 'passed_passive_core_mcp'
    assert qualification['transport'] == 'panel'
    native = {}
    failed_local = 0
    successful_local = 0
    operation_plan = None
    working_copy = None
    copy_responses = 0
    set_plan = None
    for record in qualification['records']:
        message = read(Path(record['path']))
        if 'error' in message:
            assert message['error']['code'] == -32602
            failed_local += 1
            continue
        result = message['result']
        delivered = result['structuredContent']
        request_id = delivered.get('bridgeRequestId')
        if request_id is None:
            if delivered['state'] in {'entity_set_rejected', 'operation_plan_rejected', 'working_copy_rejected', 'working_document_binding_rejected'}:
                assert result['isError']; failed_local += 1
                continue
            assert qualification.get('localOperationPlanTested') and result['isError'] is False
            assert record['tool'] in {'prepare_operation_plan', 'inspect_operation_plan', 'operation_result'}
            operation_id = delivered['operationId']
            assert re.fullmatch('[0-9a-f]{32}', operation_id)
            plan_path = root / 'operationplans' / (operation_id + '.json')
            local_plan = read(plan_path)
            assert local_plan['operationId'] == operation_id and local_plan['executionEnabled'] is False
            assert local_plan['operation'] == 'translate_entities'
            assert local_plan['parameters'] == {'vector': [1, 0, 0], 'coordinateFrame': 'world', 'lengthUnits': 'document_raw', 'mode': 'move'}
            assert set_plan and local_plan['entitySets'][0]['entityKeys'] == set_plan['entityKeys']
            for entity_set in local_plan['entitySets']:
                set_path = root / 'entitysets' / (entity_set['entitySetId'] + '.json')
                assert sha(set_path) == entity_set['entitySetSha256']
                set_data = read(set_path)
                for field, value in set_data.items(): assert entity_set[field] == value
                assert sha(root / 'responses' / (set_data['inspectionRequestId'] + '.json')) == set_data['inspectionSha256']
            event_path = root / 'operations' / operation_id / '000000.json'
            event = read(event_path)
            assert event['operationId'] == operation_id and event['planSha256'] == sha(plan_path)
            assert event['sequence'] == 0 and event['previousSha256'] is None and event['state'] == 'prepared'
            assert event['details'] == {'cadMutationAttempted': False, 'executionEnabled': False}
            assert list((root / 'operations' / operation_id).iterdir()) == [event_path]
            if record['tool'] == 'operation_result':
                journal = delivered['receipt']
                assert delivered['receiptSha256'] == sha(event_path)
            else:
                assert delivered['plan'] == local_plan and delivered['planSha256'] == sha(plan_path)
                assert delivered['cadMutationAttempted'] is False
                journal = delivered.get('journal')
            if journal:
                assert journal['events'] == [event] and journal['journalSha256'] == sha(event_path)
                assert journal['state'] == journal['recordedState'] == 'prepared' and journal['retryAllowed'] is False
            if operation_plan is not None: assert operation_plan == local_plan
            operation_plan = local_plan
            successful_local += 1
            continue
        assert re.fullmatch('[0-9a-f]{32}', request_id)
        receipt_path = root / 'responses' / (request_id + '.json')
        original = read(receipt_path)
        envelope = read(root / 'requests' / (request_id + '.json.processing.done'))
        assert envelope['operation'] == 'recipe.prepare' and envelope['requestId'] == request_id
        recipe = envelope['arguments']['recipeId']
        assert recipe in {'list_documents', 'inspect_active_model', 'prepare_entity_set', 'validate_entity_set', 'prepare_working_copy', 'inspect_working_copy', 'bind_working_document'}
        assert original.get('bridgeVersion') == options.expected_version
        if original['state'] == 'working_copy_rejected':
            assert recipe == 'prepare_working_copy' and result['isError'] and original['errorCode'] == 'file_busy'
        elif recipe == 'bind_working_document':
            assert qualification.get('unownedActiveDocumentBindingRefusalTested')
            assert original['state'] == 'working_document_binding_rejected' and result['isError']
            assert original['errorCode'] == 'active_document_not_owned_working_copy' and original['sameContextObservationReads'] == 2
            assert original['contextChangeAttempted'] is False and original['executionEnabled'] is False and original['powerShapeConnectionAttempted'] is True
            parameters = envelope['arguments']['parameters']
            assert set(parameters) == {'copyId', 'ownershipSha256', 'inspectionRequestId', 'inspectionSha256', 'expiresUtc'}
            source_path = root / 'responses' / (parameters['inspectionRequestId'] + '.json')
            assert sha(source_path) == parameters['inspectionSha256']
            source_observation = read(source_path)
            assert original['observedModel'] == source_observation['model']
            assert original['observedPropertiesSha256'] == next(r['observedPropertiesSha256'] for r in native.values() if r['state'] == 'entity_set_validated_observed')
            source_windows = next(r for r in native.values() if r['state'] == 'documents_listed_verified')
            window_values = [source_windows[k] for k in ('processId', 'processStartedUtc', 'windowsSessionId', 'windowCount', 'windows', 'activeWindow')]
            assert original['windowContextSha256'] == hashlib.sha256(json.dumps(window_values, sort_keys=True, ensure_ascii=False, separators=(',', ':')).encode()).hexdigest()
            assert not (root / 'documentbindings' / (request_id + '.json')).exists()
            assert 'workingDocumentBindingHandle' not in delivered
        else:
            assert result['isError'] is False
        adapted = dict(delivered)
        if recipe == 'list_documents':
            adapted['documents'] = [{k: v for k, v in document.items() if k != 'documentHandle'} for document in adapted['documents']]
        if recipe == 'inspect_active_model':
            adapted['state'] = 'succeeded_verified'
            adapted['entities'] = [{k: v for k, v in entity.items() if k not in {'candidateEntityHandle', 'entitySetSupported', 'entityReference'}} for entity in adapted['entities']]
        for key, value in original.items():
            if key == 'entitySetQualification': continue  # adapter adds its separate scope statement
            assert key in adapted and value == adapted[key], (record['tool'], key)
        assert original.get('cadMutationAttempted', False) is False
        native[request_id] = original
        if recipe in {'prepare_working_copy', 'inspect_working_copy'}:
            assert original['powerShapeConnectionAttempted'] is False
            copy_responses += 1
            if original['state'] == 'working_copy_rejected':
                assert not (root / 'workingcopies' / request_id).exists()
                assert not (root / 'workingcopies' / (request_id + '.reservation.json')).exists()
                continue
            copy_id, ownership = original['copyId'], original['ownership']
            folder = root / 'workingcopies' / copy_id
            assert sha(folder / 'ownership.json') == original['ownershipSha256']
            assert read(folder / 'ownership.json') == ownership and ownership['executionEnabled'] is False
            assert ownership['scope'] == 'saved_disk_bytes_only' and ownership['copyId'] == copy_id
            assert Path(ownership['working']['path']) == folder / 'Working.psmodel'
            assert Path(ownership['checkpoint']['path']) == folder / 'checkpoint' / 'Before.psmodel'
            identities = []
            for role in ('source', 'working', 'checkpoint'):
                item = ownership[role]; path = Path(item['path'])
                expected_hash = item['initialSha256'] if role == 'working' else item['sha256']
                assert sha(path) == expected_hash == ownership['source']['sha256']
                assert path.stat().st_size == item['length']
                actual_identity, links, attributes = file_identity(path)
                assert actual_identity == item['fileIdentity']
                assert not attributes & 0x400
                if role != 'source': assert links == 1
                if role == 'checkpoint': assert attributes & 1
                identities.append((actual_identity['volumeSerial'], actual_identity['fileIndex']))
            assert len(set(identities)) == 3
            if recipe == 'prepare_working_copy':
                assert envelope['arguments']['parameters']['sourceSha256'] == ownership['source']['sha256']
                assert Path(envelope['arguments']['parameters']['sourcePath']) == Path(ownership['source']['path'])
                assert request_id == copy_id
                working_copy = ownership
            else:
                assert ownership == working_copy and original['checkpointVerified'] and original['workingFileChanged'] is False
        if recipe == 'prepare_entity_set':
            plan_path = root / 'entitysets' / (delivered['entitySetId'] + '.json')
            assert sha(plan_path) == delivered['entitySetSha256']
            set_plan = read(plan_path)
            assert set_plan == envelope['arguments']['parameters']
            source = root / 'responses' / (set_plan['inspectionRequestId'] + '.json')
            assert sha(source) == set_plan['inspectionSha256']
            assert set_plan['entityKeys'] == [[e['nativeTypeCode'], e['nativeId']] for e in delivered['entities']]
        if recipe == 'validate_entity_set':
            assert original['state'] == 'entity_set_validated_observed'
    assert copy_responses == (2 if qualification.get('savedWorkingCopyTested') else 0) + (1 if qualification.get('busySourceRejectionTested') else 0)
    binding_tested = qualification.get('unownedActiveDocumentBindingRefusalTested', False)
    assert len(native) == 6 + copy_responses + int(binding_tested) and failed_local == (7 if qualification.get('localOperationPlanTested') else 4) + (3 if qualification.get('savedWorkingCopyTested') else 0) + (4 if binding_tested else 0) and set_plan
    assert successful_local == (4 if qualification.get('localOperationPlanTested') else 0)
    inspections = [item for item in native.values() if item['state'] == 'succeeded_verified']
    before = read(options.before)
    fields = ('processId', 'processStartedUtc', 'windowsSessionId', 'model', 'entityCount', 'entities',
              'levelsContainingEntities', 'contextBefore', 'contextAfter', 'selectionBefore', 'selectionAfter',
              'workplanes', 'lengthUnitsRaw', 'lengthUnitNameRaw', 'lengthUnitFactorRaw', 'areaUnitNameRaw',
              'areaUnitFactorRaw', 'volumeUnitNameRaw', 'volumeUnitFactorRaw', 'generalToleranceRaw')
    for observation in inspections:
        for field in fields: assert before[field] == observation[field], ('before/after observed property', field)
    if operation_plan:
        for field, value in operation_plan['sourceIdentity'].items(): assert inspections[0][field] == value
    documents = [item for item in native.values() if item['state'] == 'documents_listed_verified']
    for field in ('processId', 'processStartedUtc', 'windowsSessionId', 'windows', 'activeWindow'):
        assert documents[0][field] == documents[-1][field]
    report = {'state': 'passed_independent_passive_audit', 'nativeResponsesVerified': len(native),
              'localRejectionsVerified': failed_local, 'setAndSourceReceiptHashesVerified': True,
              'successfulLocalPlanCallsVerified': successful_local,
              'operationPlanAndPreparedJournalVerified': bool(operation_plan), 'expectedVersion': options.expected_version,
              'workingCopyAndIndependentCheckpointVerified': bool(working_copy), 'copyFileResponsesVerified': copy_responses,
              'unownedActiveDocumentBindingRefusalVerified': bool(binding_tested), 'successfulLiveDocumentBindingQualified': False,
              'observedPropertiesBeforeAfterMatch': True, 'windowContextBeforeAfterMatch': True,
              'qualificationSha256': sha(evidence / 'qualification.json'), 'cadMutationAcceptance': False,
              'qualification': 'Recorded observations and artifacts only; no full topological or persistent-identity certification.'}
    (evidence / 'independent-audit.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print('PASS: native responses, local refusals, optional plan/journal, artifact hashes and recorded observations before/after.')


if __name__ == '__main__': main()
