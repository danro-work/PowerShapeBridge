"""Independent audit of getter-only DGK diagnostics and unchanged original proofs."""
import argparse
import json
from datetime import datetime
from pathlib import Path

from audit_owned_dgk_targets import read, sha

CONTROLS = {'control', 'mixed', 'all_companions', 'historical_expiry'}
FAULTS = CONTROLS | {'receipt_sha', 'target_sha', 'summary_sha', 'step_sha', 'fence_sha', 'claim',
                    'output_bytes', 'missing_output', 'replaced_output', 'stray_output', 'old_inspection',
                    'equal_inspection', 'old_listing', 'future_listing', 'expired_reads', 'settings',
                    'mapping', 'selected_keys', 'busy', 'late_context', 'retained_document',
                    'wrong_window', 'foreign_model', 'unknown_export'}


def audit(stage):
    assert (stage / '.owned-dgk-verification-fixture').is_file()
    report = read(stage / 'reports/owned-dgk-verification-tests.json')
    rows = report['rows']
    assert report['state'] == 'passed' and report['cadConnectionAttempted'] is False
    assert len(rows) == len(FAULTS) and {row['fault'] for row in rows} == FAULTS
    count = 0
    for row in rows:
        fault = row['fault']
        root = Path(row['root']).resolve()
        assert root.is_relative_to(stage / 'work')
        inventory = {Path(path).resolve(): digest for path, digest in row['artifactHashes'].items()}
        assert set(inventory) == {path.resolve() for path in root.rglob('*') if path.is_file()}
        assert all(path.is_relative_to(root) and sha(path) == digest for path, digest in inventory.items())
        assert all(sha(Path(path)) == digest for path, digest in row['protectedHashes'].items())
        count += len(inventory)
        result = row['result']
        assert result['state'] == ('observed_dgk_bundle_context_match_unqualified' if fault in CONTROLS else 'owned_dgk_export_verification_rejected')
        for flag in ('exportAttempted', 'contextChangeAttempted', 'geometryMutationAttempted', 'cadSaveAttempted',
                     'geometryVerified', 'nativeProviderQualified', 'copyFenceReleased', 'retryAllowed'):
            assert result[flag] is False
        assert row['cadConnectionAttempted'] is False
        diagnostic_path = root / 'dgkexportverifications' / (row['verificationId'] + '.json')
        if fault not in CONTROLS:
            assert result['error'] and not diagnostic_path.exists()
            continue
        assert row['verificationReads'] == 4
        assert result['recordedJournalState'] == 'executed_unverified'
        assert sha(diagnostic_path) == result['diagnosticSha256']
        diagnostic = read(diagnostic_path)
        assert diagnostic['verificationId'] == row['verificationId'] and diagnostic['executionRequestId'] == row['executionId']
        assert diagnostic['geometryVerified'] is False and diagnostic['nativeProviderQualified'] is False
        assert diagnostic['copyFenceReleased'] is False
        receipt = read(root / 'responses' / (row['executionId'] + '.json'))
        assert sha(root / 'responses' / (row['executionId'] + '.json')) == diagnostic['executionReceiptSha256']
        journal_paths = sorted((root / 'operations' / row['executionId']).glob('*.json'))
        journal = [read(path) for path in journal_paths]
        assert [item['state'] for item in journal] == ['prepared', 'execution_claimed', 'command_started', 'command_returned', 'executed_unverified']
        returned = datetime.fromisoformat(journal[-2]['timestampUtc'])
        args = row['parameters']
        inspection_path = root / 'responses' / (args['inspectionRequestId'] + '.json')
        listing_path = root / 'responses' / (args['listingRequestId'] + '.json')
        assert sha(inspection_path) == diagnostic['inspectionSha256'] and sha(listing_path) == diagnostic['listingSha256']
        inspection, listing = read(inspection_path), read(listing_path)
        assert datetime.fromisoformat(inspection['timestampUtc']) > returned
        assert datetime.fromisoformat(listing['timestampUtc']) > returned
        assert inspection['selectionBefore'] == inspection['selectionAfter'] == []
        assert inspection['contextBefore'] == inspection['contextAfter'] == [0, 0]
        plan = read(root / 'dgkexportexecutions' / (row['executionId'] + '.json'))
        assert sha(root / 'dgkexportexecutions' / (row['executionId'] + '.json')) == diagnostic['planSha256']
        fence_path = root / 'workingcopies' / plan['copyId'] / 'geometry-export-fence.json'
        assert fence_path.exists() and sha(fence_path) == receipt['exportFenceSha256']
        assert read(fence_path)['operationId'] == row['executionId']
        target_path = root / 'dgkexports' / row['targetId'] / 'target.json'
        target = read(target_path)
        if fault == 'historical_expiry':
            assert datetime.fromisoformat(target['expiresUtc']) < datetime.fromisoformat(inspection['timestampUtc'])
        bundle = diagnostic['bundle']
        directory = Path(bundle['directory'])
        assert directory == root / 'dgkexports' / row['targetId'] / 'artifacts'
        assert {path.name for path in directory.iterdir()} == {item['name'] for item in bundle['artifacts']}
        for item in bundle['artifacts']:
            path = directory / item['name']
            assert sha(path) == item['sha256'] and path.stat().st_size == item['length']
            assert path.stat().st_ino == int(item['fileIdentity']['fileIndex'], 16)
        assert bundle['cadGeometryVerified'] is False and bundle['companionExecutionAttempted'] is False
        assert len(bundle['artifacts']) == (3 if fault == 'all_companions' else 1)
        previous = None
        for sequence, (path, event) in enumerate(zip(journal_paths, journal)):
            assert event['sequence'] == sequence and event['previousSha256'] == previous
            previous = sha(path)
    return {'state': 'passed', 'cases': len(rows), 'controls': len(CONTROLS),
            'artifactHashesChecked': count, 'cadConnectionAttempted': False,
            'journalAdvanced': False, 'copyFenceReleased': False}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--stage', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.stage.resolve())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(f"PASS: {result['cases']} DGK diagnostics / {result['artifactHashesChecked']} hashes independently audited; no CAD/fence release.")
