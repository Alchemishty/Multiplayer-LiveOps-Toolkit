# PlayMode Network Integration Tests — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build PlayMode tests that verify multiplayer sync works correctly between a host and client using Netcode's in-process test infrastructure.

**Architecture:** Extend `NetcodeIntegrationTest` (from `Unity.Netcode.TestHelpers.Runtime`) which manages host+client `NetworkManager` instances in a single test process. A shared `NetworkTestBase` creates the player prefab with `PlayerAvatarSetup`, `PlayerAccessory`, and `AccessoryPool`. Tests drive the public API and assert `NetworkVariable` propagation across the two instances.

**Tech Stack:** Unity Test Framework (NUnit), Netcode for GameObjects 1.9.1 TestHelpers, C#

**Spec:** `docs/superpowers/specs/2026-10-22-playmode-network-tests-design.md`

## Global Constraints

- Unity Netcode for GameObjects 1.9.1
- All test files in `Assets/Tests/PlayMode/`
- Tests use `[UnityTest]` with `IEnumerator` (PlayMode requirement)
- `RequestAccessoryUpdateServerRpc` is private — tests must use public methods or `internal` + `InternalsVisibleTo`
- No git operations per project rules
- `PlayerAccessory.OnNetworkSpawn` calls `LoadAccessoriesFromBackend` for owners — pure sync tests must account for this by not having `ShopAPI`/`AccessoryRegistry` available (the coroutine will fail but not crash the test since we drive state via ServerRpc after)

## Review Focus

1. `PlayerAccessory.OnNetworkSpawn` auto-starts `LoadAccessoriesFromBackend` on owners — if `ShopAPI.Instance` auto-creates and hits a dead backend, it could log errors or hang. Tests should use `LogAssert.Expect` or `LogAssert.ignoreFailingMessages` for expected backend errors in non-backend tests.
2. Singleton cleanup between tests — `ClientPlayerAvatars.Instance`, `AccessoryRegistry.Instance`, etc. persist as static fields. If teardown misses one, the next test gets stale state.
3. `AccessoryPool.Equip` needs a non-null prefab and avatarRoot — remote `ApplyRemoteAccessoryState` calls `OnAccessoryJsonChanged` which calls `AccessoryRegistry.Instance` for prefab lookup. Without a registry, remote visual sync won't fire. Visual sync tests need a mock registry.
4. `WaitForConditionOrTimeOut` default is 8s — if a test's condition is never met, it hangs for 8s before failing. Keep conditions tight.
5. `NetcodeIntegrationTest` teardown destroys all NetworkObjects but not plain GameObjects — singleton GameObjects without NetworkObject must be cleaned up manually.

---

### Task 1: Assembly Definition and InternalsVisibleTo

**Files:**
- Modify: `Assets/Tests/PlayMode/PlayModeTests.asmdef`
- Modify: `Assets/Scripts/MultiplayerLiveOpsToolkit.asmdef`

**Interfaces:**
- Consumes: nothing
- Produces: PlayMode tests can reference `Unity.Netcode.TestHelpers.Runtime` and access `internal` members of production assembly

- [ ] **Step 1: Update PlayModeTests.asmdef**

Add `Unity.Netcode.TestHelpers.Runtime` to references:

```json
{
    "name": "PlayModeTests",
    "rootNamespace": "",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner",
        "MultiplayerLiveOpsToolkit",
        "Unity.Netcode.Runtime",
        "Unity.Netcode.TestHelpers.Runtime"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll",
        "Newtonsoft.Json.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Make `RequestAccessoryUpdateServerRpc` internal**

In `Assets/Scripts/Player/PlayerAccessory.cs`, change line 59-60 from:

```csharp
[ServerRpc]
private void RequestAccessoryUpdateServerRpc(string accessoryJson, ServerRpcParams rpcParams = default)
```

to:

```csharp
[ServerRpc]
internal void RequestAccessoryUpdateServerRpc(string accessoryJson, ServerRpcParams rpcParams = default)
```

- [ ] **Step 3: Add InternalsVisibleTo**

In `Assets/Scripts/MultiplayerLiveOpsToolkit.asmdef`, add:

```json
{
    "name": "MultiplayerLiveOpsToolkit",
    "rootNamespace": "",
    "references": [
        "Unity.Netcode.Runtime",
        "Unity.TextMeshPro"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

Also create `Assets/Scripts/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("PlayModeTests")]
```

- [ ] **Step 4: Verify compilation**

Use `refresh_unity` + `read_console` to confirm zero errors.

---

### Task 2: NetworkTestBase

**Files:**
- Create: `Assets/Tests/PlayMode/NetworkTestBase.cs`

**Interfaces:**
- Consumes: `NetcodeIntegrationTest` base class, `PlayerAvatarSetup`, `PlayerAccessory`, `AccessoryPool`, `ClientPlayerAvatars`
- Produces: `NetworkTestBase` with `NumberOfClients => 1`, player prefab with all needed components, helper methods `GetPlayerAccessoryForClient(NetworkManager)`, `GetPlayerObjectForClient(NetworkManager, ulong ownerClientId)`, `WaitForAccessoryJsonOnClient(NetworkManager, ulong ownerClientId, Func<string, bool> predicate)`

- [ ] **Step 1: Create NetworkTestBase.cs**

```csharp
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

public abstract class NetworkTestBase : NetcodeIntegrationTest
{
    protected override int NumberOfClients => 1;

    protected NetworkManager HostManager => m_ServerNetworkManager;
    protected NetworkManager ClientManager => m_ClientNetworkManagers[0];

    private GameObject clientPlayerAvatarsObject;

    protected override void OnCreatePlayerPrefab()
    {
        m_PlayerPrefab.AddComponent<PlayerAvatarSetup>();
        m_PlayerPrefab.AddComponent<PlayerAccessory>();
        m_PlayerPrefab.AddComponent<AccessoryPool>();
    }

    protected override void OnServerAndClientsCreated()
    {
        clientPlayerAvatarsObject = new GameObject("ClientPlayerAvatars");
        clientPlayerAvatarsObject.AddComponent<ClientPlayerAvatars>();
    }

    protected override IEnumerator OnTearDown()
    {
        if (clientPlayerAvatarsObject != null)
        {
            Object.DestroyImmediate(clientPlayerAvatarsObject);
            clientPlayerAvatarsObject = null;
        }

        if (ClientPlayerAvatars.Instance != null)
        {
            ClientPlayerAvatars.Instance.avatarsByClientId.Clear();
            ClientPlayerAvatars.Instance.currentClientAvatar = null;
        }

        yield return base.OnTearDown();
    }

    protected NetworkObject GetPlayerObjectForClient(NetworkManager observer, ulong ownerClientId)
    {
        foreach (var kvp in observer.SpawnManager.SpawnedObjects)
        {
            if (kvp.Value.IsPlayerObject && kvp.Value.OwnerClientId == ownerClientId)
                return kvp.Value;
        }
        return null;
    }

    protected PlayerAccessory GetPlayerAccessoryOnObserver(NetworkManager observer, ulong ownerClientId)
    {
        var playerObject = GetPlayerObjectForClient(observer, ownerClientId);
        if (playerObject == null) return null;
        return playerObject.GetComponent<PlayerAccessory>();
    }

    protected IEnumerator WaitForAccessoryJson(
        NetworkManager observer,
        ulong ownerClientId,
        Func<string, bool> predicate,
        string failMessage = "AccessoryJson condition not met")
    {
        yield return WaitForConditionOrTimeOut(() =>
        {
            var playerAccessory = GetPlayerAccessoryOnObserver(observer, ownerClientId);
            if (playerAccessory == null) return false;
            return predicate(playerAccessory.AccessoryJson.Value.Json);
        });
        AssertOnTimeout(failMessage);
    }
}
```

- [ ] **Step 2: Verify compilation**

Use `refresh_unity` + `read_console` to confirm zero errors.

---

### Task 3: ClientPlayerAvatarsNetworkTests

**Files:**
- Create: `Assets/Tests/PlayMode/ClientPlayerAvatarsNetworkTests.cs`

**Interfaces:**
- Consumes: `NetworkTestBase`, `ClientPlayerAvatars`
- Produces: 3 passing PlayMode tests

- [ ] **Step 1: Create test file**

```csharp
using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

public class ClientPlayerAvatarsNetworkTests : NetworkTestBase
{
    [UnityTest]
    public IEnumerator HostAndClientAvatars_AfterSpawn_BothRegistered()
    {
        yield return WaitForConditionOrTimeOut(() =>
            ClientPlayerAvatars.Instance != null &&
            ClientPlayerAvatars.Instance.avatarsByClientId.Count == 2);
        AssertOnTimeout("Expected 2 avatars registered in ClientPlayerAvatars");

        Assert.AreEqual(2, ClientPlayerAvatars.Instance.avatarsByClientId.Count);
        Assert.IsTrue(ClientPlayerAvatars.Instance.avatarsByClientId.ContainsKey(HostManager.LocalClientId));
        Assert.IsTrue(ClientPlayerAvatars.Instance.avatarsByClientId.ContainsKey(ClientManager.LocalClientId));
    }

    [UnityTest]
    public IEnumerator HostAvatar_AfterSpawn_CurrentClientAvatarSetOnEachSide()
    {
        yield return WaitForConditionOrTimeOut(() =>
            ClientPlayerAvatars.Instance != null &&
            ClientPlayerAvatars.Instance.currentClientAvatar != null);
        AssertOnTimeout("currentClientAvatar was not set");

        var localAvatar = ClientPlayerAvatars.Instance.currentClientAvatar;
        Assert.IsNotNull(localAvatar);

        var avatarNetworkObject = localAvatar.GetComponent<NetworkObject>();
        Assert.IsNotNull(avatarNetworkObject);
        Assert.IsTrue(avatarNetworkObject.IsOwner,
            "currentClientAvatar should be the locally owned avatar");
    }

    [UnityTest]
    public IEnumerator ClientDisconnect_AfterShutdown_AvatarRemovedFromHost()
    {
        yield return WaitForConditionOrTimeOut(() =>
            ClientPlayerAvatars.Instance != null &&
            ClientPlayerAvatars.Instance.avatarsByClientId.Count == 2);
        AssertOnTimeout("Expected 2 avatars before disconnect");

        var clientId = ClientManager.LocalClientId;

        yield return StopOneClient(ClientManager);

        yield return WaitForConditionOrTimeOut(() =>
            !ClientPlayerAvatars.Instance.avatarsByClientId.ContainsKey(clientId));
        AssertOnTimeout("Client avatar was not removed after disconnect");

        Assert.AreEqual(1, ClientPlayerAvatars.Instance.avatarsByClientId.Count);
        Assert.IsTrue(ClientPlayerAvatars.Instance.avatarsByClientId.ContainsKey(HostManager.LocalClientId));
    }
}
```

- [ ] **Step 2: Verify compilation and run tests**

Use `refresh_unity` + `read_console`, then `run_tests` with `testMode=PlayMode`.

---

### Task 4: AccessoryStateSyncTests

**Files:**
- Create: `Assets/Tests/PlayMode/AccessoryStateSyncTests.cs`

**Interfaces:**
- Consumes: `NetworkTestBase`, `PlayerAccessory.RequestAccessoryUpdateServerRpc` (internal), `AccessoryState`, `AccessoryLoadout`
- Produces: 6 passing PlayMode tests

- [ ] **Step 1: Create test file**

```csharp
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

public class AccessoryStateSyncTests : NetworkTestBase
{
    protected override IEnumerator OnSetup()
    {
        LogAssert.ignoreFailingMessages = true;
        yield return base.OnSetup();
    }

    protected override IEnumerator OnTearDown()
    {
        LogAssert.ignoreFailingMessages = false;
        yield return base.OnTearDown();
    }

    [UnityTest]
    public IEnumerator HostEquipsAccessory_ClientReceivesUpdate()
    {
        var hostAccessory = GetPlayerAccessoryOnObserver(HostManager, HostManager.LocalClientId);
        Assert.IsNotNull(hostAccessory, "Host PlayerAccessory not found");

        string testJson = "{\"Headwear\":1}";
        hostAccessory.RequestAccessoryUpdateServerRpc(testJson);

        yield return WaitForAccessoryJson(
            ClientManager,
            HostManager.LocalClientId,
            json => json == testJson,
            "Client did not receive host's accessory update");

        var clientClone = GetPlayerAccessoryOnObserver(ClientManager, HostManager.LocalClientId);
        Assert.AreEqual(testJson, clientClone.AccessoryJson.Value.Json);
    }

    [UnityTest]
    public IEnumerator HostUnequips_ClientSeesEmptyState()
    {
        var hostAccessory = GetPlayerAccessoryOnObserver(HostManager, HostManager.LocalClientId);

        hostAccessory.RequestAccessoryUpdateServerRpc("{\"Headwear\":1}");
        yield return WaitForAccessoryJson(
            ClientManager, HostManager.LocalClientId,
            json => json == "{\"Headwear\":1}",
            "Initial equip not synced");

        hostAccessory.RequestAccessoryUpdateServerRpc("");
        yield return WaitForAccessoryJson(
            ClientManager, HostManager.LocalClientId,
            json => json == "",
            "Client did not receive empty state after unequip");
    }

    [UnityTest]
    public IEnumerator MultipleSlots_AllSyncToClient()
    {
        var hostAccessory = GetPlayerAccessoryOnObserver(HostManager, HostManager.LocalClientId);

        string testJson = "{\"Headwear\":1,\"MainHand\":20}";
        hostAccessory.RequestAccessoryUpdateServerRpc(testJson);

        yield return WaitForAccessoryJson(
            ClientManager, HostManager.LocalClientId,
            json =>
            {
                if (string.IsNullOrEmpty(json)) return false;
                var map = AccessoryLoadout.DeserializeAccessoryState(json);
                return map.ContainsKey(AccessoryType.Headwear) && map[AccessoryType.Headwear] == 1
                    && map.ContainsKey(AccessoryType.MainHand) && map[AccessoryType.MainHand] == 20;
            },
            "Client did not receive multi-slot update");
    }

    [UnityTest]
    public IEnumerator ConflictResolution_SyncsCorrectly()
    {
        var hostAccessory = GetPlayerAccessoryOnObserver(HostManager, HostManager.LocalClientId);

        hostAccessory.RequestAccessoryUpdateServerRpc("{\"Hair\":5}");
        yield return WaitForAccessoryJson(
            ClientManager, HostManager.LocalClientId,
            json => !string.IsNullOrEmpty(json) && json.Contains("Hair"),
            "Initial Hair equip not synced");

        hostAccessory.RequestAccessoryUpdateServerRpc("{\"Headwear\":1}");
        yield return WaitForAccessoryJson(
            ClientManager, HostManager.LocalClientId,
            json =>
            {
                if (string.IsNullOrEmpty(json)) return false;
                var map = AccessoryLoadout.DeserializeAccessoryState(json);
                return map.ContainsKey(AccessoryType.Headwear) && !map.ContainsKey(AccessoryType.Hair);
            },
            "Conflict resolution did not sync — Hair should be gone after Headwear equip");
    }

    [UnityTest]
    public IEnumerator ClearAllEquipped_ClientSeesEmpty()
    {
        var hostAccessory = GetPlayerAccessoryOnObserver(HostManager, HostManager.LocalClientId);

        hostAccessory.RequestAccessoryUpdateServerRpc("{\"Headwear\":1,\"MainHand\":20}");
        yield return WaitForAccessoryJson(
            ClientManager, HostManager.LocalClientId,
            json => !string.IsNullOrEmpty(json) && json.Contains("Headwear"),
            "Initial equip not synced");

        hostAccessory.RequestAccessoryUpdateServerRpc("");
        yield return WaitForAccessoryJson(
            ClientManager, HostManager.LocalClientId,
            json => json == "",
            "Client did not see empty state after clear");
    }

    [UnityTest]
    public IEnumerator ClientEquipsAccessory_HostReceivesUpdate()
    {
        var clientAccessory = GetPlayerAccessoryOnObserver(ClientManager, ClientManager.LocalClientId);
        Assert.IsNotNull(clientAccessory, "Client PlayerAccessory not found on client");

        string testJson = "{\"MainHand\":10}";
        clientAccessory.RequestAccessoryUpdateServerRpc(testJson);

        yield return WaitForAccessoryJson(
            HostManager,
            ClientManager.LocalClientId,
            json => json == testJson,
            "Host did not receive client's accessory update");
    }
}
```

- [ ] **Step 2: Verify compilation and run tests**

---

### Task 5: ServerRpcValidationTests

**Files:**
- Create: `Assets/Tests/PlayMode/ServerRpcValidationTests.cs`

**Interfaces:**
- Consumes: `NetworkTestBase`, `PlayerAccessory.RequestAccessoryUpdateServerRpc` (internal)
- Produces: 3 passing PlayMode tests

- [ ] **Step 1: Create test file**

```csharp
using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

public class ServerRpcValidationTests : NetworkTestBase
{
    protected override IEnumerator OnSetup()
    {
        LogAssert.ignoreFailingMessages = true;
        yield return base.OnSetup();
    }

    protected override IEnumerator OnTearDown()
    {
        LogAssert.ignoreFailingMessages = false;
        yield return base.OnTearDown();
    }

    [UnityTest]
    public IEnumerator MalformedJson_ServerRejectsAndStateUnchanged()
    {
        var hostAccessory = GetPlayerAccessoryOnObserver(HostManager, HostManager.LocalClientId);

        string validJson = "{\"Headwear\":1}";
        hostAccessory.RequestAccessoryUpdateServerRpc(validJson);
        yield return WaitForAccessoryJson(
            HostManager, HostManager.LocalClientId,
            json => json == validJson,
            "Valid JSON was not accepted");

        hostAccessory.RequestAccessoryUpdateServerRpc("THIS IS NOT JSON {{{");

        yield return new WaitForSecondsRealtime(1.0f);

        var currentState = GetPlayerAccessoryOnObserver(HostManager, HostManager.LocalClientId);
        Assert.AreEqual(validJson, currentState.AccessoryJson.Value.Json,
            "Server should have rejected malformed JSON and kept previous state");
    }

    [UnityTest]
    public IEnumerator EmptyString_ServerAcceptsAndClearsState()
    {
        var hostAccessory = GetPlayerAccessoryOnObserver(HostManager, HostManager.LocalClientId);

        hostAccessory.RequestAccessoryUpdateServerRpc("{\"Headwear\":1}");
        yield return WaitForAccessoryJson(
            HostManager, HostManager.LocalClientId,
            json => json == "{\"Headwear\":1}",
            "Initial state not set");

        hostAccessory.RequestAccessoryUpdateServerRpc("");

        yield return WaitForAccessoryJson(
            HostManager, HostManager.LocalClientId,
            json => json == "",
            "Empty string should clear state");
    }

    [UnityTest]
    public IEnumerator ValidJson_ServerAcceptsAndWritesState()
    {
        var hostAccessory = GetPlayerAccessoryOnObserver(HostManager, HostManager.LocalClientId);

        string testJson = "{\"Torso\":7,\"Feet\":3}";
        hostAccessory.RequestAccessoryUpdateServerRpc(testJson);

        yield return WaitForAccessoryJson(
            HostManager, HostManager.LocalClientId,
            json => json == testJson,
            "Server did not accept valid JSON");
    }
}
```

- [ ] **Step 2: Verify compilation and run tests**

---

### Task 6: AccessoryVisualSyncNetworkTests

**Files:**
- Create: `Assets/Tests/PlayMode/AccessoryVisualSyncNetworkTests.cs`

**Interfaces:**
- Consumes: `NetworkTestBase`, `AccessoryPool`, `AccessoryRegistry`, `AccessorySO`
- Produces: 3 passing PlayMode tests

Note: These tests need `AccessoryRegistry.Instance` with a real `AccessorySO` so that `OnAccessoryJsonChanged` can resolve prefabs. We create a minimal `AccessorySO` with test `AccessoryItemData` entries pointing to simple cube prefabs.

- [ ] **Step 1: Create test file**

```csharp
using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

public class AccessoryVisualSyncNetworkTests : NetworkTestBase
{
    private GameObject registryObject;
    private AccessorySO testCatalog;
    private GameObject headwearPrefab;
    private GameObject mainHandPrefab;

    protected override IEnumerator OnSetup()
    {
        LogAssert.ignoreFailingMessages = true;
        yield return base.OnSetup();
    }

    protected override IEnumerator OnServerAndClientsConnected()
    {
        yield return base.OnServerAndClientsConnected();

        headwearPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        headwearPrefab.name = "TestHeadwear";
        headwearPrefab.SetActive(false);

        mainHandPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mainHandPrefab.name = "TestMainHand";
        mainHandPrefab.SetActive(false);

        testCatalog = ScriptableObject.CreateInstance<AccessorySO>();
        testCatalog.accessories = new AccessoryItemData[]
        {
            new AccessoryItemData { itemId = 1, prefab = headwearPrefab, accessoryType = AccessoryType.Headwear },
            new AccessoryItemData { itemId = 20, prefab = mainHandPrefab, accessoryType = AccessoryType.MainHand },
        };

        registryObject = new GameObject("AccessoryRegistry");
        var registry = registryObject.AddComponent<AccessoryRegistry>();
        var field = typeof(AccessoryRegistry).GetField("masterCatalogSO",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field.SetValue(registry, testCatalog);
    }

    protected override IEnumerator OnTearDown()
    {
        if (registryObject != null) Object.DestroyImmediate(registryObject);
        if (testCatalog != null) Object.DestroyImmediate(testCatalog);
        if (headwearPrefab != null) Object.DestroyImmediate(headwearPrefab);
        if (mainHandPrefab != null) Object.DestroyImmediate(mainHandPrefab);
        AccessoryRegistry accessoryRegistryInstance = AccessoryRegistry.Instance;
        if (accessoryRegistryInstance != null)
        {
            var instanceProp = typeof(AccessoryRegistry).GetProperty("Instance",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            instanceProp.SetValue(null, null);
        }
        LogAssert.ignoreFailingMessages = false;
        yield return base.OnTearDown();
    }

    [UnityTest]
    public IEnumerator HostEquips_ClientClonesAccessoryPoolReflectsActiveSlot()
    {
        var hostAccessory = GetPlayerAccessoryOnObserver(HostManager, HostManager.LocalClientId);
        hostAccessory.RequestAccessoryUpdateServerRpc("{\"Headwear\":1}");

        yield return WaitForConditionOrTimeOut(() =>
        {
            var clientClone = GetPlayerObjectForClient(ClientManager, HostManager.LocalClientId);
            if (clientClone == null) return false;
            var pool = clientClone.GetComponent<AccessoryPool>();
            return pool != null && pool.IsSlotActive(AccessoryType.Headwear);
        });
        AssertOnTimeout("Client clone's AccessoryPool did not reflect Headwear as active");
    }

    [UnityTest]
    public IEnumerator HostUnequips_ClientClonesAccessoryPoolReflectsInactiveSlot()
    {
        var hostAccessory = GetPlayerAccessoryOnObserver(HostManager, HostManager.LocalClientId);

        hostAccessory.RequestAccessoryUpdateServerRpc("{\"Headwear\":1}");
        yield return WaitForConditionOrTimeOut(() =>
        {
            var clientClone = GetPlayerObjectForClient(ClientManager, HostManager.LocalClientId);
            if (clientClone == null) return false;
            var pool = clientClone.GetComponent<AccessoryPool>();
            return pool != null && pool.IsSlotActive(AccessoryType.Headwear);
        });
        AssertOnTimeout("Initial equip not reflected on client");

        hostAccessory.RequestAccessoryUpdateServerRpc("");
        yield return WaitForConditionOrTimeOut(() =>
        {
            var clientClone = GetPlayerObjectForClient(ClientManager, HostManager.LocalClientId);
            if (clientClone == null) return false;
            var pool = clientClone.GetComponent<AccessoryPool>();
            return pool != null && !pool.IsSlotActive(AccessoryType.Headwear);
        });
        AssertOnTimeout("Client clone's AccessoryPool still shows Headwear active after unequip");
    }

    [UnityTest]
    public IEnumerator HostEquipsDifferentSlot_PreviousSlotDeactivated()
    {
        var hostAccessory = GetPlayerAccessoryOnObserver(HostManager, HostManager.LocalClientId);

        hostAccessory.RequestAccessoryUpdateServerRpc("{\"Headwear\":1}");
        yield return WaitForConditionOrTimeOut(() =>
        {
            var clientClone = GetPlayerObjectForClient(ClientManager, HostManager.LocalClientId);
            if (clientClone == null) return false;
            var pool = clientClone.GetComponent<AccessoryPool>();
            return pool != null && pool.IsSlotActive(AccessoryType.Headwear);
        });
        AssertOnTimeout("Headwear not active on client");

        hostAccessory.RequestAccessoryUpdateServerRpc("{\"MainHand\":20}");
        yield return WaitForConditionOrTimeOut(() =>
        {
            var clientClone = GetPlayerObjectForClient(ClientManager, HostManager.LocalClientId);
            if (clientClone == null) return false;
            var pool = clientClone.GetComponent<AccessoryPool>();
            return pool != null
                && pool.IsSlotActive(AccessoryType.MainHand)
                && !pool.IsSlotActive(AccessoryType.Headwear);
        });
        AssertOnTimeout("Slot switch not reflected: MainHand should be active, Headwear inactive");
    }
}
```

- [ ] **Step 2: Verify compilation and run tests**
