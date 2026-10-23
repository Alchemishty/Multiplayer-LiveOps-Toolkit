using UnityEngine;
using Unity.Netcode;
using System;
using System.Collections;
using System.Collections.Generic;


/// <summary>
/// NetworkBehaviour that manages a player's equipped accessories with real-time sync.
///
/// Owner: maintains equippedPrefabsBySlot dict, publishes as JSON via NetworkVariable.
/// Remote: receives OnValueChanged, deserializes JSON, syncs visuals via AccessoryPool.
/// Sync format: {"Headwear": 1, "MainHand": 20} — maps AccessoryType to accessory ID.
///
/// Visual instantiation is delegated to a per-player AccessoryPool component, which
/// handles bone routing (Head, RightHand, etc.) and skinned mesh attachment.
/// </summary>
public class PlayerAccessory : NetworkBehaviour
{
    private readonly AccessoryLoadout loadout = new();

    public event Action EquippedStateChanged;

    public NetworkVariable<AccessoryState> AccessoryJson =
        new NetworkVariable<AccessoryState>(
            new AccessoryState(""),
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        AccessoryJson.OnValueChanged += OnAccessoryNetworkValueChanged;

        if (IsOwner)
        {
            StartCoroutine(LoadAccessoriesFromBackend());
        }
        else
        {
            ApplyRemoteAccessoryState(AccessoryJson.Value.Json);
        }
    }

    public override void OnNetworkDespawn()
    {
        AccessoryJson.OnValueChanged -= OnAccessoryNetworkValueChanged;
        base.OnNetworkDespawn();
    }

    private void OnAccessoryNetworkValueChanged(AccessoryState previousValue, AccessoryState newValue)
    {
        if (!IsOwner)
            ApplyRemoteAccessoryState(newValue.Json);
    }

    [ServerRpc]
    private void RequestAccessoryUpdateServerRpc(string accessoryJson, ServerRpcParams rpcParams = default)
    {
        if (string.IsNullOrEmpty(accessoryJson))
        {
            AccessoryJson.Value = new AccessoryState("");
            return;
        }

        try
        {
            AccessoryLoadout.DeserializeAccessoryState(accessoryJson);
        }
        catch (System.Exception exception)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"PlayerAccessory: rejected malformed accessory JSON from client {rpcParams.Receive.SenderClientId}: {exception.Message}");
#endif
            return;
        }

        AccessoryJson.Value = new AccessoryState(accessoryJson);
    }

    private void ApplyRemoteAccessoryState(string accessoryJson)
    {
        if (string.IsNullOrEmpty(accessoryJson))
        {
            ClearAllVisuals();
            return;
        }
        OnAccessoryJsonChanged(accessoryJson);
    }

    private void ClearAllVisuals()
    {
        var visualSync = GetComponent<IAccessoryVisualSync>();
        if (visualSync == null) return;

        foreach (AccessoryType type in allAccessoryTypes)
        {
            if (type == AccessoryType.None) continue;
            if (visualSync.IsSlotActive(type))
                visualSync.Unequip(type);
        }
    }

    IEnumerator LoadAccessoriesFromBackend()
    {
        bool isLoadComplete = false;
        yield return ShopAPI.Instance.GetEquippedAccessories(
            (List<int> response) =>
            {
                var registry = AccessoryRegistry.Instance
                    ?? throw new InvalidOperationException("PlayerAccessory: AccessoryRegistry not initialized");

                loadout.Clear();
                foreach (var accessoryId in response)
                {
                    AccessoryType type = registry.GetAccessoryTypeById(accessoryId);
                    GameObject prefab = registry.GetAccessoryPrefabById(accessoryId);
                    if (type != AccessoryType.None && prefab != null)
                        loadout.Equip(type, accessoryId);
                }
                isLoadComplete = true;
            },
            (string error) =>
            {
                Debug.LogError(error);
                isLoadComplete = true;
            }
        );
        while (!isLoadComplete) yield return null;

        SyncVisualsFromLoadout();
        RequestAccessoryUpdateServerRpc(loadout.BuildAccessoryJson());
        EquippedStateChanged?.Invoke();
    }

    public void UpdateEquippedAccessories(int accessoryId)
    {
        if (!IsSpawned)
        {
            Debug.LogWarning("PlayerAccessory: cannot update accessories before OnNetworkSpawn");
            return;
        }

        var registry = AccessoryRegistry.Instance
            ?? throw new InvalidOperationException("PlayerAccessory: AccessoryRegistry not initialized");

        AccessoryType type = registry.GetAccessoryTypeById(accessoryId);
        GameObject prefab = registry.GetAccessoryPrefabById(accessoryId);

        if (type != AccessoryType.None && prefab != null)
        {
            loadout.Equip(type, accessoryId);
            loadout.ResolveConflicts(type);
            SyncVisualsFromLoadout();
            RequestAccessoryUpdateServerRpc(loadout.BuildAccessoryJson());
            EquippedStateChanged?.Invoke();
        }
    }

    public void UpdateEquippedAccessories(List<int> accessoryIds)
    {
        if (!IsSpawned)
        {
            Debug.LogWarning("PlayerAccessory: cannot update accessories before OnNetworkSpawn");
            return;
        }

        var registry = AccessoryRegistry.Instance
            ?? throw new InvalidOperationException("PlayerAccessory: AccessoryRegistry not initialized");

        foreach (int accessoryId in accessoryIds)
        {
            AccessoryType type = registry.GetAccessoryTypeById(accessoryId);
            GameObject prefab = registry.GetAccessoryPrefabById(accessoryId);
            if (type != AccessoryType.None && prefab != null)
            {
                loadout.Equip(type, accessoryId);
                loadout.ResolveConflicts(type);
            }
        }

        SyncVisualsFromLoadout();
        RequestAccessoryUpdateServerRpc(loadout.BuildAccessoryJson());
        EquippedStateChanged?.Invoke();
    }

    public IAccessoryVisualSync GetVisualSync()
    {
        return GetComponent<IAccessoryVisualSync>()
            ?? throw new InvalidOperationException(
                "PlayerAccessory: IAccessoryVisualSync not available. Is AccessoryPool on this GameObject?");
    }

    public void UnequipAccessory(AccessoryType type)
    {
        if (!IsSpawned)
        {
            Debug.LogWarning("PlayerAccessory: cannot update accessories before OnNetworkSpawn");
            return;
        }

        if (!loadout.IsSlotOccupied(type)) return;
        loadout.Unequip(type);

        GetVisualSync().Unequip(type);

        RequestAccessoryUpdateServerRpc(loadout.BuildAccessoryJson());
        EquippedStateChanged?.Invoke();
    }

    public int GetEquippedItemId(AccessoryType type)
    {
        return loadout.GetEquippedItemId(type);
    }

    public void ClearAllEquipped()
    {
        if (!IsSpawned)
        {
            Debug.LogWarning("PlayerAccessory: cannot update accessories before OnNetworkSpawn");
            return;
        }

        var visualSync = GetVisualSync();
        foreach (AccessoryType type in allAccessoryTypes)
        {
            if (type == AccessoryType.None) continue;
            if (visualSync.IsSlotActive(type))
                visualSync.Unequip(type);
        }
        loadout.Clear();
        RequestAccessoryUpdateServerRpc("");
        EquippedStateChanged?.Invoke();
    }

    public void StoreEquippedAccessories()
    {
        List<int> equippedAccessoryIds = new();
        foreach (var equippedEntry in loadout.EquippedItems)
        {
            equippedAccessoryIds.Add(equippedEntry.Value);
        }

        ApiWriteQueue.Instance.Enqueue(
                (onSuccess, onError) => ShopAPI.Instance.UpdateEquippedAccessories(
                    equippedAccessoryIds,
                    (success, message) =>
                    {
                        if (success) onSuccess();
                        else onError(message);
                    },
                    onError
                ),
                "StoreEquippedAccessories"
            );
    }

    private static readonly AccessoryType[] allAccessoryTypes =
        (AccessoryType[])Enum.GetValues(typeof(AccessoryType));

    private void SyncVisualsFromLoadout()
    {
        var registry = AccessoryRegistry.Instance
            ?? throw new InvalidOperationException("PlayerAccessory: AccessoryRegistry not initialized");

        var equippedPrefabs = new Dictionary<AccessoryType, GameObject>();
        foreach (var equippedEntry in loadout.EquippedItems)
        {
            GameObject prefab = registry.GetAccessoryPrefabById(equippedEntry.Value);
            if (prefab != null)
                equippedPrefabs[equippedEntry.Key] = prefab;
        }

        SyncVisuals(equippedPrefabs);
    }

    private void SyncVisuals(Dictionary<AccessoryType, GameObject> equipped)
    {
        var visualSync = GetVisualSync();
        var registry = AccessoryRegistry.Instance
            ?? throw new InvalidOperationException("PlayerAccessory: AccessoryRegistry not initialized");

        foreach (AccessoryType type in allAccessoryTypes)
        {
            if (type == AccessoryType.None) continue;
            if (visualSync.IsSlotActive(type) && !equipped.ContainsKey(type))
                visualSync.Unequip(type);
        }

        foreach (var slotPrefabPair in equipped)
        {
            int itemId = registry.GetAccessoryIdByPrefab(slotPrefabPair.Value);
            if (itemId == -1) continue;
            if (visualSync.GetActiveItemId(slotPrefabPair.Key) == itemId) continue;
            visualSync.Equip(itemId, slotPrefabPair.Key, slotPrefabPair.Value, transform);
        }
    }

    private void OnAccessoryJsonChanged(string newValue)
    {
        if (string.IsNullOrEmpty(newValue)) return;

        Dictionary<AccessoryType, int> accessoryMap;
        try
        {
            accessoryMap = AccessoryLoadout.DeserializeAccessoryState(newValue);
        }
        catch (Exception e)
        {
#if UNITY_EDITOR
            Debug.LogError($"Failed to parse AccessoryJson: {e.Message}");
#endif
            return;
        }

        var registry = AccessoryRegistry.Instance
            ?? throw new InvalidOperationException("PlayerAccessory: AccessoryRegistry not initialized");

        var remoteEquipped = new Dictionary<AccessoryType, GameObject>();
        foreach (var slotIdPair in accessoryMap)
        {
            GameObject prefab = registry.GetAccessoryPrefabById(slotIdPair.Value);
            if (prefab != null)
                remoteEquipped[slotIdPair.Key] = prefab;
        }

        SyncVisuals(remoteEquipped);
    }
}
