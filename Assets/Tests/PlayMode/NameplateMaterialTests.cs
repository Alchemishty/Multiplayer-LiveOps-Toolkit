#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

public class NameplateMaterialTests
{
    private GameObject canvasObject;
    private GameObject avatarObject;

    [TearDown]
    public void TearDown()
    {
        if (avatarObject != null) Object.DestroyImmediate(avatarObject);
        if (canvasObject != null) Object.DestroyImmediate(canvasObject);
    }

    [UnityTest]
    public IEnumerator SpawnAvatar_WithOverlayLabels_PreservesOverlayDepthTest()
    {
        canvasObject = new GameObject("Overlay canvas", typeof(Canvas));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var labelObject = new GameObject("Overlay label");
        labelObject.transform.SetParent(canvasObject.transform, false);
        var overlayLabel = labelObject.AddComponent<TextMeshProUGUI>();
        overlayLabel.text = "Shop gold: 500";
        Canvas.ForceUpdateCanvases();
        overlayLabel.ForceMeshUpdate();
        var overlayMaterial = overlayLabel.fontSharedMaterial;

        var playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Player/PlayerAvatar.prefab");
        Assert.That(playerPrefab, Is.Not.Null);
        var prefabNameplate = playerPrefab.GetComponentInChildren<TextMeshPro>(true);
        Assert.That(prefabNameplate.fontSharedMaterial, Is.Not.SameAs(overlayMaterial),
            "The player prefab must not share the overlay font material: TMP changes depth testing when it spawns.");
        avatarObject = Object.Instantiate(playerPrefab);
        var nameplateLabel = avatarObject.GetComponentInChildren<TextMeshPro>(true);
        Assert.That(nameplateLabel, Is.Not.Null);
        Assert.That(nameplateLabel.fontSharedMaterial, Is.Not.SameAs(overlayMaterial),
            "World-space nameplates need a separate material from screen-space UI.");
        yield break;
    }
}
#endif
