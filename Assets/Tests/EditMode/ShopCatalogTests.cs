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
        var lookup = CreateLookup();
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

    [Test]
    public void MarkSetOwned_ConstituentAccessoriesCanBeMarkedOwned()
    {
        var catalog = new ShopCatalog();
        var lookup = CreateLookup(
            CreateItemData(1, AccessoryType.Headwear),
            CreateItemData(2, AccessoryType.MainHand));

        catalog.BuildFromBackendResponse(
            new AccessoriesResponse
            {
                accessories = new Dictionary<string, List<Accessory>>
                {
                    { "headwear", new List<Accessory> { CreateShopAccessory(1, "Hat", false) } },
                    { "mainhand", new List<Accessory> { CreateShopAccessory(2, "Sword", false) } },
                }
            },
            lookup);

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
                            costs = new List<AccessoryCost>()
                        }
                    }
                }
            }
        };

        catalog.BuildSetsFromBackendResponse(setsResponse, lookup);

        catalog.MarkSetOwned(10);
        catalog.MarkOwned(1);
        catalog.MarkOwned(2);

        Assert.IsTrue(catalog.IsSetOwned(10));
        Assert.IsTrue(catalog.IsOwned(1));
        Assert.IsTrue(catalog.IsOwned(2));
    }
}
