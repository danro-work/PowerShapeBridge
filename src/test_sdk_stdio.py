"""Independent SDK STDIO/legacy-journal parity checks; never launch CAD or a worker."""
import argparse
import hashlib
import json
from pathlib import Path
import queue
import subprocess
import threading
import uuid

from mcp_server import Server


class Client:
    def __init__(self, command):
        self.process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        self.frames = queue.Queue()
        self.transcript = []
        self.logs = []
        def read():
            for raw in self.process.stdout:
                try:
                    value = json.loads(raw)
                    assert value['jsonrpc'] == '2.0'
                    self.transcript.append(value)
                    self.frames.put(value)
                except Exception as error:
                    self.frames.put(error)
        def stderr():
            for raw in self.process.stderr:
                self.logs.append(raw.decode('utf-8', errors='replace'))
        self.stdout_thread = threading.Thread(target=read, daemon=True)
        self.stderr_thread = threading.Thread(target=stderr, daemon=True)
        self.stdout_thread.start()
        self.stderr_thread.start()

    def call(self, identity, method, params=None):
        data = {'jsonrpc': '2.0', 'id': identity, 'method': method}
        if params is not None:
            data['params'] = params
        self.process.stdin.write(json.dumps(data).encode('utf-8') + b'\n')
        self.process.stdin.flush()
        for _ in range(32):
            result = self.frames.get(timeout=10)
            if isinstance(result, Exception):
                raise result
            if result.get('id') == identity:
                return result
        raise AssertionError('No matching response')

    def notify(self, method):
        self.process.stdin.write(json.dumps({'jsonrpc': '2.0', 'method': method}).encode() + b'\n')
        self.process.stdin.flush()

    def close(self):
        self.process.stdin.close()
        self.process.wait(timeout=10)
        self.stdout_thread.join(timeout=3)
        self.stderr_thread.join(timeout=3)
        assert self.process.returncode == 0, ''.join(self.logs)


def checks(args):
    root = Path(args.evidence).resolve() / ('stdio-fixture-' + uuid.uuid4().hex)
    root.mkdir(parents=True)
    settings = {'coreProfile': 'general-v1', 'expectedVersion': '23.2.2', 'timeoutSeconds': 20,
                'operationTimeoutSeconds': 80, 'maxEntities': 10000, 'captureViewport': False}
    (root / 'bridge.config.json').write_text(json.dumps(settings), encoding='utf-8')
    legacy = Server(root, read_only=True)
    c = Client([args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root), '--read-only'])
    try:
        hello = c.call(1, 'initialize', {'protocolVersion': '2025-11-25', 'capabilities': {},
                                      'clientInfo': {'name': 'independent-python-parity', 'version': '1'}})
        assert hello['result']['protocolVersion'] == '2025-11-25', hello
        c.notify('notifications/initialized')
        tools = c.call(2, 'tools/list')['result']['tools']
        assert sorted(t['name'] for t in tools) == sorted(t['name'] for t in legacy.tools)
        for tool in tools:
            schema = json.loads(json.dumps(tool['inputSchema']))
            for value in schema.get('properties', {}).values():
                value.pop('description', None)  # additive SDK parameter help; constraints unchanged
            assert schema == next(t['inputSchema'] for t in legacy.tools if t['name'] == tool['name'])
            assert tool['annotations']['readOnlyHint'] is True
        status = c.call(3, 'tools/call', {'name': 'bridge_status', 'arguments': {}})['result']
        assert not status['isError'] and status['structuredContent']['panelLiveness'] == 'not_verified'
        for index, (name, values) in enumerate([
                ('bridge_status', {'command': 'DELETE'}), ('operation_result', {}),
                ('operation_result', {'operationId': True}), ('operation_result', {'operationId': '../bad'})], 4):
            result = c.call(index, 'tools/call', {'name': name, 'arguments': values})
            assert result['error']['code'] == -32602, result
        c.process.stdin.write(b'broken\n'); c.process.stdin.flush()
        # Official SDK drops a malformed frame and logs it to stderr; unlike the
        # Python dispatcher, it does not fabricate a response with null request ID.
        assert c.call(8, 'ping')['result'] == {}
        id = 'a' * 32
        folder = root / 'operations' / id
        folder.mkdir(parents=True)
        previous = None
        for sequence, state in enumerate(['prepared', 'execution_claimed', 'command_started', 'command_returned', 'executed_unverified', 'succeeded_verified']):
            details = {'note': 'Тест', 'value': 1.0}
            if state == 'succeeded_verified':
                details['evidence'] = [{'requestId': 'c' * 32, 'sha256': 'd' * 64}]
            event = dict(schemaVersion=1, operationId=id, planSha256='b' * 64, sequence=sequence,
                         previousSha256=previous, state=state, timestampUtc='2026-10-12T10:00:00Z', details=details)
            raw = (json.dumps(event, ensure_ascii=False, indent=2) + '\r\n').encode('utf-8')
            (folder / ('%06d.json' % sequence)).write_bytes(raw)
            previous = hashlib.sha256(raw).hexdigest()
            actual = c.call(10 + sequence, 'tools/call', {'name': 'operation_result', 'arguments': {'operationId': id}})['result']
            assert not actual['isError'], actual
            assert actual['structuredContent'] == legacy.operation_result({'operationId': id}), actual
        (folder / 'writer.lock').write_text('interrupted', encoding='utf-8')
        locked = c.call(20, 'tools/call', {'name': 'operation_result', 'arguments': {'operationId': id}})['result']
        assert locked['structuredContent'] == legacy.operation_result({'operationId': id})
        assert locked['structuredContent']['receipt']['state'] == 'outcome_unknown'
        # Hash chain corruption must be refused without deleting the writer marker.
        event['previousSha256'] = 'e' * 64
        (folder / '000005.json').write_text(json.dumps(event), encoding='utf-8')
        bad = c.call(21, 'tools/call', {'name': 'operation_result', 'arguments': {'operationId': id}})['result']
        assert bad['isError'] and bad['structuredContent'] == legacy.operation_result({'operationId': id}), bad
        assert (folder / 'writer.lock').exists()
        assert not (root / 'requests').exists()
    finally:
        c.close()
        Path(args.evidence).mkdir(parents=True, exist_ok=True)
        (Path(args.evidence) / 'stdio-transcript.json').write_text(json.dumps(c.transcript, indent=2, ensure_ascii=False), encoding='utf-8')
        (Path(args.evidence) / 'stdio-stderr.txt').write_text(''.join(c.logs), encoding='utf-8')
    assert any('Json' in line or 'JSON' in line for line in c.logs), c.logs
    print('PASS: real SDK STDIO, exact read-only schema constraints, strict arguments, malformed-frame recovery, Python journal byte/hash parity and interrupted no-retry evidence. No CAD.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--dotnet', required=True)
    parser.add_argument('--host', required=True)
    parser.add_argument('--evidence', required=True)
    checks(parser.parse_args())
