using System.Collections.Generic;
using NUnit.Framework;

[TestFixture]
public class InventoryFilterTests
{
    private ShopAccessoryItem CreateItem(int itemId, AccessoryType type, bool isOwned, string displayName = "Test Item")
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
            name = displayName,
            isOwned = isOwned,
            costs = new List<AccessoryCost>()
        };

        return new ShopAccessoryItem(masterData, shopData);
    }

    [Test]
    public void FilterOwnedItems_WithMixedOwnership_ReturnsOnlyOwned()
    {
        var items = new List<ShopAccessoryItem>
        {
            CreateItem(1, AccessoryType.Headwear, isOwned: true),
            CreateItem(2, AccessoryType.Headwear, isOwned: false),
            CreateItem(3, AccessoryType.Headwear, isOwned: true),
        };

        var ownedItems = InventoryPanel.FilterOwnedItems(items);

        Assert.AreEqual(2, ownedItems.Count);
        Assert.AreEqual(1, ownedItems[0].itemId);
        Assert.AreEqual(3, ownedItems[1].itemId);
    }

    [Test]
    public void FilterOwnedItems_WithNoOwnedItems_ReturnsEmpty()
    {
        var items = new List<ShopAccessoryItem>
        {
            CreateItem(1, AccessoryType.Headwear, isOwned: false),
            CreateItem(2, AccessoryType.Headwear, isOwned: false),
        };

        var ownedItems = InventoryPanel.FilterOwnedItems(items);

        Assert.AreEqual(0, ownedItems.Count);
    }

    [Test]
    public void FilterOwnedItems_WithAllOwned_ReturnsAll()
    {
        var items = new List<ShopAccessoryItem>
        {
            CreateItem(1, AccessoryType.Hair, isOwned: true),
            CreateItem(2, AccessoryType.Hair, isOwned: true),
        };

        var ownedItems = InventoryPanel.FilterOwnedItems(items);

        Assert.AreEqual(2, ownedItems.Count);
    }

    [Test]
    public void FilterOwnedItems_WithEmptyList_ReturnsEmpty()
    {
        var items = new List<ShopAccessoryItem>();

        var ownedItems = InventoryPanel.FilterOwnedItems(items);

        Assert.AreEqual(0, ownedItems.Count);
    }
}
