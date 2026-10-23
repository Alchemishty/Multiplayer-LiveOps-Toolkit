using System.Collections.Generic;
using Newtonsoft.Json;

[System.Serializable]
public class AccessoryCost
{
    public string currency;
    public int amount;
}

[System.Serializable]
public class Accessory
{
    public int id;
    public string name;
    [JsonProperty("is_owned")]
    public bool isOwned;
    public List<AccessoryCost> costs;
}

[System.Serializable]
public class AccessoriesResponse
{
    public Dictionary<string, List<Accessory>> accessories;
}

[System.Serializable]
public class AccessorySet
{
    public int id;
    public string name;
    [JsonProperty("is_owned")]
    public bool isOwned;
    [JsonProperty("accessory_ids")]
    public List<int> accessoryIDs;
    public List<AccessoryCost> costs;
}

[System.Serializable]
public class AccessorySetResponse
{
    public Dictionary<string, List<AccessorySet>> sets;
}

[System.Serializable]
public class PurchaseSetRequest
{
    public int set_id;
}

[System.Serializable]
public class PurchaseSetResponse
{
    public List<int> accessory_ids;
}
