"""Export all heights of each source level together. Cube-only live qualification."""
import argparse
import json
import re
import time
import uuid
from pathlib import Path
from slice_job import file_hash, save, verify_receipt, McpClient

def dgk_filename(level):
    if not isinstance(level,str) or not level.strip() or level!=level.strip() or len(level)>100:
        raise ValueError('Invalid source level name')
    name=level.replace(':','-')
    if name in ('.','..') or name.endswith('.') or re.search(r'[<>"/\\|?*\x00-\x1f\x7f\x80-\x9f\x27]',name):
        raise ValueError('Unsafe DGK filename')
    if name.split('.')[0].rstrip().upper() in {'CON','PRN','AUX','NUL',*[f'COM{i}' for i in range(1,10)],*[f'LPT{i}' for i in range(1,10)]}:
        raise ValueError('Reserved DGK filename')
    return name+'.dgk'

def groups_by_source_level(contours):
    groups={}
    filenames={}
    for contour in contours:
        level=contour['sourceLevel']
        filename=dgk_filename(level)
        key=filename.casefold()
        if key in filenames and filenames[key]!=level:
            raise ValueError('Two source levels map to the same Windows filename')
        filenames[key]=level
        groups.setdefault(level,{'sourceLevel':level,'fileName':filename,'contours':[]})['contours'].append(contour)
    # Z is metadata within a group; it never defines output-file grouping.
    return list(groups.values())

def queue_phase(root, arguments, record):
    identity=record['requestId']
    record.update(requestId=identity,state='requested_outcome_pending')
    path=root/'requests'/(identity+'.json')
    temporary=path.with_suffix('.tmp')
    temporary.write_text(json.dumps({'requestId':identity,'operation':'recipe.prepare','arguments':{'recipeId':'test_contour_group_phase','parameters':arguments}},allow_nan=False),encoding='utf-8')
    temporary.replace(path)
    end=time.monotonic()+90
    response=root/'responses'/(identity+'.json')
    while not response.exists():
        if time.monotonic()>end: raise TimeoutError('Group phase uncertain; do not resend')
        time.sleep(.2)
    result=json.loads(response.read_text(encoding='utf-8-sig'))
    if result.get('state')!='succeeded_group_phase':
        raise RuntimeError(json.dumps(result,ensure_ascii=False))
    record.update(state='succeeded_group_phase',responsePath=str(response),responseSha256=file_hash(response))
    return result

def validate_job(root, job):
    if job.get('state')!='succeeded_all_layers_verified' or job.get('scope')!='Qualified unscaled cube only; not a production technological model':
        raise ValueError('Only the completed qualified cube fixture accepted')
    for batch in job['batches']:
        receipt_path=root/'operations'/(batch['operationId']+'.json')
        receipt=json.loads(receipt_path.read_text(encoding='utf-8-sig'))
        if batch['state']=='succeeded_verified':
            if file_hash(receipt_path)!=batch['receiptSha256']:raise ValueError('Source journal changed')
            verify_receipt(root,batch['operationId'],receipt)
        elif batch['state']=='succeeded_verified_by_independent_recovery':
            verification_path=root/'reports'/('slice-verification-'+batch['verificationId']+'.json')
            verification=json.loads(verification_path.read_text(encoding='utf-8-sig'))
            if verification['state']!='succeeded_existing_slice_batch_verified' or verification['sourceOperationId']!=batch['operationId'] or file_hash(verification_path)!=batch['verificationReceiptSha256'] or file_hash(receipt_path)!=batch['originalUncertainReceiptSha256'] or file_hash(receipt['resultPath'])!=verification['sourceResultSha256'] or file_hash(receipt['checkpointPath'])!=verification['sourceCheckpointSha256']:
                raise ValueError('Recovery evidence changed')
        else: raise ValueError('Incomplete source batch')
    if [row['zWorldMm'] for row in job['layers']]!=job['heightsWorldMm']:
        raise ValueError('Source grid differs')
    for row in job['layers']:
        if file_hash(row['dgkPath'])!=row['dgkSha256'] or file_hash(row['importedProjectPath'])!=row['importedProjectSha256']:
            raise ValueError('Verified source files changed')

def run(root, job_path, output, execute=False):
    source=json.loads(job_path.read_text(encoding='utf-8-sig'))
    validate_job(root,source)
    contours=[dict(row,sourceLevel='0:General') for row in source['layers']]
    groups=groups_by_source_level(contours)
    output.mkdir(parents=True,exist_ok=False)
    report={'schemaVersion':1,'state':'planned','sourceJobPath':str(job_path),'sourceJobSha256':file_hash(job_path),'groupingRule':'One DGK per source working-surface level, all section heights in that file; colon becomes hyphen; spaces preserved','scope':'Qualified cube level 0:General. Real working-surface selection remains a separate production qualification.','groups':groups,'phases':[]}
    report_path=output/'grouped-export.json';save(report_path,report)
    if not execute:return report
    client=None
    try:
        client=McpClient(root);client.call('activate_test_baseline',{})
        for group in groups:
            identity=uuid.uuid4().hex;group['groupId']=identity
            plan={'groupId':identity,'sourceLevel':group['sourceLevel'],'curves':[{'sourceOperationId':c['sourceOperationId'],'dgkPath':c['dgkPath'],'dgkSha256':c['dgkSha256'],'zWorldMm':c['zWorldMm']} for c in group['contours']]}
            plans=root/'groups';plans.mkdir(exist_ok=True);plan_path=plans/(identity+'.json')
            with plan_path.open('x',encoding='utf-8') as stream:json.dump(plan,stream,allow_nan=False,indent=2)
            digest=file_hash(plan_path);group.update(planPath=str(plan_path),planSha256=digest)
            phases=[('append',i) for i in range(len(plan['curves']))]+[('export',0),('import',0)]+[('verify',i) for i in range(0,len(plan['curves']),2)]
            readings=[]
            for phase,index in phases:
                record={'groupId':identity,'phase':phase,'index':index,'requestId':uuid.uuid4().hex,'state':'prepared'}
                report['phases'].append(record);report['state']='running';save(report_path,report)
                # Persist pending state before sending; never replay unknown phases.
                record['state']='requested_outcome_pending';save(report_path,report)
                result=queue_phase(root,{'groupId':identity,'planSha256':digest,'phase':phase,'index':index},record)
                if phase=='export':group.update(dgkPath=result['dgkPath'],dgkSha256=result['dgkSha256'],spaceProbePath=result['spaceProbePath'],spaceProbeSha256=result['spaceProbeSha256'])
                if phase=='import':group.update(importedProjectPath=result['modelPath'],importedProjectSha256=result['modelSha256'])
                if phase=='verify':readings.extend(result['verifiedCurves'])
                save(report_path,report)
                print(f'{phase} {index+1}: {group["sourceLevel"]}',flush=True)
            if [r['zWorldMm'] for r in readings]!=source['heightsWorldMm'] or len(readings)!=len(group['contours']):raise ValueError('Final group coverage differs')
            group.update(state='succeeded_grouped_dgk_reimported_verified',verifiedCurves=readings,curveCount=len(readings))
            final=output/group['fileName']
            with final.open('xb') as stream:stream.write(Path(group['dgkPath']).read_bytes())
            if file_hash(final)!=group['dgkSha256']:raise ValueError('Published DGK differs')
            group['publishedDgkPath']=str(final)
        validate_job(root,source)
        report['state']='succeeded_grouped_dgk_reimported_verified'
        report['qualification']='All cube contours per source level exported together, DGK reimported, every contour inspected after disk reopening on independent copies. No complete production TM or arbitrary curve topology certified.'
        save(report_path,report);return report
    except Exception as error:
        report.update(state='stopped_requires_inspection',error=str(error),recovery='Inspect phase receipt; no automatic replay of an uncertain phase.')
        save(report_path,report);raise
    finally:
        if client:client.close()

if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--bridge-root',type=Path,required=True)
    parser.add_argument('--source-job',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--execute',action='store_true')
    args=parser.parse_args()
    print(run(args.bridge_root.resolve(),args.source_job.resolve(),args.output.resolve(),args.execute)['state'])
