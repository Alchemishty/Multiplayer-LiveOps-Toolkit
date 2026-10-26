using UnityEngine;

public struct ButtonDisplayState
{
    public string text;
    public Color color;
    public bool isInteractable;
    public bool showCost;
}

public static class ShopButtonStateResolver
{
    public static ButtonDisplayState ResolveShopItemState(
        bool isOwned, bool isPurchasing, bool isPreviewing)
    {
        if (isOwned)
            return new ButtonDisplayState
            {
                text = "Owned",
                color = new Color(0.3f, 0.3f, 0.35f),
                isInteractable = false,
                showCost = false,
            };

        if (isPurchasing)
            return new ButtonDisplayState
            {
                text = "...",
                color = new Color(0.3f, 0.3f, 0.35f),
                isInteractable = false,
                showCost = true,
            };

        if (isPreviewing)
            return new ButtonDisplayState
            {
                text = "Buy",
                color = new Color(0.6f, 0.3f, 0.1f),
                isInteractable = true,
                showCost = true,
            };

        return new ButtonDisplayState
        {
            text = "Preview",
            color = new Color(0.2f, 0.4f, 0.6f),
            isInteractable = true,
            showCost = true,
        };
    }

    public static ButtonDisplayState ResolveShopSetState(
        bool isOwned, bool isPurchasing, bool isPreviewing)
    {
        return ResolveShopItemState(isOwned, isPurchasing, isPreviewing);
    }

    public static ButtonDisplayState ResolveInventoryItemState(bool isEquipped)
    {
        if (isEquipped)
            return new ButtonDisplayState
            {
                text = "Unequip",
                color = new Color(0.5f, 0.2f, 0.2f),
                isInteractable = true,
                showCost = false,
            };

        return new ButtonDisplayState
        {
            text = "Equip",
            color = new Color(0.2f, 0.6f, 0.3f),
            isInteractable = true,
            showCost = false,
        };
    }

    public static ButtonDisplayState ResolveInventorySetState(bool isEquipped)
    {
        return ResolveInventoryItemState(isEquipped);
    }
}
