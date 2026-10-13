"""Independent immutable artifact, command and no-replay audit. No CAD."""
import argparse
import json
from pathlib import Path

from audit_checkpoint_recovered_open import journal, read, require, sha

FAULTS = {'control', 'mixed_targets', 'noop_success', 'negative_angle', 'oblique_axis',
          'throw_before_effect', 'throw_after_effect', 'return_zero', 'partial_second_throw',
          'partial_second_zero', 'untargeted_changed', 'changed_process_after', 'late_context',
          'alias_mapping', 'invalid_bounds', 'packet_sha', 'fenced_copy', 'busy_executor',
          'previous_target_changed', 'after_start_context', 'mapping_after_command'}
CONTROLS = {'control', 'mixed_targets', 'noop_success', 'negative_angle', 'oblique_axis'}
PREFLIGHT = {'late_context', 'alias_mapping', 'invalid_bounds', 'packet_sha', 'fenced_copy', 'busy_executor'}


def audit(stage):
    require((stage / '.owned-rotation-execution-fixture').is_file(), 'Missing own stage marker')
    report = read(stage / 'reports/owned-rotation-execution-tests.json')
    require(report['state'] == 'passed' and report['cadConnectionAttempted'] is False and
            report['productionEntryPointAvailable'] is False and len(report['rows']) == len(FAULTS) and
            {row['fault'] for row in report['rows']} == FAULTS, 'Incomplete or wrong-scope matrix')
    count = 0
    for row in report['rows']:
        fault, root, rid = row['fault'], Path(row['root']).resolve(), row['requestId']
        require(root.is_relative_to(stage / 'work'), 'Foreign fixture root')
        hashes = {Path(path).resolve(): value for path, value in row['artifactHashes'].items()}
        require(set(hashes) == {path.resolve() for path in root.rglob('*') if path.is_file()} and
                all(path.is_relative_to(root) and sha(path) == value for path, value in hashes.items()), 'Inventory/SHA changed')
        require(all(Path(path).resolve().is_relative_to(root) and sha(Path(path)) == digest
                    for path, digest in row['protectedHashes'].items()), 'Protected source/packet changed')
        count += len(hashes)
        result = row['result']
        expected = 'owned_rotation_executed_unverified' if fault in CONTROLS else (
            'owned_rotation_rejected_before_change' if fault in PREFLIGHT else 'owned_rotation_outcome_unknown')
        require(result['state'] == expected and result['contextChangeAttempted'] is False and
                result['cadSaveAttempted'] is False and result['retryAllowed'] is False, 'Wrong result/action')
        commands = row['commands']
        expected_calls = 0 if fault in PREFLIGHT or fault == 'after_start_context' else (3 if fault == 'mixed_targets' else
                         2 if fault in {'partial_second_throw', 'partial_second_zero'} else 1)
        require(len(commands) == result['commandCallsAttempted'] == expected_calls, 'Command count differs')
        if result.get('planSha256'):
            plan_path = root / 'rotationexecutions' / (rid + '.json')
            require(sha(plan_path) == result['planSha256'] and read(plan_path) == result['plan'], 'Plan differs')
            plan = read(plan_path)
            fence = root / 'workingcopies' / plan['copyId'] / 'mutation-fence.json'
            require(fence.is_file() and sha(fence) == result['executionFenceSha256'], 'Fence missing or altered')
            claim = read(root / 'rotationplans' / (plan['rotationPlanId'] + '.execution-once.json'))
            require(claim['operationId'] == rid and claim['planSha256'] == result['planSha256'] and
                    claim['automaticReplayForbidden'] is True, 'Claim changed')
            require(row['replayResult']['state'] == 'owned_rotation_rejected_before_change', 'Claim replay permitted')
            states = journal(root, rid)
            require(states[-1] == ('executed_unverified' if fault in CONTROLS else
                    'rejected_before_change' if fault in PREFLIGHT else 'outcome_unknown'), 'Wrong journal outcome')
            require('succeeded_verified' not in states, 'Executor claimed independent geometry verification')
            for index, command in enumerate(commands):
                require(command['angleDegrees'] == plan['angleDegrees'] and command['axis'] == plan['nativeAxis'] and
                        command['compositeId'] == plan['targets'][index]['compositeId'], 'Parameters/order changed')
                start_path = root / 'rotationcommands' / rid / f'{index:06}.started.json'
                start = read(start_path)
                require(start['operationId'] == rid and start['planSha256'] == result['planSha256'] and
                        start['index'] == index and start['compositeId'] == command['compositeId'] and
                        start['angleDegrees'] == command['angleDegrees'] and start['nativeAxis'] == command['axis'],
                        'Started command differs')
                returned_path = start_path.with_name(f'{index:06}.returned.json')
                thrown = fault in {'throw_before_effect', 'throw_after_effect'} or fault == 'partial_second_throw' and index == 1
                require(returned_path.exists() is not thrown, 'Return proof invented/lost')
                if not thrown:
                    returned = read(returned_path)
                    require(returned['startedSha256'] == sha(start_path) and returned['index'] == index and
                            returned['returnCode'] == (0 if fault == 'return_zero' or fault == 'partial_second_zero' and index == 1 else 1),
                            'Return proof differs')
            if fault in CONTROLS:
                require(result['geometryVerified'] is False and result['copyFenced'] is True, 'Unsafe success')
                summary_path = root / 'rotationcommands' / rid / 'summary.json'
                require(sha(summary_path) == result['commandSummarySha256'], 'Summary SHA differs')
                summary = read(summary_path)
                require(summary['geometryVerified'] is False and len(summary['commands']) == expected_calls, 'Missing command proofs')
                for item in summary['commands']:
                    prefix = root / 'rotationcommands' / rid / f"{item['index']:06}"
                    for role in ('started', 'returned', 'observed'):
                        require(sha(prefix.with_suffix('.' + role + '.json')) == item[role + 'Sha256'], 'Summary chain differs')
        else:
            require(expected_calls == 0 and row['replayResult']['state'] in {
                    'not_attempted_before_claim', 'owned_rotation_rejected_before_change'}, 'Unexpected preflight action')
    return {'state': 'passed', 'cases': len(FAULTS), 'artifactHashesChecked': count,
            'cadConnectionAttempted': False, 'productionEntryPointAvailable': False,
            'scope': 'Internal batch state machine with fake callbacks; geometry/COM convention not qualified'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('stage', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.stage.resolve())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(f"PASS: {result['cases']} internal rotation batches / {result['artifactHashesChecked']} hashes audited; no CAD.")
