"""Plan, artifact, journal and public API failure checks. Never contacts CAD."""
import copy
import datetime
import hashlib
import json
from pathlib import Path
import threading
import unittest
import uuid
from unittest.mock import patch

from core_config import general_core
from entity_references import ReferenceRejected
from entity_sets import EntitySetStore
from mcp_server import Server
from operation_plans import (OBSERVED_FIELDS, OperationPlanStore, append_event,
                             freeze, journal_events, validate_arguments)
from test_entity_references import snapshot


class PlanFixture(unittest.TestCase):
    def setUp(self):
        self.root = Path(__file__).parents[1] / 'work' / ('operation-plan-test-' + uuid.uuid4().hex)
        self.root.mkdir(parents=True)
        (self.root / 'responses').mkdir()
        (self.root / 'entitysets').mkdir()
        self.now = 10.0
        self.sets = EntitySetStore(clock=lambda: self.now)
        self.store = OperationPlanStore(self.root, self.sets, clock=lambda: self.now)
        self.source = dict.fromkeys(OBSERVED_FIELDS)
        self.source.update(snapshot(), productVersion='23.2.2', modelScope='active_document_read_only',
                           timestampUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                           contextAfter=[0, 0], selectionBefore=[], selectionAfter=[], levelsContainingEntities=[])
        self.source['entities'] += [{'nativeTypeCode': 21, 'nativeId': 8, 'type': 'SURFACE', 'name': 'Plane', 'levelNumber': 0},
                                    {'nativeTypeCode': 59, 'nativeId': 10, 'type': 'COMPCURVE', 'name': 'Curve', 'levelNumber': 0}]
        self.source['entityCount'] = 3
        self.handles = [self.make_set([[40, 5]]), self.make_set([[21, 8]]), self.make_set([[59, 10]])]

    def make_set(self, keys, source=None):
        source = copy.deepcopy(source or self.source)
        source['bridgeRequestId'] = uuid.uuid4().hex
        receipt_sha = freeze(self.root / 'responses' / (source['bridgeRequestId'] + '.json'), source)
        self.sets.observe(source)
        set_id = uuid.uuid4().hex
        expiry = (datetime.datetime.fromisoformat(source['timestampUtc']) + datetime.timedelta(seconds=300)).isoformat()
        plan = {'inspectionRequestId': source['bridgeRequestId'], 'inspectionSha256': receipt_sha,
                'expiresUtc': expiry, 'entityKeys': keys}
        sha = freeze(self.root / 'entitysets' / (set_id + '.json'), plan)
        return self.sets.issue_set({'state': 'entity_set_prepared_observed', 'entitySetId': set_id,
                                    'entitySetSha256': sha, 'expiresUtc': expiry})['entitySetHandle']

    def args(self, operation='translate_entities', handles=None):
        return {'operation': operation, 'entitySetHandles': handles or self.handles[:1],
                'parameters': {'vector': [1, 2, 3], 'coordinateFrame': 'world', 'lengthUnits': 'document_raw', 'mode': 'move'}
                if operation == 'translate_entities' else {'offset': 0}}

    def plan(self):
        return self.store.prepare(self.args())


class PlanTests(PlanFixture):
    def test_translation_freezes_receipts_raw_units_and_prepared_journal(self):
        result = self.plan()
        inspected = self.store.inspect(result['operationPlanHandle'])
        self.assertFalse(result['plan']['executionEnabled'])
        self.assertEqual(inspected['journal']['recordedState'], 'prepared')
        self.assertEqual(result['planSha256'], hashlib.sha256((self.root / 'operationplans' / (result['operationId'] + '.json')).read_bytes()).hexdigest())
        self.assertEqual(result['plan']['entitySets'][0]['entityKeys'], [[40, 5]])
        self.assertEqual(result['plan']['parameters']['lengthUnits'], 'document_raw')
        self.assertFalse((self.root / 'requests').exists())

    def test_intersection_disjoint_explicit_roles_and_non_boolean_plan(self):
        result = self.store.prepare(self.args('intersect_entity_sets', self.handles[:2]))
        self.assertEqual([x['entityKeys'] for x in result['plan']['entitySets']], [[[40, 5]], [[21, 8]]])
        self.assertFalse(result['cadMutationAttempted'])

    def test_intersection_overlap_wrong_roles_and_curve_rejected(self):
        overlap = self.make_set([[21, 8]])
        for handles in ([self.handles[1], overlap], self.handles[:2][::-1], self.handles[1:3], [self.handles[2], self.handles[1]]):
            with self.subTest(handles=handles), self.assertRaises(ReferenceRejected):
                self.store.prepare(self.args('intersect_entity_sets', handles))
        self.assertFalse((self.root / 'operationplans').exists())

    def test_full_observation_comparison_catches_selection_and_other_units(self):
        for field, value in [('selectionBefore', [[40, 5]]), ('contextAfter', [0, 3]), ('productVersion', 'other'), ('levelsContainingEntities', [{'number': 1}])]:
            changed = copy.deepcopy(self.source)
            changed[field] = value
            other = self.make_set([[21, 8]], changed)
            with self.subTest(field=field), self.assertRaises(ReferenceRejected):
                self.store.prepare(self.args('intersect_entity_sets', [self.handles[0], other]))

    def test_strict_parameters_no_native_paths_commands_copy_or_bad_numbers(self):
        bad = [dict(self.args(), path='D:/Part'), dict(self.args(), entitySetHandles=['../outside']),
               dict(self.args(), operation='execute_command'), self.args('intersect_entity_sets', self.handles[:1]),
               self.args('intersect_entity_sets', self.handles[:1] * 2)]
        for vector in ([0, 0, 0], [True, 1, 2], [float('nan'), 0, 1], [float('inf'), 0, 1], [1000001, 0, 1], [10**400, 0, 1], ['1', 2, 3], [1, 2]):
            args = self.args(); args['parameters']['vector'] = vector; bad.append(args)
        for key, value in [('mode', 'copy'), ('coordinateFrame', 'active'), ('lengthUnits', 'mm'), ('command', 'ACCEPT')]:
            args = self.args(); args['parameters'][key] = value; bad.append(args)
        for offset in (True, 0.1, float('nan'), '0'):
            args = self.args('intersect_entity_sets', self.handles[:2]); args['parameters']['offset'] = offset; bad.append(args)
        for args in bad:
            with self.subTest(args=args), self.assertRaises(ReferenceRejected): validate_arguments(args)

    def test_plan_hash_source_hash_and_set_hash_all_checked(self):
        result = self.plan()
        record = self.sets.validation_parameters(self.handles[0])
        for path in (self.root / 'operationplans' / (result['operationId'] + '.json'),
                     self.root / 'entitysets' / (record['entitySetId'] + '.json')):
            raw = path.read_bytes(); path.write_text('{}')
            with self.assertRaises(ReferenceRejected): self.store.inspect(result['operationPlanHandle'])
            path.write_bytes(raw)
        plan = json.loads((self.root / 'entitysets' / (record['entitySetId'] + '.json')).read_text())
        path = self.root / 'responses' / (plan['inspectionRequestId'] + '.json')
        path.write_text('{}')
        with self.assertRaises(ReferenceRejected): self.store.inspect(result['operationPlanHandle'])

    def test_handles_expire_revoke_with_sets_and_are_connection_local(self):
        result = self.plan()
        with self.assertRaises(ReferenceRejected): OperationPlanStore(self.root, self.sets).inspect(result['operationPlanHandle'])
        self.now += 301
        with self.assertRaises(ReferenceRejected): self.store.inspect(result['operationPlanHandle'])
        self.now = 10
        result = self.plan(); self.sets.clear()
        with self.assertRaises(ReferenceRejected): self.store.inspect(result['operationPlanHandle'])

    def test_partial_local_journal_failure_preserves_artifact_identity(self):
        with patch('operation_plans.append_event', side_effect=OSError('disk full')):
            with self.assertRaises(ReferenceRejected) as failure: self.plan()
        self.assertEqual(failure.exception.code, 'operation_plan_artifacts_incomplete_or_expired')
        self.assertTrue((self.root / 'operationplans' / (failure.exception.operation_id + '.json')).exists())
        self.assertFalse(self.store.plans)

    def test_unknown_expired_or_unsupported_set_fails_before_plan_files(self):
        with self.assertRaises(ReferenceRejected): self.store.prepare(self.args(handles=['f' * 32]))
        self.now += 301
        with self.assertRaises(ReferenceRejected): self.plan()
        self.assertFalse((self.root / 'operationplans').exists())


class JournalTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(__file__).parents[1] / 'work' / ('operation-journal-test-' + uuid.uuid4().hex)
        self.root.mkdir(parents=True)
        self.id, self.sha = uuid.uuid4().hex, 'd' * 64
        self.evidence = {'evidence': [{'requestId': 'e' * 32, 'sha256': 'f' * 64}]}
        self.append('prepared', {}, 0)

    def append(self, state, details, count):
        return append_event(self.root, self.id, self.sha, state, details, count)

    def test_interrupted_claim_start_return_is_unknown_and_never_retryable(self):
        for number, state in enumerate(('execution_claimed', 'command_started', 'command_returned'), 1):
            self.append(state, {}, number)
            journal = journal_events(self.root, self.id)
            self.assertEqual(journal['state'], 'outcome_unknown')
            self.assertEqual(journal['recordedState'], state)
            self.assertFalse(journal['retryAllowed'])

    def test_unknown_cannot_restart_or_succeed_without_independent_evidence(self):
        self.append('execution_claimed', {}, 1)
        self.append('command_started', {}, 2)
        self.append('outcome_unknown', {}, 3)
        for state, details in [('execution_claimed', {}), ('command_started', {}), ('succeeded_verified', {}), ('prepared', {})]:
            with self.subTest(state=state), self.assertRaises(ReferenceRejected): self.append(state, details, 4)
        self.append('recovery_required', self.evidence, 4)
        with self.assertRaises(ReferenceRejected): self.append('execution_claimed', {}, 5)

    def test_verified_terminal_state_rejects_replay_and_wrong_count(self):
        for number, state in enumerate(('execution_claimed', 'command_started', 'command_returned'), 1): self.append(state, {}, number)
        with self.assertRaises(ReferenceRejected): self.append('succeeded_verified', self.evidence, 2)
        self.append('succeeded_verified', self.evidence, 4)
        with self.assertRaises(ReferenceRejected): self.append('execution_claimed', {}, 5)
        self.assertEqual(journal_events(self.root, self.id)['state'], 'succeeded_verified')

    def test_chain_tamper_gaps_plan_change_and_partial_json_fail_closed(self):
        self.append('execution_claimed', {}, 1)
        folder = self.root / 'operations' / self.id
        first = folder / '000000.json'; raw = first.read_bytes()
        first.write_bytes(raw.replace(b'prepared', b'rejected'))
        with self.assertRaises(ReferenceRejected): journal_events(self.root, self.id)
        first.write_bytes(raw)
        (folder / '000001.json').rename(folder / '000002.json')
        with self.assertRaises(ReferenceRejected): journal_events(self.root, self.id)
        (folder / '000002.json').rename(folder / '000001.json')
        with self.assertRaises(ReferenceRejected): append_event(self.root, self.id, 'c' * 64, 'command_started', {}, 2)
        (folder / '000001.json').write_text('{')
        with self.assertRaises(ValueError): journal_events(self.root, self.id)

    def test_existing_writer_lock_blocks_append_and_reports_unknown(self):
        lock = self.root / 'operations' / self.id / 'writer.lock'
        lock.write_bytes(b'crashed writer')
        with self.assertRaises(FileExistsError): self.append('execution_claimed', {}, 1)
        self.assertTrue(lock.exists())
        self.assertEqual(journal_events(self.root, self.id)['state'], 'outcome_unknown')


class PlanRoutes(PlanFixture):
    def setUp(self):
        super().setUp()
        settings = json.loads((Path(__file__).parents[1] / 'config/general-operation-plans.json').read_text())
        (self.root / 'bridge.config.json').write_text(json.dumps(settings))
        self.server = Server(self.root)
        self.server.entity_sets = self.sets
        self.server.operation_plans = self.store
        self.replies = []
        self.server.reply = lambda request, result=None, error=None: self.replies.append((result, error))
        self.server.snapshot = lambda *args: self.fail('Local plan must never create a CAD request')

    def call(self, name, args=None, cancel=None):
        self.server.call(1, {'name': name, 'arguments': args or {}}, cancel or threading.Event())
        result, error = self.replies[-1]
        return result['structuredContent'] if result and 'structuredContent' in result else error

    def test_public_plan_inspection_and_journal_without_any_queue(self):
        result = self.call('prepare_operation_plan', self.args())
        self.assertEqual(result['state'], 'operation_plan_prepared_local')
        self.assertEqual(self.call('inspect_operation_plan', {'operationPlanHandle': result['operationPlanHandle']})['state'], 'operation_plan_inspected_local')
        self.assertEqual(self.call('operation_result', {'operationId': result['operationId']})['receipt']['state'], 'prepared')
        self.assertFalse((self.root / 'requests').exists())

    def test_catalog_gate_readonly_and_execution_absent(self):
        self.assertEqual(len(self.server.tools), 11)
        self.assertEqual(len(Server(self.root, read_only=True).tools), 6)
        for name in ('prepare_operation_plan', 'inspect_operation_plan', 'execute_operation_plan'):
            server = Server(self.root, read_only=True)
            server.reply = lambda request, result=None, error=None: self.replies.append((result, error))
            server.call(1, {'name': name, 'arguments': {}}, threading.Event())
            self.assertEqual(self.replies[-1][1]['code'], -32602)
        self.assertEqual(self.call('execute_operation_plan', {})['code'], -32602)

    def test_cancel_maintenance_bad_args_and_missing_handle_leave_no_artifacts(self):
        self.assertEqual(self.call('prepare_operation_plan', dict(self.args(), command='ACCEPT'))['code'], -32602)
        self.assertEqual(self.call('prepare_operation_plan', self.args(handles=['e' * 32]))['state'], 'operation_plan_rejected')
        cancel = threading.Event(); cancel.set()
        self.assertEqual(self.call('prepare_operation_plan', self.args(), cancel)['state'], 'cancelled_before_submission')
        (self.root / 'maintenance.json').write_text('{}')
        self.assertEqual(self.call('prepare_operation_plan', self.args())['state'], 'maintenance_before_submission')
        self.assertFalse((self.root / 'operationplans').exists())

    def test_config_requires_explicit_general_sets_and_boolean_flag(self):
        settings = json.loads((self.root / 'bridge.config.json').read_text())
        for change in ({'entitySetsExperimental': False}, {'coreProfile': None}, {'operationPlansExperimental': 'true'}):
            with self.subTest(change=change), self.assertRaises(ValueError): general_core({**settings, **change})

    def test_unreadable_journal_reports_unknown_without_command_retry(self):
        result = self.call('prepare_operation_plan', self.args())
        (self.root / 'operations' / result['operationId'] / '000000.json').write_text('{')
        read = self.call('operation_result', {'operationId': result['operationId']})
        self.assertEqual(read['state'], 'operation_journal_unavailable')
        self.assertEqual(read['outcome'], 'unknown')
        self.assertFalse(read['retryAllowed'])
        self.assertTrue(self.replies[-1][0]['isError'])


if __name__ == '__main__': unittest.main()
