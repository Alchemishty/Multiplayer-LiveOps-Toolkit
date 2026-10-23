using UnityEngine;
using System;
using System.Collections.Generic;

public class AccessoryEquipService : MonoBehaviour
{
    public static AccessoryEquipService Instance { get; private set; }

    private readonly PreviewStateTracker previewState = new PreviewStateTracker();
    private readonly SetPreviewStateTracker setPreviewState = new SetPreviewStateTracker();

    public bool IsPreviewActive => previewState.IsPreviewActive;
    public int PreviewedItemId => previewState.PreviewedItemId;

    public event Action PreviewChanged
    {
        add => previewState.PreviewChanged += value;
        remove => previewState.PreviewChanged -= value;
    }

    public bool IsSetPreviewActive => setPreviewState.IsSetPreviewActive;
    public int PreviewedSetId => setPreviewState.PreviewedSetId;

    public event Action SetPreviewChanged
    {
        add => setPreviewState.SetPreviewChanged += value;
        remove => setPreviewState.SetPreviewChanged -= value;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void EquipItem(int itemId, AccessoryType type)
    {
        var playerAccessory = GetPlayerAccessory();
        playerAccessory.UpdateEquippedAccessories(itemId);
        playerAccessory.StoreEquippedAccessories();
    }

    public void UnequipItem(AccessoryType type)
    {
        var playerAccessory = GetPlayerAccessory();
        playerAccessory.UnequipAccessory(type);
        playerAccessory.StoreEquippedAccessories();
    }

    public void PreviewItem(int itemId, AccessoryType type)
    {
        var playerAccessory = GetPlayerAccessory();
        var avatarRoot = GetAvatarRoot();
        var visualSync = playerAccessory.GetVisualSync();

        if (previewState.IsItemPreviewed(itemId))
        {
            ClearPreview();
            return;
        }

        if (previewState.IsPreviewActive)
            RestorePreviewedSlot(visualSync, avatarRoot);

        if (setPreviewState.IsSetPreviewActive)
            RestoreSetPreviewSlots(visualSync, avatarRoot);

        int currentEquippedItemId = playerAccessory.GetEquippedItemId(type);

        var registry = GetRegistry();
        GameObject prefab = registry.GetAccessoryPrefabById(itemId)
            ?? throw new InvalidOperationException(
                $"AccessoryEquipService.PreviewItem: no prefab found for item {itemId}");

        AccessoryType conflictSlot = AccessoryLoadout.GetConflictingSlot(type);
        int displacedConflictItemId = -1;
        if (conflictSlot != AccessoryType.None && visualSync.IsSlotActive(conflictSlot))
        {
            displacedConflictItemId = visualSync.GetActiveItemId(conflictSlot);
            visualSync.Unequip(conflictSlot);
        }

        visualSync.Equip(itemId, type, prefab, avatarRoot);
        previewState.StartPreview(itemId, type, currentEquippedItemId, conflictSlot, displacedConflictItemId);
    }

    public void ClearPreview()
    {
        var clearResult = previewState.ClearPreview();
        if (!clearResult.WasActive)
            return;

        if (!TryGetPlayerAccessory(out var playerAccessory, out var avatarRoot))
            return;

        var visualSync = playerAccessory.GetVisualSync();
        RestoreSlotFromClearResult(clearResult, visualSync, avatarRoot);
    }

    public void PreviewSet(int setId)
    {
        var registry = GetRegistry();
        var accessoryIds = registry.GetAccessoryIdsInSet(setId);
        if (accessoryIds.Count == 0) return;

        var playerAccessory = GetPlayerAccessory();
        var avatarRoot = GetAvatarRoot();
        var visualSync = playerAccessory.GetVisualSync();

        if (previewState.IsPreviewActive)
            RestorePreviewedSlot(visualSync, avatarRoot);

        if (setPreviewState.IsSetPreviewActive)
            RestoreSetPreviewSlots(visualSync, avatarRoot);

        var slotInputs = new List<SetPreviewStateTracker.SetPreviewSlotInput>();
        foreach (int accessoryId in accessoryIds)
        {
            try
            {
                AccessoryType type = registry.GetAccessoryTypeById(accessoryId);
                if (type == AccessoryType.None) continue;

                GameObject prefab = registry.GetAccessoryPrefabById(accessoryId);
                if (prefab == null)
                {
                    Debug.LogWarning($"AccessoryEquipService.PreviewSet: no prefab for item {accessoryId} in set {setId}");
                    continue;
                }

                int currentEquippedItemId = playerAccessory.GetEquippedItemId(type);

                visualSync.Equip(accessoryId, type, prefab, avatarRoot);

                slotInputs.Add(new SetPreviewStateTracker.SetPreviewSlotInput
                {
                    slot = type,
                    itemId = accessoryId,
                    currentEquippedItemId = currentEquippedItemId,
                });
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        if (slotInputs.Count > 0)
            setPreviewState.StartSetPreview(setId, slotInputs);
    }

    public void ClearSetPreview()
    {
        var clearResult = setPreviewState.ClearSetPreview();
        if (!clearResult.WasActive)
            return;

        if (!TryGetPlayerAccessory(out var playerAccessory, out var avatarRoot))
            return;

        var visualSync = playerAccessory.GetVisualSync();
        RestoreSetPreviewSlots(clearResult, visualSync, avatarRoot);
    }

    public void EquipSet(int setId)
    {
        var registry = GetRegistry();
        var accessoryIds = registry.GetAccessoryIdsInSet(setId);
        if (accessoryIds.Count == 0) return;

        var playerAccessory = GetPlayerAccessory();
        playerAccessory.UpdateEquippedAccessories(accessoryIds);
        playerAccessory.StoreEquippedAccessories();
    }

    public void UnequipSet(int setId)
    {
        var registry = GetRegistry();
        var accessoryIds = registry.GetAccessoryIdsInSet(setId);
        if (accessoryIds.Count == 0) return;

        var playerAccessory = GetPlayerAccessory();

        foreach (int accessoryId in accessoryIds)
        {
            AccessoryType type = registry.GetAccessoryTypeById(accessoryId);
            if (type == AccessoryType.None) continue;

            if (playerAccessory.GetEquippedItemId(type) == accessoryId)
                playerAccessory.UnequipAccessory(type);
        }

        playerAccessory.StoreEquippedAccessories();
    }

    private void RestoreSetPreviewSlots(IAccessoryVisualSync visualSync, Transform avatarRoot)
    {
        var clearResult = setPreviewState.ClearSetPreview();
        if (!clearResult.WasActive) return;
        RestoreSetPreviewSlots(clearResult, visualSync, avatarRoot);
    }

    private void RestoreSetPreviewSlots(
        SetPreviewStateTracker.ClearSetResult clearResult,
        IAccessoryVisualSync visualSync,
        Transform avatarRoot)
    {
        var registry = GetRegistry();
        foreach (var slotInfo in clearResult.ClearedSlots)
        {
            try
            {
                visualSync.Unequip(slotInfo.clearedSlot);

                if (slotInfo.restoreItemId > 0)
                {
                    GameObject restorePrefab = registry.GetAccessoryPrefabById(slotInfo.restoreItemId);
                    if (restorePrefab != null)
                        visualSync.Equip(slotInfo.restoreItemId, slotInfo.clearedSlot, restorePrefab, avatarRoot);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }

    private void RestorePreviewedSlot(IAccessoryVisualSync visualSync, Transform avatarRoot)
    {
        var clearResult = previewState.ClearPreview();
        if (!clearResult.WasActive)
            return;

        RestoreSlotFromClearResult(clearResult, visualSync, avatarRoot);
    }

    private void RestoreSlotFromClearResult(
        PreviewStateTracker.ClearResult clearResult,
        IAccessoryVisualSync visualSync,
        Transform avatarRoot)
    {
        visualSync.Unequip(clearResult.ClearedSlot);

        if (clearResult.RestoreItemId > 0)
        {
            var registry = GetRegistry();
            GameObject restorePrefab = registry.GetAccessoryPrefabById(clearResult.RestoreItemId);
            if (restorePrefab != null)
                visualSync.Equip(clearResult.RestoreItemId, clearResult.ClearedSlot, restorePrefab, avatarRoot);
        }

        if (clearResult.DisplacedConflictSlot != AccessoryType.None && clearResult.DisplacedConflictItemId > 0)
        {
            var registry = GetRegistry();
            GameObject conflictPrefab = registry.GetAccessoryPrefabById(clearResult.DisplacedConflictItemId);
            if (conflictPrefab != null)
                visualSync.Equip(clearResult.DisplacedConflictItemId, clearResult.DisplacedConflictSlot, conflictPrefab, avatarRoot);
        }
    }

    private bool TryGetPlayerAccessory(out PlayerAccessory playerAccessory, out Transform avatarRoot)
    {
        playerAccessory = null;
        avatarRoot = null;

        var avatar = ClientPlayerAvatars.Instance?.currentClientAvatar;
        if (avatar == null)
            return false;

        playerAccessory = avatar.GetComponent<PlayerAccessory>();
        avatarRoot = avatar.transform;
        return playerAccessory != null;
    }

    private PlayerAccessory GetPlayerAccessory()
    {
        var avatar = ClientPlayerAvatars.Instance?.currentClientAvatar
            ?? throw new InvalidOperationException(
                "AccessoryEquipService: client avatar not available");

        return avatar.GetComponent<PlayerAccessory>()
            ?? throw new InvalidOperationException(
                "AccessoryEquipService: PlayerAccessory component not found on avatar");
    }

    private Transform GetAvatarRoot()
    {
        return ClientPlayerAvatars.Instance?.currentClientAvatar?.transform
            ?? throw new InvalidOperationException(
                "AccessoryEquipService: client avatar transform not available");
    }

    private AccessoryRegistry GetRegistry()
    {
        return AccessoryRegistry.Instance
            ?? throw new InvalidOperationException(
                "AccessoryEquipService: AccessoryRegistry not initialized");
    }
}
