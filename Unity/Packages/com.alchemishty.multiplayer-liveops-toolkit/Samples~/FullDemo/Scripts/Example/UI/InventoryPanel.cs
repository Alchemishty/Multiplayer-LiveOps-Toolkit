using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Inventory;
using MultiplayerLiveOpsToolkit.Equipment;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>Cosmetic ownership view; avatar equipment is an optional sample integration.</summary>
public class InventoryPanel : MonoBehaviour
{
    [SerializeField] private Transform contentContainer;
    [SerializeField] private TMP_Text statusText;
    private readonly List<InventoryRow> rows = new List<InventoryRow>();
    private IInventoryService inventory;
    private IEquipmentService equipment;
    public event Action<IReadOnlyList<string>, bool> EquipmentRequested;
    public event Action RefreshEquipmentRequested;
    private bool hasSubscribed;

    private void OnEnable()
    {
        if (contentContainer == null || statusText == null)
            throw new InvalidOperationException("InventoryPanel requires contentContainer and statusText.");
        statusText.richText = false;
        if (inventory != null) SubscribeAndRender();
        if (equipment != null) equipment.StateChanged += RefreshButtons;
    }
    public void BindEquipment(IEquipmentService equipmentService)
    {
        if (equipmentService == null) throw new ArgumentNullException(nameof(equipmentService));
        if (isActiveAndEnabled && equipment != null) equipment.StateChanged -= RefreshButtons;
        equipment = equipmentService;
        if (isActiveAndEnabled) { equipment.StateChanged += RefreshButtons; RefreshButtons(); }
    }
    public void Bind(IInventoryService inventoryService)
    {
        if (inventoryService == null) throw new ArgumentNullException(nameof(inventoryService));
        UnsubscribeInventory();
        inventory = inventoryService;
        if (isActiveAndEnabled) SubscribeAndRender();
    }
    private void SubscribeAndRender()
    {
        if (!hasSubscribed) { inventory.SnapshotChanged += Render; hasSubscribed = true; }
        Render();
    }
    private void Render()
    {
        ClearRows();
        statusText.text = "";
        if (inventory == null || !inventory.HasSnapshot) return; // Initial or terminal account state.
        var snapshot = inventory.Snapshot;
        if (snapshot.Sets.Count > 0)
        {
            ShopUIFactory.CreateSectionHeader("Outfits", contentContainer);
            foreach (var cosmeticSet in snapshot.Sets.Values)
                AddRow(cosmeticSet.SetId, cosmeticSet.DisplayName, cosmeticSet.ItemIds, true);
        }
        var itemsByCategory = new SortedDictionary<string, List<CosmeticItem>>(StringComparer.Ordinal);
        foreach (var item in snapshot.Items.Values)
        {
            if (!itemsByCategory.TryGetValue(item.CategoryId, out var items)) { items = new List<CosmeticItem>(); itemsByCategory.Add(item.CategoryId, items); }
            items.Add(item);
        }
        foreach (var category in itemsByCategory)
        {
            ShopUIFactory.CreateSectionHeader(Enum.TryParse(category.Key, true, out AccessoryType type) ? ShopDisplayFormatter.GetDisplayName(type) : category.Key, contentContainer);
            foreach (var item in category.Value) AddRow(item.ItemId, item.DisplayName, new[] { item.ItemId }, false);
        }
        if (snapshot.Items.Count == 0) ShopUIFactory.CreateEmptyStateMessage("No cosmetics owned yet.", contentContainer);
        if (equipment != null && equipment.RequiresRefresh)
        {
            statusText.text = "Confirm equipment before another change.";
            var elements = ShopUIFactory.CreateInventoryItemRow("Confirm equipment state", contentContainer);
            elements.rootObject.name = "EquipmentRefresh";
            var row = new InventoryRow { IsRefresh = true, Elements = elements, Listener = OnRefreshEquipmentClicked };
            elements.actionButton.onClick.AddListener(row.Listener);
            rows.Add(row); RefreshButton(row);
        }
    }
    private void AddRow(string id, string displayName, IReadOnlyList<string> itemIds, bool isSet)
    {
        var elements = isSet ? ShopUIFactory.CreateInventorySetRow(displayName, contentContainer) : ShopUIFactory.CreateInventoryItemRow(displayName, contentContainer);
        elements.rootObject.name = (isSet ? "Set_" : "Item_") + id;
        foreach (var label in elements.rootObject.GetComponentsInChildren<TMP_Text>()) label.richText = false;
        var row = new InventoryRow { Id = id, ItemIds = itemIds, IsSet = isSet, Elements = elements };
        row.Listener = () => OnEquipClicked(row);
        elements.actionButton.onClick.AddListener(row.Listener);
        rows.Add(row);
        RefreshButton(row);
    }
    private void OnEquipClicked(InventoryRow row)
    {
        if (equipment == null || !equipment.HasSnapshot || equipment.IsBusy || equipment.RequiresRefresh) return;
        var subscribers = EquipmentRequested ?? throw new InvalidOperationException("InventoryPanel requires an owner for equipment commands.");
        foreach (Action<IReadOnlyList<string>, bool> subscriber in subscribers.GetInvocationList())
        { try { subscriber(row.ItemIds, IsEquipped(row)); } catch (Exception exception) { Debug.LogException(exception); } }
    }
    private bool IsEquipped(InventoryRow row)
    {
        if (equipment == null || !equipment.HasSnapshot) return false;
        foreach (string itemId in row.ItemIds)
        {
            if (!equipment.Snapshot.SlotsByItemId.TryGetValue(itemId, out string slot)) throw new InvalidOperationException("Equipment definitions are missing owned cosmetic " + itemId);
            if (!equipment.Snapshot.Loadout.ItemsBySlot.TryGetValue(slot, out string equippedId) || equippedId != itemId) return false;
        }
        return true;
    }
    private void OnRefreshEquipmentClicked()
    {
        var subscribers = RefreshEquipmentRequested ?? throw new InvalidOperationException("InventoryPanel requires an owner for equipment refresh.");
        foreach (Action subscriber in subscribers.GetInvocationList()) { try { subscriber(); } catch (Exception exception) { Debug.LogException(exception); } }
    }
    private void RefreshButtons() { Render(); }
    private void RefreshButton(InventoryRow row)
    {
        if (row.IsRefresh)
        {
            row.Elements.buttonText.text = "Refresh";
            row.Elements.actionButton.interactable = equipment != null && !equipment.IsBusy;
            return;
        }
        bool hasEquipment = equipment != null && equipment.HasSnapshot && !equipment.IsBusy && !equipment.RequiresRefresh;
        bool isEquipped = hasEquipment && IsEquipped(row);
        var state = row.IsSet ? ShopButtonStateResolver.ResolveInventorySetState(isEquipped) : ShopButtonStateResolver.ResolveInventoryItemState(isEquipped);
        row.Elements.buttonText.text = state.text;
        row.Elements.buttonImage.color = state.color;
        row.Elements.actionButton.interactable = hasEquipment;
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
    private void UnsubscribeInventory()
    {
        if (hasSubscribed) { inventory.SnapshotChanged -= Render; hasSubscribed = false; }
    }
    private void OnDisable()
    {
        StopAllCoroutines();
        UnsubscribeInventory();
        if (equipment != null) equipment.StateChanged -= RefreshButtons;
        if (contentContainer != null) ClearRows(); // References may be destroyed during scene teardown.
        if (statusText != null) statusText.text = "";
    }
    private sealed class InventoryRow
    {
        public string Id;
        public IReadOnlyList<string> ItemIds;
        public bool IsSet;
        public bool IsRefresh;
        public ItemRowElements Elements;
        public UnityAction Listener;
    }
}
