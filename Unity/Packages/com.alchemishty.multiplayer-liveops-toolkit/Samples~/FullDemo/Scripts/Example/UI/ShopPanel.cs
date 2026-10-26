using System;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Shop;
using MultiplayerLiveOpsToolkit.Inventory;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>Service-bound shop view; the sample owner executes purchase and preview requests.</summary>
public class ShopPanel : MonoBehaviour
{
    [SerializeField] private Transform contentContainer;
    [SerializeField] private TMP_Text statusText;
    private readonly List<Row> rows = new List<Row>();
    private IShopService shop;
    private IInventoryService inventory;
    private bool hasSubscribed;
    private bool canPreview;
    private string previewedOfferId;
    public event Action<string> PurchaseRequested;
    public event Action<string> PreviewRequested;
    public event Action RefreshRequested;
    public event Action Closed;
    protected virtual bool ShowsSets => false;

    protected virtual void OnEnable()
    {
        if (contentContainer == null || statusText == null) throw new InvalidOperationException("ShopPanel requires contentContainer and statusText.");
        statusText.richText = false;
        if (shop != null) SubscribeAndRender();
    }
    public void Bind(IShopService service, IInventoryService ownership)
    {
        if (service == null) throw new ArgumentNullException(nameof(service));
        if (ownership == null) throw new ArgumentNullException(nameof(ownership));
        Unsubscribe();
        shop = service; inventory = ownership;
        if (isActiveAndEnabled) SubscribeAndRender();
    }
    public void SetPreview(string offerId, bool isAvailable)
    {
        previewedOfferId = offerId; canPreview = isAvailable;
        if (isActiveAndEnabled && shop != null) Render();
    }
    private void SubscribeAndRender()
    {
        if (!hasSubscribed) { shop.StateChanged += Render; inventory.SnapshotChanged += Render; hasSubscribed = true; }
        Render();
    }
    private void Render()
    {
        ClearRows();
        statusText.text = shop.LastError == null ? "" : shop.LastError.Message;
        if (!shop.HasSnapshot) return;
        var offersByCategory = new SortedDictionary<string, List<ShopOffer>>(StringComparer.Ordinal);
        foreach (var offer in shop.Snapshot.Offers.Values)
        {
            if (offer.IsSet != ShowsSets) continue;
            if (!offersByCategory.TryGetValue(offer.CategoryId, out var offers))
            {
                offers = new List<ShopOffer>();
                offersByCategory.Add(offer.CategoryId, offers);
            }
            offers.Add(offer);
        }
        foreach (var category in offersByCategory)
        {
            ShopUIFactory.CreateSectionHeader(Enum.TryParse(category.Key, true, out AccessoryType type) ? ShopDisplayFormatter.GetDisplayName(type) : category.Key, contentContainer);
            category.Value.Sort((first, second) =>
            {
                int comparison = StringComparer.OrdinalIgnoreCase.Compare(first.DisplayName, second.DisplayName);
                return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(first.OfferId, second.OfferId);
            });
            foreach (var offer in category.Value)
            {
                var elements = ShowsSets ? ShopUIFactory.CreateShopSetRow(offer.DisplayName, contentContainer) : ShopUIFactory.CreateShopItemRow(offer.DisplayName, contentContainer);
                elements.rootObject.name = "Offer_" + offer.OfferId;
                foreach (var label in elements.rootObject.GetComponentsInChildren<TMP_Text>()) label.richText = false;
                bool isOwned = inventory.HasSnapshot;
                foreach (var itemId in offer.ItemIds) isOwned &= inventory.HasSnapshot && inventory.Snapshot.Items.ContainsKey(itemId);
                bool isPreviewing = previewedOfferId == offer.OfferId;
                var state = ShowsSets ? ShopButtonStateResolver.ResolveShopSetState(isOwned, shop.IsBusy, isPreviewing || !canPreview)
                    : ShopButtonStateResolver.ResolveShopItemState(isOwned, shop.IsBusy, isPreviewing || !canPreview);
                elements.buttonText.text = state.text;
                elements.buttonImage.color = state.color;
                elements.actionButton.interactable = state.isInteractable && !shop.RequiresRefresh && inventory.HasSnapshot;
                elements.costText.text = state.showCost ? ShopDisplayFormatter.FormatCosts(offer.Costs) : "";
                var row = new Row { Elements = elements };
                row.Listener = () =>
                {
                    if (canPreview && previewedOfferId != offer.OfferId) Notify(PreviewRequested, offer.OfferId);
                    else Notify(PurchaseRequested, offer.OfferId);
                };
                elements.actionButton.onClick.AddListener(row.Listener);
                rows.Add(row);
            }
        }
        if (ShowsSets) ShopUIFactory.CreateEmptyStateMessage("Partially owned sets use the full offer price.", contentContainer);
        if (shop.RequiresRefresh)
        {
            var elements = ShopUIFactory.CreateInventoryItemRow("Confirm account state before buying", contentContainer);
            elements.buttonText.text = "Refresh";
            elements.actionButton.interactable = !shop.IsBusy;
            var row = new Row { Elements = elements, Listener = OnRefreshClicked };
            elements.actionButton.onClick.AddListener(row.Listener);
            rows.Add(row);
        }
    }
    private static void Notify(Action<string> subscribers, string offerId)
    {
        if (subscribers == null) throw new InvalidOperationException("ShopPanel requires a sample/custom owner for its requests.");
        foreach (Action<string> subscriber in subscribers.GetInvocationList())
        { try { subscriber(offerId); } catch (Exception exception) { Debug.LogException(exception); } }
    }
    private void OnRefreshClicked()
    {
        var subscribers = RefreshRequested ?? throw new InvalidOperationException("ShopPanel requires an owner for refresh requests.");
        foreach (Action subscriber in subscribers.GetInvocationList())
        { try { subscriber(); } catch (Exception exception) { Debug.LogException(exception); } }
    }
    private void ClearRows()
    {
        foreach (var row in rows) row.Elements.actionButton.onClick.RemoveListener(row.Listener);
        rows.Clear();
        for (int index = contentContainer.childCount - 1; index >= 0; index--)
        {
            var child = contentContainer.GetChild(index);
            child.SetParent(null, false);
            Destroy(child.gameObject);
        }
    }
    private void Unsubscribe()
    { if (hasSubscribed) { shop.StateChanged -= Render; inventory.SnapshotChanged -= Render; hasSubscribed = false; } }
    protected virtual void OnDisable()
    {
        Unsubscribe();
        if (contentContainer != null) ClearRows();
        if (statusText != null) statusText.text = "";
        var subscribers = Closed;
        if (subscribers != null)
            foreach (Action subscriber in subscribers.GetInvocationList())
            { try { subscriber(); } catch (Exception exception) { Debug.LogException(exception); } }
    }
    private sealed class Row { public ItemRowElements Elements; public UnityAction Listener; }
}
