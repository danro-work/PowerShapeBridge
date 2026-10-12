"""Connection-local leases for native saved-file copy ownership; no CAD commands."""
import copy
import datetime
import re
import time
import uuid

from entity_references import ReferenceRejected
from entity_sets import remaining, token


def validate_source(args):
    if not isinstance(args, dict) or set(args) != {'sourcePath', 'sourceSha256'}:
        raise ReferenceRejected('working_copy_source_arguments_invalid')
    path = args['sourcePath']
    if not isinstance(path, str) or not re.match(r'^[A-Za-z]:[\\/]', path) or not path.lower().endswith('.psmodel') or not token(args['sourceSha256'], 64):
        raise ReferenceRejected('working_copy_saved_source_invalid')
    if any(c in path for c in '\0*?<>"|') or ':' in path[2:]:
        raise ReferenceRejected('working_copy_source_path_invalid')
    if any(part in ('.', '..') or part.endswith(('.', ' ')) for part in re.split(r'[\\/]', path[3:])):
        raise ReferenceRejected('working_copy_source_path_ambiguous')


class WorkingCopyStore:
    MAX_COPIES = 128

    def __init__(self, clock=time.monotonic):
        self.clock, self.copies = clock, {}

    def clear(self):
        self.copies.clear()

    def purge(self):
        self.copies = {k: v for k, v in self.copies.items() if v['expires'] > self.clock()}

    def issue(self, result):
        if result.get('state') != 'working_copy_prepared_disk' or not token(result.get('copyId')) or not token(result.get('ownershipSha256'), 64) or result.get('bridgeRequestId') != result.get('copyId'):
            raise ReferenceRejected('working_copy_native_receipt_invalid')
        _, seconds = remaining(result.get('timestampUtc'))
        return self._issue(result, seconds)

    def issue_verified_open(self, result):
        if result.get('state') != 'owned_open_verified_observed' or not token(result.get('copyId')) or not token(result.get('ownershipSha256'), 64) or not token(result.get('openOperationId')) or not token(result.get('bridgeRequestId')) or result.get('recordedJournalState') != 'succeeded_verified':
            raise ReferenceRejected('verified_open_native_receipt_invalid')
        _, seconds = remaining(result.get('timestampUtc'))
        expiry = datetime.datetime.fromisoformat(result['expiresUtc'].replace('Z', '+00:00'))
        seconds = min(seconds, (expiry - datetime.datetime.now(datetime.timezone.utc)).total_seconds())
        if not 0 < seconds <= 300:
            raise ReferenceRejected('verified_open_expired')
        return self._issue(result, seconds)

    def _issue(self, result, seconds):
        self.purge()
        if len(self.copies) >= self.MAX_COPIES:
            self.clear()
        handle = uuid.uuid4().hex
        self.copies[handle] = {'expires': self.clock() + seconds, 'parameters': {
            'copyId': result['copyId'], 'ownershipSha256': result['ownershipSha256']}}
        return {**result, 'workingCopyHandle': handle, 'workingCopyLifetimeSeconds': int(seconds)}

    def inspect_parameters(self, handle):
        self.purge()
        record = self.copies.get(handle)
        if record is None:
            raise ReferenceRejected('working_copy_handle_unknown_or_expired')
        return copy.deepcopy(record['parameters'])
