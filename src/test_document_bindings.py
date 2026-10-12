"""Binding leases and fixed native routes with synthetic receipts; no CAD calls."""
import datetime
import hashlib
import json
from pathlib import Path
import threading
import unittest
import uuid

from core_config import general_core
from entity_references import ReferenceRejected
from mcp_server import Server
from test_entity_references import snapshot


class BindingRoutes(unittest.TestCase):
    def setUp(self):
        self.root = Path(__file__).parents[1] / 'work' / ('binding-route-test-' + uuid.uuid4().hex)
        self.root.mkdir(parents=True)
        self.config = json.loads((Path(__file__).parents[1] / 'config/general-document-bindings.json').read_text())
        (self.root / 'bridge.config.json').write_text(json.dumps(self.config))
        self.server = Server(self.root)
        self.calls, self.replies = [], []
        self.failure = None
        self.server.reply = lambda request, result=None, error=None: self.replies.append((result, error))
        self.server.snapshot = self.queue
        self.prime()

    def prime(self):
        observation = snapshot()
        observation.update(timestampUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(), bridgeRequestId='a' * 32)
        self.document = self.server.entity_sets.issue_candidates(observation, 'b' * 64)['candidateDocumentHandle']
        self.working = self.server.working_copies.issue({'state': 'working_copy_prepared_disk',
            'copyId': 'c' * 32, 'ownershipSha256': 'd' * 64, 'bridgeRequestId': 'c' * 32,
            'timestampUtc': datetime.datetime.now(datetime.timezone.utc).isoformat()})['workingCopyHandle']
        self.args = {'candidateDocumentHandle': self.document, 'workingCopyHandle': self.working}

    def queue(self, cancel, operation, arguments):
        self.calls.append((operation, arguments))
        if cancel.is_set(): return {'state': 'cancelled_before_submission'}
        if self.failure: return self.failure
        if arguments['recipeId'] == 'bind_working_document':
            binding = {**arguments['parameters'], 'schemaVersion': 1, 'bindingId': 'f' * 32,
                       'bridgeVersion': '0.28.0', 'timestampUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
                       'observedPropertiesSha256': 'a' * 64, 'windowContextSha256': 'e' * 64, 'executionEnabled': False}
            directory = self.root / 'documentbindings'; directory.mkdir(exist_ok=True)
            raw = json.dumps(binding).encode()
            (directory / ('f' * 32 + '.json')).write_bytes(raw)
            self.made = {'state': 'working_document_bound_observed', 'bindingId': 'f' * 32,
                         'bindingSha256': hashlib.sha256(raw).hexdigest(), 'binding': binding,
                         'bridgeRequestId': 'f' * 32, 'cadMutationAttempted': False, 'contextChangeAttempted': False,
                         'executionEnabled': False, 'sameContextObservationReads': 2}
            return self.made
        return {**self.made, 'state': 'working_document_binding_validated_observed', 'bridgeRequestId': '0' * 32}

    def call(self, name, args=None, cancel=None):
        self.server.call(1, {'name': name, 'arguments': args or {}}, cancel or threading.Event())
        result, error = self.replies[-1]
        return result.get('structuredContent') if result else error

    def bind(self):
        return self.call('bind_working_document', self.args)

    def test_fixed_envelopes_and_native_file_receipt(self):
        bound = self.bind()
        self.assertEqual(bound['state'], 'working_document_bound_observed')
        expected = self.server.document_bindings.prepare_parameters(self.document, self.working)
        self.assertEqual(set(expected), {'copyId', 'ownershipSha256', 'inspectionRequestId', 'inspectionSha256', 'expiresUtc'})
        self.assertEqual(self.calls[-1], ('recipe.prepare', {'recipeId': 'bind_working_document', 'parameters': expected}))
        result = self.call('validate_working_document_binding', {'workingDocumentBindingHandle': bound['workingDocumentBindingHandle']})
        self.assertEqual(result['state'], 'working_document_binding_validated_observed')
        self.assertEqual(self.calls[-1][1]['parameters'], {'bindingId': 'f' * 32, 'bindingSha256': bound['bindingSha256']})
        self.assertFalse(result['executionEnabled'])

    def test_foreign_handles_and_old_read_references_do_not_queue(self):
        for args in ({'candidateDocumentHandle': 'a' * 32, 'workingCopyHandle': self.working},
                     {'candidateDocumentHandle': self.document, 'workingCopyHandle': 'a' * 32}):
            self.assertEqual(self.call('bind_working_document', args)['state'], 'working_document_binding_rejected')
        for args in ({'documentReference': self.document, 'workingCopyHandle': self.working},
                     {**self.args, 'command': 'SAVE'}, {'bindingId': 'a' * 32, 'bindingSha256': 'b' * 64}):
            self.assertEqual(self.call('bind_working_document', args)['code'], -32602)
        self.assertFalse(self.calls)

    def test_other_connection_expiry_and_cache_limit(self):
        bound = self.bind(); handle = bound['workingDocumentBindingHandle']
        with self.assertRaises(ReferenceRejected): Server(self.root).document_bindings.validation_parameters(handle)
        self.server.document_bindings.MAX_BINDINGS = 1
        self.bind()
        with self.assertRaises(ReferenceRejected): self.server.document_bindings.validation_parameters(handle)
        current = self.bind()['workingDocumentBindingHandle']
        self.server.document_bindings.bindings[current]['expires'] = 0
        with self.assertRaises(ReferenceRejected): self.server.document_bindings.validation_parameters(current)

    def test_source_leases_revoked_before_validation_no_queue(self):
        for store in ('entity_sets', 'working_copies'):
            self.prime(); bound = self.bind(); before = len(self.calls)
            getattr(self.server, store).clear()
            result = self.call('validate_working_document_binding', {'workingDocumentBindingHandle': bound['workingDocumentBindingHandle']})
            self.assertEqual(result['state'], 'working_document_binding_rejected')
            self.assertEqual(len(self.calls), before)

    def test_observed_context_change_revokes_binding(self):
        bound = self.bind(); changed = snapshot(); changed['model']['id'] += 1
        self.server.entity_sets.observe(changed)
        before = len(self.calls)
        self.assertEqual(self.call('validate_working_document_binding', {'workingDocumentBindingHandle': bound['workingDocumentBindingHandle']})['state'], 'working_document_binding_rejected')
        self.assertEqual(len(self.calls), before)

    def test_artifact_tamper_and_wrong_native_validation_identity(self):
        bound = self.bind(); handle = bound['workingDocumentBindingHandle']
        self.failure = {**self.made, 'state': 'working_document_binding_validated_observed', 'bindingId': 'e' * 32}
        self.assertEqual(self.call('validate_working_document_binding', {'workingDocumentBindingHandle': handle})['state'], 'working_document_binding_rejected')
        self.failure = None; bound = self.bind()
        (self.root / 'documentbindings' / ('f' * 32 + '.json')).write_text('{}')
        self.assertEqual(self.call('validate_working_document_binding', {'workingDocumentBindingHandle': bound['workingDocumentBindingHandle']})['state'], 'working_document_binding_rejected')

    def test_prepare_receipt_mismatch_never_issues_handle(self):
        parameters = self.server.document_bindings.prepare_parameters(self.document, self.working)
        made = self.queue(threading.Event(), 'recipe.prepare', {'recipeId': 'bind_working_document', 'parameters': parameters})
        for changes in ({'bindingSha256': 'a' * 64}, {'bridgeRequestId': 'e' * 32}, {'binding': {}}):
            with self.assertRaises((ReferenceRejected, OSError)):
                self.server.document_bindings.issue({**made, **changes}, parameters, self.document, self.working)

    def test_source_lease_lost_while_native_validation_runs_rejects_response(self):
        bound = self.bind()
        original_queue = self.queue
        def delayed(cancel, operation, arguments):
            result = original_queue(cancel, operation, arguments)
            self.server.working_copies.clear()
            return result
        self.server.snapshot = delayed
        result = self.call('validate_working_document_binding', {'workingDocumentBindingHandle': bound['workingDocumentBindingHandle']})
        self.assertEqual(result['state'], 'working_document_binding_rejected')
        self.assertEqual(len(self.calls), 2)
        self.assertFalse(self.server.document_bindings.bindings)

    def test_failure_timeout_cancel_revoke_and_never_retry(self):
        for state in ('working_document_binding_rejected', 'timeout_outcome_unknown', 'cancelled_outcome_unknown'):
            self.failure = None; self.prime(); bound = self.bind()
            self.failure = {'state': state, 'bridgeRequestId': '1' * 32}
            result = self.call('validate_working_document_binding', {'workingDocumentBindingHandle': bound['workingDocumentBindingHandle']})
            self.assertEqual(result['state'], state)
            before = len(self.calls)
            self.call('validate_working_document_binding', {'workingDocumentBindingHandle': bound['workingDocumentBindingHandle']})
            self.assertEqual(len(self.calls), before)

    def test_cancel_before_submission_has_no_artifact(self):
        cancel = threading.Event(); cancel.set()
        result = self.call('bind_working_document', self.args, cancel)
        self.assertEqual(result['state'], 'cancelled_before_submission')
        self.assertFalse((self.root / 'documentbindings').exists())

    def test_catalog_gate_and_readonly(self):
        self.assertEqual(len(self.server.tools), 15)
        readonly = Server(self.root, read_only=True)
        self.assertEqual(len(readonly.tools), 6)
        self.assertFalse({t['name'] for t in readonly.tools} & {'bind_working_document', 'validate_working_document_binding'})
        readonly.reply = self.server.reply
        readonly.call(1, {'name': 'bind_working_document', 'arguments': self.args}, threading.Event())
        self.assertEqual(self.replies[-1][1]['code'], -32602)
        for changes in ({'documentBindingsExperimental': 'true'}, {'workingCopiesExperimental': False}, {'entitySetsExperimental': False}, {'coreProfile': None}):
            with self.assertRaises(ValueError): general_core({**self.config, **changes})
        self.config['documentBindingsExperimental'] = False
        (self.root / 'bridge.config.json').write_text(json.dumps(self.config))
        self.assertEqual(len(Server(self.root).tools), 13)


if __name__ == '__main__': unittest.main()
