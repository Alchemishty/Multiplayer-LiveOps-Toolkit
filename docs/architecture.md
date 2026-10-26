# architecture.md

This document describes the current implementation. Public integration behavior and current boundaries are described in [package integration](../Documentation/package-integration.md); Core, Identity, Wallet, cosmetic Inventory, Shop, Equipment, Achievements and Daily Rewards are implemented in the private UPM preview. The existing full demo consumes the package; current support and limitations are summarized in the [README](../README.md#current-support).

## Layer Model

```
Saved demo scene/prefabs                — authored composition over package runtime
Tests  (EquipmentModuleTests, ShopCatalogTests, ...) — references runtime assemblies
  ↑ nothing depends on these
──────────────────────────────────────────────────────────────
Example (UI, bootstrap, visuals)        → Player, Networking, Registry, API, Data
Player  (NetworkBehaviours, sync)       → Networking, Identity, Registry, API, Data
Networking (identity handshake)        → Identity, Netcode
Registry (AccessoryRegistry)            → API, ScriptableObjects, Data
API     (DjangoShopBackend, DjangoPlayerSession, DjangoWalletBackend) → Identity, Wallet, Core, Data
ScriptableObjects (AccessorySO)         → Data
Data    (DTOs, enums, pure C#)          → (no internal deps)
```

Dependency direction is top-down. Legacy gameplay uses global C# types in `MultiplayerLiveOpsToolkit`. The new `MultiplayerLiveOpsToolkit.Core` assembly uses the matching `.Core` namespace and disables Unity engine references. The `MultiplayerLiveOpsToolkit.Identity` assembly is also engine-independent. It references Core and uses the existing Newtonsoft.Json library for strict identity-proof parsing; it has no Unity or legacy-project dependencies. Legacy layer boundaries remain conventions checked by type references and directory paths. See `enforcement/layer-imports.md`.

## Key Types

| Layer | Type | Role |
|---|---|---|
| Data | `AccessoryType` | Enum of equip slots (Headwear, MainHand, etc.) |
| Data | `AccessoryState` | INetworkSerializable JSON wrapper for equipped-slot map |
| Data | `ShopCatalog` | Runtime merged catalog: backend pricing + local prefabs |
| Data | `ShopApiDTOs` | `AccessoriesResponse`, `AccessorySetResponse`, `AccessoryCost`, etc. |
| API | `DjangoPlayerSession` | Instance-owned in-memory JWT session, shared renewal and explicit authenticated requests |
| Shop | `ShopModule` | Independent catalogs and coordinated backend-confirmed purchases |
| API | `DjangoShopBackend` | Explicit-session catalog/purchase transport |
| Equipment | `EquipmentModule` | Independent canonical loadouts, configured conflicts and local preview |
| API | `DjangoEquipmentBackend` | Explicit-session canonical loadout transport |
| Wallet | `WalletModule` | Independent confirmed balances and backend-defined conversions |
| API | `DjangoWalletBackend` | Injected-session reference Wallet transport |
| ScriptableObjects | `AccessorySO` | Master catalog asset with O(1) lookups by ID/prefab |
| Registry | `AccessoryRegistry` | Singleton bridging `AccessorySO` + `ShopCatalog`; loads on network connect |
| Player | `PlayerAccessory` | NetworkBehaviour: syncs equipped accessories via `NetworkVariable<AccessoryState>` |
| Player | `ClientPlayerAvatars` | Tracks all spawned avatars by client ID; exposes local avatar |
| Player | `IAccessoryVisualSync` | Interface decoupling visual attachment from network sync |
| Player | `ClientNetworkTransform` | Client-authoritative NetworkTransform — owner's position syncs to all clients |
| Player | `ClientNetworkAnimator` | Client-authoritative NetworkAnimator — owner's animation params sync to all clients |
| Example | `AccessoryPool` | Implements `IAccessoryVisualSync`; pools/bone-routes accessory instances |
| Identity | `IdentityModule` | Establishes a backend-confirmed player identity through provider/backend contracts |
| API | `DjangoIdentityBackend` | Exchanges Django password/development credentials for a session |
| Example | `DevAuthBootstrap` | Presents login UI through Core/Identity initialization |
| Example | `AutoConnect` | Tries Netcode Client, falls back to Host |
| Example | `ShopPanel`, `InventoryPanel`, `SetsPanel` | Browse, purchase, preview, and equip items or sets |
| Example | `AccessoryEquipService` | Coordinates local item/set previews and committed equipment |
| Example | `ShopUIFactory`, `PurchaseConfirmationPrompt` | Shared UI construction and purchase confirmation |
| Achievements | `AchievementsModule` | Backend-confirmed completion and explicit reward claims |
| API | `DjangoAchievementBackend` | Explicit-session achievement reads/claims |
| Example | `AchievementSampleController` | Durable claim/refresh owner independent of panel visibility |
| DailyRewards | `DailyRewardsModule` | Backend-date calendar and atomic attendance/currency claims |
| API | `DjangoDailyRewardBackend` | Explicit-session Daily Rewards transport |
| Example | `DailyRewardsSampleController` | Durable calendar command owner and confirmed attendance notifications |
| Player | `PlayerNameplate` | Server-verified public identity, host styling and distance fade |

## Bootstrap Ordering

```text
1. DevAuthBootstrap → PlayerContext → Identity, then explicitly selected account modules.
2. Bind service-backed Wallet/Inventory/Shop/Equipment UI and optional sample visual mappings.
3. AutoConnect starts the identity gate; clients authenticate their own backend requests.
4. Gate verifies peer identity, then signed equipment when enabled, before visibility/spawn.
5. VerifiedPlayerSpawner installs approved identity/loadout before spawning the prefab.
6. PlayerAccessory publishes server-approved revision/IDs; clients resolve local visuals.
7. Local previews override only local presentation; confirmed mutations request a new proof.
```

These are dependency relationships, not a guaranteed execution order across components. Achievements initializes through Core after its explicit Identity/economy dependencies; it does not wait for Netcode. The login panel hides after the local avatar is registered.

## Singleton Policy

| Singleton | Lifetime | Access | Auto-creates? |
|---|---|---|---|
| `DjangoPlayerSession` | Owning player context/GameObject | Explicit sample composition | No |
| `AccessoryRegistry.Instance` | DontDestroyOnLoad | Scene-placed | No |
| `ClientPlayerAvatars.Instance` | Scene | Scene-placed | No |
| `AccessoryEquipService.Instance` | Scene | Scene-placed | No |
| `DevAuthBootstrap.Instance` | Scene | Scene-placed | No |
| `WalletDisplay.Instance` | Scene | Scene-placed | No |
| `TabManager.Instance` | Scene | Scene-placed | No |

`AccessoryPool` is a component on each player prefab, with independent instance and active-slot dictionaries. It has no singleton. `PlayerAccessory` resolves it through `GetComponent<IAccessoryVisualSync>()`. API auto-creation is an existing supported pattern; required scene dependencies should fail explicitly when missing.

---

## Structural Standards (enforced for all new/modified code)

**1. Layer boundaries are convention-enforced.** Lower layers must not reference higher layers per the diagram above. `Data` must never import `API`, `Player`, or `Example`. `Player` must never import `Example`.

**2. NetworkBehaviour boundaries.** `PlayerAccessory` and `PlayerAvatarSetup` must contain ONLY: `NetworkVariable` declarations, `OnValueChanged` callbacks, RPCs, `OnNetworkSpawn`/`OnNetworkDespawn`, ownership checks. All business logic (conflict resolution, JSON building, slot management) must live in pure C# classes testable in EditMode.

**3. Singleton access must throw on null.** All `.Instance` access must use: `Registry.Instance ?? throw new InvalidOperationException($"{nameof(Registry)} not initialized")`. Never silently return or no-op on null.

**4. Scene composition contracts.** Every scene entry point (`DevAuthBootstrap.Awake`, `AccessoryRegistry.Awake`, `ClientPlayerAvatars.Awake`) must validate required sibling objects and `[SerializeField]` references. Missing required object = `throw`, not silent degradation. Example: `AccessoryRegistry.Awake` must validate `masterCatalogSO != null`.

**5. Saved demo assets own composition.** `Assets/Scenes/ExampleScene.unity`, its prefabs and catalogs are authoritative. Edit their serialized wiring directly; there is no scene generator. `tools/sync_full_demo.py` copies the authored scene, art, presentation and metadata into the package's optional Full Demo sample. Runtime assemblies remain independent of these assets. Import the sample only in consuming projects, since the source repository already contains those GUIDs and types.

**6. Client authority for player-driven sync.** The default `NetworkTransform` and `NetworkAnimator` are server-authoritative — the server is the only source of truth. This works for the host (who IS the server) but silently breaks clients: client movement and animation changes are ignored because the server never originated them. Use `ClientNetworkTransform` and `ClientNetworkAnimator` (which override `OnIsServerAuthoritative() → false`) for any player-driven system where the owning client is the source of truth. This is the single most common Netcode pitfall — it appears to work in single-player/host testing but fails silently for clients.

**7. NetworkBehaviours must be baked on prefabs.** Never add a `NetworkBehaviour` via `AddComponent` after the `NetworkObject` is spawned. Netcode does not properly register late-added NetworkBehaviours — `IsOwner`, `IsServer`, RPCs, and NetworkVariables may not function. All networked components (`SimplePlayerMovement`, `PlayerAccessory`, `ClientNetworkTransform`, `ClientNetworkAnimator`) must be on the prefab at spawn time.

## Authority and persistence limits

Movement and animation remain owner-authoritative. Equipment replication is server-written only after signed equipment verification; there is no client loadout RPC. The backend validates ownership and configured slot rules, while proofs bind the canonical revision and IDs to the verified peer/session/content policy.

Equipment and Achievements use explicit-session commands without automatic mutation retry. Achievements accepts backend-confirmed progress only. The sample backend advances purchase/spend counters inside successful Shop transactions and derives collections/recorded attendance from backend state. Claims grant rewards once using a locked progress row and one claimed flag. Unknown responses require a read refresh.

## Modular Core: implemented foundation

`Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Runtime/Core/` contains a pure C# lifecycle foundation in a separate assembly with `noEngineReferences: true`. It has no dependency on the legacy gameplay assembly, Netcode, UI, or HTTP clients.

- `ILiveOpsModule` declares a stable `Id`, required module IDs, an initialization coroutine, and synchronous local `Shutdown` cleanup.
- `ModuleDependencyResolver` validates the explicitly supplied selection and produces deterministic dependency order. Missing dependencies, cycles, duplicates and invalid metadata throw before initialization side effects. Dependencies are never implicitly selected.
- `LiveOpsRuntime` initializes that plan, reports lifecycle failures, rolls back partially started modules and performs idempotent reverse-order shutdown.
- `LiveOpsError` carries the lifecycle code, module context, diagnostic and original exception (including rollback exceptions). Backend/business error categories will be introduced alongside their actual consumers.

```csharp
var runtime = new MultiplayerLiveOpsToolkit.Core.LiveOpsRuntime(selectedModules);
yield return runtime.Initialize(onReady, onFailure);
// The context owner calls this when ending its lifetime:
runtime.Shutdown();
```

Module constructors and metadata getters must be side-effect free; IDs are stable for the instance lifetime. The selection/dependency plan is captured at construction. Supply context-owned module instances, not one mutable module shared between player/server contexts. Initialization starts when its returned IEnumerator is executed, not when merely created. Only one operation can begin; a stopped runtime cannot be reused.

An initialization hook returns a non-null IEnumerator. Exhaustion means successful initialization; exceptions become `ModuleInitializationFailed`. Nested enumerators are driven inside the lifecycle coordinator so exceptions and disposal cannot bypass rollback; ordinary yielded wait objects are forwarded to the caller's coroutine runner. If a hook fails after acquiring resources, its own cleanup is attempted before earlier modules. Shutdown during initialization or disposal of an active outer operation reports `InitializationCancelled` exactly once.

Module shutdown releases local resources synchronously and must not wait for backend responses. Cleanup continues after an independent module throws. Explicit shutdown reports collected cleanup exceptions through AggregateException after reaching Stopped; initialization failures retain those exceptions in the error cause. Terminal callbacks run after state/resources are finalized. Consumer callback exceptions propagate to the caller without changing an already established Ready/Stopped outcome or invoking the opposite callback. The single operation completion callback is not a multicast event subscription.

Drive this API on the owning Unity thread. The future operation runner must explicitly invoke Shutdown when its owner ends; stopping a Unity coroutine or destroying a sample view is not an ownership policy. Core has no hidden polling, thread scheduler, global instance, or scene lookup.

Core is now used by the sample's Identity initialization below. Explicit player ownership and the existing server verification owner are implemented below. Module settings assets, idle health deadlines and a broader lifecycle framework are deferred until a concrete module requires them. Authentication/handshake terminal disconnection is implemented.

## Identity: initial sample integration

`Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Runtime/Identity/` defines `IdentityModule`, `IIdentityProvider`, `IIdentityBackend`, `IdentityCredential`, and `PlayerIdentity` in a separate engine-independent assembly. The module obtains a credential from the supplied provider, exchanges it through the supplied backend, then exposes `CurrentPlayer` only after successful authentication. Identity contains no HTTP, UI, token storage, scene lookup, or global instance.

Provider/backend routines complete exactly once with a non-null result before exhaustion; a missing/duplicate completion or null routine fails initialization through Core rollback. Exceptions currently become Core lifecycle failures. Adapters must sanitize diagnostics; never include credentials or raw authentication responses. Callbacks arriving after their phase closes or after shutdown are ignored. The backend owns its private session, clears it synchronously during rollback/shutdown, and must not publish private state after cancellation. External provider objects are not disposed by the module.

The sample composes `DjangoPasswordIdentityProvider`, `DjangoIdentityBackend`, and `IdentityModule` into `LiveOpsRuntime`. The one-use provider packages the username/password credential for `POST /api/auth/password/` and drops its reference after supplying it. The bootstrap clears the masked password field immediately on submission and requires both fields before enabling Sign in. The sample works outside Editor/Development Builds through this password path.

Django authenticates an existing, active User through its configured authentication backend. User creation/password management belongs to Django admin for this increment. A verified first login provisions a missing player profile plus zero-balance wallets in a transaction; later logins retain identity and economy state. Invalid credentials, unknown accounts and inactive accounts receive the same 401 response. Login attempts use the `password_login` throttle scope (10/minute per IP by default). DRF's local-cache throttle is an application usage limit, not a complete brute-force defense; deployment-level rate limits/shared cache and TLS are still deployment responsibilities ([DRF documentation](https://www.django-rest-framework.org/api-guide/throttling/)).

The explicit `DevelopmentIdentityProvider` remains Editor/Development Build only, and Django blocks `/api/auth/dev/` outside DEBUG. That endpoint now also rejects existing password-managed/inactive/non-development accounts. There is no automatic development-provider fallback from password login.

Both responses carry `player_id`, the existing profile primary key serialized as a string, stable for the account within its backend database. It is not a signed multiplayer identity proof. The legacy platform nonce endpoint is unchanged. Signed peer assertions additionally bind the configured game/environment/content, as described below.

The Django adapter validates the complete response before publishing tokens, uses a bounded 15-second request timeout, and avoids raw response text in errors. It requires HTTPS except on loopback endpoints, rejects URL-embedded credentials/query/fragment, and disables redirects. `ITokenStore.SetTokens` requires the validated renewal endpoint alongside the token pair, binding the session to the authenticating adapter rather than later global configuration. It connects to the existing `DjangoPlayerSession`; legacy wallet/shop/etc. requests continue to use that token manager. The session keeps credentials in memory; legacy PlayerPrefs token/name removal belongs only to the demo bootstrap. Clearing tokens also clears the stored display name. Session/adapters live in the package Backends.Django assembly and context contracts live in UnityIntegration.

`DevAuthBootstrap` owns a `PlayerContext`, which owns the Identity/Core runtime and its authenticated session. Failed attempts roll back before allowing a fresh attempt. Disabling or destroying the bootstrap shuts down its runtime, stops its coroutine, and removes UI listeners; destruction resets its singleton. Disabling is terminal for that bootstrap instance: create a new bootstrap for a new lifetime. The sample uses `AutoConnect` through `NetcodeIdentityGate` after authentication; connection approval never creates the player directly. Provider logout notifications, general backend-health/outage monitoring remains deferred. Renewal-specific terminal disconnection is implemented below.

`SetupExampleScene` now generates the password field, updates the account hint and wires `passwordInput`. The saved ExampleScene has matching wiring. Existing serialized fields and component identity are preserved.


## Session renewal

`TokenSession` is pure C# private state with an account-generation counter, access/refresh tokens and access expiry. It reads JWT `exp` solely to schedule renewal; parsing a JWT is not signature or identity verification. Invalid/missing expiry is rejected without replacing the current account. A successful renewal preserves the account generation and accepts an optional rotated refresh token. Clearing or replacing the account advances its generation, so late responses cannot restore or overwrite a different session.

`DjangoPlayerSession` owns one shared renewal coroutine, independently of the UI/request coroutine that asked for it. Concurrent callers join the same attempt. A real-time monitor checks expiry once a second and renews within 30 seconds of expiry, including when game time is paused. Authenticated requests wait for renewal when needed; a 401 retains the existing single refresh/replay behavior. A delayed 401 from the previous access token reuses an already-renewed token instead of starting another refresh. API responses from a previous account generation are discarded. The generation is checked again after renewal callbacks before replay, so a callback-triggered account switch cannot dispatch an old request under new credentials. A second 401 after replay terminates the session.

Renewal requests use the validated endpoint captured with that session and attempt; later `ApiConfig` changes cannot redirect refresh credentials. Authenticated gameplay requests must use the same scheme, host and port as the authenticated backend before any token is attached or request dispatched. Endpoint mismatch is a terminal configuration error. Renewal uses at most two requests, each with a five-second timeout, separated by one real-time second for connection/server errors (roughly eleven seconds maximum request/retry time). Authentication rejection, malformed responses and other client errors terminate immediately. A returned token still inside the renewal window is treated as a clock/configuration error. There is no offline queue or infinite retry. Renewal callbacks run after private state is finalized; independent subscribers are isolated. Clearing/replacing the session cancels pending renewal callbacks once and aborts its request. Disabling/destroying the Unity token owner also clears credentials and cancels pending work; enabling an owner starts its expiry monitor for a fresh session.

On terminal renewal failure, credentials/name are cleared before the `SessionEnded` notification. The sample shuts down its Core runtime, disables authentication/UI submission, shows the diagnostic and shuts down an existing Netcode connection. Disabling/destroying the bootstrap also shuts down listening or pending sessions in teardown even if runtime cleanup fails. Authentication event subscribers are isolated so one exception cannot suppress later subscribers or panel progression. Host shutdown ends that host session; a client shuts down its own connection. `AutoConnect` checks that authentication remains valid before host fallback, preventing a delayed fallback from restarting a rejected session. The game still owns navigation and creating a fresh context to sign in again. The current legacy account read models are not yet modular shutdown participants.

Django continues to validate refresh-token expiry and active users. `PlayerTokenRefreshSerializer` additionally converts a deleted account into a 401 instead of the upstream missing-user exception. No schema changes or refresh rotation/blacklist database are introduced. General idle backend-health checks and the full platform release matrix remain pending; signed peer proof integration is implemented below. No new scene component or serialized reference is required for renewal.

## Identity proof foundation

Django authenticates `POST /api/auth/identity-proof/` using the player's access token and signs protocol-v1 identity assertions with a separate backend-only RSA key. `IdentityProofIssuer` derives public identity from the authenticated account/profile; the request supplies only session/challenge context. Missing signing configuration fails closed with a redacted 503 response. There is no key fallback or schema change.

Engine-independent `IdentityProofVerifier` verifies RS256 against a copied, explicitly supplied key-ID/public-key map and `IdentityProofPolicy`. It rejects unsupported/duplicate JSON fields, invalid signatures, context/time mismatches and consumed challenges. `IdentityChallenge` generates a 32-byte random nonce and consumes successful verification atomically. Newtonsoft.Json supplies bounded-depth, strict duplicate-field parsing; System cryptography supplies RSA verification.

`IIdentityProofBackend` exposes coroutine-based proof requests. `DjangoIdentityProofAPI` uses the existing authenticated HTTP/renewal channel and rejects late account-generation responses. The owner must drive/dispose its coroutine. The client treats assertions as opaque; only the server verifier publishes a verified `PlayerIdentity`.

The sample integrates this foundation with `NetcodeIdentityGate` and `VerifiedPlayerSpawner`, wired in the saved scene. See the [protocol and setup](../Documentation/multiplayer-identity-verification.md).

## Verified Netcode session integration

`NetcodeIdentityGate` owns one NetworkManager session's connection approval, named handshake messages, pending challenges and verified identity bindings. Call its `StartClient`, `StartHost` or `StartServer` entry points while stopped. Server roles require a verifier; client roles require `IIdentityProofBackend`. The server-only role has no account/token/UI/prefab dependency. The gate owns connection approval exclusively and rejects conflicting approval callbacks.

Approval admits a connection without creating a player. On connection, the server creates one random nonce for that transport peer and a fresh session GUID. The client accepts a challenge only from the server, obtains an opaque assertion over its authenticated backend, and sends it back. The server validates the retained challenge, binds verified identity and acknowledges success. Remote invalid/replayed proofs disconnect only that peer; failure of the host's own proof shuts down that host. Disconnect/shutdown removes peer bindings. Wire reads cap remaining bytes before allocation; proofs use reliable fragmented delivery. The default handshake deadline is 15 real-time seconds (configurable 1–60), with at most 32 pending challenges. Transient proof-request failure gets two attempts with one second of realtime backoff inside the handshake deadline. Configuration/authentication/malformed responses fail immediately.

The coroutine owner delegates Unity yield instructions to Unity, tracks nested iterators and disposes them on cancellation. Cleanup uses finally blocks so even a custom adapter's throwing disposer cannot prevent coroutine stopping/network shutdown. Late callbacks are generation-guarded and one attempt accepts one result.

`VerifiedPlayerSpawner` composes the sample avatar only after verification. It sets a visibility predicate before spawn: unverified peers cannot observe existing or newly spawned sample players. On successful verification it reveals existing players to that peer. `PlayerNameplate` receives verified identity before spawning, then writes public ID/display name using server-only NetworkVariables in OnNetworkSpawn. Labels disable TMP rich text. Neither account tokens nor raw proofs are NetworkVariables. Other game-specific NetworkObjects require equivalent observer restrictions if they contain protected state.

`AutoConnect` retains sample client-first/host-fallback discovery, but only through the gate. It never falls back after connecting to a rejecting server. Configuration failures end the authenticated sample session with a visible diagnostic. `-liveops-server` starts the server role without player login. Game-specific matchmaking, destinations and navigation remain game-owned. Public-trust JSON is provisioned using `export_identity_trust`; no private signing key is bundled.

## Explicit player-session ownership

`PlayerContext` owns one Identity/Core runtime and an explicitly supplied `IAuthenticatedSession`. It exposes authenticated public identity, the request session and `IIdentityProofBackend`. Initialization reuses the existing Core lifecycle; there is no new readiness/recovery state machine. Failed authentication rolls back its runtime and permits another credential attempt in the same context. An initialized or reserved initialization cannot be replaced; disposed contexts are terminal. Disposal clears identity, detaches session notifications, shuts down the runtime and disposes the session even when backend cleanup throws. Terminal notifications run after cleanup and isolate independent subscribers.

`DjangoPlayerSession` replaces the auto-created/persistent TokenAPI singleton. It is an explicitly created MonoBehaviour owned by the sample login GameObject and disposed by the player context. Its existing origin validation, account generations, shared proactive renewal, bounded retries and single 401 replay are preserved. The authenticated request contract exposes no bearer credentials. Pending HTTP requests are tracked locally and aborted on account clear/replacement or disposal; there is no generic operation coordinator. Disable/destruction are terminal for that session instance. Player identity is no longer duplicated as a username in token storage.

`DevAuthBootstrap` creates its context and binds the context session into the Django adapters. Adapters never discover token storage or create sessions. `AutoConnect` receives its login bootstrap through a serialized binding and uses the context's proof adapter; the saved scene and generator contain that binding and the session component. Existing sample UI services still assume one local player. Separate contexts, proof adapters and direct authenticated-session clients can coexist without sharing credentials. An unconfigured adapter fails explicitly; no fallback session is created.

Wallet now uses the independent WalletModule and DjangoWalletBackend rather than a singleton adapter. The sample-only reset uses an explicit session, waits for pending Wallet work, and refreshes confirmed balances after resetting. The old ShopAPI conversion method and shop conversion route were replaced.

`NetcodeIdentityGate` already owns server trust, pending challenges and verified bindings. It remains the server context; no forwarding ServerContext wrapper was added. Server-only operation requires no player context or authenticated-session component. Idle health polling, a general recovery framework, capability negotiation, module-selection settings and generic runners remain deferred. Cosmetic Inventory is implemented below; Shop is the next extraction.

## Independent Wallet

`Scripts/Wallet/` is an engine-free assembly depending only on Core. `WalletModule` declares Identity as its lifecycle dependency without referencing an identity implementation. `PlayerContext.Initialize` accepts selected additional modules; an Identity-only consumer does not initialize Wallet. The sample selects Wallet in `DevAuthBootstrap` and supplies `DjangoWalletBackend` with the same explicitly owned authenticated session.

The adapter owns HTTP/JSON mapping; Wallet owns confirmed immutable state and operation lifetime. `WalletDisplay.Bind(IWalletService)` supports UI without the demo login, avatar or network session. The sample bootstrap binding is optional for an explicitly bound view. `DevelopmentAccountReset` retains the demonstration reset flow outside Wallet. Both the scene generator and saved scene wire the sample dependencies. The replaced WalletAPI singleton and shop conversion route are removed.

## Independent cosmetic Inventory

`Scripts/Inventory/` is engine-free and references only Core. InventoryModule declares Identity as its lifecycle dependency; it does not initialize Wallet, Shop, Equipment, Netcode or Unity assets. IInventoryBackend fetches owned cosmetics and fully owned nonempty sets. Immutable snapshots contain string IDs, display names, categories and set membership, with no prices, quantities, equipment or grant commands.

DjangoInventoryBackend uses the supplied authenticated session and rejects stale account responses. The sample endpoint `/api/inventory/` reads the existing ownership tables; seeded numeric IDs are encoded as strings. Replacement backends choose their stable content identifiers. No migration/history/receipt infrastructure is added.

DevAuthBootstrap selects Inventory independently and binds InventoryPanel. The panel optionally binds IEquipmentService and emits command requests to the durable AccessoryEquipService sample owner, without requiring an avatar. Shop applies confirmed Inventory snapshots directly. Development reset waits for Wallet, Inventory, Equipment and Achievements operations and refreshes all enabled account state. The legacy ShopCatalog remains a sample visual mapping.

## Independent Shop

ShopModule is engine-free and depends on the existing Core, Wallet and Inventory assemblies. Its lifecycle requires Identity, Wallet and Inventory. IShopBackend supplies offers plus confirmed account snapshots; IShopService exposes catalog, refresh, purchase and state notifications. Offers use string IDs and whole required multi-currency baskets. Set offers charge the full advertised price even when some members are already owned, matching the reference rules.

A current Shop purchase needs exclusive access to both account caches. IWalletState/IInventoryState reserve those modules, stage both confirmed snapshots, then release/notify only after both are installed. No observer sees charged currency without confirmed ownership from that result. Wallet conversion and account refresh are rejected as busy during the reservation. Cancellation releases reservations; uncertain dispatched mutations invalidate account snapshots and require Shop refresh. There is no automatic mutation retry, receipt/history store, general transaction coordinator or offline queue.

The reference catalog/purchase routes are `/api/shop/catalog/` and `/api/shop/purchase-offer/`. The catalog snapshot and purchase share the account lock/transaction; purchases validate exported metadata and bounded integer prices, debit all currencies and grant missing items atomically. Existing tables are reused. Replaced catalog/item/set purchase routes were removed. Django remains a reference implementation with the documented SQLite development limitations.

ShopPanel/SetsPanel share a service-bound view and can render/buy without an avatar or Netcode. ShopSampleController on the persistent sample context GameObject owns requests so hiding a tab does not cancel them. Optional preview/equip/confirmation and confirmed-purchase refresh notifications live in that sample controller; DevAuthBootstrap only composes modules and binds it. AccessoryRegistry maps snapshots to the sample visual read models, with no backend request or network startup requirement. The replaced ShopAPI and equipment RPC/persistence paths have now been removed. The generator and saved scene wire the controller explicitly.

## Independent Equipment: implemented

`Scripts/Equipment/` is engine-free. EquipmentModule requires Identity and Inventory and accepts IEquipmentBackend. EquipmentState contains immutable configured slot/conflict definitions, item-to-slot mappings and a canonical revisioned loadout. ApplyItems overlays a validated item/set request on the canonical loadout, removes configured conflicts and sends one complete update. Unequip/Clear also persist one complete loadout. Preview accepts defined unowned items but changes only local presentation; canonical refresh/change and shutdown clear it.

Django provides `/api/equipment/loadout/` GET/POST and `/api/equipment/proof/` POST. Replace requests contain only string item IDs and expected_revision. The sample locks the account, checks ownership/conflicts, updates equip flags and increments equipment_revision atomically. Stale revisions reject without writing. Unknown mutation responses require explicit refresh; InventoryPanel offers a refresh action. There is no mutation retry, receipt system or offline queue. Run migrations for the new account revision column.

IdentityProofVerifier and EquipmentProofVerifier share SignedPlayerProofVerifier, preserving separate protocol types and strict claim sets. DjangoPlayerProofRequest shares authenticated request/disposal behavior between the fixed-route identity/equipment adapters. Equipment proof limits support all 32 configured slots; identity retains its existing smaller bound.

NetcodeIdentityGate owns both verification phases and subsequent equipment challenges. Equipment-enabled peers do not become verified observers until identity and equipment pass. It checks identity binding, game/environment/content/session/nonce/expiry and revision ordering, then exposes only sanitized loadouts to the sample spawner. A private accepted-revision acknowledgement schedules another proof when a newer canonical mutation completed during proof capture. This is a repeated authoritative read, never a repeated mutation. Headless validation requires public trust and no account or visual prefab.

PlayerAccessory has no backend calls, equip business rules or client equipment RPC. Only the gate's exact approved snapshot can be published. Late join and respawn receive that current server state. Optional local preview binds through ClientPlayerAvatars lifecycle; replacement avatars rebind and old subscriptions are removed. Sample visual mappings are validated before connecting and on changed definitions; missing IDs/slots/prefabs report cosmetic ID/revision and terminate the affected local session. Equipment-disabled sample players install no equipment handlers.

## Independent Achievements

`AchievementsModule` is an engine-free assembly exposing `IAchievementService` and immutable definitions/progress/reward models. Identity is mandatory. Currency rewards require explicitly supplied Wallet state; cosmetic rewards and collection conditions require Inventory. Supplied ports declare Core dependencies; missing required capabilities fail initialization. Shop, avatar, Netcode and UI are optional.

`DjangoAchievementBackend` reads `/api/achievements/state/` and claims with only an achievement ID at `/api/achievements/claim/`. Definitions come from Django admin/seed. Completion and reward claim are separate; no-reward achievements need no claim. The backend preserves stored counters and never trusts client counts. Login conditions mean recorded daily attendance, not password authentication. Claims lock the player, grant once and return confirmed achievement/account snapshots in the same transaction; invalid or unexportable configuration rolls back the grant.

The module stages all returned state before notifications. Uncertain claims invalidate affected account caches and require Refresh; it never retries a mutation. Context shutdown clears private state. The sample controller owns operation disposal even when observers disable it during reservation. TMP panel/toast bind through the interface; closing a tab leaves claims running. Successful purchases and attendance claims request reads only. Development reset waits for pending achievement work and refreshes its state afterward. As with the existing development reset, it does not reserve the whole account during reset HTTP; avoid concurrent sample commands during reset.

Removed paths: client evaluators/tracker, achievement ScriptableObject override, global AchievementService/API, authority/write queue and count-write endpoints. See [achievement authority decisions](../Documentation/architecture-decisions.md#5-evaluate-achievements-from-backend-observable-facts).

## Independent Daily Rewards

`DailyRewardsModule` is an engine-free mechanic behind `IDailyRewardService`/`IDailyRewardBackend`. Identity is required; explicitly supplied Wallet state declares a Wallet dependency and is required when any calendar day has a currency reward. Attendance-only calendars work without Wallet. It has no Shop, Inventory, Equipment, Achievements, UI, avatar or Netcode dependency.

The sample Django service preserves MonthlyRewardSchedule and unique DailyAttendance records. Its current-month calendar uses the backend UTC date, with existing backend schedule generation when a month has no configured rows. Missing individual days are attendance-only. Developers configure schedules through Django admin/commands; rewards are currency-only for this existing sample mechanic.

`GET /api/daily-rewards/state/` returns a full ordered calendar and optional confirmed Wallet. `POST /api/daily-rewards/claim/` accepts exactly the displayed ISO date. The backend rejects stale dates rather than silently claiming a new day after midnight. It locks the player and balance, records attendance/grants once and validates the complete result before commit. No extra database migration, receipts or history system was added.

The module stages calendar/Wallet together before notifications. Unknown/dispatched cancelled claims require authoritative refresh and invalidate the affected Wallet cache; mutations are never retried automatically. Day-changed/already-claimed responses require calendar refresh. Explicit Refresh handles rollover; eligibility never uses the client clock. Account shutdown clears private state.

DailyLoginPanel remains the existing serialized TMP calendar view, now bound to the interface. DailyRewardsSampleController owns claims/reads outside panel lifetime and publishes confirmed attendance for the optional achievement read refresh. Owner teardown disposes actual iterators, including disable during reservation notifications. Development reset waits for Daily Rewards and refreshes attendance after reset. The generator and saved scene wire both owners. Legacy DailyLoginAPI/DTOs and daily-login endpoints were removed.

## UPM package boundary

The reusable runtime now lives in the private preview `Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit`; Assets retains the full game demo. Existing module asmdefs and script GUIDs moved with their sources. New UnityIntegration, Backends.Django and Netcode assemblies enforce separation from the demo and TMP. Unity session/context, Django and Netcode types use their matching `MultiplayerLiveOpsToolkit.*` namespaces. Pure mechanic APIs remain unchanged.

ApiResponse belongs to the UnityIntegration assembly alongside IAuthenticatedSession/PlayerContext, so replacement Unity sessions do not depend on Django. DjangoPlayerSession requires an explicit renewal endpoint; only the demo bootstrap migrates its old PlayerPrefs keys. The SDK never discovers sample URLs or deletes host-game preferences. Portable tests ship in the package, with demo tests remaining in Assets. See [package install/composition/verification](../Documentation/package-integration.md).

## Package equipment visuals

`Runtime/EquipmentVisuals` supplies an optional Unity renderer behind IEquipmentRenderer. CosmeticVisualCatalog maps string item/slot IDs to local prefabs and attachment mode. CosmeticRenderer owns per-avatar pools, explicit slot mounts/base meshes and rig/bone aliases. It prepares missing instances before replacing the displayed look, captures only avatar bones and restores original base visibility on clear/teardown. ResetBindings releases pools before character/configuration replacement. No backend, Netcode, UI or sample enum dependency enters this assembly.

EquipmentVisualPresenter owns private service subscriptions and chooses local preview or confirmed equipment for standalone consumers. Supplying a verified public loadout makes that the canonical multiplayer display until a newer verified loadout arrives; previews remain local overrides. Demo PlayerAccessory delegates to this presenter and terminates on configuration failure. The generator and saved player prefab contain the same package-owned visual configuration. Art and sample-specific humanoid bindings remain under Assets; the old AccessoryPool/SkinnedMeshAttacher implementation is retired.

## Complete package avatar/starter integration

Reusable player lifecycle, verified spawning and equipment publication moved with GUIDs to Runtime/Netcode.Avatars. NetworkPlayerIdentity replicates approved public identity without TMP; the demo PlayerNameplate renders it. AvatarRegistry is session-local on NetworkManager; late despawn identifies the old object so replacements survive. The demo ClientPlayerAvatars retains only its legacy presentation locator over that registry. PlayerAccessory delegates preview/public rendering to EquipmentVisualPresenter.

Optional CharacterControls supplies the prior client-authoritative transform/animation/movement plus a registry-bound camera that handles replacement and reenable. ClientHostConnection supplies the existing client-first/host-fallback policy; the sample AutoConnect owns trust/account/role setup and delegates connection, including its active lifetime in the continuation predicate. DjangoPlayerModules composes selected reference adapters/services under PlayerContext; the sample login owns UI, not construction of every mechanic. Rejected concurrent initialization cannot replace accepted service references.

The optional Game Starter sample ships the remaining UI/composition examples and an art-free generator. The full repository demo continues to use its existing authored art. Runtime owners expose their existing scoped events; no global event bus or additional audio framework was added.
