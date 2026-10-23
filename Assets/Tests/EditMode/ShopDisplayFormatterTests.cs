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
