using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public class AccessoryLoadout
{
    private static readonly Dictionary<AccessoryType, AccessoryType> conflictingSlots = new()
    {
        { AccessoryType.Headwear, AccessoryType.Hair },
        { AccessoryType.Hair, AccessoryType.Headwear },
    };

    private readonly Dictionary<AccessoryType, int> equippedItemsBySlot = new();

    public IReadOnlyDictionary<AccessoryType, int> EquippedItems => equippedItemsBySlot;

    public static AccessoryType GetConflictingSlot(AccessoryType slot)
    {
        return conflictingSlots.TryGetValue(slot, out AccessoryType conflicting) ? conflicting : AccessoryType.None;
    }

    public void Equip(AccessoryType type, int itemId)
    {
        if (type == AccessoryType.None)
            throw new ArgumentException("Cannot equip to slot None", nameof(type));
        equippedItemsBySlot[type] = itemId;
    }

    public bool Unequip(AccessoryType type)
    {
        return equippedItemsBySlot.Remove(type);
    }

    public int GetEquippedItemId(AccessoryType type)
    {
        return equippedItemsBySlot.TryGetValue(type, out int itemId) ? itemId : -1;
    }

    public bool IsSlotOccupied(AccessoryType type)
    {
        return equippedItemsBySlot.ContainsKey(type);
    }

    public void ResolveConflicts(AccessoryType equippedType)
    {
        if (conflictingSlots.TryGetValue(equippedType, out AccessoryType conflicting))
            equippedItemsBySlot.Remove(conflicting);
    }

    public void Clear()
    {
        equippedItemsBySlot.Clear();
    }

    public string BuildAccessoryJson()
    {
        var state = new Dictionary<string, int>();
        foreach (var equippedEntry in equippedItemsBySlot)
            state[equippedEntry.Key.ToString()] = equippedEntry.Value;
        return JsonConvert.SerializeObject(state);
    }

    public static string SerializeAccessoryState(Dictionary<AccessoryType, int> state)
    {
        if (state == null || state.Count == 0) return "";
        var dict = new Dictionary<string, int>();
        foreach (var slotEntry in state)
            dict[slotEntry.Key.ToString()] = slotEntry.Value;
        return Newtonsoft.Json.JsonConvert.SerializeObject(dict);
    }

    public static Dictionary<AccessoryType, int> DeserializeAccessoryState(string json)
    {
        if (string.IsNullOrEmpty(json))
            return new Dictionary<AccessoryType, int>();

        Dictionary<string, int> rawMap;
        try
        {
            rawMap = JsonConvert.DeserializeObject<Dictionary<string, int>>(json)
                ?? throw new InvalidOperationException(
                    $"AccessoryLoadout: deserialized to null from: {json}");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"AccessoryLoadout: failed to parse JSON: {json}", exception);
        }

        var result = new Dictionary<AccessoryType, int>();
        foreach (var slotEntry in rawMap)
        {
            if (Enum.TryParse(slotEntry.Key, out AccessoryType type))
                result[type] = slotEntry.Value;
        }
        return result;
    }

    public void LoadFromItemIds(List<int> itemIds, Func<int, AccessoryType> typeResolver)
    {
        if (typeResolver == null)
            throw new ArgumentNullException(nameof(typeResolver));

        equippedItemsBySlot.Clear();
        if (itemIds == null) return;

        foreach (int itemId in itemIds)
        {
            AccessoryType type = typeResolver(itemId);
            if (type != AccessoryType.None)
                equippedItemsBySlot[type] = itemId;
        }
    }
}
