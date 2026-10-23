using System;
using System.Collections.Generic;
using NUnit.Framework;

[TestFixture]
public class AccessoryLoadoutTests
{
    private AccessoryLoadout CreateLoadout()
    {
        return new AccessoryLoadout();
    }

    // --- Equip ---

    [Test]
    public void Equip_ValidSlot_StoresItemId()
    {
        var loadout = CreateLoadout();

        loadout.Equip(AccessoryType.Headwear, 42);

        Assert.AreEqual(42, loadout.GetEquippedItemId(AccessoryType.Headwear));
    }

    [Test]
    public void Equip_SameSlotTwice_OverwritesPrevious()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Headwear, 42);

        loadout.Equip(AccessoryType.Headwear, 99);

        Assert.AreEqual(99, loadout.GetEquippedItemId(AccessoryType.Headwear));
    }

    [Test]
    public void Equip_NoneSlot_ThrowsArgumentException()
    {
        var loadout = CreateLoadout();

        Assert.Throws<ArgumentException>(() => loadout.Equip(AccessoryType.None, 1));
    }

    [Test]
    public void Equip_MultipleDifferentSlots_AllStored()
    {
        var loadout = CreateLoadout();

        loadout.Equip(AccessoryType.Headwear, 1);
        loadout.Equip(AccessoryType.MainHand, 2);
        loadout.Equip(AccessoryType.Back, 3);

        Assert.AreEqual(1, loadout.GetEquippedItemId(AccessoryType.Headwear));
        Assert.AreEqual(2, loadout.GetEquippedItemId(AccessoryType.MainHand));
        Assert.AreEqual(3, loadout.GetEquippedItemId(AccessoryType.Back));
    }

    // --- Unequip ---

    [Test]
    public void Unequip_OccupiedSlot_ReturnsTrueAndRemoves()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Hair, 5);

        bool result = loadout.Unequip(AccessoryType.Hair);

        Assert.IsTrue(result);
        Assert.AreEqual(-1, loadout.GetEquippedItemId(AccessoryType.Hair));
    }

    [Test]
    public void Unequip_EmptySlot_ReturnsFalse()
    {
        var loadout = CreateLoadout();

        bool result = loadout.Unequip(AccessoryType.Hair);

        Assert.IsFalse(result);
    }

    // --- GetEquippedItemId ---

    [Test]
    public void GetEquippedItemId_EmptySlot_ReturnsNegativeOne()
    {
        var loadout = CreateLoadout();

        Assert.AreEqual(-1, loadout.GetEquippedItemId(AccessoryType.Headwear));
    }

    // --- IsSlotOccupied ---

    [Test]
    public void IsSlotOccupied_OccupiedSlot_ReturnsTrue()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Headwear, 1);

        Assert.IsTrue(loadout.IsSlotOccupied(AccessoryType.Headwear));
    }

    [Test]
    public void IsSlotOccupied_EmptySlot_ReturnsFalse()
    {
        var loadout = CreateLoadout();

        Assert.IsFalse(loadout.IsSlotOccupied(AccessoryType.Headwear));
    }

    // --- ResolveConflicts ---

    [Test]
    public void ResolveConflicts_HeadwearRemovesHair()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Hair, 10);

        loadout.Equip(AccessoryType.Headwear, 20);
        loadout.ResolveConflicts(AccessoryType.Headwear);

        Assert.AreEqual(20, loadout.GetEquippedItemId(AccessoryType.Headwear));
        Assert.AreEqual(-1, loadout.GetEquippedItemId(AccessoryType.Hair));
    }

    [Test]
    public void ResolveConflicts_HairRemovesHeadwear()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Headwear, 20);

        loadout.Equip(AccessoryType.Hair, 10);
        loadout.ResolveConflicts(AccessoryType.Hair);

        Assert.AreEqual(10, loadout.GetEquippedItemId(AccessoryType.Hair));
        Assert.AreEqual(-1, loadout.GetEquippedItemId(AccessoryType.Headwear));
    }

    [Test]
    public void ResolveConflicts_NonConflictingSlot_NoEffect()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.MainHand, 5);
        loadout.Equip(AccessoryType.Back, 6);

        loadout.ResolveConflicts(AccessoryType.MainHand);

        Assert.AreEqual(5, loadout.GetEquippedItemId(AccessoryType.MainHand));
        Assert.AreEqual(6, loadout.GetEquippedItemId(AccessoryType.Back));
    }

    [Test]
    public void ResolveConflicts_ConflictSlotEmpty_NoError()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Headwear, 20);

        loadout.ResolveConflicts(AccessoryType.Headwear);

        Assert.AreEqual(20, loadout.GetEquippedItemId(AccessoryType.Headwear));
    }

    // --- BuildAccessoryJson ---

    [Test]
    public void BuildAccessoryJson_EmptyLoadout_ReturnsEmptyObject()
    {
        var loadout = CreateLoadout();

        string json = loadout.BuildAccessoryJson();

        Assert.AreEqual("{}", json);
    }

    [Test]
    public void BuildAccessoryJson_SingleItem_ReturnsValidJson()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Headwear, 42);

        string json = loadout.BuildAccessoryJson();

        Assert.IsTrue(json.Contains("\"Headwear\""));
        Assert.IsTrue(json.Contains("42"));
    }

    [Test]
    public void BuildAccessoryJson_MultipleItems_AllPresent()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Headwear, 1);
        loadout.Equip(AccessoryType.MainHand, 2);

        string json = loadout.BuildAccessoryJson();

        Assert.IsTrue(json.Contains("Headwear"));
        Assert.IsTrue(json.Contains("MainHand"));
    }

    // --- SerializeAccessoryState ---

    [Test]
    public void SerializeAccessoryState_NullDictionary_ReturnsEmptyString()
    {
        string result = AccessoryLoadout.SerializeAccessoryState(null);

        Assert.AreEqual("", result);
    }

    [Test]
    public void SerializeAccessoryState_EmptyDictionary_ReturnsEmptyString()
    {
        var emptyState = new Dictionary<AccessoryType, int>();

        string result = AccessoryLoadout.SerializeAccessoryState(emptyState);

        Assert.AreEqual("", result);
    }

    [Test]
    public void SerializeAccessoryState_SingleEntry_ReturnsValidJson()
    {
        var state = new Dictionary<AccessoryType, int>
        {
            { AccessoryType.Headwear, 1 }
        };

        string result = AccessoryLoadout.SerializeAccessoryState(state);

        Assert.IsTrue(result.Contains("\"Headwear\""));
        Assert.IsTrue(result.Contains("1"));
    }

    [Test]
    public void SerializeAccessoryState_MultipleEntries_ReturnsValidJson()
    {
        var state = new Dictionary<AccessoryType, int>
        {
            { AccessoryType.Headwear, 1 },
            { AccessoryType.MainHand, 20 },
            { AccessoryType.Back, 26 }
        };

        string result = AccessoryLoadout.SerializeAccessoryState(state);

        Assert.IsTrue(result.Contains("Headwear"));
        Assert.IsTrue(result.Contains("MainHand"));
        Assert.IsTrue(result.Contains("Back"));
    }

    [Test]
    public void SerializeAccessoryState_RoundTrip_SingleEntry_PreservesData()
    {
        var state = new Dictionary<AccessoryType, int>
        {
            { AccessoryType.Headwear, 1 }
        };

        string json = AccessoryLoadout.SerializeAccessoryState(state);
        var deserialized = AccessoryLoadout.DeserializeAccessoryState(json);

        Assert.AreEqual(1, deserialized.Count);
        Assert.AreEqual(1, deserialized[AccessoryType.Headwear]);
    }

    [Test]
    public void SerializeAccessoryState_RoundTrip_MultipleEntries_PreservesData()
    {
        var state = new Dictionary<AccessoryType, int>
        {
            { AccessoryType.Headwear, 1 },
            { AccessoryType.MainHand, 20 },
            { AccessoryType.Back, 26 }
        };

        string json = AccessoryLoadout.SerializeAccessoryState(state);
        var deserialized = AccessoryLoadout.DeserializeAccessoryState(json);

        Assert.AreEqual(3, deserialized.Count);
        Assert.AreEqual(1, deserialized[AccessoryType.Headwear]);
        Assert.AreEqual(20, deserialized[AccessoryType.MainHand]);
        Assert.AreEqual(26, deserialized[AccessoryType.Back]);
    }

    [Test]
    public void SerializeAccessoryState_RoundTrip_EmptyState_ReturnsEmptyDict()
    {
        var emptyState = new Dictionary<AccessoryType, int>();

        string json = AccessoryLoadout.SerializeAccessoryState(emptyState);
        var deserialized = AccessoryLoadout.DeserializeAccessoryState(json);

        Assert.AreEqual(0, deserialized.Count);
    }

    // --- DeserializeAccessoryState ---

    [Test]
    public void DeserializeAccessoryState_ValidJson_ReturnsCorrectMap()
    {
        var loadout = CreateLoadout();

        var result = AccessoryLoadout.DeserializeAccessoryState("{\"Headwear\":1,\"MainHand\":20}");

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(1, result[AccessoryType.Headwear]);
        Assert.AreEqual(20, result[AccessoryType.MainHand]);
    }

    [Test]
    public void DeserializeAccessoryState_EmptyString_ReturnsEmptyDict()
    {
        var loadout = CreateLoadout();

        var result = AccessoryLoadout.DeserializeAccessoryState("");

        Assert.AreEqual(0, result.Count);
    }

    [Test]
    public void DeserializeAccessoryState_NullString_ReturnsEmptyDict()
    {
        var loadout = CreateLoadout();

        var result = AccessoryLoadout.DeserializeAccessoryState(null);

        Assert.AreEqual(0, result.Count);
    }

    [Test]
    public void DeserializeAccessoryState_UnknownKeys_SkipsGracefully()
    {
        var loadout = CreateLoadout();

        var result = AccessoryLoadout.DeserializeAccessoryState("{\"Headwear\":1,\"FutureSlot\":99}");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(1, result[AccessoryType.Headwear]);
    }

    [Test]
    public void DeserializeAccessoryState_MalformedJson_ThrowsInvalidOperationException()
    {
        var loadout = CreateLoadout();

        Assert.Throws<InvalidOperationException>(
            () => AccessoryLoadout.DeserializeAccessoryState("{not valid json}"));
    }

    // --- Round-trip ---

    [Test]
    public void BuildAndDeserialize_RoundTrip_PreservesState()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Headwear, 1);
        loadout.Equip(AccessoryType.MainHand, 20);
        loadout.Equip(AccessoryType.Back, 5);

        string json = loadout.BuildAccessoryJson();
        var restored = AccessoryLoadout.DeserializeAccessoryState(json);

        Assert.AreEqual(3, restored.Count);
        Assert.AreEqual(1, restored[AccessoryType.Headwear]);
        Assert.AreEqual(20, restored[AccessoryType.MainHand]);
        Assert.AreEqual(5, restored[AccessoryType.Back]);
    }

    // --- LoadFromItemIds ---

    [Test]
    public void LoadFromItemIds_ValidIds_PopulatesSlots()
    {
        var loadout = CreateLoadout();
        var typeMap = new Dictionary<int, AccessoryType>
        {
            { 1, AccessoryType.Headwear },
            { 2, AccessoryType.MainHand },
        };

        loadout.LoadFromItemIds(
            new List<int> { 1, 2 },
            id => typeMap.TryGetValue(id, out var type) ? type : AccessoryType.None);

        Assert.AreEqual(1, loadout.GetEquippedItemId(AccessoryType.Headwear));
        Assert.AreEqual(2, loadout.GetEquippedItemId(AccessoryType.MainHand));
    }

    [Test]
    public void LoadFromItemIds_NullResolver_ThrowsArgumentNullException()
    {
        var loadout = CreateLoadout();

        Assert.Throws<ArgumentNullException>(
            () => loadout.LoadFromItemIds(new List<int> { 1 }, null));
    }

    [Test]
    public void LoadFromItemIds_NullList_ClearsLoadout()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Headwear, 42);

        loadout.LoadFromItemIds(null, id => AccessoryType.None);

        Assert.AreEqual(-1, loadout.GetEquippedItemId(AccessoryType.Headwear));
    }

    [Test]
    public void LoadFromItemIds_UnknownTypeIds_Skipped()
    {
        var loadout = CreateLoadout();

        loadout.LoadFromItemIds(
            new List<int> { 1, 2, 3 },
            id => id == 2 ? AccessoryType.Hair : AccessoryType.None);

        Assert.AreEqual(-1, loadout.GetEquippedItemId(AccessoryType.Headwear));
        Assert.AreEqual(2, loadout.GetEquippedItemId(AccessoryType.Hair));
    }

    // --- Clear ---

    [Test]
    public void Clear_RemovesAllEquippedItems()
    {
        var loadout = CreateLoadout();
        loadout.Equip(AccessoryType.Headwear, 1);
        loadout.Equip(AccessoryType.MainHand, 2);

        loadout.Clear();

        Assert.AreEqual(-1, loadout.GetEquippedItemId(AccessoryType.Headwear));
        Assert.AreEqual(-1, loadout.GetEquippedItemId(AccessoryType.MainHand));
    }
}
