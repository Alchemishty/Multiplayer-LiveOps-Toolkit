# Multiplayer Identity proof protocol v1

Status: Django issuance, Unity verification, authenticated adapter and sample Netcode handshake/spawn/public-state gating implemented and tested. See [architecture decisions](architecture-decisions.md) for the authority and verification choices behind this protocol.

## Authority and scope

A client proves its authenticated backend identity to a host or dedicated server without sending account access/refresh tokens to peers. The backend derives player ID/display name from the authenticated account/profile; it never signs caller-supplied identity. Identity assertions do not prove cosmetic ownership, equipment validity or transaction authority, and cannot make a malicious host trustworthy.

`POST /api/auth/identity-proof/` requires an active authenticated account and an existing player profile. Its JSON body contains exactly `session_id` and `challenge`. A successful response contains exactly `assertion`, an opaque signed string. Unknown fields or invalid context return 400; missing profile/invalid public identity return 409; invalid signing configuration returns a redacted 503 with `code: proof_configuration`. The endpoint is throttled at 20/minute per authenticated user using the configured Django cache; this is request throttling, not distributed replay protection.

## Wire format

A compact JWT using RS256 (RSA PKCS#1 v1.5 with SHA-256) has exactly three header fields: `alg: RS256`, `typ: liveops-identity+jwt`, and `kid`, an explicitly trusted key ID. No token-supplied key, key URL or alternative algorithm is accepted. RSA keys must be 2048–4096 bits.

The payload has exactly these 13 fields. Unknown or duplicate fields are rejected.

| Field | Meaning and limits |
|---|---|
| `iss` | Expected issuer; 1–256 printable ASCII characters without spaces |
| `aud` | Expected game ID |
| `env` | Expected environment ID |
| `content` | Expected content compatibility version |
| `sub` | Stable player profile ID, supplied by the backend |
| `name` | Nonblank public display name, at most 600 UTF-8 bytes; no C0 controls or DEL |
| `sid` | Server-owned session identifier |
| `nonce` | Canonical unpadded base64url of a 32-byte per-peer random challenge (43 characters) |
| `iat`, `nbf`, `exp` | Integer UTC Unix seconds; `nbf == iat`, `exp > iat` |
| `jti` | Unique assertion ID, 32 lowercase hexadecimal characters |
| `ver` | Integer protocol version, exactly 1 |

Game/environment/content/player/session identifiers use `[A-Za-z0-9_.:-]`, length 1–128. Key IDs use the same characters, length 1–64. Base64url segments must be canonical. The verifier caps the entire assertion at 8192 characters, the decoded header at 512 bytes and JSON nesting at four levels; it requires strict UTF-8. Issue times are positive; the implementation bounds UTC values to its supported range near year 9999.

Backend lifetime defaults to 60 seconds and must be an integer from 1–300. The verifier requires explicit `MaxLifetimeSeconds` (1–300) and `ClockSkewSeconds` (0–30), with no implicit relaxed policy. It checks issuer/game/environment/content, session/challenge, issue/not-before/expiry and maximum lifetime. A challenge has its own expiry with no skew extension. Peers need synchronized UTC clocks.

## Backend configuration

Install `backend/requirements.txt` (PyJWT is supplied by SimpleJWT; the pinned cryptography dependency enables RS256). Provision a dedicated unencrypted PEM RSA private key in a restricted location **outside the repository**. For example, generate it locally with OpenSSL in a directory readable only by the backend operator:

```sh
mkdir -p "$HOME/.config/liveops"
chmod 700 "$HOME/.config/liveops"
(umask 077; openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$HOME/.config/liveops/identity-proof.pem")
openssl pkey -in "$HOME/.config/liveops/identity-proof.pem" -pubout -out "$HOME/.config/liveops/identity-proof-public.pem"
```

Set the following process environment values before starting Django (see `backend/.env.example`). Key material must be mounted/provisioned separately for containers; the example Compose configuration does not provision it.

| Variable | Example |
|---|---|
| `LIVEOPS_PROOF_PRIVATE_KEY_FILE` | Absolute path to `identity-proof.pem` |
| `LIVEOPS_PROOF_KEY_ID` | `identity-2026-01` |
| `LIVEOPS_PROOF_ISSUER` | `https://identity.your-game.example` |
| `LIVEOPS_GAME_ID` | `your-game` |
| `LIVEOPS_ENVIRONMENT` | `development` |
| `LIVEOPS_CONTENT_VERSION` | `catalog-1` |
| `LIVEOPS_PROOF_LIFETIME_SECONDS` | `60` |

There is no fallback to Django's shared JWT secret or an embedded development key. Missing configuration disables proof issuance; the sample reports the configuration failure and disconnects without spawning a player. Private keys, account tokens and raw assertions must never be logged or committed. All non-loopback credential requests require HTTPS.

## Public trust and verifier ownership

Provision trusted RSA public modulus/exponent as `RSAParameters` to `IdentityProofVerifier`, keyed by the same `kid`, together with a matching `IdentityProofPolicy`. Pass public parameters only; private fields are rejected. The verifier validates and copies the supplied trust map/arrays. It never downloads keys from an assertion. `IdentityTrustConfiguration.CreateVerifier` reads public-only JSON exported by Django. The export uses standard base64 modulus/exponent, not PEM text; strict parsing rejects duplicates, unknown fields and invalid policy/key bindings. The JSON supports up to eight keys for explicit rotation overlap.

Rotation uses an explicit overlap of trusted public keys. Construct a new verifier with the updated map; changing the original map does not update an existing verifier. Distribute the next public key before switching the backend signer; remove old keys after the allowed lifetime/clock window. Removal rejects old-key assertions.

`IdentityChallenge.Create` generates a cryptographically random nonce. The server must retain **one instance per pending peer**, bound to the actual transport connection and session. Verification atomically consumes that instance only on success; malformed attempts do not consume it. Never reconstruct it from response data, reuse its nonce or create multiple instances for the same peer challenge. `jti` is not a global replay cache: replay protection depends on this server-owned single-use challenge.

`DjangoIdentityProofAPI` implements `IIdentityProofBackend` through an explicitly supplied `IAuthenticatedSession`. It sends only session/challenge in the body, treats the result as opaque, rejects late account responses and reports typed request failures. The operation owner must drive/dispose its coroutine during context teardown; callbacks complete once for a started operation. `NetcodeIdentityGate` owns and disposes the operation in the sample, with a bounded handshake deadline. `PlayerContext` now owns the player session, while the existing gate owns server verification. Broader module composition remains subsequent work.

The implementation uses library cryptography and JSON parsing: [PyJWT encoding/verification API](https://pyjwt.readthedocs.io/en/latest/api.html), [Microsoft cryptographic signatures](https://learn.microsoft.com/en-us/dotnet/standard/security/cryptographic-signatures). Newtonsoft.Json is an explicit engine-independent Identity dependency for bounded parsing and duplicate-field rejection.

## Export public trust for Unity

After configuring the signing key and context in the backend process environment:

```sh
cd backend
mkdir -p ../Unity/Assets/StreamingAssets
.venv/bin/python manage.py export_identity_trust > ../Unity/Assets/StreamingAssets/LiveOpsIdentityTrust.json
```

This file contains only public keys/context and may be bundled in a configured game build. Commit its Unity `.meta` together with the public file if tracking deployment configuration. Keep the private PEM outside Unity and the repository. Alternatively export to an external path and set `LIVEOPS_IDENTITY_TRUST_FILE` in the environment of the Unity editor or executable. Launching Unity through a GUI may not inherit shell environment variables. Do not fetch trust from an untrusted peer or from assertion headers. Re-export/distribute trust before switching signing keys.

## Implemented Netcode integration

The sample gate admits a transport connection with `CreatePlayerObject=false`. A fresh session GUID and canonical random nonce bind one challenge to one connected peer. The client accepts challenges/acknowledgements only from Netcode server ID 0 and sends only the assertion back. Hosts use this same challenge/request/verification path for their local player. Server-only sessions verify remote players without account login or private signing keys.

A valid proof consumes the pending challenge and binds identity once. Invalid, expired, repeated or wrong-context proofs disconnect the affected remote peer; local host failure shuts down its session. Deadlines are 15 realtime seconds by default, configurable 1–60; at most 32 challenges may be pending. A transient backend request gets at most two attempts with a one-second realtime delay, bounded by the same deadline. Auth/configuration/rejected/invalid responses are terminal. Late account/session callbacks cannot publish identity. Disconnect and disable/shutdown remove bindings and dispose outstanding requests.

The sample spawner runs only after verification. It withholds sample player visibility from unverified peers, reveals existing players after verification and initializes the new player's public ID/display name before spawning. Nameplate NetworkVariables permit server writes only. Game-specific replicated objects must use appropriate visibility rules too; the gate is not a universal firewall for arbitrary gameplay custom messages or pre-existing scene objects.

Validated on Unity 2022.3/macOS with editor-hosted real Unity Transport tests and separate standalone processes against Django: host plus client, and headless server-role plus two clients. Windows and other server operating systems remain release qualification work. This slice does not establish backend-approved equipment or complete idle-backend health monitoring.

## Equipment protocol v1 (implemented)

Equipment uses the same public trust policy and asymmetric key configuration, with a distinct `liveops-equipment+jwt` type. Its payload has the thirteen identity claims plus `revision` (nonnegative signed 64-bit integer below `long.MaxValue`) and `items` (at most 32 unique slot-to-cosmetic string ID pairs). IDs use the existing bounded identifier alphabet, up to 128 characters. The maximum equipment assertion is 32768 ASCII bytes; identity keeps its 8192-byte bound. Exact claim sets, duplicate JSON properties and protocol types are checked before consuming the challenge.

The authenticated `/api/equipment/proof/` request accepts only `session_id` and canonical `challenge`. Django signs its current canonical equipped flags and revision, never a client-provided loadout. `/api/equipment/loadout/` GET returns definitions/state; POST accepts only `item_ids` and `expected_revision`. Ownership, configured symmetric conflicts and a revision compare-and-swap are checked inside one account transaction. A set application is one replace request. Old get-equipped/update-equipped routes and the client's raw loadout RPC are removed.

When Equipment is configured, identity verification creates a separate equipment challenge before publishing the peer identity or spawning/revealing players. The server verifies proof context and the already verified player ID, then retains only the sanitized EquipmentLoadout. Later challenges accept newer revisions, or an identical loadout at the same revision; older revisions and same-revision different contents are rejected. Fresh per-peer challenges prevent assertion replay. Existing verified peers keep their public state until another proof passes; rejected peers are disconnected.

The private `liveops.equipment.accepted.v1` acknowledgement contains only the accepted revision. A newer confirmed local mutation during an in-flight proof schedules another proof after the current runner completes; host loopback delivery is synchronous. This retries an authoritative read/proof, never the mutation. The public NetworkVariable contains only `{revision, items}`; wallets, Inventory, credentials and assertions are not observer state.

The sample spawner installs the gate's approved snapshot before spawn, and game-owned respawn can initialize a replacement from that same current gate snapshot. Late join receives the current public NetworkVariable. Dedicated validation has no player account, UI or prefab requirement; the sample avatar spawner is optional composition. The sample's visual clients validate required local mappings and disconnect with cosmetic/revision diagnostics when mappings are invalid. Preview is local-only and is rebound through avatar lifecycle.

An Equipment-enabled client requires an equipment acknowledgement before the final identity/ready acknowledgement. An identity-only server therefore cannot silently admit that client without the selected verification feature; the client reports a configuration failure and disconnects.
