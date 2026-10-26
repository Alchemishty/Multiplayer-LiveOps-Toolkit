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

}
