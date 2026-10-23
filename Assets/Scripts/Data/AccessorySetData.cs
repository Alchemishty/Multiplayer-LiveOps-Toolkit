using System.Collections.Generic;

/// <summary>
/// Runtime set data for shop UI and purchasing.
/// Backend determines which sets are available and their composition.
/// Player equipped state only tracks individual accessory IDs.
/// </summary>
[System.Serializable]
public class AccessorySetData
{
    public int setId;
    public string setName;
    public List<AccessoryCost> costs;
    public bool isOwned;
    public List<int> accessoryIDs;

    public bool ContainsAccessoryType(AccessoryType type, AccessoryLookup masterCatalog)
    {
        if (masterCatalog == null)
            throw new System.ArgumentNullException(nameof(masterCatalog));

        foreach (int id in accessoryIDs)
        {
            var itemData = masterCatalog.GetAccessoryItemDataById(id);
            if (itemData != null && itemData.accessoryType == type)
                return true;
        }
        return false;
    }

    public int GetCostForCurrency(string currency)
    {
        if (costs == null) return 0;
        foreach (var cost in costs)
        {
            if (cost.currency.ToLowerInvariant() == currency.ToLowerInvariant())
                return cost.amount;
        }
        return 0;
    }
}
