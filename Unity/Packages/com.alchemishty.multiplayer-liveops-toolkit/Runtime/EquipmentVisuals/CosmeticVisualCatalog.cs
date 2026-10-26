using System;
using UnityEngine;
namespace MultiplayerLiveOpsToolkit.EquipmentVisuals
{
    public enum CosmeticAttachmentMode { Bone, Skinned }
    [Serializable]
    public sealed class CosmeticVisualDefinition
    {
        public string itemId;
        public string slotId;
        public GameObject prefab;
        public CosmeticAttachmentMode attachmentMode;
    }
    [CreateAssetMenu(menuName = "LiveOps/Cosmetic Visual Catalog")]
    public sealed class CosmeticVisualCatalog : ScriptableObject
    {
        public CosmeticVisualDefinition[] cosmetics = Array.Empty<CosmeticVisualDefinition>();
    }
    [Serializable]
    public sealed class CosmeticSlotBinding
    {
        public string slotId;
        public Transform attachment;
        public GameObject[] baseObjects = Array.Empty<GameObject>();
    }
    [Serializable]
    public sealed class CosmeticBoneBinding
    {
        public string sourceBoneName;
        public Transform targetBone;
    }
}
