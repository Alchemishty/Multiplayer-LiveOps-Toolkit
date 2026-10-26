using System.Collections.Generic;

public static class ShopDisplayFormatter
{
    public static string FormatCosts(IReadOnlyDictionary<string, long> costs)
    {
        if (costs == null || costs.Count == 0) return "Free";
        var parts = new List<string>();
        foreach (var accessoryCost in costs)
            parts.Add($"{accessoryCost.Value} {accessoryCost.Key}");
        return string.Join(" + ", parts);
    }

    public static string GetDisplayName(AccessoryType type)
    {
        switch (type)
        {
            case AccessoryType.Headwear: return "Headwear";
            case AccessoryType.Hair:     return "Hair";
            case AccessoryType.Facewear: return "Facewear";
            case AccessoryType.MainHand: return "Main Hand";
            case AccessoryType.OffHand:  return "Off Hand";
            case AccessoryType.Back:     return "Back";
            default:                     return type.ToString();
        }
    }

}
