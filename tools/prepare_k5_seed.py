"""Create a NEW immutable saved-byte seed; never saves/edits the source model."""
import argparse
import hashlib
import json
from pathlib import Path
import stat

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', required=True)
    parser.add_argument('--directory', required=True)
    args = parser.parse_args()
    source, directory = Path(args.source).resolve(), Path(args.directory).resolve()
    assert not directory.exists() and source.suffix.lower() == '.psmodel'
    before = source.stat()
    raw = source.read_bytes()
    digest = hashlib.sha256(raw).hexdigest()
    second = hashlib.sha256(source.read_bytes()).hexdigest()
    after = source.stat()
    assert (before.st_size, before.st_mtime_ns) == (after.st_size, after.st_mtime_ns) and digest == second
    directory.mkdir(parents=True)
    seed = directory / 'Source.psmodel'
    with seed.open('xb') as stream:
        stream.write(raw)
        stream.flush()
    assert hashlib.sha256(seed.read_bytes()).hexdigest() == digest
    seed.chmod(stat.S_IREAD)
    report = dict(state='stable_saved_bytes_seed_prepared', sourcePath=str(source), seedPath=str(seed),
        sha256=digest, sourceSize=before.st_size, sourceMtimeNs=before.st_mtime_ns,
        seedReadOnly=True, cadSaveAttempted=False, sourceModified=False, unsavedStateCopied=False)
    (directory / 'seed.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report))
