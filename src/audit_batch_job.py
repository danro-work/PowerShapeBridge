"""Independent file audit and geometric comparison of the batch qualification."""
import argparse,json
from pathlib import Path
from slice_job import file_hash,save,technological_grid

def signature(section):
    return (section['zWorldMm'],section['totalLengthMm'],tuple(sorted(section['uniquePerimeterEdges'])),tuple(sorted((tuple(c['boundsWorldMm']),c['lengthMm'],c['closed'],c['areaRaw']) for c in section['curves'])))

def audit(root,output,reference):
    report_path=output/'batch-job.json';report=json.loads(report_path.read_text(encoding='utf-8-sig'))
    if report['state']!='succeeded_single_intersection_batch_verified' or report['intersectionInvocationCount']!=1 or report['planeCount']!=25:raise ValueError('Completed single-intersection batch required')
    if [p['phase'] for p in report['phases']]!=['planes','intersect','export','verify']:raise ValueError('Phase order differs')
    evidence={};results={};ids=set()
    for record in report['phases']:
        if file_hash(record['responsePath'])!=record['responseSha256']:raise ValueError('Native response changed')
        path=record['responsePath'];hash_value=record['responseSha256']
        if record['state']=='succeeded_by_explicit_recovery':
            path=record['recoveryResponsePath'];hash_value=record['recoveryResponseSha256']
            if file_hash(path)!=hash_value:raise ValueError('Recovery response changed')
        result=json.loads(Path(path).read_text(encoding='utf-8-sig'))
        if result['state']!='succeeded_batch_section_phase' or result['batchId']!=report['batchId'] or result['phase']!='completed' or not result['testWindowClosed'] or not result['protectedSourceHashesUnchanged']:raise ValueError('Phase identity/context differs')
        if result['requestId'] in ids:raise ValueError('Request repeated')
        ids.add(result['requestId']);results[record['phase']]=result
        if record['state']=='succeeded_by_explicit_recovery' and (result['recoverySourceRequestId']!=record['requestId'] or result['recoverySourceJournalSha256']!=record['responseSha256'] or result['existingPlanesRecreated'] is not False):raise ValueError('Recovery identity/replay evidence differs')
        for key,hash_key in [('modelPath','modelSha256'),('sourcePath','sourceSha256'),('dgkPath','dgkSha256')]:
            if key in result:
                if file_hash(result[key])!=result[hash_key]:raise ValueError('Geometry file changed')
                evidence[result[key]]=result[hash_key]
    grid=technological_grid(20,.8);planes=results['planes']
    if planes['measuredElementHeightMm']!=20 or len(planes['planeSet'])!=25:raise ValueError('Plane count/height differs')
    for n,plane in enumerate(planes['planeSet']):
        if plane['type']!=21 or plane['boundsWorldMm']!=[-20,20,-20,20,grid[n],grid[n]]:raise ValueError('Plane geometry differs')
    intersection=results['intersect'];verified=results['verify']
    if intersection['primaryCount']!=4 or intersection['secondaryCount']!=25 or not intersection['temporaryPlanesRemoved'] or not verified['allSectionsVerifiedAfterDiskReopen']:raise ValueError('Intersection/cleanup/verification differs')
    reference_report=json.loads(reference.read_text(encoding='utf-8-sig'))
    reference_sections=reference_report['verifiedSections']
    for sections in (intersection['sections'],verified['sectionsAfterImport'],verified['sectionsAfterDiskReopen'],report['verifiedSections']):
        if len(sections)!=25 or [signature(s) for s in sections]!=[signature(s) for s in reference_sections]:raise ValueError('Batch geometry differs from independently reopened sequential result')
    if file_hash(report['publishedDgkPath'])!=report['dgkSha256'] or len(list(output.glob('*.dgk')))!=1:raise ValueError('Published DGK differs')
    config=json.loads((root/'bridge.config.json').read_text(encoding='utf-8-sig'))
    for key,hash_key in [('expectedProject','testProjectSha256'),('protectedProject','protectedProjectSha256')]:
        if file_hash(config[key])!=config[hash_key]:raise ValueError('Protected project changed')
    result={'state':'succeeded_batch_job_independent_audit','batchId':report['batchId'],'reportSha256':file_hash(report_path),'referenceReportSha256':file_hash(reference),'planeCount':25,'intersectionInvocationCount':1,'sectionCount':25,'curveCount':report['curveCount'],'planeArrayInvocationCount':planes.get('planeArrayInvocationCount',0),'planeCreationMilliseconds':planes['planeCreationMilliseconds'],'intersectionMilliseconds':intersection['intersectionMilliseconds'],'allSectionMeasuredGeometryMatchesSequentialResult':True,'temporaryPlanesRemoved':True,'onePublishedDgk':True,'allEvidenceHashesChecked':True,'allKnownPhaseWindowsClosed':True,'protectedProjectsUnchanged':True,'geometryFiles':evidence,'scope':'Planar square fixture and measured properties; no full-topology, arbitrary-model or production-performance certification.'}
    save(output/'independent-audit.json',result);return result

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--bridge-root',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--reference',type=Path,required=True)
    args=parser.parse_args();print(json.dumps(audit(args.bridge_root.resolve(),args.output.resolve(),args.reference.resolve()),ensure_ascii=False,indent=2))
