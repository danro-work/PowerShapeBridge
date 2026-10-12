"""Test-only independent audit of actual Framework prepared saved-byte copies."""
import hashlib
import re
from pathlib import Path

from save_target_fixture_oracle import file_value, path_key, require, same_json
from saved_result_fixture_oracle import verify as saved_proof


FIELDS = {'schemaVersion', 'reopenId', 'operation', 'bridgeVersion', 'preparedUtc', 'expiresUtc', 'executionEnabled',
          'verificationRequestId', 'verificationReceiptSha256', 'saveOperationId', 'targetId', 'targetSha256',
          'savedResult', 'seed', 'copyId', 'ownershipSha256', 'workingPath', 'workingSha256', 'checkpointPath',
          'checkpointSha256', 'directoryIdentity'}


def identity(value):
    require(set(value) == {'volumeSerial', 'fileIndex', 'creationTime'})
    for key, count in [('volumeSerial', 8), ('fileIndex', 16), ('creationTime', 16)]:
        require(isinstance(value[key], str) and re.fullmatch('[0-9a-f]{%d}' % count, value[key]))


def verify(root, receipt, unclaimed=True):
    ident = receipt['reopenId']
    require(isinstance(ident, str) and re.fullmatch('[0-9a-f]{32}', ident))
    plan = file_value(root / 'savedreopens' / ident / 'reopen.json', receipt['reopenSha256'])
    require(set(plan) == FIELDS and same_json(plan, receipt['reopen']))
    require(type(plan['schemaVersion']) is int and plan['schemaVersion'] == 1 and plan['executionEnabled'] is False)
    require(plan['operation'] == 'reopen_owned_saved_result' and plan['copyId'] == plan['reopenId'] == ident)
    result = file_value(root / 'responses' / (plan['verificationRequestId'] + '.json'), plan['verificationReceiptSha256'])
    evidence = result['evidence']
    require(len(evidence) == 3)
    listing = file_value(root / 'responses' / (evidence[2]['requestId'] + '.json'), evidence[2]['sha256'])
    parameters = {'saveRequestId': result['saveOperationId'], 'saveReceiptSha256': result['saveReceiptSha256'],
                  'inspectionRequestId': evidence[1]['requestId'], 'inspectionSha256': evidence[1]['sha256'],
                  'listingRequestId': evidence[2]['requestId'], 'listingSha256': evidence[2]['sha256'],
                  'modelId': listing['activeWindow']['modelId'], 'windowNumber': listing['activeWindow']['windowNumber'], 'expiresUtc': result['expiresUtc']}
    saved_proof(root, result, parameters)
    require(all(same_json(plan[k], result[k]) for k in ('saveOperationId', 'targetId', 'targetSha256', 'savedResult')))
    seed = plan['seed']
    require(set(seed) == {'path', 'sha256', 'length', 'fileIdentity'})
    digest, length = plan['savedResult']['sha256'], plan['savedResult']['length']
    require(seed['sha256'] == plan['workingSha256'] == plan['checkpointSha256'] == digest and seed['length'] == length)
    require(type(length) is int and 0 < length <= 1073741824)
    copy = root / 'workingcopies' / ident
    paths = [root / 'savedreopens' / ident / ('DiskReopen_' + ident + '.psmodel'), copy / ('Working_' + ident + '.psmodel'), copy / 'checkpoint/Before.psmodel']
    require(all(path_key(actual) == path_key(str(expected)) for actual, expected in zip([seed['path'], plan['workingPath'], plan['checkpointPath']], paths)))
    ownership = file_value(copy / 'ownership.json', plan['ownershipSha256'])
    require(set(ownership) == {'schemaVersion', 'copyId', 'bridgeVersion', 'createdUtc', 'scope', 'executionEnabled', 'source', 'working', 'checkpoint'})
    require(type(ownership['schemaVersion']) is int and ownership['schemaVersion'] == 2 and ownership['copyId'] == ident)
    require(ownership['scope'] == 'saved_disk_bytes_only' and ownership['executionEnabled'] is False and same_json(ownership['source'], seed))
    for key, path, hash_key in [('working', paths[1], 'initialSha256'), ('checkpoint', paths[2], 'sha256')]:
        record = ownership[key]
        require(set(record) == {'path', hash_key, 'length', 'fileIdentity'} and record[hash_key] == digest and record['length'] == length)
        require(path_key(record['path']) == path_key(str(path)))
    ids = [seed['fileIdentity'], ownership['working']['fileIdentity'], ownership['checkpoint']['fileIdentity'], plan['savedResult']['fileIdentity']]
    for value in ids + [plan['directoryIdentity']]:
        identity(value)
    require(len({(value['volumeSerial'], value['fileIndex']) for value in ids}) == 4)
    for path in paths:
        require(path.stat().st_size == length and hashlib.sha256(path.read_bytes()).hexdigest() == digest)
    require(paths[0].stat().st_file_attributes & 1 and paths[2].stat().st_file_attributes & 1)
    require(not (copy / 'mutation-fence.json').exists())
    if unclaimed:
        require(not (copy / 'open-once.json').exists())
