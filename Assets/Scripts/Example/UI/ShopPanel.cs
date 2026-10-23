using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class ShopPanel : MonoBehaviour
{
    private const int INVENTORY_TAB_INDEX = 2;

    [SerializeField] private Transform contentContainer;
    [SerializeField] private TMP_Text statusText;

    private List<ShopItemRow> shopItemRows = new();
    private bool isPopulated = false;
    private PlayerAccessory playerAccessory;
    private bool isPurchasing = false;
    private PurchaseConfirmationPrompt activePrompt;

    private void OnEnable()
    {
        if (!isPopulated)
            StartCoroutine(WaitAndPopulate());
        else
        {
            ResolvePlayerAccessory();
            SubscribeEvents();
        }

        RefreshAllButtons();
        SetStatus("");
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        UnsubscribeEvents();

        if (activePrompt != null)
        {
            Destroy(activePrompt.gameObject);
            activePrompt = null;
        }

        if (AccessoryEquipService.Instance != null)
            AccessoryEquipService.Instance.ClearPreview();
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

        if (AccessoryEquipService.Instance != null)
            AccessoryEquipService.Instance.PreviewChanged += RefreshAllButtons;

        if (AccessoryRegistry.Instance != null)
        {
            AccessoryRegistry.Instance.ShopCatalogLoaded += OnCatalogReloaded;
            AccessoryRegistry.Instance.AccessoryOwnershipChanged += RefreshAllButtons;
        }
    }

    private void UnsubscribeEvents()
    {
        if (playerAccessory != null)
            playerAccessory.EquippedStateChanged -= RefreshAllButtons;

        if (AccessoryEquipService.Instance != null)
            AccessoryEquipService.Instance.PreviewChanged -= RefreshAllButtons;

        if (AccessoryRegistry.Instance != null)
        {
            AccessoryRegistry.Instance.ShopCatalogLoaded -= OnCatalogReloaded;
            AccessoryRegistry.Instance.AccessoryOwnershipChanged -= RefreshAllButtons;
        }
    }

    private void OnCatalogReloaded()
    {
        Invalidate();
    }

    private IEnumerator WaitAndPopulate()
    {
        while (AccessoryRegistry.Instance == null || !AccessoryRegistry.Instance.IsShopCatalogLoaded())
            yield return null;

        while (ClientPlayerAvatars.Instance == null || ClientPlayerAvatars.Instance.currentClientAvatar == null)
            yield return null;

        if (!gameObject.activeInHierarchy)
            yield break;

        ResolvePlayerAccessory();
        SubscribeEvents();
        PopulateShop();
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

    public void Invalidate()
    {
        UnsubscribeEvents();
        foreach (var row in shopItemRows)
        {
            if (row.elements.actionButton != null)
                row.elements.actionButton.onClick.RemoveAllListeners();
        }
        shopItemRows.Clear();
        if (contentContainer != null)
        {
            for (int i = contentContainer.childCount - 1; i >= 0; i--)
                Destroy(contentContainer.GetChild(i).gameObject);
        }
        isPopulated = false;
        if (gameObject.activeInHierarchy)
            StartCoroutine(WaitAndPopulate());
    }

    private void OnDestroy()
    {
        UnsubscribeEvents();

        foreach (var row in shopItemRows)
        {
            if (row.elements.actionButton != null)
                row.elements.actionButton.onClick.RemoveAllListeners();
        }
    }

    private void PopulateShop()
    {
        AccessoryType[] types = {
            AccessoryType.Headwear, AccessoryType.Hair, AccessoryType.Facewear,
            AccessoryType.MainHand, AccessoryType.OffHand, AccessoryType.Back
        };

        foreach (var type in types)
        {
            var items = AccessoryRegistry.Instance.GetShopItemsForType(type);
            if (items.Count == 0) continue;

            ShopUIFactory.CreateSectionHeader(
                ShopDisplayFormatter.GetDisplayName(type), contentContainer);

            foreach (var item in items)
            {
                var elements = ShopUIFactory.CreateShopItemRow(
                    ShopDisplayFormatter.GetItemDisplayName(item), contentContainer);
                elements.rootObject.name = $"Item_{item.itemId}";

                var row = new ShopItemRow { shopItem = item, elements = elements };
                shopItemRows.Add(row);
                UpdateRowButton(row);
            }
        }
    }

    private void OnItemButtonClicked(int itemId, AccessoryType type)
    {
        var registry = AccessoryRegistry.Instance
            ?? throw new System.InvalidOperationException(
                "ShopPanel.OnItemButtonClicked: AccessoryRegistry not initialized");
        var shopItem = registry.GetShopItemById(itemId);
        if (shopItem == null) return;

        if (shopItem.isOwned)
            return;

        var equipService = AccessoryEquipService.Instance
            ?? throw new System.InvalidOperationException(
                "ShopPanel: AccessoryEquipService not initialized");

        if (equipService.IsPreviewActive && equipService.PreviewedItemId == itemId)
        {
            equipService.ClearPreview();
            return;
        }

        equipService.PreviewItem(itemId, type);
    }

    private void OnBuyButtonClicked(int itemId, AccessoryType type)
    {
        if (isPurchasing) return;

        var registry = AccessoryRegistry.Instance
            ?? throw new System.InvalidOperationException(
                "ShopPanel.OnBuyButtonClicked: AccessoryRegistry not initialized");
        var shopItem = registry.GetShopItemById(itemId);
        if (shopItem == null || shopItem.isOwned) return;

        StartCoroutine(PurchaseItem(itemId, type));
    }

    private IEnumerator PurchaseItem(int itemId, AccessoryType type)
    {
        isPurchasing = true;
        try
        {
            if (AccessoryEquipService.Instance != null)
                AccessoryEquipService.Instance.ClearPreview();

            SetStatus("Purchasing...");
            RefreshAllButtons();
            bool isPurchaseComplete = false;

            yield return ShopAPI.Instance.PurchaseAccessory(
                itemId,
                (bool success, string message) =>
                {
                    if (success)
                    {
                        (AccessoryRegistry.Instance
                            ?? throw new System.InvalidOperationException(
                                "ShopPanel.PurchaseItem: AccessoryRegistry not initialized"))
                            .MarkAccessoryOwned(itemId);

                        var walletDisplay = WalletDisplay.Instance
                            ?? throw new System.InvalidOperationException(
                                "ShopPanel.PurchaseItem: WalletDisplay not initialized");
                        StartCoroutine(walletDisplay.Refresh());
                        SetStatus("");

                        if (AchievementService.Instance != null)
                        {
                            AchievementService.Instance.ReportEvent(GameEventType.Purchase,
                                new GameEventContext { itemId = itemId });

                            var purchasedItem = (AccessoryRegistry.Instance
                                ?? throw new System.InvalidOperationException(
                                    "ShopPanel: AccessoryRegistry not initialized for cost lookup"))
                                .GetShopItemById(itemId);
                            if (purchasedItem != null)
                            {
                                int goldCost = purchasedItem.GetCostForCurrency("gold");
                                if (goldCost > 0)
                                {
                                    AchievementService.Instance.ReportEvent(GameEventType.CurrencySpent,
                                        new GameEventContext { currencyName = "gold", amount = goldCost });
                                }
                            }
                        }

                        ShowPurchaseConfirmation(itemId, type);
                    }
                    else
                    {
                        SetStatus(message);
                        StartCoroutine(ClearStatusAfterDelay(3f));
                    }
                    isPurchaseComplete = true;
                },
                (string error) =>
                {
                    SetStatus($"Error: {error}");
                    StartCoroutine(ClearStatusAfterDelay(3f));
                    isPurchaseComplete = true;
                }
            );
            while (!isPurchaseComplete) yield return null;
        }
        finally
        {
            isPurchasing = false;
            RefreshAllButtons();
        }
    }

    private void ShowPurchaseConfirmation(int itemId, AccessoryType type)
    {
        var registry = AccessoryRegistry.Instance
            ?? throw new System.InvalidOperationException(
                "ShopPanel.ShowPurchaseConfirmation: AccessoryRegistry not initialized");
        var shopItem = registry.GetShopItemById(itemId);
        string itemName = shopItem != null && !string.IsNullOrEmpty(shopItem.displayName)
            ? shopItem.displayName
            : $"Item {itemId}";

        var canvasTransform = GetComponentInParent<Canvas>()?.transform
            ?? throw new System.InvalidOperationException(
                "ShopPanel: no parent Canvas found for confirmation prompt");

        activePrompt = PurchaseConfirmationPrompt.Show(
            canvasTransform,
            itemName,
            onConfirm: () =>
            {
                activePrompt = null;
                var equipService = AccessoryEquipService.Instance
                    ?? throw new System.InvalidOperationException(
                        "ShopPanel: AccessoryEquipService not initialized");
                equipService.EquipItem(itemId, type);

                if (TabManager.Instance != null)
                    TabManager.Instance.ShowTab(INVENTORY_TAB_INDEX);
            },
            onCancel: () =>
            {
                activePrompt = null;
            }
        );
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    private IEnumerator ClearStatusAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SetStatus("");
    }

    private void RefreshAllButtons()
    {
        foreach (var row in shopItemRows)
            UpdateRowButton(row);
    }

    private void UpdateRowButton(ShopItemRow row)
    {
        row.elements.actionButton.onClick.RemoveAllListeners();

        bool isOwned = row.shopItem.isOwned;
        bool isPreviewing = AccessoryEquipService.Instance != null
            && AccessoryEquipService.Instance.IsPreviewActive
            && AccessoryEquipService.Instance.PreviewedItemId == row.shopItem.itemId;

        var state = ShopButtonStateResolver.ResolveShopItemState(isOwned, isPurchasing, isPreviewing);
        row.elements.buttonText.text = state.text;
        row.elements.buttonImage.color = state.color;
        row.elements.actionButton.interactable = state.isInteractable;
        row.elements.costText.text = state.showCost
            ? ShopDisplayFormatter.FormatCosts(row.shopItem.costs)
            : "";

        if (state.isInteractable)
        {
            int capturedItemId = row.shopItem.itemId;
            AccessoryType capturedType = row.shopItem.accessoryType;

            if (isPreviewing)
                row.elements.actionButton.onClick.AddListener(
                    () => OnBuyButtonClicked(capturedItemId, capturedType));
            else
                row.elements.actionButton.onClick.AddListener(
                    () => OnItemButtonClicked(capturedItemId, capturedType));
        }
    }

    private struct ShopItemRow
    {
        public ShopAccessoryItem shopItem;
        public ItemRowElements elements;
    }
}
