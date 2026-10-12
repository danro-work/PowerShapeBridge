"""Fixed one-use native translation envelopes. Never invokes CAD or retries."""
import hashlib
import json

from entity_references import ReferenceRejected
from entity_sets import token
from working_copy_open import verification_parameters


def validate_verification(args):
    fields = {'executionRequestId', 'executionReceiptSha256', 'candidateDocumentHandle', 'activeDocumentHandle'}
    if set(args) != fields or not all(token(args[k]) for k in fields - {'executionReceiptSha256'}) or not token(args['executionReceiptSha256'], 64):
        raise ReferenceRejected('owned_translation_verification_arguments_invalid')


def verification(args, candidates, documents):
    validate_verification(args)
    translated = {'openRequestId': args['executionRequestId'], 'openReceiptSha256': args['executionReceiptSha256'],
                  'candidateDocumentHandle': args['candidateDocumentHandle'], 'activeDocumentHandle': args['activeDocumentHandle']}
    result = verification_parameters(translated, candidates, documents)
    result['executionRequestId'] = result.pop('openRequestId')
    result['executionReceiptSha256'] = result.pop('openReceiptSha256')
    return result


def execution_receipt(root, result, parameters):
    ident, sha = result.get('operationId'), result.get('planSha256')
    if result.get('state') != 'owned_translation_executed_unverified' or not token(ident) or not token(sha, 64) or result.get('bridgeRequestId') != ident or result.get('geometryMutationAttempted') is not True or result.get('contextChangeAttempted') is not False or result.get('cadSaveAttempted') is not False or result.get('returnCode') != 1 or type(result.get('returnCode')) is not int or result.get('copyFenced') is not True or result.get('recordedJournalState') != 'executed_unverified':
        raise ReferenceRejected('owned_translation_execution_receipt_invalid')
    raw = (root / 'ownedexecutions' / (ident + '.json')).read_bytes()
    if len(raw) > 65536 or hashlib.sha256(raw).hexdigest() != sha:
        raise ReferenceRejected('owned_translation_plan_sha_differs')
    plan = json.loads(raw.decode('utf-8-sig'))
    if plan != result.get('plan') or plan.get('executionEnabled') is not True or plan.get('operationId') != ident or plan.get('operation') != 'translate_owned_solid' or any(plan.get(k) != v for k, v in parameters.items()):
        raise ReferenceRejected('owned_translation_execution_plan_differs')


def checked_verification(result, parameters):
    if result.get('executionOperationId') != parameters['executionRequestId'] or result.get('executionReceiptSha256') != parameters['executionReceiptSha256'] or result.get('recordedJournalState') != 'succeeded_verified' or result.get('copyFenceReleased') is not True or result.get('noCommandReplay') is not True or result.get('geometryMutationAttempted') is not False or result.get('contextChangeAttempted') is not False or result.get('cadSaveAttempted') is not False:
        raise ReferenceRejected('owned_translation_independent_receipt_invalid')
