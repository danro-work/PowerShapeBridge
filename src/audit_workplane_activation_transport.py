"""File-only audit of lost/cancelled replies; preserve the original active context and fence."""
import argparse
import json
from pathlib import Path
from audit_workplane_activation_stdio import read, sha

def audit(folder):
    cases=[]
    hashes=0
    for fault in ('lost_reply','cancel_after_effect'):
        path=folder/(fault+'.json')
        r=read(path)
        assert r['state']=='passed' and r['fault']==fault and r['cadConnectionAttempted'] is False
        assert r['setterCalls']==1
        assert r['originalReplyStillUnpublished'] is r['unknownNotResolved'] is r['originalFenceRetained'] is True
        root=Path(r['root']).resolve()
        op=r['operationId']
        for name,digest in r['artifactHashes'].items():
            actual=Path(name).resolve()
            assert actual.is_relative_to(root) and sha(actual)==digest
            hashes+=1
        for name,digest in r['seed']['protectedHashes'].items():
            assert sha(name)==digest
            hashes+=1
        response=root/'responses'/(op+'.unpublished')
        assert response.is_file() and not response.with_suffix('.json').exists()
        receipt=read(response)
        assert receipt['state']=='owned_workplane_activation_executed_unverified' and receipt['setterCalls']==1
        assert receipt['geometryMutationAttempted'] is receipt['cadSaveAttempted'] is False
        plan=receipt['plan']
        assert sha(root/'workplaneactivationexecutions'/(op+'.json'))==receipt['planSha256']
        multiple=r.get('multiple',False)
        assert plan['targetCompositeId']==(1080000012 if multiple else 1080000011)
        assert plan['schemaVersion']==(2 if multiple else 1)
        assert plan['captureBefore']['sourceActive']==(1080000011 if multiple else 0)
        if multiple:assert len(plan['captureBefore']['planes'])==3
        events=[]
        previous=None
        for i,name in enumerate(sorted((root/'operations'/op).glob('*.json'))):
            event=read(name)
            assert name.name==f'{i:06d}.json' and event['sequence']==i and event['previousSha256']==previous
            assert event['operationId']==op and event['planSha256']==receipt['planSha256']
            events.append(event)
            previous=sha(name)
        assert [e['state'] for e in events]==['prepared','execution_claimed','command_started','command_returned','executed_unverified']
        assert sha(root/'workplaneactivationexecutions'/(op+'.setter-return.json'))==events[3]['details']['setterReceiptSha256']
        claim=root/'workplaneactivations'/(plan['activationId']+'.execution-once.json')
        assert sha(claim)==receipt['claimSha256'] and read(claim)['automaticReplayForbidden'] is True
        fence=root/'workingcopies'/plan['copyId']/'mutation-fence.json'
        assert sha(fence)==receipt['executionFenceSha256'] and read(fence)['operationId']==op
        assert (root/'requests'/(op+'.json.processing')).is_file()
        assert sum(c['recipe']=='execute_owned_workplane_activation' for c in r['nativeRequests'])==1
        assert not any(c['recipe']=='verify_owned_workplane_activation' for c in r['nativeRequests'])
        recovery=r['recordedRecovery']
        assert recovery['state']=='recovery_inspected_recorded' and recovery['authoritativeSuccessReturnRecorded'] is True
        assert recovery['fencePresent'] is True and recovery['executionAuthorized'] is recovery['retryAllowed'] is False
        for key in ('replayRejected','missingOriginalReceiptAdoptionRejected'):
            assert r[key]['isError'] is True
        if fault=='lost_reply':
            result=r['executeResponse']
            assert result['isError'] is True and result['structuredContent']['intentConsumed'] is result['structuredContent']['cadMutationMayHaveOccurred'] is True
        else:
            assert r['cancelNotification']['method']=='notifications/cancelled' and r['recoveryReadSeconds']<4
        context=read(sorted((root/'wire-contexts').glob('*.json'))[-1])
        assert context['setterCalls']==1 and context['active']==(1080000012 if multiple else 1080000011)
        cases.append({'fault':fault,'reportSha256':sha(path),'fenceRetained':True,'automaticWorldCalls':0,'replays':0})
    return {'state':'passed','cases':cases,'hashChecks':hashes,'cadConnectionAttempted':False}

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--evidence',required=True)
    p.add_argument('--output',required=True)
    args=p.parse_args()
    output=Path(args.output)
    assert not output.exists()
    result=audit(Path(args.evidence))
    output.write_text(json.dumps(result,indent=2),encoding='utf-8')
    print(json.dumps({'state':result['state'],'cases':len(result['cases']),'hashChecks':result['hashChecks']}))
