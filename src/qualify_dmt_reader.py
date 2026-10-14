"""Independent struct-based audit of the Framework DMT decoder, no CAD."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import struct
import subprocess


def require(ok, message):
    if not ok:
        raise ValueError(message)


def decode(data):
    offset = data.index(b'\0') + 1
    version, flags, blocks, total_vertices, total_triangles = struct.unpack_from('<HIIII', data, offset)
    require(version == 1000 and flags & ~3 == 0, 'Unsupported positive fixture')
    offset += 18
    digest = hashlib.sha256(struct.pack('<II', flags, blocks))
    bounds = [math.inf, -math.inf, math.inf, -math.inf, math.inf, -math.inf]
    scalar = 'f' if flags & 1 else 'd'
    inventory = []
    count_vertices = count_triangles = 0
    for _ in range(blocks):
        block_flags, vertices, triangles = struct.unpack_from('<III', data, offset)
        offset += 12
        fields = 6 if block_flags & 1 else 3
        values = struct.unpack_from('<' + scalar * vertices * fields, data, offset)
        offset += vertices * fields * struct.calcsize('<' + scalar)
        positions = [values[i * fields + axis] for i in range(vertices) for axis in range(3)]
        normals = [values[i * fields + axis] for i in range(vertices) for axis in range(3, 6)] if fields == 6 else []
        index_type = 'I' if vertices > 65535 else 'H'
        indices = struct.unpack_from('<' + index_type * triangles * 3, data, offset)
        offset += triangles * 3 * struct.calcsize('<' + index_type)
        require(all(i < vertices for i in indices), 'Invalid positive index')
        require(struct.unpack_from('<H', data, offset)[0] == version, 'Positive trailer differs')
        offset += 2
        require(all(math.isfinite(v) for v in positions + normals), 'Nonfinite positive fixture')
        for axis in range(3):
            bounds[axis * 2] = min(bounds[axis * 2], *positions[axis::3])
            bounds[axis * 2 + 1] = max(bounds[axis * 2 + 1], *positions[axis::3])
        digest.update(struct.pack('<III', block_flags, vertices, triangles))
        digest.update(struct.pack('<' + 'd' * len(positions), *positions))
        digest.update(struct.pack('<' + 'd' * len(normals), *normals))
        digest.update(struct.pack('<' + 'I' * len(indices), *indices))
        inventory.append(dict(flags=block_flags, vertexCount=vertices, triangleCount=triangles,
                              normalCount=vertices if normals else 0))
        count_vertices += vertices
        count_triangles += triangles
    require(offset == len(data) and count_vertices == total_vertices and count_triangles == total_triangles,
            'Positive fixture consumption/count mismatch')
    return dict(fileFlags=flags, blockCount=blocks, vertexCount=total_vertices, triangleCount=total_triangles,
                decodedOrderedSha256=digest.hexdigest(), boundsPaired=bounds, blocks=inventory,
                lengthUnits='mm_declared_by_file_flag' if flags & 2 else 'unknown')


def synthetic(single=False, normals=False, vertices=3, mm=True):
    flags = int(single) | (2 if mm else 0)
    data = bytearray(b'Synthetic DMT in MM\0')
    data += struct.pack('<HIIIIIII', 1000, flags, 1, vertices, 1, int(normals), vertices, 1)
    for vertex in range(vertices):
        values = (vertex, 2 if vertex == 1 else 0, -3) + ((0, 0, 1) if normals else ())
        data += struct.pack('<' + ('f' if single else 'd') * len(values), *values)
    data += struct.pack('<' + ('III' if vertices > 65535 else 'HHH'), 0, 1, vertices - 1)
    data += struct.pack('<H', 1000)
    return bytes(data)


def qualify(binary, fixtures, stage, evidence):
    require(not stage.exists(), 'Fresh DMT fixture stage required')
    stage.mkdir(parents=True)
    evidence.mkdir(parents=True, exist_ok=True)
    official = [json.loads(line) for line in (fixtures / 'source-manifest.jsonl').read_text().splitlines()]
    require({row['name'] for row in official} == {'NormalDmt', 'TwoBlocks', 'smallModel'}, 'Official matrix missing')
    inputs = {}
    for row in official:
        data = (fixtures / (row['name'] + '.dmt')).read_bytes()
        require(len(data) == row['bytes'] and hashlib.sha256(data).hexdigest() == row['sha256'], 'Official LFS bytes changed')
        inputs[row['name']] = data
    for single, normals in ((False, False), (False, True), (True, False), (True, True)):
        inputs[f'synthetic_{single}_{normals}'] = synthetic(single, normals)
    inputs['ushort_boundary'] = synthetic(vertices=65535)
    inputs['uint_boundary'] = synthetic(vertices=65536)
    inputs['unknown_units'] = synthetic(mm=False)
    legacy = synthetic(single=True, normals=True, mm=False)
    inputs['legacy_copycad_header'] = b'COPYCAD historical header\n in MM\0' + legacy[legacy.index(0) + 1:]
    rows = []
    for name, data in inputs.items():
        path = stage / (name + '.dmt')
        path.write_bytes(data)
        result = subprocess.run([str(binary), str(path)], capture_output=True, text=True, check=True)
        actual = json.loads(result.stdout)
        expected = decode(data)
        require(all(actual[key] == value for key, value in expected.items()), f'{name}: decoded geometry differs')
        require(actual['sha256'] == hashlib.sha256(data).hexdigest() and actual['bytes'] == len(data) and
                actual['cadConnectionAttempted'] is False and actual['fullTopologyVerified'] is False and
                actual['coordinateFrame'] == 'file_coordinates_unqualified', 'Wrong source/qualification')
        rows.append(dict(name=name, expected=expected, actual=actual))
    bad = dict(empty=b'', truncated=inputs['NormalDmt'][:-1], trailing=inputs['NormalDmt'] + b'\0',
               lfs_pointer=b'version https://git-lfs.github.com/spec/v1\noid sha256:fake\nsize 549\n')
    bad_file = bytearray(synthetic())
    meta = bad_file.index(0) + 1
    struct.pack_into('<I', bad_file, meta + 2, 4)
    bad['unknown_flags'] = bytes(bad_file)
    for name, data in bad.items():
        path = stage / (name + '.dmt')
        path.write_bytes(data)
        result = subprocess.run([str(binary), str(path)], capture_output=True, text=True)
        require(result.returncode == 3 and result.stdout == '' and result.stderr.strip(), 'Malformed fixture accepted')
    report = dict(state='passed', controls=len(rows), refusals=len(bad), rows=rows,
                  binarySha256=hashlib.sha256(binary.read_bytes()).hexdigest(), cadConnectionAttempted=False,
                  geometryExportExecuted=False, fullTopologyVerified=False)
    (evidence / 'framework-audit.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(f'PASS: {len(rows)} independent full-geometry DMT decode controls/{len(bad)} refusals; no CAD/export.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('binary', 'fixtures', 'stage', 'evidence'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    qualify(args.binary.resolve(), args.fixtures.resolve(), args.stage.resolve(), args.evidence.resolve())
