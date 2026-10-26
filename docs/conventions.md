# conventions.md

## Existing Patterns

### Null Handling
The codebase uses **two distinct strategies**. Domain/runtime classes throw explicitly:
- `AccessoryPool.Equip` throws `InvalidOperationException` for null `prefab` or `avatarRoot`
- `SkinnedMeshAttacher.Attach` uses throw-expression: `FindRig(avatarRoot) ?? throw new InvalidOperationException(...)`
- The selected equipment visual integration reports a fatal configuration error if its per-player `IAccessoryVisualSync` or cosmetic mapping is missing

API and UI code is more lenient — null-conditional callbacks (`onError?.Invoke()`), silent skips (`ShopCatalog.BuildFromBackendResponse` skips unknown items), and null-guards before use (`if (walletText != null)`).

### Naming
- **Classes:** PascalCase — `PlayerAccessory`, `ShopCatalog`, `AccessoryRegistry`
- **Private fields:** camelCase, no prefix — `equippedPrefabsBySlot`, `activeShopCatalog`, `shopCatalogLoaded`
- **Constants:** UPPER_SNAKE_CASE — `BASE_URL`, `ACCESSORIES_API_URL`, `ACCESS_TOKEN_KEY`
- **SerializeField:** camelCase — `address`, `port`, `masterCatalogSO`, `statusText`
- **Booleans:** inconsistent — `isPopulated`, `isOwned`, `isCacheInitialized` vs bare `shopCatalogLoaded`, `done`
- **Singletons:** `Instance` property pattern across `AccessoryPool`, `TabManager`, `WalletDisplay`, `ClientPlayerAvatars`
- **Suffixes:** `SO` for ScriptableObjects (`AccessorySO`), `API` for REST clients (`ShopAPI`, `WalletAPI`)
- **Interfaces:** `I` prefix (`IAccessoryVisualSync`)
- **Debt:** UI builder code in `ShopPanel` uses abbreviations (`btn`, `tmp`, `bg`, `go`, `btnImg`)

### Data Lifecycle
- **ScriptableObjects** as immutable master data: `AccessorySO` holds all known accessories with lazy-cached dictionaries
- **`[SerializeField]`** for inspector-wired dependencies: `masterCatalogSO`, UI references (`statusText`, `walletText`)
- **Runtime state** built from backend + SO merge: `ShopCatalog` constructed in `AccessoryRegistry.LoadShopCatalog`
- **Network state:** `NetworkVariable<AccessoryState>` syncs equipped accessories as JSON string
- **DTOs:** `[System.Serializable]` classes with `[JsonProperty]` for snake_case mapping (`ShopApiDTOs.cs`)
- **Persistence:** JWT tokens owned in memory by an explicitly created `DjangoPlayerSession`; previous PlayerPrefs credentials are removed

### Network Sync Patterns
- **Client-authoritative transform/animation:** `ClientNetworkTransform` and `ClientNetworkAnimator` override `OnIsServerAuthoritative() → false`. Used on the player prefab so the owning client's movement and animation changes propagate to all other clients. The default server-authoritative versions silently ignore client-side changes — this appears to work for the host (who is the server) but breaks all clients.
- **Server-authoritative state:** `PlayerAccessory.AccessoryJson` uses `WritePermission.Server` and accepts only the gate-approved signed equipment snapshot. Clients persist their own equipment through IEquipmentService; the host/server verifies public proofs before replication. Do not accept arbitrary client IDs/loadouts through an RPC.
- **Prefab-baked NetworkBehaviours:** All `NetworkBehaviour` components must exist on the prefab before spawn. Adding via `AddComponent` after spawn breaks Netcode ownership tracking (`IsOwner` returns wrong values, RPCs fail). `SimplePlayerMovement` is on the prefab and uses `IsOwner` to gate input.

### Async Patterns
All async work uses **coroutines** with callback pairs:
- `IEnumerator` methods yielding `UnityWebRequest` — e.g., `ShopAPI.GetAccessories(Action<AccessoriesResponse> onSuccess, Action<string> onError)`
- Polling loops: `while (AccessoryRegistry.Instance == null || !AccessoryRegistry.Instance.IsShopCatalogLoaded()) yield return null;`
- Events for state change notification: `event Action EquippedStateChanged`, `event Action ShopCatalogLoaded`, `event Action OnAuthenticated`
- Authenticated dispatch through an injected session: `yield return session.Send(request, response)`
- No async/await or UniTask anywhere

### File Organization (typical member order)
1. Constants / static readonly fields
2. Serialized fields (`[SerializeField]`)
3. Private fields / backing state
4. Public properties and events
5. `NetworkVariable` declarations (`PlayerAccessory`)
6. Unity lifecycle: `Awake` → `Start` → `OnEnable` → `Update`/`LateUpdate` → `OnDestroy`
7. Netcode lifecycle: `OnNetworkSpawn` → `OnNetworkDespawn`
8. Public API methods
9. Private helpers
10. Nested types at bottom (`DevAuthBootstrap.DevAuthResponse`, `LoginRewardPanel.MessageResponse`)

---

## Mandatory Standards

### Null Safety
Never silently swallow null when correctness depends on the value. If a method requires a non-null argument, **throw with a diagnostic message** identifying what is missing and where. Use `TryGetComponent`/`TryGetValue` only when absence is a valid runtime state (e.g., optional UI element). Every JSON deserialization must null-check the result before use. **Debt:** `ShopCatalog.BuildFromBackendResponse` silently skips missing master-catalog entries — should at minimum log a warning. API callbacks lack null-checks on deserialized response bodies in several places (`DailyLoginAPI.ClaimDailyReward` error parsing).

### SOLID Principles
- **Single Responsibility:** One class, one reason to change. `ShopPanel` coordinates shop UI and purchase flow. Shared UI construction uses `ShopUIFactory`, display formatting uses `ShopDisplayFormatter`, button decisions use `ShopButtonStateResolver`, and equipment/preview operations use `AccessoryEquipService`. Further separation should be justified by current responsibilities.
- **Open/Closed:** Extend via ScriptableObjects and composition. `AccessorySO` is a good example. New accessory types should not require modifying `AccessoryPool`.
- **Liskov:** Always call `base.OnNetworkSpawn()` / `base.OnNetworkDespawn()`. `PlayerAvatarSetup` correctly does this.
- **Interface Segregation:** `IAccessoryVisualSync` is well-scoped. Keep interfaces focused.
- **Dependency Inversion:** Depend on abstractions/registries, not concrete MonoBehaviours. `PlayerAccessory` resolves its per-player visual component through `IAccessoryVisualSync`; `AccessoryPool` implements that interface.

### Naming
Fully descriptive names. No abbreviations (`btn`→`button`, `tmp`→`textLabel`, `bg`→`background`, `go`→`uiElement`). Booleans must read as questions: `isLoading`, `hasAuthority`, `isShopCatalogLoaded` (not `shopCatalogLoaded`). Collections named by contents: `equippedPrefabsBySlot` ✓, `availableSets` ✓. Constants: `UPPER_SNAKE_CASE` for compile-time constants, `PascalCase` for `static readonly`.

### Performance
- **No `GetComponent`/`Find` in `Update` or per-frame methods** — cache in `Awake`/`OnNetworkSpawn`. `SimplePlayerMovement.Start` correctly caches `animator`; `ThirdPersonCamera` queries `ClientPlayerAvatars.Instance` in `LateUpdate` which is acceptable (singleton field read) but the `GetComponent<CharacterController>` call should be cached.
- **No allocations in hot paths.** `SimplePlayerMovement.Update` is allocation-free ✓.
- **No `Debug.Log` in builds** — wrap in `#if UNITY_EDITOR` or use `[Conditional("UNITY_EDITOR")]`. **Debt:** Widespread unconditional `Debug.Log`/`Debug.LogError` calls across API and registry classes.
- Cache `Animator.StringToHash` results as `static readonly int` — `SimplePlayerMovement.SpeedParam` ✓.

### Serialization Safety
Every deserialized object must be null-checked before field access. `AccessoryLoadout.DeserializeAccessoryState` correctly validates ✓. Use `[FormerlySerializedAs("oldName")]` when renaming any `[SerializeField]` to preserve saved scene/prefab data. DTOs mapping to backend JSON must use `[JsonProperty("snake_case")]` consistently — `ShopApiDTOs` does this ✓.

### Testing
Domain logic classes (`EquipmentModule`, `AchievementsModule`, `ShopCatalog`) must have unit tests covering valid use, edge cases, and expected exceptions. Existing tests are exemplary — `EquipmentModuleTests` and `AchievementsModuleTests` cover all branches. Maintain this standard for all new domain types.

### Design only for concrete consumers
Do not design abstractions, extension points or failure policies until a concrete module needs them. Every new type must identify its current consumer and the existing responsibility it takes over. Reuse an existing owner instead of adding a forwarding wrapper. An anticipated future module is not sufficient justification.
