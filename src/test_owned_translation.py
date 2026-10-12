"""One-use translation routes with frozen files and fake worker; never contacts CAD."""
import copy
import datetime
import json
import threading
import unittest
import uuid

from core_config import general_core
from entity_references import ReferenceRejected
from mcp_server import Server
from operation_plans import append_event, freeze
from owned_translation import execution_receipt, checked_verification
import test_owned_operation_plans as packets
from test_document_handles import listing
from test_entity_references import snapshot


class TranslationRoutes(unittest.TestCase):
    prime = packets.OwnedRoutes.prime
    call = packets.OwnedRoutes.call
    prepare = packets.OwnedRoutes.prepare

    def setUp(self):
        packets.OwnedRoutes.setUp(self)
        self.config['ownedTranslationsExperimental'] = True
        (self.root / 'bridge.config.json').write_text(json.dumps(self.config))
        self.server = Server(self.root)
        self.server.reply = lambda request, result=None, error=None: self.replies.append((result, error))
        self.server.snapshot = self.queue
        self.prime()
        self.packet = self.prepare()

    def queue(self, cancel, operation, args):
        recipe = args['recipeId']
        if recipe not in ('execute_owned_translation', 'verify_owned_translation'):
            return packets.OwnedRoutes.queue(self, cancel, operation, args)
        self.calls.append((operation, copy.deepcopy(args)))
        if self.failure: return self.failure
        params = args['parameters']
        if recipe == 'execute_owned_translation':
            ident = uuid.uuid4().hex
            plan = {**params, 'schemaVersion': 1, 'operationId': ident, 'operation': 'translate_owned_solid', 'executionEnabled': True,
                    'copyId': self.made['ownedOperation']['copyId'], 'ownershipSha256': self.made['ownedOperation']['ownershipSha256']}
            directory = self.root / 'ownedexecutions'; directory.mkdir(exist_ok=True)
            sha = freeze(directory / (ident + '.json'), plan)
            for number, state in enumerate(('prepared', 'execution_claimed', 'command_started', 'command_returned', 'executed_unverified')):
                append_event(self.root, ident, sha, state, {}, number)
            self.execution = {'state': 'owned_translation_executed_unverified', 'operationId': ident, 'bridgeRequestId': ident,
                              'planSha256': sha, 'plan': plan, 'returnCode': 1, 'copyFenced': True, 'recordedJournalState': 'executed_unverified',
                              'geometryMutationAttempted': True, 'contextChangeAttempted': False, 'cadSaveAttempted': False, 'retryAllowed': False}
            return self.execution
        return {'state': 'owned_translation_verified_observed', 'bridgeRequestId': uuid.uuid4().hex,
                'executionOperationId': params['executionRequestId'], 'executionReceiptSha256': params['executionReceiptSha256'],
                'recordedJournalState': 'succeeded_verified', 'copyFenceReleased': True, 'noCommandReplay': True,
                'geometryMutationAttempted': False, 'contextChangeAttempted': False, 'cadSaveAttempted': False}

    def execute(self, handle=None, cancel=None):
        return self.call('execute_owned_translation', {'ownedOperationHandle': handle or self.packet['ownedOperationHandle']}, cancel)

    def proof(self):
        source = snapshot(); source['timestampUtc'] = datetime.datetime.now(datetime.timezone.utc).isoformat(); source['bridgeRequestId'] = uuid.uuid4().hex
        document = self.server.entity_sets.issue_candidates(source, 'b' * 64)['candidateDocumentHandle']
        data = listing(); data['bridgeRequestId'] = uuid.uuid4().hex
        active = self.server.document_handles.issue(data, 'c' * 64)['documents'][0]['documentHandle']
        return {'executionRequestId': self.execution['operationId'], 'executionReceiptSha256': 'd' * 64,
                'candidateDocumentHandle': document, 'activeDocumentHandle': active}

    def test_one_fixed_command_consumes_cad_leases_but_keeps_disk_copy_leases(self):
        packet_id = self.packet['ownedOperationId']; packet_sha = self.packet['ownedOperationSha256']
        result = self.execute()
        self.assertEqual(result['state'], 'owned_translation_executed_unverified')
        self.assertEqual(self.calls[-1], ('recipe.prepare', {'recipeId': 'execute_owned_translation', 'parameters': {'ownedOperationId': packet_id, 'ownedOperationSha256': packet_sha}}))
        for store in (self.server.references.documents, self.server.entity_sets.documents, self.server.document_handles.entries,
                      self.server.operation_plans.plans, self.server.document_bindings.bindings, self.server.owned_operations.packets):
            self.assertFalse(store)
        self.assertTrue(self.server.working_copies.copies)
        before = len(self.calls); self.assertEqual(self.execute()['state'], 'owned_translation_rejected_before_change'); self.assertEqual(len(self.calls), before)

    def test_independent_verification_fixed_proof_consumes_only_document_choice(self):
        self.execute(); args = self.proof()
        expected_source = self.server.entity_sets.document_parameters(args['candidateDocumentHandle'])
        result = self.call('verify_owned_translation', args)
        self.assertEqual(result['state'], 'owned_translation_verified_observed')
        params = self.calls[-1][1]['parameters']
        self.assertEqual(params['executionRequestId'], args['executionRequestId'])
        self.assertEqual(params['executionReceiptSha256'], args['executionReceiptSha256'])
        self.assertEqual(params['inspectionRequestId'], expected_source['inspectionRequestId'])
        self.assertEqual(set(params), {'executionRequestId', 'executionReceiptSha256', 'inspectionRequestId', 'inspectionSha256', 'listingRequestId', 'listingSha256', 'windowNumber', 'modelId', 'expiresUtc'})
        before = len(self.calls)
        self.assertEqual(self.call('verify_owned_translation', args)['state'], 'owned_translation_verification_rejected')
        self.assertEqual(len(self.calls), before)

    def test_foreign_native_ids_read_refs_and_extra_commands_never_queue(self):
        before = len(self.calls)
        self.assertEqual(self.execute('a' * 32)['state'], 'owned_translation_rejected_before_change')
        for args in ({'ownedOperationId': self.packet['ownedOperationId'], 'ownedOperationSha256': self.packet['ownedOperationSha256']},
                     {'ownedOperationHandle': self.packet['ownedOperationHandle'], 'command': 'SAVE'}, {'ownedOperationHandle': '../outside'}):
            self.assertEqual(self.call('execute_owned_translation', args)['code'], -32602)
        self.assertEqual(len(self.calls), before)
        with self.assertRaises(ReferenceRejected): Server(self.root).owned_operations.validation_parameters(self.packet['ownedOperationHandle'])

    def test_cancel_before_submission_preserves_unconsumed_packet(self):
        cancel = threading.Event(); cancel.set(); before = len(self.calls)
        self.assertEqual(self.execute(cancel=cancel)['state'], 'cancelled_before_submission')
        self.assertEqual(len(self.calls), before)
        self.assertIn(self.packet['ownedOperationHandle'], self.server.owned_operations.packets)

    def test_unknown_timeout_and_known_preflight_failure_never_replay(self):
        for state in ('owned_translation_outcome_unknown', 'timeout_outcome_unknown', 'owned_translation_rejected_before_change'):
            self.failure = None; self.prime(); self.packet = self.prepare(); before = len(self.calls)
            self.failure = {'state': state, 'bridgeRequestId': '1' * 32}
            self.assertEqual(self.execute()['state'], state)
            self.assertEqual(len(self.calls), before + 1)
            self.execute(); self.assertEqual(len(self.calls), before + 1)
            self.failure = None

    def test_invalid_success_receipt_is_unknown_after_submission(self):
        original = self.queue
        def changed(cancel, operation, args):
            result = original(cancel, operation, args)
            return {**result, 'planSha256': '0' * 64}
        self.server.snapshot = changed
        result = self.execute()
        self.assertEqual(result['state'], 'owned_translation_outcome_unknown')
        self.assertTrue(result['geometryMutationAttempted'])
        self.assertIn('bridgeRequestId', result)
        self.assertFalse(self.server.owned_operations.packets)

    def test_cancel_after_command_reports_unknown_and_consumes_handle(self):
        cancel = threading.Event(); original = self.queue
        def delayed(c, operation, args):
            result = original(c, operation, args); c.set(); return result
        self.server.snapshot = delayed
        result = self.execute(cancel=cancel)
        self.assertEqual(result['state'], 'cancelled_outcome_unknown')
        self.assertTrue(result['geometryMutationAttempted'])
        self.assertFalse(self.server.owned_operations.packets)

    def test_native_verification_identity_or_fence_release_mismatch_rejected(self):
        self.execute(); original = self.queue
        for key, value in (('executionOperationId', 'a' * 32), ('copyFenceReleased', False), ('geometryMutationAttempted', True)):
            self.server.snapshot = lambda c, op, a, k=key, v=value: {**original(c, op, a), k: v}
            self.assertEqual(self.call('verify_owned_translation', self.proof())['state'], 'owned_translation_verification_rejected')

    def test_catalog_config_gate_and_readonly(self):
        self.assertEqual(len(self.server.tools), 21)
        readonly = Server(self.root, read_only=True); self.assertEqual(len(readonly.tools), 6)
        names = {t['name'] for t in readonly.tools}; self.assertFalse(names & {'execute_owned_translation', 'verify_owned_translation'})
        readonly.reply = self.server.reply
        readonly.call(1, {'name': 'execute_owned_translation', 'arguments': {'ownedOperationHandle': self.packet['ownedOperationHandle']}}, threading.Event())
        self.assertEqual(self.replies[-1][1]['code'], -32602)
        for changes in ({'ownedTranslationsExperimental': 'true'}, {'ownedOperationPlansExperimental': False}, {'coreProfile': None}):
            with self.assertRaises(ValueError): general_core({**self.config, **changes})
        self.config['ownedTranslationsExperimental'] = False
        (self.root / 'bridge.config.json').write_text(json.dumps(self.config))
        self.assertEqual(len(Server(self.root).tools), 19)


if __name__ == '__main__': unittest.main()
