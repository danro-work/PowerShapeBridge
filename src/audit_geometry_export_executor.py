"""Independent durable command/fence/source audit for fake-context export qualification."""
import argparse
import json
from pathlib import Path
import struct

from audit_checkpoint_recovered_open import read, require, sha

FAULTS = {'control','mixed_targets','inactive_level','ambiguous_name','unsafe_name','wrong_mapping',
          'unsupported_translator','invalid_tolerance','extra_settings','target_sha','busy_executor','claimed_context',
          'select_throw','wrong_selection','late_document','retained_document','settings_after_select',
          'selection_collision','export_throw','no_output','malformed_output','output_directory',
          'settings_after_export','clear_throw','clear_no_effect','existing_export_fence'}


def audit(stage):
    require((stage / '.geometry-export-executor-fixture').is_file(), 'Missing isolated marker')
    report = read(stage / 'reports/geometry-export-executor-tests.json')
    require(report['state'] == 'passed' and report['cadConnectionAttempted'] is False and
            len(report['rows']) == len(FAULTS) and {r['fault'] for r in report['rows']} == FAULTS,
            'Incomplete executor matrix')
    count = 0
    for row in report['rows']:
        root = Path(row['root']).resolve()
        require(root.is_relative_to(stage / 'work'), 'Fixture escaped own stage')
        hashes = {Path(p).resolve(): value for p, value in row['artifactHashes'].items()}
        require(set(hashes) == {p.resolve() for p in root.rglob('*') if p.is_file()} and
                all(p.is_relative_to(root) and sha(p) == value for p, value in hashes.items()), 'Artifact inventory changed')
        require(all(Path(p).resolve().is_relative_to(root) and sha(Path(p)) == value
                    for p, value in row['protectedHashes'].items()), 'Protected source changed')
        count += len(hashes)
        result = row['result']
        calls = row['adds'] + row['exports'] + row['clears']
        require(calls == result['callbackCallsAttempted'], 'Callback accounting differs')
        for flag in ('geometryVerified','geometryMutationAttempted','cadSaveAttempted','retryAllowed',
                     'copyFenceReleased','nativeProviderQualified'):
            require(result[flag] is False, 'Unsupported authority')
        require(row['cadConnectionAttempted'] is False and row['exports'] <= 1 and row['clears'] <= 1,
                'CAD connection/repeated export/cleanup')
        control = row['fault'] in ('control','mixed_targets')
        require(result['state'] == ('owned_geometry_export_executed_unverified' if control else
                                   'owned_geometry_export_outcome_unknown' if calls else
                                   'owned_geometry_export_rejected_before_change'), 'Wrong state')
        if 'planSha256' in result:
            plan_path = root / 'geometryexportexecutions' / (row['operationId'] + '.json')
            require(sha(plan_path) == result['planSha256'] and read(plan_path) == result['plan'], 'Plan SHA/body differs')
            plan = result['plan']
            folder = root / 'geometryexports' / row['targetId']
            require(plan['targetPath'] == str(folder / ('Geometry_' + row['targetId'] + '.dmt')), 'Path not fixed')
            require(sha(folder / 'target.json') == plan['targetSha256'], 'Disabled target changed')
            owner = root / 'workingcopies' / plan['copyId'] / 'ownership.json'
            require(sha(owner) == plan['ownershipSha256'], 'Owner changed')
            require(all(Path(p).is_relative_to(root) and sha(Path(p)) == value
                        for p, value in plan['savedFileHashesBefore'].items()), 'Saved documents changed')
            fence = root / 'workingcopies' / plan['copyId'] / 'geometry-export-fence.json'
            require(sha(fence) == result['exportFenceSha256'], 'Export fence removed/changed')
            record = read(fence)
            require(record['operationId'] == row['operationId'] and record['planSha256'] == result['planSha256'] and
                    record['automaticReplayForbidden'] is True, 'Fence linkage differs')
            claim = read(folder / 'export-once.json')
            require(claim['operationId'] == row['operationId'] and claim['planSha256'] == result['planSha256'] and
                    claim['targetId'] == row['targetId'] and claim['automaticReplayForbidden'] is True, 'Claim differs')
            events = [read(p) for p in sorted((root / 'operations' / row['operationId']).glob('*.json'))]
            require(events[-1]['state'] == result['recordedJournalState'], 'Journal state differs')
            if control:
                summary_path = folder / 'export-summary.json'
                require(sha(summary_path) == result['summarySha256'], 'Summary SHA differs')
                summary = read(summary_path)
                expected_kinds = ['add_selection'] * len(plan['targets']) + ['export_dmt','clear_own_selection']
                require([c['kind'] for c in summary['commands']] == expected_kinds and len(expected_kinds) == calls,
                        'Command sequence differs')
                for index, command in enumerate(summary['commands']):
                    prefix = folder / f'{index:06d}'
                    started_path = Path(str(prefix) + '.started.json')
                    returned_path = Path(str(prefix) + '.returned.json')
                    require(sha(started_path) == command['startedSha256'] and sha(returned_path) == command['returnedSha256'],
                            'Command proof hash differs')
                    started, returned = read(started_path), read(returned_path)
                    require(started['index'] == returned['index'] == index and
                            started['operationId'] == returned['operationId'] == row['operationId'] and
                            returned['startedSha256'] == command['startedSha256'] and returned['callbackReturned'] is True,
                            'Started/returned linkage differs')
                output = Path(plan['targetPath'])
                require(sha(output) == summary['outputSha256'] == result['outputSha256'], 'Output bytes differ')
                data = output.read_bytes()
                offset = data.index(b'\0') + 1
                require(struct.unpack_from('<HIIII',data,offset) == (1000,2,1,3,1) and
                        struct.unpack_from('<9d',data,offset+30) == (0,0,0,1,0,0,0,1,0), 'Synthetic mesh differs')
                require(result['meshLengthUnits'] == 'mm_declared_by_file_flag' and
                        result['meshCoordinateFrame'] == 'file_coordinates_unqualified' and
                        result['selectionRestoredObserved'] is True, 'Wrong qualification/selection result')
                require(events[-1]['state'] == 'executed_unverified' and
                        events[-2]['details']['nativeReturnCodeQualified'] is False, 'Native return falsely qualified')
        if row['fault'] in {'select_throw','wrong_selection','late_document','retained_document','settings_after_select','selection_collision'}:
            require(row['exports'] == row['clears'] == 0, 'Unknown selection continued/cleaned')
        if row['fault'] in {'export_throw','no_output','malformed_output','output_directory','settings_after_export'}:
            require(row['exports'] == 1 and row['clears'] == 0, 'Unknown export retried/cleaned')
    return dict(state='passed',cases=len(FAULTS),artifactHashesChecked=count,cadConnectionAttempted=False,
                nativeProviderQualified=False,fullTopologyVerified=False)


if __name__ == '__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--stage',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    result=audit(args.stage.resolve())
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(f"PASS: {result['cases']} guarded export cases/{result['artifactHashesChecked']} audited hashes; no CAD.")
