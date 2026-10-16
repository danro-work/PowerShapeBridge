"""Actual SDK STDIO/native file path with fake CAD: separate activation/WORLD intents."""
import argparse
import json
import shutil
from pathlib import Path
from test_sdk_stdio import Client
from test_sdk_owned_uniform_scale_wire_stdio import initialize
from test_sdk_checkpoint_wire_stdio import Panel as BasePanel, inventory, native, read, sha


class Panel(BasePanel):
    def run(self):
        while not self.stop.is_set():
            for path in (self.root / 'requests').glob('*.json'):
                processing = path.with_name(path.name + '.processing')
                try:
                    path.rename(processing)
                except (FileNotFoundError, PermissionError):
                    continue
                try:
                    request = read(processing)
                    result = native(self.root / 'PSBridge.Worker.exe', '--workplane-activation-wire-request', path.stem)
                    self.calls.append({'recipe': request['arguments']['recipeId'], 'requestId': path.stem, 'state': result['state']})
                    target = self.root / 'responses' / (path.stem + '.json')
                    temporary = target.with_suffix('.pending')
                    assert not target.exists()
                    temporary.write_text(json.dumps(result), encoding='utf-8')
                    temporary.rename(target)
                except BaseException as error:
                    self.errors.append(repr(error))
                    self.stop.set()
            self.stop.wait(.01)


def run(args):
    stage = Path(args.stage).resolve()
    seed_stage = Path(args.legacy_stage).resolve() if args.legacy_stage else stage
    assert not (args.multiple and args.legacy_stage), 'Legacy coverage uses the original one-workplane schema.'
    seed = native(seed_stage / 'PSBridge.Worker.exe', '--multi-workplane-activation-wire-seed' if args.multiple else '--workplane-activation-wire-seed')
    root = Path(seed['root']).resolve()
    assert root.is_relative_to(seed_stage / 'work')
    output = Path(args.evidence).resolve()
    assert not output.exists()
    output.mkdir(parents=True)
    panel = Panel(root, 'control')
    command = [args.dotnet, str(Path(args.host).resolve()), '--bridge-root', str(root), '--timeout', '20']
    client = Client(command)
    sequence = 10
    report = {'state': 'started', 'cadConnectionAttempted': False, 'root': str(root), 'seed': seed, 'calls': [], 'multiple': args.multiple, 'legacyUpgrade': bool(args.legacy_stage)}

    def call(name, parameters):
        nonlocal sequence
        sequence += 1
        reply = client.call(sequence, 'tools/call', {'name': name, 'arguments': parameters}, timeout=40)
        report['calls'].append({'tool': name, 'reply': reply})
        assert not panel.errors, panel.errors
        assert 'result' in reply, reply
        return reply['result']

    def ok(name, parameters):
        result = call(name, parameters)
        assert not result.get('isError', False), result
        return result['structuredContent']

    def verify(handle):
        candidate = ok('inspect_entity_candidates', {})
        listing = ok('list_documents', {})
        document = next(d for d in listing['documents'] if d['isActive'])
        checked = ok('verify_owned_workplane_activation', {'activationExecutionHandle': handle, 'candidateDocumentHandle': candidate['candidateDocumentHandle'], 'activeDocumentHandle': document['documentHandle']})
        assert checked['state'] == 'owned_workplane_activation_verified_observed'
        assert checked['noCommandReplay'] is checked['copyFenceReleased'] is True
        return checked

    try:
        tools = initialize(client, 'workplane-activation-native-wire')
        assert {'prepare_owned_workplane_activation', 'execute_owned_workplane_activation', 'verify_owned_workplane_activation', 'adopt_owned_workplane_activation_execution'} <= {t['name'] for t in tools}
        owned = ok('prepare_working_copy', {'sourcePath': seed['sourcePath'], 'sourceSha256': sha(Path(seed['sourcePath']))})
        for mode in (('existing', 'other', 'world') if args.multiple else ('existing', 'world')):
            candidate = ok('inspect_entity_candidates', {})
            bound = ok('bind_working_document', {'candidateDocumentHandle': candidate['candidateDocumentHandle'], 'workingCopyHandle': owned['workingCopyHandle']})
            target = {'mode': 'world' if mode == 'world' else 'existing'}
            if mode != 'world':
                plane = next(e for e in candidate['entities'] if e['nativeTypeCode'] == 54 and (mode != 'other' or e['nativeId'] == 12))
                target['workplaneCandidateHandle'] = plane['candidateEntityHandle']
            prepared = ok('prepare_owned_workplane_activation', {'workingDocumentBindingHandle': bound['workingDocumentBindingHandle'], 'parameters': target})
            executed = ok('execute_owned_workplane_activation', {'workplaneActivationHandle': prepared['workplaneActivationHandle']})
            assert executed['setterCalls'] == 1 and executed['geometryMutationAttempted'] is executed['cadSaveAttempted'] is False
            expected = 1080000011 if mode == 'existing' else 1080000012 if mode == 'other' else 0
            assert read(sorted((root / 'wire-contexts').glob('*.json'))[-1])['active'] == expected
            before = inventory(root)
            denied = call('execute_owned_workplane_activation', {'workplaneActivationHandle': prepared['workplaneActivationHandle']})
            assert denied['isError'] and denied['structuredContent']['cadMutationMayHaveOccurred'] is False and inventory(root) == before
            # Reconnect before verification of the active workplane, without returning WORLD.
            if mode == 'existing':
                if args.legacy_stage:
                    report['legacyWorkerSha256'] = sha(root / 'PSBridge.Worker.exe')
                    report['legacyExecutionBridgeVersion'] = executed['bridgeVersion']
                    shutil.copyfile(stage / 'PSBridge.Worker.exe', root / 'PSBridge.Worker.exe')
                    report['upgradedWorkerSha256'] = sha(root / 'PSBridge.Worker.exe')
                    assert report['legacyWorkerSha256'] != report['upgradedWorkerSha256']
                report['firstTranscript'] = client.transcript
                client.close()
                client = Client(command)
                initialize(client, 'workplane-activation-reconnect')
                before = inventory(root)
                adopted = ok('adopt_owned_workplane_activation_execution', {'executionRequestId': executed['bridgeRequestId'], 'executionReceiptSha256': sha(root / 'responses' / (executed['bridgeRequestId'] + '.json'))})
                assert adopted['verificationOnly'] is True and adopted['executionAuthorized'] is adopted['retryAllowed'] is False
                assert inventory(root) == before
                handle = adopted['activationExecutionHandle']
                denied_adoption = call('execute_owned_workplane_activation', {'workplaneActivationHandle': handle})
                assert denied_adoption['isError'] and inventory(root) == before
                report['adoption'] = adopted
            else:
                handle = executed['activationExecutionHandle']
            owned = verify(handle)
            assert owned['activeWorkplaneAfter'] == expected and owned['workingCopyLifetimeSeconds'] > 0
            report[mode] = {'prepared': prepared, 'execution': executed, 'verification': owned}
        contexts = sorted((root / 'wire-contexts').glob('*.json'))
        final = read(contexts[-1])
        count = 3 if args.multiple else 2
        assert final['setterCalls'] == count and final['active'] == 0
        assert sum(c['recipe'] == 'execute_owned_workplane_activation' for c in panel.calls) == count
        for path, digest in seed['protectedHashes'].items():
            assert sha(Path(path)) == digest, path
        report.update(state='passed', setterCalls=count, reconnectedWithoutReplay=True, artifactHashes=inventory(root))
        if args.legacy_stage:
            assert report['existing']['execution']['bridgeVersion'] != report['existing']['verification']['bridgeVersion']
        print(json.dumps({'state': 'passed', 'setterCalls': count, 'multiple': args.multiple, 'legacyUpgrade': bool(args.legacy_stage), 'separateIntents': True, 'reconnectedWithoutReplay': True, 'cadConnectionAttempted': False}))
    except BaseException as error:
        report['state'] = 'failed_preserve_synthetic_evidence'
        report['error'] = repr(error)
        raise
    finally:
        try:
            client.close()
        finally:
            panel.close()
            report['transcript'] = client.transcript
            report['nativeRequests'] = panel.calls
            (output / 'result.json').write_text(json.dumps(report, indent=2), encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('stage', 'host', 'dotnet', 'evidence'):
        parser.add_argument('--' + name, required=True)
    parser.add_argument('--multiple', action='store_true')
    parser.add_argument('--legacy-stage')
    run(parser.parse_args())
