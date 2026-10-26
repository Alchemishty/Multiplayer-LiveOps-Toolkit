using MultiplayerLiveOpsToolkit.Netcode.Avatars;
using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Equipment;
using MultiplayerLiveOpsToolkit.Inventory;
using UnityEngine;

/// <summary>Sample command owner and preview bindings; mechanics live in IEquipmentService.</summary>
public class AccessoryEquipService : MonoBehaviour
{
    public static AccessoryEquipService Instance { get; private set; }
    private IEquipmentService equipment;
    private IInventoryService inventory;
    private IAuthenticatedSession session;
    private InventoryPanel inventoryPanel;
    private ClientPlayerAvatars avatarRegistry;
    private PlayerAccessory boundAvatar;
    private readonly HashSet<IEnumerator> operations = new HashSet<IEnumerator>();
    public bool IsPreviewActive => equipment != null && equipment.IsPreviewActive && PreviewedItemId > 0;
    public int PreviewedItemId { get; private set; } = -1;
    public bool IsSetPreviewActive => equipment != null && equipment.IsPreviewActive && PreviewedSetId > 0;
    public int PreviewedSetId { get; private set; } = -1;
    public event Action PreviewChanged;
    public event Action SetPreviewChanged;
    private void Awake() { if (Instance != null && Instance != this) { Destroy(gameObject); return; } Instance = this; }
    public void Bind(IEquipmentService service, IInventoryService ownership, IAuthenticatedSession authenticatedSession, InventoryPanel panel = null)
    {
        if (equipment != null && equipment.IsBusy) throw new InvalidOperationException("Cannot rebind equipment during an operation.");
        if (service == null) throw new ArgumentNullException(nameof(service));
        if (ownership == null) throw new ArgumentNullException(nameof(ownership));
        if (authenticatedSession == null) throw new ArgumentNullException(nameof(authenticatedSession));
        Unsubscribe(); equipment = service; inventory = ownership; session = authenticatedSession; inventoryPanel = panel;
        if (inventoryPanel != null) inventoryPanel.BindEquipment(equipment);
        if (isActiveAndEnabled) Subscribe();
    }
    private void OnEnable() { if (equipment != null) Subscribe(); if (avatarRegistry != null) { avatarRegistry.LocalAvatarChanged += OnLocalAvatarChanged; OnLocalAvatarChanged(); } }
    private void Subscribe() { equipment.PreviewChanged += OnPreviewChanged; equipment.StateChanged += ValidateVisualMappings; if (inventoryPanel != null) { inventoryPanel.EquipmentRequested += OnEquipmentRequested; inventoryPanel.RefreshEquipmentRequested += RefreshEquipment; } }
    private void Unsubscribe() { if (equipment != null) { equipment.PreviewChanged -= OnPreviewChanged; equipment.StateChanged -= ValidateVisualMappings; } if (inventoryPanel != null) { inventoryPanel.EquipmentRequested -= OnEquipmentRequested; inventoryPanel.RefreshEquipmentRequested -= RefreshEquipment; } }
    private void OnPreviewChanged()
    {
        if (!equipment.IsPreviewActive) { PreviewedItemId = -1; PreviewedSetId = -1; }
        NotifyPreview(PreviewChanged); NotifyPreview(SetPreviewChanged);
    }
    public void BindAvatars(ClientPlayerAvatars avatars)
    {
        if (avatars == null) throw new ArgumentNullException(nameof(avatars));
        if (isActiveAndEnabled && avatarRegistry != null) avatarRegistry.LocalAvatarChanged -= OnLocalAvatarChanged;
        avatarRegistry = avatars;
        ValidateVisualMappings();
        if (isActiveAndEnabled) { avatarRegistry.LocalAvatarChanged += OnLocalAvatarChanged; OnLocalAvatarChanged(); }
    }
    private void ValidateVisualMappings()
    {
        if (avatarRegistry == null || equipment == null || !equipment.HasSnapshot) return; // No visual integration selected, or account teardown.
        try
        {
            var registry = AccessoryRegistry.Instance ?? throw new InvalidOperationException("Sample equipment visuals require AccessoryRegistry.");
            foreach (var item in equipment.Snapshot.SlotsByItemId)
            {
                if (!int.TryParse(item.Key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id) || id < 1
                    || registry.GetAccessoryTypeById(id) == AccessoryType.None || registry.GetAccessoryTypeById(id).ToString().ToLowerInvariant() != item.Value || registry.GetAccessoryPrefabById(id, true) == null)
                    throw new InvalidOperationException("Missing sample visual mapping for cosmetic " + item.Key + " slot " + item.Value + " at revision " + equipment.Snapshot.Loadout.Revision);
            }
        }
        catch (InvalidOperationException exception) { session.RejectSession(session.SessionVersion, exception.Message); }
    }
    private static void NotifyPreview(Action subscribers)
    {
        if (subscribers == null) return;
        foreach (Action subscriber in subscribers.GetInvocationList()) { try { subscriber(); } catch (Exception exception) { Debug.LogException(exception); } }
    }
    private void OnLocalAvatarChanged()
    {
        if (boundAvatar != null) boundAvatar.UnbindPresentation();
        boundAvatar = null;
        var avatar = avatarRegistry.currentClientAvatar;
        if (avatar == null) { ClearPreview(); return; } // A despawn has no visual target.
        BindAvatar(avatar.GetComponent<PlayerAccessory>() ?? throw new InvalidOperationException("Sample avatar requires PlayerAccessory."));
    }
    public void BindAvatar(PlayerAccessory avatar) { if (avatar == null) throw new ArgumentNullException(nameof(avatar)); if (boundAvatar != null) boundAvatar.UnbindPresentation(); boundAvatar = avatar; avatar.BindPresentation(Equipment); }
    private IEquipmentService Equipment => equipment ?? throw new InvalidOperationException("Bind the Equipment service before using sample equipment commands.");
    public void EquipItem(int itemId, AccessoryType type) => StartOwned(Equipment.ApplyItems(new[] { Id(itemId) }, () => { }, HandleFailure));
    public void UnequipItem(AccessoryType type) => StartOwned(Equipment.Unequip(new[] { type.ToString().ToLowerInvariant() }, () => { }, HandleFailure));
    public void EquipSet(int setId) => StartOwned(Equipment.ApplyItems(OwnedSet(setId), () => { }, HandleFailure));
    public void UnequipSet(int setId) => UnequipItems(OwnedSet(setId));
    private IReadOnlyList<string> OwnedSet(int setId)
    {
        if (inventory == null || !inventory.HasSnapshot || !inventory.Snapshot.Sets.TryGetValue(Id(setId), out var set)) throw new InvalidOperationException("Sample equipment set is not owned: " + setId);
        return set.ItemIds;
    }
    private void UnequipItems(IReadOnlyList<string> ids)
    {
        var slots = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            string slot = Equipment.Snapshot.SlotsByItemId[id];
            if (Equipment.Snapshot.Loadout.ItemsBySlot.TryGetValue(slot, out string equippedId) && equippedId == id) slots.Add(slot);
        }
        StartOwned(Equipment.Unequip(slots, () => { }, HandleFailure));
    }
    private void OnEquipmentRequested(IReadOnlyList<string> ids, bool shouldUnequip)
    { if (shouldUnequip) UnequipItems(ids); else StartOwned(Equipment.ApplyItems(ids, () => { }, HandleFailure)); }
    public void PreviewItem(int itemId, AccessoryType type)
    {
        if (IsPreviewActive && PreviewedItemId == itemId) { ClearPreview(); return; }
        PreviewedItemId = itemId; PreviewedSetId = -1;
        try { Equipment.Preview(new[] { Id(itemId) }); } catch { PreviewedItemId = -1; throw; }
    }
    public void PreviewSet(int setId)
    {
        var registry = AccessoryRegistry.Instance ?? throw new InvalidOperationException("Sample set preview requires AccessoryRegistry.");
        var ids = registry.GetAccessoryIdsInSet(setId);
        if (ids.Count == 0) throw new InvalidOperationException("Sample set preview has no members: " + setId);
        var members = new List<string>(); foreach (int id in ids) members.Add(Id(id));
        PreviewedItemId = -1; PreviewedSetId = setId;
        try { Equipment.Preview(members); } catch { PreviewedSetId = -1; throw; }
    }
    public void ClearPreview() { if (equipment != null) equipment.ClearPreview(); }
    public void ClearSetPreview() => ClearPreview();
    public void RefreshEquipment() => StartOwned(Equipment.Refresh(() => { }, HandleFailure));
    private static string Id(int id) { if (id < 1) throw new ArgumentOutOfRangeException(nameof(id)); return id.ToString(System.Globalization.CultureInfo.InvariantCulture); }
    private void StartOwned(IEnumerator operation)
    {
        if (!isActiveAndEnabled || session == null || !session.IsActive) { (operation as IDisposable)?.Dispose(); return; }
        operations.Add(operation); StartCoroutine(RunOwned(operation));
    }
    private IEnumerator RunOwned(IEnumerator operation) { try { yield return operation; } finally { operations.Remove(operation); (operation as IDisposable)?.Dispose(); } }
    private void HandleFailure(EquipmentError error)
    {
        if (error.Code == EquipmentErrorCode.Misconfigured || error.Code == EquipmentErrorCode.Unavailable) session.RejectSession(session.SessionVersion, error.Message);
        else if (error.Code != EquipmentErrorCode.Cancelled) Debug.LogWarning("Equipment: " + error.Message);
    }
    private void OnDisable()
    {
        Unsubscribe();
        if (avatarRegistry != null) avatarRegistry.LocalAvatarChanged -= OnLocalAvatarChanged;
        if (boundAvatar != null) boundAvatar.UnbindPresentation();
        boundAvatar = null;
        foreach (var operation in new List<IEnumerator>(operations)) { try { (operation as IDisposable)?.Dispose(); } catch (Exception exception) { Debug.LogException(exception); } }
        operations.Clear(); StopAllCoroutines(); ClearPreview();
    }
    private void OnDestroy() { if (Instance == this) Instance = null; }
}
