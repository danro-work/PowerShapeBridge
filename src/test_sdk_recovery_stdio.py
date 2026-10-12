"""Independent STDIO checks on isolated opaque files; no panel, worker or CAD."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import uuid

from test_sdk_stdio import Client

parser = argparse.ArgumentParser()
parser.add_argument('--dotnet', required=True)
parser.add_argument('--host', required=True)
parser.add_argument('--evidence', required=True)
args = parser.parse_args()
DOTNET = args.dotnet
HOST = Path(args.host).resolve()
EVIDENCE = Path(args.evidence).resolve() / 'recovery-stdio'
EVIDENCE.mkdir(parents=True, exist_ok=True)
ROOT = EVIDENCE / ('fixture-' + uuid.uuid4().hex)
ROOT.mkdir()

def write(relative, value):
    path = ROOT / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    raw = json.dumps(value, ensure_ascii=False).encode()
    path.write_bytes(raw)
    return hashlib.sha256(raw).hexdigest()

write('bridge.config.json', {'coreProfile': 'general-v1', 'expectedVersion': '23.2.2',
    'timeoutSeconds': 20, 'operationTimeoutSeconds': 80, 'maxEntities': 10000,
    'captureViewport': False, 'recoveryInspectionExperimental': True})
cases = []
for last, assessment in [(2, 'started_without_authoritative_return'),
                         (3, 'returned_requires_independent_verification'),
                         (5, 'historically_verified_requires_fresh_context')]:
    operation, copy = uuid.uuid4().hex, uuid.uuid4().hex
    working = ROOT / f'workingcopies/{copy}/Working_{copy}.psmodel'
    checkpoint = ROOT / f'workingcopies/{copy}/checkpoint/Before.psmodel'
    for path in [working, checkpoint]:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b'opaque synthetic saved bytes; never a CAD model')
    os.chmod(checkpoint, 0o444)
    sha = hashlib.sha256(working.read_bytes()).hexdigest()
    ownership = write(f'workingcopies/{copy}/ownership.json', {
        'schemaVersion': 2, 'copyId': copy, 'bridgeVersion': '0.33.0-dev',
        'createdUtc': '2026-10-12T10:00:00Z', 'scope': 'saved_disk_bytes_only',
        'executionEnabled': False, 'source': {'path': 'C:/NeverReadThis.psmodel'},
        'working': {'path': str(working), 'initialSha256': sha},
        'checkpoint': {'path': str(checkpoint), 'sha256': sha}})
    plan = write(f'copyopenplans/{operation}.json', {'schemaVersion': 1,
        'operationId': operation, 'operation': 'open_working_copy',
        'copyId': copy, 'ownershipSha256': ownership})
    write(f'workingcopies/{copy}/open-once.json', {'operationId': operation,
        'planSha256': plan, 'copyId': copy, 'ownershipSha256': ownership,
        'automaticReplayForbidden': True})
    evidence_id = uuid.uuid4().hex
    evidence_sha = write(f'responses/{evidence_id}.json', {'state': 'opaque_evidence'})
    previous = None
    phases = ['prepared', 'execution_claimed', 'command_started', 'command_returned',
              'executed_unverified', 'succeeded_verified']
    for sequence in range(last + 1):
        details = {'nativeRequestId': operation} if sequence == 3 else (
            {'evidence': [{'requestId': evidence_id, 'sha256': evidence_sha}]} if sequence == 5 else {})
        previous = write(f'operations/{operation}/{sequence:06}.json', {
            'schemaVersion': 1, 'operationId': operation, 'planSha256': plan,
            'sequence': sequence, 'previousSha256': previous, 'state': phases[sequence],
            'timestampUtc': '2026-10-12T10:00:00Z', 'details': details})
    cases.append((operation, assessment))

def inventory():
    return {str(p.relative_to(ROOT)): {'sha256': hashlib.sha256(p.read_bytes()).hexdigest(),
            'mode': p.stat().st_mode} for p in ROOT.rglob('*') if p.is_file()}

before = inventory()
results = []
for read_only in [False, True]:
    command = [DOTNET, str(HOST), '--bridge-root', str(ROOT)] + (['--read-only'] if read_only else [])
    client = Client(command)
    try:
        client.call(1, 'initialize', {'protocolVersion': '2025-11-25', 'capabilities': {},
            'clientInfo': {'name': 'independent-recovery-reader', 'version': '1'}})
        client.notify('notifications/initialized')
        catalog = client.call(2, 'tools/list')['result']['tools']
        tool = next(t for t in catalog if t['name'] == 'inspect_operation_recovery')
        assert tool['annotations']['readOnlyHint'] is True
        for identity, (operation, assessment) in enumerate(cases, 10):
            result = client.call(identity, 'tools/call', {'name': tool['name'],
                'arguments': {'operationId': operation}})['result']
            assert not result.get('isError', False), result
            data = result['structuredContent']
            assert data['assessment'] == assessment, data
            assert data['outcome'] == 'not_resolved_by_recorded_inspection'
            for flag in ['retryAllowed', 'executionAuthorized', 'cadConnectionAttempted',
                         'artifactWritesAttempted', 'geometryMutationAttempted']:
                assert data[flag] is False, (flag, data)
            results.append({'readOnly': read_only, 'result': data})
        bad = client.call(30, 'tools/call', {'name': tool['name'],
            'arguments': {'operationId': cases[0][0], 'command': 'UNDO'}})
        assert 'error' in bad, bad
    finally:
        client.close()
    assert inventory() == before
    assert not (ROOT / 'requests').exists()
assert inventory() == before
(EVIDENCE / 'stdio-reader.json').write_text(json.dumps({'state': 'passed',
    'cases': len(results), 'artifactsUnchanged': True, 'noQueueCreated': True,
    'cadConnectionAttempted': False, 'hostSha256': hashlib.sha256(HOST.read_bytes()).hexdigest(),
    'results': results}, ensure_ascii=False, indent=2), encoding='utf-8')
print('PASS: six independent recovery STDIO cases; no artifact changes or CAD queue.')
