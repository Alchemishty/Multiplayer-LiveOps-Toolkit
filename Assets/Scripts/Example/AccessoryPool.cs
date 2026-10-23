using UnityEngine;
using System.Collections.Generic;

public class AccessoryPool : MonoBehaviour, IAccessoryVisualSync
{
    private readonly Dictionary<int, GameObject> pooledInstances = new();
    private readonly Dictionary<AccessoryType, int> activeBySlot = new();
    private Transform cachedAvatarRoot;

    private static readonly Dictionary<AccessoryType, string> baseMeshNamesBySlot = new()
    {
        { AccessoryType.Head, "head-00" },
        { AccessoryType.Torso, "torso-00" },
        { AccessoryType.Arms, "arms-00" },
        { AccessoryType.Legs, "legs-00" },
        { AccessoryType.Feet, "feet-00" },
    };

    public GameObject Equip(int itemId, AccessoryType slotType, GameObject prefab, Transform avatarRoot)
    {
        if (prefab == null)
            throw new System.InvalidOperationException(
                $"AccessoryPool.Equip: prefab is null for item {itemId}");
        if (avatarRoot == null)
            throw new System.InvalidOperationException(
                "AccessoryPool.Equip: avatarRoot is null");

        cachedAvatarRoot = avatarRoot;

        DeactivateSlot(slotType);
        SetBaseMeshActive(slotType, false);

        if (pooledInstances.TryGetValue(itemId, out GameObject cachedInstance))
        {
            cachedInstance.SetActive(true);
            activeBySlot[slotType] = itemId;
            return cachedInstance;
        }

        GameObject instance;
        bool hasSkinnedMeshRenderer = prefab.GetComponentInChildren<SkinnedMeshRenderer>() != null;

        if (hasSkinnedMeshRenderer)
        {
            instance = SkinnedMeshAttacher.Attach(prefab, avatarRoot);
        }
        else
        {
            string boneName = GetAttachmentBoneName(slotType);
            var attachmentBone = FindBoneRecursive(avatarRoot, boneName) ?? avatarRoot;
            instance = Object.Instantiate(prefab, attachmentBone);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
        }

        if (instance == null)
            throw new System.InvalidOperationException(
                $"AccessoryPool.Equip: Failed to instantiate item {itemId}");

        instance.name = $"Acc_{slotType}_{itemId}";
        pooledInstances[itemId] = instance;
        activeBySlot[slotType] = itemId;
        return instance;
    }

    public void Unequip(AccessoryType slotType)
    {
        DeactivateSlot(slotType);
        SetBaseMeshActive(slotType, true);
    }

    public bool IsSlotActive(AccessoryType slotType)
    {
        return activeBySlot.ContainsKey(slotType);
    }

    public int GetActiveItemId(AccessoryType slotType)
    {
        return activeBySlot.TryGetValue(slotType, out int itemId) ? itemId : -1;
    }

    public void DrainPool()
    {
        foreach (var kvp in activeBySlot)
            SetBaseMeshActive(kvp.Key, true);

        foreach (var kvp in pooledInstances)
        {
            if (kvp.Value != null)
                Destroy(kvp.Value);
        }
        pooledInstances.Clear();
        activeBySlot.Clear();
    }

    private void DeactivateSlot(AccessoryType slotType)
    {
        if (activeBySlot.TryGetValue(slotType, out int currentItemId))
        {
            if (pooledInstances.TryGetValue(currentItemId, out GameObject currentInstance))
                currentInstance.SetActive(false);
            activeBySlot.Remove(slotType);
        }
    }

    public static string GetAttachmentBoneName(AccessoryType slotType)
    {
        switch (slotType)
        {
            case AccessoryType.Headwear:
            case AccessoryType.Hair:
            case AccessoryType.Facewear:
                return "Head";
            case AccessoryType.MainHand:
                return "RightHand";
            case AccessoryType.OffHand:
                return "LeftHand";
            case AccessoryType.Back:
                return "Spine2";
            default:
                return "Head";
        }
    }

    private static Transform FindBoneRecursive(Transform root, string boneName)
    {
        if (root.name == boneName) return root;
        foreach (Transform child in root)
        {
            var found = FindBoneRecursive(child, boneName);
            if (found != null) return found;
        }
        return null;
    }

    private void SetBaseMeshActive(AccessoryType slotType, bool isActive)
    {
        if (cachedAvatarRoot == null) return;
        if (!baseMeshNamesBySlot.TryGetValue(slotType, out string meshName)) return;

        var baseMesh = cachedAvatarRoot.Find(meshName);
        if (baseMesh != null)
            baseMesh.gameObject.SetActive(isActive);
    }

    private void OnDestroy()
    {
        DrainPool();
    }
}
