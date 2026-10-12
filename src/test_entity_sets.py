"""Candidate/set isolation and experimental public routes; no CAD contacted."""
import copy
import datetime
import hashlib
import json
from pathlib import Path
import threading
import unittest
import uuid

from entity_sets import EntitySetStore
from entity_references import ReferenceRejected, ReferenceStore
from mcp_server import Server
from test_entity_references import snapshot


def fresh():
    result = snapshot()
    result['timestampUtc'] = datetime.datetime.now(datetime.timezone.utc).isoformat()
    return result


class EntitySetLeases(unittest.TestCase):
    def setUp(self):
        self.now = 100.0
        self.store = EntitySetStore(clock=lambda: self.now)
        self.data = fresh()
        self.issued = self.store.issue_candidates(self.data, 'b' * 64)
        self.doc = self.issued['candidateDocumentHandle']
        self.handles = [self.issued['entities'][0]['candidateEntityHandle']]

    def prepare(self):
        return self.store.prepare_parameters(self.doc, self.handles)

    def test_explicit_document_native_keys_and_fixed_receipt(self):
        args = self.prepare()
        self.assertEqual(args['entityKeys'], [[40, 5]])
        self.assertEqual(args['inspectionRequestId'], 'a' * 32)
        self.assertEqual(args['inspectionSha256'], 'b' * 64)
        self.assertNotIn('candidateEntityHandle', self.data['entities'][0])

    def test_read_references_and_other_connections_are_not_candidates(self):
        read = ReferenceStore().issue(self.data)
        with self.assertRaises(ReferenceRejected):
            self.store.prepare_parameters(read['documentReference'], [read['entities'][0]['entityReference']])
        with self.assertRaises(ReferenceRejected):
            EntitySetStore().prepare_parameters(self.doc, self.handles)
        another = self.store.issue_candidates(self.data, 'b' * 64)
        with self.assertRaises(ReferenceRejected):
            self.store.prepare_parameters(another['candidateDocumentHandle'], self.handles)

    def test_expiry_duplicate_and_empty_sets(self):
        for handles in ([], self.handles * 2, ['bad'], [None]):
            with self.assertRaises(ReferenceRejected): self.store.prepare_parameters(self.doc, handles)
        self.now += 301
        with self.assertRaises(ReferenceRejected): self.prepare()

    def test_unsupported_type_is_visible_but_cannot_prepare(self):
        data = fresh()
        data['entities'][0].update(nativeTypeCode=54, type='WORKPLANE')
        issued = self.store.issue_candidates(data, 'b' * 64)
        self.assertFalse(issued['entities'][0]['entitySetSupported'])
        with self.assertRaises(ReferenceRejected):
            self.store.prepare_parameters(issued['candidateDocumentHandle'], [issued['entities'][0]['candidateEntityHandle']])

    def test_observed_changes_clear_candidates_and_sets(self):
        for field, change in [('processId', 101), ('processStartedUtc', 'other'), ('lengthUnitFactorRaw', 25.4), ('contextBefore', [0, 9])]:
            with self.subTest(field=field):
                store = EntitySetStore()
                issued = store.issue_candidates(self.data, 'b' * 64)
                changed = copy.deepcopy(self.data)
                changed[field] = change
                store.observe(changed)
                with self.assertRaises(ReferenceRejected):
                    store.prepare_parameters(issued['candidateDocumentHandle'], [issued['entities'][0]['candidateEntityHandle']])

    def test_bad_receipt_time_and_failed_snapshot_fail_closed(self):
        for change, sha in [({'bridgeRequestId': '../x'}, 'b' * 64), ({'timestampUtc': 'invalid'}, 'b' * 64),
                            ({'timestampUtc': '2000-01-01T00:00:00Z'}, 'b' * 64), ({'state': 'failed'}, 'b' * 64), ({}, 'X' * 64)]:
            with self.subTest(change=change), self.assertRaises(ReferenceRejected):
                self.store.issue_candidates({**self.data, **change}, sha)

    def test_set_handle_uses_worker_expiry_and_same_connection(self):
        expiry = (datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(seconds=40)).isoformat()
        result = self.store.issue_set({'state': 'entity_set_prepared_observed', 'entitySetId': 'c' * 32,
                                       'entitySetSha256': 'd' * 64, 'expiresUtc': expiry})
        handle = result['entitySetHandle']
        self.assertEqual(self.store.validation_parameters(handle)['entitySetId'], 'c' * 32)
        with self.assertRaises(ReferenceRejected): EntitySetStore().validation_parameters(handle)
        self.now += 41
        with self.assertRaises(ReferenceRejected): self.store.validation_parameters(handle)


class EntitySetRoutes(unittest.TestCase):
    def setUp(self):
        self.root = Path(__file__).parents[1] / 'work' / ('set-test-' + uuid.uuid4().hex)
        self.root.mkdir(parents=True)
        (self.root / 'responses').mkdir()
        settings = json.loads((Path(__file__).parents[1] / 'config/general-entity-sets.json').read_text())
        (self.root / 'bridge.config.json').write_text(json.dumps(settings))
        self.server = Server(self.root)
        self.replies, self.calls = [], []
        self.server.reply = lambda request, result=None, error=None: self.replies.append((result, error))
        self.data = fresh()
        (self.root / 'responses' / ('a' * 32 + '.json')).write_text(json.dumps(self.data))
        self.failure = None
        def queue(cancel, operation, args):
            self.calls.append((operation, args))
            if args['recipeId'] == 'inspect_active_model': return copy.deepcopy(self.data)
            if self.failure: return self.failure
            if args['recipeId'] == 'prepare_entity_set':
                return {'state': 'entity_set_prepared_observed', 'entitySetId': 'c' * 32,
                        'entitySetSha256': 'd' * 64, 'expiresUtc': args['parameters']['expiresUtc'], 'cadMutationAttempted': False}
            return {'state': 'entity_set_validated_observed', 'cadMutationAttempted': False}
        self.server.snapshot = queue

    def call(self, name, args=None, cancel=None):
        self.server.call(len(self.replies) + 1, {'name': name, 'arguments': args or {}}, cancel or threading.Event())
        result, error = self.replies[-1]
        return result['structuredContent'] if result and 'structuredContent' in result else error

    def candidates(self):
        data = self.call('inspect_entity_candidates')
        return {'candidateDocumentHandle': data['candidateDocumentHandle'],
                'candidateEntityHandles': [data['entities'][0]['candidateEntityHandle']]}

    def test_prepare_and_validate_only_fixed_worker_envelopes(self):
        parameters = self.candidates()
        prepared = self.call('prepare_entity_set', parameters)
        operation, args = self.calls[-1]
        self.assertEqual(operation, 'recipe.prepare')
        self.assertEqual(args['recipeId'], 'prepare_entity_set')
        self.assertEqual(set(args['parameters']), {'inspectionRequestId', 'inspectionSha256', 'expiresUtc', 'entityKeys'})
        self.assertEqual(args['parameters']['inspectionSha256'], hashlib.sha256((self.root / 'responses' / ('a' * 32 + '.json')).read_bytes()).hexdigest())
        checked = self.call('validate_entity_set', {'entitySetHandle': prepared['entitySetHandle']})
        self.assertEqual(checked['state'], 'entity_set_validated_observed')
        self.assertEqual(self.calls[-1][1]['parameters'], {'entitySetId': 'c' * 32, 'entitySetSha256': 'd' * 64})

    def test_arbitrary_native_ids_paths_duplicate_and_read_tokens_never_queue(self):
        for args in ({'documentReference': 'a' * 32, 'entityReferences': ['b' * 32]},
                     {'candidateDocumentHandle': 'a' * 32, 'candidateEntityHandles': ['b' * 32], 'entityKeys': [[40, 5]]},
                     {'candidateDocumentHandle': 'a' * 32, 'candidateEntityHandles': ['b' * 32] * 2},
                     {'candidateDocumentHandle': '../outside', 'candidateEntityHandles': ['b' * 32]}):
            result = self.call('prepare_entity_set', args)
            self.assertEqual(result['code'], -32602)
        result = self.call('prepare_entity_set', {'candidateDocumentHandle': 'a' * 32, 'candidateEntityHandles': ['b' * 32]})
        self.assertEqual(result['state'], 'entity_set_rejected')
        self.assertFalse(self.calls)

    def test_failed_worker_validation_revokes_all_handles_and_does_not_retry(self):
        args = self.candidates()
        prepared = self.call('prepare_entity_set', args)
        self.failure = {'state': 'entity_set_rejected', 'error': 'Observed document changed', 'cadMutationAttempted': False}
        self.assertEqual(self.call('validate_entity_set', {'entitySetHandle': prepared['entitySetHandle']})['state'], 'entity_set_rejected')
        count = len(self.calls)
        self.assertEqual(self.call('validate_entity_set', {'entitySetHandle': prepared['entitySetHandle']})['state'], 'entity_set_rejected')
        self.assertEqual(len(self.calls), count)
        self.assertFalse(self.server.entity_sets.documents)

    def test_timeout_revokes_candidates_without_retry_or_fabricated_success(self):
        args = self.candidates()
        self.failure = {'state': 'timeout_outcome_unknown', 'bridgeRequestId': 'e' * 32}
        data = self.call('prepare_entity_set', args)
        self.assertEqual(data['state'], 'timeout_outcome_unknown')
        self.assertNotIn('entitySetHandle', data)
        count = len(self.calls)
        self.call('prepare_entity_set', args)
        self.assertEqual(count, len(self.calls))

    def test_missing_native_receipt_and_cancelled_read_clear_candidates(self):
        self.candidates()
        self.data['bridgeRequestId'] = 'f' * 32
        self.assertEqual(self.call('inspect_entity_candidates')['state'], 'entity_set_rejected')
        self.assertFalse(self.server.entity_sets.documents)
        cancel = threading.Event()
        cancel.set()
        self.assertEqual(self.call('inspect_entity_candidates', cancel=cancel)['state'], 'cancelled_read')

    def test_changed_receipt_is_never_promoted_to_candidate_handle(self):
        self.candidates()
        changed = copy.deepcopy(self.data)
        changed['entities'][0]['name'] = 'Changed after delivery'
        (self.root / 'responses' / ('a' * 32 + '.json')).write_text(json.dumps(changed))
        data = self.call('inspect_entity_candidates')
        self.assertEqual(data['state'], 'entity_set_rejected')
        self.assertNotIn('candidateDocumentHandle', data)
        self.assertFalse(self.server.entity_sets.documents)

    def test_experimental_gate_and_readonly_exclude_preparation_and_mutations(self):
        readonly = Server(self.root, read_only=True)
        names = {tool['name'] for tool in readonly.tools}
        self.assertNotIn('prepare_entity_set', names)
        self.assertNotIn('activate_document', names)
        self.assertNotIn('validate_entity_set', names)
        self.assertIn('inspect_entity_candidates', names)
        settings = json.loads((self.root / 'bridge.config.json').read_text())
        settings['entitySetsExperimental'] = False
        (self.root / 'bridge.config.json').write_text(json.dumps(settings))
        self.assertFalse({t['name'] for t in Server(self.root).tools} & {'inspect_entity_candidates', 'prepare_entity_set', 'validate_entity_set'})


if __name__ == '__main__': unittest.main()
