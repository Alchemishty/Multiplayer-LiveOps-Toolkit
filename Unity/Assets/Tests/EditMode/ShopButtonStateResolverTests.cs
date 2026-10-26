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
