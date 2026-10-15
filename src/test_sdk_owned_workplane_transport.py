"""Two SDK transport faults after native simulated effect. Never connect to CAD."""
import argparse,json,threading,time
from pathlib import Path
from test_sdk_stdio import Client
from test_sdk_owned_uniform_scale_wire_stdio import initialize
from test_sdk_checkpoint_wire_stdio import Panel as BasePanel,inventory,native,read,sha

class Panel(BasePanel):
    def __init__(self,root,fault):
        self.executed=threading.Event()
        super().__init__(root,fault)
    def run(self):
        while not self.stop.is_set():
            for path in (self.root/'requests').glob('*.json'):
                processing=path.with_name(path.name+'.processing')
                try:path.rename(processing)
                except (FileNotFoundError,PermissionError):continue
                try:
                    recipe=read(processing)['arguments']['recipeId']
                    result=native(self.root/'PSBridge.Worker.exe','--owned-workplane-wire-request',path.stem)
                    destination=self.root/'responses'/(path.stem+('.unpublished' if recipe=='execute_owned_workplane' else '.json'))
                    assert not destination.exists()
                    temporary=destination.with_suffix('.pending');temporary.write_text(json.dumps(result),encoding='utf-8');temporary.rename(destination)
                    self.calls.append({'recipe':recipe,'requestId':path.stem,'state':result['state']})
                    if recipe=='execute_owned_workplane':self.executed.set()
                except BaseException as error:self.errors.append(repr(error));self.stop.set()
            self.stop.wait(.01)

def case(args,fault,output):
    stage=Path(args.stage).resolve();seed=native(stage/'PSBridge.Worker.exe','--owned-workplane-wire-seed');root=Path(seed['root']).resolve()
    assert root.is_relative_to(stage/'work')
    panel=Panel(root,fault);command=[args.dotnet,str(Path(args.host).resolve()),'--bridge-root',str(root),'--timeout','8']
    client=Client(command);sequence=10;report={'state':'started','fault':fault,'root':str(root),'seed':seed,'cadConnectionAttempted':False}
    def save():
        report.update(transcript=client.transcript,nativeRequests=panel.calls)
        (output/(fault+'.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
    def call(name,values):
        nonlocal sequence
        sequence+=1;result=client.call(sequence,'tools/call',{'name':name,'arguments':values},timeout=20)
        assert not panel.errors,panel.errors;return result
    def ok(name,values):
        result=call(name,values)['result'];assert not result.get('isError',False),result;return result['structuredContent']
    try:
        initialize(client,'owned-workplane-transport-'+fault)
        copy=ok('prepare_working_copy',{'sourcePath':seed['sourcePath'],'sourceSha256':sha(Path(seed['sourcePath']))})
        candidate=ok('inspect_entity_candidates',{})
        bound=ok('bind_working_document',{'candidateDocumentHandle':candidate['candidateDocumentHandle'],'workingCopyHandle':copy['workingCopyHandle']})
        prepared=ok('prepare_owned_workplane',{'workingDocumentBindingHandle':bound['workingDocumentBindingHandle'],
          'parameters':{'originWorldRaw':[2,3,4],'zAxis':[0,1,0],'yAxis':[0,0,1],'coordinateFrame':'world','lengthUnits':'document_raw','activeAfter':'world','mode':'create_new'}})
        arguments={'ownedWorkplaneHandle':prepared['ownedWorkplaneHandle']};report.update(copy=copy,prepared=prepared)
        if fault=='lost_reply':
            executed=call('execute_owned_workplane',arguments)['result'];report['executeResponse']=executed;save()
            assert executed['isError'] and executed['structuredContent']['intentConsumed'] is True
            assert executed['structuredContent']['retryAllowed'] is False
            assert executed['structuredContent']['cadMutationMayHaveOccurred'] is True
        else:
            sequence+=1;rpc_id=sequence
            message={'jsonrpc':'2.0','id':rpc_id,'method':'tools/call','params':{'name':'execute_owned_workplane','arguments':arguments}}
            client.process.stdin.write(json.dumps(message).encode()+b'\n');client.process.stdin.flush()
            assert panel.executed.wait(20) and not panel.errors,panel.errors
            cancel={'jsonrpc':'2.0','method':'notifications/cancelled','params':{'requestId':rpc_id,'reason':'qualification after simulated native effect'}}
            client.process.stdin.write(json.dumps(cancel).encode()+b'\n');client.process.stdin.flush()
            report['cancelNotification']=cancel
        assert panel.executed.is_set() and not panel.errors
        execution=next(c for c in panel.calls if c['recipe']=='execute_owned_workplane');op=execution['requestId'];report['operationId']=op
        unpublished=root/'responses'/(op+'.unpublished');assert unpublished.is_file() and not unpublished.with_suffix('.json').exists()
        before=inventory(root);start=time.monotonic()
        recorded=ok('inspect_operation_recovery',{'operationId':op});elapsed=time.monotonic()-start
        report.update(recordedRecovery=recorded,recoveryReadSeconds=elapsed)
        if fault=='cancel_after_effect':assert elapsed<4,'Protocol cancellation did not release the gate before the 8s IPC deadline'
        assert recorded['state']=='recovery_inspected_recorded' and recorded['assessment']=='returned_requires_independent_verification'
        assert recorded['authoritativeSuccessReturnRecorded'] is True and recorded['executionAuthorized'] is False and recorded['retryAllowed'] is False
        assert recorded['fencePresent'] is True and recorded['outcome']=='not_resolved_by_recorded_inspection'
        assert inventory(root)==before
        replay=call('execute_owned_workplane',arguments)['result'];assert replay['isError'] and inventory(root)==before
        assert replay['structuredContent']['cadMutationMayHaveOccurred'] is False;report['replayRejected']=replay
        adoption=call('adopt_owned_workplane_execution',{'executionRequestId':op,'executionReceiptSha256':sha(unpublished)})['result']
        assert adoption['isError'] and 'workplaneExecutionHandle' not in adoption['structuredContent'] and inventory(root)==before
        report['missingOriginalReceiptAdoptionRejected']=adoption
        context=read(sorted((root/'wire-contexts').glob('*.json'))[-1]);assert context['creates']==context['activations']==1 and context['active']==0
        assert sum(c['recipe']=='execute_owned_workplane' for c in panel.calls)==1
        for path,digest in seed['protectedHashes'].items():assert sha(Path(path))==digest,path
        assert (root/'workingcopies'/copy['copyId']/'mutation-fence.json').is_file()
        report.update(state='passed',artifactHashes=inventory(root),createCalls=1,worldActivationCalls=1,
          originalReplyStillUnpublished=True,unknownNotResolved=True,originalFenceRetained=True)
    except BaseException as error:report.update(state='failed_preserve_original_evidence',error=repr(error));raise
    finally:
        try:client.close()
        finally:panel.close();save()
    return {'fault':fault,'state':report['state'],'createCalls':1,'worldActivationCalls':1,'cadConnectionAttempted':False}

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ('stage','host','dotnet','evidence'):parser.add_argument('--'+name,required=True)
    parser.add_argument('--fault',choices=('lost_reply','cancel_after_effect'))
    args=parser.parse_args();output=Path(args.evidence).resolve();assert not output.exists();output.mkdir(parents=True)
    results=[case(args,fault,output) for fault in ((args.fault,) if args.fault else ('lost_reply','cancel_after_effect'))]
    (output/'summary.json').write_text(json.dumps({'state':'passed','cases':results},indent=2),encoding='utf-8');print(json.dumps({'state':'passed','cases':len(results),'cadConnectionAttempted':False}))
