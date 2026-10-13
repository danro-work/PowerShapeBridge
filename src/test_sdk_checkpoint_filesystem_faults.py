"""Six real native filesystem failures delivered through the official SDK STDIO."""
import argparse
import json
from pathlib import Path

from test_sdk_stdio import Client
from test_sdk_checkpoint_wire_stdio import Panel, inventory, native, read, sha

FAULTS = ('checkpoint_junction', 'intent_junction', 'partial_copy_after_working_write',
          'partial_copy_before_ownership', 'partial_copy_ownership_record', 'partial_copy_after_ownership')


def case(args, fault):
    stage = Path(args.stage).resolve()
    seed = native(stage / 'PSBridge.Worker.exe', '--checkpoint-wire-seed', fault)
    root = Path(seed['root']).resolve()
    assert root.is_relative_to(stage / 'work')
    panel = Panel(root, fault)
    client = Client([args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root), '--timeout', '8'])
    try:
        client.call(1, 'initialize', {'protocolVersion': '2025-11-25', 'capabilities': {},
                    'clientInfo': {'name': 'checkpoint-real-filesystem-fault', 'version': '1'}})
        client.notify('notifications/initialized')
        prepared = client.call(2, 'tools/call', {'name': 'prepare_checkpoint_recovery', 'arguments': {
            'operationId': seed['originId'], 'operationPlanSha256': seed['originPlanSha256']}}, timeout=20)['result']
        assert not prepared.get('isError', False), (prepared, panel.errors)
        intent = prepared['structuredContent']['checkpointRecoveryHandle']
        created = client.call(3, 'tools/call', {'name': 'create_checkpoint_working_copy',
            'arguments': {'checkpointRecoveryHandle': intent}}, timeout=20)['result']
        assert not panel.errors, panel.errors
        assert created['isError'] and 'workingCopyHandle' not in created['structuredContent'], created
        assert 'workingDocumentBindingHandle' not in created['structuredContent']
        expected = 'checkpoint_copy_outcome_unknown' if fault.startswith('partial_') else 'checkpoint_copy_rejected_before_creation'
        assert created['structuredContent']['state'] == expected, created
        assert created['structuredContent']['retryAllowed'] is False
        before_replay = inventory(root)
        replay = client.call(4, 'tools/call', {'name': 'create_checkpoint_working_copy',
            'arguments': {'checkpointRecoveryHandle': intent}})['result']
        assert replay['isError'] and inventory(root) == before_replay
        assert [item['recipe'] for item in panel.calls] == ['prepare_checkpoint_recovery', 'create_checkpoint_working_copy']
        assert read(sorted((root / 'wire-contexts').glob('*.json'))[-1])['commandCalls'] == 0
        assert all(sha(Path(path)) == digest for path, digest in seed['protectedHashes'].items())
        return {'state': 'passed', 'fault': fault, 'root': str(root), 'seed': seed,
                'cadConnectionAttempted': False, 'nativeRequests': panel.calls,
                'created': created, 'replay': replay, 'transcript': client.transcript,
                'artifactHashes': inventory(root)}
    finally:
        client.close()
        panel.close()


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('dotnet', 'host', 'stage', 'evidence'):
        parser.add_argument('--' + name, required=True)
    args = parser.parse_args()
    stage = Path(args.stage).resolve()
    assert (stage / '.checkpoint-recovery-fixture').is_file()
    output = Path(args.evidence).resolve()
    output.mkdir(parents=True, exist_ok=True)
    for fault in FAULTS:
        path = output / (fault + '.json')
        assert not path.exists(), 'Fresh evidence required'
        path.write_text(json.dumps(case(args, fault), indent=2), encoding='utf-8')
        print('PASS: SDK/native filesystem fault ' + fault, flush=True)
