# Accessory Sets UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface accessory sets in a dedicated Sets tab with preview, purchase, and equip flows.

**Architecture:** Pure C# `SetPreviewStateTracker` in the Data layer handles multi-slot preview state (testable in EditMode). `SetsPanel` MonoBehaviour wires UI to existing `AccessoryRegistry` set queries. `AccessoryEquipService` orchestrates set preview/equip using existing `IAccessoryVisualSync` and `PlayerAccessory.UpdateEquippedAccessories(List<int>)`.

**Tech Stack:** Unity (Netcode for GameObjects), C#, NUnit, Newtonsoft.Json, Unity Test Framework

**Spec:** `docs/superpowers/specs/2026-10-15-accessory-sets-ui-design.md`

## Global Constraints

- Data layer (`Assets/Scripts/Data/`) must not import Player, Example, Registry, or API layers
- Never silently consume null — throw `ArgumentNullException`/`InvalidOperationException` with diagnostic message
- Fully descriptive variable names — no abbreviations beyond `id`/`url`/`api`/`ui`
- Booleans prefixed `is`/`has`/`can`/`should`/`was`
- Every event subscription must have a matching unsubscription in the lifecycle counterpart
- Every code change must include tests (EditMode for pure logic)
- All JSON deserialization must validate the result before field access

## Review Focus

1. **Set with zero accessories after filtering** — `GetAccessoryIdsInSet` returns empty list; `PreviewSet`/`EquipSet` should handle gracefully (no-op or early return), not crash on empty iteration.
2. **Set preview while single-item preview is active** — cross-preview coordination must clear the single-item preview first and restore its slot, then start set preview; reversing order loses the restore state.
3. **Purchase succeeds but MarkSetOwned called before wallet refresh** — UI shows stale wallet balance; wallet refresh must happen alongside ownership marking.
4. **Set contains accessories in conflicting slots (Headwear + Hair)** — `PlayerAccessory.UpdateEquippedAccessories(List<int>)` calls `ResolveConflicts` per item; last item wins. The set's intended visual should be deterministic — equip in list order.
5. **ClearSetPreview when avatar is null** — player disconnected during preview; must not throw, should gracefully skip visual restoration.

---

### Task 1: SetPreviewStateTracker — Pure C# Multi-Slot Preview State

**Files:**
- Create: `Assets/Scripts/Data/SetPreviewStateTracker.cs`
- Create: `Assets/Tests/EditMode/SetPreviewStateTrackerTests.cs`

**Interfaces:**
- Consumes: `AccessoryType` enum from `Assets/Scripts/Data/AccessoryType.cs`
- Produces:
  - `SetPreviewStateTracker.StartSetPreview(int setId, List<SetPreviewSlotInput> slots)`
  - `SetPreviewStateTracker.ClearSetPreview() → ClearSetResult`
  - `SetPreviewStateTracker.IsSetPreviewActive` (bool)
  - `SetPreviewStateTracker.PreviewedSetId` (int)
  - `SetPreviewStateTracker.GetPreviewedSlots() → IReadOnlyDictionary<AccessoryType, SetPreviewSlot>`
  - `event Action SetPreviewChanged`
  - Nested structs: `SetPreviewSlot`, `SetPreviewSlotInput`, `ClearSetResult`, `ClearedSlotInfo`

- [ ] **Step 1: Write the first failing test — stores set ID and slots**

In `Assets/Tests/EditMode/SetPreviewStateTrackerTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;

[TestFixture]
public class SetPreviewStateTrackerTests
{
    [Test]
    public void StartSetPreview_StoresSetIdAndSlots()
    {
        var tracker = new SetPreviewStateTracker();
        var slots = new List<SetPreviewStateTracker.SetPreviewSlotInput>
        {
            new() { slot = AccessoryType.Headwear, itemId = 1, currentEquippedItemId = -1 },
            new() { slot = AccessoryType.MainHand, itemId = 2, currentEquippedItemId = 5 },
        };

        tracker.StartSetPreview(10, slots);

        Assert.IsTrue(tracker.IsSetPreviewActive);
        Assert.AreEqual(10, tracker.PreviewedSetId);
        var previewedSlots = tracker.GetPreviewedSlots();
        Assert.AreEqual(2, previewedSlots.Count);
        Assert.AreEqual(1, previewedSlots[AccessoryType.Headwear].previewedItemId);
        Assert.AreEqual(2, previewedSlots[AccessoryType.MainHand].previewedItemId);
        Assert.AreEqual(-1, previewedSlots[AccessoryType.Headwear].previouslyEquippedItemId);
        Assert.AreEqual(5, previewedSlots[AccessoryType.MainHand].previouslyEquippedItemId);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: Unity Test Framework EditMode tests or `dotnet test` equivalent
Expected: FAIL — `SetPreviewStateTracker` does not exist

- [ ] **Step 3: Write minimal implementation to pass**

In `Assets/Scripts/Data/SetPreviewStateTracker.cs`:

```csharp
using System;
using System.Collections.Generic;

public class SetPreviewStateTracker
{
    private int previewedSetId = -1;
    private readonly Dictionary<AccessoryType, SetPreviewSlot> previewedSlots = new();

    public bool IsSetPreviewActive => previewedSetId > 0;
    public int PreviewedSetId => previewedSetId;

    public event Action SetPreviewChanged;

    public void StartSetPreview(int setId, List<SetPreviewSlotInput> slots)
    {
        if (setId <= 0)
            throw new ArgumentException("setId must be positive", nameof(setId));
        if (slots == null || slots.Count == 0)
            throw new ArgumentException("slots must not be null or empty", nameof(slots));

        previewedSetId = setId;
        previewedSlots.Clear();

        foreach (var slotInput in slots)
        {
            previewedSlots[slotInput.slot] = new SetPreviewSlot
            {
                previewedItemId = slotInput.itemId,
                previouslyEquippedItemId = slotInput.currentEquippedItemId,
            };
        }

        SetPreviewChanged?.Invoke();
    }

    public ClearSetResult ClearSetPreview()
    {
        if (!IsSetPreviewActive)
            return ClearSetResult.NoPreviewActive;

        var clearedSlots = new List<ClearedSlotInfo>();
        foreach (var kvp in previewedSlots)
        {
            clearedSlots.Add(new ClearedSlotInfo
            {
                clearedSlot = kvp.Key,
                clearedItemId = kvp.Value.previewedItemId,
                restoreItemId = kvp.Value.previouslyEquippedItemId,
            });
        }

        var result = new ClearSetResult
        {
            WasActive = true,
            ClearedSetId = previewedSetId,
            ClearedSlots = clearedSlots,
        };

        previewedSetId = -1;
        previewedSlots.Clear();
        SetPreviewChanged?.Invoke();

        return result;
    }

    public IReadOnlyDictionary<AccessoryType, SetPreviewSlot> GetPreviewedSlots()
    {
        return previewedSlots;
    }

    public struct SetPreviewSlot
    {
        public int previewedItemId;
        public int previouslyEquippedItemId;
    }

    public struct SetPreviewSlotInput
    {
        public AccessoryType slot;
        public int itemId;
        public int currentEquippedItemId;
    }

    public struct ClearedSlotInfo
    {
        public AccessoryType clearedSlot;
        public int clearedItemId;
        public int restoreItemId;
    }

    public struct ClearSetResult
    {
        public bool WasActive;
        public int ClearedSetId;
        public List<ClearedSlotInfo> ClearedSlots;

        public static ClearSetResult NoPreviewActive => new() { WasActive = false };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Expected: PASS

- [ ] **Step 5: Write remaining tests**

Add to `SetPreviewStateTrackerTests.cs`:

```csharp
    [Test]
    public void StartSetPreview_InvalidSetId_Throws()
    {
        var tracker = new SetPreviewStateTracker();
        var slots = new List<SetPreviewStateTracker.SetPreviewSlotInput>
        {
            new() { slot = AccessoryType.Headwear, itemId = 1, currentEquippedItemId = -1 },
        };

        Assert.Throws<System.ArgumentException>(() => tracker.StartSetPreview(0, slots));
        Assert.Throws<System.ArgumentException>(() => tracker.StartSetPreview(-1, slots));
    }

    [Test]
    public void StartSetPreview_EmptySlots_Throws()
    {
        var tracker = new SetPreviewStateTracker();

        Assert.Throws<System.ArgumentException>(
            () => tracker.StartSetPreview(1, new List<SetPreviewStateTracker.SetPreviewSlotInput>()));
        Assert.Throws<System.ArgumentException>(
            () => tracker.StartSetPreview(1, null));
    }

    [Test]
    public void ClearSetPreview_WhenActive_ReturnsAllSlotsToRestore()
    {
        var tracker = new SetPreviewStateTracker();
        var slots = new List<SetPreviewStateTracker.SetPreviewSlotInput>
        {
            new() { slot = AccessoryType.Headwear, itemId = 1, currentEquippedItemId = 3 },
            new() { slot = AccessoryType.MainHand, itemId = 2, currentEquippedItemId = -1 },
        };
        tracker.StartSetPreview(10, slots);

        var result = tracker.ClearSetPreview();

        Assert.IsTrue(result.WasActive);
        Assert.AreEqual(10, result.ClearedSetId);
        Assert.AreEqual(2, result.ClearedSlots.Count);
        Assert.IsFalse(tracker.IsSetPreviewActive);
        Assert.AreEqual(-1, tracker.PreviewedSetId);
    }

    [Test]
    public void ClearSetPreview_WhenInactive_ReturnsNotActive()
    {
        var tracker = new SetPreviewStateTracker();

        var result = tracker.ClearSetPreview();

        Assert.IsFalse(result.WasActive);
    }

    [Test]
    public void ClearSetPreview_RestoresCorrectPreviousItemIds()
    {
        var tracker = new SetPreviewStateTracker();
        var slots = new List<SetPreviewStateTracker.SetPreviewSlotInput>
        {
            new() { slot = AccessoryType.Headwear, itemId = 1, currentEquippedItemId = 50 },
            new() { slot = AccessoryType.Back, itemId = 2, currentEquippedItemId = -1 },
        };
        tracker.StartSetPreview(10, slots);

        var result = tracker.ClearSetPreview();

        var headwearSlot = result.ClearedSlots.Find(s => s.clearedSlot == AccessoryType.Headwear);
        var backSlot = result.ClearedSlots.Find(s => s.clearedSlot == AccessoryType.Back);
        Assert.AreEqual(50, headwearSlot.restoreItemId);
        Assert.AreEqual(-1, backSlot.restoreItemId);
    }

    [Test]
    public void IsSetPreviewActive_ReflectsState()
    {
        var tracker = new SetPreviewStateTracker();
        Assert.IsFalse(tracker.IsSetPreviewActive);

        var slots = new List<SetPreviewStateTracker.SetPreviewSlotInput>
        {
            new() { slot = AccessoryType.Headwear, itemId = 1, currentEquippedItemId = -1 },
        };
        tracker.StartSetPreview(5, slots);
        Assert.IsTrue(tracker.IsSetPreviewActive);

        tracker.ClearSetPreview();
        Assert.IsFalse(tracker.IsSetPreviewActive);
    }

    [Test]
    public void SetPreviewChanged_FiresOnStartAndClear()
    {
        var tracker = new SetPreviewStateTracker();
        int fireCount = 0;
        tracker.SetPreviewChanged += () => fireCount++;

        var slots = new List<SetPreviewStateTracker.SetPreviewSlotInput>
        {
            new() { slot = AccessoryType.Headwear, itemId = 1, currentEquippedItemId = -1 },
        };
        tracker.StartSetPreview(5, slots);
        Assert.AreEqual(1, fireCount);

        tracker.ClearSetPreview();
        Assert.AreEqual(2, fireCount);
    }

    [Test]
    public void SetPreviewChanged_DoesNotFireOnInactiveClear()
    {
        var tracker = new SetPreviewStateTracker();
        int fireCount = 0;
        tracker.SetPreviewChanged += () => fireCount++;

        tracker.ClearSetPreview();
        Assert.AreEqual(0, fireCount);
    }

    [Test]
    public void ClearSetPreview_SlotsWithNoPrevious_RestoreIdIsNegative()
    {
        var tracker = new SetPreviewStateTracker();
        var slots = new List<SetPreviewStateTracker.SetPreviewSlotInput>
        {
            new() { slot = AccessoryType.Facewear, itemId = 7, currentEquippedItemId = -1 },
        };
        tracker.StartSetPreview(3, slots);

        var result = tracker.ClearSetPreview();

        Assert.AreEqual(-1, result.ClearedSlots[0].restoreItemId);
    }
```

- [ ] **Step 6: Run all tests to verify they pass**

- [ ] **Step 7: Refresh Unity and check for compilation errors**

Run: `refresh_unity` then `read_console`
Expected: Zero compilation errors

---

### Task 2: ShopAPI.PurchaseSet Wiring + AccessoryRegistry.MarkSetOwned Fix + DTOs

**Files:**
- Modify: `Assets/Scripts/Data/ShopApiDTOs.cs` (add `PurchaseSetRequest`, `PurchaseSetResponse`)
- Modify: `Assets/Scripts/API/ShopAPI.cs:137-145` (replace `PurchaseSet` stub)
- Modify: `Assets/Scripts/Registry/AccessoryRegistry.cs:184-187` (fix `MarkSetOwned`)

**Interfaces:**
- Consumes: `AccessoryRegistry.GetAccessoryIdsInSet(int setId)`, `AccessoryRegistry.MarkAccessoryOwned(int)`, `ShopCatalog.MarkSetOwned(int)`, `ApiConfig.BASE_URL`, `SendWithTokenRefresh` extension
- Produces:
  - `ShopAPI.PurchaseSet(int setId, Action<bool, string, List<int>> onSuccess, Action<string> onError)` — real endpoint wiring
  - `AccessoryRegistry.MarkSetOwned(int setId)` — now also marks constituent accessories owned and fires `AccessoryOwnershipChanged`

- [ ] **Step 1: Add DTOs to ShopApiDTOs.cs**

Append to end of `Assets/Scripts/Data/ShopApiDTOs.cs`:

```csharp
[System.Serializable]
public class PurchaseSetRequest
{
    public int set_id;
}

[System.Serializable]
public class PurchaseSetResponse
{
    public List<int> accessory_ids;
}
```

- [ ] **Step 2: Add PURCHASE_SET_API_URL constant to ShopAPI**

Add after the existing `CONVERT_CURRENCY_API_URL` constant (line 17):

```csharp
    private const string PURCHASE_SET_API_URL = ApiConfig.BASE_URL + "/api/shop/purchase-set/";
```

- [ ] **Step 3: Replace PurchaseSet stub with real implementation**

Replace the `PurchaseSet` method (lines 140-145) in `Assets/Scripts/API/ShopAPI.cs` with:

```csharp
    public IEnumerator PurchaseSet(int setId, Action<bool, string, List<int>> onSuccess, Action<string> onError)
    {
        var requestData = new PurchaseSetRequest { set_id = setId };
        string jsonData = JsonConvert.SerializeObject(requestData);

        UnityWebRequest request = new UnityWebRequest(PURCHASE_SET_API_URL, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();

        var response = new ApiResponse();
        yield return request.SendWithTokenRefresh(response);

        if (!response.IsSuccess)
        {
            if (response.ResponseCode == 400)
            {
                var errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(response.Text);
                onSuccess?.Invoke(false, errorResponse?.message ?? "Purchase failed", null);
            }
            else
            {
                onError?.Invoke($"Request failed: {response.Error}");
            }
        }
        else
        {
            try
            {
                var parsed = JsonConvert.DeserializeObject<PurchaseSetResponse>(response.Text);
                onSuccess?.Invoke(true, "Set purchased successfully", parsed?.accessory_ids);
            }
            catch (Exception e)
            {
                onError?.Invoke($"Error parsing purchase-set response: {e.Message}");
            }
        }
    }
```

- [ ] **Step 4: Fix AccessoryRegistry.MarkSetOwned**

Replace `MarkSetOwned` (lines 184-187) in `Assets/Scripts/Registry/AccessoryRegistry.cs` with:

```csharp
    public void MarkSetOwned(int setId)
    {
        if (!shopCatalogLoaded) return;

        activeShopCatalog.MarkSetOwned(setId);

        var accessoryIds = GetAccessoryIdsInSet(setId);
        foreach (int accessoryId in accessoryIds)
        {
            activeShopCatalog.MarkOwned(accessoryId);
        }

        try { AccessoryOwnershipChanged?.Invoke(); }
        catch (Exception e) { Debug.LogException(e); }
    }
```

- [ ] **Step 5: Refresh Unity and check for compilation errors**

Run: `refresh_unity` then `read_console`
Expected: Zero compilation errors

- [ ] **Step 6: Run all existing tests to verify no regressions**

Run: Unity Test Framework EditMode tests
Expected: All existing tests pass

---

### Task 3: AccessoryEquipService — Set Preview, Clear, and Equip Methods

**Files:**
- Modify: `Assets/Scripts/Example/AccessoryEquipService.cs`

**Interfaces:**
- Consumes:
  - `SetPreviewStateTracker` (from Task 1) — all public methods and types
  - `AccessoryRegistry.GetAccessoryIdsInSet(int setId) → List<int>`
  - `AccessoryRegistry.GetAccessoryPrefabById(int) → GameObject`
  - `AccessoryRegistry.GetAccessoryTypeById(int) → AccessoryType`
  - `PlayerAccessory.UpdateEquippedAccessories(List<int>)`
  - `PlayerAccessory.StoreEquippedAccessories()`
  - `PlayerAccessory.GetEquippedItemId(AccessoryType) → int`
  - `IAccessoryVisualSync.Equip(int, AccessoryType, GameObject, Transform)`
  - `IAccessoryVisualSync.Unequip(AccessoryType)`
- Produces:
  - `AccessoryEquipService.PreviewSet(int setId)`
  - `AccessoryEquipService.ClearSetPreview()`
  - `AccessoryEquipService.EquipSet(int setId)`
  - `AccessoryEquipService.IsSetPreviewActive` (bool)
  - `AccessoryEquipService.PreviewedSetId` (int)
  - `event Action SetPreviewChanged`

- [ ] **Step 1: Add SetPreviewStateTracker field and properties**

In `Assets/Scripts/Example/AccessoryEquipService.cs`, add after the existing `previewState` field (line 8):

```csharp
    private readonly SetPreviewStateTracker setPreviewState = new SetPreviewStateTracker();
```

Add after the existing `PreviewChanged` event (after line 32):

```csharp
    public bool IsSetPreviewActive => setPreviewState.IsSetPreviewActive;
    public int PreviewedSetId => setPreviewState.PreviewedSetId;

    public event Action SetPreviewChanged
    {
        add => setPreviewState.SetPreviewChanged += value;
        remove => setPreviewState.SetPreviewChanged -= value;
    }
```

- [ ] **Step 2: Add PreviewSet method**

Add after the existing `ClearPreview` method:

```csharp
    public void PreviewSet(int setId)
    {
        var registry = GetRegistry();
        var accessoryIds = registry.GetAccessoryIdsInSet(setId);
        if (accessoryIds.Count == 0) return;

        var playerAccessory = GetPlayerAccessory();
        var avatarRoot = GetAvatarRoot();
        var visualSync = playerAccessory.GetVisualSyncPublic();

        if (previewState.IsPreviewActive)
            RestorePreviewedSlot(visualSync, avatarRoot);

        if (setPreviewState.IsSetPreviewActive)
            RestoreSetPreviewSlots(visualSync, avatarRoot);

        var slotInputs = new List<SetPreviewStateTracker.SetPreviewSlotInput>();
        foreach (int accessoryId in accessoryIds)
        {
            AccessoryType type = registry.GetAccessoryTypeById(accessoryId);
            if (type == AccessoryType.None) continue;

            GameObject prefab = registry.GetAccessoryPrefabById(accessoryId);
            if (prefab == null) continue;

            int currentEquippedItemId = playerAccessory.GetEquippedItemId(type);
            slotInputs.Add(new SetPreviewStateTracker.SetPreviewSlotInput
            {
                slot = type,
                itemId = accessoryId,
                currentEquippedItemId = currentEquippedItemId,
            });

            SetBaseMeshActive(avatarRoot, type, false);
            visualSync.Equip(accessoryId, type, prefab, avatarRoot);
        }

        if (slotInputs.Count > 0)
            setPreviewState.StartSetPreview(setId, slotInputs);
    }
```

- [ ] **Step 3: Add ClearSetPreview method and helper**

Add after `PreviewSet`:

```csharp
    public void ClearSetPreview()
    {
        var clearResult = setPreviewState.ClearSetPreview();
        if (!clearResult.WasActive)
            return;

        if (!TryGetPlayerAccessory(out var playerAccessory, out var avatarRoot))
            return;

        var visualSync = playerAccessory.GetVisualSyncPublic();
        RestoreSetPreviewSlots(clearResult, visualSync, avatarRoot);
    }

    private void RestoreSetPreviewSlots(IAccessoryVisualSync visualSync, Transform avatarRoot)
    {
        var clearResult = setPreviewState.ClearSetPreview();
        if (!clearResult.WasActive) return;
        RestoreSetPreviewSlots(clearResult, visualSync, avatarRoot);
    }

    private void RestoreSetPreviewSlots(
        SetPreviewStateTracker.ClearSetResult clearResult,
        IAccessoryVisualSync visualSync,
        Transform avatarRoot)
    {
        foreach (var slotInfo in clearResult.ClearedSlots)
        {
            visualSync.Unequip(slotInfo.clearedSlot);

            if (slotInfo.restoreItemId > 0)
            {
                var registry = GetRegistry();
                GameObject restorePrefab = registry.GetAccessoryPrefabById(slotInfo.restoreItemId);
                if (restorePrefab != null)
                    visualSync.Equip(slotInfo.restoreItemId, slotInfo.clearedSlot, restorePrefab, avatarRoot);
            }
            else
            {
                SetBaseMeshActive(avatarRoot, slotInfo.clearedSlot, true);
            }
        }
    }
```

- [ ] **Step 4: Add EquipSet method**

Add after `ClearSetPreview`:

```csharp
    public void EquipSet(int setId)
    {
        var registry = GetRegistry();
        var accessoryIds = registry.GetAccessoryIdsInSet(setId);
        if (accessoryIds.Count == 0) return;

        var playerAccessory = GetPlayerAccessory();
        var avatarRoot = GetAvatarRoot();

        foreach (int accessoryId in accessoryIds)
        {
            AccessoryType type = registry.GetAccessoryTypeById(accessoryId);
            if (type != AccessoryType.None)
                SetBaseMeshActive(avatarRoot, type, false);
        }

        playerAccessory.UpdateEquippedAccessories(accessoryIds);
        playerAccessory.StoreEquippedAccessories();
    }
```

- [ ] **Step 5: Add cross-preview coordination to existing PreviewItem**

In the existing `PreviewItem` method, add at the top (after the `IsItemPreviewed` check):

```csharp
        if (setPreviewState.IsSetPreviewActive)
        {
            var setClearResult = setPreviewState.ClearSetPreview();
            if (setClearResult.WasActive)
            {
                RestoreSetPreviewSlots(setClearResult, visualSync, avatarRoot);
            }
        }
```

Note: `visualSync` and `avatarRoot` are already resolved earlier in `PreviewItem`. Insert this block after the existing `RestorePreviewedSlot` call (line ~84) but before the `currentEquippedItemId` lookup.

- [ ] **Step 6: Refresh Unity and check for compilation errors**

Run: `refresh_unity` then `read_console`
Expected: Zero compilation errors

- [ ] **Step 7: Run all existing tests to verify no regressions**

Run: Unity Test Framework EditMode tests
Expected: All existing tests pass

---

### Task 4: SetsPanel — UI for Browsing, Previewing, Purchasing, and Equipping Sets

**Files:**
- Create: `Assets/Scripts/Example/UI/SetsPanel.cs`

**Interfaces:**
- Consumes:
  - `AccessoryRegistry.Instance.GetAllSets() → AccessorySetData[]`
  - `AccessoryRegistry.Instance.GetShopItemById(int) → ShopAccessoryItem`
  - `AccessoryRegistry.Instance.IsShopCatalogLoaded() → bool`
  - `AccessoryRegistry.Instance.ShopCatalogLoaded` (event)
  - `AccessoryRegistry.Instance.AccessoryOwnershipChanged` (event)
  - `AccessoryRegistry.Instance.MarkSetOwned(int)` (from Task 2)
  - `AccessoryEquipService.Instance.PreviewSet(int)` (from Task 3)
  - `AccessoryEquipService.Instance.ClearSetPreview()` (from Task 3)
  - `AccessoryEquipService.Instance.EquipSet(int)` (from Task 3)
  - `AccessoryEquipService.Instance.IsSetPreviewActive` (from Task 3)
  - `AccessoryEquipService.Instance.PreviewedSetId` (from Task 3)
  - `AccessoryEquipService.Instance.SetPreviewChanged` (from Task 3)
  - `ShopAPI.Instance.PurchaseSet(int, Action, Action)` (from Task 2)
  - `PlayerAccessory.EquippedStateChanged` (event)
  - `PlayerAccessory.GetEquippedItemId(AccessoryType) → int`
  - `WalletDisplay.Instance.Refresh() → IEnumerator`
  - `PurchaseConfirmationPrompt.Show(Transform, string, Action, Action)`
  - `ClientPlayerAvatars.Instance.currentClientAvatar`
  - `TabManager.Instance.ShowTab(int)`
  - `AccessorySetData.setId`, `.setName`, `.costs`, `.isOwned`, `.accessoryIDs`
  - `AccessoryCost.currency`, `.amount`
- Produces: `SetsPanel` MonoBehaviour — attach to existing scene `SetsPanel` GameObject

- [ ] **Step 1: Create SetsPanel.cs with full implementation**

Create `Assets/Scripts/Example/UI/SetsPanel.cs`:

```csharp
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class SetsPanel : MonoBehaviour
{
    private const int INVENTORY_TAB_INDEX = 1;

    [SerializeField] private Transform contentContainer;
    [SerializeField] private TMP_Text statusText;

    private List<SetRowUI> setRows = new();
    private bool isPopulated = false;
    private PlayerAccessory playerAccessory;
    private bool isPurchasing = false;
    private PurchaseConfirmationPrompt activePrompt;

    private void OnEnable()
    {
        if (!isPopulated)
            StartCoroutine(WaitAndPopulate());
        else
        {
            ResolvePlayerAccessory();
            SubscribeEvents();
        }

        RefreshAllButtons();
        SetStatus("");
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        UnsubscribeEvents();

        if (activePrompt != null)
        {
            Destroy(activePrompt.gameObject);
            activePrompt = null;
        }

        if (AccessoryEquipService.Instance != null)
            AccessoryEquipService.Instance.ClearSetPreview();
    }

    private void ResolvePlayerAccessory()
    {
        var avatar = ClientPlayerAvatars.Instance?.currentClientAvatar;
        if (avatar == null) return;

        var newPlayerAccessory = avatar.GetComponent<PlayerAccessory>();
        if (newPlayerAccessory != playerAccessory)
        {
            UnsubscribeEvents();
            playerAccessory = newPlayerAccessory;
        }
    }

    private void SubscribeEvents()
    {
        if (playerAccessory != null)
            playerAccessory.EquippedStateChanged += RefreshAllButtons;

        if (AccessoryEquipService.Instance != null)
            AccessoryEquipService.Instance.SetPreviewChanged += RefreshAllButtons;

        if (AccessoryRegistry.Instance != null)
        {
            AccessoryRegistry.Instance.ShopCatalogLoaded += RefreshSets;
            AccessoryRegistry.Instance.AccessoryOwnershipChanged += RefreshSets;
        }
    }

    private void UnsubscribeEvents()
    {
        if (playerAccessory != null)
            playerAccessory.EquippedStateChanged -= RefreshAllButtons;

        if (AccessoryEquipService.Instance != null)
            AccessoryEquipService.Instance.SetPreviewChanged -= RefreshAllButtons;

        if (AccessoryRegistry.Instance != null)
        {
            AccessoryRegistry.Instance.ShopCatalogLoaded -= RefreshSets;
            AccessoryRegistry.Instance.AccessoryOwnershipChanged -= RefreshSets;
        }
    }

    private IEnumerator WaitAndPopulate()
    {
        while (AccessoryRegistry.Instance == null || !AccessoryRegistry.Instance.IsShopCatalogLoaded())
            yield return null;

        while (ClientPlayerAvatars.Instance == null || ClientPlayerAvatars.Instance.currentClientAvatar == null)
            yield return null;

        if (!gameObject.activeInHierarchy)
            yield break;

        ResolvePlayerAccessory();
        SubscribeEvents();
        PopulateSets();
        isPopulated = true;

        yield return null;
        if (contentContainer != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentContainer.GetComponent<RectTransform>());
        Canvas.ForceUpdateCanvases();
        yield return null;
        var scrollRect = GetComponent<ScrollRect>();
        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 1f;
    }

    private void OnDestroy()
    {
        UnsubscribeEvents();

        foreach (var row in setRows)
        {
            if (row.actionButton != null)
                row.actionButton.onClick.RemoveAllListeners();
        }
    }

    private void RefreshSets()
    {
        if (AccessoryRegistry.Instance == null || !AccessoryRegistry.Instance.IsShopCatalogLoaded())
            return;

        ClearRows();
        PopulateSets();
    }

    private void ClearRows()
    {
        foreach (var row in setRows)
        {
            if (row.actionButton != null)
                row.actionButton.onClick.RemoveAllListeners();
        }
        setRows.Clear();

        if (contentContainer != null)
        {
            for (int index = contentContainer.childCount - 1; index >= 0; index--)
                Destroy(contentContainer.GetChild(index).gameObject);
        }
    }

    private void PopulateSets()
    {
        var allSets = AccessoryRegistry.Instance.GetAllSets();

        if (allSets.Length == 0)
        {
            CreateEmptyStateMessage("No sets available.");
            return;
        }

        foreach (var setData in allSets)
        {
            CreateSetRow(setData);
        }
    }

    private GameObject CreateUIObject(string name, Transform parent, bool stretch = false)
    {
        var uiObject = new GameObject(name);
        var rectTransform = uiObject.AddComponent<RectTransform>();
        uiObject.transform.SetParent(parent, false);
        if (stretch)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;
        }
        return uiObject;
    }

    private void CreateEmptyStateMessage(string message)
    {
        var containerObject = CreateUIObject("EmptyState", contentContainer, stretch: true);
        var containerLayout = containerObject.AddComponent<LayoutElement>();
        containerLayout.flexibleHeight = 1;
        containerLayout.minHeight = 200;

        var textObject = CreateUIObject("Text", containerObject.transform, stretch: true);
        var emptyStateText = textObject.AddComponent<TextMeshProUGUI>();
        emptyStateText.text = message;
        emptyStateText.fontSize = 16;
        emptyStateText.color = new Color(0.8f, 0.5f, 0.3f);
        emptyStateText.alignment = TextAlignmentOptions.Center;
    }

    private void CreateSetRow(AccessorySetData setData)
    {
        var rowObject = CreateUIObject($"Set_{setData.setId}", contentContainer, stretch: true);
        var rowLayout = rowObject.AddComponent<VerticalLayoutGroup>();
        rowLayout.spacing = 2;
        rowLayout.padding = new RectOffset(10, 10, 6, 6);
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;

        var rowLayoutElement = rowObject.AddComponent<LayoutElement>();
        rowLayoutElement.minHeight = 60;

        var background = rowObject.AddComponent<Image>();
        background.color = new Color(0.14f, 0.14f, 0.18f, 0.9f);

        var headerRow = CreateUIObject("Header", rowObject.transform);
        var headerLayout = headerRow.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 6;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = true;
        var headerLayoutElement = headerRow.AddComponent<LayoutElement>();
        headerLayoutElement.minHeight = 36;
        headerLayoutElement.preferredHeight = 36;

        var nameObject = CreateUIObject("Name", headerRow.transform);
        var nameLayout = nameObject.AddComponent<LayoutElement>();
        nameLayout.flexibleWidth = 1;
        var nameText = nameObject.AddComponent<TextMeshProUGUI>();
        nameText.text = setData.setName;
        nameText.fontSize = 15;
        nameText.fontStyle = FontStyles.Bold;
        nameText.color = new Color(0.9f, 0.85f, 0.7f);
        nameText.alignment = TextAlignmentOptions.MidlineLeft;

        var costObject = CreateUIObject("Cost", headerRow.transform);
        var costLayout = costObject.AddComponent<LayoutElement>();
        costLayout.preferredWidth = 140;
        costLayout.flexibleWidth = 0;
        var costText = costObject.AddComponent<TextMeshProUGUI>();
        costText.fontSize = 12;
        costText.color = new Color(1f, 0.85f, 0.3f);
        costText.alignment = TextAlignmentOptions.MidlineRight;

        var buttonObject = CreateUIObject("Button", headerRow.transform);
        var buttonLayout = buttonObject.AddComponent<LayoutElement>();
        buttonLayout.preferredWidth = 90;
        buttonLayout.minWidth = 90;
        buttonLayout.flexibleWidth = 0;
        var buttonImage = buttonObject.AddComponent<Image>();
        var actionButton = buttonObject.AddComponent<Button>();

        var buttonTextObject = CreateUIObject("Text", buttonObject.transform);
        var buttonTextRect = buttonTextObject.GetComponent<RectTransform>();
        buttonTextRect.anchorMin = Vector2.zero;
        buttonTextRect.anchorMax = Vector2.one;
        buttonTextRect.sizeDelta = Vector2.zero;
        var buttonText = buttonTextObject.AddComponent<TextMeshProUGUI>();
        buttonText.fontSize = 13;
        buttonText.alignment = TextAlignmentOptions.Center;
        buttonText.color = Color.white;

        foreach (int accessoryId in setData.accessoryIDs)
        {
            var shopItem = AccessoryRegistry.Instance.GetShopItemById(accessoryId);
            string itemName = shopItem != null && !string.IsNullOrEmpty(shopItem.displayName)
                ? shopItem.displayName
                : $"Item {accessoryId}";

            var itemObject = CreateUIObject($"SetItem_{accessoryId}", rowObject.transform);
            var itemLayout = itemObject.AddComponent<LayoutElement>();
            itemLayout.minHeight = 20;
            itemLayout.preferredHeight = 20;

            var itemText = itemObject.AddComponent<TextMeshProUGUI>();
            itemText.text = $"  • {itemName}";
            itemText.fontSize = 12;
            itemText.color = new Color(0.7f, 0.7f, 0.75f);
            itemText.alignment = TextAlignmentOptions.MidlineLeft;
        }

        var row = new SetRowUI
        {
            setData = setData,
            actionButton = actionButton,
            buttonImage = buttonImage,
            buttonText = buttonText,
            costText = costText,
        };
        setRows.Add(row);

        UpdateSetRowButton(row);
    }

    private string FormatCosts(List<AccessoryCost> costs)
    {
        if (costs == null || costs.Count == 0) return "Free";
        var parts = new List<string>();
        foreach (var accessoryCost in costs)
            parts.Add($"{accessoryCost.amount} {accessoryCost.currency}");
        return string.Join(" + ", parts);
    }

    private bool IsSetFullyEquipped(AccessorySetData setData)
    {
        if (playerAccessory == null) return false;

        var registry = AccessoryRegistry.Instance;
        if (registry == null) return false;

        foreach (int accessoryId in setData.accessoryIDs)
        {
            AccessoryType type = registry.GetAccessoryTypeById(accessoryId);
            if (type == AccessoryType.None) continue;
            if (playerAccessory.GetEquippedItemId(type) != accessoryId)
                return false;
        }
        return true;
    }

    private void OnSetActionClicked(int setId)
    {
        var registry = AccessoryRegistry.Instance
            ?? throw new System.InvalidOperationException(
                "SetsPanel.OnSetActionClicked: AccessoryRegistry not initialized");
        var setData = registry.GetSetById(setId);
        if (setData == null) return;

        var equipService = AccessoryEquipService.Instance
            ?? throw new System.InvalidOperationException(
                "SetsPanel: AccessoryEquipService not initialized");

        if (setData.isOwned)
        {
            equipService.EquipSet(setId);
            return;
        }

        if (equipService.IsSetPreviewActive && equipService.PreviewedSetId == setId)
        {
            OnBuySetClicked(setId);
            return;
        }

        equipService.PreviewSet(setId);
    }

    private void OnBuySetClicked(int setId)
    {
        if (isPurchasing) return;
        StartCoroutine(PurchaseSet(setId));
    }

    private IEnumerator PurchaseSet(int setId)
    {
        isPurchasing = true;

        if (AccessoryEquipService.Instance != null)
            AccessoryEquipService.Instance.ClearSetPreview();

        SetStatus("Purchasing...");
        RefreshAllButtons();
        bool isPurchaseComplete = false;

        yield return ShopAPI.Instance.PurchaseSet(
            setId,
            (bool success, string message, List<int> grantedAccessoryIds) =>
            {
                if (success)
                {
                    (AccessoryRegistry.Instance
                        ?? throw new System.InvalidOperationException(
                            "SetsPanel.PurchaseSet: AccessoryRegistry not initialized"))
                        .MarkSetOwned(setId);

                    var walletDisplay = WalletDisplay.Instance
                        ?? throw new System.InvalidOperationException(
                            "SetsPanel.PurchaseSet: WalletDisplay not initialized");
                    StartCoroutine(walletDisplay.Refresh());
                    SetStatus("");
                    ShowPurchaseConfirmation(setId);
                }
                else
                {
                    SetStatus(message);
                    StartCoroutine(ClearStatusAfterDelay(3f));
                }
                isPurchaseComplete = true;
            },
            (string error) =>
            {
                SetStatus($"Error: {error}");
                StartCoroutine(ClearStatusAfterDelay(3f));
                isPurchaseComplete = true;
            }
        );
        while (!isPurchaseComplete) yield return null;

        isPurchasing = false;
        RefreshAllButtons();
    }

    private void ShowPurchaseConfirmation(int setId)
    {
        var registry = AccessoryRegistry.Instance
            ?? throw new System.InvalidOperationException(
                "SetsPanel.ShowPurchaseConfirmation: AccessoryRegistry not initialized");
        var setData = registry.GetSetById(setId);
        string setName = setData != null ? setData.setName : $"Set {setId}";

        var canvasTransform = GetComponentInParent<Canvas>()?.transform
            ?? throw new System.InvalidOperationException(
                "SetsPanel: no parent Canvas found for confirmation prompt");

        activePrompt = PurchaseConfirmationPrompt.Show(
            canvasTransform,
            setName,
            onConfirm: () =>
            {
                activePrompt = null;
                var equipService = AccessoryEquipService.Instance
                    ?? throw new System.InvalidOperationException(
                        "SetsPanel: AccessoryEquipService not initialized");
                equipService.EquipSet(setId);

                if (TabManager.Instance != null)
                    TabManager.Instance.ShowTab(INVENTORY_TAB_INDEX);
            },
            onCancel: () =>
            {
                activePrompt = null;
            }
        );
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    private IEnumerator ClearStatusAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SetStatus("");
    }

    private void RefreshAllButtons()
    {
        foreach (var row in setRows)
            UpdateSetRowButton(row);
    }

    private void UpdateSetRowButton(SetRowUI row)
    {
        row.actionButton.onClick.RemoveAllListeners();

        bool isOwned = row.setData.isOwned;
        bool isPreviewing = AccessoryEquipService.Instance != null
            && AccessoryEquipService.Instance.IsSetPreviewActive
            && AccessoryEquipService.Instance.PreviewedSetId == row.setData.setId;
        bool isFullyEquipped = isOwned && IsSetFullyEquipped(row.setData);

        int capturedSetId = row.setData.setId;

        if (isFullyEquipped)
        {
            row.buttonText.text = "Equipped";
            row.buttonImage.color = new Color(0.3f, 0.3f, 0.35f);
            row.actionButton.interactable = false;
            row.costText.text = "";
        }
        else if (isOwned)
        {
            row.buttonText.text = "Equip";
            row.buttonImage.color = new Color(0.2f, 0.6f, 0.3f);
            row.actionButton.interactable = true;
            row.costText.text = "";
            row.actionButton.onClick.AddListener(() => OnSetActionClicked(capturedSetId));
        }
        else if (isPurchasing)
        {
            row.buttonText.text = "...";
            row.buttonImage.color = new Color(0.3f, 0.3f, 0.35f);
            row.actionButton.interactable = false;
            row.costText.text = FormatCosts(row.setData.costs);
        }
        else if (isPreviewing)
        {
            row.buttonText.text = "Buy";
            row.buttonImage.color = new Color(0.6f, 0.3f, 0.1f);
            row.actionButton.interactable = true;
            row.costText.text = FormatCosts(row.setData.costs);
            row.actionButton.onClick.AddListener(() => OnSetActionClicked(capturedSetId));
        }
        else
        {
            row.buttonText.text = "Preview";
            row.buttonImage.color = new Color(0.2f, 0.4f, 0.6f);
            row.actionButton.interactable = true;
            row.costText.text = FormatCosts(row.setData.costs);
            row.actionButton.onClick.AddListener(() => OnSetActionClicked(capturedSetId));
        }
    }

    private class SetRowUI
    {
        public AccessorySetData setData;
        public Button actionButton;
        public Image buttonImage;
        public TMP_Text buttonText;
        public TMP_Text costText;
    }
}
```

- [ ] **Step 2: Refresh Unity and check for compilation errors**

Run: `refresh_unity` then `read_console`
Expected: Zero compilation errors

- [ ] **Step 3: Run all tests to verify no regressions**

Run: Unity Test Framework EditMode tests
Expected: All existing tests plus Task 1 tests pass
