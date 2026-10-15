"""Independent saved STDIO/native level-transfer evidence audit; no CAD."""
import argparse
import hashlib
import json
from pathlib import Path
from core_config import general_core

parser = argparse.ArgumentParser()
for name in ('stage', 'evidence', 'output'):
    parser.add_argument('--' + name, required=True, type=Path)
args = parser.parse_args()
stage, evidence = args.stage.resolve(), args.evidence.resolve()
faults = ('control', 'return_false', 'wrong_level', 'lost_reply', 'packet_tamper')
hash_count = request_count = 0

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

for name, enabled in (('admission.json', True), ('admission-disabled.json', False)):
    report = read(evidence / name)
    assert report['state'] == 'passed' and report['enabled'] is enabled and report['cadConnectionAttempted'] is False
    assert len(report['configurations']) == 14 and sum(row['accepted'] for row in report['configurations']) == 2
    assert len(report['requests']) == 15 and sum(row['accepted'] for row in report['requests']) == (3 if enabled else 0)
    assert all(row['accepted'] == row['expected'] for row in report['configurations'] + report['requests'])
baseline = read(stage / 'bridge.config.json')
variants = [('enabled', baseline, True), ('disabled', dict(baseline, ownedLevelMovesExperimental=False), True)]
variants += [('wrong_flag', dict(baseline, ownedLevelMovesExperimental=value), False) for value in ('true', 1, None)]
for dependency in ('levelReadingExperimental', 'entitySetsExperimental', 'workingCopiesExperimental', 'documentBindingsExperimental'):
    variants += [(dependency, dict(baseline, **{dependency: value}), False) for value in (False, 'true')]
legacy = dict(baseline)
legacy.pop('coreProfile')
variants.append(('legacy', legacy, False))
for name, config, expected in variants:
    try:
        accepted = general_core(config)
    except ValueError:
        accepted = False
    assert accepted is expected, name

for fault in faults:
    case = read(evidence / (fault + '.json'))
    root = Path(case['root']).resolve()
    assert root.is_relative_to(stage / 'work')
    assert case['state'] == 'passed' and case['cadConnectionAttempted'] is False
    for collection in ('protectedHashes',):
        for name, digest in case['seed'][collection].items():
            path = Path(name).resolve()
            assert path.is_relative_to(root) and sha(path) == digest
            hash_count += 1
    for name, digest in case['artifactHashes'].items():
        path = Path(name).resolve()
        assert path.is_relative_to(root) and sha(path) == digest
        hash_count += 1
    calls = case['nativeRequests']
    assert all(read(root / 'requests' / (call['requestId'] + '.json.processing'))['operation'] == 'recipe.prepare' for call in calls)
    request_count += len(calls)
    preparation = next(call for call in calls if call['recipe'] == 'prepare_owned_level_move')
    result = preparation['result']
    assert result['state'] == 'owned_level_move_prepared_observed'
    for flag in ('executionEnabled', 'geometryMutationAttempted', 'contextChangeAttempted', 'cadSaveAttempted', 'nativeProviderQualified'):
        assert result[flag] is False
    packet = root / 'ownedlevelmoves' / (preparation['requestId'] + '.json')
    if fault == 'packet_tamper':
        assert sha(packet) != result['ownedLevelMoveSha256'] and packet.read_bytes().endswith(b'tamper')
        assert case['prepared']['isError'] is True and case['commandCalls'] == 0
        assert not any(call['recipe'] == 'execute_owned_level_move' for call in calls)
        continue
    assert sha(packet) == result['ownedLevelMoveSha256'] and read(packet) == result['ownedLevelMove']
    definition = read(packet)
    assert definition['entityKeys'] == [[40, 5]] and definition['arguments']['parameters']['targetLevelNumber'] == 5
    level_id = definition['arguments']['levelInspectionRequestId']
    levels = root / 'responses' / (level_id + '.json')
    assert sha(levels) == definition['arguments']['levelInspectionSha256'] and read(levels)['bridgeRequestId'] == level_id
    executed = [call for call in calls if call['recipe'] == 'execute_owned_level_move']
    assert len(executed) == case['commandCalls'] == 1
    response = executed[0]['result']
    assert response['cadMutationAttempted'] is response['contextChangeAttempted'] is True
    assert response['cadSaveAttempted'] is False and response['retryAllowed'] is False
    assert response['state'] == ('owned_level_move_executed_unverified' if fault in ('control', 'lost_reply') else 'owned_level_move_outcome_unknown')
    execution_id = executed[0]['requestId']
    events = sorted((root / 'operations' / execution_id).glob('*.json'))
    previous = None
    states = []
    returned = None
    for number, path in enumerate(events):
        event = read(path)
        assert path.name == f'{number:06d}.json' and event['sequence'] == number and event['previousSha256'] == previous
        assert event['operationId'] == execution_id and event['planSha256'] == response['planSha256']
        previous = sha(path)
        states.append(event['state'])
        if event['state'] == 'command_returned':
            returned = event['details']
    assert states.count('command_started') == states.count('command_returned') == 1
    assert returned['returnCode'] == (0 if fault == 'return_false' else 1)
    fence = root / 'workingcopies' / case['copy']['copyId'] / 'mutation-fence.json'
    if fault in ('control', 'lost_reply'):
        assert states[-1] == 'succeeded_verified' and not fence.exists()
        assert case['verification']['structuredContent']['state'] == 'owned_level_move_verified_observed'
    else:
        assert states[-1] == 'outcome_unknown' and fence.is_file() and case['verification']['isError'] is True
    if fault == 'return_false':
        assert not any(call['recipe'] == 'verify_owned_level_move' for call in calls), 'False native success reached verifier submission'
    if fault == 'lost_reply':
        assert case['execution']['isError'] is True and case['execution']['structuredContent']['bridgeRequestId'] == execution_id
    state = read(sorted((root / 'wire-contexts').glob('*.json'))[-1])
    assert state['commandCalls'] == 1
    body = next(item for item in state['snapshot']['entities'] if item['nativeTypeCode'] == 40)
    assert body['levelNumber'] == (6 if fault == 'wrong_level' else 5)
    for field in ('working', 'checkpoint'):
        assert sha(Path(case['copy']['ownership'][field]['path'])) == sha(Path(case['seed']['sourcePath']))

report = {'state': 'passed', 'cases': len(faults), 'nativeRequests': request_count, 'hashChecks': hash_count,
          'configurationParityCases': len(variants), 'nativeAdmissionCases': 30,
          'cadConnectionAttempted': False, 'runtimeDeploymentPerformed': False}
args.output.parent.mkdir(parents=True, exist_ok=True)
with args.output.open('x', encoding='utf-8') as output:
    json.dump(report, output, indent=2)
print(json.dumps(report))
