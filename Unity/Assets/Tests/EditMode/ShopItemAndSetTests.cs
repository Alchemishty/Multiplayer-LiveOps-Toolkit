using System.Collections.Generic;
using NUnit.Framework;

[TestFixture]
public class ShopItemAndSetTests
{

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

}
