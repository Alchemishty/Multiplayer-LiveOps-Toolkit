"""Package the authored playable demo as the optional Full Demo sample."""
import argparse
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
UNITY_PROJECT = ROOT / 'Unity'
TARGET = UNITY_PROJECT / 'Packages/com.alchemishty.multiplayer-liveops-toolkit/Samples~/FullDemo'


def expected_files():
    result = {}
    for folder in ['Scripts', 'Scenes', 'Prefabs', 'Models', 'ScriptableObjects']:
        for source in (UNITY_PROJECT / 'Assets' / folder).rglob('*'):
            if source.is_file() and source.name != '.DS_Store':
                result[Path(folder) / source.relative_to(UNITY_PROJECT / 'Assets' / folder)] = source.read_bytes()
        metadata = UNITY_PROJECT / 'Assets' / (folder + '.meta')
        result[Path(folder + '.meta')] = metadata.read_bytes()
    for name in ['DefaultNetworkPrefabs.asset', 'DefaultNetworkPrefabs.asset.meta']:
        result[Path(name)] = (UNITY_PROJECT / 'Assets' / name).read_bytes()
    return result


def synchronize(check=False):
    expected = expected_files()
    actual = {path.relative_to(TARGET): path for path in TARGET.rglob('*')
              if path.is_file() and path.name not in {'README.md', 'THIRD_PARTY_NOTICES.md'}}
    if check:
        if set(actual) != set(expected) or any(actual[path].read_bytes() != data for path, data in expected.items() if path in actual):
            raise RuntimeError('Packaged Full Demo differs from its authored demo sources. Run tools/sync_full_demo.py.')
        return
    for relative, path in actual.items():
        if relative not in expected:
            path.unlink()
    for path, data in expected.items():
        target = TARGET / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    synchronize(parser.parse_args().check)
