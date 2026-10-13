"""Independent disk/layout audit of passive fake-getter qualification."""
import argparse
import hashlib
import json
from pathlib import Path

from audit_checkpoint_recovered_open import read, require, sha


def canonical(value):
    if isinstance(value, dict):
        return '{' + ','.join(json.dumps(key, ensure_ascii=False) + ':' + canonical(value[key]) for key in sorted(value)) + '}'
    if isinstance(value, list):
        return '[' + ','.join(canonical(item) for item in value) + ']'
    if isinstance(value, float) and value.is_integer():
        return str(int(value))
    return json.dumps(value, ensure_ascii=False, separators=(',', ':'))


def audit(stage):
    require((stage / '.rotation-measurement-fixture').is_file(), 'Missing own fixture marker')
    report = read(stage / 'reports/rotation-measurement-tests.json')
    require(report['state'] == 'passed' and report['cadConnectionAttempted'] is False and
            report['liveGetterQualified'] is False and len(report['rows']) == 28, 'Wrong matrix/scope')
    common = {'control', 'error_code', 'nonfinite', 'bool_numeric', 'array_rank', 'short_points',
              'flag_numeric', 'changed_process', 'mapping_changed', 'expired', 'ambiguous_name', 'invalid_domain'}
    require({(r['kind'], r['fault']) for r in report['rows']} ==
            {('surface', f) for f in common | {'short_normals', 'wrong_type'}} |
            {('curve', f) for f in common | {'short_tangents', 'scalar_magnitude'}}, 'Missing scenario')
    hashes = 0
    for row in report['rows']:
        root = Path(row['root']).resolve()
        require(root.is_relative_to(stage / 'work') and row['cadConnectionAttempted'] is False, 'Foreign root or live scope')
        records = {Path(path).resolve(): digest for path, digest in row['protectedHashes'].items()}
        require(set(records) == {path.resolve() for path in root.rglob('*') if path.is_file()} and
                all(path.is_relative_to(root) and sha(path) == digest for path, digest in records.items()), 'Read changed files')
        hashes += len(records)
        before = row['fault'] in {'expired', 'ambiguous_name', 'invalid_domain', 'wrong_type'}
        require(row['getterCalls'] == (0 if before else 1), 'Wrong getter/retry count')
        if row['fault'] != 'control':
            require(row['samples'] is None and row['descriptor'] is None and row['raw'] is None and row['error'], 'Failure granted samples')
            continue
        require(row['observationReads'] == 4 and row['error'] is None, 'Missing repeated guards')
        raw, sample, descriptor = row['raw'], row['samples'], row['descriptor']
        require(all(raw[flag] is False for flag in ('geometryMutationAttempted', 'contextChangeAttempted', 'cadSaveAttempted')), 'Read acquired mutation')
        require(sample['descriptorSha256'] == hashlib.sha256(canonical(descriptor).encode()).hexdigest(), 'Descriptor SHA differs')
        response = raw['getterResponse']
        require(response[0] == 0 and descriptor['coordinateFrame'] == sample['frame'] == 'world' and
                descriptor['lengthUnits'] == sample['units'] == 'document_raw', 'Getter/frame policy differs')
        count = 4 if row['kind'] == 'surface' else 2
        require(len(response) == (4 if row['kind'] == 'surface' else 10) and len(sample['positions']) == count, 'Response/position shape differs')
        require([v for witness in sample['positions'] for v in witness['value']] == response[1] and
                sample['boundsWorldRawPaired'] == raw['boundsWorldRawPaired'], 'Position/bounds data lost')
        directions = response[2] if row['kind'] == 'surface' else response[2] + response[6]
        require([v for witness in sample['directions'] for v in witness['value']] == directions, 'Direction data lost')
        for role in ('positions', 'directions'):
            require(len({w['key'] for w in sample[role]}) == len(sample[role]) and all(len(w['value']) == 3 for w in sample[role]), 'Ambiguous anchors')
        if row['kind'] == 'surface':
            require(descriptor['domain']['visibility'] == response[3] and all(isinstance(flag, bool) for flag in response[3]), 'Visibility mask differs')
        else:
            require(descriptor['domain']['magnitudeLayout'] == 'installed_documentation_3_values_per_point' and
                    len(response[3]) == len(response[7]) == 3*count, 'Unqualified magnitude layout accepted')
    return {'state': 'passed', 'cases': 28, 'artifactHashesChecked': hashes, 'cadConnectionAttempted': False,
            'liveGetterQualified': False, 'scope': 'Native passive provider on fake COM response arrays; not live CAD proof'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('stage', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.stage.resolve())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(f"PASS: 28 passive measurement cases / {result['artifactHashesChecked']} file hashes independently audited.")
