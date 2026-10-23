using UnityEngine;
using System.Collections.Generic;

public static class SkinnedMeshAttacher
{
    public static GameObject Attach(GameObject accessoryPrefab, Transform avatarRoot)
    {
        var avatarRig = FindRig(avatarRoot)
            ?? throw new System.InvalidOperationException(
                $"SkinnedMeshAttacher: No rig found on avatar '{avatarRoot.name}'");

        var avatarBones = BuildBoneMap(avatarRig);

        var instance = Object.Instantiate(accessoryPrefab, avatarRoot);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        var skinnedRenderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>();
        foreach (var skinnedMeshRenderer in skinnedRenderers)
        {
            RemapBones(skinnedMeshRenderer, avatarBones);
            if (skinnedMeshRenderer.rootBone != null &&
                avatarBones.TryGetValue(skinnedMeshRenderer.rootBone.name, out Transform newRoot))
                skinnedMeshRenderer.rootBone = newRoot;
        }

        var accessoryRig = FindRig(instance.transform);
        if (accessoryRig != null)
            Object.DestroyImmediate(accessoryRig.gameObject);

        return instance;
    }

    static Transform FindRig(Transform root)
    {
        string[] rigNames = { "Rig", "Armature", "Hips" };
        foreach (var rigName in rigNames)
        {
            var found = root.Find(rigName);
            if (found != null) return found;
        }
        foreach (Transform child in root)
        {
            if (child.name.Contains("Rig") || child.name.Contains("Armature"))
                return child;
        }
        return null;
    }

    static Dictionary<string, Transform> BuildBoneMap(Transform rigRoot)
    {
        var boneMap = new Dictionary<string, Transform>();
        var traversalQueue = new Queue<Transform>();
        traversalQueue.Enqueue(rigRoot);
        while (traversalQueue.Count > 0)
        {
            var bone = traversalQueue.Dequeue();
            if (!boneMap.ContainsKey(bone.name))
                boneMap[bone.name] = bone;
            foreach (Transform child in bone)
                traversalQueue.Enqueue(child);
        }
        return boneMap;
    }

    static void RemapBones(SkinnedMeshRenderer skinnedMeshRenderer, Dictionary<string, Transform> boneMap)
    {
        var originalBones = skinnedMeshRenderer.bones;
        var remappedBones = new Transform[originalBones.Length];
        int failedCount = 0;

        for (int i = 0; i < originalBones.Length; i++)
        {
            if (originalBones[i] != null && boneMap.TryGetValue(originalBones[i].name, out Transform mapped))
                remappedBones[i] = mapped;
            else
            {
                remappedBones[i] = null;
                failedCount++;
            }
        }

        if (failedCount > 0)
            Debug.LogError($"SkinnedMeshAttacher: {skinnedMeshRenderer.name} — " +
                           $"{failedCount}/{originalBones.Length} bones failed to remap");

        skinnedMeshRenderer.bones = remappedBones;
    }
}
