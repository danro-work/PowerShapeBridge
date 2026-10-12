"""Independent audit against recorded Bridge responses, journals and protected file hashes."""
import argparse
import copy
import json
from pathlib import Path

from slice_job import file_hash, save
from entity_references import inventory, observation_digest


def audit(root, report_path):
    report = json.loads(report_path.read_text(encoding='utf-8'))
    if report.get('state') != 'succeeded_three_fixture_read_references':
        raise ValueError('Qualification did not complete')
    if report.get('staleReferenceChecks') != 4 or report.get('geometryMutationAttempted') is not False:
        raise ValueError('Qualification scope differs')
    fingerprints = {str(report_path): file_hash(report_path)}
    reader_calls = 0
    controls = 0
    stales = 0
    fixture_types = []
    def raw_response(request_id):
        if not isinstance(request_id, str) or len(request_id) != 32 or any(x not in '0123456789abcdef' for x in request_id):
            raise ValueError('Invalid recorded response identity')
        path = root/'responses'/(request_id+'.json')
        fingerprints[str(path)] = file_hash(path)
        return json.loads(path.read_text(encoding='utf-8-sig'))
    for record in report['records']:
        if 'fixture' in record:
            inspected = record['inspection']
            raw = raw_response(inspected['bridgeRequestId'])
            expected = copy.deepcopy(inspected)
            for key in ('documentReference','observedPropertiesSha256','referenceLifetimeSeconds','referenceQualification','bridgeRequestId'):
                expected.pop(key, None)
            for entity in expected['entities']:
                entity.pop('entityReference')
            expected['state'] = 'succeeded_verified'
            if raw != expected or raw.get('viewportCapture'):
                raise ValueError('Public inspection differs from Bridge response or captured image')
            if inspected['observedPropertiesSha256'] != observation_digest(raw, inventory(raw)):
                raise ValueError('Observation digest differs')
            resolved = record['resolution']
            fresh = raw_response(resolved['inspectionRequestId'])
            if resolved['state'] != 'entity_references_resolved_observed' or fresh['state'] != 'succeeded_verified':
                raise ValueError('Resolution failed')
            if resolved['observedPropertiesSha256'] != observation_digest(fresh, inventory(fresh)):
                raise ValueError('Fresh resolution digest differs')
            lookup = inventory(fresh)
            for entity in resolved['entities']:
                observed = dict(entity)
                observed.pop('entityReference')
                if lookup[(entity['nativeTypeCode'], entity['nativeId'])] != observed:
                    raise ValueError('Resolved entity properties differ')
            fixture_types.append(inspected['countsByType'])
            reader_calls += 2
        elif 'staleReferenceCheck' in record:
            result = record['result']
            if result.get('isError') is not True or result['structuredContent']['error']['code'] != 'document_context_changed':
                raise ValueError('Old document reference accepted')
            stales += 1
        elif 'internalFixtureControl' in record:
            data = record['result']
            raw = raw_response(data['bridgeRequestId'])
            expected = dict(data)
            expected.pop('bridgeRequestId')
            if raw != expected or data['geometryMutationAttempted'] is not False or not data.get('protectedProjectHashesUnchanged'):
                raise ValueError('Fixture control response differs')
            if data['state'] not in {'qualification_copy_opened','qualified_copy_closed_baseline_restored'}:
                raise ValueError('Fixture control incomplete')
            controls += 1
    if reader_calls != 10 or controls != 4 or stales != 4 or {'SURFACE':6} not in fixture_types or {'COMPCURVE':25} not in fixture_types:
        raise ValueError('Incomplete three-fixture qualification')
    folder = root.parent.parent/'P2_Disk_Verification'/('References_'+report['qualificationId'])
    ledger = json.loads((folder/'owned-document.json').read_text(encoding='utf-8-sig'))
    if ledger.get('state') != 'closed_qualification_copy':
        raise ValueError('Owned test window not closed')
    config = json.loads((root/'bridge.config.json').read_text(encoding='utf-8-sig'))
    for p, sha in ((Path(config['expectedProject']), config['testProjectSha256']),
                   (Path(config['protectedProject']), config['protectedProjectSha256']),
                   (folder/'SurfaceCopy.psmodel','56dd1234d09a60ff54105f7d904c1feda0390826ce81fa8847d5f283b62b02ca'),
                   (folder/'CurveCopy.psmodel','e7bc7bc164c235d8cf262c95f7f9ae2180a94cd65bcf3c248859714a84dd8819')):
        if file_hash(p) != sha:
            raise ValueError('Protected source or copied fixture changed: '+str(p))
        fingerprints[str(p)] = sha
    catalog = {x['name'] for x in report['catalog']['tools']}
    if catalog != {'bridge_status','inspect_active_model','resolve_entity_references','operation_result'}:
        raise ValueError('Read-only catalog differs')
    result = {'state':'succeeded_read_reference_independent_audit','qualificationId':report['qualificationId'],
            'publicReaderCalls':reader_calls,'staleDocumentReferenceRejections':stales,
            'internalFixtureOpenCloseControls':controls,'ownedTestWindowsClosed':True,
            'protectedFilesUnchanged':True,'files':fingerprints,
            'scope':'Saved solid/surface/square-curve fixtures only; no persistent identity, production geometry or general mutation acceptance.'}
    reopen_path = report_path.parent/'same-path-reopen.json'
    if reopen_path.is_file():
        reopened = json.loads(reopen_path.read_text(encoding='utf-8'))
        if reopened.get('state') != 'succeeded_same_path_reopen_old_references_rejected':
            raise ValueError('Same-path reopen did not complete')
        fingerprints[str(reopen_path)] = file_hash(reopen_path)
        before, after = reopened['before'], reopened['after']
        if before['model']['path'] != after['model']['path'] or before['model']['id'] == after['model']['id']:
            raise ValueError('Same-path reopen identity evidence differs')
        for inspection in (before, after):
            raw = raw_response(inspection['bridgeRequestId'])
            if raw['model'] != inspection['model'] or observation_digest(raw, inventory(raw)) != inspection['observedPropertiesSha256']:
                raise ValueError('Reopened reader data differs')
        rejected = reopened['oldReferenceResolution']
        if not rejected.get('isError') or rejected['structuredContent']['error']['code'] != 'document_context_changed':
            raise ValueError('Old reference after reopen accepted')
        rejection_read = raw_response(rejected['structuredContent']['inspectionRequestId'])
        if rejection_read['model'] != after['model'] or rejected['structuredContent']['observedDocument'] != after['model']:
            raise ValueError('Rejected reference has no matching fresh document evidence')
        resolution = reopened['newReferenceResolution']
        resolution_read = raw_response(resolution['inspectionRequestId'])
        if resolution['state'] != 'entity_references_resolved_observed' or resolution['observedPropertiesSha256'] != observation_digest(resolution_read, inventory(resolution_read)):
            raise ValueError('New reference after reopen failed')
        if len(reopened['controls']) != 4:
            raise ValueError('Reopen controls incomplete')
        for control in reopened['controls']:
            data = control['result']
            raw = raw_response(data['bridgeRequestId'])
            expected = dict(data)
            expected.pop('bridgeRequestId')
            if raw != expected or data['geometryMutationAttempted'] is not False or not data.get('protectedProjectHashesUnchanged'):
                raise ValueError('Reopen fixture control differs')
        reopen_folder = root.parent.parent/'P2_Disk_Verification'/('References_'+reopened['qualificationId'])
        reopen_ledger = json.loads((reopen_folder/'owned-document.json').read_text(encoding='utf-8-sig'))
        if reopen_ledger['state'] != 'closed_qualification_copy' or file_hash(reopen_folder/'CurveCopy.psmodel') != 'e7bc7bc164c235d8cf262c95f7f9ae2180a94cd65bcf3c248859714a84dd8819':
            raise ValueError('Reopen copy changed or remained open')
        result['samePathReopen'] = {'state':'independently_verified','oldModelId':before['model']['id'],
                                    'newModelId':after['model']['id'],'oldReferenceRejected':True,
                                    'newReferencesResolved':True,'copyWindowClosed':True}
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--bridge-root',type=Path,required=True)
    parser.add_argument('--report',type=Path,required=True)
    args = parser.parse_args()
    result = audit(args.bridge_root, args.report)
    save(args.report.parent/'independent-audit.json',result)
    print(result['state'])
