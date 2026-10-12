"""Explicit K5 pilot: NEW owned copy, one open, one SOLID translation, verify.

Stops on any unknown outcome. No retry, inverse, save, close, screenshot or
automatic restoration after failure. Leaves the known owned test copy open.
"""
import argparse
import datetime
import hashlib
import json
import re
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'src'))
from test_sdk_stdio import Client
from test_sdk_readonly import init
from qualify_owned_open_recovery import window_identity


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def entities(value):
    return [{k: v for k, v in item.items() if k not in {'entityReference', 'candidateEntityHandle', 'entitySetSupported'}}
            for item in value['entities']]


def owned_working_path(root, working):
    assert re.fullmatch('[0-9a-f]{32}', working['copyId'])
    folder = root / 'workingcopies' / working['copyId']
    assert sha(folder / 'ownership.json') == working['ownershipSha256']
    schema = working['ownership']['schemaVersion']
    assert schema in (1, 2)
    expected = folder / ('Working.psmodel' if schema == 1 else 'Working_' + working['copyId'] + '.psmodel')
    assert Path(working['ownership']['working']['path']) == expected
    assert sha(expected) == working['ownership']['working']['initialSha256']
    assert sha(folder / 'checkpoint/Before.psmodel') == working['ownership']['checkpoint']['sha256']
    return expected


def run(args):
    root, output = Path(args.bridge_root).resolve(), Path(args.output).resolve()
    source = Path(args.source).resolve()
    expected_active = Path(args.expected_active_path).resolve()
    assert args.allow_new_owned_scenario, 'Explicit new-owned scenario authorization is required.'
    assert not output.exists(), 'Evidence directory must be new; never rerun an interrupted scenario.'
    assert sha(source) == args.source_sha256 and source.suffix.lower() == '.psmodel'
    assert source != expected_active and sha(expected_active) == args.source_sha256
    config = json.loads((root / 'bridge.config.json').read_text(encoding='utf-8-sig'))
    assert config.get('coreProfile') == 'general-v1' and config.get('captureViewport') is False
    assert config.get('workingCopyOpenExperimental') is True and config.get('ownedTranslationsExperimental') is True
    assert not (root / 'maintenance.json').exists()
    assert not [p for p in (root / 'requests').iterdir() if p.name.endswith(('.json', '.processing', '.tmp'))]
    panel = json.loads((root / 'reports/panel-status.json').read_text(encoding='utf-8-sig'))
    age = (datetime.datetime.now(datetime.timezone.utc) - datetime.datetime.fromisoformat(panel['timestampUtc'].replace('Z', '+00:00'))).total_seconds()
    assert 0 <= age < 30 and panel['workerIdle'] and panel['acceptingRequests'] and panel['processId'] == args.panel_pid
    output.mkdir(parents=True)
    old_open_plans = {p.name: sha(p) for p in (root / 'copyopenplans').glob('*.json')}
    old_open_journals = {str(p.relative_to(root)): sha(p) for plan in old_open_plans
                         for p in (root / 'operations' / Path(plan).stem).glob('*.json')}
    records, clients = [], []
    summary = dict(state='incomplete', sourcePath=str(source), originalActivePath=str(expected_active), sourceSha256=args.source_sha256,
        hostSha256=sha(Path(args.host)), records=records, geometryMutationAttempted=False,
        cadSaveAttempted=False, cadCloseAttempted=False, screenshotRequested=False,
        automaticRetryAttempted=False, inverseAttempted=False, vectorWorldRaw=[1, 0, 0])

    def connect(read_only=False):
        command = [args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root), '--timeout', '90']
        if read_only:
            command.append('--read-only')
        client = Client(command)
        clients.append(client)
        init(client)
        return client

    def call(client, name, values=None, expected=None):
        number = len(records) + 10
        if name in ('open_working_copy', 'execute_owned_translation'):
            summary['lastCommandRequested'] = name
            summary['lastCommandOutcome'] = 'unknown_until_receipt_and_verification'
            if name == 'execute_owned_translation':
                summary['geometryMutationAttempted'] = True
            (output / 'qualification.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
        # Client.call consumes notifications and waits for this exact request ID.
        response = client.call(number, 'tools/call', {'name': name, 'arguments': values or {}}, timeout=100)
        path = output / ('%02d-%s.json' % (len(records) + 1, name))
        path.write_text(json.dumps(response, ensure_ascii=False, indent=2), encoding='utf-8')
        value = response.get('result', {}).get('structuredContent', response.get('error', {}))
        record = dict(tool=name, state=value.get('state'), path=str(path))
        records.append(record)
        request = value.get('bridgeRequestId')
        if request and (root / 'responses' / (request + '.json')).exists():
            record['bridgeRequestId'] = request
            record['nativeReceiptSha256'] = sha(root / 'responses' / (request + '.json'))
        if name in ('open_working_copy', 'execute_owned_translation'):
            summary[name + 'RequestId'] = request
        if value.get('geometryMutationAttempted'):
            summary['geometryMutationAttempted'] = True
        if 'error' in response or response['result'].get('isError') or expected and value.get('state') != expected:
            summary.update(failedTool=name, failedBridgeRequestId=request)
            raise RuntimeError('Stop without replay/restoration: ' + json.dumps(value, ensure_ascii=False))
        return value

    def own_read(client, working_path):
        candidate = call(client, 'inspect_entity_candidates', expected='entity_candidates_inspected')
        listing = call(client, 'list_documents', expected='documents_listed_verified')
        active = next(d for d in listing['documents'] if d['isActive'])
        assert Path(candidate['model']['path']) == Path(working_path) == Path(active['path'])
        assert candidate['processId'] == args.cad_pid
        return candidate, active

    try:
        client, readonly = connect(), connect(True)
        assert len(client.call(2, 'tools/list')['result']['tools']) == 21
        assert len(readonly.call(2, 'tools/list')['result']['tools']) == 6
        status = call(client, 'bridge_status')
        assert status['adapterVersion'] == '0.35.1-dev' and status['lastPanelReport']['version'] == '0.32.0'
        before = call(readonly, 'inspect_active_model', expected='active_model_inspected')
        listing = call(client, 'list_documents', expected='documents_listed_verified')
        original = next(d for d in listing['documents'] if d['isActive'])
        assert Path(original['path']) == expected_active and before['processId'] == args.cad_pid
        saved = {str(Path(d['path'])): sha(Path(d['path'])) for d in listing['documents'] if d.get('path')}
        candidate = call(client, 'inspect_entity_candidates', expected='entity_candidates_inspected')
        assert Path(candidate['model']['path']) == expected_active and candidate['entityCount'] == 1
        assert candidate['entities'][0]['nativeTypeCode'] == 40
        working = call(client, 'prepare_working_copy', dict(sourcePath=str(source), sourceSha256=args.source_sha256), 'working_copy_prepared_disk')
        working_path = owned_working_path(root, working)
        summary.update(copyId=working['copyId'], workingPath=str(working_path))
        assert working_path != source and working_path.is_relative_to(root / 'workingcopies' / working['copyId'])
        saved_working_sha = sha(working_path)
        # Original choice and candidate must still be fresh, then native guards
        # recheck them immediately before its single open command.
        opened = call(client, 'open_working_copy', dict(workingCopyHandle=working['workingCopyHandle'],
            candidateDocumentHandle=candidate['candidateDocumentHandle'], activeDocumentHandle=original['documentHandle']), 'working_copy_opened_observed')
        open_sha = sha(root / 'responses' / (opened['operationId'] + '.json'))
        own, active = own_read(client, working_path)
        verified_open = call(client, 'verify_working_copy_open', dict(openRequestId=opened['operationId'], openReceiptSha256=open_sha,
            candidateDocumentHandle=own['candidateDocumentHandle'], activeDocumentHandle=active['documentHandle']), 'owned_open_verified_observed')
        handle = verified_open['workingCopyHandle']
        summary.update(openOperationId=opened['operationId'], openReceiptSha256=open_sha)
        independent_before = call(readonly, 'inspect_active_model', expected='active_model_inspected')
        assert entities(independent_before) == entities(own) == entities(before)
        assert len(own['entities']) == 1 and own['entities'][0]['geometry']['lengthUnitsRaw'] == 'mm'
        binding = call(client, 'bind_working_document', dict(candidateDocumentHandle=own['candidateDocumentHandle'], workingCopyHandle=handle), 'working_document_bound_observed')
        entity_set = call(client, 'prepare_entity_set', dict(candidateDocumentHandle=own['candidateDocumentHandle'],
            candidateEntityHandles=[own['entities'][0]['candidateEntityHandle']]), 'entity_set_prepared_observed')
        plan = call(client, 'prepare_operation_plan', dict(operation='translate_entities', entitySetHandles=[entity_set['entitySetHandle']],
            parameters=dict(vector=[1, 0, 0], coordinateFrame='world', lengthUnits='document_raw', mode='move')), 'operation_plan_prepared_local')
        packet = call(client, 'prepare_owned_operation', dict(operationPlanHandle=plan['operationPlanHandle'],
            workingDocumentBindingHandle=binding['workingDocumentBindingHandle']), 'owned_operation_prepared_observed')
        execution = call(client, 'execute_owned_translation', dict(ownedOperationHandle=packet['ownedOperationHandle']), 'owned_translation_executed_unverified')
        assert execution['commandCalls'] == 1 and execution['copyFenced'] is True and execution['returnCode'] == 1
        fence = root / 'workingcopies' / working['copyId'] / 'mutation-fence.json'
        assert fence.exists()
        execution_sha = sha(root / 'responses' / (execution['operationId'] + '.json'))
        summary.update(executionOperationId=execution['operationId'], executionReceiptSha256=execution_sha)
        own_after, active = own_read(client, working_path)
        verified = call(client, 'verify_owned_translation', dict(executionRequestId=execution['operationId'], executionReceiptSha256=execution_sha,
            candidateDocumentHandle=own_after['candidateDocumentHandle'], activeDocumentHandle=active['documentHandle']), 'owned_translation_verified_observed')
        assert verified['copyFenceReleased'] and verified['noCommandReplay'] and not fence.exists()
        journal = call(readonly, 'operation_result', dict(operationId=execution['operationId']), 'operation_receipt_read')
        assert journal['receipt']['recordedState'] == 'succeeded_verified' and journal['receipt']['eventCount'] == 6 and not journal['receipt']['retryAllowed']
        independent_after = call(readonly, 'inspect_active_model', expected='active_model_inspected')
        assert entities(independent_after) == entities(own_after)
        first, last = entities(independent_before)[0], entities(independent_after)[0]
        assert {k: v for k, v in first.items() if k != 'geometry'} == {k: v for k, v in last.items() if k != 'geometry'}
        assert {k: v for k, v in first['geometry'].items() if k != 'cogRaw'} == {k: v for k, v in last['geometry'].items() if k != 'cogRaw'}
        assert last['geometry']['cogRaw'] == [first['geometry']['cogRaw'][0] + 1, *first['geometry']['cogRaw'][1:]]
        inspected = call(client, 'inspect_working_copy', dict(workingCopyHandle=handle), 'working_copy_inspected_disk')
        assert inspected['checkpointVerified'] and not inspected['workingFileChanged'] and sha(working_path) == saved_working_sha
        choices = call(client, 'list_documents', expected='documents_listed_verified')
        restore = next(d for d in choices['documents'] if d['modelId'] == original['modelId'] and Path(d['path']) == expected_active)
        call(client, 'activate_document', dict(documentHandle=restore['documentHandle']), 'document_active_verified')
        restored = call(readonly, 'inspect_active_model', expected='active_model_inspected')
        assert entities(restored) == entities(before) and Path(restored['model']['path']) == expected_active
        final = call(client, 'list_documents', expected='documents_listed_verified')
        assert final['windowCount'] == listing['windowCount'] + 1
        for old in listing['windows']:
            assert sum(window_identity(old) == window_identity(new) for new in final['windows']) == 1
        assert all(sha(Path(path)) == expected for path, expected in saved.items())
        assert sha(source) == args.source_sha256
        assert all(sha(root / 'copyopenplans' / name) == expected for name, expected in old_open_plans.items())
        assert all(sha(root / relative) == expected for relative, expected in old_open_journals.items())
        summary.update(state='passed_new_owned_sdk_scenario', commandCalls=1, openCommandCalls=1,
            lastCommandOutcome='independently_verified',
            independentVerificationPassed=True, originalActiveDocumentRestored=True, oldOpenArtifactsUnchanged=True,
            savedOriginalFilesUnchanged=True, savedWorkingBytesUnchanged=True, checkpointVerified=True,
            ownedCopyLeftOpen=True, ownedCopyHasKnownUnsavedTranslation=True)
    finally:
        for client in clients:
            client.close()
        (output / 'qualification.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
    print('PASS: new owned SDK open/verify and one SOLID translation/verify; original document restored; no save/close/replay/inverse.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('dotnet', 'host', 'bridge-root', 'output', 'source', 'source-sha256', 'expected-active-path'):
        parser.add_argument('--' + name, required=True)
    parser.add_argument('--panel-pid', type=int, required=True)
    parser.add_argument('--cad-pid', type=int, required=True)
    parser.add_argument('--allow-new-owned-scenario', action='store_true')
    run(parser.parse_args())
