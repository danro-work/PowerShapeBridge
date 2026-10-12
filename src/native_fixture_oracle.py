"""Retained Python verifier oracle for actual Framework self-test artifact bytes."""
import argparse
import hashlib
import json
from pathlib import Path

from document_bindings import DocumentBindingStore
from owned_operation_plans import OwnedOperationStore


def generate(stage, target):
    stage = Path(stage).resolve()
    cases = []
    for root in sorted((stage / 'work').glob('owned-operation-self-test-*')):
        for path in sorted((root / 'documentbindings').glob('*.json')):
            raw = path.read_bytes()
            try:
                value = json.loads(raw.decode('utf-8-sig'))
            except ValueError:
                continue
            receipt = {'bindingId': path.stem, 'bindingSha256': hashlib.sha256(raw).hexdigest(), 'binding': value}
            try:
                DocumentBindingStore(root, None, None).verify_receipt(receipt)
                accepted = True
            except (OSError, ValueError, TypeError, KeyError):
                accepted = False
            cases.append({'kind': 'binding', 'root': str(root), 'receipt': receipt, 'accepted': accepted})
        for path in sorted((root / 'ownedoperations').glob('*.json')):
            raw = path.read_bytes()
            try:
                value = json.loads(raw.decode('utf-8-sig'))
            except ValueError:
                continue
            receipt = {'ownedOperationId': path.stem, 'ownedOperationSha256': hashlib.sha256(raw).hexdigest(),
                       'ownedOperation': value, 'expiresUtc': value.get('expiresUtc'), 'executionEnabled': False,
                       'geometryMutationAttempted': False, 'contextChangeAttempted': False, 'cadSaveAttempted': False}
            try:
                OwnedOperationStore(root, None, None).receipt(receipt)
                accepted = True
            except (OSError, ValueError, TypeError, KeyError):
                accepted = False
            cases.append({'kind': 'packet', 'root': str(root), 'receipt': receipt, 'accepted': accepted})
    if len(cases) < 5 or not any(case['accepted'] for case in cases):
        raise AssertionError('Framework synthetic fixtures are missing or insufficient.')
    Path(target).parent.mkdir(parents=True, exist_ok=True)
    Path(target).write_text(json.dumps({'stage': str(stage), 'cases': cases}, indent=2, ensure_ascii=False), encoding='utf-8')
    print('Generated Python oracle for %d actual Framework artifact cases.' % len(cases))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--stage', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    generate(args.stage, args.output)
