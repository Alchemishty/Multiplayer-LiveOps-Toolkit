# Accessory Sets UI & Equip Flow

Surface accessory sets as a first-class feature: browse, preview, purchase, and equip entire sets from a dedicated Sets tab.

## Context

The data layer for sets is complete: `AccessorySetData`, DTOs (`AccessorySet`, `AccessorySetResponse`), `ShopCatalog` set queries, `ShopAPI.GetAccessorySets()`, and `AccessoryRegistry` set lookups all work. A `SetsPanel` GameObject exists in the scene with no script. `ShopAPI.PurchaseSet()` is a stub. No UI displays sets and no equip/preview flow handles multi-item sets.

## New Types

### SetPreviewStateTracker (Data layer, pure C#)

Tracks multi-slot preview state for a complete accessory set. Unlike `PreviewStateTracker` (single slot), this manages N slots simultaneously.

**State:**
- `previewedSetId` (int, -1 when inactive)
- `previewedSlots` — `Dictionary<AccessoryType, SetPreviewSlot>` where `SetPreviewSlot` is a struct of `{ int previewedItemId, int previouslyEquippedItemId }`

**Methods:**
- `StartSetPreview(int setId, List<SetPreviewSlotInput> slots)` — stores set ID and per-slot previous state. `SetPreviewSlotInput` is a struct: `{ AccessoryType slot, int itemId, int currentEquippedItemId }`. Throws if setId <= 0 or slots is empty.
- `ClearSetPreview() → ClearSetResult` — returns `WasActive`, `ClearedSetId`, and `List<ClearedSlotInfo>` (each with `ClearedSlot`, `ClearedItemId`, `RestoreItemId`). Resets all state. Fires `SetPreviewChanged` event.
- `IsSetPreviewActive` (bool property)
- `PreviewedSetId` (int property)
- `GetPreviewedSlots() → IReadOnlyDictionary<AccessoryType, SetPreviewSlot>`
- `event Action SetPreviewChanged`

**Design notes:**
- Starting a new set preview when one is already active: caller must clear first (this class doesn't auto-clear — the orchestrator `AccessoryEquipService` handles that).
- Struct types `SetPreviewSlot`, `SetPreviewSlotInput`, `ClearSetResult`, `ClearedSlotInfo` are nested in this class.

### SetsPanel (Example/UI layer, MonoBehaviour)

Script for the existing `SetsPanel` GameObject in the scene. Follows the same patterns as `ShopPanel` and `InventoryPanel`.

**SerializeFields:** `contentContainer` (Transform), `statusText` (TMP_Text)

**Layout:** For each set from `AccessoryRegistry.GetAllSets()`:
- Set header row: set name + cost + action button
- Below the header: indented list of constituent accessory names (resolved via `AccessoryRegistry.GetShopItemById()`)

**Button states:**
- "Preview" (unowned, not previewing) — calls `AccessoryEquipService.PreviewSet(setId)`
- "Buy" (unowned, actively previewed) — calls purchase flow
- "Equip" (owned, not all equipped) — calls `AccessoryEquipService.EquipSet(setId)`
- "Equipped" (owned, all items equipped) — disabled

**Lifecycle:**
- `OnEnable`: wait for registry + avatar, populate, subscribe events
- `OnDisable`: clear set preview, unsubscribe, stop coroutines
- `OnDestroy`: cleanup listeners

**Refresh triggers:** `ShopCatalogLoaded`, `AccessoryOwnershipChanged`, `EquippedStateChanged`, `SetPreviewChanged`

**Purchase flow:** Same pattern as `ShopPanel` — on success, show `PurchaseConfirmationPrompt`. On confirm: equip set via `AccessoryEquipService.EquipSet(setId)`. On cancel: dismiss.

**Empty state:** "No sets available" message when `GetAllSets()` returns empty.

## Modified Types

### AccessoryEquipService

Add set preview and equip orchestration alongside existing single-item methods.

**New fields:**
- `private readonly SetPreviewStateTracker setPreviewState = new SetPreviewStateTracker();`

**New methods:**
- `PreviewSet(int setId)` — resolves all accessory IDs via `AccessoryRegistry.GetAccessoryIdsInSet(setId)`, clears any active single-item preview, clears any active set preview (restoring slots), then visually equips each item via `IAccessoryVisualSync.Equip()` and tracks state via `SetPreviewStateTracker`. Handles base mesh toggling per slot.
- `ClearSetPreview()` — gets `ClearSetResult` from tracker, restores each slot (unequip preview, re-equip previous if any, restore base meshes).
- `EquipSet(int setId)` — resolves all accessory IDs, calls `PlayerAccessory.UpdateEquippedAccessories(List<int>)`, handles base meshes, calls `StoreEquippedAccessories()`.

**New properties/events:**
- `IsSetPreviewActive` → delegates to `setPreviewState.IsSetPreviewActive`
- `PreviewedSetId` → delegates to `setPreviewState.PreviewedSetId`
- `event Action SetPreviewChanged` → delegates to `setPreviewState.SetPreviewChanged`

**Cross-preview coordination:**
- `PreviewSet` clears any active single-item preview first
- `PreviewItem` clears any active set preview first

### ShopAPI.PurchaseSet

Wire the stub to a real endpoint.

**Endpoint:** `POST /api/shop/purchase-set/`
**Body:** `{"set_id": int}`
**Response:** 200 success with `{"accessory_ids": [int]}` (granted items), or 400 with `{"message": "reason"}`
**Callback:** `Action<bool, string, List<int>> onSuccess` — (success, message, grantedAccessoryIds)

Implementation follows `PurchaseAccessory` pattern: `UnityWebRequest` POST with JSON body, `SendWithTokenRefresh`, deserialize response.

New DTO:
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

### AccessoryRegistry

- `MarkSetOwned(int setId)` — also mark all constituent accessories as owned (call `MarkAccessoryOwned` for each accessory ID in the set), then fire `AccessoryOwnershipChanged`.
- Currently `MarkSetOwned` only calls `activeShopCatalog.MarkSetOwned(setId)` without firing events or marking constituent items.

### ShopPanel / InventoryPanel

No changes. Sets are not displayed in these panels.

## Files Changed

- New: `Assets/Scripts/Data/SetPreviewStateTracker.cs`
- New: `Assets/Scripts/Example/UI/SetsPanel.cs`
- New: `Assets/Tests/EditMode/SetPreviewStateTrackerTests.cs`
- Modified: `Assets/Scripts/Example/AccessoryEquipService.cs`
- Modified: `Assets/Scripts/API/ShopAPI.cs`
- Modified: `Assets/Scripts/Data/ShopApiDTOs.cs`
- Modified: `Assets/Scripts/Registry/AccessoryRegistry.cs`

## Testing

### SetPreviewStateTrackerTests (EditMode)
- `StartSetPreview_StoresSetIdAndSlots`
- `StartSetPreview_InvalidSetId_Throws`
- `StartSetPreview_EmptySlots_Throws`
- `ClearSetPreview_WhenActive_ReturnsAllSlotsToRestore`
- `ClearSetPreview_WhenInactive_ReturnsNotActive`
- `ClearSetPreview_RestoresCorrectPreviousItemIds`
- `ClearSetPreview_SlotsWithNoPrevious_RestoreIdIsNegative`
- `IsSetPreviewActive_ReflectsState`
- `PreviewedSetId_ReflectsState`
- `SetPreviewChanged_FiresOnStartAndClear`

### Not tested (MonoBehaviour/UI wiring)
- `SetsPanel` — procedural UI, event wiring, coroutines
- `AccessoryEquipService` set methods — depends on live `PlayerAccessory`, `IAccessoryVisualSync`, `AccessoryRegistry`
