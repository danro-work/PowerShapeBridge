import hashlib
import json
from pathlib import Path
import contextlib
import uuid
from mcp_server import Server,validate_mutation_args,RpcError
from slice_job import file_hash
with contextlib.nullcontext(Path(__file__).resolve().parents[1]/'work'/('surface-mcp-test-'+uuid.uuid4().hex)) as temporary:
    root=Path(temporary)/'releases/P2.6';root.mkdir(parents=True)
    (root/'bridge.config.json').write_text(json.dumps({'mcpMutationsPanel':True,'recipeOperationsPanel':True}))
    (root/'reports').mkdir();folder=root.parent.parent/'P2_Disk_Verification';folder.mkdir()
    identity='ce7c3a8f6fb8410c822ebee8552c780a'
    source=folder/('ExchangeImported_'+identity+'.psmodel');source.write_bytes(b'local source fixture')
    receipt_path=root/'reports'/('exchange-'+identity+'.json')
    receipt_path.write_text(json.dumps({'state':'succeeded_dgk_roundtrip','importedProjectPath':str(source),'importedProjectSha256':file_hash(source)}))
    server=Server(root)
    names={tool['name'] for tool in server.tools}
    assert {'prepare_test_surface_job','execute_test_surface_job_phase'}<=names
    prepared=server.prepare_surface_job()
    assert prepared['state']=='surface_job_plan_ready' and not prepared['cadMutationAttempted']
    assert file_hash(prepared['planPath'])==prepared['planSha256']
    assert prepared['plan']['heightsWorldMm']==[n*800000/1000000 for n in range(25)]
    assert not (root/'requests').exists()
    arguments={'jobId':prepared['jobId'],'planSha256':prepared['planSha256'],'phase':'slice','index':2}
    validate_mutation_args('execute_test_surface_job_phase',arguments)
    for invalid in (dict(arguments,index=True),dict(arguments,index=25),dict(arguments,phase='recover_slice_'+'a'*32),dict(arguments,phase='setup'),dict(arguments,jobId='../x'),dict(arguments,command='DELETE')):
        try:validate_mutation_args('execute_test_surface_job_phase',invalid)
        except RpcError:pass
        else:raise AssertionError(invalid)
    called=[];replies=[]
    def snapshot(cancel,operation,parameters):
        called.append((operation,parameters));return {'state':'succeeded_surface_section_phase'}
    server.snapshot=snapshot;server.reply=lambda identity,result=None,error=None:replies.append(result or error)
    server.call(1,{'name':'execute_test_surface_job_phase','arguments':arguments},None)
    assert called==[('recipe.prepare',{'recipeId':'test_surface_section_phase','parameters':arguments})]
    assert replies[-1]['isError'] is False
    source.write_bytes(b'changed fixture')
    try:server.prepare_surface_job()
    except ValueError:pass
    else:raise AssertionError('Changed source accepted')
print('PASS: immutable local surface plan, exact grid, fixed MCP envelope, argument rejection and changed-source rejection.')
