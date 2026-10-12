"""Independent, read-only audit of a completed public-MCP surface qualification."""
import argparse
import json
from pathlib import Path
from slice_job import file_hash,save,technological_grid

def audit(root,output):
    report_path=output/'surface-job.json'
    report=json.loads(report_path.read_text(encoding='utf-8-sig'))
    if report.get('state')!='succeeded_working_surface_slices_grouped_dgk_verified' or report.get('executionChannel')!='public_mcp_stdio':
        raise ValueError('Completed public-MCP job required')
    grid=technological_grid(20,.8)
    if report['heightsWorldMm']!=grid or file_hash(report['planPath'])!=report['planSha256']:
        raise ValueError('Plan grid/hash changed')
    phases=[('setup',0)]+[('slice',i) for i in range(25)]+[('verify_surfaces',0),('export',0),('import',0)]+[('verify',i) for i in range(25)]
    if [(r['phase'],r['index']) for r in report['phases']]!=phases:raise ValueError('Phase sequence differs')
    ids=set();sections=[];files={};sources=set()
    for record in report['controlCalls']+report['phases']:
        path=Path(record['mcpResponsePath'])
        if file_hash(path)!=record['mcpResponseSha256']:raise ValueError('MCP response changed')
        mcp=json.loads(path.read_text(encoding='utf-8-sig'))
        if mcp.get('isError'):raise ValueError('Unsuccessful MCP response')
        result=mcp['structuredContent']
        if 'phase' not in record:continue
        if record['tool']!='execute_test_surface_job_phase' or record['state']!='succeeded_surface_section_phase':raise ValueError('Phase bypassed MCP or did not succeed')
        identity=record['requestId']
        if identity in ids or result.get('requestId')!=identity or result.get('bridgeRequestId')!=identity:raise ValueError('Phase request identity differs or repeats')
        ids.add(identity)
        if file_hash(record['responsePath'])!=record['responseSha256']:raise ValueError('Bridge response changed')
        bridge=json.loads(Path(record['responsePath']).read_text(encoding='utf-8-sig'))
        if {k:v for k,v in result.items() if k!='bridgeRequestId'}!=bridge:raise ValueError('MCP payload differs from Bridge response')
        if result['jobId']!=report['jobId'] or result['planSha256']!=report['planSha256'] or result['phase']!='completed' or not result['testWindowClosed'] or not result['protectedSourceHashesUnchanged']:raise ValueError('Phase identity/context verification differs')
        sources.add((result['sourcePath'],result['sourceSha256']))
        for pathkey,hashkey in [('modelPath','modelSha256'),('dgkPath','dgkSha256'),('sourcePath','sourceSha256')]:
            if pathkey in result:
                path=result[pathkey];digest=result[hashkey]
                if file_hash(path)!=digest:raise ValueError('Geometry file changed: '+path)
                files[path]=digest
        if record['phase']=='slice':
            if len(result['selectedWorkingSurfaces'])!=4 or len(result['excludedAuxiliaryEntities'])!=2 or result['zWorldMm']!=grid[record['index']]:raise ValueError('Working-surface selection/height differs')
            if any(c['type']!=21 or c['levelNumber']!=1 for c in result['selectedWorkingSurfaces']):raise ValueError('Nonworking entity selected')
        if record['phase']=='import':
            if result['curveCount']!=25 or result['surfacesInDgk']!=0 or any(c['type']!=59 for c in result['inventory']):raise ValueError('Whole DGK inventory differs')
        if record['phase']=='verify_surfaces' and not result['sourceSurfacesUnchangedOnFinalDiskReopen']:raise ValueError('Source surfaces verification failed')
        if record['phase']=='verify':
            section=result['section'];sections.append(section)
            if section['zWorldMm']!=grid[record['index']] or section['totalLengthMm']!=80 or len(section['uniquePerimeterEdges'])!=4:raise ValueError('Section geometry differs')
    if sections!=report['verifiedSections'] or len(sources)!=1:raise ValueError('Reported sections/source differ')
    if file_hash(report['publishedDgkPath'])!=report['dgkSha256'] or len(list(output.glob('*.dgk')))!=1:raise ValueError('Published group file differs')
    config=json.loads((root/'bridge.config.json').read_text(encoding='utf-8-sig'))
    protected={}
    for pathkey,hashkey in [('expectedProject','testProjectSha256'),('protectedProject','protectedProjectSha256')]:
        path=config[pathkey];digest=file_hash(path)
        if digest!=config[hashkey]:raise ValueError('Protected model changed')
        protected[path]=digest
    audit_report={'state':'succeeded_public_mcp_surface_job_independent_audit','jobId':report['jobId'],'jobReportSha256':file_hash(report_path),'publicMcpPhaseCount':len(phases),'sectionCount':len(sections),'heightRangeWorldMm':[grid[0],grid[-1]],'stepMm':.8,'oneDgkPerSourceLevel':True,'allMcpAndBridgeResponsesMatched':True,'allRecordedFileHashesVerified':True,'allKnownPhaseWindowsClosed':True,'protectedFiles':protected,'geometryFiles':files,'scope':'Fixed four planar walls, square perimeter and measured source properties; not arbitrary production geometry or full topology.'}
    save(output/'independent-audit.json',audit_report)
    return audit_report

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--bridge-root',type=Path,required=True);parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();print(json.dumps(audit(args.bridge_root.resolve(),args.output.resolve()),ensure_ascii=False,indent=2))
