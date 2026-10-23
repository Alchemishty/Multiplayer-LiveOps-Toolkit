using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class SetsPanel : MonoBehaviour
{
    private const int INVENTORY_TAB_INDEX = 2;

    [SerializeField] private Transform contentContainer;
    [SerializeField] private TMP_Text statusText;

    private List<SetPanelRow> setRows = new();
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
        isPurchasing = false;
        UnsubscribeEvents();

        if (activePrompt != null)
        {
            Destroy(activePrompt.gameObject);
            activePrompt = null;
        }

        if (AccessoryEquipService.Instance != null)
            AccessoryEquipService.Instance.ClearSetPreview();
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
            AccessoryEquipService.Instance.SetPreviewChanged += RefreshAllButtons;

        if (AccessoryRegistry.Instance != null)
        {
            AccessoryRegistry.Instance.ShopCatalogLoaded += RefreshSets;
            AccessoryRegistry.Instance.AccessoryOwnershipChanged += RefreshSets;
        }
    }

    private void UnsubscribeEvents()
    {
        if (playerAccessory != null)
            playerAccessory.EquippedStateChanged -= RefreshAllButtons;

        if (AccessoryEquipService.Instance != null)
            AccessoryEquipService.Instance.SetPreviewChanged -= RefreshAllButtons;

        if (AccessoryRegistry.Instance != null)
        {
            AccessoryRegistry.Instance.ShopCatalogLoaded -= RefreshSets;
            AccessoryRegistry.Instance.AccessoryOwnershipChanged -= RefreshSets;
        }
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
        PopulateSets();
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

        foreach (var row in setRows)
        {
            if (row.elements.actionButton != null)
                row.elements.actionButton.onClick.RemoveAllListeners();
        }
    }

    private void RefreshSets()
    {
        if (AccessoryRegistry.Instance == null || !AccessoryRegistry.Instance.IsShopCatalogLoaded())
            return;

        ClearRows();
        PopulateSets();
    }

    private void ClearRows()
    {
        foreach (var row in setRows)
        {
            if (row.elements.actionButton != null)
                row.elements.actionButton.onClick.RemoveAllListeners();
        }
        setRows.Clear();

        if (contentContainer != null)
        {
            for (int index = contentContainer.childCount - 1; index >= 0; index--)
                Destroy(contentContainer.GetChild(index).gameObject);
        }
    }

    private void PopulateSets()
    {
        var registry = AccessoryRegistry.Instance
            ?? throw new System.InvalidOperationException(
                "SetsPanel.PopulateSets: AccessoryRegistry not initialized");
        var allSets = registry.GetAllSets();

        if (allSets.Length == 0)
        {
            ShopUIFactory.CreateEmptyStateMessage("No sets available.", contentContainer);
            return;
        }

        foreach (var setData in allSets)
        {
            var elements = ShopUIFactory.CreateShopSetRow(setData.setName, contentContainer);
            elements.rootObject.name = $"Set_{setData.setId}";

            var row = new SetPanelRow { setData = setData, elements = elements };
            setRows.Add(row);
            UpdateSetRowButton(row);
        }
    }

    private void OnSetActionClicked(int setId)
    {
        var registry = AccessoryRegistry.Instance
            ?? throw new System.InvalidOperationException(
                "SetsPanel.OnSetActionClicked: AccessoryRegistry not initialized");
        var setData = registry.GetSetById(setId);
        if (setData == null) return;

        var equipService = AccessoryEquipService.Instance
            ?? throw new System.InvalidOperationException(
                "SetsPanel: AccessoryEquipService not initialized");

        if (setData.isOwned)
            return;

        if (equipService.IsSetPreviewActive && equipService.PreviewedSetId == setId)
        {
            OnBuySetClicked(setId);
            return;
        }

        equipService.PreviewSet(setId);
    }

    private void OnBuySetClicked(int setId)
    {
        if (isPurchasing) return;
        StartCoroutine(PurchaseSet(setId));
    }

    private IEnumerator PurchaseSet(int setId)
    {
        isPurchasing = true;
        try
        {
            if (AccessoryEquipService.Instance != null)
                AccessoryEquipService.Instance.ClearSetPreview();

            SetStatus("Purchasing...");
            RefreshAllButtons();
            bool isPurchaseComplete = false;

            yield return ShopAPI.Instance.PurchaseSet(
                setId,
                (bool success, string message, List<int> grantedAccessoryIds) =>
                {
                    if (success)
                    {
                        (AccessoryRegistry.Instance
                            ?? throw new System.InvalidOperationException(
                                "SetsPanel.PurchaseSet: AccessoryRegistry not initialized"))
                            .MarkSetOwned(setId);

                        var walletDisplay = WalletDisplay.Instance
                            ?? throw new System.InvalidOperationException(
                                "SetsPanel.PurchaseSet: WalletDisplay not initialized");
                        StartCoroutine(walletDisplay.Refresh());
                        SetStatus("");

                        if (AchievementService.Instance != null)
                        {
                            AchievementService.Instance.ReportEvent(GameEventType.SetPurchase,
                                new GameEventContext { setId = setId });

                            var purchasedSet = (AccessoryRegistry.Instance
                                ?? throw new System.InvalidOperationException(
                                    "SetsPanel: AccessoryRegistry not initialized for cost lookup"))
                                .GetSetById(setId);
                            if (purchasedSet != null)
                            {
                                int setGoldCost = purchasedSet.GetCostForCurrency("gold");
                                if (setGoldCost > 0)
                                {
                                    AchievementService.Instance.ReportEvent(GameEventType.CurrencySpent,
                                        new GameEventContext { currencyName = "gold", amount = setGoldCost });
                                }
                            }
                        }

                        ShowPurchaseConfirmation(setId);
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

    private void ShowPurchaseConfirmation(int setId)
    {
        var registry = AccessoryRegistry.Instance
            ?? throw new System.InvalidOperationException(
                "SetsPanel.ShowPurchaseConfirmation: AccessoryRegistry not initialized");
        var setData = registry.GetSetById(setId);
        string setName = setData != null ? setData.setName : $"Set {setId}";

        var canvasTransform = GetComponentInParent<Canvas>()?.transform
            ?? throw new System.InvalidOperationException(
                "SetsPanel: no parent Canvas found for confirmation prompt");

        activePrompt = PurchaseConfirmationPrompt.Show(
            canvasTransform,
            setName,
            onConfirm: () =>
            {
                activePrompt = null;
                var equipService = AccessoryEquipService.Instance
                    ?? throw new System.InvalidOperationException(
                        "SetsPanel: AccessoryEquipService not initialized");
                equipService.EquipSet(setId);

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
        foreach (var row in setRows)
            UpdateSetRowButton(row);
    }

    private void UpdateSetRowButton(SetPanelRow row)
    {
        row.elements.actionButton.onClick.RemoveAllListeners();

        bool isOwned = row.setData.isOwned;
        bool isPreviewing = AccessoryEquipService.Instance != null
            && AccessoryEquipService.Instance.IsSetPreviewActive
            && AccessoryEquipService.Instance.PreviewedSetId == row.setData.setId;

        var state = ShopButtonStateResolver.ResolveShopSetState(isOwned, isPurchasing, isPreviewing);
        row.elements.buttonText.text = state.text;
        row.elements.buttonImage.color = state.color;
        row.elements.actionButton.interactable = state.isInteractable;
        row.elements.costText.text = state.showCost
            ? ShopDisplayFormatter.FormatCosts(row.setData.costs)
            : "";

        if (state.isInteractable)
        {
            int capturedSetId = row.setData.setId;
            row.elements.actionButton.onClick.AddListener(
                () => OnSetActionClicked(capturedSetId));
        }
    }

    private struct SetPanelRow
    {
        public AccessorySetData setData;
        public ItemRowElements elements;
    }
}
