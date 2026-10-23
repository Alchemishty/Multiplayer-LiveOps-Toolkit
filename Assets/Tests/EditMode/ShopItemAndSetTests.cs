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
