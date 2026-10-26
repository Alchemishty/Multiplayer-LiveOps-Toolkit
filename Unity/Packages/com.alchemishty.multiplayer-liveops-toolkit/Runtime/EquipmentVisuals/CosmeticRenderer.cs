using System;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Equipment;
using UnityEngine;

namespace MultiplayerLiveOpsToolkit.EquipmentVisuals
{
    /// <summary>Per-avatar pooled rendering with explicit assets, slots and rig bindings.</summary>
    public sealed class CosmeticRenderer : MonoBehaviour, IEquipmentRenderer
    {
        public CosmeticVisualCatalog catalog;
        public Transform rigRoot;
        public CosmeticSlotBinding[] slots = Array.Empty<CosmeticSlotBinding>();
        public CosmeticBoneBinding[] boneAliases = Array.Empty<CosmeticBoneBinding>();
        private Dictionary<string, Transform> targetBones;
        private readonly Dictionary<string, GameObject> pooledInstances = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly Dictionary<GameObject, bool> originalBaseStates = new Dictionary<GameObject, bool>();

        public void Render(EquipmentLoadout loadout)
        {
            if (loadout == null) throw new ArgumentNullException(nameof(loadout));
            if (!isActiveAndEnabled) throw new InvalidOperationException("Enable CosmeticRenderer before rendering equipment.");
            var definitions = ReadDefinitions();
            var slotBindings = ReadSlots();
            var desired = new Dictionary<string, CosmeticVisualDefinition>(StringComparer.Ordinal);
            foreach (var item in loadout.ItemsBySlot)
            {
                if (!definitions.TryGetValue(item.Value, out var definition) || definition.slotId != item.Key || !slotBindings.ContainsKey(item.Key))
                    throw new InvalidOperationException("Missing visual mapping for cosmetic " + item.Value + " slot " + item.Key + " revision " + loadout.Revision + ".");
                desired.Add(item.Key, definition);
            }
            if (rigRoot != null && targetBones == null)
            {
                if (!rigRoot.IsChildOf(transform)) throw new InvalidOperationException("Avatar rig must belong to this renderer's character.");
                // Capture avatar bones before any cosmetic instance can enter the hierarchy.
                targetBones = CosmeticSkinBinding.CreateBoneMap(rigRoot, boneAliases);
            }
            // Prepare every missing instance before changing the currently displayed look.
            var created = new List<string>();
            try
            {
                foreach (var entry in desired)
                {
                    if (pooledInstances.TryGetValue(entry.Value.itemId, out var cached) && cached != null) continue;
                    var instance = CreateInstance(entry.Value, slotBindings[entry.Key]);
                    pooledInstances[entry.Value.itemId] = instance;
                    created.Add(entry.Value.itemId);
                }
            }
            catch
            {
                foreach (string itemId in created) { DestroyInstance(pooledInstances[itemId]); pooledInstances.Remove(itemId); }
                throw;
            }
            foreach (var binding in slotBindings.Values)
                foreach (var baseObject in binding.baseObjects)
                    if (!originalBaseStates.ContainsKey(baseObject)) originalBaseStates.Add(baseObject, baseObject.activeSelf);
            Clear();
            foreach (var entry in desired)
            {
                foreach (var baseObject in slotBindings[entry.Key].baseObjects) baseObject.SetActive(false);
                pooledInstances[entry.Value.itemId].SetActive(true);
            }
        }

        public void Clear()
        {
            foreach (var instance in pooledInstances.Values) if (instance != null) instance.SetActive(false);
            foreach (var entry in originalBaseStates) if (entry.Key != null) entry.Key.SetActive(entry.Value);
        }

        /// <summary>Release pooled visuals before changing character/catalog bindings at runtime.</summary>
        public void ResetBindings()
        {
            Clear();
            foreach (var instance in pooledInstances.Values) if (instance != null) DestroyInstance(instance);
            pooledInstances.Clear();
            originalBaseStates.Clear();
            targetBones = null;
        }

        private Dictionary<string, CosmeticVisualDefinition> ReadDefinitions()
        {
            if (catalog == null || catalog.cosmetics == null) throw new InvalidOperationException("CosmeticRenderer requires a cosmetic visual catalog.");
            var result = new Dictionary<string, CosmeticVisualDefinition>(StringComparer.Ordinal);
            foreach (var definition in catalog.cosmetics)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.itemId) || string.IsNullOrWhiteSpace(definition.slotId) || definition.prefab == null
                    || !Enum.IsDefined(typeof(CosmeticAttachmentMode), definition.attachmentMode))
                    throw new InvalidOperationException("Cosmetic visual definitions require item ID, slot ID, prefab and a valid attachment mode.");
                if (result.ContainsKey(definition.itemId)) throw new InvalidOperationException("Duplicate cosmetic visual item: " + definition.itemId);
                result.Add(definition.itemId, definition);
            }
            return result;
        }

        private Dictionary<string, CosmeticSlotBinding> ReadSlots()
        {
            if (slots == null) throw new InvalidOperationException("CosmeticRenderer requires explicit slot bindings.");
            var result = new Dictionary<string, CosmeticSlotBinding>(StringComparer.Ordinal);
            foreach (var binding in slots)
            {
                if (binding == null || string.IsNullOrWhiteSpace(binding.slotId) || binding.attachment == null || !binding.attachment.IsChildOf(transform) || binding.baseObjects == null)
                    throw new InvalidOperationException("Cosmetic slots require an ID, an attachment within this avatar and explicit base objects.");
                if (result.ContainsKey(binding.slotId)) throw new InvalidOperationException("Duplicate cosmetic visual slot: " + binding.slotId);
                foreach (var baseObject in binding.baseObjects)
                {
                    if (baseObject == null || !baseObject.transform.IsChildOf(transform) || binding.attachment.IsChildOf(baseObject.transform)
                        || (rigRoot != null && rigRoot.IsChildOf(baseObject.transform)))
                        throw new InvalidOperationException("Base meshes must belong to this avatar and cannot contain its attachment or rig: " + binding.slotId);
                }
                result.Add(binding.slotId, binding);
            }
            return result;
        }

        private GameObject CreateInstance(CosmeticVisualDefinition definition, CosmeticSlotBinding binding)
        {
            var staging = new GameObject("Cosmetic staging");
            staging.SetActive(false);
            staging.transform.SetParent(transform, false);
            GameObject instance = null;
            try
            {
                instance = Instantiate(definition.prefab, staging.transform, false);
                instance.SetActive(false);
                if (definition.attachmentMode == CosmeticAttachmentMode.Skinned)
                {
                    if (rigRoot == null || !rigRoot.IsChildOf(transform)) throw new InvalidOperationException("Skinned cosmetics require a rig root within this avatar.");
                    CosmeticSkinBinding.Bind(instance, targetBones);
                }
                instance.transform.SetParent(definition.attachmentMode == CosmeticAttachmentMode.Skinned ? transform : binding.attachment, false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                instance.name = "Cosmetic_" + definition.itemId;
                return instance;
            }
            catch { if (instance != null) DestroyInstance(instance); throw; }
            finally { DestroyInstance(staging); }
        }

        private static void DestroyInstance(GameObject instance)
        {
            instance.SetActive(false);
            // Destroy is deferred; exclude retired bones from same-frame binding rebuilds now.
            instance.transform.SetParent(null, false);
            Destroy(instance);
        }

        private void OnDisable() => Clear();
        private void OnDestroy() => ResetBindings();
    }
}
