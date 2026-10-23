# PlayMode Network Integration Tests

Two-client network tests for verifying multiplayer sync across host and client using Netcode for GameObjects' built-in test infrastructure, with a live Django backend.

## Motivation

The project has 90+ EditMode tests covering pure domain logic but zero PlayMode tests. Every networked code path — `NetworkVariable` sync, ServerRpc validation, spawn/despawn lifecycle, avatar registration — is untested. This gap means we discover sync bugs only through manual two-player QA.

## Approach

Extend `NetcodeIntegrationTest` from `Unity.Netcode.TestHelpers.Runtime`. This base class spins up a host `NetworkManager` and one or more client `NetworkManager` instances within a single Unity test process, using real `UnityTransport` (localhost loopback). It handles connection, player prefab spawning, teardown, and provides `WaitForConditionOrTimeOut` for async assertions.

### Backend Requirement

Tests run against the live Django backend at `localhost:8000`. The `DevAuthBootstrap` flow authenticates each side and `AccessoryRegistry` loads the real shop catalog. This gives true end-to-end coverage.

Tests that need the backend will be marked with a `[Category("RequiresBackend")]` attribute so they can be filtered when the backend is unavailable.

### Player Prefab Composition

The test base class creates a player prefab in `OnCreatePlayerPrefab` with the components needed for sync:

- `PlayerAvatarSetup` (registers with `ClientPlayerAvatars`)
- `PlayerAccessory` (NetworkVariable sync of equipped state)
- `AccessoryPool` (implements `IAccessoryVisualSync`)

Scene singletons (`ClientPlayerAvatars`, `AccessoryRegistry`, `DevAuthBootstrap`, `AccessoryEquipService`) are created in `OnServerAndClientsCreated` or `OnServerAndClientsConnected` as GameObjects in the test scene.

### Key Design Decision: Direct NetworkVariable Driving vs Full Bootstrap

For accessory sync tests, we bypass the `LoadAccessoriesFromBackend` coroutine (which needs full auth + registry setup) and instead drive `PlayerAccessory` directly:

1. Call `RequestAccessoryUpdateServerRpc` with known JSON payloads
2. Assert the `NetworkVariable<AccessoryState>` propagates to the client's clone

For the full end-to-end test (auth → catalog load → equip → sync), we do the complete bootstrap. This gives us both isolated network sync tests and full integration tests.

## Assembly Definition

Update `Assets/Tests/PlayMode/PlayModeTests.asmdef` to reference the test helpers:

```json
{
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner",
        "MultiplayerLiveOpsToolkit",
        "Unity.Netcode.Runtime",
        "Unity.Netcode.TestHelpers.Runtime"
    ]
}
```

## Test Base Class

`NetworkTestBase` — abstract class extending `NetcodeIntegrationTest`:

- `NumberOfClients => 1` (host + 1 client = 2 players)
- `m_UseHost = true` (host acts as server + player)
- `OnCreatePlayerPrefab`: adds `PlayerAvatarSetup`, `PlayerAccessory`, `AccessoryPool`
- `OnServerAndClientsCreated`: creates `ClientPlayerAvatars` singleton GO, adds it to both server and client NetworkManager scenes
- Helper methods:
  - `GetHostPlayerAccessory()` — gets `PlayerAccessory` from the host's player object
  - `GetClientPlayerAccessory()` — gets `PlayerAccessory` from the client's player object
  - `GetHostPlayerOnClient()` — gets the client-side clone of the host's player
  - `GetClientPlayerOnHost()` — gets the host-side clone of the client's player
  - `WaitForAccessorySync(NetworkManager target, string expectedJson)` — polls until `AccessoryJson.Value.Json` matches

## Test Classes and Cases

### 1. `ClientPlayerAvatarsNetworkTests`

Tests `ClientPlayerAvatars` registration and cleanup across host+client.

| Test | What it verifies |
|------|-----------------|
| `HostAndClientAvatars_AfterSpawn_BothRegistered` | After connection, `ClientPlayerAvatars` on host has 2 entries in `avatarsByClientId`, client has 2 entries |
| `HostAvatar_AfterSpawn_CurrentClientAvatarSetOnEachSide` | Host's `currentClientAvatar` is its own avatar; client's `currentClientAvatar` is its own avatar |
| `ClientDisconnect_AfterShutdown_AvatarRemovedFromHost` | After client disconnects, host's `ClientPlayerAvatars.avatarsByClientId` has 1 entry |

### 2. `AccessoryStateSyncTests`

Tests `NetworkVariable<AccessoryState>` propagation from host to client and vice versa.

| Test | What it verifies |
|------|-----------------|
| `HostEquipsAccessory_ClientReceivesUpdate` | Host writes `AccessoryJson` via ServerRpc, client-side clone's `AccessoryJson.Value.Json` matches within timeout |
| `HostUnequips_ClientSeesEmptyState` | Host sends empty JSON, client-side clone sees empty |
| `MultipleSlots_AllSyncToClient` | Host equips Headwear + MainHand, client sees both in the JSON map |
| `ConflictResolution_SyncsCorrectly` | Host equips Headwear (conflicts Hair), client sees only Headwear in state |
| `ClearAllEquipped_ClientSeesEmpty` | Host clears all, client sees empty state |
| `ClientEquipsAccessory_HostReceivesUpdate` | Client writes via ServerRpc, host-side clone's `AccessoryJson` matches |

### 3. `ServerRpcValidationTests`

Tests that the server rejects invalid accessory payloads.

| Test | What it verifies |
|------|-----------------|
| `MalformedJson_ServerRejectsAndStateUnchanged` | Client sends garbage JSON via ServerRpc, server's `AccessoryJson` doesn't change |
| `EmptyString_ServerAcceptsAndClearsState` | Client sends empty string, server writes empty `AccessoryState` |
| `ValidJson_ServerAcceptsAndWritesState` | Client sends valid JSON, server writes it to `AccessoryJson` |

### 4. `AccessoryVisualSyncNetworkTests`

Tests that `AccessoryPool` (the `IAccessoryVisualSync` impl) responds to network state changes on remote players.

| Test | What it verifies |
|------|-----------------|
| `HostEquips_ClientClonesAccessoryPoolReflectsActiveSlot` | After host equips, client-side clone's `AccessoryPool.IsSlotActive` returns true for that slot |
| `HostUnequips_ClientClonesAccessoryPoolReflectsInactiveSlot` | After host unequips, client-side clone's `AccessoryPool.IsSlotActive` returns false |
| `HostEquipsDifferentSlot_PreviousSlotDeactivated` | After host changes slot, client sees new slot active, old slot inactive |

### 5. `FullBootstrapNetworkTests`

End-to-end tests requiring the live backend. Marked `[Category("RequiresBackend")]`.

| Test | What it verifies |
|------|-----------------|
| `DevAuth_BothPlayersAuthenticate` | Both host and client complete `DevAuthBootstrap` and have valid tokens |
| `ShopCatalogLoad_BothPlayersHaveCatalog` | Both sides have `AccessoryRegistry.IsShopCatalogLoaded() == true` after bootstrap |
| `EquipFromCatalog_SyncsAcrossNetwork` | Host equips a real catalog item, client sees the update via NetworkVariable |

## File Layout

```
Assets/Tests/PlayMode/
├── PlayModeTests.asmdef          (updated references)
├── NetworkTestBase.cs            (base class extending NetcodeIntegrationTest)
├── ClientPlayerAvatarsNetworkTests.cs
├── AccessoryStateSyncTests.cs
├── ServerRpcValidationTests.cs
├── AccessoryVisualSyncNetworkTests.cs
└── FullBootstrapNetworkTests.cs
```

## Risks and Mitigations

| Risk | Mitigation |
|------|-----------|
| Backend not running → tests fail | `[Category("RequiresBackend")]` on backend-dependent tests; pure network sync tests don't need backend |
| Timing sensitivity in NetworkVariable propagation | Use `WaitForConditionOrTimeOut` with 8s timeout (the `NetcodeIntegrationTest` default) rather than frame counting |
| `AccessoryPool.Equip` needs real prefabs and bone hierarchy | For visual sync tests, create minimal test prefabs (empty GO with `MeshRenderer`) — doesn't need real skinned meshes to verify slot tracking |
| `AccessoryRegistry` singleton conflicts between test runs | Teardown nulls all singleton `.Instance` fields; `NetcodeIntegrationTest.ShutdownAndCleanUp` destroys all NetworkObjects |

## Out of Scope

- Movement/`NetworkTransform` sync testing (no custom logic to test — it's stock Netcode)
- UI panel tests (ShopPanel, InventoryPanel — these are not networked)
- Load testing / performance (this is correctness testing)
