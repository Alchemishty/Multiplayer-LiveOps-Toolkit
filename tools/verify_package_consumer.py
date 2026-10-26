#!/usr/bin/env python3
"""Qualify the local UPM package in a fresh, isolated Unity 2022.3 project.

Requires installed macOS Unity 2022.3.33f1 and backend/.venv. Does not publish,
modify the toolkit package, or copy repository Assets directly. Imports the packaged Full Demo and retains evidence under /tmp.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import secrets
import shutil
import socket
import subprocess
import tempfile
import time
import urllib.request
import xml.etree.ElementTree as element_tree

ROOT = Path(__file__).resolve().parents[1]
UNITY_PROJECT = ROOT / 'Unity'
PACKAGE = UNITY_PROJECT / 'Packages/com.alchemishty.multiplayer-liveops-toolkit'


def digest(directory):
    return {str(path.relative_to(directory)): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in directory.rglob('*') if path.is_file()}


def verify_preserved_file(path, expected):
    if not path.is_file() or path.read_bytes() != expected:
        raise RuntimeError('Upgrade lost or modified consumer-owned file: ' + str(path))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity', type=Path, default=Path('/Applications/Unity/Hub/Editor/2022.3.33f1/Unity.app/Contents/MacOS/Unity'))
    args = parser.parse_args()
    state = Path(tempfile.mkdtemp(prefix='liveops-package-consumer-'))
    print('Evidence: ' + str(state), flush=True)
    source_digest = digest(PACKAGE)
    project = state / 'Project'
    environment = os.environ.copy()
    with socket.socket() as reservation:
        reservation.bind(('127.0.0.1', 0)); backend_port = reservation.getsockname()[1]
    environment['SMOKE_BACKEND'] = f'http://127.0.0.1:{backend_port}'

    def unity(label, *options):
        print(label, flush=True)
        result = subprocess.run([str(args.unity), '-batchmode', '-nographics', *options, '-logFile', str(state / (label + '.log'))], env=environment, timeout=600)
        if result.returncode:
            raise RuntimeError(f'{label} failed ({result.returncode}); inspect {state / (label + ".log")}')

    unity('create-project', '-createProject', str(project), '-quit')
    manifests = project / 'Packages/manifest.json'
    # Engine modules are Unity's built-ins; no TMP, demo, editor plugin or toolkit source in Assets.
    dependencies = {name: version for name, version in json.loads((UNITY_PROJECT / 'Packages/manifest.json').read_text())['dependencies'].items() if name.startswith('com.unity.modules.')}
    dependencies['com.unity.test-framework'] = '1.3.9'
    package_v1 = state / 'package-v1'; shutil.copytree(PACKAGE, package_v1)
    dependencies[PACKAGE.name] = 'file:' + str(package_v1)

    def write_manifest():
        manifests.write_text(json.dumps({'dependencies': dependencies, 'testables': [PACKAGE.name]}, indent=4) + '\n')

    write_manifest()
    editor = project / 'Assets/Editor'; editor.mkdir(parents=True, exist_ok=True)
    shutil.copy2(ROOT / 'tools/consumer/ToolkitConsumerBuild.cs', editor)
    shutil.copy2(ROOT / 'tools/consumer/PackageConsumer.cs', project / 'Assets')
    owned_file = project / 'Assets/ConsumerOwned.txt'; owned_file.write_text('The consuming game owns this file.\n')
    environment['SMOKE_BUILD'] = str(state / 'sdk/ToolkitConsumer.app')
    unity('install-build-sdk', '-projectPath', str(project), '-executeMethod', 'ToolkitConsumerBuild.BuildSdk', '-quit')
    unity('package-editmode', '-projectPath', str(project), '-runTests', '-testPlatform', 'editmode', '-assemblyNames', 'MultiplayerLiveOpsToolkit.Tests.EditMode', '-testResults', str(state / 'editmode.xml'))
    test_results = element_tree.parse(state / 'editmode.xml').getroot()
    if test_results.attrib.get('failed', '0') != '0' or int(test_results.attrib.get('passed', '0')) == 0:
        raise RuntimeError('Clean consumer package EditMode tests failed or did not execute.')
    print('Clean package tests: ' + test_results.attrib.get('passed', '?') + ' passed', flush=True)
    if any(path.name == 'Unity.TextMeshPro.dll' for path in (project / 'Library/ScriptAssemblies').glob('*.dll')):
        raise RuntimeError('Baseline package consumer unexpectedly required TMP.')
    preserved_files = {path: path.read_bytes() for path in [owned_file, project / 'Assets/Scenes/Consumer.unity', project / 'Assets/Scenes/Consumer.unity.meta']}
    package_v2 = state / 'package-v2'; shutil.copytree(PACKAGE, package_v2)
    metadata = json.loads((package_v2 / 'package.json').read_text()); metadata['version'] = '0.1.0-preview.2'; (package_v2 / 'package.json').write_text(json.dumps(metadata, indent=4) + '\n')
    dependencies[PACKAGE.name] = 'file:' + str(package_v2); write_manifest()
    unity('upgrade-build-sdk', '-projectPath', str(project), '-executeMethod', 'ToolkitConsumerBuild.BuildExistingSdk', '-quit')
    for path, expected in preserved_files.items():
        verify_preserved_file(path, expected)
    dependencies['com.unity.textmeshpro'] = '3.0.6'; dependencies['com.unity.ugui'] = '1.0.0'; write_manifest()
    unity('import-full-demo', '-projectPath', str(project), '-executeMethod', 'ToolkitConsumerBuild.ImportSample')
    environment['SMOKE_BUILD'] = str(state / 'demo/ToolkitConsumer.app')
    unity('build-full-demo', '-projectPath', str(project), '-executeMethod', 'ToolkitConsumerBuild.BuildFullDemo', '-quit')
    run_backend_and_players(state, environment, backend_port)
    if digest(PACKAGE) != source_digest:
        raise RuntimeError('Qualification edited original package source.')
    print('PASS install, upgrade, portable tests, SDK native consumer and imported Full Demo; package source unchanged', flush=True)


def run_backend_and_players(state, environment, backend_port):
    password = secrets.token_urlsafe(24)
    backend_environment = environment.copy()
    backend_environment.update(DB_PATH=str(state / 'test.sqlite3'), DJANGO_SECRET_KEY=secrets.token_urlsafe(40), SMOKE_PASSWORD=password)
    python = str(ROOT / 'backend/.venv/bin/python')
    def django(*options):
        subprocess.run([python, 'manage.py', *options], cwd=ROOT / 'backend', env=backend_environment, check=True, stdout=subprocess.DEVNULL)
    django('migrate', '--noinput')
    django('shell', '-c', "import os; from django.contrib.auth.models import User; from django.utils import timezone; from shop.models import PlayerProfile,Currency,Wallet,Accessory,AccessoryCost,Achievement,MonthlyRewardSchedule; gold=Currency.objects.create(name='gold'); hat=Accessory.objects.create(name='Hat',type='headwear'); AccessoryCost.objects.create(accessory=hat,currency=gold,amount=25); Achievement.objects.create(key='first',title='First purchase',condition_type='one_time_trigger',trigger_event='purchase',reward_type='currency',currency_reward=gold,reward_amount=10); today=timezone.now().date(); MonthlyRewardSchedule.objects.create(year=today.year,month=today.month,day=today.day,currency=gold,reward_amount=50); [(Wallet.objects.create(player=PlayerProfile.objects.create(user=User.objects.create_user(name,password=os.environ['SMOKE_PASSWORD']),platform_id=name),currency=gold,amount=100)) for name in ['sdk-consumer']]")
    with (state / 'django.log').open('w') as server_log:
        server = subprocess.Popen([python, 'manage.py', 'runserver', f'127.0.0.1:{backend_port}', '--noreload', '--nothreading'], cwd=ROOT / 'backend', env=backend_environment, stdout=server_log, stderr=subprocess.STDOUT)
        try:
            for attempt in range(60):
                try:
                    urllib.request.urlopen(environment['SMOKE_BACKEND'] + '/admin/login/', timeout=1); break
                except OSError:
                    time.sleep(.2)
            else:
                raise RuntimeError('Isolated Django did not become ready.')
            for mode in ['sdk']:
                result_path = state / (mode + '-result.txt')
                result_path.unlink(missing_ok=True)
                player_environment = environment.copy(); player_environment.update(SMOKE_USER=mode + '-consumer', SMOKE_PASSWORD=password, SMOKE_RESULT=str(result_path))
                player = subprocess.Popen([str(state / mode / 'ToolkitConsumer.app/Contents/MacOS/ToolkitConsumer'), '-batchmode', '-nographics', '-logFile', str(state / (mode + '-player.log'))], env=player_environment, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                try:
                    player.wait(timeout=60)
                finally:
                    if player.poll() is None:
                        player.terminate(); player.wait(timeout=5)
                outcome = result_path.read_text() if result_path.exists() else 'FAIL no native result: ' + mode
                print(outcome, flush=True)
                if player.returncode or not outcome.startswith('PASS'):
                    raise RuntimeError('Native qualification failed: ' + mode)
        finally:
            server.terminate(); server.wait(timeout=5)
            for path in state.glob('test.sqlite3*'):
                path.unlink()


if __name__ == '__main__':
    main()
