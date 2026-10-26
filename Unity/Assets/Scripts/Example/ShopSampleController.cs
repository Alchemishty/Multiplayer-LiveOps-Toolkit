using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Shop;
using MultiplayerLiveOpsToolkit.Inventory;
using UnityEngine;

/// <summary>Owns sample shop requests and optional preview, equip and achievement integrations.</summary>
public sealed class ShopSampleController : MonoBehaviour
{
    [SerializeField] private ShopPanel shopPanel;
    [SerializeField] private SetsPanel setsPanel;
    [SerializeField] private AccessoryRegistry accessoryRegistry;
    private readonly HashSet<IEnumerator> operations = new HashSet<IEnumerator>();
    public event Action PurchaseConfirmed;
    private IShopService shop;
    private IAuthenticatedSession session;
    private AccessoryEquipService previewService;
    private PurchaseConfirmationPrompt activePurchasePrompt;
    private bool IsAuthenticated => session != null && session.IsActive;

    public void Bind(IShopService shopService, IInventoryService inventory, IAuthenticatedSession authenticatedSession)
    {
        if (shop != null && shop.IsBusy) throw new InvalidOperationException("Cannot rebind a Shop controller during an operation.");
        if (shopService == null) throw new ArgumentNullException(nameof(shopService));
        if (inventory == null) throw new ArgumentNullException(nameof(inventory));
        if (authenticatedSession == null) throw new ArgumentNullException(nameof(authenticatedSession));
        if (isActiveAndEnabled && shop != null) shop.StateChanged -= OnShopStateChanged;
        shop = shopService;
        session = authenticatedSession;
        if (isActiveAndEnabled) shop.StateChanged += OnShopStateChanged;
        if (shopPanel != null) shopPanel.Bind(shop, inventory);
        if (setsPanel != null) setsPanel.Bind(shop, inventory);
        // Standalone/custom UI consumers need no local asset bridge.
        if (accessoryRegistry != null)
        {
            try { accessoryRegistry.Bind(shop, inventory); }
            catch (InvalidOperationException exception) { session.RejectSession(session.SessionVersion, exception.Message); }
        }
    }
    private void OnEnable()
    {
        BindPanelRequests(shopPanel, true);
        BindPanelRequests(setsPanel, true);
        if (shop != null) shop.StateChanged += OnShopStateChanged;
        if (previewService != null) { previewService.PreviewChanged += UpdateShopPreview; previewService.SetPreviewChanged += UpdateShopPreview; UpdateShopPreview(); }
    }
    public void BindPreview(AccessoryEquipService equipmentPreview)
    {
        if (equipmentPreview == null) throw new ArgumentNullException(nameof(equipmentPreview));
        if (isActiveAndEnabled && previewService != null) { previewService.PreviewChanged -= UpdateShopPreview; previewService.SetPreviewChanged -= UpdateShopPreview; }
        previewService = equipmentPreview;
        if (isActiveAndEnabled) { previewService.PreviewChanged += UpdateShopPreview; previewService.SetPreviewChanged += UpdateShopPreview; UpdateShopPreview(); }
    }
    private void StartOwned(IEnumerator operation)
    {
        operations.Add(operation);
        StartCoroutine(RunOwned(operation));
    }
    private IEnumerator RunOwned(IEnumerator operation)
    {
        try { yield return operation; }
        finally { operations.Remove(operation); (operation as IDisposable)?.Dispose(); }
    }
    private void OnDisable()
    {
        BindPanelRequests(shopPanel, false);
        BindPanelRequests(setsPanel, false);
        if (shop != null) shop.StateChanged -= OnShopStateChanged;
        if (previewService != null) { previewService.PreviewChanged -= UpdateShopPreview; previewService.SetPreviewChanged -= UpdateShopPreview; }
        foreach (var operation in new List<IEnumerator>(operations))
        { try { (operation as IDisposable)?.Dispose(); } catch (Exception exception) { Debug.LogException(exception); } }
        operations.Clear();
        StopAllCoroutines();
        ClearPresentation();
    }

    private void BindPanelRequests(ShopPanel panel, bool shouldBind)
    {
        if (panel == null) return; // Sample UI is optional for an explicitly selected service.
        if (shouldBind) { panel.PurchaseRequested += PurchaseOffer; panel.PreviewRequested += PreviewOffer; panel.RefreshRequested += RefreshShop; panel.Closed += ClearPresentation; }
        else { panel.PurchaseRequested -= PurchaseOffer; panel.PreviewRequested -= PreviewOffer; panel.RefreshRequested -= RefreshShop; panel.Closed -= ClearPresentation; }
    }
    public void PurchaseOffer(string offerId)
    {
        if (shop == null) throw new InvalidOperationException("Bind Shop before requesting purchases.");
        if (!IsAuthenticated) return;
        if (!shop.Snapshot.Offers.TryGetValue(offerId, out var offer)) throw new InvalidOperationException("Requested offer is not in the catalog: " + offerId);
        if (previewService != null) { previewService.ClearPreview(); previewService.ClearSetPreview(); }
        StartOwned(shop.Purchase(offerId, state => OnShopPurchase(offer), HandleShopFailure));
    }
    public void RefreshShop()
    {
        if (shop == null) throw new InvalidOperationException("Bind Shop before requesting a refresh.");
        if (IsAuthenticated) StartOwned(shop.Refresh(() => { }, HandleShopFailure));
    }
    private void ClearPresentation()
    {
        if (previewService != null) { previewService.ClearPreview(); previewService.ClearSetPreview(); }
        if (activePurchasePrompt != null) { Destroy(activePurchasePrompt.gameObject); activePurchasePrompt = null; }
    }
    private void OnShopStateChanged()
    {
        if (!shop.HasSnapshot || !IsAuthenticated) ClearPresentation();
    }
    private void HandleShopFailure(ShopError error)
    {
        if (error.Code == ShopErrorCode.Misconfigured) session.RejectSession(session.SessionVersion, error.Message);
    }
    private void PreviewOffer(string offerId)
    {
        if (previewService == null) throw new InvalidOperationException("Sample preview requires a local equipment integration.");
        var offer = shop.Snapshot.Offers[offerId];
        if (offer.IsSet) previewService.PreviewSet(int.Parse(offerId.Substring(4), System.Globalization.CultureInfo.InvariantCulture));
        else
        {
            int itemId = int.Parse(offer.ItemIds[0], System.Globalization.CultureInfo.InvariantCulture);
            var registry = accessoryRegistry ?? throw new InvalidOperationException("Sample preview requires AccessoryRegistry.");
            previewService.PreviewItem(itemId, registry.GetAccessoryTypeById(itemId));
        }
    }
    private void UpdateShopPreview()
    {
        if (shopPanel != null) shopPanel.SetPreview(previewService.IsPreviewActive ? "item:" + previewService.PreviewedItemId : null, true);
        if (setsPanel != null) setsPanel.SetPreview(previewService.IsSetPreviewActive ? "set:" + previewService.PreviewedSetId : null, true);
    }
    private void OnShopPurchase(ShopOffer offer)
    {
        if (!isActiveAndEnabled || !IsAuthenticated || !shop.HasSnapshot) return; // A committed completion can arrive during teardown.
        var purchaseSubscribers = PurchaseConfirmed;
        if (purchaseSubscribers != null) foreach (Action subscriber in purchaseSubscribers.GetInvocationList()) { try { subscriber(); } catch (Exception exception) { Debug.LogException(exception); } }
        var panel = offer.IsSet ? setsPanel : shopPanel;
        if (previewService == null || panel == null || !panel.gameObject.activeInHierarchy) return;
        var canvas = panel.GetComponentInParent<Canvas>() ?? throw new InvalidOperationException("Shop sample requires a Canvas for its purchase prompt.");
        activePurchasePrompt = PurchaseConfirmationPrompt.Show(canvas.transform, offer.DisplayName, () =>
        {
            activePurchasePrompt = null;
            if (!isActiveAndEnabled || !IsAuthenticated || !shop.HasSnapshot) return;
            if (offer.IsSet) previewService.EquipSet(int.Parse(offer.OfferId.Substring(4), System.Globalization.CultureInfo.InvariantCulture));
            else
            {
                int itemId = int.Parse(offer.ItemIds[0], System.Globalization.CultureInfo.InvariantCulture);
                previewService.EquipItem(itemId, accessoryRegistry.GetAccessoryTypeById(itemId));
            }
            if (TabManager.Instance != null) TabManager.Instance.ShowTab(2);
        }, () => activePurchasePrompt = null);
    }

}
