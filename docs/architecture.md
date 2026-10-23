# architecture.md

## Layer Model

```
Editor (SetupExampleScene)              — offline tooling, references all layers
Tests  (AccessoryLoadoutTests, ShopCatalogTests, ...) — EditMode, references Data only
  ↑ nothing depends on these
──────────────────────────────────────────────────────────────
Example (UI, bootstrap, visuals)        → Player, Registry, API, Data
Player  (NetworkBehaviours, sync)       → Registry, API, Data
Registry (AccessoryRegistry)            → API, ScriptableObjects, Data
API     (ShopAPI, TokenAPI, WalletAPI)  → Data
ScriptableObjects (AccessorySO)         → Data
Data    (DTOs, enums, pure C#)          → (no internal deps)
```

Dependency direction is top-down. The project uses global C# types and one runtime assembly; layer boundaries are conventions checked by type references and directory paths, not compiler-enforced assembly boundaries. See `enforcement/layer-imports.md`.

## Key Types

| Layer | Type | Role |
|---|---|---|
| Data | `AccessoryType` | Enum of equip slots (Headwear, MainHand, etc.) |
| Data | `AccessoryState` | INetworkSerializable JSON wrapper for equipped-slot map |
| Data | `ShopCatalog` | Runtime merged catalog: backend pricing + local prefabs |
| Data | `ShopApiDTOs` | `AccessoriesResponse`, `AccessorySetResponse`, `AccessoryCost`, etc. |
| API | `TokenAPI` | JWT storage (PlayerPrefs), refresh flow, `SendWithTokenRefresh` extension |
| API | `ShopAPI` | REST client for shop, purchase, equip endpoints |
| API | `WalletAPI` | REST client for wallet balance and currency conversion |
| ScriptableObjects | `AccessorySO` | Master catalog asset with O(1) lookups by ID/prefab |
| Registry | `AccessoryRegistry` | Singleton bridging `AccessorySO` + `ShopCatalog`; loads on network connect |
| Player | `PlayerAccessory` | NetworkBehaviour: syncs equipped accessories via `NetworkVariable<AccessoryState>` |
| Player | `ClientPlayerAvatars` | Tracks all spawned avatars by client ID; exposes local avatar |
| Player | `IAccessoryVisualSync` | Interface decoupling visual attachment from network sync |
| Player | `ClientNetworkTransform` | Client-authoritative NetworkTransform — owner's position syncs to all clients |
| Player | `ClientNetworkAnimator` | Client-authoritative NetworkAnimator — owner's animation params sync to all clients |
| Example | `AccessoryPool` | Implements `IAccessoryVisualSync`; pools/bone-routes accessory instances |
| Example | `DevAuthBootstrap` | Prompts for a username, authenticates with the dev endpoint, then enables connection |
| Example | `AutoConnect` | Tries Netcode Client, falls back to Host |
| Example | `ShopPanel`, `InventoryPanel`, `SetsPanel` | Browse, purchase, preview, and equip items or sets |
| Example | `AccessoryEquipService` | Coordinates local item/set previews and committed equipment |
| Example | `ShopUIFactory`, `PurchaseConfirmationPrompt` | Shared UI construction and purchase confirmation |
| Registry | `AchievementService` | Loads definitions/progress, evaluates events, and requests unlock confirmation |
| Data | `AchievementProgressTracker` | Pure C# evaluation using threshold, one-time, and collection strategies |
| API | `ServerValidatedAuthority` | Implements `IAchievementAuthority`; queues unlock/progress REST writes |
| API | `ApiWriteQueue` | Sequential in-memory writes with up to three attempts |
| API | `DailyLoginAPI`, `AchievementAPI` | Daily reward and achievement REST clients |
| Player | `PlayerNameplate` | Owner-written username replication, host styling, and distance fade |

## Bootstrap Ordering

```text
1. DevAuthBootstrap.Start()        → shows login UI; waits for username + Play
2. DevAuthBootstrap                → POST /api/auth/dev/ → stores JWT and username
3. AutoConnect.Start()             → waits for authentication → tries Client, falls back to Host
4. AccessoryRegistry.Start()       → waits for network client → loads item/set catalog
5. PlayerAvatarSetup.OnNetworkSpawn → registers avatar with ClientPlayerAvatars
6. PlayerAccessory.OnNetworkSpawn  → owner loads equipped IDs → applies visuals → requests server write
7. PlayerNameplate.OnNetworkSpawn  → owner publishes username; remotes display initial value
8. ShopPanel.OnEnable()            → waits for catalog + local avatar before population
```

These are dependency relationships, not a guaranteed execution order across components. `AchievementService` independently waits for a network client, loads definitions and progress, and drains events queued before loading. The login panel hides after the local avatar is registered.

## Singleton Policy

| Singleton | Lifetime | Access | Auto-creates? |
|---|---|---|---|
| `TokenAPI.Instance` | DontDestroyOnLoad | Lazy getter | Yes (new GameObject) |
| `ShopAPI.Instance` | DontDestroyOnLoad | Lazy getter | Yes (new GameObject) |
| `WalletAPI.Instance` | DontDestroyOnLoad | Lazy getter | Yes (new GameObject) |
| `AccessoryRegistry.Instance` | DontDestroyOnLoad | Scene-placed | No |
| `ClientPlayerAvatars.Instance` | Scene | Scene-placed | No |
| `AchievementAPI.Instance`, `DailyLoginAPI.Instance` | DontDestroyOnLoad | Lazy getter | Yes (new GameObject) |
| `ApiWriteQueue.Instance` | DontDestroyOnLoad | Lazy getter | Yes (new GameObject) |
| `AchievementService.Instance` | DontDestroyOnLoad | Scene-placed | No |
| `AccessoryEquipService.Instance` | Scene | Scene-placed | No |
| `DevAuthBootstrap.Instance` | Scene | Scene-placed | No |
| `WalletDisplay.Instance` | Scene | Scene-placed | No |
| `TabManager.Instance` | Scene | Scene-placed | No |

`AccessoryPool` is a component on each player prefab, with independent instance and active-slot dictionaries. It has no singleton. `PlayerAccessory` resolves it through `GetComponent<IAccessoryVisualSync>()`. API auto-creation is an existing supported pattern; required scene dependencies should fail explicitly when missing.

---

## Structural Standards (enforced for all new/modified code)

**1. Layer boundaries are convention-enforced.** Lower layers must not reference higher layers per the diagram above. `Data` must never import `API`, `Player`, or `Example`. `Player` must never import `Example`.

**2. NetworkBehaviour boundaries.** `PlayerAccessory` and `PlayerAvatarSetup` must contain ONLY: `NetworkVariable` declarations, `OnValueChanged` callbacks, RPCs, `OnNetworkSpawn`/`OnNetworkDespawn`, ownership checks. All business logic (conflict resolution, JSON building, slot management) must live in pure C# classes testable in EditMode.

**3. Singleton access must throw on null.** All `.Instance` access must use: `Registry.Instance ?? throw new InvalidOperationException($"{nameof(Registry)} not initialized")`. Never silently return or no-op on null. The auto-create pattern in `ShopAPI`/`TokenAPI`/`WalletAPI` is acceptable only because it guarantees non-null.

**4. Scene composition contracts.** Every scene entry point (`DevAuthBootstrap.Awake`, `AccessoryRegistry.Awake`, `ClientPlayerAvatars.Awake`) must validate required sibling objects and `[SerializeField]` references. Missing required object = `throw`, not silent degradation. Example: `AccessoryRegistry.Awake` must validate `masterCatalogSO != null`.

**5. `SetupExampleScene.cs` owns the scene.** The editor script at `Assets/Editor/SetupExampleScene.cs` programmatically builds the entire `ExampleScene` — all GameObjects, components, serialized references, UI hierarchy, and prefab wiring. Running `Tools/Setup Example Scene` destroys the current scene and rebuilds it from scratch. **Every new feature must update this script**: new singletons, new tab panels, new accessories/sets, new `[SerializeField]` wiring. Manual inspector changes that are not reflected in the script will be lost on the next rebuild. Treat the script as the authoritative definition of what exists in the scene.

**6. Client authority for player-driven sync.** The default `NetworkTransform` and `NetworkAnimator` are server-authoritative — the server is the only source of truth. This works for the host (who IS the server) but silently breaks clients: client movement and animation changes are ignored because the server never originated them. Use `ClientNetworkTransform` and `ClientNetworkAnimator` (which override `OnIsServerAuthoritative() → false`) for any player-driven system where the owning client is the source of truth. This is the single most common Netcode pitfall — it appears to work in single-player/host testing but fails silently for clients.

**7. NetworkBehaviours must be baked on prefabs.** Never add a `NetworkBehaviour` via `AddComponent` after the `NetworkObject` is spawned. Netcode does not properly register late-added NetworkBehaviours — `IsOwner`, `IsServer`, RPCs, and NetworkVariables may not function. All networked components (`SimplePlayerMovement`, `PlayerAccessory`, `ClientNetworkTransform`, `ClientNetworkAnimator`) must be on the prefab at spawn time.

## Authority and persistence limits

Movement and animation are owner-authoritative. Accessory replication is server-written: the owner calls `RequestAccessoryUpdateServerRpc`, which accepts empty state or checks that JSON deserializes before writing. It does **not** check item ownership, catalog membership, or slot/item consistency. Server write permission alone does not establish ownership validation.

Equipment persistence and achievement writes use `ApiWriteQueue`. It processes requests sequentially, attempts each at most three times, and waits 2 then 4 seconds before retries. The queue is in memory and is lost when the application exits. It does not classify retryable errors or provide durable delivery.

Achievements use `IAchievementAuthority` with `ServerValidatedAuthority` as the default component. Unlock notifications fire after a successful backend response. The client implementation alone does not prove that the backend independently verifies conditions; see [the authority decision](decisions/003-authority-model.md).
