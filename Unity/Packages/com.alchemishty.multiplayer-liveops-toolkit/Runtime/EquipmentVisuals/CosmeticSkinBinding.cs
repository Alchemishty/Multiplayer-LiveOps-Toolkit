using System;
using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerLiveOpsToolkit.EquipmentVisuals
{
    internal static class CosmeticSkinBinding
    {
        internal static Dictionary<string, Transform> CreateBoneMap(Transform rigRoot, CosmeticBoneBinding[] aliases)
        {
            if (rigRoot == null) throw new InvalidOperationException("Skinned cosmetics require an explicit avatar rig root.");
            if (aliases == null) throw new InvalidOperationException("Bone alias bindings cannot be null.");
            var targetBones = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var bone in rigRoot.GetComponentsInChildren<Transform>(true))
            {
                if (targetBones.ContainsKey(bone.name)) targetBones[bone.name] = null;
                else targetBones.Add(bone.name, bone);
            }
            var aliasNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var alias in aliases)
            {
                if (alias == null || string.IsNullOrWhiteSpace(alias.sourceBoneName) || alias.targetBone == null || !alias.targetBone.IsChildOf(rigRoot) || !aliasNames.Add(alias.sourceBoneName))
                    throw new InvalidOperationException("Bone aliases require unique source names and target bones within the configured rig.");
                targetBones[alias.sourceBoneName] = alias.targetBone;
            }
            return targetBones;
        }
        internal static void Bind(GameObject instance, Dictionary<string, Transform> targetBones)
        {
            var renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("Skinned cosmetic has no SkinnedMeshRenderer: " + instance.name);
            foreach (var renderer in renderers)
            {
                var sourceBones = renderer.bones;
                var remappedBones = new Transform[sourceBones.Length];
                for (int index = 0; index < sourceBones.Length; index++) remappedBones[index] = Resolve(sourceBones[index], targetBones);
                renderer.rootBone = Resolve(renderer.rootBone, targetBones);
                renderer.bones = remappedBones;
            }
            // The avatar's Animator drives the rebound skin; cosmetic prefabs must not drive a second animator.
            foreach (var animator in instance.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        }
        private static Transform Resolve(Transform source, Dictionary<string, Transform> targets)
        {
            if (source == null || !targets.TryGetValue(source.name, out var target) || target == null)
                throw new InvalidOperationException("Missing or ambiguous cosmetic bone binding: " + (source == null ? "<null>" : source.name));
            return target;
        }
    }
}
