using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Extends AccessoryItemData with shop-specific data (ownership, pricing)
/// by merging the local master catalog with the backend shop response.
/// </summary>
[System.Serializable]
public class ShopAccessoryItem : AccessoryItemData
{
    public string displayName;
    public bool isOwned;
    public List<AccessoryCost> costs;

    public ShopAccessoryItem(AccessoryItemData masterData, Accessory shopData)
    {
        this.itemId = masterData.itemId;
        this.accessoryType = masterData.accessoryType;
        this.prefab = masterData.prefab;
        this.displayName = shopData.name;
        this.isOwned = shopData.isOwned;
        this.costs = shopData.costs;
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

/// <summary>
/// Runtime shop catalog built by merging backend response with the local
/// AccessoryLookup master catalog. All shop/accessory systems reference this
/// for pricing, ownership, and availability queries.
/// </summary>
public class ShopCatalog
{
    private Dictionary<int, ShopAccessoryItem> itemsById = new();
    private Dictionary<AccessoryType, List<ShopAccessoryItem>> itemsByType = new();
    private Dictionary<int, AccessorySetData> setsById = new();
    private List<AccessorySetData> availableSets = new();

    public void BuildFromBackendResponse(AccessoriesResponse response, AccessoryLookup masterCatalog)
    {
        if (masterCatalog == null)
            throw new System.ArgumentNullException(nameof(masterCatalog));

        itemsById.Clear();
        itemsByType.Clear();

        foreach (var category in response.accessories)
        {
            string categoryName = category.Key;
            List<Accessory> accessories = category.Value;

            if (!System.Enum.TryParse(categoryName, true, out AccessoryType type))
            {
                Debug.LogWarning($"Unknown category: {categoryName}");
                continue;
            }

            if (!itemsByType.ContainsKey(type))
                itemsByType[type] = new List<ShopAccessoryItem>();

            foreach (var shopAccessory in accessories)
            {
                AccessoryItemData masterData = masterCatalog.GetAccessoryItemDataById(shopAccessory.id);

                if (masterData != null)
                {
                    ShopAccessoryItem shopItem = new ShopAccessoryItem(masterData, shopAccessory);
                    itemsById[shopItem.itemId] = shopItem;
                    itemsByType[type].Add(shopItem);
                }
                // Items in the backend but not in the master catalog are silently skipped.
                // This is expected for body part items reserved for future set support.
            }
        }
    }

    public void BuildSetsFromBackendResponse(AccessorySetResponse setsResponse, AccessoryLookup masterCatalog)
    {
        if (masterCatalog == null)
            throw new System.ArgumentNullException(nameof(masterCatalog));

        setsById.Clear();
        availableSets.Clear();

        foreach (var category in setsResponse.sets)
        {
            foreach (var setResponse in category.Value)
            {
                bool areAllAccessoriesValid = true;
                foreach (int accessoryId in setResponse.accessoryIDs)
                {
                    if (masterCatalog.GetAccessoryItemDataById(accessoryId) == null)
                    {
                        areAllAccessoriesValid = false;
                        break;
                    }
                }

                if (areAllAccessoriesValid)
                {
                    AccessorySetData setData = new AccessorySetData
                    {
                        setId = setResponse.id,
                        setName = setResponse.name,
                        accessoryIDs = setResponse.accessoryIDs,
                        costs = setResponse.costs,
                        isOwned = setResponse.isOwned,
                    };

                    setsById[setData.setId] = setData;
                    availableSets.Add(setData);
                }
            }
        }
    }

    // --- Accessory queries ---

    public ShopAccessoryItem GetItemById(int id) => itemsById.GetValueOrDefault(id);

    public GameObject GetPrefabById(int id) => itemsById.GetValueOrDefault(id)?.prefab;

    public List<ShopAccessoryItem> GetItemsForType(AccessoryType type) =>
        itemsByType.GetValueOrDefault(type, new List<ShopAccessoryItem>());

    public GameObject[] GetPrefabsForType(AccessoryType type)
    {
        var items = GetItemsForType(type);
        GameObject[] prefabs = new GameObject[items.Count];
        for (int i = 0; i < items.Count; i++)
            prefabs[i] = items[i].prefab;
        return prefabs;
    }

    public bool IsOwned(int id) =>
        itemsById.TryGetValue(id, out var item) && item.isOwned;

    public bool TryGetGoldCost(int id, out int goldAmount)
    {
        goldAmount = 0;
        if (itemsById.TryGetValue(id, out var item) && item.costs != null)
        {
            goldAmount = item.GetCostForCurrency("gold");
            return true;
        }
        return false;
    }

    public void MarkOwned(int id)
    {
        if (itemsById.TryGetValue(id, out var item))
            item.isOwned = true;
    }

    // --- Set queries ---

    public AccessorySetData[] GetAllAvailableSets() => availableSets.ToArray();

    public AccessorySetData GetSetById(int setId) => setsById.GetValueOrDefault(setId);

    public bool IsSetOwned(int id) =>
        setsById.TryGetValue(id, out var set) && set.isOwned;

    public bool TryGetSetGoldCost(int id, out int goldAmount)
    {
        goldAmount = 0;
        if (setsById.TryGetValue(id, out var set) && set.costs != null)
        {
            goldAmount = set.GetCostForCurrency("gold");
            return true;
        }
        return false;
    }

    public void MarkSetOwned(int id)
    {
        if (setsById.TryGetValue(id, out var set))
            set.isOwned = true;
    }
}
