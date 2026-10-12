"""Leases for passive native owned-document bindings. No CAD commands or executor."""
import copy
import datetime
import hashlib
import json
from pathlib import Path
import time
import uuid

from entity_references import ReferenceRejected
from entity_sets import token


class DocumentBindingStore:
    MAX_BINDINGS = 128

    def __init__(self, root, candidates, copies, clock=time.monotonic):
        self.root, self.candidates, self.copies = Path(root), candidates, copies
        self.clock, self.bindings = clock, {}

    def clear(self):
        self.bindings.clear()

    def prepare_parameters(self, document, working_copy):
        return {**self.candidates.document_parameters(document), **self.copies.inspect_parameters(working_copy)}

    def verify_receipt(self, result, parameters=None):
        binding_id, sha = result.get('bindingId'), result.get('bindingSha256')
        if not token(binding_id) or not token(sha, 64):
            raise ReferenceRejected('document_binding_native_identity_invalid')
        path = self.root / 'documentbindings' / (binding_id + '.json')
        raw = path.read_bytes()
        if len(raw) > 65536 or hashlib.sha256(raw).hexdigest() != sha:
            raise ReferenceRejected('document_binding_artifact_sha_differs')
        plan = json.loads(raw.decode('utf-8-sig'))
        fields = {'copyId', 'ownershipSha256', 'inspectionRequestId', 'inspectionSha256', 'expiresUtc',
                  'schemaVersion', 'bindingId', 'bridgeVersion', 'timestampUtc', 'observedPropertiesSha256',
                  'windowContextSha256', 'executionEnabled'}
        if not isinstance(plan, dict) or set(plan) != fields or type(plan.get('schemaVersion')) is not int or plan.get('schemaVersion') != 1 or plan.get('executionEnabled') is not False or plan.get('bindingId') != binding_id or plan != result.get('binding'):
            raise ReferenceRejected('document_binding_native_receipt_invalid')
        if not token(plan.get('observedPropertiesSha256'), 64) or not token(plan.get('windowContextSha256'), 64):
            raise ReferenceRejected('document_binding_observation_invalid')
        if parameters is not None and any(plan.get(k) != v for k, v in parameters.items()):
            raise ReferenceRejected('document_binding_source_identity_differs')
        return plan

    def issue(self, result, parameters, document, working_copy):
        if result.get('state') != 'working_document_bound_observed' or result.get('bridgeRequestId') != result.get('bindingId'):
            raise ReferenceRejected('document_binding_native_receipt_invalid')
        # Check dependencies again after native processing; expiry never extends their life.
        if self.prepare_parameters(document, working_copy) != parameters:
            raise ReferenceRejected('document_binding_source_identity_differs')
        plan = self.verify_receipt(result, parameters)
        try:
            expiry = datetime.datetime.fromisoformat(plan['expiresUtc'].replace('Z', '+00:00'))
            seconds = (expiry - datetime.datetime.now(datetime.timezone.utc)).total_seconds()
            if expiry.tzinfo is None or not 0 < seconds <= 300:
                raise ValueError()
        except (ValueError, TypeError, AttributeError):
            raise ReferenceRejected('document_binding_time_invalid_or_expired')
        seconds = min(seconds, self.candidates.documents[document]['expires'] - self.candidates.clock(),
                      self.copies.copies[working_copy]['expires'] - self.copies.clock())
        if seconds <= 0:
            raise ReferenceRejected('document_binding_source_expired')
        if len(self.bindings) >= self.MAX_BINDINGS:
            self.clear()
        handle = uuid.uuid4().hex
        self.bindings[handle] = {'expires': self.clock() + seconds, 'document': document,
                                 'copy': working_copy, 'source': copy.deepcopy(parameters),
                                 'parameters': {'bindingId': result['bindingId'], 'bindingSha256': result['bindingSha256']}}
        return {**result, 'workingDocumentBindingHandle': handle, 'bindingLifetimeSeconds': int(seconds)}

    def validation_parameters(self, handle):
        record = self.bindings.get(handle)
        if record is None or record['expires'] <= self.clock():
            self.bindings.pop(handle, None)
            raise ReferenceRejected('document_binding_handle_unknown_or_expired')
        try:
            if self.prepare_parameters(record['document'], record['copy']) != record['source']:
                raise ReferenceRejected('document_binding_source_identity_differs')
        except ReferenceRejected:
            self.clear()
            raise
        return copy.deepcopy(record['parameters'])

    def checked_validation(self, result, parameters):
        if result.get('bindingId') != parameters['bindingId'] or result.get('bindingSha256') != parameters['bindingSha256']:
            raise ReferenceRejected('document_binding_native_identity_differs')
        self.verify_receipt(result)
