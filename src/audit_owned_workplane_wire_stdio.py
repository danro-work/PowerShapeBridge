"""Independent hashes, native journals and WORLD-frame audit; no SDK/worker/CAD calls."""
import argparse,hashlib,json,math
from pathlib import Path
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def audit(folder):
    report_path=folder/'result.json';r=read(report_path);assert r['state']=='passed' and r['cadConnectionAttempted'] is False
    root=Path(r['root']);e=r['execution'];op=e['operationId'];checks=0
    assert r['createCalls']==r['worldActivationCalls']==1
    assert e['createCalls']==e['worldActivationCalls']==1 and e['commandCalls']==2 and e['sameWorkerPostconditionsPassed'] is True
    assert sum(v['recipe']=='execute_owned_workplane' for v in r['nativeRequests'])==1
    for rel,digest in r['artifactHashes'].items():
        path=root/rel;assert path.resolve().is_relative_to(root.resolve());assert sha(path)==digest;checks+=1
    for path,digest in r['seed']['protectedHashes'].items():assert sha(Path(path))==digest;checks+=1
    raw=read(root/'responses'/(op+'.json'));assert all(e[k]==v for k,v in raw.items())
    plan=raw['plan'];assert sha(root/'workplaneexecutions'/(op+'.json'))==raw['planSha256'];checks+=1
    claim=read(root/'ownedworkplanes'/(plan['ownedWorkplaneId']+'.execution-once.json'))
    assert claim=={'operationId':op,'planSha256':raw['planSha256'],'ownedWorkplaneId':plan['ownedWorkplaneId'],'copyId':plan['copyId'],'automaticReplayForbidden':True}
    previous=None;events=[]
    for index,p in enumerate(sorted((root/'operations'/op).glob('*.json'))):
        event=read(p);assert event['sequence']==index and p.name==f'{index:06d}.json' and event['previousSha256']==previous and event['planSha256']==raw['planSha256']
        previous=sha(p);events.append(event);checks+=1
    assert [e['state'] for e in events]==['prepared','execution_claimed','command_started','command_returned','executed_unverified','succeeded_verified']
    returned=events[3]['details'];assert type(returned['createdCompositeId']) is int and returned['createdCompositeId']>0 and returned['nativeReturnType']=='System.Int32' and returned['worldActivationReturned'] is True
    assert sha(root/'workplaneexecutions'/(op+'.creation-return.json'))==returned['creationReceiptSha256'];checks+=1
    for result in (r['verification'],r['reconnectedVerification']):
        assert result['state']=='owned_workplane_verified_observed' and result['noCommandReplay'] is True and result['copyFenceReleased'] is True
        for proof in result['evidence']:assert sha(root/'responses'/(proof['requestId']+'.json'))==proof['sha256'];checks+=1
        info=result['createdWorkplane']['worldInfo'];assert info==[2,3,4,0,1,0,0,0,1]
        assert all(type(x) in (int,float) and math.isfinite(x) for x in info)
        z,y=info[3:6],info[6:9];assert abs(sum(v*v for v in z)-1)<1e-9 and abs(sum(v*v for v in y)-1)<1e-9 and abs(sum(a*b for a,b in zip(z,y)))<1e-9
    assert r['adoption']['verificationOnly'] is True and r['adoption']['executionAuthorized'] is False
    assert r['rejectedVerificationHandleExecution']['isError'] is True
    if 'boundCurrentVerifiedCopy' in r:
        first=r['verification'];second=r['reconnectedVerification']
        assert first['workingCopyHandle']!=second['workingCopyHandle'] and 0<first['workingCopyLifetimeSeconds']<=300 and 0<second['workingCopyLifetimeSeconds']<=300
        for key in ('boundCurrentVerifiedCopy','boundReconnectedVerifiedCopy'):
            bound=r[key];binding=bound['binding']
            assert bound['state']=='working_document_bound_observed' and binding['copyId']==plan['copyId'] and binding['ownershipSha256']==plan['ownershipSha256']
            assert sha(root/'documentbindings'/(binding['bindingId']+'.json'))==bound['bindingSha256'];checks+=1
            observed=read(root/'responses'/(binding['inspectionRequestId']+'.json'))
            assert len(observed['entities'])==len(plan['before']['entities'])+1 and sum(e['nativeTypeCode']==54 for e in observed['entities'])==1
            assert observed['contextBefore']==observed['contextAfter']==[0,0]
    assert not (root/'workingcopies'/plan['copyId']/'mutation-fence.json').exists()
    contexts=sorted((root/'wire-contexts').glob('*.json'));last=read(contexts[-1]);assert last['creates']==last['activations']==1 and last['active']==0
    old=plan['before']['entities'];new=last['snapshot']['entities'];oldkeys={(v['nativeTypeCode'],v['nativeId']) for v in old}
    kept=[v for v in new if (v['nativeTypeCode'],v['nativeId']) in oldkeys];added=[v for v in new if (v['nativeTypeCode'],v['nativeId']) not in oldkeys]
    assert kept==old and len(added)==1 and added[0]['nativeTypeCode']==54
    return {'state':'passed','nativeRequests':len(r['nativeRequests']),'hashChecks':checks,'createCalls':1,'worldActivationCalls':1,'reconnectedVerificationWithoutReplay':True,'cadConnectionAttempted':False,'reportSha256':sha(report_path)}
if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--evidence',required=True);parser.add_argument('--output',required=True);args=parser.parse_args()
    destination=Path(args.output);assert not destination.exists();result=audit(Path(args.evidence));destination.write_text(json.dumps(result,indent=2),encoding='utf-8');print(json.dumps(result))
