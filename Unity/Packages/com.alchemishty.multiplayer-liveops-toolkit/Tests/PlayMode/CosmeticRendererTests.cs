using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Equipment;
using MultiplayerLiveOpsToolkit.EquipmentVisuals;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class CosmeticRendererTests
{
    private readonly List<Object> ownedObjects = new List<Object>();
    private GameObject CreateObject(string name, Transform parent = null)
    {
        var instance = new GameObject(name);
        if (parent != null) instance.transform.SetParent(parent, false);
        else ownedObjects.Add(instance);
        return instance;
    }
    private CosmeticRenderer CreateRenderer(string name = "Avatar")
    {
        var renderer = CreateObject(name).AddComponent<CosmeticRenderer>();
        renderer.catalog = ScriptableObject.CreateInstance<CosmeticVisualCatalog>();
        ownedObjects.Add(renderer.catalog);
        renderer.slots = new[] { new CosmeticSlotBinding { slotId = "tail", attachment = CreateObject("Mount", renderer.transform).transform } };
        return renderer;
    }
    private void AddCosmetic(CosmeticRenderer renderer, string itemId, string slotId, GameObject prefab, CosmeticAttachmentMode mode = CosmeticAttachmentMode.Bone)
    {
        var definitions = new List<CosmeticVisualDefinition>(renderer.catalog.cosmetics);
        definitions.Add(new CosmeticVisualDefinition { itemId = itemId, slotId = slotId, prefab = prefab, attachmentMode = mode });
        renderer.catalog.cosmetics = definitions.ToArray();
    }
    private static EquipmentLoadout Loadout(params string[] pairs)
    {
        var items = new Dictionary<string, string>();
        for (int index = 0; index < pairs.Length; index += 2) items.Add(pairs[index], pairs[index + 1]);
        return new EquipmentLoadout(1, items);
    }
    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        foreach (var instance in ownedObjects) if (instance != null) Object.Destroy(instance);
        ownedObjects.Clear();
        yield return null;
    }
    [UnityTest]
    public IEnumerator Render_CustomStringSlot_AttachesPoolsAndRestoresBaseMeshes()
    {
        var renderer = CreateRenderer();
        var baseMesh = CreateObject("BaseTail", renderer.transform);
        renderer.slots[0].baseObjects = new[] { baseMesh };
        AddCosmetic(renderer, "fox-tail", "tail", CreateObject("TailPrefab"));
        renderer.Render(Loadout("tail", "fox-tail"));
        var attachment = renderer.slots[0].attachment;
        Assert.That(baseMesh.activeSelf, Is.False);
        Assert.That(attachment.childCount, Is.EqualTo(1));
        var firstInstance = attachment.GetChild(0);
        renderer.Clear();
        Assert.That(baseMesh.activeSelf, Is.True);
        Assert.That(firstInstance.gameObject.activeSelf, Is.False);
        renderer.Render(Loadout("tail", "fox-tail"));
        Assert.That(attachment.childCount, Is.EqualTo(1));
        Assert.That(attachment.GetChild(0), Is.SameAs(firstInstance));
        yield return null;
    }
    [UnityTest]
    public IEnumerator Render_TwoAvatars_PoolsAndBaseStateAreIndependent()
    {
        var first = CreateRenderer("First");
        var second = CreateRenderer("Second");
        var prefab = CreateObject("TailPrefab");
        AddCosmetic(first, "tail-item", "tail", prefab);
        AddCosmetic(second, "tail-item", "tail", prefab);
        first.Render(Loadout("tail", "tail-item"));
        second.Render(Loadout("tail", "tail-item"));
        first.Clear();
        Assert.That(first.slots[0].attachment.GetChild(0).gameObject.activeSelf, Is.False);
        Assert.That(second.slots[0].attachment.GetChild(0).gameObject.activeSelf, Is.True);
        yield return null;
    }
    [UnityTest]
    public IEnumerator Render_InvalidMapping_RetainsWholePreviousLook()
    {
        var renderer = CreateRenderer();
        AddCosmetic(renderer, "tail-item", "tail", CreateObject("TailPrefab"));
        renderer.Render(Loadout("tail", "tail-item"));
        var previous = renderer.slots[0].attachment.GetChild(0).gameObject;
        var error = Assert.Throws<InvalidOperationException>(() => renderer.Render(Loadout("tail", "missing")));
        Assert.That(error.Message, Does.Contain("missing"));
        Assert.That(previous.activeSelf, Is.True);
        yield return null;
    }
    [UnityTest]
    public IEnumerator Render_SkinnedReplacement_RemapsBonesAndRootWithExplicitAlias()
    {
        var renderer = CreateRenderer();
        renderer.rigRoot = CreateObject("Skeleton", renderer.transform).transform;
        var targetBone = CreateObject("DifferentBone", renderer.rigRoot).transform;
        renderer.boneAliases = new[] { new CosmeticBoneBinding { sourceBoneName = "SourceBone", targetBone = targetBone } };
        var prefab = CreateObject("SkinnedPrefab");
        var sourceBone = CreateObject("SourceBone", prefab.transform).transform;
        var meshRenderer = CreateObject("Skin", prefab.transform).AddComponent<SkinnedMeshRenderer>();
        meshRenderer.bones = new[] { sourceBone };
        meshRenderer.rootBone = sourceBone;
        AddCosmetic(renderer, "skin", "tail", prefab, CosmeticAttachmentMode.Skinned);
        renderer.Render(Loadout("tail", "skin"));
        var displayed = renderer.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Assert.That(displayed, Has.Length.EqualTo(1));
        Assert.That(displayed[0].bones[0], Is.SameAs(targetBone));
        Assert.That(displayed[0].rootBone, Is.SameAs(targetBone));
        yield return null;
    }
    [UnityTest]
    public IEnumerator Render_AvatarRootRigAndPooledAccessoryNames_RemapOnlyAvatarBones()
    {
        var renderer = CreateRenderer();
        renderer.rigRoot = renderer.transform;
        var targetBone = CreateObject("Joint", renderer.rigRoot).transform;
        var accessory = CreateObject("AccessoryTemplate");
        CreateObject("Joint", accessory.transform);
        AddCosmetic(renderer, "attachment", "tail", accessory);
        renderer.Render(Loadout("tail", "attachment"));
        var prefab = CreateObject("SkinTemplate");
        var sourceBone = CreateObject("Joint", prefab.transform).transform;
        var skin = CreateObject("Skin", prefab.transform).AddComponent<SkinnedMeshRenderer>();
        skin.bones = new[] { sourceBone }; skin.rootBone = sourceBone;
        AddCosmetic(renderer, "skin", "tail", prefab, CosmeticAttachmentMode.Skinned);
        renderer.Render(Loadout("tail", "skin"));
        var displayed = renderer.GetComponentInChildren<SkinnedMeshRenderer>(true);
        Assert.That(displayed.bones[0], Is.SameAs(targetBone));
        yield return null;
    }
    [UnityTest]
    public IEnumerator ResetBindings_CharacterReplacement_ReleasesPoolAndUsesNewMount()
    {
        var renderer = CreateRenderer();
        AddCosmetic(renderer, "tail-item", "tail", CreateObject("TailPrefab"));
        renderer.Render(Loadout("tail", "tail-item"));
        var oldInstance = renderer.slots[0].attachment.GetChild(0);
        renderer.ResetBindings();
        renderer.slots[0].attachment = CreateObject("ReplacementMount", renderer.transform).transform;
        renderer.Render(Loadout("tail", "tail-item"));
        yield return null;
        Assert.That(oldInstance == null, Is.True);
        Assert.That(renderer.slots[0].attachment.childCount, Is.EqualTo(1));
    }
    [UnityTest]
    public IEnumerator Render_MultiSlotOutfit_HidesAndRestoresBothBaseParts()
    {
        var renderer = CreateRenderer();
        var tailBase = CreateObject("TailBase", renderer.transform);
        var wingBase = CreateObject("WingBase", renderer.transform);
        renderer.slots = new[] {
            new CosmeticSlotBinding { slotId = "tail", attachment = renderer.transform, baseObjects = new[] { tailBase } },
            new CosmeticSlotBinding { slotId = "wings", attachment = renderer.transform, baseObjects = new[] { wingBase } }
        };
        AddCosmetic(renderer, "tail-item", "tail", CreateObject("TailPrefab"));
        AddCosmetic(renderer, "wing-item", "wings", CreateObject("WingPrefab"));
        renderer.Render(Loadout("tail", "tail-item", "wings", "wing-item"));
        Assert.That(tailBase.activeSelf || wingBase.activeSelf, Is.False);
        renderer.Render(Loadout("wings", "wing-item"));
        Assert.That(tailBase.activeSelf, Is.True);
        Assert.That(wingBase.activeSelf, Is.False);
        renderer.Clear(); Assert.That(wingBase.activeSelf, Is.True);
        yield return null;
    }
    [UnityTest]
    public IEnumerator ResetBindings_SkinnedRootRig_CanRenderAgainInSameFrame()
    {
        var renderer = CreateRenderer(); renderer.rigRoot = renderer.transform;
        var targetBone = CreateObject("Joint", renderer.transform).transform;
        var prefab = CreateObject("SkinTemplate");
        var sourceBone = CreateObject("Joint", prefab.transform).transform;
        var skin = CreateObject("Skin", prefab.transform).AddComponent<SkinnedMeshRenderer>();
        skin.bones = new[] { sourceBone }; skin.rootBone = sourceBone;
        AddCosmetic(renderer, "skin", "tail", prefab, CosmeticAttachmentMode.Skinned);
        renderer.Render(Loadout("tail", "skin"));
        renderer.ResetBindings();
        renderer.Render(Loadout("tail", "skin"));
        var displayed = renderer.GetComponentInChildren<SkinnedMeshRenderer>(true);
        Assert.That(displayed.bones[0], Is.SameAs(targetBone));
        yield return null;
    }
    [UnityTest]
    public IEnumerator Render_MissingSkinBone_FailsBeforeHidingBase()
    {
        var renderer = CreateRenderer();
        var baseMesh = CreateObject("Base", renderer.transform);
        renderer.slots[0].baseObjects = new[] { baseMesh };
        renderer.rigRoot = CreateObject("Skeleton", renderer.transform).transform;
        var prefab = CreateObject("SkinPrefab");
        var sourceBone = CreateObject("MissingBone", prefab.transform).transform;
        var skin = prefab.AddComponent<SkinnedMeshRenderer>();
        skin.bones = new[] { sourceBone }; skin.rootBone = sourceBone;
        AddCosmetic(renderer, "skin", "tail", prefab, CosmeticAttachmentMode.Skinned);
        Assert.Throws<InvalidOperationException>(() => renderer.Render(Loadout("tail", "skin")));
        Assert.That(baseMesh.activeSelf, Is.True);
        yield return null;
    }
    [UnityTest]
    public IEnumerator Clear_InitiallyHiddenBase_RemainsHiddenAndDisableRestoresVisibleBase()
    {
        var renderer = CreateRenderer();
        var visibleBase = CreateObject("Visible", renderer.transform);
        var hiddenBase = CreateObject("Hidden", renderer.transform); hiddenBase.SetActive(false);
        renderer.slots[0].baseObjects = new[] { visibleBase, hiddenBase };
        AddCosmetic(renderer, "tail-item", "tail", CreateObject("TailPrefab"));
        renderer.Render(Loadout("tail", "tail-item"));
        renderer.enabled = false;
        Assert.That(visibleBase.activeSelf, Is.True);
        Assert.That(hiddenBase.activeSelf, Is.False);
        yield return null;
    }
}
