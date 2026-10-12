"""Internal qualification runner; uncertain phase outcomes are never replayed."""
import argparse
import json
import shutil
import time
import uuid
from pathlib import Path
from slice_job import McpClient,file_hash,save

def run(root,source_job,output,resume_planes=None,native_array=False):
    source=root.parent.parent/'P2_Disk_Verification'/('SurfaceJob_'+source_job)/'setup.json'
    path=output/'batch-job.json'
    if resume_planes:
        report=json.loads(path.read_text(encoding='utf-8-sig'));batch=report['batchId']
        if report['state']!='stopped_requires_inspection' or report['sourceJobId']!=source_job or len(report['phases'])!=1 or report['phases'][0]['phase']!='planes':raise ValueError('Only the inspected planes phase can be resumed')
        recovery_path=root/'responses'/(resume_planes+'.json');recovered=json.loads(recovery_path.read_text(encoding='utf-8-sig'))
        old=report['phases'][0]
        if file_hash(old['responsePath'])!=old['responseSha256'] or recovered.get('state')!='succeeded_batch_section_phase' or recovered.get('batchId')!=batch or recovered.get('recoverySourceRequestId')!=old['requestId'] or recovered.get('existingPlanesRecreated') is not False or recovered.get('planeCount')!=25 or file_hash(recovered['modelPath'])!=recovered['modelSha256'] or recovered['recoverySourceJournalSha256']!=old['responseSha256']:raise ValueError('Plane recovery evidence differs')
        with (output/'batch-job.stopped-before-planes-recovery.json').open('xb') as stream:stream.write(path.read_bytes())
        old.update(state='succeeded_by_explicit_recovery',recoveryResponsePath=str(recovery_path),recoveryResponseSha256=file_hash(recovery_path))
        report.update(planeCount=25,planeCreationMilliseconds=recovered['planeCreationMilliseconds'],planeCreationMethod=recovered['planeCreationMethod'],planeArrayInvocationCount=recovered.get('planeArrayInvocationCount',0),measuredElementHeightMm=recovered['measuredElementHeightMm'],recovery='Existing plane inspected and retained; remaining planes created; no intersection performed during recovery.')
        report.pop('error',None)
    else:
        output.mkdir(parents=True,exist_ok=False);batch=uuid.uuid4().hex
        report={'schemaVersion':1,'batchId':batch,'sourceJobId':source_job,'sourceReceiptSha256':file_hash(source),'state':'planned','phases':[],
                'nativePlaneArray':native_array,'scope':'Four planar walls fixture, world XY; internal qualification, not a public general MCP operation.'}
    save(path,report)
    client=None
    try:
        client=McpClient(root);client.call('activate_test_baseline',{})
        for phase in ('planes','intersect','export','verify'):
            if phase=='planes' and resume_planes:continue
            identity=uuid.uuid4().hex;record={'phase':phase,'requestId':identity,'state':'requested_outcome_pending'}
            report['state']='running';report['phases'].append(record);save(path,report)
            args={'batchId':batch,'sourceJobId':source_job,'sourceReceiptSha256':report['sourceReceiptSha256'],'phase':'planes_array' if phase=='planes' and native_array else phase}
            request={'requestId':identity,'operation':'recipe.prepare','arguments':{'recipeId':'qualify_batch_section_phase','parameters':args}}
            temporary=root/'requests'/(identity+'.tmp');queued=temporary.with_suffix('.json')
            temporary.write_text(json.dumps(request,allow_nan=False),encoding='utf-8');temporary.replace(queued)
            response=root/'responses'/(identity+'.json');started=time.monotonic()
            while not response.exists():
                if time.monotonic()-started>90:raise TimeoutError('Unknown batch phase outcome; no retry')
                time.sleep(.2)
            result=json.loads(response.read_text(encoding='utf-8-sig'))
            record.update(responsePath=str(response),responseSha256=file_hash(response),elapsedSeconds=round(time.monotonic()-started,3),resultState=result.get('state'))
            save(path,report)
            if result.get('state')!='succeeded_batch_section_phase' or result.get('requestId')!=identity or result.get('batchId')!=batch:raise RuntimeError(json.dumps(result,ensure_ascii=False))
            record['state']='succeeded_batch_section_phase'
            if phase=='planes':report.update(planeCount=result['planeCount'],planeCreationMilliseconds=result['planeCreationMilliseconds'],measuredElementHeightMm=result['measuredElementHeightMm'],planeCreationMethod=result['planeCreationMethod'],planeArrayInvocationCount=result.get('planeArrayInvocationCount',0))
            if phase=='intersect':report.update(curveCount=result['curveCount'],intersectionInvocationCount=result['intersectionInvocationCount'],intersectionMilliseconds=result['intersectionMilliseconds'],sections=result['sections'],temporaryPlanesRemoved=result['temporaryPlanesRemoved'])
            if phase=='export':report.update(dgkPath=result['dgkPath'],dgkSha256=result['dgkSha256'])
            if phase=='verify':report['verifiedSections']=result['sectionsAfterDiskReopen']
            save(path,report);print(phase+': '+str(record['elapsedSeconds'])+' s',flush=True)
        target=output/'1-Vneshnie 0.8.dgk'
        with target.open('xb') as stream:stream.write(Path(report['dgkPath']).read_bytes())
        if file_hash(target)!=report['dgkSha256']:raise ValueError('Published DGK differs')
        report.update(state='succeeded_single_intersection_batch_verified',publishedDgkPath=str(target),sectionCount=len(report['verifiedSections']))
        save(path,report);return report
    except Exception as error:
        report.update(state='stopped_requires_inspection',error=str(error),recovery='Inspect request and native working-copy journal. Do not repeat an uncertain phase.')
        save(path,report);raise
    finally:
        if client:client.close()

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--bridge-root',type=Path,required=True);parser.add_argument('--source-job',required=True);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--resume-planes',help='Explicit successful recovery response ID; never repeats an uncertain intersection');parser.add_argument('--native-array',action='store_true')
    args=parser.parse_args()
    if len(args.source_job)!=32 or any(c not in '0123456789abcdef' for c in args.source_job):parser.error('source job must be a lowercase GUID without separators')
    if args.resume_planes and (len(args.resume_planes)!=32 or any(c not in '0123456789abcdef' for c in args.resume_planes)):parser.error('Invalid recovery response ID')
    if args.resume_planes and args.native_array:parser.error('Recovery of the bounded-creation run cannot switch to array mode')
    print(run(args.bridge_root.resolve(),args.source_job,args.output.resolve(),args.resume_planes,args.native_array)['state'])
