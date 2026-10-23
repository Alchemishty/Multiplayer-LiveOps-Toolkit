using System.Collections.Generic;

public class AccessoryLookup
{
    private readonly Dictionary<int, AccessoryItemData> accessoryById;
    private readonly Dictionary<string, int> accessoryIdByPrefabName;

    public AccessoryLookup(AccessoryItemData[] accessories)
    {
        int count = (accessories != null) ? accessories.Length : 0;
        accessoryById = new Dictionary<int, AccessoryItemData>(count);
        accessoryIdByPrefabName = new Dictionary<string, int>(count);

        if (count > 0)
        {
            foreach (var accessory in accessories)
            {
                if (accessory == null) continue;
                accessoryById[accessory.itemId] = accessory;
                if (accessory.prefab != null)
                    accessoryIdByPrefabName[accessory.prefab.name] = accessory.itemId;
            }
        }
    }

    public AccessoryItemData GetAccessoryItemDataById(int accessoryId)
    {
        return accessoryById.GetValueOrDefault(accessoryId);
    }

    public int GetAccessoryIdByPrefabName(string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName)) return -1;
        string cleanName = prefabName.Replace("(Clone)", "");
        if (accessoryIdByPrefabName.TryGetValue(cleanName, out int id))
            return id;
        return -1;
    }

    public AccessoryType GetAccessoryTypeById(int accessoryId)
    {
        if (accessoryById.TryGetValue(accessoryId, out var accessory))
            return accessory.accessoryType;
        return AccessoryType.None;
    }
}
