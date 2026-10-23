# Multiplayer LiveOps Toolkit

Private development repository for a Unity multiplayer live-ops toolkit and its Django REST backend. It currently provides an integrated example of shop purchases, inventory and accessory sets, wallet/currency conversion, daily login rewards, achievements, avatar cosmetics, and player nameplates.

This repository preserves the current working Unity example and its reference backend.

## Repository layout

- `Assets/`, `Packages/`, `ProjectSettings/`: Unity project; open this repository root in Unity Hub.
- `backend/`: Django server, migrations, tests, and seed command.
- `docs/`: architecture, domain contracts, and testing.
- `enforcement/`, `AGENTS.md`: development conventions and change requirements.

## Requirements

- Unity **2022.3.33f1** (see `ProjectSettings/ProjectVersion.txt`).
- Python **3.11** for the backend, or Docker Compose.
- Git and Git LFS. Binary assets use LFS; Unity YAML and `.meta` files stay in ordinary Git.

## Clone

```sh
git lfs install
git clone https://github.com/Alchemishty/MultiplayerLiveOpsToolkit.git
cd MultiplayerLiveOpsToolkit
git lfs pull
```

Access requires permission to the private repository. Commit asset changes together with their `.meta` files. The project uses Visible Meta Files and Force Text serialization. Unity caches, builds, local databases, credentials, and virtual environments are excluded.

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
python manage.py runserver 127.0.0.1:8000
```

PowerShell activation is `.venv\Scripts\Activate.ps1`; set the variables from `.env.example` in that shell. Django reads process environment variables, so creating `.env` alone does not load it. Use the seed command for the current schema; the imported `initial_data.json` fixture is legacy data from an earlier schema.

Alternatively, from the repository root:

```sh
cp backend/.env.example backend/.env
docker compose up --build
```

This starts the local development backend at `http://localhost:8000` and persists SQLite data in a Docker volume. Migrations are committed and applied, never generated at container startup. These examples use development configuration; deployment requires production secrets, authentication, host/TLS settings, and a database/operations review.

## Run Unity

1. Add the repository root in Unity Hub and open it with 2022.3.33f1. Allow packages and assets to import.
2. Open `Assets/Scenes/ExampleScene.unity` and start the backend.
3. Enter Play Mode, type a development username, and click Play.
4. The first instance becomes a Netcode host after a connection timeout; additional instances connect to it. Run builds from Unity's Build Settings with ExampleScene enabled.

The editor setup command `Tools/Setup Example Scene` regenerates the example scene and prefab wiring. See `Assets/Editor/SetupExampleScene.cs` before changing scene composition. API requests default to `http://localhost:8000`; `ApiConfig.SetBaseUrl` supports an override. The development login/reset endpoints require `DEBUG=True` and are unsuitable as production authentication.

## Verification

```sh
cd backend
.venv/bin/python manage.py check
.venv/bin/python manage.py makemigrations --check --dry-run
.venv/bin/python manage.py test shop
```

Unity: run `EditModeTests` and `PlayModeTests` in Test Runner, then inspect the console after returning to Edit Mode. The baseline has 198 EditMode tests, one nameplate-material PlayMode regression, and 65 backend tests. Test Framework is pinned to 1.3.9 to fix the earlier runner teardown exception. Backend checks run in GitHub Actions. Unity CI requires a separately configured Unity license workflow; it is not configured here.

## Distribution

This private development snapshot is not the Asset Store deliverable. No open-source license is granted by this repository. Third-party material retains its own terms; see [third-party notes](THIRD_PARTY_NOTICES.md). Package licensing and redistributable sample assets must be settled before publication.
