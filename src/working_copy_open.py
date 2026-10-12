"""Fixed owned-open envelopes and receipt checks. No CAD calls or automatic replay."""
import datetime
import hashlib
import json

from entity_references import ReferenceRejected
from entity_sets import token


def validate_arguments(args):
    if not isinstance(args, dict) or set(args) != {'workingCopyHandle', 'candidateDocumentHandle', 'activeDocumentHandle'} or not all(token(value) for value in args.values()):
        raise ReferenceRejected('owned_open_arguments_invalid')


def prepare_parameters(args, candidates, copies, documents):
    validate_arguments(args)
    candidate = candidates.document_parameters(args['candidateDocumentHandle'])
    owned = copies.inspect_parameters(args['workingCopyHandle'])
    choice = documents.consume(args['activeDocumentHandle'])
    expiry = min(datetime.datetime.fromisoformat(candidate['expiresUtc'].replace('Z', '+00:00')),
                 datetime.datetime.fromisoformat(choice['expiresUtc'].replace('Z', '+00:00')))
    return {**candidate, **owned, **{k: choice[k] for k in ('listingRequestId', 'listingSha256', 'windowNumber', 'modelId')}, 'expiresUtc': expiry.isoformat()}


def verify_receipt(root, result, parameters):
    operation = result.get('operationId')
    if result.get('state') != 'working_copy_opened_observed' or not token(operation) or result.get('bridgeRequestId') != operation or not token(result.get('planSha256'), 64) or result.get('copyId') != parameters['copyId'] or result.get('ownershipSha256') != parameters['ownershipSha256'] or result.get('contextChangeAttempted') is not True or result.get('geometryMutationAttempted') is not False or result.get('cadSaveAttempted') is not False:
        raise ReferenceRejected('owned_open_native_receipt_invalid')
    raw = (root / 'copyopenplans' / (operation + '.json')).read_bytes()
    if len(raw) > 65536 or hashlib.sha256(raw).hexdigest() != result['planSha256']:
        raise ReferenceRejected('owned_open_plan_sha_differs')
    plan = json.loads(raw.decode('utf-8-sig'))
    if plan != result.get('plan') or any(plan.get(k) != value for k, value in parameters.items()) or plan.get('operation') != 'open_working_copy' or plan.get('geometryExecutionEnabled') is not False or plan.get('operationId') != operation:
        raise ReferenceRejected('owned_open_plan_identity_differs')


def validate_verification_arguments(args):
    if not isinstance(args, dict) or set(args) != {'openRequestId', 'openReceiptSha256', 'candidateDocumentHandle', 'activeDocumentHandle'} or not all(token(args[k]) for k in ('openRequestId', 'candidateDocumentHandle', 'activeDocumentHandle')) or not token(args['openReceiptSha256'], 64):
        raise ReferenceRejected('owned_open_verification_arguments_invalid')


def verification_parameters(args, candidates, documents):
    validate_verification_arguments(args)
    candidate = candidates.document_parameters(args['candidateDocumentHandle'])
    choice = documents.consume(args['activeDocumentHandle'])
    expiry = min(datetime.datetime.fromisoformat(candidate['expiresUtc'].replace('Z', '+00:00')),
                 datetime.datetime.fromisoformat(choice['expiresUtc'].replace('Z', '+00:00')))
    return {**candidate, **{k: choice[k] for k in ('listingRequestId', 'listingSha256', 'windowNumber', 'modelId')},
            'expiresUtc': expiry.isoformat(), 'openRequestId': args['openRequestId'], 'openReceiptSha256': args['openReceiptSha256']}
