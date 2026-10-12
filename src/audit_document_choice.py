"""Audit public document tool results against native receipts and operation journals."""
import argparse
import copy
import json
from pathlib import Path

from slice_job import file_hash,save


def audit(root, report_path):
    report=json.loads(report_path.read_text(encoding='utf-8'))
    if report.get('state')!='succeeded_document_listing_and_verified_choice':
        raise ValueError('Document qualification did not complete')
    fingerprints={str(report_path):file_hash(report_path)}
    counts={'list':0,'inspect':0,'activated':0,'noop':0,'rejectedNative':0,'rejectedConsumed':0,'controls':0}
    def read(path):
        fingerprints[str(path)]=file_hash(path)
        return json.loads(path.read_text(encoding='utf-8-sig'))
    def native(data):
        identity=data.get('bridgeRequestId')
        if not isinstance(identity,str) or len(identity)!=32 or any(x not in '0123456789abcdef' for x in identity):
            raise ValueError('Invalid Bridge response identity')
        return read(root/'responses'/(identity+'.json'))
    initial=None
    final=None
    surface_inspections=[]
    all_saved_hashes={}
    for record in report['records']:
        if 'fixtureControl' in record:
            data=record['result']
            expected=dict(data)
            expected.pop('bridgeRequestId')
            if native(data)!=expected or data['geometryMutationAttempted'] is not False or not data.get('protectedProjectHashesUnchanged'):
                raise ValueError('Fixture control response differs')
            counts['controls']+=1
            continue
        name=record['tool']
        data=record['result']['structuredContent']
        if name=='list_documents':
            expected=copy.deepcopy(data)
            for key in ('bridgeRequestId','documentHandleLifetimeSeconds','documentHandleQualification'):
                expected.pop(key,None)
            for document in expected['documents']:document.pop('documentHandle')
            if native(data)!=expected or data['contextChangeAttempted'] is not False:
                raise ValueError('Document listing differs or changed context')
            if initial is None:initial=data
            final=data
            counts['list']+=1
        elif name=='inspect_active_model':
            expected=copy.deepcopy(data)
            for key in ('bridgeRequestId','documentReference','observedPropertiesSha256','referenceLifetimeSeconds','referenceQualification'):
                expected.pop(key,None)
            for entity in expected['entities']:entity.pop('entityReference')
            expected['state']='succeeded_verified'
            if native(data)!=expected or expected.get('viewportCapture'):
                raise ValueError('Model inspection differs or created image')
            if data['countsByType']=={'SURFACE':6}:surface_inspections.append(expected['entities'])
            counts['inspect']+=1
        elif name=='activate_document':
            if data['state']=='document_choice_rejected':
                if not record['result'].get('isError') or data['contextChangeAttempted'] is not False:
                    raise ValueError('Consumed choice was accepted')
                counts['rejectedConsumed']+=1
                continue
            expected=dict(data)
            expected.pop('bridgeRequestId')
            if expected.pop('documentHandleConsumed') is not True or native(data)!=expected:
                raise ValueError('Activation response differs or choice not consumed')
            journal=read(root/'operations'/(data['operationId']+'.json'))
            if journal!=expected:
                raise ValueError('Activation journal differs from response')
            request=read(root/'requests'/(data['bridgeRequestId']+'.json.processing.done'))
            args=request['arguments']['parameters']
            if request['operation']!='recipe.prepare' or request['arguments']['recipeId']!='activate_document':
                raise ValueError('Activation used an unexpected native request')
            receipt=root/'responses'/(args['listingRequestId']+'.json')
            listed=read(receipt)
            if file_hash(receipt)!=args['listingSha256']:
                raise ValueError('Lease listing digest differs')
            candidates=[d for d in listed['documents'] if d['modelId']==args['modelId'] and d['windowNumber']==args['windowNumber'] and d['activationSupported']]
            if len(candidates)!=1:
                raise ValueError('Chosen document absent/ambiguous in source listing')
            if data['state']=='rejected_before_context_change':
                if data['contextChangeAttempted'] is not False:
                    raise ValueError('Stale lease changed context')
                counts['rejectedNative']+=1
                continue
            if data['state']!='document_active_verified' or not data.get('savedFilesUnchanged'):
                raise ValueError('Activation not verified')
            if data['after']['activeWindow']['modelId']!=args['modelId'] or data['after']['activeWindow']['windowNumber']!=args['windowNumber']:
                raise ValueError('Wrong document active after choice')
            def windows(snapshot):return [{k:v for k,v in w.items() if k!='selectedRaw'} for w in snapshot['windows']]
            if windows(data['before'])!=windows(data['after']):
                raise ValueError('Activation changed the native window inventory')
            for p,sha in data['savedFileHashesBefore'].items():
                if file_hash(Path(p))!=sha:raise ValueError('An open saved project changed')
                fingerprints[p]=sha
                all_saved_hashes[p]=sha
            if data.get('alreadyActive'):
                if data['contextChangeAttempted'] is not False:raise ValueError('Already-active case issued a command')
                counts['noop']+=1
            else:
                if data['contextChangeAttempted'] is not True:raise ValueError('Activation attempt unrecorded')
                counts['activated']+=1
        else:raise ValueError('Unexpected public tool in qualification')
    if counts!={'list':6,'inspect':3,'activated':2,'noop':1,'rejectedNative':2,'rejectedConsumed':1,'controls':2}:
        raise ValueError('Incomplete public document qualification: '+str(counts))
    if len(surface_inspections)!=2 or surface_inspections[0]!=surface_inspections[1]:
        raise ValueError('Observed surface properties changed during document round trip')
    def windows(snapshot):return [{k:v for k,v in w.items() if k!='selectedRaw'} for w in snapshot['windows']]
    if windows(initial)!=windows(final) or initial['activeWindow']!=final['activeWindow']:
        raise ValueError('Original window inventory/context not restored')
    if report['readonlyActivationRejection']['error']['code']!=-32602:
        raise ValueError('Read-only server accepted activation')
    readonly={x['name'] for x in report['readonlyCatalog']['tools']}
    if 'list_documents' not in readonly or 'activate_document' in readonly:
        raise ValueError('Read-only catalog incorrect')
    folder=root.parent.parent/'P2_Disk_Verification'/('References_'+report['fixtureId'])
    ledger=read(folder/'owned-document.json')
    if ledger['state']!='closed_qualification_copy' or file_hash(folder/'SurfaceCopy.psmodel')!='56dd1234d09a60ff54105f7d904c1feda0390826ce81fa8847d5f283b62b02ca':
        raise ValueError('Owned copy not closed or changed')
    config=json.loads((root/'bridge.config.json').read_text(encoding='utf-8-sig'))
    for p,sha in ((config['expectedProject'],config['testProjectSha256']),(config['protectedProject'],config['protectedProjectSha256'])):
        if file_hash(Path(p))!=sha:raise ValueError('Protected project changed')
        fingerprints[p]=sha
    return {'state':'succeeded_document_choice_independent_audit','counts':counts,
            'nativeWindowInventoryRestored':True,'observedSurfacePropertiesUnchanged':True,
            'savedProjectsUnchanged':True,'ownedCopyWindowClosed':True,'files':fingerprints,
            'scope':'Already-open saved model documents and fixed planar test copy; no arbitrary open/save/geometry-edit or full UI-state certification.'}


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--bridge-root',type=Path,required=True)
    parser.add_argument('--report',type=Path,required=True)
    args=parser.parse_args()
    result=audit(args.bridge_root,args.report)
    save(args.report.parent/'independent-audit.json',result)
    print(result['state'])
