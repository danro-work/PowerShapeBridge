"""Independent byte/identity audit of the offline Framework DGK fixture matrix."""
import argparse
import hashlib
import json
from pathlib import Path


def audit(stage, output):
    stage = stage.resolve()
    matrices = list(stage.glob('dgk-bundle-file-tests-*/matrix.json'))
    assert len(matrices) == 1, 'Fresh isolated stage with exactly one matrix required'
    matrix = json.loads(matrices[0].read_text(encoding='utf-8-sig'))
    assert matrix['state'] == 'passed' and matrix['cadConnectionAttempted'] is False
    cases = matrix['cases']
    required = {'primary', 'features', 'optional_absent', 'missing_primary', 'missing_companion',
                'stray_file', 'nested_directory', 'hash_mismatch', 'empty', 'hardlink', 'held_writer', 'outside_root'}
    assert len(cases) == len(required) and {x['kind'] for x in cases} == required
    controls = {'primary', 'features', 'optional_absent'}
    hashes = []
    for case in cases:
        assert case['accepted'] is (case['kind'] in controls)
        if case['kind'] not in controls:
            assert case['error'] and case['result'] is None
            continue
        result = case['result']
        assert result['state'] == 'dgk_bundle_bytes_observed_unqualified'
        for flag in ('cadGeometryVerified', 'exportSettingsQualified', 'ownershipAuthorized',
                     'cadConnectionAttempted', 'companionExecutionAttempted'):
            assert result[flag] is False
        folder = Path(result['directory']).resolve()
        assert folder.parent == matrices[0].parent and folder.name == case['kind']
        assert {x.name for x in folder.iterdir()} == {x['name'] for x in result['artifacts']}
        assert len(result['artifacts']) == (3 if case['kind'] == 'features' else 1)
        assert result['absentOptionalArtifacts'] == (['sample.dmt'] if case['kind'] == 'optional_absent' else [])
        total = 0
        for item in result['artifacts']:
            file = folder / item['name']
            size = file.stat().st_size
            digest = hashlib.sha256(file.read_bytes()).hexdigest()
            assert size == item['length'] and digest == item['sha256']
            assert file.stat().st_nlink == 1
            # CPython's Windows stat file index is independently obtained by the
            # runtime. Check the native volume/file pair is present and nonzero.
            assert int(item['fileIdentity']['fileIndex'], 16) == file.stat().st_ino
            assert int(item['fileIdentity']['volumeSerial'], 16) != 0
            assert item['contentQualification'] == 'opaque_bytes_content_unqualified'
            assert item['lengthUnits'] == 'unknown' and item['vertexCount'] == item['triangleCount'] == -1
            assert item['expectedHashMatched'] is False
            total += size
            hashes.append({'path': str(file), 'sha256': digest, 'length': size})
        assert total == result['totalBytes']
    record = {'state': 'passed', 'cases': len(cases), 'controls': len(controls),
              'refusals': len(cases) - len(controls), 'hashes': hashes,
              'matrixSha256': hashlib.sha256(matrices[0].read_bytes()).hexdigest(),
              'cadConnectionAttempted': False}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(record, indent=2), encoding='utf-8')
    print(f"DGK file witness independent audit: {len(cases)} cases, {len(hashes)} hashes; no CAD.")


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--stage', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    audit(args.stage, args.output)
