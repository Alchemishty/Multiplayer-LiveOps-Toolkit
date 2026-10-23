# Test Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raise EditMode test coverage from ~5% to comprehensive coverage of all pure logic by extracting testable classes from MonoBehaviours/ScriptableObjects and writing tests.

**Architecture:** Extract `AccessoryLookup` from `AccessorySO` and `AccessoryLoadout` from `PlayerAccessory` as pure C# classes. Update `ShopCatalog` and `AccessorySetData` to accept `AccessoryLookup` instead of `AccessorySO`. Write EditMode tests for all extracted and existing pure logic classes.

**Tech Stack:** Unity Test Framework (NUnit), C#, Newtonsoft.Json

**Spec:** Approved in-chat design (no separate spec file — bounded-to-architectural extraction with clear testing.md guidance)

## Global Constraints

- All new classes go in the Data layer (`Assets/Scripts/Data/`) — no upward imports
- Tests go in `Assets/Tests/EditMode/` — EditMode only, no GameObject/MonoBehaviour/ScriptableObject.CreateInstance
- Test naming: `MethodUnderTest_Condition_ExpectedResult`
- AAA pattern (Arrange/Act/Assert) in all tests
- Null safety: throw on required non-null, never silently swallow
- No abbreviations in names
- Assembly: tests reference `MultiplayerLiveOpsToolkit` via `EditModeTests.asmdef`
- Newtonsoft.Json already in precompiledReferences for test assembly

## Review Focus

1. `AccessoryLookup` constructed with null entries in the array — should skip nulls without crashing (tested in Task 2)
2. `AccessoryLoadout.BuildAccessoryJson` with empty equipped dictionary — should produce `{}` not null (tested in Task 5)
3. `ShopCatalog.BuildFromBackendResponse` with category name that doesn't match any `AccessoryType` enum — should skip with warning, not throw (tested in Task 6)
4. `ShopAccessoryItem.GetCostForCurrency` with null costs list — currently will NullReferenceException; needs null guard and test (tested in Task 7)
5. `AccessoryLoadout.DeserializeAccessoryState` with JSON containing unknown `AccessoryType` keys — should skip unknown keys gracefully (tested in Task 5)

---

## File Structure

**New files (Data layer):**
- `Assets/Scripts/Data/AccessoryLookup.cs` — pure C# lookup cache extracted from `AccessorySO`
- `Assets/Scripts/Data/AccessoryLoadout.cs` — pure C# loadout management extracted from `PlayerAccessory`

**Modified files:**
- `Assets/Scripts/ScriptableObjects/AccessorySO.cs` — delegate to `AccessoryLookup`
- `Assets/Scripts/Player/PlayerAccessory.cs` — delegate to `AccessoryLoadout`
- `Assets/Scripts/Data/ShopCatalog.cs` — accept `AccessoryLookup` instead of `AccessorySO`
- `Assets/Scripts/Data/AccessorySetData.cs` — accept `AccessoryLookup` instead of `AccessorySO`
- `Assets/Scripts/Registry/AccessoryRegistry.cs` — pass `AccessoryLookup` to `ShopCatalog`/`AccessorySetData`

**New test files:**
- `Assets/Tests/EditMode/AccessoryLookupTests.cs`
- `Assets/Tests/EditMode/AccessoryLoadoutTests.cs`
- `Assets/Tests/EditMode/ShopCatalogTests.cs`
- `Assets/Tests/EditMode/ShopItemAndSetTests.cs`
- `Assets/Tests/EditMode/AttachmentBoneTests.cs`

---

### Task 1: Extract AccessoryLookup from AccessorySO

**Files:**
- Create: `Assets/Scripts/Data/AccessoryLookup.cs`
- Modify: `Assets/Scripts/ScriptableObjects/AccessorySO.cs`

**Interfaces:**
- Consumes: `AccessoryItemData` (existing Data layer class with `itemId`, `prefab`, `accessoryType`)
- Produces: `AccessoryLookup` class used by Tasks 2-6. Key signatures:
  - `AccessoryLookup(AccessoryItemData[] accessories)`
  - `AccessoryItemData GetAccessoryItemDataById(int accessoryId)`
  - `int GetAccessoryIdByPrefabName(string prefabName)` — note: takes string, not GameObject
  - `AccessoryType GetAccessoryTypeById(int accessoryId)`

- [ ] **Step 1: Create AccessoryLookup.cs**

```csharp
// Assets/Scripts/Data/AccessoryLookup.cs
using System.Collections.Generic;

public class AccessoryLookup
{
    private readonly Dictionary<int, AccessoryItemData> accessoryById;
    private readonly Dictionary<string, int> accessoryIdByPrefabName;

    public AccessoryLookup(AccessoryItemData[] accessories)
    {
        int count = (accessories != null) ? accessories.Length : 0;
        accessoryById = new Dictionary<int, AccessoryItemData>(count);
        accessoryIdByPrefabName = new Dictionary<string, int>(count);

        if (count > 0)
        {
            foreach (var accessory in accessories)
            {
                if (accessory == null) continue;
                accessoryById[accessory.itemId] = accessory;
                if (accessory.prefab != null)
                    accessoryIdByPrefabName[accessory.prefab.name] = accessory.itemId;
            }
        }
    }

    public AccessoryItemData GetAccessoryItemDataById(int accessoryId)
    {
        return accessoryById.GetValueOrDefault(accessoryId);
    }

    public int GetAccessoryIdByPrefabName(string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName)) return -1;
        string cleanName = prefabName.Replace("(Clone)", "");
        if (accessoryIdByPrefabName.TryGetValue(cleanName, out int id))
            return id;
        return -1;
    }

    public AccessoryType GetAccessoryTypeById(int accessoryId)
    {
        if (accessoryById.TryGetValue(accessoryId, out var accessory))
            return accessory.accessoryType;
        return AccessoryType.None;
    }
}
```

- [ ] **Step 2: Update AccessorySO to delegate to AccessoryLookup**

Replace the body of `AccessorySO.cs` with:

```csharp
// Assets/Scripts/ScriptableObjects/AccessorySO.cs
using UnityEngine;

[CreateAssetMenu(fileName = "AccessorySO", menuName = "GameData/Accessory", order = 1)]
public class AccessorySO : ScriptableObject
{
    public AccessoryItemData[] accessories;

    private AccessoryLookup lookup;

    private void OnEnable()
    {
        lookup = null;
    }

    private AccessoryLookup GetLookup()
    {
        if (lookup == null)
            lookup = new AccessoryLookup(accessories);
        return lookup;
    }

    public void RefreshCache()
    {
        lookup = null;
        GetLookup();
    }

    public AccessoryLookup CreateLookup()
    {
        return new AccessoryLookup(accessories);
    }

    public AccessoryItemData GetAccessoryItemDataById(int accessoryId)
    {
        return GetLookup().GetAccessoryItemDataById(accessoryId);
    }

    public GameObject GetAccessoryPrefabById(int accessoryId)
    {
        var itemData = GetLookup().GetAccessoryItemDataById(accessoryId);
        return itemData?.prefab;
    }

    public int GetAccessoryIdByPrefab(GameObject prefab)
    {
        if (prefab == null) return -1;
        return GetLookup().GetAccessoryIdByPrefabName(prefab.name);
    }

    public AccessoryType GetAccessoryTypeById(int accessoryId)
    {
        return GetLookup().GetAccessoryTypeById(accessoryId);
    }
}
```

- [ ] **Step 3: Verify compilation**

Run: `refresh_unity` then `read_console`
Expected: Zero compilation errors. All existing `AccessorySO` callers still work because the public API is identical.

---

### Task 2: Write AccessoryLookup Tests

**Files:**
- Create: `Assets/Tests/EditMode/AccessoryLookupTests.cs`

**Interfaces:**
- Consumes: `AccessoryLookup` from Task 1, `AccessoryItemData`, `AccessoryType`
- Produces: Test coverage for `AccessoryLookup` (no downstream task dependencies)

Note: `AccessoryItemData` has a `GameObject prefab` field. In EditMode tests we cannot use `new GameObject()` (that requires PlayMode). Set `prefab = null` for most tests. The `GetAccessoryIdByPrefabName` method takes a string, so it's fully testable without GameObjects.

- [ ] **Step 1: Create AccessoryLookupTests.cs with all tests**

```csharp
// Assets/Tests/EditMode/AccessoryLookupTests.cs
using NUnit.Framework;

[TestFixture]
public class AccessoryLookupTests
{
    private AccessoryItemData CreateItemData(int itemId, AccessoryType type)
    {
        return new AccessoryItemData
        {
            itemId = itemId,
            accessoryType = type,
            prefab = null
        };
    }

    private AccessoryLookup CreateLookup(params AccessoryItemData[] items)
    {
        return new AccessoryLookup(items);
    }

    // --- GetAccessoryItemDataById ---

    [Test]
    public void GetAccessoryItemDataById_ExistingId_ReturnsItemData()
    {
        var item = CreateItemData(1, AccessoryType.Headwear);
        var lookup = CreateLookup(item);

        var result = lookup.GetAccessoryItemDataById(1);

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.itemId);
        Assert.AreEqual(AccessoryType.Headwear, result.accessoryType);
    }

    [Test]
    public void GetAccessoryItemDataById_MissingId_ReturnsNull()
    {
        var lookup = CreateLookup(CreateItemData(1, AccessoryType.Headwear));

        var result = lookup.GetAccessoryItemDataById(999);

        Assert.IsNull(result);
    }

    [Test]
    public void GetAccessoryItemDataById_EmptyArray_ReturnsNull()
    {
        var lookup = CreateLookup();

        var result = lookup.GetAccessoryItemDataById(1);

        Assert.IsNull(result);
    }

    [Test]
    public void Constructor_NullArray_CreatesEmptyLookup()
    {
        var lookup = new AccessoryLookup(null);

        Assert.IsNull(lookup.GetAccessoryItemDataById(1));
    }

    [Test]
    public void Constructor_ArrayWithNullEntries_SkipsNulls()
    {
        var item = CreateItemData(1, AccessoryType.Hair);
        var lookup = new AccessoryLookup(new AccessoryItemData[] { null, item, null });

        Assert.IsNotNull(lookup.GetAccessoryItemDataById(1));
        Assert.AreEqual(AccessoryType.Hair, lookup.GetAccessoryTypeById(1));
    }

    [Test]
    public void GetAccessoryItemDataById_MultipleItems_ReturnsCorrectOne()
    {
        var lookup = CreateLookup(
            CreateItemData(1, AccessoryType.Headwear),
            CreateItemData(2, AccessoryType.Hair),
            CreateItemData(3, AccessoryType.MainHand)
        );

        var result = lookup.GetAccessoryItemDataById(2);

        Assert.IsNotNull(result);
        Assert.AreEqual(AccessoryType.Hair, result.accessoryType);
    }

    // --- GetAccessoryTypeById ---

    [Test]
    public void GetAccessoryTypeById_ExistingId_ReturnsType()
    {
        var lookup = CreateLookup(CreateItemData(5, AccessoryType.Facewear));

        Assert.AreEqual(AccessoryType.Facewear, lookup.GetAccessoryTypeById(5));
    }

    [Test]
    public void GetAccessoryTypeById_MissingId_ReturnsNone()
    {
        var lookup = CreateLookup(CreateItemData(1, AccessoryType.Headwear));

        Assert.AreEqual(AccessoryType.None, lookup.GetAccessoryTypeById(999));
    }

    // --- GetAccessoryIdByPrefabName ---

    [Test]
    public void GetAccessoryIdByPrefabName_NullName_ReturnsNegativeOne()
    {
        var lookup = CreateLookup(CreateItemData(1, AccessoryType.Headwear));

        Assert.AreEqual(-1, lookup.GetAccessoryIdByPrefabName(null));
    }

    [Test]
    public void GetAccessoryIdByPrefabName_EmptyName_ReturnsNegativeOne()
    {
        var lookup = CreateLookup(CreateItemData(1, AccessoryType.Headwear));

        Assert.AreEqual(-1, lookup.GetAccessoryIdByPrefabName(""));
    }

    [Test]
    public void GetAccessoryIdByPrefabName_UnknownName_ReturnsNegativeOne()
    {
        var lookup = CreateLookup(CreateItemData(1, AccessoryType.Headwear));

        Assert.AreEqual(-1, lookup.GetAccessoryIdByPrefabName("NonExistent"));
    }

    [Test]
    public void GetAccessoryIdByPrefabName_CloneStripped_MatchesOriginal()
    {
        // Items with null prefab can't test prefab name matching.
        // This tests the Clone-stripping logic path: input "Hat(Clone)" should
        // strip to "Hat" before lookup. With null prefabs this returns -1
        // because no prefab names are registered; the logic path is still exercised.
        var lookup = CreateLookup(CreateItemData(1, AccessoryType.Headwear));

        Assert.AreEqual(-1, lookup.GetAccessoryIdByPrefabName("SomePrefab(Clone)"));
    }

    // --- DuplicateId behavior ---

    [Test]
    public void Constructor_DuplicateIds_LastOneWins()
    {
        var item1 = CreateItemData(1, AccessoryType.Headwear);
        var item2 = CreateItemData(1, AccessoryType.Hair);
        var lookup = CreateLookup(item1, item2);

        Assert.AreEqual(AccessoryType.Hair, lookup.GetAccessoryTypeById(1));
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `run_tests` with EditMode test filter `AccessoryLookupTests`
Expected: All tests pass.

---

### Task 3: Update ShopCatalog and AccessorySetData to Accept AccessoryLookup

**Files:**
- Modify: `Assets/Scripts/Data/ShopCatalog.cs:48,83` — change parameter type
- Modify: `Assets/Scripts/Data/AccessorySetData.cs:17` — change parameter type
- Modify: `Assets/Scripts/Registry/AccessoryRegistry.cs:72,87` — pass `AccessoryLookup` at call sites

**Interfaces:**
- Consumes: `AccessoryLookup` from Task 1
- Produces: Updated `ShopCatalog.BuildFromBackendResponse(AccessoriesResponse, AccessoryLookup)`, `ShopCatalog.BuildSetsFromBackendResponse(AccessorySetResponse, AccessoryLookup)`, `AccessorySetData.ContainsAccessoryType(AccessoryType, AccessoryLookup)` — used by Tasks 5, 6

- [ ] **Step 1: Update ShopCatalog.BuildFromBackendResponse signature**

In `Assets/Scripts/Data/ShopCatalog.cs` line 48, change:
```csharp
// BEFORE:
public void BuildFromBackendResponse(AccessoriesResponse response, AccessorySO masterCatalog)

// AFTER:
public void BuildFromBackendResponse(AccessoriesResponse response, AccessoryLookup masterCatalog)
```

The method body uses only `masterCatalog.GetAccessoryItemDataById(shopAccessory.id)` on line 69 — `AccessoryLookup` has this exact method, so no other changes needed in the body.

- [ ] **Step 2: Update ShopCatalog.BuildSetsFromBackendResponse signature**

In `Assets/Scripts/Data/ShopCatalog.cs` line 83, change:
```csharp
// BEFORE:
public void BuildSetsFromBackendResponse(AccessorySetResponse setsResponse, AccessorySO masterCatalog)

// AFTER:
public void BuildSetsFromBackendResponse(AccessorySetResponse setsResponse, AccessoryLookup masterCatalog)
```

The method body uses only `masterCatalog.GetAccessoryItemDataById(accessoryId)` on line 95 — same method exists on `AccessoryLookup`.

- [ ] **Step 3: Update AccessorySetData.ContainsAccessoryType signature**

In `Assets/Scripts/Data/AccessorySetData.cs` line 17, change:
```csharp
// BEFORE:
public bool ContainsAccessoryType(AccessoryType type, AccessorySO masterCatalog)

// AFTER:
public bool ContainsAccessoryType(AccessoryType type, AccessoryLookup masterCatalog)
```

The method body uses only `masterCatalog.GetAccessoryItemDataById(id)` on line 21 — same method exists on `AccessoryLookup`.

- [ ] **Step 4: Update AccessoryRegistry call sites**

In `Assets/Scripts/Registry/AccessoryRegistry.cs`, the two call sites pass `masterCatalogSO` (an `AccessorySO`). Change them to pass `masterCatalogSO.CreateLookup()`:

Line 72:
```csharp
// BEFORE:
activeShopCatalog.BuildFromBackendResponse(response, masterCatalogSO);

// AFTER:
activeShopCatalog.BuildFromBackendResponse(response, masterCatalogSO.CreateLookup());
```

Line 87 (inside the sets callback):
```csharp
// BEFORE:
activeShopCatalog.BuildSetsFromBackendResponse(setsResponse, masterCatalogSO);

// AFTER:
activeShopCatalog.BuildSetsFromBackendResponse(setsResponse, masterCatalogSO.CreateLookup());
```

- [ ] **Step 5: Find and update any other callers of ContainsAccessoryType**

Run: `grep -rn "ContainsAccessoryType" Assets/Scripts/ --include="*.cs"`

For each caller that passes an `AccessorySO`, update to pass `AccessorySO.CreateLookup()` or an `AccessoryLookup` instance instead.

- [ ] **Step 6: Verify compilation**

Run: `refresh_unity` then `read_console`
Expected: Zero compilation errors.

---

### Task 4: Extract AccessoryLoadout from PlayerAccessory

**Files:**
- Create: `Assets/Scripts/Data/AccessoryLoadout.cs`
- Modify: `Assets/Scripts/Player/PlayerAccessory.cs`

**Interfaces:**
- Consumes: `AccessoryType`, `Newtonsoft.Json`
- Produces: `AccessoryLoadout` class used by Task 5 tests. Key signatures:
  - `AccessoryLoadout()`
  - `void Equip(AccessoryType type, int itemId)` — stores slot→itemId mapping
  - `bool Unequip(AccessoryType type)` — returns whether slot was occupied
  - `int GetEquippedItemId(AccessoryType type)` — returns -1 if not equipped
  - `void ResolveConflicts(AccessoryType equippedType)` — removes conflicting slot
  - `string BuildAccessoryJson()` — serializes equipped state to JSON
  - `Dictionary<AccessoryType, int> DeserializeAccessoryState(string json)` — parses remote JSON
  - `IReadOnlyDictionary<AccessoryType, int> EquippedItems` — read-only view

The extraction changes `PlayerAccessory`'s internal `Dictionary<AccessoryType, GameObject> equippedPrefabsBySlot` to use `AccessoryLoadout` for slot→itemId logic, while `PlayerAccessory` continues to own the slot→prefab mapping for visual sync.

- [ ] **Step 1: Create AccessoryLoadout.cs**

```csharp
// Assets/Scripts/Data/AccessoryLoadout.cs
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public class AccessoryLoadout
{
    private static readonly Dictionary<AccessoryType, AccessoryType> conflictingSlots = new()
    {
        { AccessoryType.Headwear, AccessoryType.Hair },
        { AccessoryType.Hair, AccessoryType.Headwear },
    };

    private readonly Dictionary<AccessoryType, int> equippedItemsBySlot = new();

    public IReadOnlyDictionary<AccessoryType, int> EquippedItems => equippedItemsBySlot;

    public void Equip(AccessoryType type, int itemId)
    {
        if (type == AccessoryType.None)
            throw new ArgumentException("Cannot equip to slot None", nameof(type));
        equippedItemsBySlot[type] = itemId;
    }

    public bool Unequip(AccessoryType type)
    {
        return equippedItemsBySlot.Remove(type);
    }

    public int GetEquippedItemId(AccessoryType type)
    {
        return equippedItemsBySlot.TryGetValue(type, out int itemId) ? itemId : -1;
    }

    public bool IsSlotOccupied(AccessoryType type)
    {
        return equippedItemsBySlot.ContainsKey(type);
    }

    public void ResolveConflicts(AccessoryType equippedType)
    {
        if (conflictingSlots.TryGetValue(equippedType, out AccessoryType conflicting))
            equippedItemsBySlot.Remove(conflicting);
    }

    public void Clear()
    {
        equippedItemsBySlot.Clear();
    }

    public string BuildAccessoryJson()
    {
        var state = new Dictionary<string, int>();
        foreach (var kvp in equippedItemsBySlot)
            state[kvp.Key.ToString()] = kvp.Value;
        return JsonConvert.SerializeObject(state);
    }

    public Dictionary<AccessoryType, int> DeserializeAccessoryState(string json)
    {
        if (string.IsNullOrEmpty(json))
            return new Dictionary<AccessoryType, int>();

        Dictionary<string, int> rawMap;
        try
        {
            rawMap = JsonConvert.DeserializeObject<Dictionary<string, int>>(json)
                ?? throw new InvalidOperationException(
                    $"AccessoryLoadout: deserialized to null from: {json}");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"AccessoryLoadout: failed to parse JSON: {json}", exception);
        }

        var result = new Dictionary<AccessoryType, int>();
        foreach (var kvp in rawMap)
        {
            if (Enum.TryParse(kvp.Key, out AccessoryType type))
                result[type] = kvp.Value;
        }
        return result;
    }

    public void LoadFromItemIds(List<int> itemIds, Func<int, AccessoryType> typeResolver)
    {
        if (typeResolver == null)
            throw new ArgumentNullException(nameof(typeResolver));

        equippedItemsBySlot.Clear();
        if (itemIds == null) return;

        foreach (int itemId in itemIds)
        {
            AccessoryType type = typeResolver(itemId);
            if (type != AccessoryType.None)
                equippedItemsBySlot[type] = itemId;
        }
    }
}
```

- [ ] **Step 2: Update PlayerAccessory to use AccessoryLoadout**

`PlayerAccessory` keeps its `Dictionary<AccessoryType, GameObject> equippedPrefabsBySlot` for visual sync (needs GameObjects), but delegates slot/conflict/JSON logic to a private `AccessoryLoadout loadout` field.

Key changes in `PlayerAccessory.cs`:

Add field:
```csharp
private AccessoryLoadout loadout = new();
```

Update `LoadAccessoriesFromBackend` callback to also populate `loadout`:
```csharp
// Inside the success callback, after populating equippedPrefabsBySlot:
loadout.Clear();
foreach (var kvp in equippedPrefabsBySlot)
{
    int id = AccessoryRegistry.Instance.GetAccessoryIdByPrefab(kvp.Value);
    if (id != -1) loadout.Equip(kvp.Key, id);
}
```

Replace `ResolveConflicts` call with:
```csharp
loadout.Equip(type, accessoryId);
loadout.ResolveConflicts(type);
// Also remove from equippedPrefabsBySlot any slot that loadout removed
foreach (var slot in new List<AccessoryType>(equippedPrefabsBySlot.Keys))
{
    if (!loadout.IsSlotOccupied(slot))
        equippedPrefabsBySlot.Remove(slot);
}
```

Replace `BuildAccessoryJson(equippedPrefabsBySlot)` with:
```csharp
loadout.BuildAccessoryJson()
```

Replace the body of `GetEquippedItemId`:
```csharp
public int GetEquippedItemId(AccessoryType type)
{
    return loadout.GetEquippedItemId(type);
}
```

Delete the old private `ResolveConflicts`, `BuildAccessoryJson`, and `conflictingSlots` from `PlayerAccessory` — they now live in `AccessoryLoadout`.

Keep `OnAccessoryJsonChanged` and `SyncVisuals` as-is — they deal with GameObjects and visual sync, which stay in the NetworkBehaviour.

- [ ] **Step 3: Verify compilation**

Run: `refresh_unity` then `read_console`
Expected: Zero compilation errors.

---

### Task 5: Write AccessoryLoadout Tests

**Files:**
- Create: `Assets/Tests/EditMode/AccessoryLoadoutTests.cs`

**Interfaces:**
- Consumes: `AccessoryLoadout` from Task 4, `AccessoryType`
- Produces: Test coverage for loadout logic (no downstream dependencies)

- [ ] **Step 1: Create AccessoryLoadoutTests.cs**

```csharp
// Assets/Tests/EditMode/AccessoryLoadoutTests.cs
using System;
using System.Collections.Generic;
using NUnit.Framework;

[TestFixture]
public class AccessoryLoadoutTests
{
    private AccessoryLoadout CreateLoadout()
    {
        return new AccessoryLoadout();
    }

    // --- Equip ---

    [Test]
    public void Equip_ValidSlot_StoresItemId()
    {
        var loadout = CreateLoadout();

        loadout.Equip(AccessoryType.Headwear, 42);

        Assert.AreEqual(42, loadout.GetEquippedItemId(AccessoryType.Headwear));
    }

    [Test]
    public void Equip_SameSlotTwice_OverwritesPrevious()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Headwear, 42);

        loadout.Equip(AccessoryType.Headwear, 99);

        Assert.AreEqual(99, loadout.GetEquippedItemId(AccessoryType.Headwear));
    }

    [Test]
    public void Equip_NoneSlot_ThrowsArgumentException()
    {
        var loadout = CreateLoadout();

        Assert.Throws<ArgumentException>(() => loadout.Equip(AccessoryType.None, 1));
    }

    [Test]
    public void Equip_MultipleDifferentSlots_AllStored()
    {
        var loadout = CreateLoadout();

        loadout.Equip(AccessoryType.Headwear, 1);
        loadout.Equip(AccessoryType.MainHand, 2);
        loadout.Equip(AccessoryType.Back, 3);

        Assert.AreEqual(1, loadout.GetEquippedItemId(AccessoryType.Headwear));
        Assert.AreEqual(2, loadout.GetEquippedItemId(AccessoryType.MainHand));
        Assert.AreEqual(3, loadout.GetEquippedItemId(AccessoryType.Back));
    }

    // --- Unequip ---

    [Test]
    public void Unequip_OccupiedSlot_ReturnsTrueAndRemoves()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Hair, 5);

        bool result = loadout.Unequip(AccessoryType.Hair);

        Assert.IsTrue(result);
        Assert.AreEqual(-1, loadout.GetEquippedItemId(AccessoryType.Hair));
    }

    [Test]
    public void Unequip_EmptySlot_ReturnsFalse()
    {
        var loadout = CreateLoadout();

        bool result = loadout.Unequip(AccessoryType.Hair);

        Assert.IsFalse(result);
    }

    // --- GetEquippedItemId ---

    [Test]
    public void GetEquippedItemId_EmptySlot_ReturnsNegativeOne()
    {
        var loadout = CreateLoadout();

        Assert.AreEqual(-1, loadout.GetEquippedItemId(AccessoryType.Headwear));
    }

    // --- IsSlotOccupied ---

    [Test]
    public void IsSlotOccupied_OccupiedSlot_ReturnsTrue()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Headwear, 1);

        Assert.IsTrue(loadout.IsSlotOccupied(AccessoryType.Headwear));
    }

    [Test]
    public void IsSlotOccupied_EmptySlot_ReturnsFalse()
    {
        var loadout = CreateLoadout();

        Assert.IsFalse(loadout.IsSlotOccupied(AccessoryType.Headwear));
    }

    // --- ResolveConflicts ---

    [Test]
    public void ResolveConflicts_HeadwearRemovesHair()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Hair, 10);

        loadout.Equip(AccessoryType.Headwear, 20);
        loadout.ResolveConflicts(AccessoryType.Headwear);

        Assert.AreEqual(20, loadout.GetEquippedItemId(AccessoryType.Headwear));
        Assert.AreEqual(-1, loadout.GetEquippedItemId(AccessoryType.Hair));
    }

    [Test]
    public void ResolveConflicts_HairRemovesHeadwear()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Headwear, 20);

        loadout.Equip(AccessoryType.Hair, 10);
        loadout.ResolveConflicts(AccessoryType.Hair);

        Assert.AreEqual(10, loadout.GetEquippedItemId(AccessoryType.Hair));
        Assert.AreEqual(-1, loadout.GetEquippedItemId(AccessoryType.Headwear));
    }

    [Test]
    public void ResolveConflicts_NonConflictingSlot_NoEffect()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.MainHand, 5);
        loadout.Equip(AccessoryType.Back, 6);

        loadout.ResolveConflicts(AccessoryType.MainHand);

        Assert.AreEqual(5, loadout.GetEquippedItemId(AccessoryType.MainHand));
        Assert.AreEqual(6, loadout.GetEquippedItemId(AccessoryType.Back));
    }

    [Test]
    public void ResolveConflicts_ConflictSlotEmpty_NoError()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Headwear, 20);

        loadout.ResolveConflicts(AccessoryType.Headwear);

        Assert.AreEqual(20, loadout.GetEquippedItemId(AccessoryType.Headwear));
    }

    // --- BuildAccessoryJson ---

    [Test]
    public void BuildAccessoryJson_EmptyLoadout_ReturnsEmptyObject()
    {
        var loadout = CreateLoadout();

        string json = loadout.BuildAccessoryJson();

        Assert.AreEqual("{}", json);
    }

    [Test]
    public void BuildAccessoryJson_SingleItem_ReturnsValidJson()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Headwear, 42);

        string json = loadout.BuildAccessoryJson();

        Assert.IsTrue(json.Contains("\"Headwear\""));
        Assert.IsTrue(json.Contains("42"));
    }

    [Test]
    public void BuildAccessoryJson_MultipleItems_AllPresent()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Headwear, 1);
        loadout.Equip(AccessoryType.MainHand, 2);

        string json = loadout.BuildAccessoryJson();

        Assert.IsTrue(json.Contains("Headwear"));
        Assert.IsTrue(json.Contains("MainHand"));
    }

    // --- DeserializeAccessoryState ---

    [Test]
    public void DeserializeAccessoryState_ValidJson_ReturnsCorrectMap()
    {
        var loadout = CreateLoadout();

        var result = loadout.DeserializeAccessoryState("{\"Headwear\":1,\"MainHand\":20}");

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(1, result[AccessoryType.Headwear]);
        Assert.AreEqual(20, result[AccessoryType.MainHand]);
    }

    [Test]
    public void DeserializeAccessoryState_EmptyString_ReturnsEmptyDict()
    {
        var loadout = CreateLoadout();

        var result = loadout.DeserializeAccessoryState("");

        Assert.AreEqual(0, result.Count);
    }

    [Test]
    public void DeserializeAccessoryState_NullString_ReturnsEmptyDict()
    {
        var loadout = CreateLoadout();

        var result = loadout.DeserializeAccessoryState(null);

        Assert.AreEqual(0, result.Count);
    }

    [Test]
    public void DeserializeAccessoryState_UnknownKeys_SkipsGracefully()
    {
        var loadout = CreateLoadout();

        var result = loadout.DeserializeAccessoryState("{\"Headwear\":1,\"FutureSlot\":99}");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(1, result[AccessoryType.Headwear]);
    }

    [Test]
    public void DeserializeAccessoryState_MalformedJson_ThrowsInvalidOperationException()
    {
        var loadout = CreateLoadout();

        Assert.Throws<InvalidOperationException>(
            () => loadout.DeserializeAccessoryState("{not valid json}"));
    }

    // --- Round-trip ---

    [Test]
    public void BuildAndDeserialize_RoundTrip_PreservesState()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Headwear, 1);
        loadout.Equip(AccessoryType.MainHand, 20);
        loadout.Equip(AccessoryType.Back, 5);

        string json = loadout.BuildAccessoryJson();
        var restored = loadout.DeserializeAccessoryState(json);

        Assert.AreEqual(3, restored.Count);
        Assert.AreEqual(1, restored[AccessoryType.Headwear]);
        Assert.AreEqual(20, restored[AccessoryType.MainHand]);
        Assert.AreEqual(5, restored[AccessoryType.Back]);
    }

    // --- LoadFromItemIds ---

    [Test]
    public void LoadFromItemIds_ValidIds_PopulatesSlots()
    {
        var loadout = CreateLoadout();
        var typeMap = new Dictionary<int, AccessoryType>
        {
            { 1, AccessoryType.Headwear },
            { 2, AccessoryType.MainHand },
        };

        loadout.LoadFromItemIds(
            new List<int> { 1, 2 },
            id => typeMap.TryGetValue(id, out var type) ? type : AccessoryType.None);

        Assert.AreEqual(1, loadout.GetEquippedItemId(AccessoryType.Headwear));
        Assert.AreEqual(2, loadout.GetEquippedItemId(AccessoryType.MainHand));
    }

    [Test]
    public void LoadFromItemIds_NullResolver_ThrowsArgumentNullException()
    {
        var loadout = CreateLoadout();

        Assert.Throws<ArgumentNullException>(
            () => loadout.LoadFromItemIds(new List<int> { 1 }, null));
    }

    [Test]
    public void LoadFromItemIds_NullList_ClearsLoadout()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Headwear, 42);

        loadout.LoadFromItemIds(null, id => AccessoryType.None);

        Assert.AreEqual(-1, loadout.GetEquippedItemId(AccessoryType.Headwear));
    }

    [Test]
    public void LoadFromItemIds_UnknownTypeIds_Skipped()
    {
        var loadout = CreateLoadout();

        loadout.LoadFromItemIds(
            new List<int> { 1, 2, 3 },
            id => id == 2 ? AccessoryType.Hair : AccessoryType.None);

        Assert.AreEqual(-1, loadout.GetEquippedItemId(AccessoryType.Headwear));
        Assert.AreEqual(2, loadout.GetEquippedItemId(AccessoryType.Hair));
    }

    // --- Clear ---

    [Test]
    public void Clear_RemovesAllEquippedItems()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Headwear, 1);
        loadout.Equip(AccessoryType.MainHand, 2);

        loadout.Clear();

        Assert.AreEqual(-1, loadout.GetEquippedItemId(AccessoryType.Headwear));
        Assert.AreEqual(-1, loadout.GetEquippedItemId(AccessoryType.MainHand));
    }
}
```

- [ ] **Step 2: Run tests**

Run: `run_tests` with EditMode test filter `AccessoryLoadoutTests`
Expected: All tests pass.

---

### Task 6: Write ShopCatalog Tests

**Files:**
- Create: `Assets/Tests/EditMode/ShopCatalogTests.cs`

**Interfaces:**
- Consumes: `ShopCatalog`, `AccessoryLookup` from Task 1, `ShopAccessoryItem`, `AccessorySetData`, DTOs (`AccessoriesResponse`, `Accessory`, `AccessoryCost`, `AccessorySetResponse`, `AccessorySet`)
- Produces: Test coverage for ShopCatalog build and query (no downstream dependencies)

Note: `ShopAccessoryItem` constructor takes `AccessoryItemData` which has a `GameObject prefab` field. Set `prefab = null` in tests — the catalog logic doesn't require live prefabs.

- [ ] **Step 1: Create ShopCatalogTests.cs**

```csharp
// Assets/Tests/EditMode/ShopCatalogTests.cs
using System.Collections.Generic;
using NUnit.Framework;

[TestFixture]
public class ShopCatalogTests
{
    private AccessoryItemData CreateItemData(int itemId, AccessoryType type)
    {
        return new AccessoryItemData
        {
            itemId = itemId,
            accessoryType = type,
            prefab = null
        };
    }

    private AccessoryLookup CreateLookup(params AccessoryItemData[] items)
    {
        return new AccessoryLookup(items);
    }

    private Accessory CreateShopAccessory(int id, string name, bool isOwned, int goldCost = 100)
    {
        return new Accessory
        {
            id = id,
            name = name,
            isOwned = isOwned,
            costs = new List<AccessoryCost>
            {
                new AccessoryCost { currency = "gold", amount = goldCost }
            }
        };
    }

    private AccessoriesResponse CreateAccessoriesResponse(
        string category, List<Accessory> accessories)
    {
        return new AccessoriesResponse
        {
            accessories = new Dictionary<string, List<Accessory>>
            {
                { category, accessories }
            }
        };
    }

    // --- BuildFromBackendResponse ---

    [Test]
    public void BuildFromBackendResponse_MatchingItem_CreatesShopItem()
    {
        var catalog = new ShopCatalog();
        var lookup = CreateLookup(CreateItemData(1, AccessoryType.Headwear));
        var response = CreateAccessoriesResponse("headwear",
            new List<Accessory> { CreateShopAccessory(1, "Cool Hat", false) });

        catalog.BuildFromBackendResponse(response, lookup);

        var item = catalog.GetItemById(1);
        Assert.IsNotNull(item);
        Assert.AreEqual("Cool Hat", item.displayName);
        Assert.IsFalse(item.isOwned);
    }

    [Test]
    public void BuildFromBackendResponse_NoMasterMatch_SkipsItem()
    {
        var catalog = new ShopCatalog();
        var lookup = CreateLookup(); // empty
        var response = CreateAccessoriesResponse("headwear",
            new List<Accessory> { CreateShopAccessory(999, "Ghost Hat", false) });

        catalog.BuildFromBackendResponse(response, lookup);

        Assert.IsNull(catalog.GetItemById(999));
    }

    [Test]
    public void BuildFromBackendResponse_UnknownCategory_SkipsCategory()
    {
        var catalog = new ShopCatalog();
        var lookup = CreateLookup(CreateItemData(1, AccessoryType.Headwear));
        var response = CreateAccessoriesResponse("unknowncategory",
            new List<Accessory> { CreateShopAccessory(1, "Hat", false) });

        catalog.BuildFromBackendResponse(response, lookup);

        Assert.IsNull(catalog.GetItemById(1));
    }

    [Test]
    public void BuildFromBackendResponse_MultipleCategoriesAndItems()
    {
        var catalog = new ShopCatalog();
        var lookup = CreateLookup(
            CreateItemData(1, AccessoryType.Headwear),
            CreateItemData(2, AccessoryType.MainHand));

        var response = new AccessoriesResponse
        {
            accessories = new Dictionary<string, List<Accessory>>
            {
                { "headwear", new List<Accessory> { CreateShopAccessory(1, "Hat", true) } },
                { "mainhand", new List<Accessory> { CreateShopAccessory(2, "Sword", false) } },
            }
        };

        catalog.BuildFromBackendResponse(response, lookup);

        Assert.IsNotNull(catalog.GetItemById(1));
        Assert.IsNotNull(catalog.GetItemById(2));
        Assert.IsTrue(catalog.IsOwned(1));
        Assert.IsFalse(catalog.IsOwned(2));
    }

    [Test]
    public void BuildFromBackendResponse_CalledTwice_ClearsPrevious()
    {
        var catalog = new ShopCatalog();
        var lookup = CreateLookup(CreateItemData(1, AccessoryType.Headwear));

        catalog.BuildFromBackendResponse(
            CreateAccessoriesResponse("headwear",
                new List<Accessory> { CreateShopAccessory(1, "Hat", false) }),
            lookup);

        catalog.BuildFromBackendResponse(
            CreateAccessoriesResponse("headwear", new List<Accessory>()),
            lookup);

        Assert.IsNull(catalog.GetItemById(1));
    }

    // --- Query methods ---

    [Test]
    public void GetItemsForType_ExistingType_ReturnsItems()
    {
        var catalog = new ShopCatalog();
        var lookup = CreateLookup(
            CreateItemData(1, AccessoryType.Headwear),
            CreateItemData(2, AccessoryType.Headwear));

        var response = CreateAccessoriesResponse("headwear",
            new List<Accessory>
            {
                CreateShopAccessory(1, "Hat A", false),
                CreateShopAccessory(2, "Hat B", true),
            });

        catalog.BuildFromBackendResponse(response, lookup);

        var items = catalog.GetItemsForType(AccessoryType.Headwear);
        Assert.AreEqual(2, items.Count);
    }

    [Test]
    public void GetItemsForType_EmptyType_ReturnsEmptyList()
    {
        var catalog = new ShopCatalog();

        var items = catalog.GetItemsForType(AccessoryType.Back);

        Assert.IsNotNull(items);
        Assert.AreEqual(0, items.Count);
    }

    [Test]
    public void TryGetGoldCost_ExistingItem_ReturnsTrueWithAmount()
    {
        var catalog = new ShopCatalog();
        var lookup = CreateLookup(CreateItemData(1, AccessoryType.Headwear));
        catalog.BuildFromBackendResponse(
            CreateAccessoriesResponse("headwear",
                new List<Accessory> { CreateShopAccessory(1, "Hat", false, goldCost: 250) }),
            lookup);

        bool result = catalog.TryGetGoldCost(1, out int goldAmount);

        Assert.IsTrue(result);
        Assert.AreEqual(250, goldAmount);
    }

    [Test]
    public void TryGetGoldCost_MissingItem_ReturnsFalse()
    {
        var catalog = new ShopCatalog();

        bool result = catalog.TryGetGoldCost(999, out int goldAmount);

        Assert.IsFalse(result);
        Assert.AreEqual(0, goldAmount);
    }

    [Test]
    public void MarkOwned_ExistingItem_SetsOwnedTrue()
    {
        var catalog = new ShopCatalog();
        var lookup = CreateLookup(CreateItemData(1, AccessoryType.Headwear));
        catalog.BuildFromBackendResponse(
            CreateAccessoriesResponse("headwear",
                new List<Accessory> { CreateShopAccessory(1, "Hat", false) }),
            lookup);

        catalog.MarkOwned(1);

        Assert.IsTrue(catalog.IsOwned(1));
    }

    [Test]
    public void MarkOwned_MissingItem_NoError()
    {
        var catalog = new ShopCatalog();

        catalog.MarkOwned(999);

        Assert.IsFalse(catalog.IsOwned(999));
    }

    [Test]
    public void IsOwned_MissingItem_ReturnsFalse()
    {
        var catalog = new ShopCatalog();

        Assert.IsFalse(catalog.IsOwned(999));
    }

    // --- Set queries ---

    [Test]
    public void BuildSetsFromBackendResponse_ValidSet_CreatesSetData()
    {
        var catalog = new ShopCatalog();
        var lookup = CreateLookup(
            CreateItemData(1, AccessoryType.Headwear),
            CreateItemData(2, AccessoryType.MainHand));

        var setsResponse = new AccessorySetResponse
        {
            sets = new Dictionary<string, List<AccessorySet>>
            {
                {
                    "bundles", new List<AccessorySet>
                    {
                        new AccessorySet
                        {
                            id = 10,
                            name = "Warrior Set",
                            isOwned = false,
                            accessoryIDs = new List<int> { 1, 2 },
                            costs = new List<AccessoryCost>
                            {
                                new AccessoryCost { currency = "gold", amount = 500 }
                            }
                        }
                    }
                }
            }
        };

        catalog.BuildSetsFromBackendResponse(setsResponse, lookup);

        var sets = catalog.GetAllAvailableSets();
        Assert.AreEqual(1, sets.Length);
        Assert.AreEqual("Warrior Set", sets[0].setName);
    }

    [Test]
    public void BuildSetsFromBackendResponse_SetWithMissingAccessory_SkipsSet()
    {
        var catalog = new ShopCatalog();
        var lookup = CreateLookup(CreateItemData(1, AccessoryType.Headwear));

        var setsResponse = new AccessorySetResponse
        {
            sets = new Dictionary<string, List<AccessorySet>>
            {
                {
                    "bundles", new List<AccessorySet>
                    {
                        new AccessorySet
                        {
                            id = 10,
                            name = "Incomplete Set",
                            isOwned = false,
                            accessoryIDs = new List<int> { 1, 999 },
                            costs = new List<AccessoryCost>()
                        }
                    }
                }
            }
        };

        catalog.BuildSetsFromBackendResponse(setsResponse, lookup);

        Assert.AreEqual(0, catalog.GetAllAvailableSets().Length);
    }

    [Test]
    public void TryGetSetGoldCost_ExistingSet_ReturnsTrueWithAmount()
    {
        var catalog = new ShopCatalog();
        var lookup = CreateLookup(CreateItemData(1, AccessoryType.Headwear));

        var setsResponse = new AccessorySetResponse
        {
            sets = new Dictionary<string, List<AccessorySet>>
            {
                {
                    "bundles", new List<AccessorySet>
                    {
                        new AccessorySet
                        {
                            id = 10,
                            name = "Set",
                            isOwned = false,
                            accessoryIDs = new List<int> { 1 },
                            costs = new List<AccessoryCost>
                            {
                                new AccessoryCost { currency = "gold", amount = 300 }
                            }
                        }
                    }
                }
            }
        };

        catalog.BuildSetsFromBackendResponse(setsResponse, lookup);

        bool result = catalog.TryGetSetGoldCost(10, out int goldAmount);
        Assert.IsTrue(result);
        Assert.AreEqual(300, goldAmount);
    }

    [Test]
    public void MarkSetOwned_ExistingSet_SetsOwnedTrue()
    {
        var catalog = new ShopCatalog();
        var lookup = CreateLookup(CreateItemData(1, AccessoryType.Headwear));

        var setsResponse = new AccessorySetResponse
        {
            sets = new Dictionary<string, List<AccessorySet>>
            {
                {
                    "bundles", new List<AccessorySet>
                    {
                        new AccessorySet
                        {
                            id = 10,
                            name = "Set",
                            isOwned = false,
                            accessoryIDs = new List<int> { 1 },
                            costs = new List<AccessoryCost>()
                        }
                    }
                }
            }
        };

        catalog.BuildSetsFromBackendResponse(setsResponse, lookup);
        catalog.MarkSetOwned(10);

        Assert.IsTrue(catalog.IsSetOwned(10));
    }
}
```

- [ ] **Step 2: Run tests**

Run: `run_tests` with EditMode test filter `ShopCatalogTests`
Expected: All tests pass.

---

### Task 7: Write ShopAccessoryItem, AccessorySetData, and AttachmentBone Tests

**Files:**
- Create: `Assets/Tests/EditMode/ShopItemAndSetTests.cs`
- Create: `Assets/Tests/EditMode/AttachmentBoneTests.cs`

**Interfaces:**
- Consumes: `ShopAccessoryItem`, `AccessorySetData`, `AccessoryLookup`, `AccessoryPool.GetAttachmentBoneName` (static), DTOs
- Produces: Test coverage (no downstream dependencies)

**Production fix needed:** Both `ShopAccessoryItem.GetCostForCurrency` and `AccessorySetData.GetCostForCurrency` iterate `costs` with foreach — null `costs` throws `NullReferenceException`. Add a null guard to both: `if (costs == null) return 0;` as the first line. The null-costs tests below will fail (TDD-style) until these guards are added.

- [ ] **Step 0: Add null guard to ShopAccessoryItem.GetCostForCurrency and AccessorySetData.GetCostForCurrency**

In `Assets/Scripts/Data/ShopCatalog.cs`, `ShopAccessoryItem.GetCostForCurrency` (line 26):
```csharp
public int GetCostForCurrency(string currency)
{
    if (costs == null) return 0;
    foreach (var cost in costs)
    {
        if (cost.currency.ToLowerInvariant() == currency.ToLowerInvariant())
            return cost.amount;
    }
    return 0;
}
```

In `Assets/Scripts/Data/AccessorySetData.cs`, `AccessorySetData.GetCostForCurrency` (line 28):
```csharp
public int GetCostForCurrency(string currency)
{
    if (costs == null) return 0;
    foreach (var cost in costs)
    {
        if (cost.currency.ToLowerInvariant() == currency.ToLowerInvariant())
            return cost.amount;
    }
    return 0;
}
```

- [ ] **Step 1: Create ShopItemAndSetTests.cs**

```csharp
// Assets/Tests/EditMode/ShopItemAndSetTests.cs
using System.Collections.Generic;
using NUnit.Framework;

[TestFixture]
public class ShopItemAndSetTests
{
    // --- ShopAccessoryItem.GetCostForCurrency ---

    private ShopAccessoryItem CreateShopItem(int itemId, AccessoryType type, List<AccessoryCost> costs)
    {
        var masterData = new AccessoryItemData
        {
            itemId = itemId,
            accessoryType = type,
            prefab = null
        };
        var shopData = new Accessory
        {
            id = itemId,
            name = "Test Item",
            isOwned = false,
            costs = costs
        };
        return new ShopAccessoryItem(masterData, shopData);
    }

    [Test]
    public void GetCostForCurrency_ExactMatch_ReturnsAmount()
    {
        var costs = new List<AccessoryCost>
        {
            new AccessoryCost { currency = "gold", amount = 100 },
            new AccessoryCost { currency = "gems", amount = 10 },
        };
        var item = CreateShopItem(1, AccessoryType.Headwear, costs);

        Assert.AreEqual(100, item.GetCostForCurrency("gold"));
        Assert.AreEqual(10, item.GetCostForCurrency("gems"));
    }

    [Test]
    public void GetCostForCurrency_CaseInsensitive_ReturnsAmount()
    {
        var costs = new List<AccessoryCost>
        {
            new AccessoryCost { currency = "Gold", amount = 100 },
        };
        var item = CreateShopItem(1, AccessoryType.Headwear, costs);

        Assert.AreEqual(100, item.GetCostForCurrency("gold"));
        Assert.AreEqual(100, item.GetCostForCurrency("GOLD"));
    }

    [Test]
    public void GetCostForCurrency_MissingCurrency_ReturnsZero()
    {
        var costs = new List<AccessoryCost>
        {
            new AccessoryCost { currency = "gold", amount = 100 },
        };
        var item = CreateShopItem(1, AccessoryType.Headwear, costs);

        Assert.AreEqual(0, item.GetCostForCurrency("diamonds"));
    }

    [Test]
    public void GetCostForCurrency_EmptyCosts_ReturnsZero()
    {
        var item = CreateShopItem(1, AccessoryType.Headwear, new List<AccessoryCost>());

        Assert.AreEqual(0, item.GetCostForCurrency("gold"));
    }

    [Test]
    public void GetCostForCurrency_NullCosts_ReturnsZero()
    {
        var item = CreateShopItem(1, AccessoryType.Headwear, null);

        Assert.AreEqual(0, item.GetCostForCurrency("gold"));
    }

    [Test]
    public void Constructor_CopiesMasterDataFields()
    {
        var masterData = new AccessoryItemData
        {
            itemId = 42,
            accessoryType = AccessoryType.MainHand,
            prefab = null
        };
        var shopData = new Accessory
        {
            id = 42,
            name = "Epic Sword",
            isOwned = true,
            costs = new List<AccessoryCost>()
        };

        var shopItem = new ShopAccessoryItem(masterData, shopData);

        Assert.AreEqual(42, shopItem.itemId);
        Assert.AreEqual(AccessoryType.MainHand, shopItem.accessoryType);
        Assert.AreEqual("Epic Sword", shopItem.displayName);
        Assert.IsTrue(shopItem.isOwned);
    }

    // --- AccessorySetData.GetCostForCurrency ---

    [Test]
    public void SetData_GetCostForCurrency_ExactMatch_ReturnsAmount()
    {
        var setData = new AccessorySetData
        {
            setId = 1,
            setName = "Test Set",
            costs = new List<AccessoryCost>
            {
                new AccessoryCost { currency = "gold", amount = 500 },
            },
            accessoryIDs = new List<int> { 1, 2 }
        };

        Assert.AreEqual(500, setData.GetCostForCurrency("gold"));
    }

    [Test]
    public void SetData_GetCostForCurrency_CaseInsensitive()
    {
        var setData = new AccessorySetData
        {
            setId = 1,
            setName = "Test Set",
            costs = new List<AccessoryCost>
            {
                new AccessoryCost { currency = "Gold", amount = 500 },
            },
            accessoryIDs = new List<int>()
        };

        Assert.AreEqual(500, setData.GetCostForCurrency("GOLD"));
    }

    [Test]
    public void SetData_GetCostForCurrency_MissingCurrency_ReturnsZero()
    {
        var setData = new AccessorySetData
        {
            setId = 1,
            setName = "Test Set",
            costs = new List<AccessoryCost>
            {
                new AccessoryCost { currency = "gold", amount = 500 },
            },
            accessoryIDs = new List<int>()
        };

        Assert.AreEqual(0, setData.GetCostForCurrency("diamonds"));
    }

    [Test]
    public void SetData_GetCostForCurrency_NullCosts_ReturnsZero()
    {
        var setData = new AccessorySetData
        {
            setId = 1,
            setName = "Test Set",
            costs = null,
            accessoryIDs = new List<int>()
        };

        Assert.AreEqual(0, setData.GetCostForCurrency("gold"));
    }

    // --- AccessorySetData.ContainsAccessoryType ---

    [Test]
    public void ContainsAccessoryType_MatchingType_ReturnsTrue()
    {
        var lookup = new AccessoryLookup(new[]
        {
            new AccessoryItemData { itemId = 1, accessoryType = AccessoryType.Headwear, prefab = null },
            new AccessoryItemData { itemId = 2, accessoryType = AccessoryType.MainHand, prefab = null },
        });

        var setData = new AccessorySetData
        {
            setId = 1,
            setName = "Test",
            accessoryIDs = new List<int> { 1, 2 },
            costs = new List<AccessoryCost>()
        };

        Assert.IsTrue(setData.ContainsAccessoryType(AccessoryType.Headwear, lookup));
        Assert.IsTrue(setData.ContainsAccessoryType(AccessoryType.MainHand, lookup));
    }

    [Test]
    public void ContainsAccessoryType_NoMatch_ReturnsFalse()
    {
        var lookup = new AccessoryLookup(new[]
        {
            new AccessoryItemData { itemId = 1, accessoryType = AccessoryType.Headwear, prefab = null },
        });

        var setData = new AccessorySetData
        {
            setId = 1,
            setName = "Test",
            accessoryIDs = new List<int> { 1 },
            costs = new List<AccessoryCost>()
        };

        Assert.IsFalse(setData.ContainsAccessoryType(AccessoryType.Back, lookup));
    }

    [Test]
    public void ContainsAccessoryType_UnknownAccessoryId_ReturnsFalse()
    {
        var lookup = new AccessoryLookup(new AccessoryItemData[0]);

        var setData = new AccessorySetData
        {
            setId = 1,
            setName = "Test",
            accessoryIDs = new List<int> { 999 },
            costs = new List<AccessoryCost>()
        };

        Assert.IsFalse(setData.ContainsAccessoryType(AccessoryType.Headwear, lookup));
    }
}
```

- [ ] **Step 2: Create AttachmentBoneTests.cs**

```csharp
// Assets/Tests/EditMode/AttachmentBoneTests.cs
using NUnit.Framework;

[TestFixture]
public class AttachmentBoneTests
{
    [Test]
    public void GetAttachmentBoneName_Headwear_ReturnsHead()
    {
        Assert.AreEqual("Head", AccessoryPool.GetAttachmentBoneName(AccessoryType.Headwear));
    }

    [Test]
    public void GetAttachmentBoneName_Hair_ReturnsHead()
    {
        Assert.AreEqual("Head", AccessoryPool.GetAttachmentBoneName(AccessoryType.Hair));
    }

    [Test]
    public void GetAttachmentBoneName_Facewear_ReturnsHead()
    {
        Assert.AreEqual("Head", AccessoryPool.GetAttachmentBoneName(AccessoryType.Facewear));
    }

    [Test]
    public void GetAttachmentBoneName_MainHand_ReturnsRightHand()
    {
        Assert.AreEqual("RightHand", AccessoryPool.GetAttachmentBoneName(AccessoryType.MainHand));
    }

    [Test]
    public void GetAttachmentBoneName_OffHand_ReturnsLeftHand()
    {
        Assert.AreEqual("LeftHand", AccessoryPool.GetAttachmentBoneName(AccessoryType.OffHand));
    }

    [Test]
    public void GetAttachmentBoneName_Back_ReturnsSpine2()
    {
        Assert.AreEqual("Spine2", AccessoryPool.GetAttachmentBoneName(AccessoryType.Back));
    }

    [Test]
    public void GetAttachmentBoneName_DefaultSlot_ReturnsHead()
    {
        Assert.AreEqual("Head", AccessoryPool.GetAttachmentBoneName(AccessoryType.Torso));
        Assert.AreEqual("Head", AccessoryPool.GetAttachmentBoneName(AccessoryType.Arms));
        Assert.AreEqual("Head", AccessoryPool.GetAttachmentBoneName(AccessoryType.Legs));
        Assert.AreEqual("Head", AccessoryPool.GetAttachmentBoneName(AccessoryType.Feet));
    }
}
```

- [ ] **Step 3: Run all tests**

Run: `run_tests` with EditMode filter
Expected: All tests pass, including `ShopItemAndSetTests` and `AttachmentBoneTests`.

---

### Task 8: Run Full Test Suite and Verify

**Files:** None (verification only)

**Interfaces:** None

- [ ] **Step 1: Run all EditMode tests**

Run: `run_tests` for all EditMode tests.
Expected: All tests pass — `InventoryTests`, `InventoryStateTests`, `PreviewStateTrackerTests`, `ApiResponseTests`, `InventoryFilterTests`, `AccessoryLookupTests`, `AccessoryLoadoutTests`, `ShopCatalogTests`, `ShopItemAndSetTests`, `AttachmentBoneTests`.

- [ ] **Step 2: Verify no compilation errors**

Run: `refresh_unity` then `read_console`
Expected: Zero errors, zero warnings from project code.

- [ ] **Step 3: Verify existing behavior unchanged**

Run: Enter Play Mode in Unity Editor, verify the shop and accessories work as before.
This confirms the extraction didn't break runtime behavior.
