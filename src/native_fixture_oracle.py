"""Retained Python verifier oracle for actual Framework self-test artifact bytes."""
import argparse
import copy
import hashlib
import json
from pathlib import Path

from document_bindings import DocumentBindingStore
from owned_operation_plans import OwnedOperationStore
from owned_translation import execution_receipt


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
        for path in sorted((root / 'responses').glob('*.json')):
            raw = path.read_bytes()
            try:
                receipt = json.loads(raw.decode('utf-8-sig'))
            except ValueError:
                continue
            if receipt.get('state') not in {'owned_translation_executed_unverified', 'owned_translation_outcome_unknown'}:
                continue
            # Framework tests write their original returned bytes before the IPC
            # layer adds its request identity. Mirror only that queue annotation.
            receipt['bridgeRequestId'] = path.stem
            plan = receipt.get('plan', {})
            parameters = {key: plan.get(key) for key in ('ownedOperationId', 'ownedOperationSha256')}
            variants = [receipt]
            if receipt['state'] == 'owned_translation_executed_unverified':
                for key, value in [('returnCode', True), ('copyFenced', False),
                                   ('planSha256', 'f' * 64), ('recordedJournalState', 'succeeded_verified')]:
                    changed = copy.deepcopy(receipt)
                    changed[key] = value
                    variants.append(changed)
            for variant in variants:
                try:
                    execution_receipt(root, variant, parameters)
                    accepted = True
                except (OSError, ValueError, TypeError, KeyError):
                    accepted = False
                cases.append({'kind': 'execution', 'root': str(root), 'receipt': variant, 'parameters': parameters,
                              'sourceReceiptSha256': hashlib.sha256(raw).hexdigest(), 'accepted': accepted})
    if len(cases) < 5 or not any(case['accepted'] for case in cases):
        raise AssertionError('Framework synthetic fixtures are missing or insufficient.')
    if not any(case['kind'] == 'execution' and case['accepted'] for case in cases):
        raise AssertionError('Actual Framework successful execution receipt coverage is missing.')
    Path(target).parent.mkdir(parents=True, exist_ok=True)
    Path(target).write_text(json.dumps({'stage': str(stage), 'cases': cases}, indent=2, ensure_ascii=False), encoding='utf-8')
    print('Generated Python oracle for %d actual Framework artifact cases.' % len(cases))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--stage', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    generate(args.stage, args.output)
