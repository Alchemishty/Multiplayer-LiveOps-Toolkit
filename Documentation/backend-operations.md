# Reference backend and content operations

This Django backend demonstrates the toolkit mechanics. Games replace or extend it through the published backend contracts. It does not supply production economy history, receipt processing or a deployment platform.

## Local setup and migrations

Follow [README](../README.md) for environment loading, Python dependencies, migrations, seed and admin-created accounts. `.env` is not loaded automatically by manage.py. Use an isolated `DB_PATH` for tests; never test migrations/reset against the only copy of player data. Back up an existing database before applying migrations and retain a restore procedure. Apply committed migrations with `manage.py migrate --noinput`; do not generate migrations at server startup.

The current schema includes equipment revision ordering and achievement claim state. Qualification creates a fresh database from the committed migrations. It does not certify migration of an arbitrary production database or rollback of newer player data.

`manage.py seed` creates/updates sample content and replaces seeded price rows. It is repeatable for demo setup but is not a production content migration policy. The legacy `initial_data.json` fixture does not represent the current schema. Local SQLite should use `runserver --nothreading` to avoid overlapping first-login write-lock upgrades.

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

A `manage.py check --deploy` run with DEBUG false and ephemeral strong secrets reports four outstanding settings warnings in the unmodified sample: HSTS (`security.W004`), HTTPS redirection (`W008`), secure session cookie (`W012`) and secure CSRF cookie (`W016`). Supply reviewed environment-specific Django settings and HTTPS proxy behavior before public deployment; the sample currently does not expose environment toggles for those settings. Re-run the deployment check against the settings actually deployed. Do not suppress warnings to claim qualification.

For an actual game deployment, choose and qualify the database/concurrency behavior, shared throttle cache, web-server/proxy timeouts, secure admin access, secret rotation, logging privacy and backup/restore. Existing local transaction tests do not certify a multi-worker production service. These responsibilities belong to the consuming game, not additional mandatory frontend abstractions.

Tokens/passwords/assertions must stay out of logs. Keep only diagnostics needed to identify configuration and request failures. Development reset and username-only auth remain development-only endpoints; password-managed accounts cannot use the development bypass. Restart a terminated sample context for a fresh login. Idle backend-health detection remains an open product requirement, as noted under [current support](../README.md#current-support).
