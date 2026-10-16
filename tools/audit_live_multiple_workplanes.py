"""File-only check of one creation, preserved frames, A -> B -> WORLD and original proofs."""
import argparse,copy,json
from datetime import datetime
from pathlib import Path
from audit_live_workplane_activation import read,sha,same_observation,audit_pair

def audit(folder,runtime,qualification,once_path):
 report=read(folder/'result.json');q=read(qualification);project=Path(__file__).resolve().parents[1]
 assert report['state']=='owned_multiple_workplanes_sdk_independently_observed' and report['executeRequested'] is True
 assert report['createSubmissions']==1 and report['activationSubmissions']==3 and report['independentReaderSubmitted'] is True
 assert sha(report['approvalNote'])==report['approvalSha256']
 once=read(once_path);assert once['approvalSha256']==report['approvalSha256'] and once['automaticReplayForbidden'] is True and Path(once['evidence']).resolve()==folder.resolve()
 assert report['protectedHashes']==q['protectedHashes']
 for name,digest in q['pinnedHashes'].items():assert sha(project/name)==digest,name
 for name,digest in q['protectedHashes'].items():assert sha(name)==digest,name
 names=[s['tool'] for s in report['steps']]
 assert set(names)<={'inspect_entity_candidates','list_documents','adopt_owned_workplane_activation_execution','verify_owned_workplane_activation','bind_working_document','prepare_owned_workplane','execute_owned_workplane','verify_owned_workplane','prepare_owned_workplane_activation','execute_owned_workplane_activation'}
 assert names.count('execute_owned_workplane')==names.count('verify_owned_workplane')==1
 assert names.count('execute_owned_workplane_activation')==3 and names.count('verify_owned_workplane_activation')==4
 for mode in ('creation','existing','other','world'):
  submission=read(folder/(mode+'-submission.once'));assert submission['approvalSha256']==report['approvalSha256'] and submission['automaticReplayForbidden'] is True
  key='ownedWorkplaneId' if mode=='creation' else 'activationId'
  assert submission['prepared'][key]==report[mode]['execution']['plan'][key]
 creation=report['creation'];op=creation['execution']['operationId'];receipt_path=runtime/'responses'/(op+'.json')
 assert sha(receipt_path)==creation['executionReceiptSha256'];receipt=read(receipt_path)
 assert all(creation['execution'][k]==v for k,v in receipt.items())
 assert receipt['state']=='owned_workplane_executed_unverified' and receipt['createCalls']==receipt['worldActivationCalls']==1 and receipt['worldActivationReturned'] is True
 plan=receipt['plan'];assert plan==read(runtime/'workplaneexecutions'/(op+'.json')) and sha(runtime/'workplaneexecutions'/(op+'.json'))==receipt['planSha256']
 assert plan['operation']=='create_owned_world_workplane' and plan['parameters']==q['createParameters']
 baseline=read(project/'evidence/K8-multi-workplane-final-preflight/independent-native-preflight.json')['after'];same_observation(plan['before'],baseline)
 packet=runtime/'ownedworkplanes'/(plan['ownedWorkplaneId']+'.json');assert sha(packet)==plan['ownedWorkplaneSha256']
 assert read(packet)['arguments']['parameters']==q['createParameters']
 claim=runtime/'ownedworkplanes'/(plan['ownedWorkplaneId']+'.execution-once.json');assert sha(claim)==receipt['claimSha256']
 assert read(claim)==dict(operationId=op,planSha256=receipt['planSha256'],ownedWorkplaneId=plan['ownedWorkplaneId'],copyId=plan['copyId'],automaticReplayForbidden=True)
 events=[];previous=None
 for i,path in enumerate(sorted((runtime/'operations'/op).glob('*.json'))):
  event=read(path);assert path.name==f'{i:06d}.json' and event['sequence']==i and event['previousSha256']==previous and event['operationId']==op and event['planSha256']==receipt['planSha256']
  events.append(event);previous=sha(path)
 assert [e['state'] for e in events]==['prepared','execution_claimed','command_started','command_returned','executed_unverified','succeeded_verified']
 returned=events[3]['details'];assert returned['worldActivationReturned'] is True and returned['nativeReturnType']=='System.Int32'
 native=runtime/'workplaneexecutions'/(op+'.creation-return.json');assert sha(native)==returned['creationReceiptSha256']==receipt['creationReceiptSha256']
 raw=read(native);assert raw['operationId']==op and raw['planSha256']==receipt['planSha256'] and raw['createdCompositeId']==raw['createdActiveRaw']==receipt['createdCompositeId'] and raw['createCalls']==1
 checked=creation['verification'];verify_raw=read(runtime/'responses'/(checked['bridgeRequestId']+'.json'))
 assert all(checked[k]==v for k,v in verify_raw.items()) and checked['state']=='owned_workplane_verified_observed' and checked['copyFenceReleased'] is checked['noCommandReplay'] is True
 assert checked['evidence']==events[-1]['details']['evidence']
 for proof in checked['evidence']:assert sha(runtime/'responses'/(proof['requestId']+'.json'))==proof['sha256']
 assert checked['createdWorkplane']['worldInfo']==q['newFrameWorldRaw'] and checked['createdWorkplane']==report['createdWorkplane']
 after=receipt['after'];same_observation(after,report['baselineAfterCreation']);normalized=copy.deepcopy(after);new_id=checked['createdWorkplane']['nativeId']
 assert len(after['entities'])==3 and sum(e['nativeTypeCode']==54 for e in after['entities'])==2
 normalized['entities']=[e for e in normalized['entities'] if not(e['nativeTypeCode']==54 and e['nativeId']==new_id)]
 normalized['entityCount']=len(normalized['entities']);normalized['workplanes']=[p for p in normalized['workplanes'] if p['nativeId']!=new_id];same_observation(normalized,baseline)
 assert not (runtime/'workingcopies'/plan['copyId']/'mutation-fence.json').exists()
 first_op=report['existing']['execution']['operationId'];first_started=read(runtime/'operations'/first_op/'000002.json')['timestampUtc']
 assert datetime.fromisoformat(first_started.replace('Z','+00:00'))>datetime.fromisoformat(events[-1]['timestampUtc'].replace('Z','+00:00'))
 independent=read(folder/'independent-native-result.json');assert independent['assemblySha256']==sha(runtime/'PSBridge.Worker.exe')
 assert independent['diagnosticSha256']==sha(qualification.parent/'native/ReadOwnedWorkplaneFrames.exe') and independent['expectedFramesSha256']==sha(qualification.parent/'expected-after-frames.json')
 assert sorted(p['worldInfo'] for p in independent['frames'])==sorted(read(qualification.parent/'expected-after-frames.json'))
 result=audit_pair(runtime,report,after,independent,9,evidence_kind='live_recorded_evidence',multiple=True)
 result.update(createCalls=1,creationWorldSetterCalls=1,totalExplicitSetterCalls=4,protectedHashChecks=len(q['protectedHashes']),packagePinChecks=len(q['pinnedHashes']),reportSha256=sha(folder/'result.json'))
 return result

if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__)
 for name in ('evidence','runtime','qualification','once','output'):p.add_argument('--'+name,type=Path,required=True)
 args=p.parse_args();assert not args.output.exists();result=audit(args.evidence.resolve(),args.runtime.resolve(),args.qualification.resolve(),args.once.resolve())
 with args.output.open('x',encoding='utf-8') as out:json.dump(result,out,indent=2)
 print(json.dumps(result))
