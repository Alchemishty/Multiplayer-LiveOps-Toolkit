# Run the demo

The repository demo and the package's Full Demo sample use the same toolkit runtime, authored scene/art/UI and separately hosted Django reference backend.

To try the repository demo, follow the steps below. To use a fresh Unity project, follow [package installation and Full Demo import](package-integration.md#import-the-full-demo), with the same backend/account/public-trust setup. The imported scene is under the sample folder rather than the repository's Unity/Assets/Scenes folder.

For a separate Unity 6 consumer project, follow [Unity 6 setup](unity6.md). The repository demo below retains its Unity 2022.3 baseline.

## Requirements

- Unity **2022.3.33f1** (see `Unity/ProjectSettings/ProjectVersion.txt`).
- Python **3.11** for the backend, or Docker Compose.
- For Android, install Unity Android Build Support and an ARM64 emulator; see [Android requirements](android.md#install-the-tools).
- Git and Git LFS. Binary assets use LFS; Unity YAML and `.meta` files stay in ordinary Git.

## Clone

```sh
git lfs install
git clone https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit.git
cd Multiplayer-LiveOps-Toolkit
git lfs pull
```

Commit asset changes together with their `.meta` files. The project uses Visible Meta Files and Force Text serialization. Unity caches, builds, local databases, credentials, and virtual environments are excluded.

## Run the backend

```sh
cd backend
python3.11 -m venv .venv
source .venv/bin/activate
python -m pip install -r requirements.txt
cp .env.example .env
set -a
source .env
set +a
python manage.py migrate
python manage.py seed
python manage.py runserver 127.0.0.1:8000 --nothreading
```

PowerShell activation is `.venv\Scripts\Activate.ps1`; set the variables from `.env.example` in that shell. Django reads process environment variables, so creating `.env` alone does not load it. Use the seed command for the current schema; the imported `initial_data.json` fixture is legacy data from an earlier schema.

Alternatively, from the repository root:

```sh
cp backend/.env.example backend/.env
docker compose up --build
```

This starts the local development backend at `http://localhost:8000` and persists SQLite data in a Docker volume. Migrations are committed and applied, never generated at container startup. These examples use development configuration; deployment requires production secrets, authentication, host/TLS settings, and a database/operations review.

## Run Unity

1. Add the repository's `Unity/` folder in Unity Hub and open it with 2022.3.33f1. Allow packages and assets to import.
2. Configure [identity proof signing and public trust](multiplayer-identity-verification.md#backend-configuration), then open `Assets/Scenes/ExampleScene.unity` within that Unity project and start the backend. The sample uses Django username/password login; create an account as described below.
3. Enter Play Mode, enter your Django username/password, and click Sign in.
4. The first instance becomes a Netcode host after a connection timeout; additional instances connect to it. Run builds from Unity's Build Settings with ExampleScene enabled.

The saved `Assets/Scenes/ExampleScene.unity` and its prefabs define the demo. Edit those assets directly; run `python3 tools/sync_full_demo.py` after demo changes to update the package sample. API requests default to `http://localhost:8000`; `ApiConfig.SetBaseUrl` supports an override. Username-only development login and development reset require `DEBUG=True`; the demo signs in through Django username/password authentication.

## Build and run on Android

Android builds are supported on the [verified ARM64 emulator configuration](android.md). Follow the [Android setup and build guide](android.md) after completing the backend, account and public-trust setup here. It covers Android Build Support/SDK/NDK/JDK, IL2CPP/ARM64/OpenGLES3 settings, APK installation, localhost backend forwarding and desktop multiplayer. The demo's movement/camera controls remain keyboard-based.

## Create a login account

The sample signs in through `POST /api/auth/password/`. It does not register users. After migrating and seeding the backend, create an administrator interactively:

```sh
cd backend
.venv/bin/python manage.py createsuperuser
```

Open `http://localhost:8000/admin/`, sign in as the administrator, then use **Users → Add user** to create an ordinary active player with a password. Use that player's username/password in the Unity sample. Django admin also provides password changes. An existing development account can be given a password there; its existing player profile and balances are preserved.

The first verified login provisions missing player/profile wallets with zero balances. Use Django admin to fund test wallets. There is no automatic development currency grant on password login. Failed login never creates a user. The separate username-only development provider remains available explicitly for development accounts; it cannot bypass accounts with passwords and is disabled by Django outside DEBUG mode.

The Unity adapter requires HTTPS for non-loopback servers and refuses redirects. Local `localhost`/`127.0.0.1` HTTP remains available for development. Passwords are masked and cleared on submission; they are not saved. Session tokens stay in memory through the context-owned `DjangoPlayerSession`. Access tokens renew 30 seconds before expiry; refresh requests are shared and recovery is bounded. Terminal renewal failure clears credentials and disconnects the sample. Restart the sample context to sign in again. The sample requires signed multiplayer identity verification before player spawn or player-state observation; hosts verify their local player too. See [identity proof configuration and protocol](multiplayer-identity-verification.md).

For a headless sample server, launch the desktop executable with `-batchmode -nographics -liveops-server`. The gate verifies peers with configured public trust; this role does not sign in to a player account. The sample spawner still requires its configured player prefab. The gate can be used separately without sample UI or prefabs. Unity and builds load `Assets/StreamingAssets/LiveOpsIdentityTrust.json` by default; `LIVEOPS_IDENTITY_TRUST_FILE` overrides the path. Only public trust belongs in that file.

SQLite is a local development database. Concurrent first-login profile creation on Django's threaded development server can produce SQLite write-lock errors; use `--nothreading` locally. Production database/concurrency qualification remains part of the wider backend refactor.

## Verification

```sh
cd backend
.venv/bin/python manage.py check
.venv/bin/python manage.py makemigrations --check --dry-run
.venv/bin/python manage.py test shop
```

Unity: run both demo (`EditModeTests`, `PlayModeTests`) and package (`MultiplayerLiveOpsToolkit.Tests.EditMode`, `MultiplayerLiveOpsToolkit.Tests.PlayMode`) assemblies in Test Runner, then inspect the console after returning to Edit Mode. Current regression results and platform qualification are recorded in [docs/testing.md](../docs/testing.md). Test Framework is pinned to 1.3.9 to fix the earlier runner teardown exception. Backend checks run in GitHub Actions. Unity CI requires a separately configured Unity license workflow; it is not configured here.

