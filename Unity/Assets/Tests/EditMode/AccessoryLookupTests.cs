using NUnit.Framework;

[TestFixture]
public class AccessoryLookupTests
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

    // --- GetAccessoryItemDataById ---

    [Test]
    public void GetAccessoryItemDataById_ExistingId_ReturnsItemData()
    {
        var item = CreateItemData(1, AccessoryType.Headwear);
        var lookup = CreateLookup(item);

        var result = lookup.GetAccessoryItemDataById(1);

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.itemId);
        Assert.AreEqual(AccessoryType.Headwear, result.accessoryType);
    }

    [Test]
    public void GetAccessoryItemDataById_MissingId_ReturnsNull()
    {
        var lookup = CreateLookup(CreateItemData(1, AccessoryType.Headwear));

        var result = lookup.GetAccessoryItemDataById(999);

        Assert.IsNull(result);
    }

    [Test]
    public void GetAccessoryItemDataById_EmptyArray_ReturnsNull()
    {
        var lookup = CreateLookup();

        var result = lookup.GetAccessoryItemDataById(1);

        Assert.IsNull(result);
    }

    [Test]
    public void Constructor_NullArray_CreatesEmptyLookup()
    {
        var lookup = new AccessoryLookup(null);

        Assert.IsNull(lookup.GetAccessoryItemDataById(1));
    }

    [Test]
    public void Constructor_ArrayWithNullEntries_SkipsNulls()
    {
        var item = CreateItemData(1, AccessoryType.Hair);
        var lookup = new AccessoryLookup(new AccessoryItemData[] { null, item, null });

        Assert.IsNotNull(lookup.GetAccessoryItemDataById(1));
        Assert.AreEqual(AccessoryType.Hair, lookup.GetAccessoryTypeById(1));
    }

    [Test]
    public void GetAccessoryItemDataById_MultipleItems_ReturnsCorrectOne()
    {
        var lookup = CreateLookup(
            CreateItemData(1, AccessoryType.Headwear),
            CreateItemData(2, AccessoryType.Hair),
            CreateItemData(3, AccessoryType.MainHand)
        );

        var result = lookup.GetAccessoryItemDataById(2);

        Assert.IsNotNull(result);
        Assert.AreEqual(AccessoryType.Hair, result.accessoryType);
    }

    // --- GetAccessoryTypeById ---

    [Test]
    public void GetAccessoryTypeById_ExistingId_ReturnsType()
    {
        var lookup = CreateLookup(CreateItemData(5, AccessoryType.Facewear));

        Assert.AreEqual(AccessoryType.Facewear, lookup.GetAccessoryTypeById(5));
    }

    [Test]
    public void GetAccessoryTypeById_MissingId_ReturnsNone()
    {
        var lookup = CreateLookup(CreateItemData(1, AccessoryType.Headwear));

        Assert.AreEqual(AccessoryType.None, lookup.GetAccessoryTypeById(999));
    }

    // --- GetAccessoryIdByPrefabName ---

    [Test]
    public void GetAccessoryIdByPrefabName_NullName_ReturnsNegativeOne()
    {
        var lookup = CreateLookup(CreateItemData(1, AccessoryType.Headwear));

        Assert.AreEqual(-1, lookup.GetAccessoryIdByPrefabName(null));
    }

    [Test]
    public void GetAccessoryIdByPrefabName_EmptyName_ReturnsNegativeOne()
    {
        var lookup = CreateLookup(CreateItemData(1, AccessoryType.Headwear));

        Assert.AreEqual(-1, lookup.GetAccessoryIdByPrefabName(""));
    }

    [Test]
    public void GetAccessoryIdByPrefabName_UnknownName_ReturnsNegativeOne()
    {
        var lookup = CreateLookup(CreateItemData(1, AccessoryType.Headwear));

        Assert.AreEqual(-1, lookup.GetAccessoryIdByPrefabName("NonExistent"));
    }

    [Test]
    public void GetAccessoryIdByPrefabName_CloneStripped_MatchesOriginal()
    {
        var lookup = CreateLookup(CreateItemData(1, AccessoryType.Headwear));

        Assert.AreEqual(-1, lookup.GetAccessoryIdByPrefabName("SomePrefab(Clone)"));
    }

    // --- DuplicateId behavior ---

    [Test]
    public void Constructor_DuplicateIds_LastOneWins()
    {
        var item1 = CreateItemData(1, AccessoryType.Headwear);
        var item2 = CreateItemData(1, AccessoryType.Hair);
        var lookup = CreateLookup(item1, item2);

        Assert.AreEqual(AccessoryType.Hair, lookup.GetAccessoryTypeById(1));
    }
}
