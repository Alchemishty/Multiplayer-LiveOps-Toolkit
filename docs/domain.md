## Glossary

| Term | Definition |
|---|---|
| `AccessoryType` | Enum of equip slots: Headwear, Hair, Facewear, Head, Torso, Arms, Legs, Feet, MainHand, OffHand, Back, None |
| `AccessoryItemData` | Local catalog entry: `itemId`, `prefab`, `accessoryType`. No pricing or ownership. |
| `AccessorySO` | ScriptableObject master catalog of all `AccessoryItemData` entries. O(1) lookups via lazy dict. |
| `ShopAccessoryItem` | Extends `AccessoryItemData` with backend-sourced `displayName`, `isOwned`, `costs`. |
| `ShopCatalog` | Runtime object merging backend `AccessoriesResponse` with local `AccessorySO`. All pricing/ownership queries go here. |
| `AccessorySetData` | A purchasable bundle of accessory IDs with its own costs and ownership flag. |
| `AccessoryState` | `INetworkSerializable` struct wrapping a JSON string (e.g. `{"Headwear":1,"MainHand":20}`) for NetworkVariable sync. |
| `AccessoryRegistry` | Singleton that owns the `ShopCatalog`, loads it from backend on Netcode connect, and exposes all accessory lookups. |
| `PlayerAccessory` | `NetworkBehaviour` on each player avatar. Owner requests a server write of equipped JSON; remotes read and apply visuals. |
| `AccessoryPool` | Per-avatar component implementing `IAccessoryVisualSync`. Pools accessory GameObjects and manages one active item per slot independently for each player. |
| `SkinnedMeshAttacher` | Static utility that reparents a skinned-mesh accessory prefab onto an avatar's bone hierarchy. |
| `ClientPlayerAvatars` | Singleton registry mapping Netcode `clientId` → avatar `GameObject`. Tracks local player's avatar. |
| `TokenAPI` | JWT manager. Stores access/refresh tokens in `PlayerPrefs`. |
| `SendWithTokenRefresh` | Extension method on `UnityWebRequest`: attaches Bearer token, retries once on 401 after refresh. |
| `DevAuthBootstrap` | Prompts for a username and authenticates with `/api/auth/dev/` after Play is clicked. |
| `WalletEntry` | DTO: `{ currency: { name }, amount }` representing one currency balance. |
| `AutoConnect` | Tries Netcode Client first; falls back to Host after timeout. |
| `ClientNetworkTransform` | Client-authoritative `NetworkTransform`. Overrides `OnIsServerAuthoritative()` → `false` so the owning client's position changes propagate to all other clients. Required because the default `NetworkTransform` is server-authoritative — only the server can move the transform. |
| `ClientNetworkAnimator` | Client-authoritative `NetworkAnimator`. Same override pattern. Without this, only the host's animations sync — client animation parameter changes (e.g. walk speed) are ignored by the server-auth default. |
| `SimplePlayerMovement` | `NetworkBehaviour` on the player prefab. Uses `IsOwner` guard so only the owning client processes WASD input. Baked on the prefab (not added at runtime) so Netcode properly tracks ownership. |

## State Ownership Model

| State | Owner | Sync Method |
|---|---|---|
| Equipped accessories (visual) | Server (JSON structure check) | `NetworkVariable<AccessoryState>` — owner requests via ServerRpc, server checks JSON deserialization and writes; ownership is not checked in this RPC |
| Equipped accessories (persistent) | Backend | REST `GET/POST /api/accessories/get-equipped/` and `/update-equipped/` |
| Shop catalog (pricing, ownership) | Backend → `AccessoryRegistry` | REST `GET /api/shop/accessories/` and `/api/shop/sets/` merged into `ShopCatalog` at runtime |
| Wallet balances | Backend | REST `GET /api/wallet/`; displayed by `WalletDisplay` |
| Master accessory catalog (prefabs) | Client (local asset) | `AccessorySO` ScriptableObject — never synced |
| Player username/nameplate | Client (owner authority) | `PlayerNameplate.Username`, an owner-written `NetworkVariable<FixedString64Bytes>`; UTF-8 input truncated to 63 bytes |
| JWT tokens | Client-local | `PlayerPrefs` via `TokenAPI` |
| Player avatar registry | Client-local | `ClientPlayerAvatars` singleton; populated by `PlayerAvatarSetup.OnNetworkSpawn` |
| Player movement | Client (owner authority) | `SimplePlayerMovement` writes `transform.position`; `ClientNetworkTransform` syncs to all clients. Client-authoritative — the owner is trusted. |
| Player animation | Client (owner authority) | `SimplePlayerMovement` sets Animator Speed param; `ClientNetworkAnimator` syncs to all clients. |

## API/Backend Contracts

| Endpoint | Method | Body / Params | Response Shape |
|---|---|---|---|
| `/api/auth/dev/` | POST | `{"username":"..."}` | `{"access","refresh","username"}` |
| `/api/auth/refresh/` | POST | `{"refresh":"..."}` | `{"access","refresh"}` |
| `/api/shop/accessories/` | GET | — | `{"accessories": {"headwear": [{"id","name","is_owned","costs":[{"currency","amount"}]}]}}` |
| `/api/shop/sets/` | GET | — | `{"sets": {"category": [{"id","name","is_owned","accessory_ids","costs"}]}}` |
| `/api/shop/purchase/` | POST | `{"accessory_id": int}` | 200 success / 400 `{"message":"reason"}` |
| `/api/shop/convert-currency/` | POST | `currency_type=str&amount=int` | `{"gold_amount": int}` / 400 `{"message":"..."}` |
| `/api/accessories/update-equipped/` | POST | `{"accessory_ids": [int]}` | 200 success |
| `/api/accessories/get-equipped/` | GET | — | `{"accessory_ids": [int]}` |
| `/api/wallet/` | GET | — | `[{"currency":{"name":"gold"},"amount":500},...]` |
| `/api/shop/purchase-set/` | POST | `{"set_id": int}` | Success or error response handled by `ShopAPI.PurchaseSet` |
| `/api/daily-login/calendar/` | GET | — | `{"year","month","today","days":[{"day","reward":{"currency","amount"},"is_claimed"}]}` |
| `/api/daily-login/claim/` | POST | Empty body | `{"day","reward":{"currency","amount"},"message"}` |
| `/api/achievements/definitions/` | GET | — | `{"achievements":[...]}` |
| `/api/achievements/progress/` | GET | — | `{"progress":[...]}` |
| `/api/achievements/unlock/` | POST | `{"achievement_id": str, "current_count": int}` | HTTP success confirms the unlock to the client |
| `/api/achievements/progress/update/` | POST | `{"achievement_id": str, "current_count": int}` | HTTP success/error |

All authenticated endpoints use `SendWithTokenRefresh` (Bearer JWT, auto-retry on 401).

## Networking Standards

This project uses **Unity Netcode for GameObjects**. All new and modified code must follow:

1. **Lifecycle:** `Awake` (local refs only) → `OnNetworkSpawn` (subscribe `OnValueChanged`, read initial values, RPCs safe) → `OnNetworkDespawn` (unsubscribe, cleanup) → `OnDestroy` (local only). Always call `base.OnNetworkSpawn()` and `base.OnNetworkDespawn()`. `PlayerAccessory` and `PlayerAvatarSetup` already follow this correctly.
2. **Initial value pattern:** `OnValueChanged` does NOT fire for the initial value. Read explicitly in `OnNetworkSpawn`: see `PlayerAccessory.OnNetworkSpawn` which calls `ApplyRemoteAccessoryState(AccessoryJson.Value.Json)` for non-owners.
3. **Never subscribe to `OnValueChanged` in `Awake`** — `NetworkVariable` isn't initialized yet.
4. **Never unsubscribe in `OnDestroy`** — use `OnNetworkDespawn` (fires first for networked objects).
5. **Validate deserialized payloads:** `AccessoryState.Json` can be null or empty — `PlayerAccessory.ApplyRemoteAccessoryState` guards with `if (string.IsNullOrEmpty(accessoryJson)) return;`. Apply this pattern to all `NetworkVariable` JSON payloads.
6. **RPCs cannot be called before `OnNetworkSpawn`** — fails silently.
7. **Check `IsOwner`/`IsServer` before writing `NetworkVariables`** — `PlayerAccessory.AccessoryJson` is `WritePermission.Server`; owner sends `RequestAccessoryUpdateServerRpc`, server checks JSON deserialization and writes; ownership is not checked in this RPC. All public mutation methods have `IsSpawned` guards.
8. **Client authority for movement and animation.** The default `NetworkTransform` and `NetworkAnimator` are server-authoritative — only the server/host can change the synced values. For client-driven systems like player movement, use `ClientNetworkTransform` and `ClientNetworkAnimator` which override `OnIsServerAuthoritative()` to return `false`. Without this, client movement and animations will not propagate to other players. The host appears to work because the host IS the server.
9. **NetworkBehaviours must be on the prefab, not added at runtime.** A `NetworkBehaviour` added via `AddComponent` after `NetworkObject.Spawn` does not properly register with Netcode — `IsOwner`, `IsServer`, and RPCs may not work. `SimplePlayerMovement` is baked on the player prefab and uses `IsOwner` to gate input, not added conditionally at runtime.
10. **Player prefab component list:** `NetworkObject`, `ClientNetworkTransform`, `ClientNetworkAnimator`, `SimplePlayerMovement`, `PlayerAvatarSetup`, `PlayerAccessory`, `AccessoryPool`, `Animator`, `CharacterController`, `PlayerNameplate`. All baked on the prefab. `SetupExampleScene.cs` is the source of truth.

API URLs use `ApiConfig.BASE_URL` (default `http://localhost:8000`, overridable via `SetBaseUrl`). Contracts above describe the Unity client; backend enforcement must be checked separately.
