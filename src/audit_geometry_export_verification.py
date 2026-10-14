"""Independent linkage/fence/byte audit of diagnostic-only export verification."""
import argparse
import json
from pathlib import Path

from audit_checkpoint_recovered_open import read, require, sha

FAULTS={'control','mixed_targets','receipt_sha','target_sha','summary_sha','step_sha','fence_sha','claim',
        'output_bytes','missing_output','replaced_output','old_inspection','equal_inspection','expired_reads',
        'settings','mapping','late_context','retained_document','wrong_window','foreign_model','unknown_export'}


def audit(stage):
    require((stage/'.geometry-export-verification-fixture').is_file(),'Missing isolated marker')
    report=read(stage/'reports/geometry-export-verification-tests.json')
    require(report['state']=='passed' and report['cadConnectionAttempted'] is False and
            len(report['rows'])==len(FAULTS) and {row['fault'] for row in report['rows']}==FAULTS,'Incomplete matrix')
    count=0
    for row in report['rows']:
        root=Path(row['root']).resolve()
        require(root.is_relative_to(stage/'work'),'Fixture outside own stage')
        hashes={Path(path).resolve():value for path,value in row['artifactHashes'].items()}
        require(set(hashes)=={path.resolve() for path in root.rglob('*') if path.is_file()} and
                all(path.is_relative_to(root) and sha(path)==value for path,value in hashes.items()),'Changed inventory')
        require(all(Path(path).resolve().is_relative_to(root) and sha(Path(path))==value
                    for path,value in row['protectedHashes'].items()),'Verifier changed protected evidence')
        count+=len(hashes)
        result=row['result']
        for flag in ('exportAttempted','contextChangeAttempted','geometryMutationAttempted','cadSaveAttempted',
                     'geometryVerified','nativeProviderQualified','copyFenceReleased','retryAllowed'):
            require(result[flag] is False,'Mutation/topology/native qualification authority')
        control=row['fault'] in {'control','mixed_targets'}
        require(result['state']==('observed_export_file_context_match_unqualified' if control else
                                  'owned_geometry_export_verification_rejected'),'Wrong state')
        require(row['cadConnectionAttempted'] is False,'CAD connection')
        if control:
            path=root/'geometryexportverifications'/(row['verificationId']+'.json')
            require(sha(path)==result['diagnosticSha256'],'Diagnostic SHA differs')
            diagnostic=read(path)
            args=row['parameters']
            for key in ('executionRequestId','executionReceiptSha256','inspectionRequestId','inspectionSha256',
                        'listingRequestId','listingSha256'):
                require(diagnostic[key]==args[key],'Independent references differ')
            for key,sha_key in (('executionRequestId','executionReceiptSha256'),('inspectionRequestId','inspectionSha256'),
                               ('listingRequestId','listingSha256')):
                require(sha(root/'responses'/(args[key]+'.json'))==args[sha_key],'Source proof SHA differs')
            receipt=read(root/'responses'/(row['executionId']+'.json'))
            require(receipt['state']=='owned_geometry_export_executed_unverified','Unknown treated as success')
            plan=read(root/'geometryexportexecutions'/(row['executionId']+'.json'))
            folder=root/'geometryexports'/row['targetId']
            summary=read(folder/'export-summary.json')
            require(sha(folder/'export-summary.json')==receipt['summarySha256'],'Summary linkage differs')
            output=Path(plan['targetPath'])
            require(output.is_relative_to(root) and sha(output)==receipt['outputSha256']==diagnostic['outputSha256'],
                    'Independent file SHA differs')
            require(summary['outputFileIdentity']==diagnostic['outputFileIdentity'],'Independent file identity differs')
            require(diagnostic['geometryVerified'] is False and diagnostic['nativeProviderQualified'] is False and
                    diagnostic['copyFenceReleased'] is False and diagnostic['coordinateFrame']=='file_coordinates_unqualified',
                    'Unsupported success claim')
            fence=root/'workingcopies'/plan['copyId']/'geometry-export-fence.json'
            require(sha(fence)==receipt['exportFenceSha256'] and read(fence)['operationId']==row['executionId'],
                    'Original fence removed or changed')
            events=[read(p) for p in sorted((root/'operations'/row['executionId']).glob('*.json'))]
            require(events[-1]['state']=='executed_unverified','Diagnostic advanced execution journal')
            returned=next(event for event in events if event['state']=='command_returned')
            inspection=read(root/'responses'/(args['inspectionRequestId']+'.json'))
            listing=read(root/'responses'/(args['listingRequestId']+'.json'))
            require(inspection['timestampUtc']>returned['timestampUtc'] and listing['timestampUtc']>returned['timestampUtc'],
                    'Independent reads not strictly later')
            require(row['verificationReads']==4 and result['fileAndContextObservedMatch'] is True,'Missing retained read guards')
        else:
            require(not (root/'geometryexportverifications'/(row['verificationId']+'.json')).exists(),
                    'Rejected verification published success diagnostic')
    return dict(state='passed',cases=len(FAULTS),artifactHashesChecked=count,cadConnectionAttempted=False,
                copyFenceReleased=False,nativeProviderQualified=False,geometryVerified=False)


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--stage',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    result=audit(args.stage.resolve())
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(f"PASS: {result['cases']} independent export verifications/{result['artifactHashesChecked']} hashes; no CAD/fence release.")
