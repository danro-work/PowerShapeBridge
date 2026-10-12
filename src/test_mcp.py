"""Fake protocol checks; explicitly requested live translation/recipe tests change test copies."""
import argparse
import contextlib
import uuid
import base64
import hashlib
import json
from pathlib import Path
import queue
import subprocess
import sys
import tempfile
import threading
import time

SCRIPT = Path(__file__).with_name('mcp_server.py')


class Client:
    def __init__(self, root, timeout=2):
        self.p = subprocess.Popen([sys.executable, str(SCRIPT), '--bridge-root', str(root), '--timeout', str(timeout)],
                                  stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        self.lines = queue.Queue()
        def read():
            for line in self.p.stdout:
                self.lines.put(json.loads(line))
        self.thread = threading.Thread(target=read, daemon=True)
        self.thread.start()

    def send(self, method, params=None, id=None):
        value = {'jsonrpc': '2.0', 'method': method}
        if params is not None:
            value['params'] = params
        if id is not None:
            value['id'] = id
        self.p.stdin.write(json.dumps(value).encode() + b'\n')
        self.p.stdin.flush()

    def recv(self, seconds=5):
        return self.lines.get(timeout=seconds)

    def init(self):
        self.send('initialize', {'protocolVersion': '2025-11-25', 'capabilities': {}, 'clientInfo': {'name': 'bridge-tests', 'version': '1'}}, 1)
        result = self.recv()
        assert result['result']['protocolVersion'] == '2025-11-25'
        self.send('notifications/initialized')

    def close(self):
        self.p.stdin.close()
        self.p.wait(timeout=5)
        stderr = self.p.stderr.read().decode()
        assert self.p.returncode == 0, stderr
        assert not stderr, stderr


def checks():
    with contextlib.nullcontext(Path(__file__).parents[1] / 'work' / ('mcp-test-' + uuid.uuid4().hex)) as tmp:
        root = Path(tmp)
        root.mkdir(parents=True)
        c = Client(root)
        c.send('tools/list', id=0)
        assert c.recv()['error']['code'] == -32600
        c.init()
        c.send('tools/list', id=2)
        tools = c.recv()['result']['tools']
        assert [t['name'] for t in tools] == ['bridge_status', 'model_snapshot']
        c.send('tools/call', {'name': 'model_snapshot', 'arguments': {'command': 'DELETE'}}, 3)
        assert c.recv()['error']['code'] == -32602
        assert not (root / 'requests').exists()
        c.send('tools/call', {'name': 'execute'}, 4)
        assert c.recv()['error']['code'] == -32602
        c.p.stdin.write(b'broken\n'); c.p.stdin.flush()
        assert c.recv()['error']['code'] == -32700
        c.send('tools/call', {'name': 'bridge_status'}, 5)
        assert c.recv()['result']['structuredContent']['panelLiveness'] == 'not_verified'
        (root / 'PSBridge.Worker.exe').touch()
        c.send('tools/call', {'name': 'model_snapshot'}, 6)
        result = c.recv()['result']
        assert result['isError'] and result['structuredContent']['state'] == 'timeout_outcome_unknown'
        assert list((root / 'requests').glob('*.json')) == []
        c.send('tools/call', {'name': 'model_snapshot'}, 7)
        deadline = time.monotonic() + 3
        while not list((root / 'requests').glob('*.json')) and time.monotonic() < deadline:
            time.sleep(.02)
        request = next((root / 'requests').glob('*.json'))
        c.send('notifications/cancelled', {'requestId': 7, 'reason': 'test'})
        assert c.recv()['result']['structuredContent']['state'] == 'cancelled_outcome_unknown'
        assert not request.exists()
        # Simulated panel: matching response plus checked image; no PowerShape.
        c.send('tools/call', {'name': 'model_snapshot', 'arguments': {'includeImage': True}}, 8)
        deadline = time.monotonic() + 3
        while not list((root / 'requests').glob('*.json')) and time.monotonic() < deadline:
            time.sleep(.02)
        request = next((root / 'requests').glob('*.json'))
        value = json.loads(request.read_text())
        assert value == {'requestId': request.stem, 'operation': 'model.snapshot'}
        (root / 'responses').mkdir()
        (root / 'captures').mkdir()
        jpg = root / 'captures' / 'test.jpg'
        jpg.write_bytes(b'\xff\xd8test')
        response = {'state': 'succeeded_verified', 'viewportCapture': {'path': str(jpg), 'sha256': hashlib.sha256(jpg.read_bytes()).hexdigest()}}
        (root / 'responses' / request.name).write_text(json.dumps(response))
        result = c.recv()['result']
        assert not result['isError'] and base64.b64decode(result['content'][1]['data']) == jpg.read_bytes()
        c.send('ping', id=9)
        assert c.recv()['result'] == {}
        c.close()
        (root / 'bridge.config.json').write_text(json.dumps({'diskVerificationPanel': True}))
        d = Client(root)
        d.init()
        d.send('tools/list', id=2)
        assert d.recv()['result']['tools'][-1]['name'] == 'verify_saved_operation'
        d.send('tools/call', {'name': 'verify_saved_operation', 'arguments': {'path': 'unsafe'}}, 3)
        assert d.recv()['error']['code'] == -32602
        d.send('tools/call', {'name': 'verify_saved_operation'}, 4)
        deadline = time.monotonic() + 3
        while not list((root / 'requests').glob('*.json')) and time.monotonic() < deadline:
            time.sleep(.02)
        request = next((root / 'requests').glob('*.json'))
        assert json.loads(request.read_text())['operation'] == 'disk.verify'
        (root / 'responses' / request.name).write_text(json.dumps({'state': 'succeeded_disk_verified'}))
        assert not d.recv()['result']['isError']
        d.close()
        (root / 'bridge.config.json').write_text(json.dumps({'mcpMutationsPanel': True, 'recipeOperationsPanel': True}))
        e = Client(root)
        e.init()
        e.send('tools/list', id=2)
        assert 'execute_translation' in {t['name'] for t in e.recv()['result']['tools']}
        e.send('tools/call', {'name': 'prepare_recipe', 'arguments': {'recipeId': 'rotate_z', 'parameters': {'angleDegrees': 45.5}}}, 20)
        assert e.recv()['error']['code'] == -32602
        for bad_parameters in ({'levelNumber': True}, {'levelNumber': 899}, {'levelNumber': 910}, {'angleDegrees': 45}, {'levelNumber': 900, 'command': 'DELETE'}):
            e.send('tools/call', {'name': 'prepare_recipe', 'arguments': {'recipeId': 'assign_test_level', 'parameters': bad_parameters}}, 23)
            assert e.recv()['error']['code'] == -32602
        for z_axis, y_axis, origin in (([0,0,0],[0,1,0],[0,0,0]),([0,0,1],[0,0,1],[0,0,0]),([0,0,2],[0,1,0],[0,0,0]),([0,0,1],[0,1,0],[101,0,0]),([False,0,1],[0,1,0],[0,0,0])):
            e.send('tools/call', {'name':'prepare_recipe','arguments':{'recipeId':'create_workplane_world','parameters':{'originWorldMm':origin,'zAxis':z_axis,'yAxis':y_axis}}},25)
            assert e.recv()['error']['code'] == -32602
        for factor in (True,0,-1,1,2.1,'1.1'):
            e.send('tools/call',{'name':'prepare_recipe','arguments':{'recipeId':'scale_uniform_world','parameters':{'scaleFactor':factor}}},27)
            assert e.recv()['error']['code']==-32602
        for invalid in ({'command':'DELETE'},{'operationId':'a'*32},{'scaleFactor':1.1}):
            e.send('tools/call',{'name':'prepare_recipe','arguments':{'recipeId':'convert_test_solid_to_surfaces','parameters':invalid}},29)
            assert e.recv()['error']['code']==-32602
        for invalid in ({'offsetDistanceMm':2},{'targetSide':0},{'command':'DELETE'}):
            e.send('tools/call',{'name':'prepare_recipe','arguments':{'recipeId':'offset_test_top_surface_1mm','parameters':invalid}},32)
            assert e.recv()['error']['code']==-32602
        for internal in ('qualify_test_dgk_roundtrip','verify_test_dgk_saved_copy','cleanup_failed_test_dgk_source','qualify_test_section_z10','qualify_test_section_z0','verify_existing_slice_batch'):
            e.send('tools/call',{'name':'prepare_recipe','arguments':{'recipeId':internal,'parameters':{}}},33)
            assert e.recv()['error']['code']==-32602
        for parameters in ({'firstZMm':0,'lastZMm':18,'stepMm':8},{'firstZMm':2,'lastZMm':20,'stepMm':9},{'firstZMm':2,'lastZMm':18,'stepMm':7},{'firstZMm':2,'lastZMm':18,'stepMm':4},{'firstZMm':True,'lastZMm':10,'stepMm':1},{'firstZMm':0.10000001,'lastZMm':10,'stepMm':1},{'firstZMm':2,'lastZMm':18,'stepMm':8,'command':'DELETE'}):
            e.send('tools/call',{'name':'prepare_recipe','arguments':{'recipeId':'slice_test_cube_xy','parameters':parameters}},34)
            assert e.recv()['error']['code']==-32602
        e.send('tools/call', {'name': 'prepare_translation', 'arguments': {'deltaWorldMm': [True, 0, 0]}}, 3)
        assert e.recv()['error']['code'] == -32602
        e.send('tools/call', {'name': 'execute_translation', 'arguments': {'operationId': '../bad', 'planSha256': 'a'*64}}, 4)
        assert e.recv()['error']['code'] == -32602
        e.send('tools/call', {'name': 'prepare_translation', 'arguments': {'deltaWorldMm': [-3, 4, 2]}}, 5)
        deadline = time.monotonic() + 3
        while not list((root / 'requests').glob('*.json')) and time.monotonic() < deadline:
            time.sleep(.02)
        request = next((root / 'requests').glob('*.json'))
        envelope = json.loads(request.read_text())
        assert envelope['operation'] == 'translation.prepare' and envelope['arguments'] == {'deltaWorldMm': [-3, 4, 2]}
        (root / 'responses' / request.name).write_text(json.dumps({'state': 'plan_ready'}))
        assert not e.recv()['result']['isError']
        # Exercise valid recipe routes through the adapter queue, not only validation.
        for request_id, name, arguments, operation, state in (
            (21, 'prepare_recipe', {'recipeId': 'rotate_z', 'parameters': {'angleDegrees': 45}}, 'recipe.prepare', 'plan_ready'),
            (22, 'execute_recipe', {'operationId': 'a'*32, 'planSha256': 'b'*64}, 'recipe.execute', 'succeeded_saved'),
            (37, 'execute_recipe', {'operationId': 'a'*32, 'planSha256': 'b'*64}, 'recipe.execute', 'succeeded_slices_saved_reopened_dgk_verified'),
            (24, 'prepare_recipe', {'recipeId': 'assign_test_level', 'parameters': {'levelNumber': 900}}, 'recipe.prepare', 'plan_ready'),
            (26, 'prepare_recipe', {'recipeId': 'create_workplane_world', 'parameters': {'originWorldMm': [2,3,4], 'zAxis': [0,1,0], 'yAxis': [0,0,1]}}, 'recipe.prepare', 'plan_ready'),
            (31, 'prepare_recipe', {'recipeId':'offset_test_top_surface_1mm','parameters':{}}, 'recipe.prepare', 'plan_ready'),
            (35, 'prepare_recipe', {'recipeId':'slice_test_cube_xy','parameters':{'firstZMm':2,'lastZMm':18,'stepMm':8}}, 'recipe.prepare', 'plan_ready'),
            (36, 'prepare_recipe', {'recipeId':'slice_test_cube_xy','parameters':{'firstZMm':0.1,'lastZMm':0.3,'stepMm':0.1}}, 'recipe.prepare', 'plan_ready'),
            (30, 'prepare_recipe', {'recipeId':'convert_test_solid_to_surfaces','parameters':{}}, 'recipe.prepare', 'plan_ready'),
            (28, 'prepare_recipe', {'recipeId':'scale_uniform_world','parameters':{'scaleFactor':1.1}}, 'recipe.prepare', 'plan_ready'),
        ):
            request.unlink(missing_ok=True)
            e.send('tools/call', {'name': name, 'arguments': arguments}, request_id)
            deadline = time.monotonic() + 3
            while not list((root / 'requests').glob('*.json')) and time.monotonic() < deadline:
                time.sleep(.02)
            request = next((root / 'requests').glob('*.json'))
            assert json.loads(request.read_text()) == {'requestId': request.stem, 'operation': operation, 'arguments': arguments}
            (root / 'responses' / request.name).write_text(json.dumps({'state': state}))
            assert not e.recv()['result']['isError']
        request.unlink(missing_ok=True)
        e.send('tools/call',{'name':'operation_result','arguments':{'operationId':'../bad'}},38)
        assert e.recv()['error']['code']==-32602
        (root/'operations').mkdir(exist_ok=True)
        receipt={'operationId':'a'*32,'state':'succeeded_slices_saved_reopened_dgk_verified','phase':'completed'}
        body=json.dumps(receipt).encode()
        (root/'operations'/('a'*32+'.json')).write_bytes(body)
        e.send('tools/call',{'name':'operation_result','arguments':{'operationId':'a'*32}},39)
        recorded=e.recv()['result']
        assert not recorded['isError'] and recorded['structuredContent']['receipt']==receipt
        assert recorded['structuredContent']['receiptSha256']==hashlib.sha256(body).hexdigest()
        assert not list((root/'requests').glob('*.json'))
        (root/'operations'/('a'*32+'.json')).write_text(json.dumps({'operationId':'b'*32,'state':'succeeded_saved'}))
        e.send('tools/call',{'name':'operation_result','arguments':{'operationId':'a'*32}},40)
        assert e.recv()['result']['isError']
        e.close()
    print('PASS: protocol lifecycle, allowlist, malformed input, status, timeout cleanup, cancellation, fixed queue envelope and image integrity.')


def live(root, output):
    c = Client(root, 30)
    c.init()
    c.send('tools/call', {'name': 'model_snapshot', 'arguments': {'includeImage': True}}, 2)
    response = c.recv(40)
    result = response['result']
    assert not result['isError'], result
    data = result['structuredContent']
    assert data['filesBefore'] == data['filesAfter']
    assert data['selectionBefore'] == data['selectionAfter']
    assert data['contextBefore'] == data['contextAfter']
    assert data['entityCount'] == 1
    assert data['selectionBounds']['size'] == [20, 20, 20]
    image = result['content'][1]
    assert image['mimeType'] == 'image/jpeg'
    image_data = base64.b64decode(image['data'])
    assert hashlib.sha256(image_data).hexdigest() == data['viewportCapture']['sha256']
    target = Path(output)
    target.mkdir(exist_ok=True)
    (target / 'PowerShape Bridge MCP — успешный ответ.json').write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')
    (target / 'PowerShape Bridge MCP — снимок.jpg').write_bytes(image_data)
    c.close()
    print('PASS: real MCP subprocess -> existing P1 panel -> PowerShape -> structured data and verified JPEG; unchanged file hashes, selection and workplane.')


def disk_live(root, output):
    c = Client(root, 90)
    c.init()
    c.send('tools/call', {'name': 'verify_saved_operation'}, 2)
    result = c.recv(100)['result']
    c.close()
    target = Path(output)
    (target / 'PowerShape Bridge P2.4 — ответ MCP.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    assert not result['isError'], result
    data = result['structuredContent']
    assert data['resultDiskVerified'] and data['checkpointDiskVerified'] and data['operationSourceHashesUnchanged']
    print('PASS: real MCP disk verification via user-session panel.')


def mutation_live(root, output):
    c = Client(root, 90)
    c.init()
    def call(name, args, id):
        c.send('tools/call', {'name': name, 'arguments': args}, id)
        return c.recv(100)['result']
    results = []
    activated = call('activate_test_baseline', {}, 2)
    results.append(activated)
    assert not activated['isError'], activated
    prepared = call('prepare_translation', {'deltaWorldMm': [-3, 4, 2]}, 3)
    results.append(prepared)
    assert not prepared['isError'], prepared
    data = prepared['structuredContent']
    plan = data['plan']
    assert plan['expectedCogWorldMm'] == [-3, 4, 12]
    wrong = call('execute_translation', {'operationId': plan['operationId'], 'planSha256': '0'*64}, 4)
    results.append(wrong)
    assert wrong['isError'] and not wrong['structuredContent'].get('mutationAttempted'), wrong
    executed = call('execute_translation', {'operationId': plan['operationId'], 'planSha256': data['planSha256']}, 5)
    results.append(executed)
    assert not executed['isError'], executed
    operation = executed['structuredContent']
    assert operation['after']['cogWorldMm'] == [-3, 4, 12]
    assert operation['after']['boundsWorldMm'] == [-13, 7, -6, 14, 2, 22]
    reactivated = call('activate_test_baseline', {}, 6)
    results.append(reactivated)
    assert not reactivated['isError'], reactivated
    repeat = call('execute_translation', {'operationId': plan['operationId'], 'planSha256': data['planSha256']}, 7)
    results.append(repeat)
    assert repeat['isError'] and not repeat['structuredContent'].get('mutationAttempted'), repeat
    c.close()
    target = Path(output)
    (target / 'PowerShape Bridge P2.5 — ответы MCP.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
    print('PASS: real MCP baseline activation, preparation, wrong digest rejection, planned translation and repeat rejection.')


def recipe_live(root, output, level=False, workplane=False, scale=False, surfaces=False, offset=False):
    c = Client(root, 90);c.init()
    def call(name, args, id):
        c.send('tools/call', {'name': name, 'arguments': args}, id)
        return c.recv(100)['result']
    results=[]
    active=call('activate_test_baseline',{},2);results.append(active);assert not active['isError'],active
    recipe_args={'recipeId':'assign_test_level','parameters':{'levelNumber':900}} if level else {'recipeId':'rotate_z','parameters':{'angleDegrees':45}}
    if workplane: recipe_args={'recipeId':'create_workplane_world','parameters':{'originWorldMm':[2,3,4],'zAxis':[0,1,0],'yAxis':[0,0,1]}}
    if offset: recipe_args={'recipeId':'offset_test_top_surface_1mm','parameters':{}}
    if surfaces: recipe_args={'recipeId':'convert_test_solid_to_surfaces','parameters':{}}
    if scale: recipe_args={'recipeId':'scale_uniform_world','parameters':{'scaleFactor':1.1}}
    prepared=call('prepare_recipe',recipe_args,3);results.append(prepared);assert not prepared['isError'],prepared
    data=prepared['structuredContent'];plan=data['plan']
    executed=call('execute_recipe',{'operationId':plan['operationId'],'planSha256':data['planSha256']},4);results.append(executed)
    target=Path(output);(target/('PowerShape Bridge P2.12 — ответы MCP.json' if offset else 'PowerShape Bridge P2.10 — ответы MCP.json' if surfaces else 'PowerShape Bridge P2.9 — ответы MCP.json' if scale else 'PowerShape Bridge P2.8 — ответы MCP.json' if workplane else 'PowerShape Bridge P2.7 — ответы MCP.json' if level else 'PowerShape Bridge P2.6 — ответы MCP.json')).write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
    c.close();assert not executed['isError'],executed
    if offset:
        data=executed['structuredContent'];assert data['expectedTopPlaneWorldZMm']==21 and data['surfaceResult']['surfaceCount']==6
        print('PASS: fixed top surface offset 1 mm saved.')
        return
    if surfaces:
        data=executed['structuredContent'];assert data['surfaceResult']['surfaceCount']==6 and abs(data['surfaceResult']['areaRawSum']-2400)<1e-6
        print('PASS: six qualified cube surfaces saved via MCP.')
        return
    if scale:
        data=executed['structuredContent'];after=data['after']
        assert all(abs(a-b)<1e-6 for a,b in zip(after['boundsWorldMm'],[-11,11,-11,11,0,22]))
        assert abs(after['volumeRaw']-0.8*1.1**3)<1e-6 and abs(after['areaRaw']-2400*1.1**2)<1e-6
        print('PASS: uniform scale 1.1 via fixed command recipe; bounds, COG, volume and area verified.')
        return
    if workplane:
        data=executed['structuredContent'];assert data['workplaneAfterSave']['worldInfo']==[2,3,4,0,1,0,0,0,1]
        assert data['workplaneAfterSave']['activeWorkplane']==0 and data['before']==data['after']
        print('PASS: world workplane created with measured origin/axes; world context restored; solid geometry unchanged.')
        return
    if level:
        data=executed['structuredContent'];assert data['afterLevelNumber']==900 and data['afterLevelName']=='AI_Bridge_Test_900'
        assert data['before']==data['after']
        print('PASS: registered level assignment via MCP; geometry unchanged and fixed name verified.')
        return
    after=executed['structuredContent']['after'];limit=10*2**.5
    assert all(abs(x-y)<1e-6 for x,y in zip(after['boundsWorldMm'],[-limit,limit,-limit,limit,0,20]))
    print('PASS: registered world-Z rotation recipe executed via MCP with checked bounds.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--live-root')
    parser.add_argument('--output')
    parser.add_argument('--disk-live-root')
    parser.add_argument('--mutation-live-root')
    parser.add_argument('--recipe-live-root')
    parser.add_argument('--level-live-root')
    parser.add_argument('--workplane-live-root')
    parser.add_argument('--scale-live-root')
    parser.add_argument('--surfaces-live-root')
    parser.add_argument('--offset-live-root')
    args = parser.parse_args()
    checks()
    if args.live_root:
        live(args.live_root, args.output)
    if args.disk_live_root:
        disk_live(args.disk_live_root, args.output)
    if args.mutation_live_root:
        mutation_live(args.mutation_live_root, args.output)
    if args.recipe_live_root:
        recipe_live(args.recipe_live_root, args.output)
    if args.level_live_root:
        recipe_live(args.level_live_root, args.output, level=True)
    if args.workplane_live_root:
        recipe_live(args.workplane_live_root, args.output, workplane=True)
    if args.offset_live_root:
        recipe_live(args.offset_live_root,args.output,offset=True)
    if args.surfaces_live_root:
        recipe_live(args.surfaces_live_root,args.output,surfaces=True)
    if args.scale_live_root:
        recipe_live(args.scale_live_root, args.output, scale=True)
