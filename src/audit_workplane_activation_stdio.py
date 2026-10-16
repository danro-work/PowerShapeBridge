"""Independent file/coordinate audit of the two separate synthetic MCP operations."""
import argparse
import copy
import hashlib
import json
from pathlib import Path

def read(p): return json.loads(Path(p).read_text(encoding='utf-8-sig'))
def sha(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest()

def audit(folder):
    report = read(folder / 'result.json')
    assert report['state'] == 'passed' and report['cadConnectionAttempted'] is False
    assert report['setterCalls'] == (3 if report.get('multiple', False) else 2) and report['reconnectedWithoutReplay'] is True
    root = Path(report['root']).resolve()
    count = 0
    for path, digest in report['artifactHashes'].items():
        path = Path(path).resolve()
        assert path.is_relative_to(root) and sha(path) == digest
        count += 1
    for path, digest in report['seed']['protectedHashes'].items():
        assert sha(path) == digest
        count += 1
    ids = []
    multiple = report.get('multiple', False)
    steps = [('existing', 1080000011, [0,0,10], [2,6,-3])]
    if multiple:
        steps.append(('other', 1080000012, [2,6,-3], [-4,-5,4]))
    steps.append(('world', 0, [-4,-5,4] if multiple else [2,6,-3], [0,0,10]))
    for mode, target, old_cog, new_cog in steps:
        item = report[mode]
        execution = item['execution']
        op = execution['operationId']
        ids.append(op)
        raw_path = root / 'responses' / (op + '.json')
        raw = read(raw_path)
        assert all(execution[k] == v for k,v in raw.items())
        assert raw['state'] == 'owned_workplane_activation_executed_unverified' and raw['setterCalls'] == 1
        assert raw['contextChangeAttempted'] is True and raw['geometryMutationAttempted'] is raw['cadSaveAttempted'] is False
        plan = raw['plan']
        assert sha(root / 'workplaneactivationexecutions' / (op + '.json')) == raw['planSha256']
        packet = read(root / 'workplaneactivations' / (plan['activationId'] + '.json'))
        assert sha(root / 'workplaneactivations' / (plan['activationId'] + '.json')) == plan['activationSha256']
        assert plan['targetCompositeId'] == target and packet['targetCompositeId'] == target
        assert plan['schemaVersion'] == packet['schemaVersion'] == (2 if multiple else 1)
        capture = plan['captureBefore']
        if multiple:
            assert len(capture['planes']) == 3 and 'plane' not in capture
            expected_frames = {11:[2,3,4,0,1,0,0,0,1],12:[-4,5,6,1,0,0,0,1,0],13:[9,-2,3,0,0,1,0,1,0]}
            assert {p['nativeId']:p['worldInfo'] for p in capture['planes']} == expected_frames
            assert [p['compositeId'] for p in capture['planes']] == [1080000011,1080000012,1080000013]
        else:
            assert 'plane' in capture and 'planes' not in capture
        claim_path = root / 'workplaneactivations' / (plan['activationId'] + '.execution-once.json')
        assert sha(claim_path) == raw['claimSha256']
        assert read(claim_path) == {'operationId':op, 'planSha256':raw['planSha256'], 'activationId':plan['activationId'], 'copyId':plan['copyId'], 'automaticReplayForbidden':True}
        previous = None
        events = []
        for index, path in enumerate(sorted((root/'operations'/op).glob('*.json'))):
            event = read(path)
            assert path.name == f'{index:06d}.json' and event['sequence'] == index
            assert event['previousSha256'] == previous and event['operationId'] == op and event['planSha256'] == raw['planSha256']
            previous = sha(path)
            events.append(event)
            count += 1
        assert [e['state'] for e in events] == ['prepared','execution_claimed','command_started','command_returned','executed_unverified','succeeded_verified']
        returned = events[3]['details']
        setter_path = root / 'workplaneactivationexecutions' / (op + '.setter-return.json')
        assert sha(setter_path) == returned['setterReceiptSha256'] == raw['setterReceiptSha256']
        setter = read(setter_path)
        assert setter['setterReturned'] is True and setter['setterCalls'] == 1
        assert setter['targetCompositeId'] == setter['activeRaw'] == target
        before = copy.deepcopy(plan['before'])
        after = copy.deepcopy(raw['after'])
        body_before = next(e for e in before['entities'] if e['nativeTypeCode'] == 40)
        body_after = next(e for e in after['entities'] if e['nativeTypeCode'] == 40)
        assert body_before['geometry']['cogRaw'] == old_cog and body_after['geometry']['cogRaw'] == new_cog
        assert body_after['geometry']['coordinateFrame'] == ('world' if mode == 'world' else 'active_workplane_unqualified')
        assert before['workplanes'] == after['workplanes']
        assert after['contextBefore'] == after['contextAfter'] == [0,target]
        body_after['geometry'] = body_before['geometry']
        after['contextBefore'] = before['contextBefore']
        after['contextAfter'] = before['contextAfter']
        for field in ('model','processId','processStartedUtc','windowsSessionId','entities','entityCount','workplanes','levelsContainingEntities','selectionBefore','selectionAfter','lengthUnitFactorRaw','volumeUnitFactorRaw'):
            assert before[field] == after[field], field
        verified = item['verification']
        assert verified['state'] == 'owned_workplane_activation_verified_observed' and verified['copyFenceReleased'] is verified['noCommandReplay'] is True
        assert verified['activeWorkplaneAfter'] == target and 0 < verified['workingCopyLifetimeSeconds'] <= 300
        for proof in verified['evidence']:
            assert sha(root / 'responses' / (proof['requestId'] + '.json')) == proof['sha256']
            count += 1
        assert not (root / 'workingcopies' / plan['copyId'] / 'mutation-fence.json').exists()
    count_steps = len(steps)
    assert len(set(ids)) == count_steps
    assert sum(c['recipe'] == 'execute_owned_workplane_activation' for c in report['nativeRequests']) == count_steps
    assert report['adoption']['verificationOnly'] is True and report['adoption']['executionAuthorized'] is False
    final = read(sorted((root / 'wire-contexts').glob('*.json'))[-1])
    assert final['setterCalls'] == count_steps and final['active'] == 0
    if report.get('legacyUpgrade', False):
        assert report['legacyWorkerSha256'] != report['upgradedWorkerSha256'] == sha(root/'PSBridge.Worker.exe')
        assert report['legacyExecutionBridgeVersion'] == report['existing']['execution']['bridgeVersion']
        assert report['existing']['execution']['bridgeVersion'] != report['existing']['verification']['bridgeVersion']
    return {'state':'passed','hashChecks':count,'setterCalls':count_steps,'multiple':multiple,'legacyUpgrade':report.get('legacyUpgrade',False),'separateIntents':True,'reconnectedWithoutReplay':True,'cadConnectionAttempted':False,'reportSha256':sha(folder/'result.json')}

if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--evidence', required=True)
    p.add_argument('--output', required=True)
    args = p.parse_args()
    output = Path(args.output)
    assert not output.exists()
    result = audit(Path(args.evidence))
    output.write_text(json.dumps(result,indent=2),encoding='utf-8')
    print(json.dumps(result))
