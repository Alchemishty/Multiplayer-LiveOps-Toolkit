# Shop / Inventory Tab Split

Split the single `ShopPanel` into two tabs: **Shop** (browse, preview, buy) and **Inventory** (equip/unequip owned items).

## New Types

### AccessoryEquipService (Example layer, MonoBehaviour singleton)
Centralizes equip/unequip/preview logic extracted from ShopPanel.

**Methods:**
- `EquipItem(int itemId, AccessoryType type)` — equip via PlayerAccessory, toggle base mesh, persist to backend
- `UnequipItem(AccessoryType type)` — unequip via PlayerAccessory, restore base mesh, persist to backend
- `PreviewItem(int itemId, AccessoryType type)` — show accessory visually without network sync or persistence
- `ClearPreview()` — revert previewed slot to prior state
- `IsPreviewActive` (property) — whether a preview is showing
- `PreviewedItemId` (property) — which item is being previewed (-1 if none)

**Preview mechanics:**
- Calls `IAccessoryVisualSync.Equip()` directly for visual-only display
- Stores `previouslyEquippedItemId` so ClearPreview can restore the slot
- On ClearPreview: unequip the preview slot, re-equip the original if one existed

**Owns:** `baseMeshNames` dictionary and `SetBaseMeshActive` (moved from ShopPanel).

**Events:**
- `event Action PreviewChanged` — fired when preview starts/clears, so ShopPanel can update button states

### InventoryPanel (Example/UI layer, MonoBehaviour)
Shows owned accessories grouped by type. Equip/unequip buttons.

**Layout:** Same section-header + item-row pattern as ShopPanel. Each row: item name + Equip/Unequip button. No cost display.

**Data source:** `AccessoryRegistry.Instance.GetShopItemsForType(type)` filtered to `isOwned == true`.

**Refresh triggers:** `PlayerAccessory.EquippedStateChanged`, `AccessoryRegistry.ShopCatalogLoaded`, and a new `AccessoryRegistry.AccessoryOwnershipChanged` event (fired when `MarkAccessoryOwned` is called, so inventory updates after purchase).

### PurchaseConfirmationPrompt (Example/UI layer, MonoBehaviour)
Simple overlay prompt shown after a successful purchase. "Equip [item name] now?" with Yes/No buttons.

**On Yes:** calls `AccessoryEquipService.EquipItem()`, then `TabManager.Instance.ShowTab(inventoryTabIndex)`.
**On No:** dismisses the prompt, stays on shop tab.

Built programmatically (same pattern as ShopPanel row construction).

## Modified Types

### ShopPanel (refactored)
- Remove: `EquipItem`, `UnequipItem`, `SetBaseMeshActive`, `baseMeshNames` dictionary
- Button behavior changes:
  - **Unowned item tap:** toggle preview via `AccessoryEquipService.PreviewItem()` / `ClearPreview()`, button shows "Buy"
  - **Owned item tap:** no action (marked "Owned")
  - **Currently previewing same item:** `ClearPreview()`
- `OnDisable`: calls `AccessoryEquipService.ClearPreview()` (tab switch clears preview)
- Post-purchase: show `PurchaseConfirmationPrompt` instead of auto-equipping
- Row button states: "Buy" (unowned, not previewing), "Previewing" (unowned, actively previewed), "Owned" (owned)
- Listens to `AccessoryEquipService.PreviewChanged` to refresh button states

### AccessoryRegistry
- Add `event Action AccessoryOwnershipChanged`
- Fire in `MarkAccessoryOwned()` after setting `isOwned = true`

### PlayerAccessory
- Add `public IAccessoryVisualSync GetVisualSyncPublic()` — exposes the visual sync for preview use by AccessoryEquipService (the existing `GetVisualSync()` is private)

## Tab Wiring
`TabManager` already supports N tabs/panels via serialized lists. The new InventoryPanel GameObject gets added to the scene's tab configuration as index 1 (Shop = 0, Inventory = 1).

## Testing
- **AccessoryEquipServiceTests (EditMode):** Extract preview state logic into a testable `PreviewStateTracker` pure C# class. Test: start preview, swap preview, clear preview, clear restores previous, no-op when clearing with no preview.
- **InventoryFilterTests (EditMode):** Test filtering ShopAccessoryItem lists to owned-only items (pure list operation).

## Files Changed
- New: `Assets/Scripts/Example/AccessoryEquipService.cs`
- New: `Assets/Scripts/Example/UI/InventoryPanel.cs`
- New: `Assets/Scripts/Example/UI/PurchaseConfirmationPrompt.cs`
- New: `Assets/Scripts/Data/PreviewStateTracker.cs`
- New: `Assets/Tests/EditMode/PreviewStateTrackerTests.cs`
- New: `Assets/Tests/EditMode/InventoryFilterTests.cs`
- Modified: `Assets/Scripts/Example/UI/ShopPanel.cs`
- Modified: `Assets/Scripts/Registry/AccessoryRegistry.cs`
- Modified: `Assets/Scripts/Player/PlayerAccessory.cs`
