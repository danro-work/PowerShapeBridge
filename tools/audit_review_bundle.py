"""Inspect a development distribution directory or ZIP without extracting or starting it."""
import argparse
import hashlib
import json
import re
import zipfile
from pathlib import Path, PurePosixPath

NATIVE = {
    'PSBridge.exe','PSBridge.Worker.exe','bridge.config.json','mcp_server.py','core_config.py',
    'entity_sets.py','operation_plans.py','working_copies.py','document_bindings.py','working_copy_open.py',
    'owned_operation_plans.py','owned_translation.py','entity_references.py','document_handles.py',
    'surface_job.py','slice_job.py','group_job.py',
}


def require(condition, reason):
    if not condition:
        raise ValueError(reason)


def safe_name(name):
    require(isinstance(name,str) and name and '\\' not in name and ':' not in name, 'unsafe_archive_path')
    path=PurePosixPath(name)
    require(not path.is_absolute() and all(p not in ('','.','..') for p in name.split('/')), 'unsafe_archive_path')
    for part in path.parts:
        require(part==part.rstrip(' .') and not any(ord(c)<32 for c in part), 'unsafe_archive_path')
        require(not re.fullmatch(r'(?i)(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?',part), 'unsafe_archive_path')
    return path


def allowed(name):
    path=safe_name(name)
    if name in ('bundle-manifest.json','README.txt','dependency-lock.json','PSBridge.exe'):
        return True
    if len(path.parts)==3 and path.parts[:2]==('releases','P2.6'):
        return path.name in NATIVE
    if path.parts[0]=='mcp':
        if len(path.parts)==2:
            return path.suffix.lower()=='.dll' or path.name in ('PowerShapeMcp.Host.exe','PowerShapeMcp.Host.deps.json','PowerShapeMcp.Host.runtimeconfig.json')
        return path.parts[1:]==('runtimes','win','lib','net10.0',path.name) and path.name in ('System.Diagnostics.EventLog.dll','System.Diagnostics.EventLog.Messages.dll')
    return False


def inspect_entries(entries):
    require('bundle-manifest.json' in entries, 'missing_manifest')
    folded=set()
    for name in entries:
        require(allowed(name), 'unapproved_payload:'+name)
        require(name.casefold() not in folded,'duplicate_path')
        folded.add(name.casefold())
    manifest=json.loads(entries['bundle-manifest.json'])
    require(manifest['schema']=='powershape.review-bundle.v1','manifest_schema')
    require(manifest['releaseEligible'] is False and manifest['installed'] is False and manifest['cadQualificationPerformedByPackaging'] is False,'review_scope_only')
    require(manifest['transport']=='stdio' and manifest['configurationProfile']=='general-v1','unsupported_profile')
    require(set(manifest['pendingMilestones'])=={'K8','K9','K10','K11'},'missing_release_gates')
    files=manifest['files']
    require(len(files)==len({row['path'] for row in files}),'duplicate_manifest_path')
    require({row['path'] for row in files}==set(entries)-{'bundle-manifest.json'},'inventory_mismatch')
    for row in files:
        data=entries[row['path']]
        require(type(row['length']) is int and row['length']==len(data),'length_mismatch:'+row['path'])
        require(hashlib.sha256(data).hexdigest()==row['sha256'],'hash_mismatch:'+row['path'])
    required={'PSBridge.exe','README.txt','dependency-lock.json','mcp/PowerShapeMcp.Host.dll','mcp/PowerShapeMcp.Host.deps.json','mcp/PowerShapeMcp.Host.runtimeconfig.json'}|{'releases/P2.6/'+name for name in NATIVE}
    require(required<=set(entries),'required_component_missing')
    config=json.loads(entries['releases/P2.6/bridge.config.json'])
    require(config['coreProfile']=='general-v1' and config['expectedVersion']=='23.2.2' and config.get('captureViewport') is False,'configuration_policy')
    for key,value in config.items():
        if isinstance(value,str):
            require(not re.search(r'[A-Za-z]:[\\/]|^[/\\]|[\r\n]',value),'machine_path_in_config')
        if key in ('expectedProject','sourceStep','protectedProject','qualificationOperationId','qualificationReceiptSha256','testProjectSha256','protectedProjectSha256'):
            require(value in (None,''),'fixture_data_in_config')
        if key in ('translationTestPanel','checkpointTestPanel','parameterizedTranslationPanel','diskVerificationPanel','mcpMutationsPanel','recipeOperationsPanel'):
            require(value in (None,False),'fixture_flag_in_config')
    runtime=json.loads(entries['mcp/PowerShapeMcp.Host.runtimeconfig.json'])
    require(runtime['runtimeOptions']['tfm']=='net10.0','unsupported_sdk_runtime')
    catalogs=manifest['components']['sdk']['catalogs']
    require(set(catalogs)=={'general_core','read_only'},'missing_catalog')
    for catalog in catalogs.values():
        require(catalog['count']==len(catalog['names'])==len(set(catalog['names'])),'catalog_count_mismatch')
    require(set(catalogs['read_only']['names'])<set(catalogs['general_core']['names']),'catalog_mode_split')
    require('activate_document' not in catalogs['read_only']['names'] and not any(n.startswith(('execute_','prepare_','adopt_','verify_','create_','open_','bind_','validate_')) for n in catalogs['read_only']['names']),'mutator_in_readonly_catalog')
    require(manifest['components']['sdk']['catalogQualification']=='discovery_only_not_cad_acceptance','catalog_scope')
    require(manifest['components']['native']['workerSha256']==hashlib.sha256(entries['releases/P2.6/PSBridge.Worker.exe']).hexdigest(),'native_identity_mismatch')
    require(manifest['components']['sdk']['hostSha256']==hashlib.sha256(entries['mcp/PowerShapeMcp.Host.dll']).hexdigest(),'sdk_identity_mismatch')
    require(manifest['components']['native']['configurationSha256']==hashlib.sha256(entries['releases/P2.6/bridge.config.json']).hexdigest(),'configuration_identity_mismatch')
    runtime_assets={name for name in entries if name.startswith('mcp/runtimes/')}
    proofs=manifest['runtimeAssetProof']
    require({p['path'] for p in proofs}==runtime_assets and len(proofs)==len(runtime_assets),'runtime_asset_proof_missing')
    locked=json.loads(entries['dependency-lock.json'])['dependencies']['net10.0']
    for proof in proofs:
        require(hashlib.sha256(entries[proof['path']]).hexdigest()==proof['sha256'],'runtime_asset_identity_mismatch')
        dependency=locked[proof['package']]
        require(dependency['resolved']==proof['version'] and dependency['contentHash']==proof['packageContentHash']==proof['contentProof']['contentHash'],'runtime_asset_lock_mismatch')
        require(proof['contentProof']['state']=='passed' and proof['contentProof']['networkAccessAttempted'] is False,'runtime_asset_content_proof')
        require(not proof['contentProof']['signed'] or proof['contentProof']['signatureContentIntegrityVerified'] is True,'runtime_asset_integrity_proof')
    return {'state':'passed','releaseEligible':False,'fileCount':len(files),'totalFileCount':len(entries),'catalogCounts':{k:v['count'] for k,v in catalogs.items()},'cadConnectionAttempted':False,'extracted':False,'scope':'distribution inventory and bytes; not install, clean-machine startup, CAD acceptance or artifact authenticity'}


def inspect_bundle(path):
    path=Path(path)
    if path.is_dir():
        entries={}
        for item in path.rglob('*'):
            require(not item.is_symlink() and not (hasattr(item,'is_junction') and item.is_junction()),'reparse_entry')
            if item.is_file():
                name=item.relative_to(path).as_posix()
                require(allowed(name),'unapproved_payload:'+name)
                entries[name]=item.read_bytes()
    else:
        entries={}
        with zipfile.ZipFile(path) as archive:
            for info in archive.infolist():
                safe_name(info.filename)
                require(not info.is_dir() and info.filename not in entries,'duplicate_or_directory_zip_entry')
                require((info.external_attr>>16)&0o170000!=0o120000,'symlink_zip_entry')
                require(allowed(info.filename),'unapproved_payload:'+info.filename)
                entries[info.filename]=archive.read(info)
    return inspect_entries(entries)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--bundle',required=True)
    parser.add_argument('--output')
    args=parser.parse_args()
    result=inspect_bundle(args.bundle)
    if args.output:
        with Path(args.output).open('x',encoding='utf-8') as stream:json.dump(result,stream,indent=2)
    print(json.dumps(result))
