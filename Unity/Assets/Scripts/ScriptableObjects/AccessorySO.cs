using UnityEngine;

[CreateAssetMenu(fileName = "AccessorySO", menuName = "GameData/Accessory", order = 1)]
public class AccessorySO : ScriptableObject
{
    public AccessoryItemData[] accessories;

    private AccessoryLookup lookup;

    private void OnEnable()
    {
        lookup = null;
    }

    private AccessoryLookup GetLookup()
    {
        if (lookup == null)
            lookup = new AccessoryLookup(accessories);
        return lookup;
    }

    public void RefreshCache()
    {
        lookup = null;
        GetLookup();
    }

    public AccessoryLookup CreateLookup()
    {
        return new AccessoryLookup(accessories);
    }

    public AccessoryItemData GetAccessoryItemDataById(int accessoryId)
    {
        return GetLookup().GetAccessoryItemDataById(accessoryId);
    }

    public GameObject GetAccessoryPrefabById(int accessoryId)
    {
        var itemData = GetLookup().GetAccessoryItemDataById(accessoryId);
        return itemData?.prefab;
    }

    public int GetAccessoryIdByPrefab(GameObject prefab)
    {
        if (prefab == null) return -1;
        return GetLookup().GetAccessoryIdByPrefabName(prefab.name);
    }

    public AccessoryType GetAccessoryTypeById(int accessoryId)
    {
        return GetLookup().GetAccessoryTypeById(accessoryId);
    }
}
