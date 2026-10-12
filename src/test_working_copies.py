"""Native copy leases and routes; no CAD or native filesystem worker is called."""
import datetime
import json
from pathlib import Path
import threading
import unittest
import uuid

from core_config import general_core
from entity_references import ReferenceRejected
from mcp_server import Server
from working_copies import WorkingCopyStore, validate_source


class CopyLeases(unittest.TestCase):
    def setUp(self):
        self.now = 1.0
        self.store = WorkingCopyStore(clock=lambda: self.now)
        self.result = {'state': 'working_copy_prepared_disk', 'copyId': 'a' * 32,
                       'ownershipSha256': 'b' * 64, 'bridgeRequestId': 'a' * 32,
                       'timestampUtc': datetime.datetime.now(datetime.timezone.utc).isoformat()}

    def test_handle_is_local_expires_and_cannot_be_native_identity(self):
        issued = self.store.issue(self.result)
        args = self.store.inspect_parameters(issued['workingCopyHandle'])
        self.assertEqual(args, {'copyId': 'a' * 32, 'ownershipSha256': 'b' * 64})
        with self.assertRaises(ReferenceRejected): WorkingCopyStore().inspect_parameters(issued['workingCopyHandle'])
        with self.assertRaises(ReferenceRejected): self.store.inspect_parameters('a' * 32)
        self.now += 301
        with self.assertRaises(ReferenceRejected): self.store.inspect_parameters(issued['workingCopyHandle'])

    def test_receipt_identity_state_time_and_capacity(self):
        for change in ({'copyId': '../x'}, {'ownershipSha256': 'X' * 64}, {'bridgeRequestId': 'c' * 32},
                       {'state': 'working_copy_rejected'}, {'timestampUtc': '2000-01-01T00:00:00Z'}):
            with self.subTest(change=change), self.assertRaises(ReferenceRejected): self.store.issue({**self.result, **change})
        self.store.MAX_COPIES = 1
        first = self.store.issue(self.result)
        self.store.issue(self.result)
        with self.assertRaises(ReferenceRejected): self.store.inspect_parameters(first['workingCopyHandle'])

    def test_explicit_saved_source_no_streams_relative_steps_or_extra_commands(self):
        good = {'sourcePath': 'D:/Детали/Source.psmodel', 'sourceSha256': 'a' * 64}
        validate_source(good)
        for path in ('relative.psmodel', r'\\server\Part.psmodel', r'\\?\D:\Part.psmodel', 'D:/Part.step',
                     'D:/Part.psmodel:stream', 'D:/../Part.psmodel', 'D:/Folder./Part.psmodel', 'D:/Part*.psmodel'):
            with self.subTest(path=path), self.assertRaises(ReferenceRejected): validate_source({**good, 'sourcePath': path})
        with self.assertRaises(ReferenceRejected): validate_source({**good, 'overwrite': True})
        with self.assertRaises(ReferenceRejected): validate_source({**good, 'sourceSha256': None})


class CopyRoutes(unittest.TestCase):
    def setUp(self):
        self.root = Path(__file__).parents[1] / 'work' / ('copy-route-test-' + uuid.uuid4().hex)
        self.root.mkdir(parents=True)
        self.config = json.loads((Path(__file__).parents[1] / 'config/general-working-copies.json').read_text())
        (self.root / 'bridge.config.json').write_text(json.dumps(self.config))
        self.server = Server(self.root)
        self.replies, self.calls = [], []
        self.failure = None
        self.source = {'sourcePath': 'D:/Source.psmodel', 'sourceSha256': 'b' * 64}
        self.server.reply = lambda request, result=None, error=None: self.replies.append((result, error))
        def queue(cancel, operation, args):
            self.calls.append((operation, args))
            if self.failure: return self.failure
            if cancel.is_set(): return {'state': 'cancelled_before_submission'}
            prepared = args['recipeId'] == 'prepare_working_copy'
            return {'state': 'working_copy_prepared_disk' if prepared else 'working_copy_inspected_disk',
                    'copyId': 'c' * 32, 'ownershipSha256': 'd' * 64, 'bridgeRequestId': 'c' * 32,
                    'timestampUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
                    'cadMutationAttempted': False, 'powerShapeConnectionAttempted': False}
        self.server.snapshot = queue

    def call(self, name, args=None, cancel=None):
        self.server.call(1, {'name': name, 'arguments': args or {}}, cancel or threading.Event())
        result, error = self.replies[-1]
        return result['structuredContent'] if result and 'structuredContent' in result else error

    def test_prepare_inspect_only_fixed_envelopes_and_no_cad_claim(self):
        result = self.call('prepare_working_copy', self.source)
        self.assertEqual(self.calls[-1], ('recipe.prepare', {'recipeId': 'prepare_working_copy', 'parameters': self.source}))
        inspected = self.call('inspect_working_copy', {'workingCopyHandle': result['workingCopyHandle']})
        self.assertEqual(inspected['state'], 'working_copy_inspected_disk')
        self.assertEqual(self.calls[-1][1]['parameters'], {'copyId': 'c' * 32, 'ownershipSha256': 'd' * 64})
        self.assertFalse(inspected['cadMutationAttempted'])
        self.assertFalse(inspected['powerShapeConnectionAttempted'])

    def test_unknown_native_paths_and_old_read_tokens_never_queue(self):
        for args in ({'copyId': 'c' * 32, 'ownershipSha256': 'd' * 64}, {'documentReference': 'a' * 32},
                     {'workingCopyHandle': 'a' * 32, 'path': 'D:/Other.psmodel'}):
            self.assertEqual(self.call('inspect_working_copy', args)['code'], -32602)
        self.assertEqual(self.call('inspect_working_copy', {'workingCopyHandle': 'a' * 32})['state'], 'working_copy_rejected')
        self.assertFalse(self.calls)

    def test_failure_timeout_cancel_and_wrong_worker_identity_revoke_without_retry(self):
        for state in ('working_copy_rejected', 'timeout_outcome_unknown', 'cancelled_outcome_unknown'):
            self.failure = None
            result = self.call('prepare_working_copy', self.source)
            self.failure = {'state': state, 'bridgeRequestId': 'e' * 32}
            inspected = self.call('inspect_working_copy', {'workingCopyHandle': result['workingCopyHandle']})
            self.assertEqual(inspected['state'], state)
            number = len(self.calls)
            self.call('inspect_working_copy', {'workingCopyHandle': result['workingCopyHandle']})
            self.assertEqual(len(self.calls), number)
        self.failure = None
        result = self.call('prepare_working_copy', self.source)
        self.failure = {'state': 'working_copy_inspected_disk', 'copyId': 'f' * 32, 'ownershipSha256': 'd' * 64}
        self.assertEqual(self.call('inspect_working_copy', {'workingCopyHandle': result['workingCopyHandle']})['state'], 'working_copy_rejected')

    def test_cancel_before_submission_preserves_known_non_submission(self):
        cancel = threading.Event(); cancel.set()
        result = self.call('prepare_working_copy', self.source, cancel)
        self.assertEqual(result['state'], 'cancelled_before_submission')
        self.assertNotIn('workingCopyHandle', result)

    def test_gate_readonly_and_catalog(self):
        self.assertEqual(len(self.server.tools), 13)
        readonly = Server(self.root, read_only=True)
        self.assertEqual(len(readonly.tools), 6)
        names = {t['name'] for t in readonly.tools}
        self.assertFalse(names & {'prepare_working_copy', 'inspect_working_copy', 'execute_operation_plan'})
        readonly.reply = self.server.reply
        readonly.call(1, {'name': 'prepare_working_copy', 'arguments': self.source}, threading.Event())
        self.assertEqual(self.replies[-1][1]['code'], -32602)
        self.config['workingCopiesExperimental'] = False
        (self.root / 'bridge.config.json').write_text(json.dumps(self.config))
        self.assertEqual(len(Server(self.root).tools), 11)
        for changes in ({'workingCopiesExperimental': 'true'}, {'coreProfile': None}):
            with self.assertRaises(ValueError): general_core({**self.config, **changes, 'workingCopiesExperimental': changes.get('workingCopiesExperimental', True)})


if __name__ == '__main__': unittest.main()
