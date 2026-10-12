"""Independent structural oracle for Framework target artifacts; test-only."""
import hashlib
import json
import os
import re


FIELDS = {'schemaVersion', 'targetId', 'operation', 'bridgeVersion', 'preparedUtc', 'expiresUtc', 'executionEnabled',
          'bindingId', 'bindingSha256', 'copyId', 'ownershipSha256', 'inspectionRequestId', 'inspectionSha256',
          'nativeObservedPropertiesSha256', 'beforeModel', 'workingPath', 'workingSha256', 'checkpointPath',
          'checkpointSha256', 'resultPath', 'directoryIdentity'}


def require(condition):
    if not condition:
        raise ValueError('Save target structural proof rejected.')


def file_value(path, digest):
    raw = path.read_bytes()
    require(len(raw) <= 16777216 and hashlib.sha256(raw).hexdigest() == digest)
    return json.loads(raw.decode('utf-8-sig'))


def path_key(path):
    return os.path.normcase(os.path.abspath(path.replace('/', '\\')))


def same_json(left, right):
    # Python treats True == 1 and False == 0. JSON boolean/number types are
    # distinct, so ordinary dictionary equality is insufficient for this oracle.
    if isinstance(left, bool) or isinstance(right, bool):
        return type(left) is type(right) and left == right
    if isinstance(left, dict) and isinstance(right, dict):
        return set(left) == set(right) and all(same_json(value, right[key]) for key, value in left.items())
    if isinstance(left, list) and isinstance(right, list):
        return len(left) == len(right) and all(same_json(a, b) for a, b in zip(left, right))
    return left == right


def verify(root, receipt):
    ident, digest = receipt['targetId'], receipt['targetSha256']
    require(isinstance(ident, str) and re.fullmatch('[0-9a-f]{32}', ident) and re.fullmatch('[0-9a-f]{64}', digest))
    plan = file_value(root / 'savedresults' / ident / 'target.json', digest)
    require(set(plan) == FIELDS and same_json(plan, receipt['target']) and type(plan['schemaVersion']) is int and plan['schemaVersion'] == 1)
    require(plan['targetId'] == ident and plan['executionEnabled'] is False and plan['operation'] == 'save_owned_document_as')
    for field in ('bindingId', 'copyId', 'inspectionRequestId'):
        require(isinstance(plan[field], str) and re.fullmatch('[0-9a-f]{32}', plan[field]))
    for field in ('bindingSha256', 'ownershipSha256', 'inspectionSha256', 'nativeObservedPropertiesSha256', 'workingSha256', 'checkpointSha256'):
        require(isinstance(plan[field], str) and re.fullmatch('[0-9a-f]{64}', plan[field]))
    directory = plan['directoryIdentity']
    require(set(directory) == {'volumeSerial', 'fileIndex', 'creationTime'})
    for field, length in (('volumeSerial', 8), ('fileIndex', 16), ('creationTime', 16)):
        require(isinstance(directory[field], str) and re.fullmatch('[0-9a-f]{%d}' % length, directory[field]))
    require(path_key(plan['resultPath']) == path_key(str(root / 'savedresults' / ident / ('Result_' + ident + '.psmodel'))))
    binding = file_value(root / 'documentbindings' / (plan['bindingId'] + '.json'), plan['bindingSha256'])
    require(type(binding['schemaVersion']) is int and binding['schemaVersion'] == 1 and binding['executionEnabled'] is False)
    for field in ('copyId', 'ownershipSha256', 'inspectionRequestId', 'inspectionSha256', 'expiresUtc'):
        require(binding[field] == plan[field])
    require(binding['observedPropertiesSha256'] == plan['nativeObservedPropertiesSha256'])
    source = file_value(root / 'responses' / (plan['inspectionRequestId'] + '.json'), plan['inspectionSha256'])
    require(source['model'] == plan['beforeModel'] and path_key(source['model']['path']) == path_key(plan['workingPath']))
    copy = root / 'workingcopies' / plan['copyId']
    ownership = file_value(copy / 'ownership.json', plan['ownershipSha256'])
    require(type(ownership['schemaVersion']) is int and ownership['schemaVersion'] in (1, 2))
    require(ownership['copyId'] == plan['copyId'] and ownership['scope'] == 'saved_disk_bytes_only' and ownership['executionEnabled'] is False)
    name = 'Working.psmodel' if ownership['schemaVersion'] == 1 else 'Working_' + plan['copyId'] + '.psmodel'
    require(path_key(plan['workingPath']) == path_key(str(copy / name)) == path_key(ownership['working']['path']))
    require(path_key(plan['checkpointPath']) == path_key(str(copy / 'checkpoint/Before.psmodel')))
    require(ownership['checkpoint']['sha256'] == plan['checkpointSha256'])
