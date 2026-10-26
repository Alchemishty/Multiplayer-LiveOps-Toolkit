#!/usr/bin/env python3
"""Run current-package macOS host/client and headless server/client probes.

Run with backend/.venv/bin/python after verify_package_consumer.py completes;
pass its retained Project path. Only modifies that disposable consumer project.
"""
import argparse
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
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import rsa

ROOT = Path(__file__).resolve().parents[1]
UNITY_PROJECT = ROOT / 'Unity'
UNITY = '/Applications/Unity/Hub/Editor/2022.3.33f1/Unity.app/Contents/MacOS/Unity'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', required=True, type=Path)
    args = parser.parse_args()
    project = args.project.resolve()
    if not (project / 'Assets/ConsumerOwned.txt').is_file():
        raise RuntimeError('Use only the disposable project created by verify_package_consumer.py.')
    state = Path(tempfile.mkdtemp(prefix='liveops-release-netcode-'))
    print('Evidence: ' + str(state), flush=True)
    # Use a new copy of CURRENT source, rather than trusting an earlier cached binary.
    package = UNITY_PROJECT / 'Packages/com.alchemishty.multiplayer-liveops-toolkit'
    shutil.copytree(package, state / package.name)
    manifest = project / 'Packages/manifest.json'
    configuration = json.loads(manifest.read_text())
    configuration['dependencies'][package.name] = 'file:' + str(state / package.name)
    manifest.write_text(json.dumps(configuration, indent=4) + '\n')
    shutil.copy2(ROOT / 'tools/consumer/NetcodeConsumer.cs', project / 'Assets')
    shutil.copy2(ROOT / 'tools/consumer/ToolkitConsumerBuild.cs', project / 'Assets/Editor')
    environment = os.environ.copy()
    environment['SMOKE_BUILD'] = str(state / 'NetcodeConsumer.app')
    subprocess.run([UNITY, '-batchmode', '-nographics', '-projectPath', str(project), '-executeMethod', 'ToolkitConsumerBuild.BuildNetcode', '-quit', '-logFile', str(state / 'build.log')], env=environment, check=True, timeout=600)
    run_players(state, environment)


def run_players(state, environment):
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    keyfile = state / 'private.pem'
    keyfile.write_bytes(key.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption()))
    keyfile.chmod(0o600)
    with socket.socket() as reservation:
        reservation.bind(('127.0.0.1', 0)); backend_port = reservation.getsockname()[1]
    environment.update(DB_PATH=str(state / 'test.sqlite3'), DJANGO_SECRET_KEY=secrets.token_urlsafe(40), SMOKE_PASSWORD=secrets.token_urlsafe(24), LIVEOPS_PROOF_PRIVATE_KEY_FILE=str(keyfile), LIVEOPS_PROOF_KEY_ID='release', LIVEOPS_PROOF_ISSUER='release-qualification', LIVEOPS_GAME_ID='game', LIVEOPS_ENVIRONMENT='test', LIVEOPS_CONTENT_VERSION='v1', SMOKE_BACKEND=f'http://127.0.0.1:{backend_port}', SMOKE_TRUST=str(state / 'trust.json'))
    python = str(ROOT / 'backend/.venv/bin/python')
    def django(*options, capture=False):
        result = subprocess.run([python, 'manage.py', *options], cwd=ROOT / 'backend', env=environment, check=True, stdout=subprocess.PIPE if capture else subprocess.DEVNULL, text=True)
        return result.stdout
    players = []
    server = None
    try:
        django('migrate', '--noinput')
        django('shell', '-c', "import os; from django.contrib.auth.models import User; from shop.models import Accessory,PlayerProfile,UserAccessory; items=[Accessory.objects.create(name=name,type=slot) for name,slot in [('Hat','headwear'),('Sword','mainhand')]]; profiles=[PlayerProfile.objects.create(user=User.objects.create_user(name,password=os.environ['SMOKE_PASSWORD']),platform_id=name) for name in ['release-host','release-client']]; UserAccessory.objects.bulk_create([UserAccessory(player=profile,accessory=item) for profile in profiles for item in items])")
        (state / 'trust.json').write_text(django('export_identity_trust', capture=True))
        with (state / 'django.log').open('w') as server_log:
            server = subprocess.Popen([python, 'manage.py', 'runserver', f'127.0.0.1:{backend_port}', '--noreload', '--nothreading'], cwd=ROOT / 'backend', env=environment, stdout=server_log, stderr=subprocess.STDOUT)
            for attempt in range(60):
                try:
                    urllib.request.urlopen(environment['SMOKE_BACKEND'] + '/admin/login/', timeout=1); break
                except OSError:
                    time.sleep(.2)
            else:
                raise RuntimeError('Isolated Django did not become ready.')
            binary = str(state / 'NetcodeConsumer.app/Contents/MacOS/ToolkitConsumer')
            def launch(mode, prefix, port):
                result = state / (prefix + mode + '.txt')
                player_environment = environment.copy()
                player_environment.update(SMOKE_MODE=mode, SMOKE_USER='release-client' if mode == 'client' else 'release-host', SMOKE_PORT=str(port), SMOKE_PREFIX=str(state / prefix), SMOKE_RESULT=str(result))
                process = subprocess.Popen([binary, '-batchmode', '-nographics', '-logFile', str(state / (prefix + mode + '.log'))], env=player_environment, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                players.append(process)
                return process, result
            def check(process, result):
                process.wait(timeout=45)
                outcome = result.read_text() if result.exists() else 'FAIL no native result'
                print(outcome, flush=True)
                if process.returncode or not outcome.startswith('PASS'):
                    raise RuntimeError('Native Netcode qualification failed: ' + str(result))
            for mode in ['host', 'server']:
                with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as reservation:
                    reservation.bind(('127.0.0.1', 0)); port = reservation.getsockname()[1]
                prefix = mode + '-'
                owner, owner_result = launch(mode, prefix, port)
                deadline = time.monotonic() + 25
                while not (state / (prefix + 'ready')).exists() and owner.poll() is None and time.monotonic() < deadline:
                    time.sleep(.1)
                if not (state / (prefix + 'ready')).exists():
                    raise RuntimeError('Native network owner did not become ready: ' + mode)
                remote, remote_result = launch('client', prefix, port)
                check(remote, remote_result)
                check(owner, owner_result)
        print('PASS current package: separate host/client and headless server/client with real signed Django identity/equipment', flush=True)
    finally:
        for process in players:
            if process.poll() is None:
                process.terminate(); process.wait(timeout=5)
        if server is not None:
            server.terminate(); server.wait(timeout=5)
        keyfile.unlink(missing_ok=True)
        for path in state.glob('test.sqlite3*'):
            path.unlink()


if __name__ == '__main__':
    main()
