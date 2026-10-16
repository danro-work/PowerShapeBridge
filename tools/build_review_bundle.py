"""Assemble already qualified development binaries; no install, panel start, CAD call or release."""
import argparse
import hashlib
import json
import re
import shutil
import subprocess
import sys
import zipfile
from datetime import datetime,timezone
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]


def need(ok,message):
    if not ok:raise ValueError(message)


def read(path):return json.loads(Path(path).read_text(encoding='utf-8-sig'))
def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def relative(name):
    from audit_review_bundle import safe_name
    safe_name(name)
    return Path(*name.split('/'))


def ordinary(path):
    path=Path(path).absolute()
    for item in (path,*path.parents):
        need(not item.is_symlink() and not (hasattr(item,'is_junction') and item.is_junction()),'Reparse paths are not packaged.')


def catalogs(dotnet,host,bridge):
    sys.path.insert(0,str(ROOT/'src'))
    from test_sdk_stdio import Client
    values={};version=None
    for readonly in (False,True):
        command=[dotnet,str(host),'--bridge-root',str(bridge),'--timeout','120']
        if readonly:command.append('--read-only')
        client=Client(command)
        try:
            hello=client.call(1,'initialize',{'protocolVersion':'2025-11-25','capabilities':{},'clientInfo':{'name':'distribution-discovery-only','version':'1'}})
            info=hello['result']['serverInfo']
            need(version is None or version==info['version'],'SDK mode versions differ.')
            version=info['version'];client.notify('notifications/initialized')
            tools=client.call(2,'tools/list')['result']['tools']
            need(not readonly or all(t['annotations']['readOnlyHint'] is True for t in tools),'Read-only annotations differ.')
            values['read_only' if readonly else 'general_core']={'count':len(tools),'names':sorted(t['name'] for t in tools)}
        finally:client.close()
    return version,values


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--qualified-package',required=True)
    parser.add_argument('--output',required=True)
    parser.add_argument('--dotnet',required=True)
    parser.add_argument('--nuget-cache',required=True)
    args=parser.parse_args()
    package=Path(args.qualified_package).absolute();output=Path(args.output).absolute()
    for path in (package,output):
        ordinary(path)
        need(path.resolve().is_relative_to((ROOT/'work').resolve()),'Source and new bundle must be under project work.')
    archive=output.with_suffix('.zip')
    need(not output.exists() and not archive.exists(),'Use a fresh directory/archive; never overwrite a frozen bundle.')
    q=read(package/'qualification.json');source=read(package/'source-commit.json')
    need(sha(package/'qualification.json')==source['qualificationSha256'],'Source qualification pin differs.')
    native=package/'native';sdk=package/'sdk';build=read(native/'build-manifest.json')
    need(build['version']==q['nativeVersion'] and build['configurationProfile']=='general-v1','Native qualified profile differs.')
    expected={row['name']:row['afterSha256'] for row in q['deploymentFiles']}
    need(len(expected)==18 and len(build['files'])==18 and {r['name'] for r in build['files']}==set(expected),'Native inventory differs.')
    rows=[]
    for row in build['files']:
        name=row['name'];relative(name);ordinary(native/name)
        need(sha(native/name)==row['sha256']==expected[name],'Native hash differs: '+name)
        destination='PSBridge.exe' if name=='PSBridge.Launcher.exe' else 'releases/P2.6/'+name
        rows.append((native/name,destination,'native'))
    lock=read(ROOT/'src/PowerShapeMcp.Host/packages.lock.json')['dependencies']['net10.0']
    deps=read(sdk/'PowerShapeMcp.Host.deps.json')
    targets={k:v for table in deps['targets'].values() for k,v in table.items()}
    runtime_proof=[];runtime_expected={};content_proofs={}
    dotnet_path=Path(shutil.which(args.dotnet) or args.dotnet).resolve()
    sdk_version=subprocess.check_output([str(dotnet_path),'--version'],cwd=ROOT,text=True).strip()
    need(re.fullmatch(r'\d+\.\d+\.\d+(?:[-.A-Za-z0-9]*)?',sdk_version) is not None,'Unexpected installed .NET SDK version.')
    nuget_sdk=dotnet_path.parent/'sdk'/sdk_version
    pwsh=shutil.which('pwsh')
    need(pwsh is not None,'PowerShell 7 is required for the offline NuGet content proof.')
    for library,value in targets.items():
        for asset in value.get('runtimeTargets',{}):
            relative(asset)
            if (sdk/relative(asset)).is_file():runtime_expected[asset]=library
    actual={p.relative_to(sdk).as_posix():p for p in sdk.rglob('*') if p.is_file()}
    qualified={str(k).replace('\\','/'):v for k,v in q['sdkFiles'].items()}
    need(set(qualified)<=set(actual) and set(actual)-set(qualified)<=set(runtime_expected),'Unqualified SDK payload found.')
    for name,path in actual.items():
        relative(name);ordinary(path)
        if name in qualified:need(sha(path)==qualified[name],'SDK qualified hash differs: '+name)
        if name in runtime_expected:
            library=runtime_expected[name];identity,version=library.rsplit('/',1)
            need(identity in lock and lock[identity]['resolved']==version,'Runtime asset is not locked.')
            nupkg=Path(args.nuget_cache)/identity.lower()/version/(identity.lower()+'.'+version+'.nupkg')
            ordinary(nupkg)
            if library not in content_proofs:
                proof=json.loads(subprocess.check_output([pwsh,'-NoProfile','-File',str(ROOT/'tools/read-nuget-content-proof.ps1'),'-Nupkg',str(nupkg),'-LockedContentHash',lock[identity]['contentHash'],'-NugetSdkDirectory',str(nuget_sdk)]))
                need(proof['state']=='passed' and proof['contentHash']==lock[identity]['contentHash'] and proof['networkAccessAttempted'] is False,'Offline NuGet content proof failed.')
                content_proofs[library]=proof
            with zipfile.ZipFile(nupkg) as source_zip:need(hashlib.sha256(source_zip.read(name)).hexdigest()==sha(path),'Runtime DLL differs from locked NuGet archive.')
            runtime_proof.append({'path':'mcp/'+name,'package':identity,'version':version,'packageContentHash':lock[identity]['contentHash'],'sha256':sha(path),'contentProof':content_proofs[library]})
        if path.suffix.lower()=='.pdb':continue
        rows.append((path,'mcp/'+name,'sdk'))
    output.mkdir(parents=True)
    from audit_review_bundle import allowed,inspect_bundle
    for origin,destination,_ in rows:
        need(allowed(destination),'Distribution allowlist rejected '+destination)
        target=output/relative(destination);target.parent.mkdir(parents=True,exist_ok=True)
        shutil.copyfile(origin,target)
        need(sha(origin)==sha(target),'Copy hash differs.')
    (output/'README.txt').write_text('PowerShape Bridge — development review bundle\n\nThis is an inventory/packaging preview, not MCP v1 or a release candidate.\nIt has not been installed and no panel, worker queue or CAD tool is started by packaging.\nNative and SDK binaries remain experimental; K8–K11 acceptance is pending.\nUse the manifest to inspect versions, relative paths and hashes.\nA future installation requires one native panel, a separate read-only MCP server,\nstartup/reconnect checks and a reviewed rollback procedure.\nDo not infer CAD support from the number of discovered tool names.\n',encoding='utf-8')
    (output/'dependency-lock.json').write_text(json.dumps({'version':1,'dependencies':{'net10.0':lock}},indent=2),encoding='utf-8')
    native_info=json.loads(subprocess.check_output([str(output/'releases/P2.6/PSBridge.Worker.exe'),'--component-info'],cwd=output))
    need(native_info['state']=='component_configuration_validated' and native_info['powerShapeConnectionAttempted'] is False and native_info['version']==q['nativeVersion'],'Offline native component check failed.')
    sdk_version,discovered=catalogs(args.dotnet,output/'mcp/PowerShapeMcp.Host.dll',output/'releases/P2.6')
    need(sdk_version==q['sdkVersion'],'Actual SDK server version differs.')
    need(not (output/'releases/P2.6/requests').exists(),'Discovery must not create CAD requests.')
    files=[{'path':p.relative_to(output).as_posix(),'length':p.stat().st_size,'sha256':sha(p)} for p in sorted(output.rglob('*')) if p.is_file()]
    manifest={'schema':'powershape.review-bundle.v1','createdUtc':datetime.now(timezone.utc).isoformat(),'releaseEligible':False,'installed':False,'cadQualificationPerformedByPackaging':False,'configurationProfile':'general-v1','transport':'stdio','sourceRevision':source['sourceCommit'],'sourceQualificationSha256':sha(package/'qualification.json'),'pendingMilestones':['K8','K9','K10','K11'],'components':{'native':{'version':q['nativeVersion'],'workerSha256':q['nativeWorkerSha256'],'framework':'.NET Framework 4.8','configurationSha256':sha(output/'releases/P2.6/bridge.config.json')},'sdk':{'version':sdk_version,'hostSha256':q['sdkHostSha256'],'framework':'.NET 10','catalogQualification':'discovery_only_not_cad_acceptance','catalogs':discovered}},'runtimeAssetProof':runtime_proof,'excluded':['CAD models','session reports','operation receipts','debug symbols'],'files':files}
    (output/'bundle-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    directory_result=inspect_bundle(output)
    with zipfile.ZipFile(archive,'x',compression=zipfile.ZIP_DEFLATED) as target:
        for p in sorted(output.rglob('*')):
            if p.is_file():
                info=zipfile.ZipInfo(p.relative_to(output).as_posix(),date_time=(1980,1,1,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED;info.external_attr=0o100644<<16
                target.writestr(info,p.read_bytes())
    zip_result=inspect_bundle(archive)
    need(directory_result==zip_result,'Directory and ZIP audits differ.')
    report={'state':'passed','scope':'development packaging only; no installation or CAD acceptance','directory':str(output),'archive':str(archive),'archiveSha256':sha(archive),'sourceVersion':{'native':q['nativeVersion'],'sdk':sdk_version},'runtimeAssetsCheckedAgainstLockedNuget':len(runtime_proof),'audit':directory_result}
    with output.with_suffix('.audit.json').open('x',encoding='utf-8') as stream:json.dump(report,stream,indent=2)
    print(json.dumps({'state':'passed','fileCount':len(files),'totalFileCount':len(files)+1,'runtimeAssetsCheckedAgainstLockedNuget':len(runtime_proof),'releaseEligible':False,'installed':False}))


if __name__=='__main__':main()
