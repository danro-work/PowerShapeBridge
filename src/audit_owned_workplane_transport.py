"""Independent file-only audit: no republishing a lost reply, no fence release or replay."""
import argparse,json
from pathlib import Path
from audit_owned_workplane_wire_stdio import read,sha

def audit(folders):
    cases=[];hashes=0;faults=set()
    for folder in folders:
        for report_path in sorted(folder.glob('*.json')):
            r=read(report_path)
            if 'fault' not in r:continue
            assert r['state']=='passed' and r['cadConnectionAttempted'] is False
            fault=r['fault'];assert fault in ('lost_reply','cancel_after_effect') and fault not in faults;faults.add(fault)
            root=Path(r['root']).resolve();op=r['operationId']
            for path,digest in r['artifactHashes'].items():
                actual=Path(path).resolve();assert actual.is_relative_to(root);assert sha(actual)==digest;hashes+=1
            for path,digest in r['seed']['protectedHashes'].items():assert sha(Path(path))==digest;hashes+=1
            assert r['createCalls']==r['worldActivationCalls']==1
            assert r['originalReplyStillUnpublished'] is r['unknownNotResolved'] is r['originalFenceRetained'] is True
            receipt=root/'responses'/(op+'.unpublished');assert receipt.is_file() and not receipt.with_suffix('.json').exists()
            raw=read(receipt);plan=raw['plan'];assert raw['operationId']==op and raw['state']=='owned_workplane_executed_unverified'
            assert raw['createCalls']==raw['worldActivationCalls']==1 and raw['commandCalls']==2
            assert sha(root/'workplaneexecutions'/(op+'.json'))==raw['planSha256'];hashes+=1
            previous=None;events=[]
            for i,p in enumerate(sorted((root/'operations'/op).glob('*.json'))):
                ev=read(p);assert p.name==f'{i:06d}.json' and ev['sequence']==i and ev['previousSha256']==previous
                assert ev['operationId']==op and ev['planSha256']==raw['planSha256'];events.append(ev);previous=sha(p);hashes+=1
            assert [v['state'] for v in events]==['prepared','execution_claimed','command_started','command_returned','executed_unverified']
            returned=events[3]['details'];assert type(returned['createdCompositeId']) is int and returned['createdCompositeId']>0
            assert returned['nativeReturnType']=='System.Int32' and returned['worldActivationReturned'] is True
            assert sha(root/'workplaneexecutions'/(op+'.creation-return.json'))==returned['creationReceiptSha256'];hashes+=1
            claim=root/'ownedworkplanes'/(plan['ownedWorkplaneId']+'.execution-once.json');assert sha(claim)==raw['claimSha256'];hashes+=1
            assert read(claim)=={'operationId':op,'planSha256':raw['planSha256'],'ownedWorkplaneId':plan['ownedWorkplaneId'],'copyId':plan['copyId'],'automaticReplayForbidden':True}
            fence=root/'workingcopies'/plan['copyId']/'mutation-fence.json';assert sha(fence)==raw['executionFenceSha256'];hashes+=1
            assert read(fence)=={'schemaVersion':1,'operationId':op,'planSha256':raw['planSha256'],'copyId':plan['copyId'],'ownershipSha256':plan['ownershipSha256'],'automaticReplayForbidden':True}
            assert (root/'requests'/(op+'.json.processing')).is_file()
            assert sum(c['recipe']=='execute_owned_workplane' for c in r['nativeRequests'])==1
            assert not any(c['recipe']=='verify_owned_workplane' for c in r['nativeRequests'])
            recovered=r['recordedRecovery'];assert recovered['authoritativeSuccessReturnRecorded'] is True and recovered['fencePresent'] is True
            assert recovered['outcome']=='not_resolved_by_recorded_inspection' and recovered['retryAllowed'] is recovered['executionAuthorized'] is False
            for key in ('replayRejected','missingOriginalReceiptAdoptionRejected'):assert r[key]['isError'] is True
            assert 'workplaneExecutionHandle' not in r['missingOriginalReceiptAdoptionRejected']['structuredContent']
            if fault=='lost_reply':
                response=r['executeResponse'];data=response['structuredContent'];assert response['isError'] is True
                assert data['bridgeRequestId']==op and data['cadMutationMayHaveOccurred'] is data['intentConsumed'] is True and data['retryAllowed'] is False
            else:
                assert r['cancelNotification']['method']=='notifications/cancelled' and r['recoveryReadSeconds']<4
            contexts=sorted((root/'wire-contexts').glob('*.json'));state=read(contexts[-1]);assert state['creates']==state['activations']==1 and state['active']==0
            cases.append({'fault':fault,'reportSha256':sha(report_path),'originalReplySha256':sha(receipt),'fenceRetained':True,'replayCommands':0})
    assert faults=={'lost_reply','cancel_after_effect'}
    return {'state':'passed','cases':cases,'hashChecks':hashes,'cadConnectionAttempted':False,'scope':'SDK/native simulated effects only; both original replies remain unpublished, unknown unresolved, claims/fences retained'}

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--evidence',required=True,action='append');p.add_argument('--output',required=True);args=p.parse_args()
    output=Path(args.output);assert not output.exists();result=audit([Path(v) for v in args.evidence]);output.write_text(json.dumps(result,indent=2),encoding='utf-8');print(json.dumps(result))
