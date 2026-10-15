"""Independent audit of disabled DGK reservations and synthetic guard refusals."""
import argparse
import hashlib
import json
from datetime import datetime
from pathlib import Path

FAULTS = {'primary', 'curve', 'mixed', 'all_companions', 'working_byte_capture', 'caller_mutation',
          'wrong_binding_sha', 'wrong_set_sha', 'extra_path', 'world_required', 'settings_claim',
          'unknown_variant', 'bad_triangle_suffix', 'duplicate_triangle', 'features_missing',
          'features_on_geometry', 'optional_only_features', 'feature_path', 'feature_wrong_role',
          'numeric_required', 'expired', 'selection', 'unknown_fence', 'export_fence',
          'checkpoint_writable', 'reserved', 'existing_folder', 'held_writer', 'held_writer_truncate', 'held_writer_mapped',
          'output_file', 'output_directory', 'stray_file', 'claimed', 'late_fence', 'late_working',
          'target_path_tamper', 'manifest_tamper', 'expiry_tamper', 'future_prepared_tamper',
          'parameters_tamper', 'directory_replaced', 'bundle_replaced', 'target_tamper'}
CONTROLS = {'primary', 'curve', 'mixed', 'all_companions', 'working_byte_capture', 'caller_mutation', 'held_writer', 'held_writer_truncate', 'held_writer_mapped'}
PREPARED = CONTROLS | {'output_file', 'output_directory', 'stray_file', 'claimed', 'late_fence',
                       'late_working', 'target_path_tamper', 'manifest_tamper', 'expiry_tamper',
                       'future_prepared_tamper', 'parameters_tamper', 'directory_replaced',
                       'bundle_replaced', 'target_tamper'}
TAMPER = {'target_path_tamper', 'manifest_tamper', 'expiry_tamper', 'future_prepared_tamper',
          'parameters_tamper', 'target_tamper'}


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def canonical_sha(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False).encode()).hexdigest()


def audit(stage):
    assert (stage / '.owned-dgk-fixture').is_file()
    report = read(stage / 'reports/owned-dgk-target-tests.json')
    rows = report['rows']
    assert report['state'] == 'passed' and report['cadConnectionAttempted'] is False
    assert len(rows) == len(FAULTS) and {row['fault'] for row in rows} == FAULTS
    byte_rows = report['workingBytePinCases']
    assert len(byte_rows) == 14 and {row['case'] for row in byte_rows} == {
        'existing_writer_read', 'overwrite_blocked', 'append_blocked', 'truncate_detected_by_sha',
        'rename_blocked', 'second_shared_reader', 'mapped_write_detected_by_sha', 'released_write_allowed', 'exclusive_lock_refused',
        'cfb_reserved_lock_compatible', 'cfb_data_overwrite_blocked', 'cfb_supported_last_byte_blocked', 'cfb_reserved_lock_preserved', 'empty_file_refused'}
    for row in byte_rows:
        assert row['state'] == 'passed' and row['cadConnectionAttempted'] is False
        path = Path(row['path']).resolve()
        assert path.is_relative_to(stage / 'work') and sha(path) == row['sha256']
    count = 0
    for row in rows:
        fault = row['fault']
        assert row['workingChangeRejected'] is (fault in {'held_writer_truncate', 'held_writer_mapped'})
        root = Path(row['root']).resolve()
        assert root.is_relative_to(stage / 'work')
        inventory = {Path(path).resolve(): digest for path, digest in row['artifactHashes'].items()}
        assert set(inventory) == {path.resolve() for path in root.rglob('*') if path.is_file()}
        assert all(path.is_relative_to(root) and sha(path) == digest for path, digest in inventory.items())
        count += len(inventory)
        for path, digest in row['protectedHashes'].items():
            path = Path(path).resolve()
            assert path.is_relative_to(root)
            if not (fault == 'late_working' and path.name.startswith('Working_')):
                assert sha(path) == digest
        result = row['result']
        assert result['state'] == ('owned_dgk_target_prepared_files' if fault in PREPARED else 'owned_dgk_target_rejected')
        assert row['leaseAccepted'] == (fault in CONTROLS) if fault in PREPARED else row['leaseAccepted'] is None
        for flag in ('executionEnabled', 'exportAttempted', 'geometryMutationAttempted', 'contextChangeAttempted',
                     'cadSaveAttempted', 'powerShapeConnectionAttempted', 'retryAllowed', 'exportSettingsQualified', 'nativeProviderQualified'):
            assert result[flag] is False
        assert row['cadConnectionAttempted'] is False
        if fault not in PREPARED:
            assert result['error']
            continue
        plan = result['target']
        folder = root / 'dgkexports' / row['targetId']
        artifacts = folder / 'artifacts'
        stem = 'Geometry_' + row['targetId']
        assert plan['operation'] == 'export_owned_dgk' and plan['format'] == 'dgk'
        assert plan['targetPath'] == str(artifacts / (stem + '.dgk')) and plan['bundleDirectory'] == str(artifacts)
        assert plan['parameters'] == row['parameters']['parameters']
        assert plan['parametersSha256'] == canonical_sha(plan['parameters'])
        assert plan['parameters']['coordinateFrame'] == 'world' and plan['parameters']['settingsQualification'] == 'planned_unqualified'
        expected = [{'name': stem + '.dgk', 'role': 'geometry_dgk', 'required': True, 'expectedSha256': ''}]
        for item in plan['parameters']['triangleCompanions']:
            expected.append({'name': stem + item['suffix'], 'role': 'triangles_dmt', 'required': item['required'], 'expectedSha256': ''})
        for item in plan['parameters']['featureCompanions']:
            expected.append(dict(item, expectedSha256=''))
        assert plan['artifactManifest'] == expected
        reservation = read(Path(str(folder) + '.reservation.json'))
        assert reservation['parametersSha256'] == plan['parametersSha256'] and reservation['format'] == 'dgk'
        assert reservation['targetId'] == row['targetId'] and reservation['automaticReplayForbidden'] is True
        binding_path = root / 'documentbindings' / (plan['bindingId'] + '.json')
        set_path = root / 'entitysets' / (plan['entitySetId'] + '.json')
        assert sha(binding_path) == plan['bindingSha256'] and sha(set_path) == plan['entitySetSha256']
        binding, entity_set = read(binding_path), read(set_path)
        assert plan['entityKeys'] == entity_set['entityKeys'] and all(key[0] in (40, 21, 59) for key in plan['entityKeys'])
        assert datetime.fromisoformat(plan['expiresUtc']) == min(datetime.fromisoformat(binding['expiresUtc']), datetime.fromisoformat(entity_set['expiresUtc']))
        observation_path = root / 'responses' / (plan['inspectionRequestId'] + '.json')
        assert sha(observation_path) == plan['inspectionSha256']
        observation = read(observation_path)
        assert observation['selectionBefore'] == observation['selectionAfter'] == []
        assert observation['contextBefore'] == observation['contextAfter'] == [0, 0]
        owner_path = root / 'workingcopies' / plan['copyId'] / 'ownership.json'
        assert sha(owner_path) == plan['ownershipSha256']
        owner = read(owner_path)
        assert owner['working']['path'] == plan['workingPath'] and owner['checkpoint']['path'] == plan['checkpointPath']
        assert sha(Path(plan['checkpointPath'])) == plan['checkpointSha256']
        if fault != 'late_working':
            assert sha(Path(plan['workingPath'])) == plan['workingSha256']
        if fault not in TAMPER:
            assert sha(folder / 'target.json') == result['targetSha256']
        if fault in CONTROLS:
            assert list(artifacts.iterdir()) == []
            assert read(folder / 'target.json')['parameters'] == row['parameters']['parameters']
        previous_journals = {Path(path).resolve() for path in row['protectedHashes']
                             if Path(path).resolve().is_relative_to(root / 'operations')}
        assert {path.resolve() for path in (root / 'operations').rglob('*') if path.is_file()} == previous_journals, 'Preparation added a mutation journal'
    return {'state': 'passed', 'cases': len(rows), 'leaseControls': len(CONTROLS), 'workingBytePinCases': len(byte_rows),
            'artifactHashesChecked': count, 'cadConnectionAttempted': False, 'geometryExportExecuted': False}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--stage', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.stage.resolve())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(f"PASS: {result['cases']} DGK targets / {result['artifactHashesChecked']} hashes independently audited; no CAD/export.")
