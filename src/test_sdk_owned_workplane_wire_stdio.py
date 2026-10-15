"""C# STDIO -> real native file/guard path with simulated CAD only; one creation control."""
import argparse,json
from pathlib import Path
from test_sdk_stdio import Client
from test_sdk_owned_uniform_scale_wire_stdio import initialize
from test_sdk_checkpoint_wire_stdio import Panel as BasePanel,inventory,native,read,sha

class Panel(BasePanel):
    def run(self):
        while not self.stop.is_set():
            for path in (self.root/'requests').glob('*.json'):
                processing=path.with_name(path.name+'.processing')
                try:path.rename(processing)
                except (FileNotFoundError,PermissionError):continue
                try:
                    request=read(processing);recipe=request['arguments']['recipeId']
                    result=native(self.root/'PSBridge.Worker.exe','--owned-workplane-wire-request',path.stem)
                    self.calls.append({'recipe':recipe,'requestId':path.stem,'state':result['state']})
                    target=self.root/'responses'/(path.stem+'.json');temporary=target.with_suffix('.pending')
                    assert not target.exists();temporary.write_text(json.dumps(result),encoding='utf-8');temporary.rename(target)
                except BaseException as error:self.errors.append(repr(error));self.stop.set()
            self.stop.wait(.01)

def run(args):
    stage=Path(args.stage).resolve();seed=native(stage/'PSBridge.Worker.exe','--owned-workplane-wire-seed');root=Path(seed['root']).resolve()
    assert root.is_relative_to(stage/'work');panel=Panel(root,'control')
    command=[args.dotnet,str(Path(args.host).resolve()),'--bridge-root',str(root),'--timeout','20']
    client=Client(command);seq=10;report={'state':'started','root':str(root),'seed':seed,'cadConnectionAttempted':False}
    output=Path(args.evidence).resolve();assert not output.exists();output.mkdir(parents=True)
    def call(name,arguments):
        nonlocal seq
        seq+=1;reply=client.call(seq,'tools/call',{'name':name,'arguments':arguments},timeout=40)
        report.setdefault('calls',[]).append({'tool':name,'reply':reply})
        assert not panel.errors,panel.errors;assert 'result' in reply,reply;return reply['result']
    def ok(name,arguments):
        result=call(name,arguments);assert not result.get('isError',False),result;return result['structuredContent']
    try:
        tools=initialize(client,'owned-workplane-native-wire-control')
        assert {'prepare_owned_workplane','execute_owned_workplane','verify_owned_workplane','adopt_owned_workplane_execution'}<={t['name'] for t in tools}
        copy=ok('prepare_working_copy',{'sourcePath':seed['sourcePath'],'sourceSha256':sha(Path(seed['sourcePath']))})
        candidate=ok('inspect_entity_candidates',{})
        bound=ok('bind_working_document',{'candidateDocumentHandle':candidate['candidateDocumentHandle'],'workingCopyHandle':copy['workingCopyHandle']})
        prepared=ok('prepare_owned_workplane',{'workingDocumentBindingHandle':bound['workingDocumentBindingHandle'],
            'parameters':{'originWorldRaw':[2,3,4],'zAxis':[0,1,0],'yAxis':[0,0,1],'coordinateFrame':'world','lengthUnits':'document_raw','activeAfter':'world','mode':'create_new'}})
        execution=ok('execute_owned_workplane',{'ownedWorkplaneHandle':prepared['ownedWorkplaneHandle']});report.update(copy=copy,prepared=prepared,execution=execution)
        assert execution['createCalls']==execution['worldActivationCalls']==1 and execution['commandCalls']==2
        candidate=ok('inspect_entity_candidates',{});listing=ok('list_documents',{})
        active=next(d for d in listing['documents'] if d['isActive'])
        verified=ok('verify_owned_workplane',{'workplaneExecutionHandle':execution['workplaneExecutionHandle'],'candidateDocumentHandle':candidate['candidateDocumentHandle'],'activeDocumentHandle':active['documentHandle']})
        report['verification']=verified;assert verified['state']=='owned_workplane_verified_observed'
        assert verified['workingCopyLifetimeSeconds']>0 and len(verified['workingCopyHandle'])==32
        report['boundCurrentVerifiedCopy']=ok('bind_working_document',{'candidateDocumentHandle':candidate['candidateDocumentHandle'],'workingCopyHandle':verified['workingCopyHandle']})
        assert report['boundCurrentVerifiedCopy']['binding']['copyId']==copy['copyId']
        no_writes=inventory(root);recovery=ok('inspect_operation_recovery',{'operationId':execution['operationId']})
        assert recovery['state']=='recovery_inspected_recorded' and recovery['assessment']=='historically_verified_requires_fresh_context'
        assert recovery['authoritativeSuccessReturnRecorded'] is True and recovery['executionAuthorized'] is False and inventory(root)==no_writes
        report['recordedRecoveryInspection']=recovery
        old_transcript=client.transcript;client.close();client=Client(command);initialize(client,'owned-workplane-reconnect-verification')
        baseline=inventory(root);request=execution['bridgeRequestId'];receipt_sha=sha(root/'responses'/(request+'.json'))
        adopted=ok('adopt_owned_workplane_execution',{'executionRequestId':request,'executionReceiptSha256':receipt_sha})
        assert adopted['verificationOnly'] is True and adopted['executionAuthorized'] is adopted['retryAllowed'] is False
        assert inventory(root)==baseline,'Adoption touched native files/queue'
        rejected=call('execute_owned_workplane',{'ownedWorkplaneHandle':adopted['workplaneExecutionHandle']})
        assert rejected['isError'] and rejected['structuredContent']['cadMutationMayHaveOccurred'] is False and inventory(root)==baseline
        candidate=ok('inspect_entity_candidates',{});listing=ok('list_documents',{});active=next(d for d in listing['documents'] if d['isActive'])
        again=ok('verify_owned_workplane',{'workplaneExecutionHandle':adopted['workplaneExecutionHandle'],'candidateDocumentHandle':candidate['candidateDocumentHandle'],'activeDocumentHandle':active['documentHandle']})
        report.update(adoption=adopted,rejectedVerificationHandleExecution=rejected,reconnectedVerification=again,previousTranscript=old_transcript)
        assert again['state']=='owned_workplane_verified_observed'
        assert again['workingCopyHandle']!=verified['workingCopyHandle']
        report['boundReconnectedVerifiedCopy']=ok('bind_working_document',{'candidateDocumentHandle':candidate['candidateDocumentHandle'],'workingCopyHandle':again['workingCopyHandle']})
        assert report['boundReconnectedVerifiedCopy']['binding']['copyId']==copy['copyId']
        for path,digest in seed['protectedHashes'].items():assert sha(Path(path))==digest,path
        contexts=sorted((root/'wire-contexts').glob('*.json'));last=read(contexts[-1])
        assert last['creates']==last['activations']==1 and last['active']==0
        assert sum(c['recipe']=='execute_owned_workplane' for c in panel.calls)==1
        report.update(state='passed',nativeRequests=panel.calls,artifactHashes=inventory(root),createCalls=1,worldActivationCalls=1)
        print(json.dumps({'state':'passed','nativeRequests':len(panel.calls),'createCalls':1,'worldActivationCalls':1,'reconnectedWithoutReplay':True,'cadConnectionAttempted':False}),flush=True)
    except BaseException as error:report['state']='failed_preserve_synthetic_evidence';report['error']=repr(error);raise
    finally:
        try:client.close()
        finally:
            panel.close();report['transcript']=client.transcript;report['nativeRequests']=panel.calls
            (output/'result.json').write_text(json.dumps(report,indent=2),encoding='utf-8')

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ('stage','host','dotnet','evidence'):parser.add_argument('--'+name,required=True)
    run(parser.parse_args())
