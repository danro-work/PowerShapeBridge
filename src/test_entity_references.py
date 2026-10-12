"""Meaningful stale-context and read-only routing checks; no CAD is contacted."""
import copy
import json
from pathlib import Path
import threading
import unittest

from entity_references import ReferenceRejected, ReferenceStore
from mcp_server import Server


def snapshot():
    return {'state': 'succeeded_verified', 'processId': 100, 'processStartedUtc': '2026-10-09T08:00:00Z',
            'windowsSessionId': 1, 'model': {'id': 9, 'name': 'Part', 'path': 'D:/Parts/part.psmodel'},
            'entities': [{'nativeTypeCode': 40, 'nativeId': 5, 'type': 'SOLID', 'name': 'Body',
                          'levelNumber': 0, 'geometry': {'volumeRaw': 1.0}}],
            'entityCount': 1, 'lengthUnitsRaw': 'mm', 'lengthUnitFactorRaw': 1,
            'contextBefore': [0, 0], 'workplanes': [], 'bridgeRequestId': 'a' * 32}


class References(unittest.TestCase):
    def setUp(self):
        self.time = 100.0
        self.store = ReferenceStore(clock=lambda: self.time)
        self.data = snapshot()
        issued = self.store.issue(self.data)
        self.doc = issued['documentReference']
        self.token = issued['entities'][0]['entityReference']

    def test_resolves_same_observations_and_normalized_path(self):
        fresh = copy.deepcopy(self.data)
        fresh['model']['path'] = 'd:\\PARTS\\PART.psmodel'
        result = self.store.resolve(self.doc, [self.token], fresh)
        self.assertEqual(result['state'], 'entity_references_resolved_observed')
        self.assertFalse(result['cadMutationAttempted'])
        self.assertEqual(self.data['entities'][0].get('entityReference'), None)

    def test_each_observed_change_invalidates_old_reference(self):
        changes = (
            lambda x: x.update(processId=101),
            lambda x: x.update(processStartedUtc='2026-10-09T09:00:00Z'),
            lambda x: x['model'].update(id=10),
            lambda x: x['model'].update(path='D:/Parts/other.psmodel'),
            lambda x: x.update(lengthUnitFactorRaw=25.4),
            lambda x: x['entities'][0].update(name='Renamed'),
            lambda x: x['entities'][0].update(levelNumber=2),
            lambda x: x['entities'][0]['geometry'].update(volumeRaw=2),
            lambda x: x.update(contextBefore=[0, 7]),
        )
        for change in changes:
            with self.subTest(change=change):
                store = ReferenceStore(clock=lambda: self.time)
                issued = store.issue(self.data)
                doc, token = issued['documentReference'], issued['entities'][0]['entityReference']
                fresh = copy.deepcopy(self.data)
                change(fresh)
                with self.assertRaises(ReferenceRejected):
                    store.resolve(doc, [token], fresh)
                with self.assertRaises(ReferenceRejected):
                    store.require(doc, [token])

    def test_expiry_including_expiry_during_read(self):
        self.time = 400.0
        with self.assertRaises(ReferenceRejected):
            self.store.resolve(self.doc, [self.token], self.data)
        store = ReferenceStore(clock=lambda: 100.0)
        issued = store.issue(self.data)
        ticks = iter((100.0, 401.0))
        store.clock = lambda: next(ticks)
        with self.assertRaises(ReferenceRejected):
            store.resolve(issued['documentReference'], [issued['entities'][0]['entityReference']], self.data)

    def test_cross_document_and_new_connection_rejected(self):
        other = self.store.issue(self.data)
        with self.assertRaises(ReferenceRejected):
            self.store.require(other['documentReference'], [self.token])
        with self.assertRaises(ReferenceRejected):
            ReferenceStore().require(self.doc, [self.token])

    def test_observed_switch_and_back_never_revives_old_token(self):
        other = copy.deepcopy(self.data)
        other['model']['id'] = 10
        self.store.issue(other)
        self.store.issue(self.data)
        with self.assertRaises(ReferenceRejected):
            self.store.require(self.doc, [self.token])

    def test_duplicate_inventory_and_failed_read_clear_references(self):
        for bad in ({**self.data, 'state': 'failed'},
                    {**self.data, 'entities': self.data['entities'] * 2, 'entityCount': 2}):
            with self.assertRaises(ReferenceRejected):
                self.store.issue(bad)
            self.assertFalse(self.store.documents)


class ReadOnlyRouting(unittest.TestCase):
    def setUp(self):
        # Unique directories in the writable workspace, not restricted Windows Temp.
        import uuid
        self.root = Path(__file__).parents[1] / 'work' / ('references-test-' + uuid.uuid4().hex)
        self.root.mkdir(parents=True)
        (self.root / 'bridge.config.json').write_text(json.dumps({
            'mcpMutationsPanel': True, 'recipeOperationsPanel': True}), encoding='utf-8')
        self.server = Server(self.root, read_only=True)
        self.replies = []
        self.reads = []
        self.current = snapshot()
        self.server.reply = lambda request_id, result=None, error=None: self.replies.append((result, error))
        def read(cancel, operation, args):
            self.reads.append((operation, args))
            return copy.deepcopy(self.current)
        self.server.snapshot = read

    def call(self, name, args=None):
        self.server.call(1, {'name': name, 'arguments': args or {}}, threading.Event())
        return self.replies[-1]

    def test_read_only_catalog_and_mutation_rejected_before_queue(self):
        self.assertEqual({t['name'] for t in self.server.tools}, {
            'bridge_status', 'inspect_active_model', 'resolve_entity_references', 'operation_result', 'list_documents'})
        _, error = self.call('execute_translation', {'operationId': 'a' * 32, 'planSha256': 'b' * 64})
        self.assertEqual(error['code'], -32602)
        self.assertFalse(self.reads)
        self.assertEqual(self.server.status()['geometryMutations'], 'disabled')

    def test_route_resolve_and_changed_properties_rejected(self):
        result, error = self.call('inspect_active_model')
        self.assertIsNone(error)
        data = result['structuredContent']
        arguments = {'documentReference': data['documentReference'],
                     'entityReferences': [data['entities'][0]['entityReference']]}
        resolved, _ = self.call('resolve_entity_references', arguments)
        self.assertFalse(resolved['isError'])
        self.assertEqual(self.reads[0], ('recipe.prepare', {'recipeId': 'inspect_active_model', 'parameters': {}}))
        self.current['entities'][0]['geometry']['volumeRaw'] = 2
        rejected, _ = self.call('resolve_entity_references', arguments)
        self.assertEqual(rejected['structuredContent']['error']['code'], 'observed_properties_changed')
        self.assertTrue(rejected['isError'])
        count = len(self.reads)
        self.call('resolve_entity_references', arguments)
        self.assertEqual(len(self.reads), count)

    def test_invalid_and_duplicate_tokens_never_contact_cad(self):
        for tokens in ([], ['x'], ['a' * 32] * 2, ['a' * 32] * 129, [True]):
            _, error = self.call('resolve_entity_references', {
                'documentReference': 'a' * 32, 'entityReferences': tokens})
            self.assertEqual(error['code'], -32602)
        self.assertFalse(self.reads)

    def test_failed_inspection_invalidates_existing_tokens(self):
        result, _ = self.call('inspect_active_model')
        data = result['structuredContent']
        self.current = {'state': 'timeout_outcome_unknown'}
        failed, _ = self.call('inspect_active_model')
        self.assertTrue(failed['isError'])
        self.current = snapshot()
        failed, _ = self.call('resolve_entity_references', {
            'documentReference': data['documentReference'],
            'entityReferences': [data['entities'][0]['entityReference']]})
        self.assertEqual(failed['structuredContent']['error']['code'], 'document_reference_unknown_or_expired')


if __name__ == '__main__':
    unittest.main()
