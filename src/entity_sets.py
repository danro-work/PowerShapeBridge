"""Experimental candidate/set leases, separate from P2.21 read references."""
import copy
import datetime
import re
import time
import uuid

from entity_references import ReferenceRejected, identity, inventory, observation_digest


def token(value, size=32):
    return isinstance(value, str) and re.fullmatch('[0-9a-f]{%d}' % size, value) is not None


def remaining(timestamp, ttl=300):
    try:
        issued = datetime.datetime.fromisoformat(timestamp.replace('Z', '+00:00'))
        if issued.tzinfo is None:
            raise ValueError()
        expiry = issued + datetime.timedelta(seconds=ttl)
        seconds = (expiry - datetime.datetime.now(datetime.timezone.utc)).total_seconds()
        if not 0 < seconds <= ttl + 5:
            raise ValueError()
        return expiry.isoformat(), min(seconds, ttl)
    except (ValueError, TypeError, AttributeError):
        raise ReferenceRejected('entity_set_time_invalid_or_expired')


class EntitySetStore:
    MAX_ENTITIES = 4096
    MAX_DOCUMENTS = 32
    MAX_SETS = 128
    QUALIFICATION = ('Experimental observed entity set only. No CAD edit is authorized. '
                     'Standalone validation is not a check immediately before a later edit. '
                     'Unobserved ID reuse and changes outside measured properties remain possible.')

    def __init__(self, clock=time.monotonic):
        self.clock = clock
        self.documents, self.candidates, self.sets = {}, {}, {}
        self.context = None

    def clear(self):
        self.documents.clear()
        self.candidates.clear()
        self.sets.clear()
        self.context = None

    def purge(self):
        self.documents = {k: v for k, v in self.documents.items() if v['expires'] > self.clock()}
        self.candidates = {k: v for k, v in self.candidates.items() if v['document'] in self.documents}
        self.sets = {k: v for k, v in self.sets.items() if v['expires'] > self.clock()}

    def observe(self, snapshot):
        try:
            if type(snapshot.get('entityCount')) is not int:
                raise ReferenceRejected('entity_candidate_inventory_invalid')
            entities = inventory(snapshot)
            context = (identity(snapshot), observation_digest(snapshot, entities))
            if self.context is not None and context != self.context:
                self.clear()
            self.context = context
            return entities
        except (ReferenceRejected, ValueError, TypeError, IndexError):
            self.clear()
            raise

    def issue_candidates(self, snapshot, receipt_sha256):
        entities = self.observe(snapshot)
        request = snapshot.get('bridgeRequestId')
        if not token(request) or not token(receipt_sha256, 64):
            self.clear()
            raise ReferenceRejected('entity_candidate_receipt_invalid')
        expiry, seconds = remaining(snapshot.get('timestampUtc'))
        self.purge()
        if len(entities) > self.MAX_ENTITIES:
            self.clear()
            raise ReferenceRejected('entity_candidate_inventory_limit')
        if len(self.candidates) + len(entities) > self.MAX_ENTITIES or len(self.documents) >= self.MAX_DOCUMENTS:
            context = self.context
            self.clear()
            self.context = context
        document = uuid.uuid4().hex
        self.documents[document] = {'expires': self.clock() + seconds, 'parameters': {
            'inspectionRequestId': request, 'inspectionSha256': receipt_sha256, 'expiresUtc': expiry}}
        result = copy.deepcopy(snapshot)
        result.update(state='entity_candidates_inspected', candidateDocumentHandle=document,
                      candidateLifetimeSeconds=int(seconds), entitySetQualification=self.QUALIFICATION)
        for entity in result['entities']:
            handle = uuid.uuid4().hex
            key = (entity['nativeTypeCode'], entity['nativeId'])
            self.candidates[handle] = {'document': document, 'key': key}
            entity['candidateEntityHandle'] = handle
            entity['entitySetSupported'] = key[0] in (40, 21, 59)
        return result

    def prepare_parameters(self, document, handles):
        parameters = self.document_parameters(document)
        if not isinstance(handles, list) or not 1 <= len(handles) <= 128 or not all(token(x) for x in handles) or len(set(handles)) != len(handles):
            raise ReferenceRejected('candidate_entities_invalid')
        keys = []
        for handle in handles:
            candidate = self.candidates.get(handle)
            if candidate is None or candidate['document'] != document:
                raise ReferenceRejected('candidate_entity_unknown_or_wrong_document')
            if candidate['key'][0] not in (40, 21, 59):
                raise ReferenceRejected('entity_set_type_unsupported')
            keys.append(list(candidate['key']))
        return {**parameters, 'entityKeys': keys}

    def document_parameters(self, document):
        self.purge()
        record = self.documents.get(document)
        if record is None:
            raise ReferenceRejected('candidate_document_unknown_or_expired')
        return copy.deepcopy(record['parameters'])

    def issue_set(self, result):
        if result.get('state') != 'entity_set_prepared_observed' or not token(result.get('entitySetId')) or not token(result.get('entitySetSha256'), 64):
            raise ReferenceRejected('entity_set_receipt_invalid')
        try:
            expiry = datetime.datetime.fromisoformat(result['expiresUtc'].replace('Z', '+00:00'))
            if expiry.tzinfo is None:
                raise ValueError()
            seconds = (expiry - datetime.datetime.now(datetime.timezone.utc)).total_seconds()
            if not 0 < seconds <= 300:
                raise ValueError()
        except (ValueError, TypeError, AttributeError, KeyError):
            raise ReferenceRejected('entity_set_time_invalid_or_expired')
        return self._issue_set(result, seconds)

    def _issue_set(self, result, seconds):
        self.purge()
        if len(self.sets) >= self.MAX_SETS:
            self.sets.clear()
        handle = uuid.uuid4().hex
        self.sets[handle] = {'expires': self.clock() + seconds, 'parameters': {
            'entitySetId': result['entitySetId'], 'entitySetSha256': result['entitySetSha256']}}
        return {**result, 'entitySetHandle': handle, 'entitySetQualification': self.QUALIFICATION}

    def validation_parameters(self, handle):
        self.purge()
        record = self.sets.get(handle)
        if record is None:
            raise ReferenceRejected('entity_set_handle_unknown_or_expired')
        return copy.deepcopy(record['parameters'])
