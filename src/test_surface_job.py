"""Check public MCP routing, preserved errors and no replay after timeout."""
import json
import contextlib
import unittest
import uuid
from pathlib import Path
from surface_job import run
from slice_job import file_hash,save

class FakeClient:
    def __init__(self,root,mode='success'):
        self.root=root;self.mode=mode;self.calls=[];self.closed=False;self.job=uuid.uuid4().hex
    def request(self,method,params):
        assert method=='tools/call'
        name=params['name'];args=params['arguments'];self.calls.append((name,args))
        if name=='prepare_test_surface_job':
            plan={'jobId':self.job,'heightsWorldMm':[n*.8 for n in range(25)]}
            # Use the same exact decimal grid as the real adapter.
            plan['heightsWorldMm']=[n*800000/1000000 for n in range(25)]
            path=self.root/'surfacejobs'/(self.job+'.json');path.parent.mkdir();save(path,plan)
            data={'state':'surface_job_plan_ready','jobId':self.job,'planPath':str(path),'planSha256':file_hash(path),'plan':plan}
        elif name=='activate_test_baseline':data={'state':'baseline_active'}
        else:
            assert name=='execute_test_surface_job_phase'
            if self.mode=='timeout':raise TimeoutError('Simulated unknown outcome')
            token=uuid.uuid4().hex
            data={'state':'succeeded_surface_section_phase','requestId':token,'bridgeRequestId':token,'jobId':self.job}
            phase=args['phase'];index=args['index']
            if self.mode=='failure':data={'state':'timeout_outcome_unknown','bridgeRequestId':token,'error':{'code':'worker_timeout'}}
            else:
                model=self.root/'fixture.psmodel'
                if not model.exists():model.write_bytes(b'fixture')
                data.update(modelPath=str(model),modelSha256=file_hash(model))
                if phase=='slice':data.update(zWorldMm=index*800000/1000000,createdCurveCount=1,selectedWorkingSurfaces=[{}]*4,excludedAuxiliaryEntities=[{}]*2)
                if phase=='verify_surfaces':data['sourceSurfacesUnchangedOnFinalDiskReopen']=True
                if phase=='export':
                    dgk=self.root/'group.dgk';dgk.write_bytes(b'curves');data.update(dgkPath=str(dgk),dgkSha256=file_hash(dgk),curveCount=25)
                if phase=='verify':data['section']={'zWorldMm':index*800000/1000000,'totalLengthMm':80}
            response=self.root/'responses'/(token+'.json');response.parent.mkdir(exist_ok=True);save(response,data)
        return {'structuredContent':data,'content':[],'isError':data['state']=='timeout_outcome_unknown'}
    def close(self):self.closed=True

class SurfaceJobTests(unittest.TestCase):
    def exercise(self,mode):
        folder=Path(__file__).resolve().parents[1]/'work'/('surface-job-test-'+uuid.uuid4().hex)
        folder.mkdir()
        with contextlib.nullcontext(folder) as folder:
            root=Path(folder)/'release';root.mkdir();output=Path(folder)/'out';client=FakeClient(root,mode)
            if mode=='success':
                report=run(root,output,client_factory=lambda _:client)
                self.assertEqual(report['state'],'succeeded_working_surface_slices_grouped_dgk_verified')
                self.assertEqual(len(report['phases']),54)
                self.assertEqual(len(report['verifiedSections']),25)
                self.assertEqual(len(list(output.glob('*.dgk'))),1)
                self.assertFalse((root/'requests').exists())
                self.assertEqual(report['executionChannel'],'public_mcp_stdio')
            else:
                with self.assertRaises((RuntimeError,TimeoutError)):run(root,output,client_factory=lambda _:client)
                report=json.loads((output/'surface-job.json').read_text(encoding='utf-8'))
                self.assertEqual(report['state'],'stopped_requires_inspection')
                self.assertEqual(len(report['phases']),1)
                if mode=='failure':
                    record=report['phases'][0]
                    payload=json.loads(Path(record['mcpResponsePath']).read_text(encoding='utf-8'))
                    self.assertEqual(payload['structuredContent']['error']['code'],'worker_timeout')
                    self.assertEqual(file_hash(record['mcpResponsePath']),record['mcpResponseSha256'])
            self.assertTrue(client.closed)
            self.assertEqual(sum(name=='execute_test_surface_job_phase' for name,_ in client.calls),54 if mode=='success' else 1)
    def test_complete_public_chain(self):self.exercise('success')
    def test_timeout_never_replays(self):self.exercise('timeout')
    def test_failure_response_preserved(self):self.exercise('failure')

if __name__=='__main__':unittest.main()
