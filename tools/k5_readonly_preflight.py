"""Explicit passive SDK pilot on the existing panel; never starts a CAD worker."""
import argparse
import datetime
import hashlib
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'src'))
from test_sdk_stdio import Client
from test_sdk_readonly import init


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(args):
    root = Path(args.bridge_root).resolve()
    evidence = Path(args.evidence).resolve()
    evidence.mkdir(parents=True, exist_ok=True)
    config = json.loads((root / 'bridge.config.json').read_text(encoding='utf-8-sig'))
    assert config['coreProfile'] == 'general-v1' and config.get('captureViewport') is False
    deployment = json.loads((root / 'reports/deployment-current.json').read_text(encoding='utf-8-sig'))
    for name in ('PSBridge.exe', 'PSBridge.Worker.exe', 'bridge.config.json'):
        expected = next(item['afterSha256'] for item in deployment['files'] if item['name'] == name)
        assert sha(root / name) == expected, 'Installed deployment differs: ' + name
    panel = json.loads((root / 'reports/panel-status.json').read_text(encoding='utf-8-sig'))
    stamp = datetime.datetime.fromisoformat(panel['timestampUtc'].replace('Z', '+00:00'))
    age = (datetime.datetime.now(datetime.timezone.utc) - stamp).total_seconds()
    assert 0 <= age < 30 and panel['processId'] == args.panel_pid and panel['workerIdle'] and panel['acceptingRequests']
    pending = [path.name for path in (root / 'requests').iterdir() if path.name.endswith(('.json', '.processing', '.tmp'))]
    assert not pending and not (root / 'maintenance.json').exists(), pending
    client = Client([args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root), '--read-only', '--timeout', '90'])
    results = {}
    try:
        init(client)
        tools = client.call(2, 'tools/list')['result']['tools']
        assert len(tools) == 6 and all(tool['annotations']['readOnlyHint'] for tool in tools)
        for identity, name in enumerate(('bridge_status', 'list_documents', 'inspect_active_model', 'inspect_entity_candidates'), 3):
            value = client.call(identity, 'tools/call', {'name': name, 'arguments': {}})['result']
            assert not value['isError'], value
            results[name] = value['structuredContent']
        observed = results['inspect_active_model']
        assert observed['processId'] == args.cad_pid and observed['productVersion'] == '23.2.2'
        assert results['bridge_status']['geometryMutations'] == 'disabled'
    finally:
        client.close()
        (evidence / 'readonly-live-transcript.json').write_text(json.dumps(client.transcript, ensure_ascii=False, indent=2), encoding='utf-8')
        (evidence / 'readonly-live-stderr.txt').write_text(''.join(client.logs), encoding='utf-8')
    report = dict(state='passed', adapterVersion=results['bridge_status']['adapterVersion'],
        nativeVersion=panel['version'], panelPid=args.panel_pid, cadPid=args.cad_pid,
        geometryMutationAttempted=False, contextChangeAttempted=False, cadSaveAttempted=False,
        screenshotRequested=False, workerStartedByClient=False, hostSha256=sha(Path(args.host)), results=results)
    (evidence / 'readonly-live-preflight.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print('PASS: fresh SDK read-only pilot on installed panel; no activation/open/save/geometry/screenshot.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('dotnet', 'host', 'bridge-root', 'evidence'):
        parser.add_argument('--' + name, required=True)
    parser.add_argument('--panel-pid', type=int, required=True)
    parser.add_argument('--cad-pid', type=int, required=True)
    run(parser.parse_args())
