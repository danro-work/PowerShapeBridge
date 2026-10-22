"""Independent fixed-schema/file/journal audit of passive scale preparation; no CAD."""
import argparse
import math
from pathlib import Path
from audit_checkpoint_recovered_open import journal, read, require, sha
import json

CONTROLS={'control','existing_writer','mixed_targets','shrink','caller_alias','held_proofs','saved_bytes_changed_before_prepare'}
FAULTS=CONTROLS|{'factor_bool','factor_string','factor_zero','factor_negative','factor_one',
    'missing_centre','implicit_frame','keep_original','axis_lock','set_sha','binding_sha',
    'set_expired','absent_target','source_drift','mutation_fence','mutation_fence_directory','export_fence','late_fence',
    'changed_document','changed_units','changed_workplane','nonempty_selection','late_context',
    'checkpoint_writable','working_write_during_guard','busy_executor'}
FIELDS={'entitySetId','entitySetSha256','bindingId','bindingSha256','parameters','schemaVersion',
    'ownedScaleId','operation','preparedUtc','expiresUtc','executionEnabled','copyId',
    'ownershipSha256','workingSha256','entityKeys','setInspectionRequestId','setInspectionSha256',
    'bindingInspectionRequestId','bindingInspectionSha256','nativeObservedPropertiesSha256',
    'windowContextSha256','lengthUnitsRaw','lengthUnitFactorRaw','nativeProviderQualified',
    'requiredBeforeExecution'}
REQUIREMENTS=['qualified_uniform_scale_recipe','qualified_native_measurements','fresh_owned_context_guard',
    'exact_target_set','primitive_remapping_proof','once_claim_and_mutation_fence',
    'independent_postconditions','saved_result_reopen','recovery_fault_matrix']

def audit(stage):
    require((stage/'.owned-uniform-scale-fixture').is_file(),'Missing own fixture marker')
    report=read(stage/'reports/owned-uniform-scale-plan-tests.json')
    require(report['state']=='passed' and report['cadConnectionAttempted'] is False and
        len(report['rows'])==len(FAULTS) and {r['fault'] for r in report['rows']}==FAULTS,'Incomplete case matrix')
    require(report['configurationRefusals']==4,'Missing strict native configuration controls')
    count=0
    for row in report['rows']:
        root=Path(row['root']).resolve()
        require(root.is_relative_to(stage/'work'),'Fixture outside stage')
        hashes={Path(p).resolve():h for p,h in row['artifactHashes'].items()}
        require(set(hashes)=={p.resolve() for p in root.rglob('*') if p.is_file()} and
            all(p.is_relative_to(root) and sha(p)==h for p,h in hashes.items()),'Artifact inventory/SHA changed')
        require(all(Path(p).resolve().is_relative_to(root) and sha(Path(p))==h
            for p,h in row['protectedHashes'].items()),'Original bytes changed')
        count+=len(hashes)
        result=row['result'];control=row['fault'] in CONTROLS
        require(result['state']==('owned_uniform_scale_prepared_observed' if control else
            'owned_uniform_scale_preparation_rejected'),'Wrong outcome')
        require(all(result[k] is False for k in ('executionEnabled','geometryMutationAttempted',
            'contextChangeAttempted','cadSaveAttempted','nativeProviderQualified')),'Unexpected authority')
        if 'ownedScaleSha256' in result:
            path=root/'ownedscales'/(row['requestId']+'.json');packet=read(path)
            require(sha(path)==result['ownedScaleSha256'] and set(packet)==FIELDS,'Packet SHA/schema differs')
            require(packet['ownedScaleId']==row['requestId'] and packet['schemaVersion']==1 and
                packet['operation']=='uniform_scale_entities' and packet['executionEnabled'] is False and
                packet['nativeProviderQualified'] is False and packet['requiredBeforeExecution']==REQUIREMENTS,'Packet policy differs')
            require(journal(root,row['requestId'])==['prepared'],'Prepared journal advanced')
            event=read(root/'operations'/row['requestId']/'000000.json')
            require(event['planSha256']==result['ownedScaleSha256'],'Journal plan SHA differs')
            parameters=packet['parameters']
            require(set(parameters)=={'factor','centre','coordinateFrame','lengthUnits','mode'} and
                type(parameters['factor']) in (float,int) and math.isfinite(parameters['factor']) and
                parameters['factor']>0 and parameters['factor']!=1 and len(parameters['centre'])==3 and
                all(type(v) in (int,float) and math.isfinite(v) for v in parameters['centre']) and
                parameters['coordinateFrame']=='world' and parameters['lengthUnits']=='document_raw' and
                parameters['mode']=='scale_in_place','Scale parameters differ')
            require(parameters==row['submitted']['parameters'],'Caller alias changed frozen parameters')
            for field,folder,id_field,sha_field in (
                ('set','entitysets','entitySetId','entitySetSha256'),
                ('binding','documentbindings','bindingId','bindingSha256')):
                dependency_path=root/folder/(packet[id_field]+'.json')
                require(sha(dependency_path)==packet[sha_field],'Dependency SHA differs')
                dep=read(dependency_path)
                source_path=root/'responses'/(packet[field+'InspectionRequestId']+'.json')
                require(dep['inspectionRequestId']==packet[field+'InspectionRequestId'] and
                    dep['inspectionSha256']==packet[field+'InspectionSha256'] and
                    sha(source_path)==packet[field+'InspectionSha256'],'Source receipt differs')
                if field=='set':
                    require(dep['entityKeys']==packet['entityKeys'],'Target set differs')
                else:
                    require(dep['copyId']==packet['copyId'] and dep['ownershipSha256']==packet['ownershipSha256'] and
                        dep['observedPropertiesSha256']==packet['nativeObservedPropertiesSha256'] and
                        dep['windowContextSha256']==packet['windowContextSha256'],'Binding context differs')
                    source=read(source_path)
                    require(source['lengthUnitsRaw']==packet['lengthUnitsRaw'] and
                        source['lengthUnitFactorRaw']==packet['lengthUnitFactorRaw'],'Source raw units changed')
            owner_path=root/'workingcopies'/packet['copyId']/'ownership.json';owner=read(owner_path)
            require(sha(owner_path)==packet['ownershipSha256'],'Ownership SHA differs')
            for field,expected in (('working',packet['workingSha256']),('checkpoint',owner['checkpoint']['sha256'])):
                owned_path=Path(owner[field]['path']).resolve()
                require(owned_path.is_relative_to(root) and sha(owned_path)==expected,'Owned bytes differ')
        if control:
            require(result['sameContextObservationReads']==4 and result['guardPasses']==2 and row['observationReads']==4,'Missing held reads')
            require(packet==result['ownedScale'],'Receipt differs from raw packet')
            if row['fault']=='mixed_targets':require({k[0] for k in packet['entityKeys']}=={40,21,59},'Mixed keys lost')
            if row['fault']=='held_proofs':require(row['blockedWrites']==3,'Proof write checks missing')
        if row['fault']=='late_fence':
            require((root/'workingcopies'/result['copyId']/'mutation-fence.json').is_file() if 'copyId' in result else
                any(root.glob('workingcopies/*/mutation-fence.json')),'Late fence lost')
            require('ownedScaleSha256' in result,'Late refusal did not exercise post-publication guard')
    return {'state':'passed','cases':len(FAULTS),'controls':len(CONTROLS),'refusals':len(FAULTS)-len(CONTROLS),
        'artifactHashesChecked':count,'cadConnectionAttempted':False,'nativeScaleExecuted':False}

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--stage',type=Path,required=True);parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();result=audit(args.stage.resolve())
    args.output.parent.mkdir(parents=True,exist_ok=True)
    with args.output.open('x',encoding='utf-8') as stream:stream.write(json.dumps(result,indent=2)+'\n')
    print(f"PASS: {result['cases']} scale preparations/{result['artifactHashesChecked']} independently audited hashes; no CAD.")
