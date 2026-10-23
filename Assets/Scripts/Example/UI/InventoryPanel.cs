using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class InventoryPanel : MonoBehaviour
{
    [SerializeField] private Transform contentContainer;
    [SerializeField] private TMP_Text statusText;

    private List<InventoryItemRow> inventoryItemRows = new();
    private List<InventorySetRow> setRows = new();
    private bool isPopulated = false;
    private PlayerAccessory playerAccessory;

    private void OnEnable()
    {
        if (!isPopulated)
            StartCoroutine(WaitAndPopulate());
        else
        {
            ResolvePlayerAccessory();
            SubscribeEvents();
            RefreshInventory();
        }
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        UnsubscribeEvents();
        SetStatus("");
    }

    private void ResolvePlayerAccessory()
    {
        var avatar = ClientPlayerAvatars.Instance?.currentClientAvatar;
        if (avatar == null) return;

        var newPlayerAccessory = avatar.GetComponent<PlayerAccessory>();
        if (newPlayerAccessory != playerAccessory)
        {
            UnsubscribeEvents();
            playerAccessory = newPlayerAccessory;
        }
    }

    private void SubscribeEvents()
    {
        if (playerAccessory != null)
            playerAccessory.EquippedStateChanged += RefreshAllButtons;

        if (AccessoryRegistry.Instance != null)
            AccessoryRegistry.Instance.AccessoryOwnershipChanged += RefreshInventory;
    }

    private void UnsubscribeEvents()
    {
        if (playerAccessory != null)
            playerAccessory.EquippedStateChanged -= RefreshAllButtons;

        if (AccessoryRegistry.Instance != null)
            AccessoryRegistry.Instance.AccessoryOwnershipChanged -= RefreshInventory;
    }

    private IEnumerator WaitAndPopulate()
    {
        while (AccessoryRegistry.Instance == null || !AccessoryRegistry.Instance.IsShopCatalogLoaded())
            yield return null;

        while (ClientPlayerAvatars.Instance == null || ClientPlayerAvatars.Instance.currentClientAvatar == null)
            yield return null;

        if (!gameObject.activeInHierarchy)
            yield break;

        var avatar = ClientPlayerAvatars.Instance.currentClientAvatar;
        playerAccessory = avatar.GetComponent<PlayerAccessory>()
            ?? throw new System.InvalidOperationException(
                "InventoryPanel: PlayerAccessory component not found on avatar");

        SubscribeEvents();
        PopulateInventory();
        isPopulated = true;

        yield return null;
        if (contentContainer != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentContainer.GetComponent<RectTransform>());
        Canvas.ForceUpdateCanvases();
        yield return null;
        var scrollRect = GetComponent<ScrollRect>();
        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 1f;
    }

    private void OnDestroy()
    {
        UnsubscribeEvents();

        foreach (var row in inventoryItemRows)
        {
            if (row.elements.actionButton != null)
                row.elements.actionButton.onClick.RemoveAllListeners();
        }
        foreach (var row in setRows)
        {
            if (row.elements.actionButton != null)
                row.elements.actionButton.onClick.RemoveAllListeners();
        }
    }

    private void RefreshInventory()
    {
        if (AccessoryRegistry.Instance == null || !AccessoryRegistry.Instance.IsShopCatalogLoaded())
            return;

        ClearRows();
        PopulateInventory();
    }

    private void ClearRows()
    {
        foreach (var row in inventoryItemRows)
        {
            if (row.elements.actionButton != null)
                row.elements.actionButton.onClick.RemoveAllListeners();
        }
        inventoryItemRows.Clear();

        foreach (var row in setRows)
        {
            if (row.elements.actionButton != null)
                row.elements.actionButton.onClick.RemoveAllListeners();
        }
        setRows.Clear();

        if (contentContainer != null)
        {
            for (int i = contentContainer.childCount - 1; i >= 0; i--)
                Destroy(contentContainer.GetChild(i).gameObject);
        }
    }

    private void PopulateInventory()
    {
        bool hasAnyContent = false;

        var ownedSets = GetOwnedSets();
        if (ownedSets.Count > 0)
        {
            hasAnyContent = true;
            ShopUIFactory.CreateSectionHeader("Outfits", contentContainer);

            foreach (var setData in ownedSets)
            {
                var elements = ShopUIFactory.CreateInventorySetRow(
                    setData.setName, contentContainer);
                elements.rootObject.name = $"Set_{setData.setId}";

                var row = new InventorySetRow { setData = setData, elements = elements };
                setRows.Add(row);
                UpdateSetRowButton(row);
            }
        }

        AccessoryType[] types = {
            AccessoryType.Headwear, AccessoryType.Hair, AccessoryType.Facewear,
            AccessoryType.MainHand, AccessoryType.OffHand, AccessoryType.Back
        };

        foreach (var type in types)
        {
            var allItems = AccessoryRegistry.Instance.GetShopItemsForType(type);
            var ownedItems = FilterOwnedItems(allItems);
            if (ownedItems.Count == 0) continue;

            hasAnyContent = true;
            ShopUIFactory.CreateSectionHeader(
                ShopDisplayFormatter.GetDisplayName(type), contentContainer);

            foreach (var ownedItem in ownedItems)
            {
                var elements = ShopUIFactory.CreateInventoryItemRow(
                    ShopDisplayFormatter.GetItemDisplayName(ownedItem), contentContainer);
                elements.rootObject.name = $"Item_{ownedItem.itemId}";

                var row = new InventoryItemRow { shopItem = ownedItem, elements = elements };
                inventoryItemRows.Add(row);

                int capturedItemId = ownedItem.itemId;
                AccessoryType capturedAccessoryType = ownedItem.accessoryType;
                elements.actionButton.onClick.AddListener(
                    () => OnItemButtonClicked(capturedItemId, capturedAccessoryType));

                UpdateRowButton(row);
            }
        }

        if (!hasAnyContent)
            ShopUIFactory.CreateEmptyStateMessage(
                "No items owned yet.\nVisit the Shop to buy accessories!", contentContainer);
    }

    private List<AccessorySetData> GetOwnedSets()
    {
        var registry = AccessoryRegistry.Instance;
        if (registry == null) return new List<AccessorySetData>();

        var allSets = registry.GetAllSets();
        var ownedSets = new List<AccessorySetData>();
        foreach (var setData in allSets)
        {
            if (setData.isOwned)
                ownedSets.Add(setData);
        }
        return ownedSets;
    }

    public static List<ShopAccessoryItem> FilterOwnedItems(List<ShopAccessoryItem> items)
    {
        var ownedItems = new List<ShopAccessoryItem>();
        foreach (var item in items)
        {
            if (item.isOwned)
                ownedItems.Add(item);
        }
        return ownedItems;
    }

    private void OnSetEquipClicked(int setId)
    {
        var equipService = AccessoryEquipService.Instance
            ?? throw new System.InvalidOperationException(
                "InventoryPanel: AccessoryEquipService not initialized");

        if (equipService.IsPreviewActive)
            equipService.ClearPreview();

        if (IsSetEquipped(setId))
            equipService.UnequipSet(setId);
        else
            equipService.EquipSet(setId);
    }

    private bool IsSetEquipped(int setId)
    {
        if (playerAccessory == null) return false;

        var registry = AccessoryRegistry.Instance;
        if (registry == null) return false;

        var accessoryIds = registry.GetAccessoryIdsInSet(setId);
        if (accessoryIds.Count == 0) return false;

        foreach (int accessoryId in accessoryIds)
        {
            AccessoryType type = registry.GetAccessoryTypeById(accessoryId);
            if (type == AccessoryType.None) continue;
            if (playerAccessory.GetEquippedItemId(type) != accessoryId)
                return false;
        }
        return true;
    }

    private void OnItemButtonClicked(int itemId, AccessoryType type)
    {
        var equipService = AccessoryEquipService.Instance
            ?? throw new System.InvalidOperationException(
                "InventoryPanel: AccessoryEquipService not initialized");

        if (equipService.IsPreviewActive)
            equipService.ClearPreview();

        bool isEquipped = playerAccessory != null && playerAccessory.GetEquippedItemId(type) == itemId;

        if (isEquipped)
            equipService.UnequipItem(type);
        else
            equipService.EquipItem(itemId, type);
    }

    private void RefreshAllButtons()
    {
        foreach (var row in inventoryItemRows)
            UpdateRowButton(row);
        foreach (var row in setRows)
            UpdateSetRowButton(row);
    }

    private void UpdateRowButton(InventoryItemRow row)
    {
        bool isEquipped = playerAccessory != null
            && playerAccessory.GetEquippedItemId(row.shopItem.accessoryType) == row.shopItem.itemId;

        var state = ShopButtonStateResolver.ResolveInventoryItemState(isEquipped);
        row.elements.buttonText.text = state.text;
        row.elements.buttonImage.color = state.color;
        row.elements.actionButton.interactable = state.isInteractable;
    }

    private void UpdateSetRowButton(InventorySetRow row)
    {
        row.elements.actionButton.onClick.RemoveAllListeners();
        int capturedSetId = row.setData.setId;

        bool isEquipped = IsSetEquipped(row.setData.setId);

        var state = ShopButtonStateResolver.ResolveInventorySetState(isEquipped);
        row.elements.buttonText.text = state.text;
        row.elements.buttonImage.color = state.color;

        row.elements.actionButton.interactable = true;
        row.elements.actionButton.onClick.AddListener(
            () => OnSetEquipClicked(capturedSetId));
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    private struct InventoryItemRow
    {
        public ShopAccessoryItem shopItem;
        public ItemRowElements elements;
    }

    private struct InventorySetRow
    {
        public AccessorySetData setData;
        public ItemRowElements elements;
    }
}
