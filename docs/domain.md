## Glossary

| Term | Definition |
|---|---|
| `AccessoryType` | Enum of equip slots: Headwear, Hair, Facewear, Head, Torso, Arms, Legs, Feet, MainHand, OffHand, Back, None |
| `AccessoryItemData` | Local catalog entry: `itemId`, `prefab`, `accessoryType`. No pricing or ownership. |
| `AccessorySO` | ScriptableObject master catalog of all `AccessoryItemData` entries. O(1) lookups via lazy dict. |
| `ShopAccessoryItem` | Extends `AccessoryItemData` with backend-sourced `displayName`, `isOwned`, `costs`. |
| `ShopCatalog` | Runtime object merging backend `AccessoriesResponse` with local `AccessorySO`. All pricing/ownership queries go here. |
| `AccessorySetData` | A purchasable bundle of accessory IDs with its own costs and ownership flag. |
| `AccessoryState` | `INetworkSerializable` wrapper for revisioned public equipment JSON, containing only slot/item IDs and revision. |
| `AccessoryRegistry` | Demo singleton that builds ShopCatalog from bound confirmed Shop/Inventory snapshots and local accessory mappings; it performs no backend request. |
| `PlayerAccessory` | `NetworkBehaviour` on each player avatar. Server publishes only gate-approved canonical equipment revisions; clients read and render them. |
| `CosmeticRenderer` | Per-avatar IEquipmentRenderer implementation with pools, slot/rig bindings and base-mesh restoration. |
| `EquipmentVisualPresenter` | Selects canonical/verified equipment or a local-only preview for IEquipmentRenderer. |
| `ClientPlayerAvatars` | Singleton registry mapping Netcode `clientId` → avatar `GameObject`. Tracks local player's avatar. |
| `DjangoPlayerSession` | Owns an in-memory JWT session with shared proactive renewal. |
| `IAuthenticatedSession.Send` | Explicit-session authenticated dispatch with bounded renewal and one replay on 401; mechanic mutations are never automatically retried. |
| `DevAuthBootstrap` | Presents username/password login through Identity/Core before multiplayer connection. |
| `WalletEntry` | DTO: `{ currency: { name }, amount }` representing one currency balance. |
| `AutoConnect` | Tries Netcode Client first; falls back to Host after timeout. |
| `ClientNetworkTransform` | Client-authoritative `NetworkTransform`. Overrides `OnIsServerAuthoritative()` → `false` so the owning client's position changes propagate to all other clients. Required because the default `NetworkTransform` is server-authoritative — only the server can move the transform. |
| `ClientNetworkAnimator` | Client-authoritative `NetworkAnimator`. Same override pattern. Without this, only the host's animations sync — client animation parameter changes (e.g. walk speed) are ignored by the server-auth default. |
| `SimplePlayerMovement` | `NetworkBehaviour` on the player prefab. Uses `IsOwner` guard so only the owning client processes WASD input. Baked on the prefab (not added at runtime) so Netcode properly tracks ownership. |

## State Ownership Model

| State | Owner | Sync Method |
|---|---|---|
| Equipped cosmetics (public) | Server after signed equipment verification | `NetworkVariable<AccessoryState>` contains revision and equipped slot/ID pairs only |
| Equipped accessories (persistent) | Backend | REST `GET/POST /api/equipment/loadout/` |
| Wallet balances | Backend | REST `GET /api/wallet/`; displayed by `WalletDisplay` |
| Master accessory catalog (prefabs) | Client (local asset) | `AccessorySO` ScriptableObject — never synced |
| Public player ID/nameplate | Server after signed identity verification | Server-written `NetworkPlayerIdentity.PlayerId` / `Username`; unverified peers cannot observe sample players |
| JWT tokens | Client-local | In memory via `DjangoPlayerSession`; never replicated |
| Player avatar registry | Client-local | `ClientPlayerAvatars` singleton; populated by `PlayerAvatarSetup.OnNetworkSpawn` |
| Player movement | Client (owner authority) | `SimplePlayerMovement` writes `transform.position`; `ClientNetworkTransform` syncs to all clients. Client-authoritative — the owner is trusted. |
| Player animation | Client (owner authority) | `SimplePlayerMovement` sets Animator Speed param; `ClientNetworkAnimator` syncs to all clients. |

## API/Backend Contracts

| Endpoint | Method | Body / Params | Response Shape |
|---|---|---|---|
| `/api/auth/password/` | POST | `{"username":"...","password":"..."}` | `{"access","refresh","username","player_id"}`; existing active Django user required |
| `/api/auth/dev/` | POST | `{"username":"..."}` | `{"access","refresh","username","player_id"}` (DEBUG only) |
| `/api/auth/refresh/` | POST | `{"refresh":"..."}` | `{"access","refresh"}` |
| `/api/wallet/convert/` | POST | `{"offer_id": str, "amount": int}` | Confirmed wallet snapshot / typed `{"code": str}` rejection |
| `/api/equipment/loadout/` | POST | `{"item_ids": [string], "expected_revision": int}` | Confirmed definitions/loadout, or typed rejection |
| `/api/equipment/loadout/` | GET | — | Configured slots/items and revisioned canonical loadout |
| `/api/equipment/proof/` | POST | `{"session_id": string, "challenge": string}` | Private signed equipment assertion |
| `/api/shop/catalog/` | GET | — | `{offers, wallet, inventory}` confirmed shop/account snapshots |
| `/api/shop/purchase-offer/` | POST | `{"offer_id": str}` | `{purchased_offer_id, offers, wallet, inventory}` / typed `{code}` rejection |
| `/api/inventory/` | GET | — | `{"items":[{"item_id","display_name","category_id"}],"sets":[{"set_id","display_name","item_ids"}]}` |
| `/api/wallet/` | GET | — | `{"balances":[{"currency_id":"gold","amount":500}],"offers":[...]}` |

Authenticated adapters use an explicitly supplied `IAuthenticatedSession` (Bearer JWT, bounded renewal and one replay on 401). Wallet conversions have no automatic transport retry.

## Networking Standards

This project uses **Unity Netcode for GameObjects**. All new and modified code must follow:

1. **Lifecycle:** `Awake` (local refs only) → `OnNetworkSpawn` (subscribe `OnValueChanged`, read initial values, RPCs safe) → `OnNetworkDespawn` (unsubscribe, cleanup) → `OnDestroy` (local only). Always call `base.OnNetworkSpawn()` and `base.OnNetworkDespawn()`. `PlayerAccessory` and `PlayerAvatarSetup` already follow this correctly.
2. **Initial value pattern:** `OnValueChanged` does NOT fire for the initial value. Read explicitly in `OnNetworkSpawn`: see PlayerAccessory.OnNetworkSpawn, which explicitly reads AccessoryJson after subscribing.
3. **Never subscribe to `OnValueChanged` in `Awake`** — `NetworkVariable` isn't initialized yet.
4. **Never unsubscribe in `OnDestroy`** — use `OnNetworkDespawn` (fires first for networked objects).
5. **Validate deserialized payloads:** PlayerAccessory parses public equipment through EquipmentPublicState.Parse, which validates the wire shape and revision. Treat malformed required state as an error; do not silently skip it.
6. **RPCs cannot be called before `OnNetworkSpawn`** — fails silently.
7. **Check `IsOwner`/`IsServer` before writing `NetworkVariables`** — `PlayerAccessory.AccessoryJson` is server-written only from the gate-approved EquipmentLoadout. There is no client equipment RPC. Publish only after spawn; keep ownership/conflict validation in the backend and module.
8. **Client authority for movement and animation.** The default `NetworkTransform` and `NetworkAnimator` are server-authoritative — only the server/host can change the synced values. For client-driven systems like player movement, use `ClientNetworkTransform` and `ClientNetworkAnimator` which override `OnIsServerAuthoritative()` to return `false`. Without this, client movement and animations will not propagate to other players. The host appears to work because the host IS the server.
9. **NetworkBehaviours must be on the prefab, not added at runtime.** A `NetworkBehaviour` added via `AddComponent` after `NetworkObject.Spawn` does not properly register with Netcode — `IsOwner`, `IsServer`, and RPCs may not work. `SimplePlayerMovement` is baked on the player prefab and uses `IsOwner` to gate input, not added conditionally at runtime.
10. **Player prefab component list:** `NetworkObject`, `ClientNetworkTransform`, `ClientNetworkAnimator`, `SimplePlayerMovement`, `PlayerAvatarSetup`, `PlayerAccessory`, `CosmeticRenderer`, `NetworkPlayerIdentity`, `Animator`, `CharacterController`, `PlayerNameplate`. All baked on the prefab. The saved scene and player prefab are the source of truth.

API URLs use `ApiConfig.BASE_URL` (default `http://localhost:8000`, overridable via `SetBaseUrl`). Contracts above describe the Unity client; backend enforcement must be checked separately.

## Identity assertion protocol v1

An identity assertion is a short-lived backend-signed statement of public player identity, scoped to configured game/environment/content and a server-generated session/challenge. It is separate from account access/refresh tokens and does not prove cosmetic ownership. `IdentityChallenge` belongs to one peer connection; successful verification consumes it once. See [wire format and operational setup](../Documentation/multiplayer-identity-verification.md). The sample enforces peer verification, spawn gating and player visibility through `NetcodeIdentityGate` and `VerifiedPlayerSpawner`. Equipment uses a separate signed assertion for canonical slot/ID pairs and revision, verified after identity and before spawn/publication. See [Equipment protocol](../Documentation/multiplayer-identity-verification.md#equipment-protocol-v1-implemented).

## Wallet mechanics

Wallet is account-scoped and independent of avatars, Netcode and UI. The backend defines offers as an ID, source/target currency IDs and integral source/target units. The sample uses nonnegative signed-32-bit balances and exact exchange units; it validates the request and atomically debits/credits the authenticated account. No caller-supplied player ID is accepted.

`IWalletBackend` supplies confirmed snapshots to `WalletModule`; `IWalletService` exposes immutable balances/offers, refresh, conversion and change notifications. One operation runs at a time. A lost conversion response leaves its outcome unknown: cached balances are invalidated and a refresh is required before another conversion. No transaction history, receipt lookup, revision protocol or durable retry queue is required. Django is a reference implementation; consuming games choose bookkeeping and economy policies.

## Achievement authority

`GET /api/achievements/state/` returns definitions, confirmed progress/completion, claim flags and required Wallet/Inventory snapshots. `POST /api/achievements/claim/` accepts exactly `{"achievement_id":"..."}` and returns the same state plus `claimed_achievement_id`. Unknown/inactive, locked, already claimed and no-reward achievements reject without granting. Client counts/player IDs are rejected; legacy count/unlock routes are removed.

Sample conditions: successful individual/set purchase counts, actual currency charged by Shop, current owned cosmetic collections and recorded daily attendance. Existing progress is preserved; no historical spend is inferred from current prices. Completion does not itself grant rewards. Claims atomically set `UserAchievement.is_claimed` and grant configured currency/cosmetics once. No-reward completion has no claim action. Generic gameplay-event evidence and game-specific receipts/history are outside this slice.

## Daily Rewards authority

`GET /api/daily-rewards/state/` returns `calendar: {today_date, days: [{day, reward: {currency_id, amount} | null, is_claimed}]}`, `wallet` (or null for attendance-only calendars) and `claimed_date: null`. The backend UTC date selects the month; all days appear once in order. Null reward means attendance-only. No client date/clock controls eligibility.

`POST /api/daily-rewards/claim/` accepts only `{"date":"YYYY-MM-DD"}` from the displayed snapshot. A success returns confirmed calendar/Wallet plus matching `claimed_date`. A different current backend date returns `day_changed`; duplicate attendance returns `already_claimed`. Both require a read refresh. Unknown outcomes require refresh before another claim; no automatic retry. Negative/overflowing rewards, malformed schedules or unexportable results roll back both attendance and grants. Existing attendance also supplies achievement login-condition progress.
