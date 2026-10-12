"""Owned-open routing and unknown outcomes with synthetic worker receipts; no CAD."""
import datetime
import hashlib
import json
import os
from pathlib import Path
import threading
import unittest
import uuid

from core_config import general_core
from entity_references import ReferenceRejected
from mcp_server import Server
from operation_plans import journal_events
from test_entity_references import snapshot
from working_copy_open import prepare_parameters


class OwnedOpenRoutes(unittest.TestCase):
    def setUp(self):
        self.root = Path(__file__).parents[1] / 'work' / ('owned-open-route-' + uuid.uuid4().hex)
        self.root.mkdir(parents=True)
        self.config = json.loads((Path(__file__).parents[1] / 'config/general-working-copy-open.json').read_text())
        (self.root / 'bridge.config.json').write_text(json.dumps(self.config))
        self.server = Server(self.root)
        self.replies, self.calls = [], []
        self.failure = None
        self.server.reply = lambda request, result=None, error=None: self.replies.append((result, error))
        self.server.snapshot = self.queue
        timestamp = datetime.datetime.now(datetime.timezone.utc).isoformat()
        observed = snapshot(); observed.update(timestampUtc=timestamp, bridgeRequestId='a' * 32)
        document = self.server.entity_sets.issue_candidates(observed, 'b' * 64)['candidateDocumentHandle']
        working = self.server.working_copies.issue({'state': 'working_copy_prepared_disk', 'copyId': 'c' * 32,
            'ownershipSha256': 'd' * 64, 'bridgeRequestId': 'c' * 32, 'timestampUtc': timestamp})['workingCopyHandle']
        listing = {'state': 'documents_listed_verified', 'bridgeRequestId': 'e' * 32, 'timestampUtc': timestamp,
                   'documents': [{'modelId': 9, 'windowNumber': 1, 'activationSupported': True}]}
        choice = self.server.document_handles.issue(listing, 'f' * 64)['documents'][0]['documentHandle']
        self.args = {'workingCopyHandle': working, 'candidateDocumentHandle': document, 'activeDocumentHandle': choice}

    def queue(self, cancel, operation, arguments):
        self.calls.append((operation, arguments))
        if self.failure: return self.failure
        parameters = arguments['parameters']
        plan = {**parameters, 'schemaVersion': 1, 'operationId': '1' * 32, 'operation': 'open_working_copy',
                'bridgeVersion': '0.29.0', 'createdUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
                'geometryExecutionEnabled': False, 'workingPath': 'D:/Owned/Working.psmodel'}
        raw = json.dumps(plan).encode()
        directory = self.root / 'copyopenplans'; directory.mkdir(exist_ok=True)
        (directory / ('1' * 32 + '.json')).write_bytes(raw)
        return {'state': 'working_copy_opened_observed', 'operationId': '1' * 32, 'bridgeRequestId': '1' * 32,
                'copyId': 'c' * 32, 'ownershipSha256': 'd' * 64, 'plan': plan, 'planSha256': hashlib.sha256(raw).hexdigest(),
                'contextChangeAttempted': True, 'geometryMutationAttempted': False, 'cadSaveAttempted': False}

    def call(self, args=None, cancel=None):
        self.server.call(1, {'name': 'open_working_copy', 'arguments': self.args if args is None else args}, cancel or threading.Event())
        result, error = self.replies[-1]
        return result['structuredContent'] if result else error

    def test_fixed_native_envelope_consumes_choice_and_revokes_cad_refs(self):
        result = self.call()
        self.assertEqual(result['state'], 'working_copy_opened_observed')
        operation, args = self.calls[0]
        self.assertEqual(operation, 'recipe.prepare')
        self.assertEqual(args['recipeId'], 'open_working_copy')
        self.assertEqual(set(args['parameters']), {'copyId', 'ownershipSha256', 'inspectionRequestId', 'inspectionSha256', 'expiresUtc', 'listingRequestId', 'listingSha256', 'windowNumber', 'modelId'})
        self.assertFalse(self.server.entity_sets.documents)
        self.assertFalse(self.server.document_handles.entries)
        self.server.working_copies.inspect_parameters(self.args['workingCopyHandle'])
        self.assertEqual(self.call()['state'], 'working_copy_open_rejected')
        self.assertEqual(len(self.calls), 1)

    def test_unknown_foreign_handle_never_submits_or_consumes_choice(self):
        result = self.call({**self.args, 'workingCopyHandle': '0' * 32})
        self.assertEqual(result['state'], 'working_copy_open_rejected')
        self.assertFalse(self.calls)
        self.assertTrue(self.server.document_handles.entries)

    def test_native_ids_paths_read_refs_and_extra_commands_rejected(self):
        for args in ({'copyId': 'c' * 32}, {**self.args, 'command': 'SAVE'}, {**self.args, 'path': 'D:/Source.psmodel'},
                     {'workingCopyHandle': self.args['workingCopyHandle'], 'documentReference': 'a' * 32, 'activeDocumentHandle': 'b' * 32}):
            self.assertEqual(self.call(args)['code'], -32602)
        self.assertFalse(self.calls)

    def test_cancel_before_submission_preserves_unconsumed_choice(self):
        cancel = threading.Event(); cancel.set()
        self.assertEqual(self.call(cancel=cancel)['state'], 'cancelled_before_submission')
        self.assertFalse(self.calls)
        self.assertTrue(self.server.document_handles.entries)

    def test_timeout_revokes_copy_without_automatic_replay(self):
        self.failure = {'state': 'timeout_outcome_unknown', 'bridgeRequestId': '2' * 32}
        result = self.call()
        self.assertEqual(result['state'], 'timeout_outcome_unknown')
        self.assertFalse(self.server.working_copies.copies)
        self.assertEqual(self.call()['state'], 'working_copy_open_rejected')
        self.assertEqual(len(self.calls), 1)

    def test_cancel_after_worker_preserves_unknown_native_request(self):
        original = self.queue; cancel = threading.Event()
        def cancelled(event, operation, args):
            result = original(event, operation, args); cancel.set(); return result
        self.server.snapshot = cancelled
        result = self.call(cancel=cancel)
        self.assertEqual(result['state'], 'working_copy_open_outcome_unknown')
        self.assertEqual(result['bridgeRequestId'], '1' * 32)
        self.assertFalse(result['retryAllowed'])

    def test_bad_native_receipt_is_unknown_after_submission_not_known_rejection(self):
        original = self.queue
        self.server.snapshot = lambda event, operation, args: {**original(event, operation, args), 'planSha256': '0' * 64}
        result = self.call()
        self.assertEqual(result['state'], 'working_copy_open_outcome_unknown')
        self.assertEqual(result['bridgeRequestId'], '1' * 32)
        self.assertFalse(self.server.working_copies.copies)

    def test_expired_source_lease_does_not_consume_choice(self):
        self.server.entity_sets.documents[self.args['candidateDocumentHandle']]['expires'] = 0
        self.assertEqual(self.call()['state'], 'working_copy_open_rejected')
        self.assertFalse(self.calls)
        self.assertTrue(self.server.document_handles.entries)

    def test_combined_expiry_is_earliest_listing_or_observation(self):
        record = self.server.document_handles.entries[self.args['activeDocumentHandle']]
        earlier = datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(seconds=40)
        record['parameters']['expiresUtc'] = earlier.isoformat()
        native = prepare_parameters(self.args, self.server.entity_sets, self.server.working_copies, self.server.document_handles)
        self.assertEqual(native['expiresUtc'], earlier.isoformat())

    def test_catalog_readonly_and_flag_dependencies(self):
        self.assertEqual(len(self.server.tools), 17)
        readonly = Server(self.root, read_only=True)
        self.assertEqual(len(readonly.tools), 6)
        self.assertNotIn('open_working_copy', {t['name'] for t in readonly.tools})
        readonly.reply = self.server.reply
        readonly.call(1, {'name': 'open_working_copy', 'arguments': self.args}, threading.Event())
        self.assertEqual(self.replies[-1][1]['code'], -32602)
        for changes in ({'workingCopyOpenExperimental': 'true'}, {'documentBindingsExperimental': False}, {'workingCopiesExperimental': False}, {'coreProfile': None}):
            with self.assertRaises(ValueError): general_core({**self.config, **changes})
        self.config['workingCopyOpenExperimental'] = False
        (self.root / 'bridge.config.json').write_text(json.dumps(self.config))
        self.assertEqual(len(Server(self.root).tools), 15)

    def test_python_reads_actual_native_journal_schema_and_unknown_claim(self):
        stage = Path(os.environ.get('PSBRIDGE_TEST_STAGE', Path(__file__).parents[1] / 'work/P2.29-open'))
        roots = sorted((stage / 'work').glob('open-self-test-*'), key=lambda path: path.stat().st_mtime)
        self.assertTrue(roots, 'Run staged native self-tests before this integration check')
        journals = [journal_events(roots[-1], p.name) for p in (roots[-1] / 'operations').iterdir()]
        self.assertEqual({j['state'] for j in journals}, {'succeeded_verified', 'outcome_unknown'})
        for journal in journals:
            self.assertFalse(journal['retryAllowed'])
            plan = roots[-1] / 'copyopenplans' / (journal['operationId'] + '.json')
            self.assertEqual(journal['planSha256'], hashlib.sha256(plan.read_bytes()).hexdigest())


class OwnedOpenVerificationRoutes(unittest.TestCase):
    def setUp(self):
        OwnedOpenRoutes.setUp(self)
        self.server.working_copies.clear()
        self.args = {'openRequestId': '1' * 32, 'openReceiptSha256': '2' * 64,
                     'candidateDocumentHandle': self.args['candidateDocumentHandle'],
                     'activeDocumentHandle': self.args['activeDocumentHandle']}

    def queue(self, cancel, operation, arguments):
        self.calls.append((operation, arguments))
        if self.failure: return self.failure
        parameters = arguments['parameters']
        return {'state': 'owned_open_verified_observed', 'bridgeRequestId': '3' * 32,
                'copyId': 'c' * 32, 'ownershipSha256': 'd' * 64,
                'openOperationId': parameters['openRequestId'], 'openReceiptSha256': parameters['openReceiptSha256'],
                'expiresUtc': parameters['expiresUtc'], 'timestampUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
                'recordedJournalState': 'succeeded_verified', 'contextChangeAttempted': False,
                'geometryMutationAttempted': False, 'cadSaveAttempted': False}

    def call(self, args=None, cancel=None):
        self.server.call(1, {'name': 'verify_working_copy_open', 'arguments': self.args if args is None else args}, cancel or threading.Event())
        result, error = self.replies[-1]
        return result['structuredContent'] if result else error

    def test_fixed_proof_envelope_reissues_copy_lease_without_open(self):
        result = self.call()
        self.assertEqual(result['state'], 'owned_open_verified_observed')
        operation, arguments = self.calls[0]
        self.assertEqual(operation, 'recipe.prepare')
        self.assertEqual(arguments['recipeId'], 'verify_working_copy_open')
        self.assertEqual(set(arguments['parameters']), {'openRequestId', 'openReceiptSha256', 'inspectionRequestId', 'inspectionSha256', 'expiresUtc', 'listingRequestId', 'listingSha256', 'windowNumber', 'modelId'})
        self.assertEqual(self.server.working_copies.inspect_parameters(result['workingCopyHandle'])['copyId'], 'c' * 32)
        self.assertTrue(self.server.entity_sets.documents)
        self.assertFalse(result['contextChangeAttempted'])
        self.assertEqual(self.call()['state'], 'owned_open_verification_rejected')
        self.assertEqual(len(self.calls), 1)

    def test_arbitrary_copy_path_command_and_bad_receipt_identity_refused(self):
        for args in ({**self.args, 'copyId': 'c' * 32}, {**self.args, 'path': 'D:/Other.psmodel'},
                     {**self.args, 'command': 'OPEN'}, {**self.args, 'openReceiptSha256': 'bad'}):
            self.assertEqual(self.call(args)['code'], -32602)
        self.assertFalse(self.calls)

    def test_foreign_or_expired_candidate_never_consumes_choice(self):
        self.assertEqual(self.call({**self.args, 'candidateDocumentHandle': '0' * 32})['state'], 'owned_open_verification_rejected')
        self.server.entity_sets.documents[self.args['candidateDocumentHandle']]['expires'] = 0
        self.assertEqual(self.call()['state'], 'owned_open_verification_rejected')
        self.assertTrue(self.server.document_handles.entries)
        self.assertFalse(self.calls)

    def test_bad_native_identity_does_not_issue_copy_handle(self):
        original = self.queue
        self.server.snapshot = lambda event, op, args: {**original(event, op, args), 'openOperationId': '0' * 32}
        self.assertEqual(self.call()['state'], 'owned_open_verification_rejected')
        self.assertFalse(self.server.working_copies.copies)

    def test_unknown_receipt_and_cancellation_never_replay_open(self):
        self.failure = {'state': 'timeout_outcome_unknown', 'bridgeRequestId': '3' * 32}
        self.assertEqual(self.call()['state'], 'timeout_outcome_unknown')
        self.assertFalse(self.server.working_copies.copies)
        self.assertEqual(self.calls[0][1]['recipeId'], 'verify_working_copy_open')

    def test_cancel_before_submission_preserves_choice(self):
        event = threading.Event(); event.set()
        self.assertEqual(self.call(cancel=event)['state'], 'cancelled_before_submission')
        self.assertTrue(self.server.document_handles.entries)
        self.assertFalse(self.calls)

    def test_cancel_after_verification_reports_only_journal_uncertainty(self):
        original = self.queue; event = threading.Event()
        def cancelled(cancel, operation, arguments):
            result = original(cancel, operation, arguments); event.set(); return result
        self.server.snapshot = cancelled
        result = self.call(cancel=event)
        self.assertEqual(result['state'], 'cancelled_outcome_unknown')
        self.assertFalse(result['contextChangeAttempted'])
        self.assertFalse(self.server.working_copies.copies)

    def test_readonly_refuses_verification_journal_writer(self):
        readonly = Server(self.root, read_only=True); readonly.reply = self.server.reply
        readonly.call(1, {'name': 'verify_working_copy_open', 'arguments': self.args}, threading.Event())
        self.assertEqual(self.replies[-1][1]['code'], -32602)
        self.assertNotIn('verify_working_copy_open', {tool['name'] for tool in readonly.tools})

    def test_verified_copy_lease_refuses_expiry_and_incomplete_journal(self):
        from working_copies import WorkingCopyStore
        value = self.queue(threading.Event(), 'recipe.prepare', {'parameters': {
            'openRequestId': self.args['openRequestId'], 'openReceiptSha256': self.args['openReceiptSha256'],
            'expiresUtc': (datetime.datetime.now(datetime.timezone.utc) - datetime.timedelta(seconds=1)).isoformat()}})
        with self.assertRaises(ReferenceRejected): WorkingCopyStore().issue_verified_open(value)
        value['expiresUtc'] = (datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(seconds=30)).isoformat()
        value['recordedJournalState'] = 'outcome_unknown'
        with self.assertRaises(ReferenceRejected): WorkingCopyStore().issue_verified_open(value)


if __name__ == '__main__': unittest.main()
