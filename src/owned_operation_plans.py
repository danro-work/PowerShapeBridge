"""Connection-local coupled plan/binding leases; no execution or CAD commands."""
import copy
import datetime
import hashlib
import json
from pathlib import Path
import time
import uuid

from entity_references import ReferenceRejected
from entity_sets import token
from operation_plans import journal_events, read_set, source_observations, utc, verified_json, validate_arguments


class OwnedOperationStore:
    MAX_PACKETS = 128
    FIELDS = {'schemaVersion', 'ownedOperationId', 'operationPlanId', 'operationPlanSha256',
              'bindingId', 'bindingSha256', 'preparedUtc', 'expiresUtc', 'executionEnabled',
              'nativeObservedPropertiesSha256', 'windowContextSha256', 'workingSha256', 'copyId', 'ownershipSha256'}

    def __init__(self, root, plans, bindings, clock=time.monotonic):
        self.root, self.plans, self.bindings, self.clock = Path(root), plans, bindings, clock
        self.packets = {}

    def clear(self):
        self.packets.clear()

    def prepare_parameters(self, plan_handle, binding_handle):
        local = self.plans.inspect(plan_handle)
        binding_args = self.bindings.validation_parameters(binding_handle)
        binding = verified_json(self.root / 'documentbindings' / (binding_args['bindingId'] + '.json'), binding_args['bindingSha256'], 65536)
        plan = local['plan']; now = datetime.datetime.now(datetime.timezone.utc)
        if local['journal']['state'] != 'prepared' or local['journal']['writerLockPresent']:
            raise ReferenceRejected('owned_operation_plan_journal_not_prepared')
        sets = [read_set(self.root, {k: entry[k] for k in ('entitySetId', 'entitySetSha256')}, now) for entry in plan['entitySets']]
        validate_arguments({'operation': plan['operation'], 'entitySetHandles': [entry['entitySetId'] for entry in plan['entitySets']], 'parameters': plan['parameters']})
        observed = source_observations(sets[0][1])
        expected_sha = hashlib.sha256(json.dumps(observed, sort_keys=True, ensure_ascii=False, allow_nan=False, separators=(',', ':')).encode('utf-8')).hexdigest()
        identity = {k: observed[k] for k in ('processId', 'processStartedUtc', 'windowsSessionId', 'productVersion', 'model')}
        binding_source = verified_json(self.root / 'responses' / (binding['inspectionRequestId'] + '.json'), binding['inspectionSha256'], 16000000)
        if plan['executionEnabled'] is not False or plan['sourceIdentity'] != identity or plan['observedPropertiesSha256'] != expected_sha or source_observations(binding_source) != observed:
            raise ReferenceRejected('owned_operation_plan_binding_observations_differ')
        for entry, source in zip(plan['entitySets'], sets):
            if {k: entry[k] for k in source[0]} != source[0] or source_observations(source[1]) != observed:
                raise ReferenceRejected('owned_operation_set_receipt_differs')
        if utc(plan['expiresUtc']) != min(s[3] for s in sets) or plan['lengthUnitsRaw'] != observed['lengthUnitsRaw'] or plan['lengthUnitFactorRaw'] != observed['lengthUnitFactorRaw']:
            raise ReferenceRejected('owned_operation_plan_units_or_lifetime_differs')
        return {'operationPlanId': local['operationId'], 'operationPlanSha256': local['planSha256'], **binding_args}

    def receipt(self, result, parameters=None):
        packet_id, sha = result.get('ownedOperationId'), result.get('ownedOperationSha256')
        if not token(packet_id) or not token(sha, 64):
            raise ReferenceRejected('owned_operation_native_identity_invalid')
        packet = verified_json(self.root / 'ownedoperations' / (packet_id + '.json'), sha, 65536)
        if set(packet) != self.FIELDS or packet != result.get('ownedOperation') or packet['schemaVersion'] != 1 or type(packet['schemaVersion']) is not int or packet['ownedOperationId'] != packet_id or packet['executionEnabled'] is not False:
            raise ReferenceRejected('owned_operation_native_packet_invalid')
        for key in ('nativeObservedPropertiesSha256', 'windowContextSha256', 'workingSha256', 'ownershipSha256'):
            if not token(packet[key], 64): raise ReferenceRejected('owned_operation_native_proof_invalid')
        if result.get('executionEnabled') is not False or result.get('geometryMutationAttempted') is not False or result.get('contextChangeAttempted') is not False or result.get('cadSaveAttempted') is not False or result.get('expiresUtc') != packet['expiresUtc']:
            raise ReferenceRejected('owned_operation_native_execution_policy_differs')
        if parameters is not None and any(packet[k] != v for k, v in parameters.items()):
            raise ReferenceRejected('owned_operation_native_dependencies_differ')
        for key in ('operationPlanId', 'bindingId', 'copyId'):
            if not token(packet[key]): raise ReferenceRejected('owned_operation_native_dependencies_invalid')
        for key in ('operationPlanSha256', 'bindingSha256'):
            if not token(packet[key], 64): raise ReferenceRejected('owned_operation_native_dependencies_invalid')
        binding = verified_json(self.root / 'documentbindings' / (packet['bindingId'] + '.json'), packet['bindingSha256'], 65536)
        if any(packet[p] != binding[b] for p, b in (('copyId', 'copyId'), ('ownershipSha256', 'ownershipSha256'), ('windowContextSha256', 'windowContextSha256'), ('nativeObservedPropertiesSha256', 'observedPropertiesSha256'))):
            raise ReferenceRejected('owned_operation_native_binding_differs')
        journal = journal_events(self.root, packet_id)
        if journal['state'] != 'prepared' or journal['writerLockPresent'] or journal['eventCount'] != 1 or journal['planSha256'] != sha:
            raise ReferenceRejected('owned_operation_native_journal_not_prepared')
        return packet

    def issue(self, result, parameters, plan_handle, binding_handle):
        if result.get('state') != 'owned_operation_prepared_observed' or result.get('bridgeRequestId') != result.get('ownedOperationId'):
            raise ReferenceRejected('owned_operation_preparation_receipt_invalid')
        if self.prepare_parameters(plan_handle, binding_handle) != parameters:
            raise ReferenceRejected('owned_operation_dependency_lost_during_preparation')
        packet = self.receipt(result, parameters)
        seconds = min((utc(packet['expiresUtc']) - datetime.datetime.now(datetime.timezone.utc)).total_seconds(),
                      self.plans.plans[plan_handle]['expires'] - self.plans.clock(),
                      self.bindings.bindings[binding_handle]['expires'] - self.bindings.clock())
        if not 0 < seconds <= 300: raise ReferenceRejected('owned_operation_expired')
        self.packets = {k: v for k, v in self.packets.items() if v['expires'] > self.clock()}
        if len(self.packets) >= self.MAX_PACKETS: self.clear()
        handle = uuid.uuid4().hex
        self.packets[handle] = {'expires': self.clock() + seconds, 'plan': plan_handle, 'binding': binding_handle,
                                'source': copy.deepcopy(parameters), 'parameters': {'ownedOperationId': result['ownedOperationId'], 'ownedOperationSha256': result['ownedOperationSha256']}}
        return {**result, 'ownedOperationHandle': handle, 'ownedOperationLifetimeSeconds': int(seconds)}

    def validation_parameters(self, handle):
        record = self.packets.get(handle)
        if record is None or record['expires'] <= self.clock():
            self.packets.pop(handle, None)
            raise ReferenceRejected('owned_operation_handle_unknown_or_expired')
        if self.prepare_parameters(record['plan'], record['binding']) != record['source']:
            self.clear(); raise ReferenceRejected('owned_operation_dependencies_changed')
        return copy.deepcopy(record['parameters'])

    def checked_validation(self, result, parameters):
        if any(result.get(k) != v for k, v in parameters.items()):
            raise ReferenceRejected('owned_operation_validation_identity_differs')
        self.receipt(result)
