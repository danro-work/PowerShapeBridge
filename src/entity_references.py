"""Ephemeral read references. They are never an authorization for CAD mutation."""
import copy
import hashlib
import json
import ntpath
import time
import uuid


class ReferenceRejected(ValueError):
    def __init__(self, code):
        self.code = code
        super().__init__(code)


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, ensure_ascii=False,
                                    allow_nan=False, separators=(',', ':')).encode()).hexdigest()


def identity(snapshot):
    if snapshot.get('state') != 'succeeded_verified':
        raise ReferenceRejected('fresh_inspection_failed')
    model = snapshot.get('model')
    if not isinstance(model, dict):
        raise ReferenceRejected('document_identity_unavailable')
    for value in (snapshot.get('processId'), snapshot.get('windowsSessionId'), model.get('id')):
        if type(value) is not int or value < 0:
            raise ReferenceRejected('document_identity_unavailable')
    if not isinstance(snapshot.get('processStartedUtc'), str) or not snapshot['processStartedUtc']:
        raise ReferenceRejected('process_identity_unavailable')
    if not isinstance(model.get('path'), str) or not isinstance(model.get('name'), str):
        raise ReferenceRejected('document_identity_unavailable')
    return {'processId': snapshot['processId'], 'processStartedUtc': snapshot['processStartedUtc'],
            'windowsSessionId': snapshot['windowsSessionId'], 'modelId': model['id'],
            'modelName': model['name'], 'modelPath': ntpath.normcase(ntpath.normpath(model['path']))}


def inventory(snapshot):
    entities = snapshot.get('entities')
    if not isinstance(entities, list) or snapshot.get('entityCount') != len(entities):
        raise ReferenceRejected('incomplete_inventory')
    result = {}
    for entity in entities:
        if not isinstance(entity, dict):
            raise ReferenceRejected('incomplete_inventory')
        key = (entity.get('nativeTypeCode'), entity.get('nativeId'))
        if any(type(x) is not int or x < 0 for x in key) or key in result:
            raise ReferenceRejected('ambiguous_entity_identity')
        if not isinstance(entity.get('type'), str) or not isinstance(entity.get('name'), str):
            raise ReferenceRejected('incomplete_inventory')
        if type(entity.get('levelNumber')) is not int:
            raise ReferenceRejected('incomplete_inventory')
        result[key] = entity
    return result


def observation_digest(snapshot, entities):
    return digest({'entities': [entities[k] for k in sorted(entities)],
                   'contextWorkplane': snapshot.get('contextBefore', [None, None])[1],
                   'units': {key: snapshot.get(key) for key in (
                       'lengthUnitsRaw', 'lengthUnitNameRaw', 'lengthUnitFactorRaw',
                       'areaUnitNameRaw', 'areaUnitFactorRaw', 'volumeUnitNameRaw',
                       'volumeUnitFactorRaw', 'generalToleranceRaw')},
                   'workplanes': snapshot.get('workplanes')})


class ReferenceStore:
    TTL_SECONDS = 300
    MAX_ENTITIES = 4096
    QUALIFICATION = ('Read observations only; not a full geometry revision or persistent identity. '
                     'Unobserved close/reopen or ID reuse cannot be ruled out. '
                     'These references are not accepted by mutation tools.')

    def __init__(self, clock=time.monotonic):
        self.clock = clock
        self.documents = {}
        self.entities = {}
        self.observed_context = None

    def clear(self):
        self.documents.clear()
        self.entities.clear()
        self.observed_context = None

    def purge(self):
        now = self.clock()
        self.documents = {k: v for k, v in self.documents.items() if v['expires'] > now}
        self.entities = {k: v for k, v in self.entities.items() if v['document'] in self.documents}

    def observe(self, snapshot):
        try:
            model_identity = identity(snapshot)
            entities = inventory(snapshot)
            fingerprint = observation_digest(snapshot, entities)
        except (ReferenceRejected, ValueError, TypeError, IndexError):
            self.clear()
            raise
        context = digest(model_identity), fingerprint
        if self.observed_context is not None and self.observed_context != context:
            self.clear()
        self.observed_context = context
        return model_identity, entities, fingerprint

    def issue(self, snapshot):
        model_identity, entities, fingerprint = self.observe(snapshot)
        if len(entities) > self.MAX_ENTITIES:
            self.clear()
            raise ReferenceRejected('reference_inventory_limit')
        self.purge()
        if len(self.entities) + len(entities) > self.MAX_ENTITIES or len(self.documents) >= 128:
            self.clear()
            self.observed_context = digest(model_identity), fingerprint
        document_token = uuid.uuid4().hex
        self.documents[document_token] = {'identity': model_identity, 'fingerprint': fingerprint,
                                          'expires': self.clock() + self.TTL_SECONDS}
        result = copy.deepcopy(snapshot)
        result.update(state='active_model_inspected', documentReference=document_token,
                      observedPropertiesSha256=fingerprint, referenceLifetimeSeconds=self.TTL_SECONDS,
                      referenceQualification=self.QUALIFICATION)
        for entity in result['entities']:
            token = uuid.uuid4().hex
            self.entities[token] = {'document': document_token,
                                    'key': (entity['nativeTypeCode'], entity['nativeId'])}
            entity['entityReference'] = token
        return result

    def require(self, document_token, entity_tokens):
        self.purge()
        document = self.documents.get(document_token)
        if document is None:
            raise ReferenceRejected('document_reference_unknown_or_expired')
        records = []
        for token in entity_tokens:
            record = self.entities.get(token)
            if record is None or record['document'] != document_token:
                raise ReferenceRejected('entity_reference_unknown_or_wrong_document')
            records.append(record)
        return document, records

    def resolve(self, document_token, entity_tokens, snapshot):
        document, records = self.require(document_token, entity_tokens)
        # Retain expected observations before observe() invalidates a changed context.
        expected_identity = document['identity']
        expected_fingerprint = document['fingerprint']
        model_identity, entities, fingerprint = self.observe(snapshot)
        if model_identity != expected_identity:
            raise ReferenceRejected('document_context_changed')
        if fingerprint != expected_fingerprint:
            raise ReferenceRejected('observed_properties_changed')
        self.require(document_token, entity_tokens)  # Recheck expiry after the CAD read.
        resolved = []
        for token, record in zip(entity_tokens, records):
            entity = copy.deepcopy(entities[record['key']])
            entity['entityReference'] = token
            resolved.append(entity)
        return {'state': 'entity_references_resolved_observed', 'documentReference': document_token,
                'entities': resolved, 'model': snapshot['model'], 'processId': snapshot['processId'],
                'observedPropertiesSha256': fingerprint,
                'inspectionRequestId': snapshot.get('bridgeRequestId'),
                'warnings': snapshot.get('warnings', []), 'referenceQualification': self.QUALIFICATION,
                'cadMutationAttempted': False}
