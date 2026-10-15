"""Independent audit of bounded synthetic workplane native evidence. No CAD calls."""
import argparse,hashlib,json,math
from pathlib import Path
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def audit(stage):
    report_path=stage/'reports/owned-workplane-tests.json';report=read(report_path)
    assert report['state']=='passed' and report['cadConnectionAttempted'] is False
    expected={'control','nonorthogonal','bool','extra','changed_document','claimed_replay','native_zero','native_bool','world_throw_after','wrong_frame','old_entity_changed','verify_changed_document'}
    assert {c['fault'] for c in report['cases']}==expected and len(report['cases'])==12
    hashes=0;verified=0
    for case in report['cases']:
        root=Path(case['root']).resolve();assert root.is_relative_to(stage/'work')
        fault=case['fault'];prepared=case['prepared'];e=case['execution'];v=case['verification']
        if e is None:
            assert fault in {'nonorthogonal','bool','extra','changed_document'}
            assert prepared['state']=='owned_workplane_preparation_rejected' and case['createCalls']==case['worldActivationCalls']==0
            continue
        packet=root/'ownedworkplanes'/(prepared['ownedWorkplaneId']+'.json')
        assert sha(packet)==prepared['ownedWorkplaneSha256'];hashes+=1
        data=read(packet);assert data==prepared['ownedWorkplane'] and data['executionEnabled'] is data['nativeProviderQualified'] is False
        assert data['arguments']['parameters']=={'originWorldRaw':[2,3,4],'zAxis':[0,1,0],'yAxis':[0,0,1],'coordinateFrame':'world','lengthUnits':'document_raw','activeAfter':'world','mode':'create_new'}
        op=case['operationId'];receipt_path=root/'responses'/(op+'.json');assert sha(receipt_path)==case['receiptSha256'];hashes+=1
        assert read(receipt_path)==e
        plan_path=root/'workplaneexecutions'/(op+'.json');assert sha(plan_path)==e['planSha256'];hashes+=1;plan=read(plan_path);assert plan==e['plan']
        claim_path=root/'ownedworkplanes'/(prepared['ownedWorkplaneId']+'.execution-once.json');assert sha(claim_path)==e['claimSha256'];hashes+=1
        claim=read(claim_path);assert claim=={'operationId':op,'planSha256':e['planSha256'],'ownedWorkplaneId':prepared['ownedWorkplaneId'],'copyId':plan['copyId'],'automaticReplayForbidden':True}
        for p,digest in plan['savedFileHashesBefore'].items():assert sha(Path(p))==digest;hashes+=1
        events=[];previous=None
        for index,path in enumerate(sorted((root/'operations'/op).glob('*.json'))):
            event=read(path);assert path.name==f'{index:06d}.json' and event['sequence']==index and event['previousSha256']==previous
            assert event['operationId']==op and event['planSha256']==e['planSha256'];previous=sha(path);events.append(event);hashes+=1
        assert sum(x['state']=='command_started' for x in events)==1
        fence=root/'workingcopies'/plan['copyId']/'mutation-fence.json'
        assert case['createCalls']==1
        assert case['worldActivationCalls']==(0 if fault in {'native_zero','native_bool'} else 1)
        assert e['cadMutationAttempted'] is e['geometryMutationAttempted'] is e['contextChangeAttempted'] is True and e['cadSaveAttempted'] is False
        if v and v['state']=='owned_workplane_verified_observed':
            verified+=1;assert events[-1]['state']=='succeeded_verified' and not fence.exists()
            assert e['state']=='owned_workplane_executed_unverified' and e['createCalls']==e['worldActivationCalls']==1 and e['commandCalls']==2
            returned=next(x['details'] for x in events if x['state']=='command_returned')
            assert type(returned['createdCompositeId']) is int and returned['createdCompositeId']>0 and returned['nativeReturnType']=='System.Int32' and returned['worldActivationReturned'] is True
            creation_path=root/'workplaneexecutions'/(op+'.creation-return.json');assert sha(creation_path)==returned['creationReceiptSha256'];hashes+=1
            for proof in v['evidence']:assert sha(root/'responses'/(proof['requestId']+'.json'))==proof['sha256'];hashes+=1
            info=v['createdWorkplane']['worldInfo'];assert info==[2,3,4,0,1,0,0,0,1]
            assert all(type(x) in (int,float) and math.isfinite(x) for x in info)
            assert v['noCommandReplay'] is True and v['copyFenceReleased'] is True
        else:
            assert fence.is_file() and sha(fence)==e['executionFenceSha256'];hashes+=1
            assert e['state']=='owned_workplane_outcome_unknown' or fault=='verify_changed_document'
            if v:assert v['state']=='owned_workplane_verification_rejected'
    return {'state':'passed','cases':12,'independentlyVerifiedControls':verified,'hashChecks':hashes,'cadConnectionAttempted':False,'reportSha256':sha(report_path)}
if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--stage',required=True);parser.add_argument('--output',required=True);args=parser.parse_args()
    output=Path(args.output).resolve();assert not output.exists();result=audit(Path(args.stage).resolve());output.parent.mkdir(parents=True,exist_ok=True);output.write_text(json.dumps(result,indent=2),encoding='utf-8');print(json.dumps(result))
