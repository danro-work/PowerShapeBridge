"""Check the real development ZIP and reject changed bytes, CAD payload and unsafe paths; no extraction."""
import argparse
import hashlib
import json
import zipfile
from pathlib import Path
from audit_review_bundle import inspect_bundle


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--bundle',required=True)
    parser.add_argument('--evidence',required=True)
    args=parser.parse_args()
    archive=Path(args.bundle);output=Path(args.evidence)
    assert not output.exists()
    before=hashlib.sha256(archive.read_bytes()).hexdigest()
    good=inspect_bundle(archive)
    assert good['state']=='passed'
    output.mkdir(parents=True)
    with zipfile.ZipFile(archive) as source:
        original={row.filename:source.read(row) for row in source.infolist()}
    results=[]
    for fault,expected in (
        ('changed_byte','hash_mismatch:'),('cad_in_manifest','unapproved_payload:'),
        ('path_traversal','unsafe_archive_path'),('release_claim','review_scope_only'),
    ):
        values=dict(original)
        if fault=='changed_byte':
            name='releases/P2.6/mcp_server.py'
            payload=bytearray(values[name]);payload[0]^=1;values[name]=bytes(payload)
        elif fault=='cad_in_manifest':
            name='releases/P2.6/Working.psmodel';payload=b'SYNTHETIC PRIVATE CAD PAYLOAD, NEVER OPENED'
            values[name]=payload;manifest=json.loads(values['bundle-manifest.json'])
            manifest['files'].append({'path':name,'length':len(payload),'sha256':hashlib.sha256(payload).hexdigest()})
            values['bundle-manifest.json']=json.dumps(manifest).encode()
        elif fault=='path_traversal':values['../escape.txt']=b'MUST NEVER BE EXTRACTED'
        elif fault=='release_claim':
            manifest=json.loads(values['bundle-manifest.json']);manifest['releaseEligible']=True
            values['bundle-manifest.json']=json.dumps(manifest).encode()
        destination=output/(fault+'.zip')
        with zipfile.ZipFile(destination,'x',compression=zipfile.ZIP_DEFLATED) as changed:
            for name,data in values.items():changed.writestr(name,data)
        try:inspect_bundle(destination)
        except ValueError as failure:
            assert str(failure).startswith(expected),(fault,str(failure))
            results.append({'case':fault,'rejected':True,'reason':str(failure)})
        else:raise AssertionError('Invalid archive accepted: '+fault)
    assert hashlib.sha256(archive.read_bytes()).hexdigest()==before
    assert not (output.parent/'escape.txt').exists()
    result={'state':'passed','positiveArchiveAudit':good,'negativeControls':results,'sourceArchiveUnchanged':True,'cadConnectionAttempted':False,'installationPerformed':False,'extracted':False}
    with (output/'result.json').open('x',encoding='utf-8') as stream:json.dump(result,stream,indent=2)
    print(json.dumps({'state':'passed','positiveControls':1,'negativeControls':len(results),'cadConnectionAttempted':False,'installationPerformed':False}))


if __name__=='__main__':main()
