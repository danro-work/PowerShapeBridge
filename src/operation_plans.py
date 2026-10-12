"""Local experimental plans and durable journals. There is no CAD executor here."""
import copy
import datetime
import hashlib
import json
import math
import os
from pathlib import Path
import time
import uuid

from entity_references import ReferenceRejected, identity, inventory, observation_digest
from entity_sets import token

QUALIFICATION = ('Local observed plan only; no CAD command or execution authorization. '
                 'Execution requires a qualified worker guard in its own COM context, '
                 'owned working copy, independent checkpoint and postcondition checks. '
                 'Observed properties do not certify full topology or persistent identity.')
OBSERVED_FIELDS = ('processId', 'processStartedUtc', 'windowsSessionId', 'productVersion', 'model',
                   'entityCount', 'entities', 'levelsContainingEntities', 'contextBefore', 'contextAfter',
                   'selectionBefore', 'selectionAfter', 'workplanes', 'lengthUnitsRaw', 'lengthUnitNameRaw',
                   'lengthUnitFactorRaw', 'areaUnitNameRaw', 'areaUnitFactorRaw', 'volumeUnitNameRaw',
                   'volumeUnitFactorRaw', 'generalToleranceRaw')
TRANSITIONS = {
    None: {'prepared'},
    'prepared': {'execution_claimed', 'rejected_before_change'},
    'execution_claimed': {'command_started', 'rejected_before_change', 'outcome_unknown'},
    'command_started': {'command_returned', 'outcome_unknown'},
    'command_returned': {'succeeded_verified', 'executed_unverified', 'outcome_unknown', 'recovery_required'},
    'outcome_unknown': {'succeeded_verified', 'recovery_required'},
    'executed_unverified': {'succeeded_verified', 'recovery_required'},
    'recovery_required': set(), 'succeeded_verified': set(), 'rejected_before_change': set(),
}


def reject(message):
    raise ReferenceRejected(message)


def utc(value):
    try:
        parsed = datetime.datetime.fromisoformat(value.replace('Z', '+00:00'))
        if parsed.tzinfo is None:
            raise ValueError()
        return parsed.astimezone(datetime.timezone.utc)
    except (ValueError, TypeError, AttributeError):
        reject('operation_time_invalid')


def validate_arguments(args):
    if not isinstance(args, dict) or set(args) != {'operation', 'entitySetHandles', 'parameters'}:
        reject('operation_plan_arguments_invalid')
    kind, handles, parameters = args['operation'], args['entitySetHandles'], args['parameters']
    if not isinstance(kind, str) or kind not in ('translate_entities', 'intersect_entity_sets'):
        reject('operation_kind_unsupported')
    count = 1 if kind == 'translate_entities' else 2
    if not isinstance(handles, list) or len(handles) != count or not all(token(x) for x in handles) or len(set(handles)) != count:
        reject('operation_sets_invalid')
    if not isinstance(parameters, dict):
        reject('operation_parameters_invalid')
    if kind == 'translate_entities':
        if set(parameters) != {'vector', 'coordinateFrame', 'lengthUnits', 'mode'} or parameters['coordinateFrame'] != 'world' or parameters['lengthUnits'] != 'document_raw' or parameters['mode'] != 'move':
            reject('translation_parameters_invalid')
        vector = parameters['vector']
        if not isinstance(vector, list) or len(vector) != 3 or any(type(x) not in (int, float) or abs(x) > 1000000 or not math.isfinite(x) for x in vector) or not any(vector):
            reject('translation_vector_invalid')
    elif set(parameters) != {'offset'} or type(parameters['offset']) not in (int, float) or parameters['offset'] != 0:
        reject('intersection_offset_not_qualified')


def read_bytes(path, limit):
    with path.open('rb') as stream:
        raw = stream.read(limit + 1)
    if len(raw) > limit:
        reject('operation_artifact_size_limit')
    return raw


def verified_json(path, expected, limit):
    raw = read_bytes(path, limit)
    if hashlib.sha256(raw).hexdigest() != expected:
        reject('operation_artifact_changed')
    result = json.loads(raw.decode('utf-8-sig'))
    if not isinstance(result, dict):
        reject('operation_artifact_invalid')
    return result


def freeze(path, value):
    raw = json.dumps(value, ensure_ascii=False, allow_nan=False, indent=2).encode('utf-8')
    with path.open('xb') as stream:
        stream.write(raw)
        stream.flush()
        os.fsync(stream.fileno())
    return hashlib.sha256(raw).hexdigest()


def read_set(root, handle_parameters, now):
    if set(handle_parameters) != {'entitySetId', 'entitySetSha256'} or not token(handle_parameters['entitySetId']) or not token(handle_parameters['entitySetSha256'], 64):
        reject('operation_set_identity_invalid')
    plan = verified_json(root / 'entitysets' / (handle_parameters['entitySetId'] + '.json'), handle_parameters['entitySetSha256'], 65536)
    if set(plan) != {'inspectionRequestId', 'inspectionSha256', 'expiresUtc', 'entityKeys'} or not token(plan['inspectionRequestId']) or not token(plan['inspectionSha256'], 64):
        reject('operation_set_artifact_invalid')
    source = verified_json(root / 'responses' / (plan['inspectionRequestId'] + '.json'), plan['inspectionSha256'], 16000000)
    if 'bridgeRequestId' in source and source['bridgeRequestId'] != plan['inspectionRequestId']:
        reject('operation_source_identity_invalid')
    entities = inventory(source)
    if type(source.get('entityCount')) is not int or source.get('state') != 'succeeded_verified' or source.get('modelScope') != 'active_document_read_only':
        reject('operation_source_not_verified')
    keys = plan['entityKeys']
    if not isinstance(keys, list) or not 1 <= len(keys) <= 128 or any(not isinstance(k, list) or len(k) != 2 or any(type(x) is not int for x in k) or k[0] not in (40, 21, 59) or k[1] < 0 for k in keys):
        reject('operation_set_keys_invalid')
    if len(set(map(tuple, keys))) != len(keys) or any(tuple(k) not in entities for k in keys):
        reject('operation_set_keys_ambiguous_or_absent')
    issued, expiry = utc(source.get('timestampUtc')), utc(plan['expiresUtc'])
    if not issued <= now + datetime.timedelta(seconds=5) or not now < expiry <= issued + datetime.timedelta(seconds=300):
        reject('operation_set_expired')
    return plan, source, (identity(source), observation_digest(source, entities)), expiry


def source_observations(source):
    if any(field not in source for field in OBSERVED_FIELDS):
        reject('operation_source_observations_incomplete')
    result = {field: copy.deepcopy(source[field]) for field in OBSERVED_FIELDS}
    result['entities'].sort(key=lambda e: (e['nativeTypeCode'], e['nativeId']))
    return result


def journal_events(root, operation_id):
    if not token(operation_id):
        reject('operation_identity_invalid')
    folder = root / 'operations' / operation_id
    paths = sorted(folder.glob('*.json'))
    if not 1 <= len(paths) <= 64:
        reject('operation_journal_missing_or_limit')
    previous, last_state, plan_sha, events = None, None, None, []
    for number, path in enumerate(paths):
        if path.name != '%06d.json' % number:
            reject('operation_journal_sequence_invalid')
        raw = read_bytes(path, 65536)
        event = json.loads(raw.decode('utf-8'))
        if not isinstance(event, dict) or set(event) != {'schemaVersion', 'operationId', 'planSha256', 'sequence', 'previousSha256', 'state', 'timestampUtc', 'details'}:
            reject('operation_journal_event_invalid')
        if type(event['schemaVersion']) is not int or event['schemaVersion'] != 1 or event['operationId'] != operation_id or type(event['sequence']) is not int or event['sequence'] != number or event['previousSha256'] != previous or not token(event['planSha256'], 64):
            reject('operation_journal_chain_invalid')
        if plan_sha is not None and event['planSha256'] != plan_sha:
            reject('operation_journal_plan_changed')
        validate_transition(last_state, event['state'], event['details'])
        utc(event['timestampUtc'])
        previous, last_state, plan_sha = hashlib.sha256(raw).hexdigest(), event['state'], event['planSha256']
        events.append(event)
    # An interrupted claim/start/return cannot prove that CAD did or did not change.
    locked = (folder / 'writer.lock').exists()
    state = 'outcome_unknown' if locked or last_state in {'execution_claimed', 'command_started', 'command_returned'} else last_state
    return {'operationId': operation_id, 'planSha256': plan_sha, 'recordedState': last_state,
            'state': state, 'eventCount': len(events), 'journalSha256': previous,
            'retryAllowed': False, 'writerLockPresent': locked, 'events': events}


def validate_transition(previous, state, details):
    if not isinstance(state, str) or state not in TRANSITIONS.get(previous, set()) or not isinstance(details, dict):
        reject('operation_journal_transition_invalid')
    if len(json.dumps(details, ensure_ascii=False, allow_nan=False).encode('utf-8')) > 8192:
        reject('operation_journal_details_limit')
    if state in {'succeeded_verified', 'recovery_required', 'rejected_before_change'}:
        evidence = details.get('evidence')
        if not isinstance(evidence, list) or not evidence or len(evidence) > 16 or any(not isinstance(e, dict) or set(e) != {'requestId', 'sha256'} or not token(e['requestId']) or not token(e['sha256'], 64) for e in evidence):
            reject('operation_independent_evidence_required')


def append_event(root, operation_id, plan_sha, state, details, expected_count):
    """Internal future-executor primitive; never exposed as a public state setter."""
    if not token(operation_id) or not token(plan_sha, 64) or type(expected_count) is not int or not 0 <= expected_count < 64:
        reject('operation_journal_append_invalid')
    folder = root / 'operations' / operation_id
    folder.mkdir(parents=True, exist_ok=True)
    # A crashed writer leaves this marker. No automatic removal or command retry.
    lock = folder / 'writer.lock'
    with lock.open('xb') as stream:
        stream.write(b'locked'); stream.flush(); os.fsync(stream.fileno())
    try:
        paths = list(folder.glob('*.json'))
        previous, prior = None, None
        if paths:
            journal = journal_events(root, operation_id)
            if journal['planSha256'] != plan_sha:
                reject('operation_journal_plan_changed')
            prior, previous = journal['recordedState'], journal['journalSha256']
        if len(paths) != expected_count:
            reject('operation_journal_concurrent_or_repeated_append')
        validate_transition(prior, state, details)
        event = {'schemaVersion': 1, 'operationId': operation_id, 'planSha256': plan_sha,
                 'sequence': expected_count, 'previousSha256': previous, 'state': state,
                 'timestampUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'details': copy.deepcopy(details)}
        return freeze(folder / ('%06d.json' % expected_count), event)
    finally:
        lock.unlink()


class OperationPlanStore:
    MAX_PLANS = 128

    def __init__(self, root, entity_sets, clock=time.monotonic):
        self.root, self.entity_sets, self.clock = Path(root), entity_sets, clock
        self.plans = {}

    def clear(self):
        self.plans.clear()

    def prepare(self, args):
        validate_arguments(args)
        now = datetime.datetime.now(datetime.timezone.utc)
        sets = [read_set(self.root, self.entity_sets.validation_parameters(h), now) for h in args['entitySetHandles']]
        context = sets[0][2]
        observed = source_observations(sets[0][1])
        if any(s[2] != context or source_observations(s[1]) != observed for s in sets) or self.entity_sets.context != context:
            reject('operation_sets_document_or_observations_differ')
        if args['operation'] == 'intersect_entity_sets':
            primary, secondary = sets[0][0]['entityKeys'], sets[1][0]['entityKeys']
            if any(k[0] not in (40, 21) for k in primary) or any(k[0] != 21 for k in secondary):
                reject('intersection_set_type_unsupported')
            if set(map(tuple, primary)) & set(map(tuple, secondary)):
                reject('intersection_sets_overlap')
        operation_id = uuid.uuid4().hex
        expiry = min(s[3] for s in sets)
        source = sets[0][1]
        plan = {'schemaVersion': 1, 'operationId': operation_id, 'operation': args['operation'],
                'parameters': copy.deepcopy(args['parameters']), 'preparedUtc': now.isoformat(),
                'expiresUtc': expiry.isoformat(), 'executionEnabled': False,
                'sourceIdentity': {k: copy.deepcopy(source[k]) for k in ('processId', 'processStartedUtc', 'windowsSessionId', 'productVersion', 'model')},
                'observedPropertiesSha256': hashlib.sha256(json.dumps(observed, sort_keys=True, ensure_ascii=False, allow_nan=False, separators=(',', ':')).encode('utf-8')).hexdigest(),
                'observationDigestEncoding': 'Python JSON sorted keys; distinct from native C# digest',
                'lengthUnitsRaw': copy.deepcopy(source.get('lengthUnitsRaw')),
                'lengthUnitFactorRaw': copy.deepcopy(source.get('lengthUnitFactorRaw')),
                'entitySets': [dict(self.entity_sets.validation_parameters(h), **copy.deepcopy(s[0])) for h, s in zip(args['entitySetHandles'], sets)],
                'requiredBeforeExecution': ['qualified_operation', 'owned_working_copy', 'independent_checkpoint', 'guard_in_executor_com_context', 'postconditions'],
                'qualification': QUALIFICATION}
        folder = self.root / 'operationplans'
        folder.mkdir(exist_ok=True)
        sha = None
        try:
            sha = freeze(folder / (operation_id + '.json'), plan)
            append_event(self.root, operation_id, sha, 'prepared', {'cadMutationAttempted': False, 'executionEnabled': False}, 0)
            if expiry <= datetime.datetime.now(datetime.timezone.utc):
                reject('operation_plan_expired_during_preparation')
        except Exception as error:
            failure = ReferenceRejected('operation_plan_artifacts_incomplete_or_expired')
            failure.operation_id, failure.plan_sha = operation_id, sha
            raise failure from error
        self.plans = {k: v for k, v in self.plans.items() if v['expires'] > self.clock()}
        if len(self.plans) >= self.MAX_PLANS:
            self.clear()
        handle = uuid.uuid4().hex
        self.plans[handle] = {'id': operation_id, 'sha': sha, 'context': context,
                              'expires': self.clock() + (expiry - now).total_seconds(),
                              'setHandles': copy.deepcopy(args['entitySetHandles'])}
        return {'state': 'operation_plan_prepared_local', 'operationId': operation_id,
                'planSha256': sha, 'operationPlanHandle': handle, 'plan': plan,
                'cadMutationAttempted': False, 'qualification': QUALIFICATION}

    def inspect(self, handle):
        record = self.plans.get(handle)
        if record is None or record['expires'] <= self.clock() or record['context'] != self.entity_sets.context:
            self.clear()
            reject('operation_plan_handle_unknown_or_expired')
        now = datetime.datetime.now(datetime.timezone.utc)
        for set_handle in record['setHandles']:
            read_set(self.root, self.entity_sets.validation_parameters(set_handle), now)
        plan = verified_json(self.root / 'operationplans' / (record['id'] + '.json'), record['sha'], 65536)
        if utc(plan['expiresUtc']) <= now:
            reject('operation_plan_expired')
        return {'state': 'operation_plan_inspected_local', 'operationId': record['id'],
                'planSha256': record['sha'], 'plan': plan,
                'journal': journal_events(self.root, record['id']), 'cadMutationAttempted': False,
                'qualification': QUALIFICATION}
