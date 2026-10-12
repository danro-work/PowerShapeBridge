"""Profile isolation and routing; no PowerShape connection."""
import json
from pathlib import Path
import uuid
import threading
import unittest

from core_config import general_core, FIXTURE_FIELDS, FIXTURE_FLAGS
from mcp_server import Server
from test_entity_references import snapshot


class CoreConfiguration(unittest.TestCase):
    def setUp(self):
        self.settings = json.loads((Path(__file__).parents[1] / 'config/general-core.json').read_text())

    def test_fixture_options_and_unknown_profile_fail_closed(self):
        for key in FIXTURE_FIELDS + FIXTURE_FLAGS:
            with self.subTest(key=key), self.assertRaises(ValueError):
                general_core({**self.settings, key: True if key in FIXTURE_FLAGS else 'fixture'})
        with self.assertRaises(ValueError): general_core({**self.settings, 'coreProfile': 'typo'})

    def test_invalid_limits_rejected(self):
        for key in ('maxEntities', 'timeoutSeconds', 'operationTimeoutSeconds'):
            for value in (True, 0, '20', 100001):
                with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                    general_core({**self.settings, key: value})

    def test_maintenance_refuses_queue_submission_without_cad(self):
        root = Path(__file__).parents[1] / 'work' / ('maintenance-test-' + uuid.uuid4().hex)
        root.mkdir(parents=True)
        (root / 'bridge.config.json').write_text(json.dumps(self.settings))
        (root / 'maintenance.json').write_text('{}')
        server = Server(root)
        self.assertTrue(server.status()['maintenance'])
        result = server.snapshot(threading.Event(), 'recipe.prepare', {'recipeId': 'inspect_active_model', 'parameters': {}})
        self.assertEqual(result['state'], 'maintenance_before_submission')
        self.assertFalse((root / 'requests').exists())

    def test_core_catalog_and_read_only_do_not_require_fixture(self):
        root = Path(__file__).parents[1] / 'work' / ('core-test-' + uuid.uuid4().hex)
        root.mkdir(parents=True)
        with self.subTest(profile='general-v1'):
            Path(root, 'bridge.config.json').write_text(json.dumps(self.settings))
            server = Server(root)
            names = {t['name'] for t in server.tools}
            self.assertEqual(names, {'bridge_status', 'list_documents', 'activate_document',
                                     'inspect_active_model', 'resolve_entity_references', 'operation_result'})
            self.assertEqual({t['name'] for t in Server(root, read_only=True).tools}, names - {'activate_document'})
            self.assertEqual(server.status()['geometryMutations'], 'disabled')
            calls, replies = [], []
            def read(cancel, operation, args):
                calls.append(args['recipeId'])
                return snapshot()
            server.snapshot = read
            server.reply = lambda request, result=None, error=None: replies.append((result, error))
            server.call(1, {'name': 'inspect_active_model'}, threading.Event())
            self.assertEqual(calls, ['inspect_active_model'])
            self.assertEqual(replies[0][0]['structuredContent']['state'], 'active_model_inspected')
            server.call(2, {'name': 'execute_recipe', 'arguments': {}}, threading.Event())
            self.assertEqual(len(calls), 1)
            self.assertEqual(replies[-1][1]['code'], -32602)


if __name__ == '__main__': unittest.main()
