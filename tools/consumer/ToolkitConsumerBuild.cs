using MultiplayerLiveOpsToolkit.Netcode.Avatars;
using MultiplayerLiveOpsToolkit.EquipmentVisuals;
using System;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ToolkitConsumerBuild
{
    public static void BuildNetcode() => Build("NetcodeConsumer");
    public static void BuildSdk() => Build("PackageConsumer");
    public static void BuildExistingSdk()
    {
        const string path = "Assets/Scenes/Consumer.unity";
        if (!File.Exists(path)) throw new InvalidOperationException("Upgrade lost the consuming game's scene.");
        EditorSceneManager.OpenScene(path);
        if (UnityEngine.Object.FindFirstObjectByType<PackageConsumer>() == null) throw new InvalidOperationException("Upgrade lost the consumer component binding.");
        BuildSavedScene();
    }
    public static void ImportSample()
    {
        var package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Single(entry => entry.name ==
#if UNITY_6000_0_OR_NEWER
            "com.unity.ugui"
#else
            "com.unity.textmeshpro"
#endif
        );
        AssetDatabase.importPackageCompleted += OnEssentialsImported;
        AssetDatabase.importPackageFailed += OnImportFailed;
        AssetDatabase.importPackageCancelled += OnImportCancelled;
        AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
    }
    public static void ImportFullDemo()
    {
        var sample = UnityEditor.PackageManager.UI.Sample.FindByPackage("com.alchemishty.multiplayer-liveops-toolkit", null).Single(entry => entry.displayName == "Full Demo");
        if (!sample.Import(UnityEditor.PackageManager.UI.Sample.ImportOptions.OverridePreviousImports)) throw new InvalidOperationException("Full Demo import failed.");
    }
    public static void BuildFullDemo()
    {
        var sample = UnityEditor.PackageManager.UI.Sample.FindByPackage("com.alchemishty.multiplayer-liveops-toolkit", null).Single(entry => entry.displayName == "Full Demo");
        string scenePath = Path.Combine(sample.importPath, "Scenes/ExampleScene.unity").Replace('\\', '/');
        if (Path.IsPathRooted(scenePath)) scenePath = FileUtil.GetProjectRelativePath(scenePath);
        if (!File.Exists(scenePath)) throw new InvalidOperationException("Imported Full Demo scene is missing.");
        var scene = EditorSceneManager.OpenScene(scenePath);
        foreach (var root in scene.GetRootGameObjects())
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0)
                    throw new InvalidOperationException("Missing demo script: " + transform.name);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scenePath }, locationPathName = Environment.GetEnvironmentVariable("SMOKE_BUILD"), target = BuildTarget.StandaloneOSX, options = BuildOptions.Development | BuildOptions.StrictMode });
        if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Full Demo build failed.");
    }
    private static void RemoveImportListeners()
    {
        AssetDatabase.importPackageCompleted -= OnEssentialsImported;
        AssetDatabase.importPackageFailed -= OnImportFailed;
        AssetDatabase.importPackageCancelled -= OnImportCancelled;
    }
    private static void OnEssentialsImported(string name)
    {
        RemoveImportListeners();
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/TextMesh Pro/Resources/TMP Settings.asset") == null) { EditorApplication.Exit(1); return; }
        var samples = UnityEditor.PackageManager.UI.Sample.FindByPackage("com.alchemishty.multiplayer-liveops-toolkit", null).ToArray();
        if (!samples.Single(sample => sample.displayName == "Full Demo").Import(UnityEditor.PackageManager.UI.Sample.ImportOptions.OverridePreviousImports)) { EditorApplication.Exit(1); return; }
        AssetDatabase.Refresh();
        EditorApplication.Exit(0);
    }
    private static void OnImportFailed(string name, string error) { RemoveImportListeners(); Debug.LogError(error); EditorApplication.Exit(1); }
    private static void OnImportCancelled(string name) { RemoveImportListeners(); EditorApplication.Exit(1); }
    private static void Build(string componentName)
    {
        PlayerSettings.productName = "ToolkitConsumer";
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Camera", typeof(Camera)); var light = new GameObject("Light", typeof(Light)).GetComponent<Light>(); light.type = LightType.Directional;
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(componentName)).FirstOrDefault(value => value != null);
        if (type == null) throw new InvalidOperationException("Consumer component was not compiled: " + componentName);
        var consumer = new GameObject("Consumer", type);
        if (componentName == "NetcodeConsumer")
        {
            Directory.CreateDirectory("Assets/Prefabs");
            var source = new GameObject("Portable avatar");
            var catalog = ScriptableObject.CreateInstance<CosmeticVisualCatalog>();
            var hat = new GameObject("Hat template"); var weapon = new GameObject("Weapon template");
            try
            {
                var hatPrefab = PrefabUtility.SaveAsPrefabAsset(hat, "Assets/Prefabs/Hat.prefab");
                var weaponPrefab = PrefabUtility.SaveAsPrefabAsset(weapon, "Assets/Prefabs/Weapon.prefab");
                catalog.cosmetics = new[] {
                    new CosmeticVisualDefinition { itemId = "1", slotId = "headwear", prefab = hatPrefab },
                    new CosmeticVisualDefinition { itemId = "2", slotId = "mainhand", prefab = weaponPrefab }
                };
                AssetDatabase.CreateAsset(catalog, "Assets/Prefabs/VisualCatalog.asset");
                source.AddComponent<Unity.Netcode.NetworkObject>();
                source.AddComponent<MultiplayerLiveOpsToolkit.CharacterControls.ClientNetworkTransform>();
                var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath("Assets/Prefabs/Movement.controller");
                controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
                var idle = controller.layers[0].stateMachine.AddState("Idle");
                controller.layers[0].stateMachine.defaultState = idle;
                var animator = source.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                source.AddComponent<MultiplayerLiveOpsToolkit.CharacterControls.ClientNetworkAnimator>().Animator = animator;
                source.AddComponent<NetworkPlayerIdentity>(); source.AddComponent<PlayerAccessory>(); source.AddComponent<PlayerAvatarSetup>();
                var renderer = source.AddComponent<CosmeticRenderer>(); renderer.catalog = catalog;
                renderer.slots = new[] { new CosmeticSlotBinding { slotId = "headwear", attachment = source.transform }, new CosmeticSlotBinding { slotId = "mainhand", attachment = source.transform } };
                var prefab = PrefabUtility.SaveAsPrefabAsset(source, "Assets/Prefabs/Avatar.prefab");
                var fields = new SerializedObject(consumer.GetComponent(type)); fields.FindProperty("avatarPrefab").objectReferenceValue = prefab; fields.ApplyModifiedPropertiesWithoutUndo();
            }
            finally { UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(hat); UnityEngine.Object.DestroyImmediate(weapon); }
        }
        Directory.CreateDirectory("Assets/Scenes"); EditorSceneManager.SaveScene(scene, "Assets/Scenes/Consumer.unity");
        BuildSavedScene();
    }
    private static void BuildSavedScene()
    {
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { "Assets/Scenes/Consumer.unity" }, locationPathName = Environment.GetEnvironmentVariable("SMOKE_BUILD"), target = BuildTarget.StandaloneOSX, options = BuildOptions.Development | BuildOptions.StrictMode });
        if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Consumer build failed: " + report.summary.result);
    }
}
