using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class UnityPackageBuild
{
    private const string ROOT = "Assets/MultiplayerLiveOpsToolkit";

    public static void ImportEssentials()
    {
        var package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Single(entry => entry.name ==
#if UNITY_6000_0_OR_NEWER
            "com.unity.ugui"
#else
            "com.unity.textmeshpro"
#endif
        );
        AssetDatabase.importPackageCompleted += name => EditorApplication.Exit(File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset") ? 0 : 1);
        AssetDatabase.importPackageFailed += (name, error) => { Debug.LogError(error); EditorApplication.Exit(1); };
        AssetDatabase.importPackageCancelled += name => EditorApplication.Exit(1);
        AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
    }

    public static void Export()
    {
        string output = RequiredEnvironment("LIVEOPS_EXPORT_PATH");
        AssetDatabase.ExportPackage(ROOT, output, ExportPackageOptions.Recurse);
        if (!File.Exists(output)) throw new InvalidOperationException("Unity did not create the asset package.");
    }

    public static void Verify()
    {
        const string scenePath = ROOT + "/FullDemo/Scenes/ExampleScene.unity";
        var scene = EditorSceneManager.OpenScene(scenePath);
        foreach (var root in scene.GetRootGameObjects()) ValidateScripts(root);
        var avatar = PrefabUtility.LoadPrefabContents(ROOT + "/FullDemo/Prefabs/Player/PlayerAvatar.prefab");
        try { ValidateScripts(avatar); }
        finally { PrefabUtility.UnloadPrefabContents(avatar); }
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scenePath },
            locationPathName = RequiredEnvironment("LIVEOPS_VERIFY_BUILD"),
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.Development | BuildOptions.StrictMode
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Imported unitypackage demo build failed: " + report.summary.result);
        Debug.Log("UNITYPACKAGE_VERIFY_PASS: " + report.summary.totalErrors + " errors");
    }

    private static void ValidateScripts(GameObject root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0)
                throw new InvalidOperationException("Missing imported script on " + transform.name);
    }

    private static string RequiredEnvironment(string name)
    {
        string value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Missing export configuration: " + name);
        return value;
    }
}
