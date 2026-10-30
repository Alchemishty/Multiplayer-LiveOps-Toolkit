"""Export and verify the runtime + Full Demo as a classic Unity asset package.

Uses an isolated Unity consumer project, removed after verification. The main
project is never modified. Dependencies and TMP Essentials are prerequisites,
not copied into the exported toolkit.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import os
import re
import tarfile
import tempfile
from verify_package_consumer import sample_dependencies

ROOT = Path(__file__).resolve().parents[1]
UNITY_PROJECT = ROOT / 'Unity'
PACKAGE = UNITY_PROJECT / 'Packages/com.alchemishty.multiplayer-liveops-toolkit'
IMPORT_ROOT = 'Assets/MultiplayerLiveOpsToolkit'


def export_sources():
    files = {}
    for source_folder, destination_folder in [('Runtime', 'Runtime'), ('Samples~/FullDemo', 'FullDemo'), ('Documentation~', 'Documentation')]:
        for source in (PACKAGE / source_folder).rglob('*'):
            if source.is_file() and source.name != '.DS_Store':
                files[Path(destination_folder) / source.relative_to(PACKAGE / source_folder)] = source
    files[Path('LICENSE.md')] = PACKAGE / 'LICENSE.md'
    files[Path('LICENSE.md.meta')] = PACKAGE / 'LICENSE.md.meta'
    files[Path('README.md')] = ROOT / 'Documentation/unitypackage.md'
    return files


def export_content(relative, source):
    data = source.read_bytes()
    if relative.suffix == '.md':
        data = data.replace(b'../Samples~/FullDemo/', b'../FullDemo/')
    return data


def verify_archive(archive, sources):
    """Read archive data without extracting; require only the intended subtree."""
    with tarfile.open(archive, 'r:gz') as package:
        members = {member.name: member for member in package.getmembers()}
        exported_files = {}
        exported_metadata = {}
        for name, member in members.items():
            if name.endswith('/pathname'):
                stream = package.extractfile(member)
                if stream is None:
                    raise RuntimeError('Missing archive pathname: ' + name)
                path = stream.read().decode().rstrip('\x00\n')
                if path != IMPORT_ROOT and not path.startswith(IMPORT_ROOT + '/'):
                    raise RuntimeError('Unexpected exported dependency: ' + path)
                if '..' in Path(path).parts:
                    raise RuntimeError('Invalid exported path: ' + path)
                asset = members.get(name.rsplit('/', 1)[0] + '/asset')
                metadata = members.get(name.rsplit('/', 1)[0] + '/asset.meta')
                if metadata is not None:
                    stream = package.extractfile(metadata)
                    if stream is None:
                        raise RuntimeError('Unreadable exported metadata: ' + path)
                    exported_metadata[path] = stream.read()
                if asset is not None:
                    stream = package.extractfile(asset)
                    if stream is None:
                        raise RuntimeError('Unreadable exported asset: ' + path)
                    exported_files[path] = stream.read()
        for relative, source in sources.items():
            if relative.suffix == '.meta':
                continue
            destination = IMPORT_ROOT + '/' + relative.as_posix()
            if exported_files.get(destination) != export_content(relative, source):
                raise RuntimeError('Exported asset differs from source: ' + destination)
            source_metadata = sources.get(Path(str(relative) + '.meta'))
            if source_metadata is not None:
                source_guid = re.search(rb'^guid: ([0-9a-f]{32})', source_metadata.read_bytes(), re.M)
                archive_guid = re.search(rb'^guid: ([0-9a-f]{32})', exported_metadata.get(destination, b''), re.M)
                if source_guid is None or archive_guid is None or source_guid[1] != archive_guid[1]:
                    raise RuntimeError('Exported GUID differs from source: ' + destination)
        expected = {IMPORT_ROOT + '/' + path.as_posix() for path in sources if path.suffix != '.meta'}
        if set(exported_files) != expected:
            raise RuntimeError('Unexpected files in exported package: ' + str(set(exported_files) - expected))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity', type=Path, default=Path('/Applications/Unity/Hub/Editor/2022.3.33f1/Unity.app/Contents/MacOS/Unity'))
    version = json.loads((PACKAGE / 'package.json').read_text())['version']
    parser.add_argument('--output', type=Path, default=UNITY_PROJECT / 'Builds/Distribution' / ('MultiplayerLiveOpsToolkit-' + version + '.unitypackage'))
    args = parser.parse_args()
    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    logs = output.parent / 'verification'
    logs.mkdir(exist_ok=True)
    sources = export_sources()
    environment = os.environ.copy()
    environment['LIVEOPS_EXPORT_PATH'] = str(output)

    def unity(label, *options):
        print(label, flush=True)
        subprocess.run([str(args.unity), '-batchmode', '-nographics', *options, '-logFile', str(logs / (label + '.log'))], env=environment, check=True, timeout=600)

    with tempfile.TemporaryDirectory(prefix='liveops-unitypackage-') as directory:
        project = Path(directory) / 'Project'
        unity('create-project', '-createProject', str(project), '-quit')
        dependencies = {name: version for name, version in json.loads((UNITY_PROJECT / 'Packages/manifest.json').read_text())['dependencies'].items() if name.startswith('com.unity.modules.')}
        dependencies.update(json.loads((PACKAGE / 'package.json').read_text())['dependencies'])
        editor_version = (project / 'ProjectSettings/ProjectVersion.txt').read_text().split('m_EditorVersion: ', 1)[1].splitlines()[0]
        dependencies.update(sample_dependencies(editor_version))
        (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': dependencies}, indent=4) + '\n')
        staging = project / IMPORT_ROOT
        for relative, source in sources.items():
            destination = staging / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(export_content(relative, source))
        editor = project / 'Assets/Editor'
        editor.mkdir(exist_ok=True)
        shutil.copy2(ROOT / 'tools/consumer/UnityPackageBuild.cs', editor)
        unity('import-tmp-essentials', '-projectPath', str(project), '-executeMethod', 'UnityPackageBuild.ImportEssentials')
        unity('export', '-projectPath', str(project), '-executeMethod', 'UnityPackageBuild.Export', '-quit')
        verify_archive(output, sources)
        # Import into an empty toolkit location, not the staged source tree.
        shutil.rmtree(staging)
        Path(str(staging) + '.meta').unlink(missing_ok=True)
        unity('import', '-projectPath', str(project), '-importPackage', str(output), '-quit')
        environment['LIVEOPS_VERIFY_BUILD'] = str(Path(directory) / 'Verification.app')
        unity('verify-build', '-projectPath', str(project), '-executeMethod', 'UnityPackageBuild.Verify', '-quit')
    checksum = hashlib.sha256(output.read_bytes()).hexdigest()
    Path(str(output) + '.sha256').write_text(checksum + '  ' + output.name + '\n')
    print('PASS exported assets/GUIDs, real Unity import, missing-script checks and macOS build; temporary project removed.', flush=True)
    print(output, flush=True)


if __name__ == '__main__':
    main()
