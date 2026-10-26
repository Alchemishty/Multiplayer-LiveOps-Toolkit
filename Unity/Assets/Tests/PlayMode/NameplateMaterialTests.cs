using MultiplayerLiveOpsToolkit.Netcode.Avatars;
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
    private UnityEngine.SceneManagement.Scene loadedExampleScene;
    private Unity.Netcode.NetworkManager sceneManager;
    private AccessoryRegistry sceneRegistry;
    [UnityTearDown]
    public IEnumerator UnloadExampleScene()
    {
        if (loadedExampleScene.IsValid())
        {
            if (sceneManager != null) Object.DestroyImmediate(sceneManager.gameObject);
            if (sceneRegistry != null) Object.DestroyImmediate(sceneRegistry.gameObject);
            if (loadedExampleScene.isLoaded) yield return UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(loadedExampleScene);
        }
    }
    [UnityTest]
    public IEnumerator ExampleScene_LoadedFromDisk_HasSpawnableVerifiedPlayerPrefab()
    {
        var exampleScene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/Scenes/ExampleScene.unity");
        if (!exampleScene.IsValid() || !exampleScene.isLoaded)
        {
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Scenes/ExampleScene.unity",
                new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Additive));
            exampleScene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/Scenes/ExampleScene.unity");
            loadedExampleScene = exampleScene;
        }
        var managers = Object.FindObjectsOfType<Unity.Netcode.NetworkManager>();
        Assert.That(managers.Length, Is.EqualTo(1), "The example must be the only NetworkManager in this isolated test.");
        var manager = managers[0];
        sceneManager = manager;
        sceneRegistry = Object.FindObjectOfType<AccessoryRegistry>();
        Assert.That(manager, Is.Not.Null, "Saved ExampleScene must contain NetworkManager.");
        var prefab = manager.NetworkConfig.PlayerPrefab;
        Assert.That(prefab != null, Is.True, "Saved ExampleScene must resolve its verified player prefab.");
        Assert.That(prefab.GetComponent<Unity.Netcode.NetworkObject>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<PlayerNameplate>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<PlayerAccessory>(), Is.Not.Null);
    }

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
