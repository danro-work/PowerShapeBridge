"""Coupled packet public routes with real frozen artifacts; fake queue, no CAD."""
import copy
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
from operation_plans import OBSERVED_FIELDS, append_event, freeze
from test_entity_references import snapshot


class OwnedRoutes(unittest.TestCase):
    def setUp(self):
        self.root = Path(__file__).parents[1] / 'work' / ('owned-operation-route-' + uuid.uuid4().hex)
        self.root.mkdir(parents=True)
        self.config = json.loads((Path(__file__).parents[1] / 'config/general-owned-operation-plans.json').read_text())
        (self.root / 'bridge.config.json').write_text(json.dumps(self.config))
        for folder in ('responses', 'entitysets', 'documentbindings', 'ownedoperations'):
            (self.root / folder).mkdir()
        self.server = Server(self.root)
        self.calls, self.replies, self.failure = [], [], None
        self.server.reply = lambda request, result=None, error=None: self.replies.append((result, error))
        self.server.snapshot = self.queue
        self.prime()

    def prime(self):
        source = dict.fromkeys(OBSERVED_FIELDS)
        source.update(snapshot(), productVersion='23.2.2', modelScope='active_document_read_only',
                      timestampUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                      contextAfter=[0, 0], selectionBefore=[], selectionAfter=[], levelsContainingEntities=[], bridgeRequestId=uuid.uuid4().hex)
        sha = freeze(self.root / 'responses' / (source['bridgeRequestId'] + '.json'), source)
        document = self.server.entity_sets.issue_candidates(source, sha)['candidateDocumentHandle']
        copy_id = uuid.uuid4().hex
        working = self.server.working_copies.issue({'state': 'working_copy_prepared_disk', 'copyId': copy_id,
                    'ownershipSha256': 'd' * 64, 'bridgeRequestId': copy_id, 'timestampUtc': source['timestampUtc']})['workingCopyHandle']
        binding_parameters = self.server.document_bindings.prepare_parameters(document, working)
        binding_id = uuid.uuid4().hex
        binding = {**binding_parameters, 'schemaVersion': 1, 'bindingId': binding_id, 'bridgeVersion': '0.30.0',
                   'timestampUtc': source['timestampUtc'], 'observedPropertiesSha256': 'e' * 64,
                   'windowContextSha256': 'f' * 64, 'executionEnabled': False}
        binding_sha = freeze(self.root / 'documentbindings' / (binding_id + '.json'), binding)
        result = {'state': 'working_document_bound_observed', 'bindingId': binding_id, 'bridgeRequestId': binding_id,
                  'bindingSha256': binding_sha, 'binding': binding}
        self.binding = self.server.document_bindings.issue(result, binding_parameters, document, working)['workingDocumentBindingHandle']
        set_id = uuid.uuid4().hex
        entity_set = {k: binding_parameters[k] for k in ('inspectionRequestId', 'inspectionSha256', 'expiresUtc')}
        entity_set['entityKeys'] = [[40, 5]]
        set_sha = freeze(self.root / 'entitysets' / (set_id + '.json'), entity_set)
        set_handle = self.server.entity_sets.issue_set({'state': 'entity_set_prepared_observed', 'entitySetId': set_id,
                    'entitySetSha256': set_sha, 'expiresUtc': entity_set['expiresUtc']})['entitySetHandle']
        plan = self.server.operation_plans.prepare({'operation': 'translate_entities', 'entitySetHandles': [set_handle],
            'parameters': {'vector': [1, 0, 0], 'coordinateFrame': 'world', 'lengthUnits': 'document_raw', 'mode': 'move'}})
        self.plan = plan['operationPlanHandle']
        self.args = {'operationPlanHandle': self.plan, 'workingDocumentBindingHandle': self.binding}
        self.proofs = [self.root / 'responses' / (source['bridgeRequestId'] + '.json'), self.root / 'entitysets' / (set_id + '.json'),
                       self.root / 'documentbindings' / (binding_id + '.json'), self.root / 'operationplans' / (plan['operationId'] + '.json'),
                       self.root / 'operations' / plan['operationId'] / '000000.json']

    def queue(self, cancel, operation, args):
        self.calls.append((operation, copy.deepcopy(args)))
        if self.failure: return self.failure
        if args['recipeId'] == 'prepare_owned_operation':
            params = args['parameters']; packet_id = uuid.uuid4().hex
            binding = json.loads((self.root / 'documentbindings' / (params['bindingId'] + '.json')).read_text())
            plan = json.loads((self.root / 'operationplans' / (params['operationPlanId'] + '.json')).read_text())
            packet = {**params, 'schemaVersion': 1, 'ownedOperationId': packet_id,
                      'preparedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
                      'expiresUtc': min(plan['expiresUtc'], binding['expiresUtc']), 'executionEnabled': False,
                      'nativeObservedPropertiesSha256': binding['observedPropertiesSha256'], 'windowContextSha256': binding['windowContextSha256'],
                      'workingSha256': 'a' * 64, 'copyId': binding['copyId'], 'ownershipSha256': binding['ownershipSha256']}
            sha = freeze(self.root / 'ownedoperations' / (packet_id + '.json'), packet)
            append_event(self.root, packet_id, sha, 'prepared', {'executionEnabled': False}, 0)
            self.made = {'state': 'owned_operation_prepared_observed', 'ownedOperationId': packet_id,
                         'ownedOperationSha256': sha, 'ownedOperation': packet, 'expiresUtc': packet['expiresUtc'],
                         'bridgeRequestId': packet_id, 'executionEnabled': False, 'geometryMutationAttempted': False,
                         'contextChangeAttempted': False, 'cadSaveAttempted': False, 'guardPasses': 2, 'sameContextObservationReads': 4}
            return self.made
        return {**self.made, 'state': 'owned_operation_validated_observed', 'bridgeRequestId': uuid.uuid4().hex}

    def call(self, name, args, cancel=None):
        self.server.call(1, {'name': name, 'arguments': args}, cancel or threading.Event())
        result, error = self.replies[-1]
        return result.get('structuredContent') if result else error

    def prepare(self): return self.call('prepare_owned_operation', self.args)
    def validate(self, handle): return self.call('validate_owned_operation', {'ownedOperationHandle': handle})

    def test_fixed_private_envelopes_and_prepared_only_journal(self):
        prepared = self.prepare()
        self.assertEqual(prepared['state'], 'owned_operation_prepared_observed')
        self.assertFalse(prepared['executionEnabled'])
        parameters = self.server.owned_operations.prepare_parameters(self.plan, self.binding)
        self.assertEqual(self.calls[-1], ('recipe.prepare', {'recipeId': 'prepare_owned_operation', 'parameters': parameters}))
        path = self.root / 'operations' / prepared['ownedOperationId'] / '000000.json'
        before = path.read_bytes()
        result = self.validate(prepared['ownedOperationHandle'])
        self.assertEqual(result['state'], 'owned_operation_validated_observed')
        self.assertEqual(self.calls[-1][1], {'recipeId': 'validate_owned_operation', 'parameters': {'ownedOperationId': prepared['ownedOperationId'], 'ownedOperationSha256': prepared['ownedOperationSha256']}})
        self.assertEqual(before, path.read_bytes())
        self.assertEqual(len(list(path.parent.glob('*.json'))), 1)

    def test_foreign_old_handles_and_extra_fields_never_queue(self):
        for key in self.args:
            args = {**self.args, key: 'a' * 32}
            self.assertEqual(self.call('prepare_owned_operation', args)['state'], 'owned_operation_rejected')
        self.assertEqual(self.validate('a' * 32)['state'], 'owned_operation_rejected')
        for args in ({**self.args, 'command': 'SAVE'}, {'operationPlanId': 'a' * 32, 'bindingId': 'b' * 32}):
            self.assertEqual(self.call('prepare_owned_operation', args)['code'], -32602)
        self.assertFalse(self.calls)

    def test_dependency_revocation_and_expiry_never_queue(self):
        for store in ('entity_sets', 'operation_plans', 'working_copies', 'document_bindings'):
            self.prime(); packet = self.prepare(); before = len(self.calls)
            getattr(self.server, store).clear()
            self.assertEqual(self.validate(packet['ownedOperationHandle'])['state'], 'owned_operation_rejected')
            self.assertEqual(len(self.calls), before)
        self.prime(); prepared = self.prepare(); handle = prepared['ownedOperationHandle']
        self.server.owned_operations.packets[handle]['expires'] = 0
        self.assertEqual(self.validate(handle)['state'], 'owned_operation_rejected')

    def test_other_connection_and_capacity_revoke_handles(self):
        packet = self.prepare()
        with self.assertRaises(ReferenceRejected): Server(self.root).owned_operations.validation_parameters(packet['ownedOperationHandle'])
        self.server.owned_operations.MAX_PACKETS = 1
        self.prepare()
        self.assertEqual(self.validate(packet['ownedOperationHandle'])['state'], 'owned_operation_rejected')

    def test_every_source_proof_tamper_is_rejected_before_queue(self):
        for index in range(5):
            self.prime(); packet = self.prepare(); before = len(self.calls)
            path = self.proofs[index]
            if index == 4:
                event = json.loads(path.read_text()); event['state'] = 'command_started'; path.write_text(json.dumps(event))
            else:
                path.write_bytes(path.read_bytes() + b' ')
            self.assertEqual(self.validate(packet['ownedOperationHandle'])['state'], 'owned_operation_rejected')
            self.assertEqual(len(self.calls), before)

    def test_native_packet_hash_identity_policy_and_binding_checks(self):
        prepared = self.prepare(); params = self.server.owned_operations.prepare_parameters(self.plan, self.binding)
        for changes in ({'ownedOperationSha256': '0' * 64}, {'bridgeRequestId': 'b' * 32}, {'executionEnabled': True}, {'geometryMutationAttempted': True}, {'ownedOperation': {}}):
            with self.assertRaises((ReferenceRejected, OSError)):
                self.server.owned_operations.issue({**self.made, **changes}, params, self.plan, self.binding)
        packet = copy.deepcopy(prepared['ownedOperation']); packet['copyId'] = '0' * 32
        path = self.root / 'ownedoperations' / (packet['ownedOperationId'] + '.json')
        path.unlink(); sha = freeze(path, packet)
        with self.assertRaises(ReferenceRejected): self.server.owned_operations.receipt({**self.made, 'ownedOperation': packet, 'ownedOperationSha256': sha})

    def test_native_prepared_journal_corruption_or_interruption_is_rejected(self):
        for target in ('000000.json', 'writer.lock'):
            self.prime(); packet = self.prepare()
            path = self.root / 'operations' / packet['ownedOperationId'] / target
            path.write_text('interrupted')
            with self.assertRaises((ReferenceRejected, ValueError)): self.server.owned_operations.receipt(self.made)

    def test_dependency_loss_during_native_call_never_returns_handle(self):
        original = self.queue
        def delayed(cancel, operation, args):
            result = original(cancel, operation, args)
            self.server.document_bindings.clear()
            return result
        self.server.snapshot = delayed
        result = self.prepare()
        self.assertEqual(result['state'], 'owned_operation_rejected')
        self.assertTrue(result['localArtifactsMayExist'])
        self.assertIn('bridgeRequestId', result)
        self.assertNotIn('ownedOperationHandle', result)

    def test_validation_dependency_loss_rejects_native_success(self):
        packet = self.prepare(); original = self.queue
        def delayed(cancel, operation, args):
            result = original(cancel, operation, args); self.server.working_copies.clear(); return result
        self.server.snapshot = delayed
        self.assertEqual(self.validate(packet['ownedOperationHandle'])['state'], 'owned_operation_rejected')
        self.assertFalse(self.server.owned_operations.packets)

    def test_failure_unknown_cancel_no_retry(self):
        for state in ('owned_operation_rejected', 'timeout_outcome_unknown', 'cancelled_outcome_unknown'):
            self.prime(); prepared = self.prepare(); self.failure = {'state': state, 'bridgeRequestId': '1' * 32}
            self.assertEqual(self.validate(prepared['ownedOperationHandle'])['state'], state)
            before = len(self.calls); self.validate(prepared['ownedOperationHandle']); self.assertEqual(len(self.calls), before)
            self.failure = None
        cancel = threading.Event(); cancel.set(); before = len(self.calls)
        self.assertEqual(self.call('prepare_owned_operation', self.args, cancel)['state'], 'cancelled_before_submission')
        self.assertEqual(len(self.calls), before)

    def test_catalog_strict_gate_and_readonly_exclusion(self):
        self.assertEqual(len(self.server.tools), 19)
        readonly = Server(self.root, read_only=True); self.assertEqual(len(readonly.tools), 6)
        names = {t['name'] for t in readonly.tools}; self.assertFalse(names & {'prepare_owned_operation', 'validate_owned_operation'})
        readonly.reply = self.server.reply
        readonly.call(1, {'name': 'validate_owned_operation', 'arguments': {'ownedOperationHandle': 'a' * 32}}, threading.Event())
        self.assertEqual(self.replies[-1][1]['code'], -32602)
        for changes in ({'ownedOperationPlansExperimental': 'true'}, {'operationPlansExperimental': False}, {'entitySetsExperimental': False}, {'workingCopiesExperimental': False}, {'documentBindingsExperimental': False}, {'coreProfile': None}):
            with self.assertRaises(ValueError): general_core({**self.config, **changes})
        self.config['ownedOperationPlansExperimental'] = False
        (self.root / 'bridge.config.json').write_text(json.dumps(self.config))
        self.assertEqual(len(Server(self.root).tools), 17)


if __name__ == '__main__': unittest.main()
