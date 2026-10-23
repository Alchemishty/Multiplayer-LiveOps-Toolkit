# Shop Panel SRP Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract UI construction, display formatting, and button state logic from ShopPanel/SetsPanel/InventoryPanel into three shared pure/static types, eliminating SRP violations and cross-panel duplication.

**Architecture:** Three new types (ShopDisplayFormatter, ShopButtonStateResolver in Data layer; ShopUIFactory in Example/UI layer) replace duplicated inline code. Each panel becomes a thin orchestrator delegating to these types. TDD for the two Data-layer types; ShopUIFactory verified by compilation.

**Tech Stack:** Unity, C#, NUnit (Unity Test Framework), TMPro, UnityEngine.UI

**Spec:** `docs/superpowers/specs/2026-10-15-shop-panel-srp-refactor-design.md`

## Global Constraints

- All work is local — no git operations, no commits, no PRs
- Unity compiler must show zero errors after each task (`refresh_unity` → `read_console`)
- All existing tests must continue to pass after each task
- No visible changes to UI layout, colors, text, or interaction flow
- Layer imports: Data layer must not reference Example/Player/Registry/API layers
- Fully descriptive variable names — no abbreviations
- Never silently consume null — throw when non-null is required

## Review Focus

1. **ShopDisplayFormatter.GetItemDisplayName with null item** — callers should never pass null, but a thrown ArgumentNullException is safer than a NullReferenceException. Add null guard test.
2. **ShopButtonStateResolver priority when multiple booleans are true** (e.g., isOwned AND isPreviewing) — owned takes precedence. Add test confirming owned wins over previewing.
3. **ItemRowElements.costText is null for inventory rows** — panels that call `state.showCost` must guard against null costText. Inventory resolvers always return `showCost = false`, but add a test pinning this.
4. **ShopUIFactory stretch parameter default** — must match the existing `false` default. Verified by compilation + identical UI behavior.
5. **SetsPanel uses same click handler for Preview and Buy states** — refactored panel must preserve this (unlike ShopPanel which uses different handlers). Verified by reading refactored code.

---

### Task 1: ShopDisplayFormatter + Tests

**Files:**
- Create: `Assets/Scripts/Data/ShopDisplayFormatter.cs`
- Test: `Assets/Tests/EditMode/ShopDisplayFormatterTests.cs`

**Interfaces:**
- Consumes: `AccessoryCost` (from ShopApiDTOs.cs), `AccessoryType` (enum), `ShopAccessoryItem` (from ShopCatalog.cs)
- Produces: `ShopDisplayFormatter.FormatCosts(List<AccessoryCost>)` → string, `ShopDisplayFormatter.GetDisplayName(AccessoryType)` → string, `ShopDisplayFormatter.GetItemDisplayName(ShopAccessoryItem)` → string

- [ ] **Step 1: Write failing tests**

```csharp
// Assets/Tests/EditMode/ShopDisplayFormatterTests.cs
using NUnit.Framework;
using System.Collections.Generic;

[TestFixture]
public class ShopDisplayFormatterTests
{
    [Test]
    public void FormatCosts_NullCosts_ReturnsFree()
    {
        Assert.AreEqual("Free", ShopDisplayFormatter.FormatCosts(null));
    }

    [Test]
    public void FormatCosts_EmptyCosts_ReturnsFree()
    {
        Assert.AreEqual("Free", ShopDisplayFormatter.FormatCosts(new List<AccessoryCost>()));
    }

    [Test]
    public void FormatCosts_SingleCost_ReturnsFormatted()
    {
        var costs = new List<AccessoryCost>
        {
            new AccessoryCost { amount = 100, currency = "gold" }
        };
        Assert.AreEqual("100 gold", ShopDisplayFormatter.FormatCosts(costs));
    }

    [Test]
    public void FormatCosts_MultipleCosts_JoinsWithPlus()
    {
        var costs = new List<AccessoryCost>
        {
            new AccessoryCost { amount = 100, currency = "gold" },
            new AccessoryCost { amount = 50, currency = "gems" }
        };
        Assert.AreEqual("100 gold + 50 gems", ShopDisplayFormatter.FormatCosts(costs));
    }

    [Test]
    public void GetDisplayName_MainHand_ReturnsMainHand()
    {
        Assert.AreEqual("Main Hand", ShopDisplayFormatter.GetDisplayName(AccessoryType.MainHand));
    }

    [Test]
    public void GetDisplayName_OffHand_ReturnsOffHand()
    {
        Assert.AreEqual("Off Hand", ShopDisplayFormatter.GetDisplayName(AccessoryType.OffHand));
    }

    [Test]
    public void GetDisplayName_Headwear_ReturnsHeadwear()
    {
        Assert.AreEqual("Headwear", ShopDisplayFormatter.GetDisplayName(AccessoryType.Headwear));
    }

    [Test]
    public void GetDisplayName_UndefinedType_FallsBackToToString()
    {
        Assert.AreEqual("Torso", ShopDisplayFormatter.GetDisplayName(AccessoryType.Torso));
    }

    [Test]
    public void GetItemDisplayName_WithDisplayName_ReturnsDisplayName()
    {
        var item = new ShopAccessoryItem(
            new AccessoryItemData { itemId = 1, accessoryType = AccessoryType.Headwear },
            new Accessory { id = 1, name = "Cool Hat", isOwned = false, costs = new List<AccessoryCost>() }
        );
        Assert.AreEqual("Cool Hat", ShopDisplayFormatter.GetItemDisplayName(item));
    }

    [Test]
    public void GetItemDisplayName_NullDisplayName_FallsToItemId()
    {
        var item = new ShopAccessoryItem(
            new AccessoryItemData { itemId = 42, accessoryType = AccessoryType.Headwear },
            new Accessory { id = 42, name = null, isOwned = false, costs = new List<AccessoryCost>() }
        );
        Assert.AreEqual("Item 42", ShopDisplayFormatter.GetItemDisplayName(item));
    }

    [Test]
    public void GetItemDisplayName_EmptyDisplayName_FallsToItemId()
    {
        var item = new ShopAccessoryItem(
            new AccessoryItemData { itemId = 7, accessoryType = AccessoryType.Hair },
            new Accessory { id = 7, name = "", isOwned = false, costs = new List<AccessoryCost>() }
        );
        Assert.AreEqual("Item 7", ShopDisplayFormatter.GetItemDisplayName(item));
    }

    [Test]
    public void GetItemDisplayName_NullItem_ThrowsArgumentNullException()
    {
        Assert.Throws<System.ArgumentNullException>(
            () => ShopDisplayFormatter.GetItemDisplayName(null));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `mcp: run_tests with testMode=EditMode`
Expected: FAIL — `ShopDisplayFormatter` class does not exist

- [ ] **Step 3: Write implementation**

```csharp
// Assets/Scripts/Data/ShopDisplayFormatter.cs
using System.Collections.Generic;

public static class ShopDisplayFormatter
{
    public static string FormatCosts(List<AccessoryCost> costs)
    {
        if (costs == null || costs.Count == 0) return "Free";
        var parts = new List<string>();
        foreach (var accessoryCost in costs)
            parts.Add($"{accessoryCost.amount} {accessoryCost.currency}");
        return string.Join(" + ", parts);
    }

    public static string GetDisplayName(AccessoryType type)
    {
        switch (type)
        {
            case AccessoryType.Headwear: return "Headwear";
            case AccessoryType.Hair:     return "Hair";
            case AccessoryType.Facewear: return "Facewear";
            case AccessoryType.MainHand: return "Main Hand";
            case AccessoryType.OffHand:  return "Off Hand";
            case AccessoryType.Back:     return "Back";
            default:                     return type.ToString();
        }
    }

    public static string GetItemDisplayName(ShopAccessoryItem item)
    {
        if (item == null)
            throw new System.ArgumentNullException(nameof(item));

        if (!string.IsNullOrEmpty(item.displayName))
            return item.displayName;
        if (item.prefab != null)
            return item.prefab.name;
        return $"Item {item.itemId}";
    }
}
```

- [ ] **Step 4: Verify compilation**

Run: `mcp: refresh_unity` then `mcp: read_console`
Expected: zero errors

- [ ] **Step 5: Run tests to verify they pass**

Run: `mcp: run_tests with testMode=EditMode`
Expected: all ShopDisplayFormatterTests pass, all existing tests still pass

---

### Task 2: ShopButtonStateResolver + Tests

**Files:**
- Create: `Assets/Scripts/Data/ShopButtonStateResolver.cs`
- Test: `Assets/Tests/EditMode/ShopButtonStateResolverTests.cs`

**Interfaces:**
- Consumes: `UnityEngine.Color`
- Produces: `ButtonDisplayState` struct, `ShopButtonStateResolver.ResolveShopItemState(bool, bool, bool)`, `.ResolveShopSetState(bool, bool, bool)`, `.ResolveInventoryItemState(bool)`, `.ResolveInventorySetState(bool)`

- [ ] **Step 1: Write failing tests**

```csharp
// Assets/Tests/EditMode/ShopButtonStateResolverTests.cs
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class ShopButtonStateResolverTests
{
    private static readonly Color GRAY = new Color(0.3f, 0.3f, 0.35f);
    private static readonly Color ORANGE = new Color(0.6f, 0.3f, 0.1f);
    private static readonly Color BLUE = new Color(0.2f, 0.4f, 0.6f);
    private static readonly Color RED = new Color(0.5f, 0.2f, 0.2f);
    private static readonly Color GREEN = new Color(0.2f, 0.6f, 0.3f);

    // --- Shop Item ---

    [Test]
    public void ResolveShopItemState_Owned_ReturnsOwnedState()
    {
        var state = ShopButtonStateResolver.ResolveShopItemState(
            isOwned: true, isPurchasing: false, isPreviewing: false);
        Assert.AreEqual("Owned", state.text);
        Assert.AreEqual(GRAY, state.color);
        Assert.IsFalse(state.isInteractable);
        Assert.IsFalse(state.showCost);
    }

    [Test]
    public void ResolveShopItemState_Purchasing_ReturnsPurchasingState()
    {
        var state = ShopButtonStateResolver.ResolveShopItemState(
            isOwned: false, isPurchasing: true, isPreviewing: false);
        Assert.AreEqual("...", state.text);
        Assert.AreEqual(GRAY, state.color);
        Assert.IsFalse(state.isInteractable);
        Assert.IsTrue(state.showCost);
    }

    [Test]
    public void ResolveShopItemState_Previewing_ReturnsBuyState()
    {
        var state = ShopButtonStateResolver.ResolveShopItemState(
            isOwned: false, isPurchasing: false, isPreviewing: true);
        Assert.AreEqual("Buy", state.text);
        Assert.AreEqual(ORANGE, state.color);
        Assert.IsTrue(state.isInteractable);
        Assert.IsTrue(state.showCost);
    }

    [Test]
    public void ResolveShopItemState_Default_ReturnsPreviewState()
    {
        var state = ShopButtonStateResolver.ResolveShopItemState(
            isOwned: false, isPurchasing: false, isPreviewing: false);
        Assert.AreEqual("Preview", state.text);
        Assert.AreEqual(BLUE, state.color);
        Assert.IsTrue(state.isInteractable);
        Assert.IsTrue(state.showCost);
    }

    [Test]
    public void ResolveShopItemState_OwnedTakesPrecedenceOverPreviewing()
    {
        var state = ShopButtonStateResolver.ResolveShopItemState(
            isOwned: true, isPurchasing: false, isPreviewing: true);
        Assert.AreEqual("Owned", state.text);
        Assert.IsFalse(state.isInteractable);
    }

    // --- Shop Set ---

    [Test]
    public void ResolveShopSetState_Owned_ReturnsOwnedState()
    {
        var state = ShopButtonStateResolver.ResolveShopSetState(
            isOwned: true, isPurchasing: false, isPreviewing: false);
        Assert.AreEqual("Owned", state.text);
        Assert.IsFalse(state.isInteractable);
        Assert.IsFalse(state.showCost);
    }

    [Test]
    public void ResolveShopSetState_Default_ReturnsPreviewState()
    {
        var state = ShopButtonStateResolver.ResolveShopSetState(
            isOwned: false, isPurchasing: false, isPreviewing: false);
        Assert.AreEqual("Preview", state.text);
        Assert.IsTrue(state.isInteractable);
        Assert.IsTrue(state.showCost);
    }

    // --- Inventory Item ---

    [Test]
    public void ResolveInventoryItemState_Equipped_ReturnsUnequipState()
    {
        var state = ShopButtonStateResolver.ResolveInventoryItemState(isEquipped: true);
        Assert.AreEqual("Unequip", state.text);
        Assert.AreEqual(RED, state.color);
        Assert.IsTrue(state.isInteractable);
        Assert.IsFalse(state.showCost);
    }

    [Test]
    public void ResolveInventoryItemState_NotEquipped_ReturnsEquipState()
    {
        var state = ShopButtonStateResolver.ResolveInventoryItemState(isEquipped: false);
        Assert.AreEqual("Equip", state.text);
        Assert.AreEqual(GREEN, state.color);
        Assert.IsTrue(state.isInteractable);
        Assert.IsFalse(state.showCost);
    }

    // --- Inventory Set ---

    [Test]
    public void ResolveInventorySetState_Equipped_ReturnsUnequipState()
    {
        var state = ShopButtonStateResolver.ResolveInventorySetState(isEquipped: true);
        Assert.AreEqual("Unequip", state.text);
        Assert.AreEqual(RED, state.color);
        Assert.IsTrue(state.isInteractable);
    }

    [Test]
    public void ResolveInventorySetState_NotEquipped_ReturnsEquipState()
    {
        var state = ShopButtonStateResolver.ResolveInventorySetState(isEquipped: false);
        Assert.AreEqual("Equip", state.text);
        Assert.AreEqual(GREEN, state.color);
        Assert.IsTrue(state.isInteractable);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `mcp: run_tests with testMode=EditMode`
Expected: FAIL — `ShopButtonStateResolver` class does not exist

- [ ] **Step 3: Write implementation**

```csharp
// Assets/Scripts/Data/ShopButtonStateResolver.cs
using UnityEngine;

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
        bool isOwned, bool isPurchasing, bool isPreviewing)
    {
        if (isOwned)
            return new ButtonDisplayState
            {
                text = "Owned",
                color = new Color(0.3f, 0.3f, 0.35f),
                isInteractable = false,
                showCost = false,
            };

        if (isPurchasing)
            return new ButtonDisplayState
            {
                text = "...",
                color = new Color(0.3f, 0.3f, 0.35f),
                isInteractable = false,
                showCost = true,
            };

        if (isPreviewing)
            return new ButtonDisplayState
            {
                text = "Buy",
                color = new Color(0.6f, 0.3f, 0.1f),
                isInteractable = true,
                showCost = true,
            };

        return new ButtonDisplayState
        {
            text = "Preview",
            color = new Color(0.2f, 0.4f, 0.6f),
            isInteractable = true,
            showCost = true,
        };
    }

    public static ButtonDisplayState ResolveShopSetState(
        bool isOwned, bool isPurchasing, bool isPreviewing)
    {
        return ResolveShopItemState(isOwned, isPurchasing, isPreviewing);
    }

    public static ButtonDisplayState ResolveInventoryItemState(bool isEquipped)
    {
        if (isEquipped)
            return new ButtonDisplayState
            {
                text = "Unequip",
                color = new Color(0.5f, 0.2f, 0.2f),
                isInteractable = true,
                showCost = false,
            };

        return new ButtonDisplayState
        {
            text = "Equip",
            color = new Color(0.2f, 0.6f, 0.3f),
            isInteractable = true,
            showCost = false,
        };
    }

    public static ButtonDisplayState ResolveInventorySetState(bool isEquipped)
    {
        return ResolveInventoryItemState(isEquipped);
    }
}
```

- [ ] **Step 4: Verify compilation**

Run: `mcp: refresh_unity` then `mcp: read_console`
Expected: zero errors

- [ ] **Step 5: Run tests to verify they pass**

Run: `mcp: run_tests with testMode=EditMode`
Expected: all ShopButtonStateResolverTests pass, all existing tests still pass

---

### Task 3: ShopUIFactory

**Files:**
- Create: `Assets/Scripts/Example/UI/ShopUIFactory.cs`

**Interfaces:**
- Consumes: UnityEngine.UI, TMPro
- Produces: `ItemRowElements` struct, `ShopUIFactory.CreateUIObject`, `.CreateSectionHeader`, `.CreateEmptyStateMessage`, `.CreateShopItemRow`, `.CreateShopSetRow`, `.CreateInventoryItemRow`, `.CreateInventorySetRow`

- [ ] **Step 1: Create ShopUIFactory with shared struct and helper**

Write the `ItemRowElements` struct and `CreateUIObject` helper to `Assets/Scripts/Example/UI/ShopUIFactory.cs`:

```csharp
// Assets/Scripts/Example/UI/ShopUIFactory.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public struct ItemRowElements
{
    public GameObject rootObject;
    public Button actionButton;
    public Image buttonImage;
    public TMP_Text buttonText;
    public TMP_Text costText;
}

public static class ShopUIFactory
{
    public static GameObject CreateUIObject(string name, Transform parent, bool stretch = false)
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

    public static GameObject CreateSectionHeader(string title, Transform container)
    {
        var headerObject = CreateUIObject($"Header_{title}", container, stretch: true);
        var layout = headerObject.AddComponent<LayoutElement>();
        layout.minHeight = 30;
        layout.preferredHeight = 30;

        var background = headerObject.AddComponent<Image>();
        background.color = new Color(0.2f, 0.25f, 0.35f, 0.9f);

        var textObject = CreateUIObject("Text", headerObject.transform, stretch: true);
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.offsetMin = new Vector2(10, 0);
        var headerText = textObject.AddComponent<TextMeshProUGUI>();
        headerText.text = title;
        headerText.fontSize = 16;
        headerText.fontStyle = FontStyles.Bold;
        headerText.color = new Color(0.7f, 0.8f, 1f);
        headerText.alignment = TextAlignmentOptions.MidlineLeft;

        return headerObject;
    }

    public static GameObject CreateEmptyStateMessage(string message, Transform container)
    {
        var containerObject = CreateUIObject("EmptyState", container, stretch: true);
        var containerLayout = containerObject.AddComponent<LayoutElement>();
        containerLayout.flexibleHeight = 1;
        containerLayout.minHeight = 200;

        var textObject = CreateUIObject("Text", containerObject.transform, stretch: true);
        var emptyStateText = textObject.AddComponent<TextMeshProUGUI>();
        emptyStateText.text = message;
        emptyStateText.fontSize = 16;
        emptyStateText.color = new Color(0.8f, 0.5f, 0.3f);
        emptyStateText.alignment = TextAlignmentOptions.Center;

        return containerObject;
    }

    public static ItemRowElements CreateShopItemRow(string displayName, Transform container)
    {
        var rowObject = CreateUIObject("ItemRow", container, stretch: true);
        var layout = rowObject.AddComponent<LayoutElement>();
        layout.minHeight = 40;
        layout.preferredHeight = 40;

        var rowLayout = rowObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 6;
        rowLayout.padding = new RectOffset(10, 10, 4, 4);
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;

        var background = rowObject.AddComponent<Image>();
        background.color = new Color(0.12f, 0.12f, 0.16f, 0.8f);

        var nameObject = CreateUIObject("Name", rowObject.transform);
        var nameLayout = nameObject.AddComponent<LayoutElement>();
        nameLayout.flexibleWidth = 1;
        var nameText = nameObject.AddComponent<TextMeshProUGUI>();
        nameText.text = displayName;
        nameText.fontSize = 14;
        nameText.color = Color.white;
        nameText.alignment = TextAlignmentOptions.MidlineLeft;

        var costObject = CreateUIObject("Cost", rowObject.transform);
        var costLayout = costObject.AddComponent<LayoutElement>();
        costLayout.preferredWidth = 140;
        costLayout.flexibleWidth = 0;
        var costText = costObject.AddComponent<TextMeshProUGUI>();
        costText.fontSize = 12;
        costText.color = new Color(1f, 0.85f, 0.3f);
        costText.alignment = TextAlignmentOptions.MidlineRight;

        var buttonElements = CreateActionButton(rowObject.transform);

        return new ItemRowElements
        {
            rootObject = rowObject,
            actionButton = buttonElements.actionButton,
            buttonImage = buttonElements.buttonImage,
            buttonText = buttonElements.buttonText,
            costText = costText,
        };
    }

    public static ItemRowElements CreateShopSetRow(string displayName, Transform container)
    {
        var rowObject = CreateUIObject("SetRow", container, stretch: true);
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
        nameText.text = displayName;
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

        var buttonElements = CreateActionButton(headerRow.transform);

        return new ItemRowElements
        {
            rootObject = rowObject,
            actionButton = buttonElements.actionButton,
            buttonImage = buttonElements.buttonImage,
            buttonText = buttonElements.buttonText,
            costText = costText,
        };
    }

    public static ItemRowElements CreateInventoryItemRow(string displayName, Transform container)
    {
        var rowObject = CreateUIObject("ItemRow", container, stretch: true);
        var layout = rowObject.AddComponent<LayoutElement>();
        layout.minHeight = 40;
        layout.preferredHeight = 40;

        var rowLayout = rowObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 6;
        rowLayout.padding = new RectOffset(10, 10, 4, 4);
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;

        var background = rowObject.AddComponent<Image>();
        background.color = new Color(0.12f, 0.12f, 0.16f, 0.8f);

        var nameObject = CreateUIObject("Name", rowObject.transform);
        var nameLayout = nameObject.AddComponent<LayoutElement>();
        nameLayout.flexibleWidth = 1;
        var nameText = nameObject.AddComponent<TextMeshProUGUI>();
        nameText.text = displayName;
        nameText.fontSize = 14;
        nameText.color = Color.white;
        nameText.alignment = TextAlignmentOptions.MidlineLeft;

        var buttonElements = CreateActionButton(rowObject.transform);

        return new ItemRowElements
        {
            rootObject = rowObject,
            actionButton = buttonElements.actionButton,
            buttonImage = buttonElements.buttonImage,
            buttonText = buttonElements.buttonText,
            costText = null,
        };
    }

    public static ItemRowElements CreateInventorySetRow(string displayName, Transform container)
    {
        var rowObject = CreateUIObject("SetRow", container, stretch: true);
        var layout = rowObject.AddComponent<LayoutElement>();
        layout.minHeight = 40;
        layout.preferredHeight = 40;

        var rowLayout = rowObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 6;
        rowLayout.padding = new RectOffset(10, 10, 4, 4);
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;

        var background = rowObject.AddComponent<Image>();
        background.color = new Color(0.14f, 0.14f, 0.18f, 0.9f);

        var nameObject = CreateUIObject("Name", rowObject.transform);
        var nameLayout = nameObject.AddComponent<LayoutElement>();
        nameLayout.flexibleWidth = 1;
        var nameText = nameObject.AddComponent<TextMeshProUGUI>();
        nameText.text = displayName;
        nameText.fontSize = 14;
        nameText.color = new Color(0.9f, 0.85f, 0.7f);
        nameText.alignment = TextAlignmentOptions.MidlineLeft;

        var buttonElements = CreateActionButton(rowObject.transform);

        return new ItemRowElements
        {
            rootObject = rowObject,
            actionButton = buttonElements.actionButton,
            buttonImage = buttonElements.buttonImage,
            buttonText = buttonElements.buttonText,
            costText = null,
        };
    }

    private struct ActionButtonElements
    {
        public Button actionButton;
        public Image buttonImage;
        public TMP_Text buttonText;
    }

    private static ActionButtonElements CreateActionButton(Transform parent)
    {
        var buttonObject = CreateUIObject("Button", parent);
        var buttonLayout = buttonObject.AddComponent<LayoutElement>();
        buttonLayout.preferredWidth = 90;
        buttonLayout.minWidth = 90;
        buttonLayout.flexibleWidth = 0;
        var buttonImage = buttonObject.AddComponent<Image>();
        var button = buttonObject.AddComponent<Button>();

        var buttonTextObject = CreateUIObject("Text", buttonObject.transform);
        var buttonTextRect = buttonTextObject.GetComponent<RectTransform>();
        buttonTextRect.anchorMin = Vector2.zero;
        buttonTextRect.anchorMax = Vector2.one;
        buttonTextRect.sizeDelta = Vector2.zero;
        var buttonText = buttonTextObject.AddComponent<TextMeshProUGUI>();
        buttonText.fontSize = 13;
        buttonText.alignment = TextAlignmentOptions.Center;
        buttonText.color = Color.white;

        return new ActionButtonElements
        {
            actionButton = button,
            buttonImage = buttonImage,
            buttonText = buttonText,
        };
    }
}
```

- [ ] **Step 2: Verify compilation**

Run: `mcp: refresh_unity` then `mcp: read_console`
Expected: zero errors

---

### Task 4: Refactor ShopPanel

**Files:**
- Modify: `Assets/Scripts/Example/UI/ShopPanel.cs`

**Interfaces:**
- Consumes: `ShopUIFactory.CreateSectionHeader`, `ShopUIFactory.CreateShopItemRow`, `ShopDisplayFormatter.FormatCosts`, `ShopDisplayFormatter.GetDisplayName`, `ShopDisplayFormatter.GetItemDisplayName`, `ShopButtonStateResolver.ResolveShopItemState`, `ItemRowElements`

- [ ] **Step 1: Replace ShopPanel with refactored version**

Replace the entire file `Assets/Scripts/Example/UI/ShopPanel.cs` with:

```csharp
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class ShopPanel : MonoBehaviour
{
    private const int INVENTORY_TAB_INDEX = 1;

    [SerializeField] private Transform contentContainer;
    [SerializeField] private TMP_Text statusText;

    private List<ShopItemRow> shopItemRows = new();
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
            AccessoryEquipService.Instance.ClearPreview();
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
            AccessoryEquipService.Instance.PreviewChanged += RefreshAllButtons;
    }

    private void UnsubscribeEvents()
    {
        if (playerAccessory != null)
            playerAccessory.EquippedStateChanged -= RefreshAllButtons;

        if (AccessoryEquipService.Instance != null)
            AccessoryEquipService.Instance.PreviewChanged -= RefreshAllButtons;
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
        PopulateShop();
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

        foreach (var row in shopItemRows)
        {
            if (row.elements.actionButton != null)
                row.elements.actionButton.onClick.RemoveAllListeners();
        }
    }

    private void PopulateShop()
    {
        AccessoryType[] types = {
            AccessoryType.Headwear, AccessoryType.Hair, AccessoryType.Facewear,
            AccessoryType.MainHand, AccessoryType.OffHand, AccessoryType.Back
        };

        foreach (var type in types)
        {
            var items = AccessoryRegistry.Instance.GetShopItemsForType(type);
            if (items.Count == 0) continue;

            ShopUIFactory.CreateSectionHeader(
                ShopDisplayFormatter.GetDisplayName(type), contentContainer);

            foreach (var item in items)
            {
                var elements = ShopUIFactory.CreateShopItemRow(
                    ShopDisplayFormatter.GetItemDisplayName(item), contentContainer);
                elements.rootObject.name = $"Item_{item.itemId}";

                var row = new ShopItemRow { shopItem = item, elements = elements };
                shopItemRows.Add(row);
                UpdateRowButton(row);
            }
        }
    }

    private void OnItemButtonClicked(int itemId, AccessoryType type)
    {
        var registry = AccessoryRegistry.Instance
            ?? throw new System.InvalidOperationException(
                "ShopPanel.OnItemButtonClicked: AccessoryRegistry not initialized");
        var shopItem = registry.GetShopItemById(itemId);
        if (shopItem == null) return;

        if (shopItem.isOwned)
            return;

        var equipService = AccessoryEquipService.Instance
            ?? throw new System.InvalidOperationException(
                "ShopPanel: AccessoryEquipService not initialized");

        if (equipService.IsPreviewActive && equipService.PreviewedItemId == itemId)
        {
            equipService.ClearPreview();
            return;
        }

        equipService.PreviewItem(itemId, type);
    }

    private void OnBuyButtonClicked(int itemId, AccessoryType type)
    {
        if (isPurchasing) return;

        var registry = AccessoryRegistry.Instance
            ?? throw new System.InvalidOperationException(
                "ShopPanel.OnBuyButtonClicked: AccessoryRegistry not initialized");
        var shopItem = registry.GetShopItemById(itemId);
        if (shopItem == null || shopItem.isOwned) return;

        StartCoroutine(PurchaseItem(itemId, type));
    }

    private IEnumerator PurchaseItem(int itemId, AccessoryType type)
    {
        isPurchasing = true;

        if (AccessoryEquipService.Instance != null)
            AccessoryEquipService.Instance.ClearPreview();

        SetStatus("Purchasing...");
        RefreshAllButtons();
        bool isPurchaseComplete = false;

        yield return ShopAPI.Instance.PurchaseAccessory(
            itemId,
            (bool success, string message) =>
            {
                if (success)
                {
                    (AccessoryRegistry.Instance
                        ?? throw new System.InvalidOperationException(
                            "ShopPanel.PurchaseItem: AccessoryRegistry not initialized"))
                        .MarkAccessoryOwned(itemId);

                    var walletDisplay = WalletDisplay.Instance
                        ?? throw new System.InvalidOperationException(
                            "ShopPanel.PurchaseItem: WalletDisplay not initialized");
                    StartCoroutine(walletDisplay.Refresh());
                    SetStatus("");
                    ShowPurchaseConfirmation(itemId, type);
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

    private void ShowPurchaseConfirmation(int itemId, AccessoryType type)
    {
        var registry = AccessoryRegistry.Instance
            ?? throw new System.InvalidOperationException(
                "ShopPanel.ShowPurchaseConfirmation: AccessoryRegistry not initialized");
        var shopItem = registry.GetShopItemById(itemId);
        string itemName = shopItem != null && !string.IsNullOrEmpty(shopItem.displayName)
            ? shopItem.displayName
            : $"Item {itemId}";

        var canvasTransform = GetComponentInParent<Canvas>()?.transform
            ?? throw new System.InvalidOperationException(
                "ShopPanel: no parent Canvas found for confirmation prompt");

        activePrompt = PurchaseConfirmationPrompt.Show(
            canvasTransform,
            itemName,
            onConfirm: () =>
            {
                activePrompt = null;
                var equipService = AccessoryEquipService.Instance
                    ?? throw new System.InvalidOperationException(
                        "ShopPanel: AccessoryEquipService not initialized");
                equipService.EquipItem(itemId, type);

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
        foreach (var row in shopItemRows)
            UpdateRowButton(row);
    }

    private void UpdateRowButton(ShopItemRow row)
    {
        row.elements.actionButton.onClick.RemoveAllListeners();

        bool isOwned = row.shopItem.isOwned;
        bool isPreviewing = AccessoryEquipService.Instance != null
            && AccessoryEquipService.Instance.IsPreviewActive
            && AccessoryEquipService.Instance.PreviewedItemId == row.shopItem.itemId;

        var state = ShopButtonStateResolver.ResolveShopItemState(isOwned, isPurchasing, isPreviewing);
        row.elements.buttonText.text = state.text;
        row.elements.buttonImage.color = state.color;
        row.elements.actionButton.interactable = state.isInteractable;
        row.elements.costText.text = state.showCost
            ? ShopDisplayFormatter.FormatCosts(row.shopItem.costs)
            : "";

        if (state.isInteractable)
        {
            int capturedItemId = row.shopItem.itemId;
            AccessoryType capturedType = row.shopItem.accessoryType;

            if (isPreviewing)
                row.elements.actionButton.onClick.AddListener(
                    () => OnBuyButtonClicked(capturedItemId, capturedType));
            else
                row.elements.actionButton.onClick.AddListener(
                    () => OnItemButtonClicked(capturedItemId, capturedType));
        }
    }

    private struct ShopItemRow
    {
        public ShopAccessoryItem shopItem;
        public ItemRowElements elements;
    }
}
```

- [ ] **Step 2: Verify compilation**

Run: `mcp: refresh_unity` then `mcp: read_console`
Expected: zero errors

- [ ] **Step 3: Run all tests**

Run: `mcp: run_tests with testMode=EditMode`
Expected: all tests pass (existing + new)

---

### Task 5: Refactor SetsPanel

**Files:**
- Modify: `Assets/Scripts/Example/UI/SetsPanel.cs`

**Interfaces:**
- Consumes: `ShopUIFactory.CreateShopSetRow`, `ShopUIFactory.CreateEmptyStateMessage`, `ShopDisplayFormatter.FormatCosts`, `ShopButtonStateResolver.ResolveShopSetState`, `ItemRowElements`

- [ ] **Step 1: Replace SetsPanel with refactored version**

Replace the entire file `Assets/Scripts/Example/UI/SetsPanel.cs` with:

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

    private List<SetPanelRow> setRows = new();
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
        isPurchasing = false;
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
            if (row.elements.actionButton != null)
                row.elements.actionButton.onClick.RemoveAllListeners();
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
            if (row.elements.actionButton != null)
                row.elements.actionButton.onClick.RemoveAllListeners();
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
        var registry = AccessoryRegistry.Instance
            ?? throw new System.InvalidOperationException(
                "SetsPanel.PopulateSets: AccessoryRegistry not initialized");
        var allSets = registry.GetAllSets();

        if (allSets.Length == 0)
        {
            ShopUIFactory.CreateEmptyStateMessage("No sets available.", contentContainer);
            return;
        }

        foreach (var setData in allSets)
        {
            var elements = ShopUIFactory.CreateShopSetRow(setData.setName, contentContainer);
            elements.rootObject.name = $"Set_{setData.setId}";

            var row = new SetPanelRow { setData = setData, elements = elements };
            setRows.Add(row);
            UpdateSetRowButton(row);
        }
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
            return;

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

    private void UpdateSetRowButton(SetPanelRow row)
    {
        row.elements.actionButton.onClick.RemoveAllListeners();

        bool isOwned = row.setData.isOwned;
        bool isPreviewing = AccessoryEquipService.Instance != null
            && AccessoryEquipService.Instance.IsSetPreviewActive
            && AccessoryEquipService.Instance.PreviewedSetId == row.setData.setId;

        var state = ShopButtonStateResolver.ResolveShopSetState(isOwned, isPurchasing, isPreviewing);
        row.elements.buttonText.text = state.text;
        row.elements.buttonImage.color = state.color;
        row.elements.actionButton.interactable = state.isInteractable;
        row.elements.costText.text = state.showCost
            ? ShopDisplayFormatter.FormatCosts(row.setData.costs)
            : "";

        if (state.isInteractable)
        {
            int capturedSetId = row.setData.setId;
            row.elements.actionButton.onClick.AddListener(
                () => OnSetActionClicked(capturedSetId));
        }
    }

    private struct SetPanelRow
    {
        public AccessorySetData setData;
        public ItemRowElements elements;
    }
}
```

- [ ] **Step 2: Verify compilation**

Run: `mcp: refresh_unity` then `mcp: read_console`
Expected: zero errors

- [ ] **Step 3: Run all tests**

Run: `mcp: run_tests with testMode=EditMode`
Expected: all tests pass

---

### Task 6: Refactor InventoryPanel

**Files:**
- Modify: `Assets/Scripts/Example/UI/InventoryPanel.cs`

**Interfaces:**
- Consumes: `ShopUIFactory.CreateSectionHeader`, `ShopUIFactory.CreateEmptyStateMessage`, `ShopUIFactory.CreateInventoryItemRow`, `ShopUIFactory.CreateInventorySetRow`, `ShopDisplayFormatter.GetDisplayName`, `ShopDisplayFormatter.GetItemDisplayName`, `ShopButtonStateResolver.ResolveInventoryItemState`, `ShopButtonStateResolver.ResolveInventorySetState`, `ItemRowElements`

- [ ] **Step 1: Replace InventoryPanel with refactored version**

Replace the entire file `Assets/Scripts/Example/UI/InventoryPanel.cs` with:

```csharp
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class InventoryPanel : MonoBehaviour
{
    [SerializeField] private Transform contentContainer;
    [SerializeField] private TMP_Text statusText;

    private List<InventoryItemRow> inventoryItemRows = new();
    private List<InventorySetRow> setRows = new();
    private bool isPopulated = false;
    private PlayerAccessory playerAccessory;

    private void OnEnable()
    {
        if (!isPopulated)
            StartCoroutine(WaitAndPopulate());
        else
        {
            ResolvePlayerAccessory();
            SubscribeEvents();
            RefreshInventory();
        }
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        UnsubscribeEvents();
        SetStatus("");
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

        if (AccessoryRegistry.Instance != null)
            AccessoryRegistry.Instance.AccessoryOwnershipChanged += RefreshInventory;
    }

    private void UnsubscribeEvents()
    {
        if (playerAccessory != null)
            playerAccessory.EquippedStateChanged -= RefreshAllButtons;

        if (AccessoryRegistry.Instance != null)
            AccessoryRegistry.Instance.AccessoryOwnershipChanged -= RefreshInventory;
    }

    private IEnumerator WaitAndPopulate()
    {
        while (AccessoryRegistry.Instance == null || !AccessoryRegistry.Instance.IsShopCatalogLoaded())
            yield return null;

        while (ClientPlayerAvatars.Instance == null || ClientPlayerAvatars.Instance.currentClientAvatar == null)
            yield return null;

        if (!gameObject.activeInHierarchy)
            yield break;

        var avatar = ClientPlayerAvatars.Instance.currentClientAvatar;
        playerAccessory = avatar.GetComponent<PlayerAccessory>()
            ?? throw new System.InvalidOperationException(
                "InventoryPanel: PlayerAccessory component not found on avatar");

        SubscribeEvents();
        PopulateInventory();
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

        foreach (var row in inventoryItemRows)
        {
            if (row.elements.actionButton != null)
                row.elements.actionButton.onClick.RemoveAllListeners();
        }
        foreach (var row in setRows)
        {
            if (row.elements.actionButton != null)
                row.elements.actionButton.onClick.RemoveAllListeners();
        }
    }

    private void RefreshInventory()
    {
        if (AccessoryRegistry.Instance == null || !AccessoryRegistry.Instance.IsShopCatalogLoaded())
            return;

        ClearRows();
        PopulateInventory();
    }

    private void ClearRows()
    {
        foreach (var row in inventoryItemRows)
        {
            if (row.elements.actionButton != null)
                row.elements.actionButton.onClick.RemoveAllListeners();
        }
        inventoryItemRows.Clear();

        foreach (var row in setRows)
        {
            if (row.elements.actionButton != null)
                row.elements.actionButton.onClick.RemoveAllListeners();
        }
        setRows.Clear();

        if (contentContainer != null)
        {
            for (int i = contentContainer.childCount - 1; i >= 0; i--)
                Destroy(contentContainer.GetChild(i).gameObject);
        }
    }

    private void PopulateInventory()
    {
        bool hasAnyContent = false;

        var ownedSets = GetOwnedSets();
        if (ownedSets.Count > 0)
        {
            hasAnyContent = true;
            ShopUIFactory.CreateSectionHeader("Outfits", contentContainer);

            foreach (var setData in ownedSets)
            {
                var elements = ShopUIFactory.CreateInventorySetRow(
                    setData.setName, contentContainer);
                elements.rootObject.name = $"Set_{setData.setId}";

                var row = new InventorySetRow { setData = setData, elements = elements };
                setRows.Add(row);
                UpdateSetRowButton(row);
            }
        }

        AccessoryType[] types = {
            AccessoryType.Headwear, AccessoryType.Hair, AccessoryType.Facewear,
            AccessoryType.MainHand, AccessoryType.OffHand, AccessoryType.Back
        };

        foreach (var type in types)
        {
            var allItems = AccessoryRegistry.Instance.GetShopItemsForType(type);
            var ownedItems = FilterOwnedItems(allItems);
            if (ownedItems.Count == 0) continue;

            hasAnyContent = true;
            ShopUIFactory.CreateSectionHeader(
                ShopDisplayFormatter.GetDisplayName(type), contentContainer);

            foreach (var ownedItem in ownedItems)
            {
                var elements = ShopUIFactory.CreateInventoryItemRow(
                    ShopDisplayFormatter.GetItemDisplayName(ownedItem), contentContainer);
                elements.rootObject.name = $"Item_{ownedItem.itemId}";

                var row = new InventoryItemRow { shopItem = ownedItem, elements = elements };
                inventoryItemRows.Add(row);

                int capturedItemId = ownedItem.itemId;
                AccessoryType capturedAccessoryType = ownedItem.accessoryType;
                elements.actionButton.onClick.AddListener(
                    () => OnItemButtonClicked(capturedItemId, capturedAccessoryType));

                UpdateRowButton(row);
            }
        }

        if (!hasAnyContent)
            ShopUIFactory.CreateEmptyStateMessage(
                "No items owned yet.\nVisit the Shop to buy accessories!", contentContainer);
    }

    private List<AccessorySetData> GetOwnedSets()
    {
        var registry = AccessoryRegistry.Instance;
        if (registry == null) return new List<AccessorySetData>();

        var allSets = registry.GetAllSets();
        var ownedSets = new List<AccessorySetData>();
        foreach (var setData in allSets)
        {
            if (setData.isOwned)
                ownedSets.Add(setData);
        }
        return ownedSets;
    }

    public static List<ShopAccessoryItem> FilterOwnedItems(List<ShopAccessoryItem> items)
    {
        var ownedItems = new List<ShopAccessoryItem>();
        foreach (var item in items)
        {
            if (item.isOwned)
                ownedItems.Add(item);
        }
        return ownedItems;
    }

    private void OnSetEquipClicked(int setId)
    {
        var equipService = AccessoryEquipService.Instance
            ?? throw new System.InvalidOperationException(
                "InventoryPanel: AccessoryEquipService not initialized");

        if (equipService.IsPreviewActive)
            equipService.ClearPreview();

        if (IsSetEquipped(setId))
            equipService.UnequipSet(setId);
        else
            equipService.EquipSet(setId);
    }

    private bool IsSetEquipped(int setId)
    {
        if (playerAccessory == null) return false;

        var registry = AccessoryRegistry.Instance;
        if (registry == null) return false;

        var accessoryIds = registry.GetAccessoryIdsInSet(setId);
        if (accessoryIds.Count == 0) return false;

        foreach (int accessoryId in accessoryIds)
        {
            AccessoryType type = registry.GetAccessoryTypeById(accessoryId);
            if (type == AccessoryType.None) continue;
            if (playerAccessory.GetEquippedItemId(type) != accessoryId)
                return false;
        }
        return true;
    }

    private void OnItemButtonClicked(int itemId, AccessoryType type)
    {
        var equipService = AccessoryEquipService.Instance
            ?? throw new System.InvalidOperationException(
                "InventoryPanel: AccessoryEquipService not initialized");

        if (equipService.IsPreviewActive)
            equipService.ClearPreview();

        bool isEquipped = playerAccessory != null && playerAccessory.GetEquippedItemId(type) == itemId;

        if (isEquipped)
            equipService.UnequipItem(type);
        else
            equipService.EquipItem(itemId, type);
    }

    private void RefreshAllButtons()
    {
        foreach (var row in inventoryItemRows)
            UpdateRowButton(row);
        foreach (var row in setRows)
            UpdateSetRowButton(row);
    }

    private void UpdateRowButton(InventoryItemRow row)
    {
        bool isEquipped = playerAccessory != null
            && playerAccessory.GetEquippedItemId(row.shopItem.accessoryType) == row.shopItem.itemId;

        var state = ShopButtonStateResolver.ResolveInventoryItemState(isEquipped);
        row.elements.buttonText.text = state.text;
        row.elements.buttonImage.color = state.color;
        row.elements.actionButton.interactable = state.isInteractable;
    }

    private void UpdateSetRowButton(InventorySetRow row)
    {
        row.elements.actionButton.onClick.RemoveAllListeners();
        int capturedSetId = row.setData.setId;

        bool isEquipped = IsSetEquipped(row.setData.setId);

        var state = ShopButtonStateResolver.ResolveInventorySetState(isEquipped);
        row.elements.buttonText.text = state.text;
        row.elements.buttonImage.color = state.color;

        row.elements.actionButton.interactable = true;
        row.elements.actionButton.onClick.AddListener(
            () => OnSetEquipClicked(capturedSetId));
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    private struct InventoryItemRow
    {
        public ShopAccessoryItem shopItem;
        public ItemRowElements elements;
    }

    private struct InventorySetRow
    {
        public AccessorySetData setData;
        public ItemRowElements elements;
    }
}
```

- [ ] **Step 2: Verify compilation**

Run: `mcp: refresh_unity` then `mcp: read_console`
Expected: zero errors

- [ ] **Step 3: Run all tests**

Run: `mcp: run_tests with testMode=EditMode`
Expected: all tests pass (including InventoryFilterTests which uses `InventoryPanel.FilterOwnedItems`)

---

### Task 7: Final Verification

**Files:** None (verification only)

- [ ] **Step 1: Run full EditMode test suite**

Run: `mcp: run_tests with testMode=EditMode`
Expected: all tests pass

- [ ] **Step 2: Verify no compilation warnings related to refactored files**

Run: `mcp: refresh_unity` then `mcp: read_console`
Expected: zero errors, no new warnings from refactored files

- [ ] **Step 3: Spot-check line counts**

Verify the refactored panels are smaller than originals:
- ShopPanel: was 458 lines, target ~220
- SetsPanel: was 473 lines, target ~220
- InventoryPanel: was 517 lines, target ~250
- ShopUIFactory: ~250 lines of extracted UI construction
- ShopDisplayFormatter: ~30 lines
- ShopButtonStateResolver: ~65 lines
