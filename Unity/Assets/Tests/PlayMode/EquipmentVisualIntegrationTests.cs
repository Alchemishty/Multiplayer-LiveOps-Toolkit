#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Equipment;
using MultiplayerLiveOpsToolkit.EquipmentVisuals;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class EquipmentVisualIntegrationTests
{
    [UnityTest]
    public IEnumerator SavedAvatar_AllAuthoredCosmetics_RenderAndRestoreWithoutRigErrors()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/PlayerAvatar.prefab");
        Assert.That(prefab != null, Is.True);
        var avatar = Object.Instantiate(prefab);
        try
        {
            var renderer = avatar.GetComponent<CosmeticRenderer>();
            Assert.That(renderer, Is.Not.Null);
            var baseStates = new Dictionary<GameObject, bool>();
            foreach (var slot in renderer.slots)
                foreach (var baseObject in slot.baseObjects) baseStates[baseObject] = baseObject.activeSelf;
            foreach (var cosmetic in renderer.catalog.cosmetics)
            {
                Assert.DoesNotThrow(() => renderer.Render(new EquipmentLoadout(1,
                    new Dictionary<string, string> { { cosmetic.slotId, cosmetic.itemId } })), cosmetic.itemId);
                renderer.Clear();
                foreach (var state in baseStates) Assert.That(state.Key.activeSelf, Is.EqualTo(state.Value));
            }
            yield return null;
        }
        finally { Object.DestroyImmediate(avatar); }
    }
}
#endif
