"""Offline installed export-profile catalog. Never a getter for effective CAD settings."""
import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET


def inspect(source):
    data=source.read_bytes()
    if not data or len(data)>4*1024*1024:
        raise ValueError('Bounded nonempty XML catalog required')
    if b'<!DOCTYPE' in data.upper() or b'<!ENTITY' in data.upper():
        raise ValueError('DTD/entities are unsupported')
    root=ET.fromstring(data)
    if root.tag!='exportwizardtypes':
        raise ValueError('Unexpected export wizard root')
    profiles=[]
    seen=set()
    for wizard in root:
        if wizard.tag!='wizard' or set(wizard.attrib)!={'ID'}:
            raise ValueError('Unexpected wizard shape')
        name=wizard.attrib['ID']
        if not name or name in seen:
            raise ValueError('Missing/duplicate wizard ID')
        seen.add(name)
        sections={}
        for section in wizard:
            if section.tag in sections or section.attrib:
                raise ValueError('Duplicate/attributed profile section')
            values={}
            for field in section:
                if field.tag in values or field.attrib or list(field):
                    raise ValueError('Duplicate/complex profile field')
                values[field.tag]=(field.text or '').strip()
            sections[section.tag]=values
        profiles.append(dict(id=name,sections=sections))
    if not profiles:
        raise ValueError('No installed export profiles')
    return dict(schema='psbridge.static-export-profile-catalog.v1',
                state='static_export_profile_catalog_read_unqualified',
                source=str(source),sourceSha256=hashlib.sha256(data).hexdigest(),sourceBytes=len(data),
                profileCount=len(profiles),profiles=profiles,
                powerShapeConnectionAttempted=False,liveStateRead=False,effectiveSettingsVerified=False,
                selectedTargetSystem=None,lengthUnits='not_qualified_from_catalog',
                warning='Static catalog values are not effective current model/export settings; preserve section names.')


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--source',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    if args.output.exists():
        raise ValueError('Fresh output required; no overwrite')
    result=inspect(args.source.resolve())
    args.output.parent.mkdir(parents=True,exist_ok=True)
    with args.output.open('x',encoding='utf-8') as file:
        json.dump(result,file,indent=2)
        file.write('\n')
    print(f"READ: {result['profileCount']} static profiles; effective settings unqualified; no CAD.")
