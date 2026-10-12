"""Deterministic cube job; final DGK output groups all heights by source level."""
import argparse
from decimal import Decimal
import hashlib
import json
import os
from pathlib import Path
import queue
import re
import subprocess
import sys
import threading
import uuid

def microns(value):
    value = Decimal(str(value))
    scaled = value*1000000
    if not value.is_finite() or scaled!=scaled.to_integral_value():
        raise ValueError('Finite mm with at most six decimal places required')
    return int(scaled)

def technological_grid(zmax_mm, step_mm):
    zmax, step = microns(zmax_mm), microns(step_mm)
    if zmax<0 or step<=0:
        raise ValueError('Nonnegative Zmax and positive h required')
    if zmax<step:
        return []
    last_index=(zmax-step)//step
    if last_index>=4096:
        raise ValueError('Grid exceeds the planning limit of 4096 layers')
    return [n*step/1000000 for n in range(last_index+1)]

class McpClient:
    def __init__(self, root, read_only=False):
        adapter=Path(root)/'mcp_server.py'
        if not adapter.is_file():adapter=Path(__file__).with_name('mcp_server.py')
        command = [sys.executable,str(adapter),'--bridge-root',str(root),'--timeout','90']
        if read_only: command.append('--read-only')
        self.process = subprocess.Popen(command,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
        self.messages=queue.Queue()
        def read():
            try:
                for line in self.process.stdout:
                    self.messages.put(json.loads(line))
            except Exception as error:
                self.messages.put(error)
        self.reader=threading.Thread(target=read,daemon=True)
        self.reader.start()
        self.identity=0
        self.initialize_result=self.request('initialize',{'protocolVersion':'2025-11-25','capabilities':{},'clientInfo':{'name':'psbridge-qualification-client','version':'0.19.0'}})
        self.send('notifications/initialized')
    def send(self, method, params=None, identity=None):
        message={'jsonrpc':'2.0','method':method}
        if params is not None: message['params']=params
        if identity is not None: message['id']=identity
        self.process.stdin.write(json.dumps(message,allow_nan=False).encode()+b'\n')
        self.process.stdin.flush()
    def request(self, method, params):
        message = self.request_raw(method, params)
        if 'error' in message:
            raise RuntimeError('MCP response identity/error: '+str(message['error']))
        return message['result']
    def request_raw(self, method, params):
        self.identity+=1
        self.send(method,params,self.identity)
        message=self.messages.get(timeout=100)
        if isinstance(message,Exception): raise message
        if message.get('id')!=self.identity:
            raise RuntimeError('MCP response identity/error: '+str(message.get('error')))
        return message
    def call(self, name, arguments):
        result=self.request('tools/call',{'name':name,'arguments':arguments})
        data=result.get('structuredContent',{})
        if result.get('isError'):
            raise RuntimeError('MCP '+name+': '+str(data.get('state'))+'; '+str(data.get('error',result.get('content'))))
        return data
    def close(self):
        self.process.stdin.close()
        try: self.process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            self.process.terminate()
            self.process.wait(timeout=5)

def save(path, data):
    temporary=path.with_suffix('.tmp')
    with temporary.open('w',encoding='utf-8') as stream:
        json.dump(data,stream,ensure_ascii=False,allow_nan=False,indent=2)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary,path)

def file_hash(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream,'sha256').hexdigest()

def verify_receipt(root, operation, receipt):
    if receipt.get('operationId')!=operation or receipt.get('state')!='succeeded_slices_saved_reopened_dgk_verified' or receipt.get('phase')!='completed':
        raise ValueError('Recorded batch is not complete')
    folder=(root.parent.parent/'P2_Disk_Verification'/('Slices_'+operation)).resolve()
    files=[(receipt['checkpointPath'],receipt['checkpointSha256']),(receipt['resultPath'],receipt['resultSha256'])]
    for layer in receipt['layers']:
        files.extend(((layer['dgkPath'],layer['dgkSha256']),(layer['importedProjectPath'],layer['importedProjectSha256'])))
    for path, expected in files:
        path=Path(path).resolve()
        if path.parent!=folder or file_hash(path)!=expected:
            raise ValueError('Batch evidence path/hash differs')

def reconcile(root, job_directory, job, identity):
    if not re.fullmatch('[0-9a-f]{32}',identity) or job.get('state')!='stopped_requires_inspection':
        raise ValueError('Explicit successful verification required for stopped job')
    record_path=root/'reports'/('slice-verification-'+identity+'.json')
    verification=json.loads(record_path.read_text(encoding='utf-8-sig'))
    batch=job['batches'][-1]
    source=batch['operationId']
    if verification.get('state')!='succeeded_existing_slice_batch_verified' or verification.get('phase')!='completed' or verification.get('sourceOperationId')!=source:
        raise ValueError('Recovery verification identity/state differs')
    source_path=root/'operations'/(source+'.json')
    if file_hash(source_path)!=verification['sourceReceiptSha256']:
        raise ValueError('Original uncertain journal changed')
    original=json.loads(source_path.read_text(encoding='utf-8-sig'))
    source_folder=(root.parent.parent/'P2_Disk_Verification'/('Slices_'+source)).resolve()
    recovery_folder=(root.parent.parent/'P2_Disk_Verification'/('SliceVerification_'+identity)).resolve()
    for filename,key in (('Result.psmodel','sourceResultSha256'),('Checkpoint.psmodel','sourceCheckpointSha256')):
        if file_hash(source_folder/filename)!=verification[key]: raise ValueError('Recovered source changed')
    offset=len(job['layers'])
    layers=verification['layers']
    planned=job['heightsWorldMm'][offset:offset+len(layers)]
    if planned!=batch['heightsWorldMm'] or [x['zWorldMm'] for x in layers]!=planned:
        raise ValueError('Recovery global layer mapping differs')
    for index,layer in enumerate(layers,1):
        dgk=source_folder/f'Layer_{index:04d}.dgk'
        imported=recovery_folder/f'VerifiedLayer_{index:04d}.psmodel'
        if Path(layer['dgkPath']).resolve()!=dgk or Path(layer['verificationImportedProjectPath']).resolve()!=imported or file_hash(dgk)!=layer['dgkSha256'] or file_hash(imported)!=layer['verificationImportedProjectSha256'] or not layer['verificationWindowClosed']:
            raise ValueError('Recovery evidence changed')
    for completed in job['batches'][:-1]:
        receipt_path=root/'operations'/(completed['operationId']+'.json')
        if file_hash(receipt_path)!=completed['receiptSha256']: raise ValueError('Earlier receipt changed')
        verify_receipt(root,completed['operationId'],json.loads(receipt_path.read_text(encoding='utf-8-sig')))
    backup=job_directory/('job.stopped-before-reconciliation-'+identity+'.json')
    with backup.open('xb') as stream: stream.write((job_directory/'job.json').read_bytes())
    batch.update(state='succeeded_verified_by_independent_recovery',verificationId=identity,verificationReceiptSha256=file_hash(record_path),originalUncertainReceiptSha256=verification['sourceReceiptSha256'],resultPath=original['resultPath'])
    for index,layer in enumerate(layers):
        job['layers'].append({'globalLayerIndex':offset+index+1,'zWorldMm':layer['zWorldMm'],'sourceOperationId':source,'batchLayerIndex':layer['layerIndex'],'dgkPath':layer['dgkPath'],'dgkSha256':layer['dgkSha256'],'importedProjectPath':layer['verificationImportedProjectPath'],'importedProjectSha256':layer['verificationImportedProjectSha256'],'recoveryVerificationId':identity})
    job.setdefault('reconciliations',[]).append({'verificationId':identity,'verificationReceiptSha256':file_hash(record_path),'sourceOperationId':source,'sourceReceiptSha256':verification['sourceReceiptSha256']})
    job['reconciliation']='Existing assets certified by independent verification; original slicing operation and uncertain document not retried.'
    job.pop('error',None)
    return job

def run(root, step_mm, job_directory, execute=False, resume_verification=None):
    step=microns(step_mm)
    if step<100000 or step>20000000:
        raise ValueError('Qualified cube job step is 0.1..20 mm')
    heights=technological_grid(20,step_mm)
    if heights and heights[-1]>19.9: raise ValueError('Qualified last height exceeds 19.9 mm')
    job_path=job_directory/'job.json'
    if resume_verification:
        if not execute: raise ValueError('Resume requires explicit execution mode')
        job=json.loads(job_path.read_text(encoding='utf-8-sig'))
        if job.get('stepMm')!=step/1000000 or job.get('zMaxWorldMm')!=20 or job.get('heightsWorldMm')!=heights:
            raise ValueError('Stopped job grid differs')
        job=reconcile(root,job_directory,job,resume_verification)
    else:
        job_directory.mkdir(parents=True,exist_ok=False)
        job={'schemaVersion':1,'jobId':uuid.uuid4().hex,'scope':'Qualified unscaled cube only; not a production technological model','zMaxWorldMm':20,'stepMm':step/1000000,'heightsWorldMm':heights,'gridRule':'z_i=i*h; last=h*floor((Zmax-h)/h); no off-grid extra section','state':'planned','batches':[],'layers':[]}
    save(job_path,job)
    if not execute: return job
    config=json.loads((root/'bridge.config.json').read_text(encoding='utf-8-sig'))
    if config.get('expectedVersion')!='23.2.2' or config.get('mcpMutationsPanel') is not True:
        raise ValueError('Unexpected Bridge configuration')
    protected=[(config['expectedProject'],config['testProjectSha256']),(config['protectedProject'],config['protectedProjectSha256'])]
    for path, expected in protected:
        if file_hash(path)!=expected: raise ValueError('Protected source changed')
    client=None
    try:
        client=McpClient(root)
        job['state']='running';save(job_path,job)
        for offset in range(len(job['layers']),len(heights),2):
            planned=heights[offset:offset+2]
            batch={'batchIndex':len(job['batches'])+1,'heightsWorldMm':planned,'state':'not_started'}
            job['batches'].append(batch);save(job_path,job)
            client.call('activate_test_baseline',{})
            plan=client.call('prepare_recipe',{'recipeId':'slice_test_cube_xy','parameters':{'firstZMm':planned[0],'lastZMm':planned[-1],'stepMm':step/1000000}})
            if plan.get('state')!='plan_ready' or plan['plan']['sliceHeightsWorldMm']!=planned:
                raise ValueError('Batch plan grid differs')
            batch.update(operationId=plan['plan']['operationId'],planSha256=plan['planSha256'],state='planned')
            save(job_path,job)
            # Write the uncertain state before sending. Never retry this call automatically.
            batch['state']='execution_requested_outcome_pending';save(job_path,job)
            result=client.call('execute_recipe',{'operationId':batch['operationId'],'planSha256':batch['planSha256']})
            if result.get('state')!='succeeded_slices_saved_reopened_dgk_verified':
                raise ValueError('Batch execution outcome not certified')
            recorded=client.call('operation_result',{'operationId':batch['operationId']})
            receipt=recorded['receipt'];verify_receipt(root,batch['operationId'],receipt)
            batch.update(state='succeeded_verified',receiptSha256=recorded['receiptSha256'],resultPath=receipt['resultPath'],manifestPath=receipt['manifestPath'])
            for local_index, layer in enumerate(receipt['layers']):
                if layer['zWorldMm']!=planned[local_index] or layer['curve']['lengthMm']!=80 or not layer['curve']['closed']:
                    raise ValueError('Batch layer mapping differs')
                job['layers'].append({'globalLayerIndex':offset+local_index+1,'zWorldMm':layer['zWorldMm'],'sourceOperationId':batch['operationId'],'batchLayerIndex':layer['layerIndex'],'dgkPath':layer['dgkPath'],'dgkSha256':layer['dgkSha256'],'importedProjectPath':layer['importedProjectPath'],'importedProjectSha256':layer['importedProjectSha256']})
            save(job_path,job)
            print(f'Batch {batch["batchIndex"]}: {planned}; verified {len(job["layers"])}/{len(heights)} layers.',flush=True)
        actual=[layer['zWorldMm'] for layer in job['layers']]
        if actual!=heights or len(actual)!=len(set(actual)):
            raise ValueError('Missing, duplicate or reordered global layers')
        for path, expected in protected:
            if file_hash(path)!=expected: raise ValueError('Protected source changed during job')
        job['state']='succeeded_all_layers_verified';job['protectedSourceHashesUnchanged']=True
        job['limitations']='Separate batch Result projects; no single consolidated TM project. Fixed cube sections only. Imported layer psmodels not individually reopened. No technological acceptance, contour cleanup/deviation or production group naming.'
        save(job_path,job)
        return job
    except Exception as error:
        job['state']='stopped_requires_inspection';job['error']=str(error);job['recovery']='Read the recorded operation journal and checkpoint; no automatic retry or resume.';save(job_path,job)
        raise
    finally:
        if client: client.close()

if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--bridge-root',required=True,type=Path)
    parser.add_argument('--job-directory',required=True,type=Path)
    parser.add_argument('--step-mm',required=True)
    parser.add_argument('--execute',action='store_true')
    parser.add_argument('--resume-with-verification',help='Successful independent verification ID; no uncertain operation is retried')
    parser.add_argument('--grouped-output',type=Path,help='Final DGKs: one per source level. Defaults to job-directory/DGK.')
    args=parser.parse_args()
    result=run(args.bridge_root.resolve(),args.step_mm,args.job_directory.resolve(),args.execute,args.resume_with_verification)
    print(result['state'])
    if args.execute:
        # Intermediate per-height DGKs are verification fixtures. Deliver grouped files.
        from group_job import run as export_groups
        grouped=export_groups(args.bridge_root.resolve(),args.job_directory.resolve()/'job.json',(args.grouped_output or args.job_directory/'DGK').resolve(),True)
        print(grouped['state'])
