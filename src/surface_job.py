"""Autonomous qualification through public MCP tools, one DGK per source level."""
import argparse
import json
import uuid
from pathlib import Path
from slice_job import McpClient, file_hash, save, technological_grid

def public_call(client, name, arguments, output, report, record):
    """Persist intent and the full MCP response, including unsuccessful outcomes."""
    record.update(tool=name,arguments=arguments,state='requested_outcome_pending')
    save(output/'surface-job.json',report)
    result=client.request('tools/call',{'name':name,'arguments':arguments})
    folder=output/'mcp-responses';folder.mkdir(exist_ok=True)
    path=folder/(uuid.uuid4().hex+'.json');save(path,result)
    data=result.get('structuredContent',{})
    record.update(mcpResponsePath=str(path),mcpResponseSha256=file_hash(path),resultState=data.get('state'))
    if data.get('bridgeRequestId'):
        identity=data['bridgeRequestId'];response=client.root/'responses'/(identity+'.json')
        record['requestId']=identity
        if response.is_file():record.update(responsePath=str(response),responseSha256=file_hash(response))
    save(output/'surface-job.json',report)
    if result.get('isError') or not isinstance(data,dict) or not data:
        raise RuntimeError('MCP '+name+': '+json.dumps(data or result,ensure_ascii=False))
    return data

def run(root, output, resume=False, client_factory=McpClient):
    identity=uuid.uuid4().hex
    source_receipt=root/'reports/exchange-ce7c3a8f6fb8410c822ebee8552c780a.json'
    heights=technological_grid(20,.8)
    report_path=output/'surface-job.json'
    if resume:
        report=json.loads(report_path.read_text(encoding='utf-8-sig'))
        assert report['state']=='stopped_requires_inspection' and report['heightsWorldMm']==heights
        identity=report['jobId'];path=root/'surfacejobs'/(identity+'.json');digest=report['planSha256']
        assert file_hash(path)==digest
        plan=json.loads(path.read_text(encoding='utf-8-sig'));assert plan['sourceReceiptSha256']==file_hash(source_receipt)
        for record in report['phases']:
            assert file_hash(record['responsePath'])==record['responseSha256']
            if 'mcpResponsePath' in record:assert file_hash(record['mcpResponsePath'])==record['mcpResponseSha256']
            if record['state']=='succeeded_by_explicit_recovery':
                assert file_hash(record['recoveryResponsePath'])==record['recoveryResponseSha256']
                recovered=json.loads(Path(record['recoveryResponsePath']).read_text(encoding='utf-8-sig'))
                assert recovered['state']=='succeeded_surface_section_phase' and recovered['intersectionCommandRepeated'] is False and recovered['recoverySourceRequestId']==record['requestId']
                if 'modelPath' in recovered:assert file_hash(recovered['modelPath'])==recovered['modelSha256']
                else:assert file_hash(recovered['dgkPath'])==recovered['dgkSha256']
            else:assert record['state']=='succeeded_surface_section_phase'
        report.pop('error',None);report['resumeQualification']='Continue remaining phases only; existing zero section recovered without repeating its intersection.'
    else:
        output.mkdir(parents=True,exist_ok=False)
        report={'schemaVersion':2,'state':'preparing','sourceLevel':'1:Vneshnie 0.8','contourLevelNumber':101,'contourLevelName':'1k:Vneshnie 0.8','stepMm':.8,'heightsWorldMm':heights,'phases':[],'controlCalls':[],'executionChannel':'public_mcp_stdio','scope':'Fixed four planar cube wall surfaces; caps on auxiliary level; not a production TM.'}
    save(report_path,report)
    client=None
    try:
        client=client_factory(root);client.root=root
        if not resume:
            record={};report['controlCalls'].append(record)
            prepared=public_call(client,'prepare_test_surface_job',{},output,report,record)
            if prepared.get('state')!='surface_job_plan_ready' or prepared['plan']['heightsWorldMm']!=heights:
                raise ValueError('Unexpected MCP surface plan')
            identity=prepared['jobId'];path=Path(prepared['planPath']);digest=prepared['planSha256']
            if path.resolve()!=(root/'surfacejobs'/(identity+'.json')).resolve() or file_hash(path)!=digest:
                raise ValueError('MCP surface plan path/hash differs')
            record['state']='surface_job_plan_ready'
            report.update(jobId=identity,planPath=str(path),planSha256=digest)
        record={};report.setdefault('controlCalls',[]).append(record)
        activated=public_call(client,'activate_test_baseline',{},output,report,record)
        if activated.get('state')!='baseline_active':raise ValueError('Baseline activation failed')
        record['state']='baseline_active'
        phases=[('setup',0)]+[('slice',n) for n in range(len(heights))]+[('verify_surfaces',0),('export',0),('import',0)]+[('verify',n) for n in range(len(heights))]
        readings=[]
        for completed in report['phases']:
            if completed['phase']=='verify':
                restored=json.loads(Path(completed['responsePath']).read_text(encoding='utf-8-sig'))
                readings.append(restored['section'])
        for phase,index in phases:
            if any(record['phase']==phase and record['index']==index for record in report['phases']):continue
            record={'phase':phase,'index':index,'state':'requested_outcome_pending'}
            report['state']='running';report['phases'].append(record);save(report_path,report)
            result=public_call(client,'execute_test_surface_job_phase',{'jobId':identity,'planSha256':digest,'phase':phase,'index':index},output,report,record)
            if result.get('state')!='succeeded_surface_section_phase' or result.get('jobId')!=identity or result.get('requestId')!=record.get('requestId'):raise RuntimeError(json.dumps(result,ensure_ascii=False))
            record['state']='succeeded_surface_section_phase'
            if phase=='setup':report.update(fixturePath=result['modelPath'],fixtureSha256=result['modelSha256'])
            if phase=='slice':
                record.update(zWorldMm=result['zWorldMm'],curveCount=result['createdCurveCount'])
                assert len(result['selectedWorkingSurfaces'])==4 and len(result['excludedAuxiliaryEntities'])==2
            if phase=='export':report.update(dgkPath=result['dgkPath'],dgkSha256=result['dgkSha256'],curveCount=result['curveCount'])
            if phase=='import':report.update(importedProjectPath=result['modelPath'],importedProjectSha256=result['modelSha256'])
            if phase=='verify':readings.append(result['section'])
            if phase=='verify_surfaces':report['sourceSurfacesUnchangedOnFinalDiskReopen']=result['sourceSurfacesUnchangedOnFinalDiskReopen']
            save(report_path,report)
            print(f'{phase} {index+1}: '+(str(result.get('zWorldMm')) if phase in ('slice','verify') else 'done'),flush=True)
        assert [r['zWorldMm'] for r in readings]==heights
        assert len(readings)==25 and all(r['totalLengthMm']==80 for r in readings)
        final=output/'1-Vneshnie 0.8.dgk'
        with final.open('xb') as stream:stream.write(Path(report['dgkPath']).read_bytes())
        assert file_hash(final)==report['dgkSha256']
        report.update(state='succeeded_working_surface_slices_grouped_dgk_verified',publishedDgkPath=str(final),verifiedSections=readings,sectionCount=len(readings),qualifications='Source type/level selection, separate contour level, whole-group DGK, only curves in import, all heights/edges inspected after disk reopen. Arbitrary surfaces and complete TM not certified.')
        save(report_path,report);return report
    except Exception as error:
        report.update(state='stopped_requires_inspection',error=str(error),recovery='Inspect recorded phase and its independent working copy; no uncertain phase replay.')
        save(report_path,report);raise
    finally:
        if client:client.close()

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--bridge-root',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--resume-after-recovery',action='store_true')
    args=parser.parse_args();print(run(args.bridge_root.resolve(),args.output.resolve(),args.resume_after_recovery)['state'])

