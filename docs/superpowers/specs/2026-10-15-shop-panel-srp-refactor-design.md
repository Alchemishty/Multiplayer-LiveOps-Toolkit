# Shop Panel SRP Refactor Design

## Problem

`ShopPanel` violates Single Responsibility Principle by mixing four concerns: UI construction, input handling, purchase orchestration, and display formatting. The same violation is replicated in `SetsPanel` and `InventoryPanel`, with substantial code duplication across all three.

### Current Responsibilities in ShopPanel (458 lines)

| Concern | Methods | Lines |
|---|---|---|
| UI construction | `CreateUIObject`, `CreateSectionHeader`, `CreateItemRow` | ~100 |
| Display formatting | `FormatCosts`, `GetDisplayName` | ~20 |
| Button state logic | `UpdateRowButton`, `RefreshAllButtons` | ~40 |
| Lifecycle + events | `OnEnable`, `OnDisable`, `OnDestroy`, `WaitAndPopulate`, `Subscribe/UnsubscribeEvents` | ~60 |
| Purchase flow | `PurchaseItem`, `OnBuyButtonClicked`, `ShowPurchaseConfirmation` | ~80 |
| Input routing | `OnItemButtonClicked`, `OnBuyButtonClicked` | ~25 |

### Cross-Panel Duplication

| Method | ShopPanel | SetsPanel | InventoryPanel |
|---|---|---|---|
| `CreateUIObject` | identical | identical | identical |
| `CreateSectionHeader` | identical | — | identical |
| `CreateEmptyStateMessage` | — | identical | identical |
| `FormatCosts` | identical | identical | — |
| `GetDisplayName` | identical | — | identical |
| `ResolvePlayerAccessory` | identical | identical | identical |
| `WaitAndPopulate` (pattern) | near-identical | near-identical | near-identical |
| `SetStatus` | identical | identical | identical |

## Solution

Extract three new types. Each panel becomes a thin orchestrator.

### New Type 1: `ShopDisplayFormatter` (Data layer)

**File:** `Assets/Scripts/Data/ShopDisplayFormatter.cs`

Static utility for pure formatting functions.

```csharp
public static class ShopDisplayFormatter
{
    public static string FormatCosts(List<AccessoryCost> costs);
    public static string GetDisplayName(AccessoryType type);
    public static string GetItemDisplayName(ShopAccessoryItem item);
}
```

- `FormatCosts`: returns `"Free"` for null/empty, otherwise joins as `"100 gold + 50 gems"`. Currently duplicated verbatim in ShopPanel and SetsPanel.
- `GetDisplayName`: maps `AccessoryType` → human-readable string (e.g., `MainHand` → `"Main Hand"`). Currently duplicated in ShopPanel and InventoryPanel.
- `GetItemDisplayName`: resolves the fallback chain `displayName → prefab.name → "Item {id}"`. Currently inline in ShopPanel and InventoryPanel's `CreateItemRow`.

No dependencies beyond Data-layer types.

### New Type 2: `ShopButtonStateResolver` (Data layer)

**File:** `Assets/Scripts/Data/ShopButtonStateResolver.cs`

Pure function mapping item state to display state. Extracts the 4-way branching in `UpdateRowButton` / `UpdateSetRowButton`.

```csharp
public struct ButtonDisplayState
{
    public string text;
    public Color color;
    public bool isInteractable;
    public bool showCost;
}

public static class ShopButtonStateResolver
{
    public static ButtonDisplayState ResolveShopItemState(
        bool isOwned, bool isPurchasing, bool isPreviewing);

    public static ButtonDisplayState ResolveShopSetState(
        bool isOwned, bool isPurchasing, bool isPreviewing);

    public static ButtonDisplayState ResolveInventoryItemState(bool isEquipped);

    public static ButtonDisplayState ResolveInventorySetState(bool isEquipped);
}
```

State → display mapping:

| Resolver | Condition | Text | Color | Interactable | ShowCost |
|---|---|---|---|---|---|
| ShopItem | owned | "Owned" | gray (0.3, 0.3, 0.35) | false | false |
| ShopItem | purchasing | "..." | gray (0.3, 0.3, 0.35) | false | true |
| ShopItem | previewing | "Buy" | orange (0.6, 0.3, 0.1) | true | true |
| ShopItem | default | "Preview" | blue (0.2, 0.4, 0.6) | true | true |
| ShopSet | (same as ShopItem) | (same) | (same) | (same) | (same) |
| InventoryItem | equipped | "Unequip" | red (0.5, 0.2, 0.2) | true | false |
| InventoryItem | default | "Equip" | green (0.2, 0.6, 0.3) | true | false |
| InventorySet | (same as InventoryItem) | (same) | (same) | (same) | (same) |

Note: ShopItem and ShopSet currently produce identical states. InventoryItem and InventorySet also produce identical states. Keep them as separate methods to allow future divergence without modifying callers.

### New Type 3: `ShopUIFactory` (Example/UI layer)

**File:** `Assets/Scripts/Example/UI/ShopUIFactory.cs`

Static utility for all procedural UI construction.

```csharp
public struct ItemRowElements
{
    public GameObject rootObject;
    public Button actionButton;
    public Image buttonImage;
    public TMP_Text buttonText;
    public TMP_Text costText;  // null for inventory rows without cost display
}

public static class ShopUIFactory
{
    public static GameObject CreateUIObject(
        string name, Transform parent, bool stretch = false);

    public static GameObject CreateSectionHeader(
        string title, Transform container);

    public static GameObject CreateEmptyStateMessage(
        string message, Transform container);

    public static ItemRowElements CreateShopItemRow(
        string displayName, Transform container);

    public static ItemRowElements CreateShopSetRow(
        string displayName, Transform container);

    public static ItemRowElements CreateInventoryItemRow(
        string displayName, Transform container);

    public static ItemRowElements CreateInventorySetRow(
        string displayName, Transform container);
}
```

Key design decisions:
- Factory methods take display-ready strings, not domain objects. The panel resolves `ShopAccessoryItem` → display name via `ShopDisplayFormatter` before calling the factory.
- `ItemRowElements` is a shared struct. `costText` is null for inventory rows that don't show cost.
- Row creation methods are per-context (shop item vs set vs inventory) because the layout differs: shop items use horizontal layout with cost column; sets use vertical layout with header row; inventory items omit the cost column.

### Panel Changes

#### ShopPanel (after refactor: ~220 lines)

Keeps:
- Fields: `shopItemRows`, `isPopulated`, `playerAccessory`, `isPurchasing`, `activePrompt`
- Lifecycle: `OnEnable`, `OnDisable`, `OnDestroy`
- Events: `SubscribeEvents`, `UnsubscribeEvents`, `ResolvePlayerAccessory`
- Wait: `WaitAndPopulate`
- Input: `OnItemButtonClicked`, `OnBuyButtonClicked`
- Purchase: `PurchaseItem`, `ShowPurchaseConfirmation`, `ClearStatusAfterDelay`, `SetStatus`

Delegates:
- `CreateSectionHeader(title)` → `ShopUIFactory.CreateSectionHeader(title, contentContainer)`
- `CreateItemRow(item)` → `ShopUIFactory.CreateShopItemRow(ShopDisplayFormatter.GetItemDisplayName(item), contentContainer)`
- `FormatCosts(costs)` → `ShopDisplayFormatter.FormatCosts(costs)`
- `GetDisplayName(type)` → `ShopDisplayFormatter.GetDisplayName(type)`
- `UpdateRowButton` branching → `ShopButtonStateResolver.ResolveShopItemState(...)` + apply struct

Row tracking struct changes from nested `ShopRowUI` class to:

```csharp
private struct ShopItemRow
{
    public ShopAccessoryItem shopItem;
    public ItemRowElements elements;
}
```

#### SetsPanel (after refactor: ~220 lines)

Same structural change. Uses `ShopUIFactory.CreateShopSetRow`, `ShopButtonStateResolver.ResolveShopSetState`, `ShopDisplayFormatter.FormatCosts`.

#### InventoryPanel (after refactor: ~250 lines)

Same structural change. Uses `ShopUIFactory.CreateInventoryItemRow`, `ShopUIFactory.CreateInventorySetRow`, `ShopButtonStateResolver.ResolveInventoryItemState`, `ShopButtonStateResolver.ResolveInventorySetState`, `ShopDisplayFormatter.GetDisplayName`.

## Testing

### ShopDisplayFormatterTests (EditMode)

- `FormatCosts_NullCosts_ReturnsFree`
- `FormatCosts_EmptyCosts_ReturnsFree`
- `FormatCosts_SingleCost_ReturnsFormatted`
- `FormatCosts_MultipleCosts_JoinsWithPlus`
- `GetDisplayName_AllDefinedTypes_ReturnsReadableString`
- `GetDisplayName_UndefinedType_FallsBackToToString`
- `GetItemDisplayName_WithDisplayName_ReturnsDisplayName`
- `GetItemDisplayName_NullDisplayName_FallsToPrefabName`
- `GetItemDisplayName_NullPrefabAndDisplayName_FallsToItemId`

### ShopButtonStateResolverTests (EditMode)

- `ResolveShopItemState_Owned_ReturnsOwnedState`
- `ResolveShopItemState_Purchasing_ReturnsPurchasingState`
- `ResolveShopItemState_Previewing_ReturnsBuyState`
- `ResolveShopItemState_Default_ReturnsPreviewState`
- `ResolveShopSetState_Owned_ReturnsOwnedState`
- `ResolveShopSetState_Default_ReturnsPreviewState`
- `ResolveInventoryItemState_Equipped_ReturnsUnequipState`
- `ResolveInventoryItemState_NotEquipped_ReturnsEquipState`
- `ResolveInventorySetState_Equipped_ReturnsUnequipState`
- `ResolveInventorySetState_NotEquipped_ReturnsEquipState`

### ShopUIFactory

Not EditMode-testable (requires GameObjects). Verified through:
1. Unity compilation (zero errors after `refresh_unity`)
2. Manual visual verification (existing scene still renders correctly)
3. Existing behavior preserved (same UI, same interactions)

## Files Changed

| File | Action |
|---|---|
| `Assets/Scripts/Data/ShopDisplayFormatter.cs` | New |
| `Assets/Scripts/Data/ShopButtonStateResolver.cs` | New |
| `Assets/Scripts/Example/UI/ShopUIFactory.cs` | New |
| `Assets/Scripts/Example/UI/ShopPanel.cs` | Refactored — delegates to new types |
| `Assets/Scripts/Example/UI/SetsPanel.cs` | Refactored — delegates to new types |
| `Assets/Scripts/Example/UI/InventoryPanel.cs` | Refactored — delegates to new types |
| `Assets/Tests/EditMode/ShopDisplayFormatterTests.cs` | New |
| `Assets/Tests/EditMode/ShopButtonStateResolverTests.cs` | New |

## Behavioral Guarantees

- Zero visible changes to UI layout, colors, text, or interaction flow
- All existing tests continue to pass
- Button state transitions remain identical (same colors, text, interactability for each state)
- Purchase flow unchanged (same coroutine pattern, same confirmation prompt)
- Event subscription/unsubscription patterns unchanged
