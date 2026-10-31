# Run the demo

This walkthrough runs the repository's saved demo with a local Django backend and two player accounts. Complete backend, account and public-trust setup before entering Play Mode.

For a fresh consuming project, first [install the toolkit](../Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Documentation~/installation.md). Use its imported Full Demo scene and export trust into that project's Assets directory in the steps below. The repository demo uses Unity 2022.3; [Unity 6](unity6.md) uses a separate consuming project.

## Requirements

- Unity **2022.3.33f1** for the repository project.
- Python **3.11**, Git and Git LFS.
- Two different player accounts for the multiplayer check.

## Clone the repository

```sh
git lfs install
git clone https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit.git
cd Multiplayer-LiveOps-Toolkit
git lfs pull
```

## Prepare the backend

From the repository root, create the environment and database:

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
python manage.py createsuperuser
```

Keep this shell open for the signing configuration and server startup below. `.env` is not loaded automatically by Django: source it whenever you open a backend shell. On PowerShell, activate `.venv\Scripts\Activate.ps1` and set the environment variables from `.env.example` explicitly.

The seed command supplies current sample content. The legacy `initial_data.json` fixture is not compatible with the current schema. SQLite is for local development; use `--nothreading` below to avoid overlapping first-login write-lock errors.

## Configure local signing and public trust

The backend signs account identity and equipment; Unity hosts/servers verify those signatures with public keys. A missing signer or mismatched public trust prevents spawning.

Generate a dedicated local RSA key **once**, outside the repository. Reuse your existing key if you already have one; do not overwrite it:

```sh
mkdir -p "$HOME/.config/liveops"
chmod 700 "$HOME/.config/liveops"
(umask 077; openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$HOME/.config/liveops/identity-proof.pem")
```

In the same backend shell, configure a consistent local context:

```sh
export LIVEOPS_PROOF_PRIVATE_KEY_FILE="$HOME/.config/liveops/identity-proof.pem"
export LIVEOPS_PROOF_KEY_ID=local-demo-1
export LIVEOPS_PROOF_ISSUER=https://identity.example.invalid
export LIVEOPS_GAME_ID=toolkit-demo
export LIVEOPS_ENVIRONMENT=development
export LIVEOPS_CONTENT_VERSION=catalog-1
export LIVEOPS_PROOF_LIFETIME_SECONDS=60
mkdir -p ../Unity/Assets/StreamingAssets
python manage.py export_identity_trust > ../Unity/Assets/StreamingAssets/LiveOpsIdentityTrust.json
```

The issuer is an identifier, not a URL Unity contacts. Record these values in your local ignored `backend/.env` for later sessions; use an absolute private-key path there. The exported JSON contains **public keys and context only** and may be bundled with Unity builds. Never copy the private PEM into Unity. For a consuming project, replace the export destination with its `Assets/StreamingAssets/LiveOpsIdentityTrust.json` path.

Unity reads that public file by default. `LIVEOPS_IDENTITY_TRUST_FILE` can override it, but a GUI-launched Unity editor may not inherit shell environment variables. Key rotation and validation rules are in the optional [protocol reference](multiplayer-identity-verification.md).

## Start the backend and create player accounts

In the configured backend shell:

```sh
python manage.py runserver 127.0.0.1:8000 --nothreading
```

1. Open `http://localhost:8000/admin/` and sign in with the administrator you created.
2. Under **Users > Add user**, create two ordinary active users with passwords.
3. Sign each player in once in Unity after completing the next section. First login creates missing player profiles and wallets with **zero** balances.
4. Return to admin and fund each player's test wallets. Refresh the Unity account views after funding them.

The demo uses password login; it has no self-service registration and grants no starting currency automatically. Failed login creates no user. Existing development accounts can receive passwords through admin while retaining their profiles and balances. Username-only development login/reset are separate DEBUG-only tools.

For Docker instead of Python server startup, see [local Compose setup](backend-operations.md#local-compose-setup). Keep signing configuration and exported public trust consistent whichever server you use.

## Open Unity and sign in

1. Add the repository's **Unity/** folder to Unity Hub and open it with 2022.3.33f1. Allow packages/assets to import.
2. Open **Assets/Scenes/ExampleScene.unity**. In a consuming project, open the imported Full Demo scene instead.
3. Confirm the backend is running and the public trust JSON is in this project's **Assets/StreamingAssets/** folder.
4. Enter Play Mode, enter the first player's username/password and click **Sign in**.
5. Wait for the connection attempt to time out and the first instance to become host. The login panel hides when the local avatar is registered.

The backend URL defaults to `http://localhost:8000`. To change it, call `ApiConfig.SetBaseUrl` before authentication starts. Non-loopback credential requests require HTTPS and reject redirects. Passwords clear on submission; tokens stay in memory, so a restarted context requires another sign-in.

## Run a second instance and verify

1. Add ExampleScene to **Build Settings > Scenes In Build**, build a desktop player and launch it alongside the editor.
2. Sign in with the second account. The default local connection joins the first instance on port 7777.
3. Confirm both avatars and their player names appear in each instance.
4. Preview a cosmetic: only the previewing player sees the change.
5. Buy and equip an affordable cosmetic: balances/ownership update, then the approved appearance becomes visible to the other player.
6. Open Achievements and Daily Rewards. Confirm state loads; claim an eligible reward and refresh to confirm the persisted result.

For a headless server, launch the desktop executable with `-batchmode -nographics -liveops-server`, with public trust configured. It verifies players without a local account. This is a desktop headless role, not qualification of Unity's dedicated-server build target.

For Android, continue with [Android setup and build](android.md). Its UI supports touch; supplied movement/camera controls use keyboard input.

## Troubleshooting

| Symptom | Check |
|---|---|
| Login fails | Active user, correct password, backend running; failed login does not register an account. |
| No avatar / proof configuration error | Backend signing variables, private-key readability, exported trust in the correct Unity project, and matching context/key ID. |
| Expired or not-yet-valid proof | Synchronize host/client/backend clocks. |
| Cannot afford any item | Fund the player wallets in admin, then refresh the account views. |
| Second instance becomes another host | Start the first instance fully; confirm address/UDP port 7777 and firewall reachability. |
| Session ends after renewal failure | Resolve the backend/authentication failure, then restart the sample context and sign in again. |

For developer checks, see [testing](../docs/testing.md). For content changes and deployment limits, see [backend operations](backend-operations.md).
