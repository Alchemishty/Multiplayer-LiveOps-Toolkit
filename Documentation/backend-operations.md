# Reference backend and content operations

This Django backend demonstrates the toolkit mechanics. Games replace or extend it through the published backend contracts. It does not supply production economy history, receipt processing or a deployment platform.

## Local setup and migrations

Follow [Run the demo](getting-started.md) for environment loading, Python dependencies, migrations, seed and admin-created accounts. `.env` is not loaded automatically by manage.py. Use an isolated `DB_PATH` for tests; never test migrations/reset against the only copy of player data. Back up an existing database before applying migrations and retain a restore procedure. Apply committed migrations with `manage.py migrate --noinput`; do not generate migrations at server startup.

The current schema includes equipment revision ordering and achievement claim state. Qualification creates a fresh database from the committed migrations. It does not certify migration of an arbitrary production database or rollback of newer player data.

`manage.py seed` creates/updates sample content and replaces seeded price rows. It is repeatable for demo setup but is not a production content migration policy. The legacy `initial_data.json` fixture does not represent the current schema. Local SQLite should use `runserver --nothreading` to avoid overlapping first-login write-lock upgrades.

For a local Android build, keep Django on the development computer and use the [Android backend/forwarding setup](android.md#configure-the-backend-and-public-trust). The APK bundles exported public trust only; it does not contain the backend or private signing key. A remote backend must use HTTPS for player credential requests.

## Local Compose setup

Prepare `backend/.env` and signing/public trust using [Run the demo](getting-started.md#prepare-the-backend). Compose reads `.env` directly, unlike a plain Django shell. Mount the private key into the container and set its **container** path; the host key path alone is not accessible there.

For example, create `$HOME/.config/liveops/compose.local.yaml` outside the repository with the following contents. Replace `/absolute/host/path/identity-proof.pem` with your existing private-key path:

```yaml
services:
  backend:
    environment:
      LIVEOPS_PROOF_PRIVATE_KEY_FILE: /run/secrets/liveops-identity.pem
    volumes:
      - /absolute/host/path/identity-proof.pem:/run/secrets/liveops-identity.pem:ro
```

Run from the repository root after filling all signing context values in `backend/.env`:

```sh
docker compose -f docker-compose.yml -f "$HOME/.config/liveops/compose.local.yaml" up --build
```

In another terminal, create the administrator and export public trust from that same configured backend:

```sh
docker compose -f docker-compose.yml -f "$HOME/.config/liveops/compose.local.yaml" exec backend python manage.py createsuperuser
mkdir -p Unity/Assets/StreamingAssets
docker compose -f docker-compose.yml -f "$HOME/.config/liveops/compose.local.yaml" exec -T backend python manage.py export_identity_trust > Unity/Assets/StreamingAssets/LiveOpsIdentityTrust.json
```

Use your consuming project's StreamingAssets path instead when appropriate. Continue with [player accounts](getting-started.md#start-the-backend-and-create-player-accounts). Compose applies committed migrations and seeds content at startup; SQLite data persists in its named volume. This local arrangement does not configure production TLS or database operations.

## Content authoring

Use Django admin and existing management commands for this version:

- **Users / player profiles / Wallet:** create active users with passwords, then fund test currency balances. First password login provisions missing zero-balance wallets. Currency names are stable frontend IDs; the demo uses gold, snowflakes and gears.
- **Accessories / costs / sets:** preserve stable IDs. The full demo's IDs must match the saved demo prefabs and local visual catalog. Configure valid slot IDs, nonnegative integral prices and complete set membership. Body-part items in the demo are sold through sets. Partially owned sets retain the full offer price.
- **Equipment:** `LIVEOPS_EQUIPMENT_SLOTS` in Django settings defines allowed slots and symmetric conflicts. The frontend receives these rules. Update local visual mappings and content-version trust together when changing content compatibility. Missing required visuals deliberately disconnect with an ID/revision diagnostic.
- **Achievements:** configure active definitions, supported backend conditions and optional currency/cosmetic rewards in admin. Completion and explicit reward claim are separate. Purchase/spend counters come from successful backend purchases; attendance uses recorded claims. No client progress-write endpoint is provided.
- **Monthly reward schedules:** create rows for the current UTC year/month/day in admin. There is no monthly schedule seed command: the service automatically generates a default schedule if the entire month has no rows (gold must exist). Within an existing partial schedule, missing reward rows mean attendance-only days. The frontend claims exactly the displayed backend date and must refresh on rollover. Negative/overflowing or unexportable rewards reject atomically.

Do not edit equipped flags, claim state, balances or live definitions underneath active players as a normal game command. The admin is an operator tool; runtime mutations must go through the corresponding authenticated mechanic. Refresh/reconnect clients after deliberate administrative maintenance.

## Signing and deployment configuration

Use the [proof configuration/export guide](multiplayer-identity-verification.md). Keep the dedicated private RSA key exclusively on the backend, outside source/builds; clients and servers receive public trust only. Match issuer, game, environment, content version and key IDs. Roll out overlapping public keys before changing the signer. Keep clocks synchronized.

The supplied Compose file binds localhost and uses sample SQLite settings. It does not provision TLS, public/private proof files, backups or a production database. Merely changing `DEBUG=False` does not finish deployment configuration. Set independent Django/session secrets, explicit allowed hosts and restrictive key permissions; never ship the development defaults. Non-loopback Unity credential requests require HTTPS and reject redirects.

The unmodified sample has four outstanding `manage.py check --deploy` settings warnings with DEBUG false and strong secrets: HSTS (`security.W004`), HTTPS redirection (`W008`), secure session cookie (`W012`) and secure CSRF cookie (`W016`). Supply reviewed environment-specific Django settings and HTTPS proxy behavior before public deployment; the sample currently does not expose environment toggles for those settings. Re-run the deployment check against the settings actually deployed. Do not suppress warnings to claim qualification.

For an actual game deployment, choose and qualify the database/concurrency behavior, shared throttle cache, web-server/proxy timeouts, secure admin access, secret rotation, logging privacy and backup/restore. Existing local transaction tests do not certify a multi-worker production service. These responsibilities belong to the consuming game, not additional mandatory frontend abstractions.

Tokens/passwords/assertions must stay out of logs. Keep only diagnostics needed to identify configuration and request failures. Development reset and username-only auth remain development-only endpoints; password-managed accounts cannot use the development bypass. Restart a terminated sample context for a fresh login. Idle backend-health detection remains an open product requirement, as noted under [current support](../README.md#current-support).
