#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Components;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;

public class SetupExampleScene
{
    const string CHARACTER_FBX = "Assets/Models/Character/Modular Characters Free.fbx";

    struct AccessoryDef
    {
        public int id;
        public string meshName; // child name inside the FBX
        public AccessoryType type;
    }

    // Default base meshes — active on the player by default, hidden when accessories replace them
    static readonly string[] DefaultBaseMeshes = { "head-00", "torso-00", "arms-00", "legs-00", "feet-00", "hands-00" };

    // IDs must match backend seed data (shop/management/commands/seed.py)
    static readonly AccessoryDef[] Accessories = new[]
    {
        // Headwear — hats (attached to Head bone)
        new AccessoryDef { id = 1,  meshName = "hat-01",      type = AccessoryType.Headwear },
        new AccessoryDef { id = 6,  meshName = "hat-02",      type = AccessoryType.Headwear },
        new AccessoryDef { id = 12, meshName = "hat-51",      type = AccessoryType.Headwear },
        new AccessoryDef { id = 17, meshName = "hat-101",     type = AccessoryType.Headwear },
        // Hair (attached to Head bone)
        new AccessoryDef { id = 18, meshName = "hair-51",     type = AccessoryType.Hair },
        new AccessoryDef { id = 19, meshName = "hair-102",    type = AccessoryType.Hair },
        // Facewear — glasses & masks (attached to Head bone)
        new AccessoryDef { id = 7,  meshName = "glasses-102", type = AccessoryType.Facewear },
        new AccessoryDef { id = 3,  meshName = "mask-102",    type = AccessoryType.Facewear },
        // MainHand — weapons (attached to RightHand bone)
        new AccessoryDef { id = 20, meshName = "sword-01",    type = AccessoryType.MainHand },
        new AccessoryDef { id = 21, meshName = "axe-01",      type = AccessoryType.MainHand },
        new AccessoryDef { id = 22, meshName = "staff-02",    type = AccessoryType.MainHand },
        new AccessoryDef { id = 23, meshName = "gun-101",     type = AccessoryType.MainHand },
        // OffHand — shields & bows (attached to LeftHand bone)
        new AccessoryDef { id = 24, meshName = "shield-01",   type = AccessoryType.OffHand },
        new AccessoryDef { id = 25, meshName = "bow-01",      type = AccessoryType.OffHand },
        // Back — cloaks & packs (attached to Spine2 bone)
        new AccessoryDef { id = 26, meshName = "backpack-01", type = AccessoryType.Back },
        new AccessoryDef { id = 27, meshName = "cloak-01",    type = AccessoryType.Back },
        // Body-part sets — skinned meshes that replace the default base meshes
        // Ninja Outfit (Set ID=4)
        new AccessoryDef { id = 28, meshName = "head-01",     type = AccessoryType.Head },
        new AccessoryDef { id = 29, meshName = "torso-01",    type = AccessoryType.Torso },
        new AccessoryDef { id = 30, meshName = "arms-01",     type = AccessoryType.Arms },
        new AccessoryDef { id = 31, meshName = "legs-01",     type = AccessoryType.Legs },
        new AccessoryDef { id = 32, meshName = "feet-01",     type = AccessoryType.Feet },
        // Mage Outfit (Set ID=5)
        new AccessoryDef { id = 33, meshName = "head-02",     type = AccessoryType.Head },
        new AccessoryDef { id = 34, meshName = "torso-02",    type = AccessoryType.Torso },
        new AccessoryDef { id = 35, meshName = "arms-02",     type = AccessoryType.Arms },
        new AccessoryDef { id = 36, meshName = "legs-02",     type = AccessoryType.Legs },
        new AccessoryDef { id = 37, meshName = "feet-02",     type = AccessoryType.Feet },
        // Sheriff Outfit (Set ID=6)
        new AccessoryDef { id = 38, meshName = "head-51",     type = AccessoryType.Head },
        new AccessoryDef { id = 39, meshName = "torso-51",    type = AccessoryType.Torso },
        new AccessoryDef { id = 40, meshName = "arms-51",     type = AccessoryType.Arms },
        new AccessoryDef { id = 41, meshName = "legs-51",     type = AccessoryType.Legs },
        new AccessoryDef { id = 42, meshName = "feet-51",     type = AccessoryType.Feet },
        // Steampunk Outfit (Set ID=7)
        new AccessoryDef { id = 43, meshName = "head-52",     type = AccessoryType.Head },
        new AccessoryDef { id = 44, meshName = "torso-52",    type = AccessoryType.Torso },
        new AccessoryDef { id = 45, meshName = "arms-52",     type = AccessoryType.Arms },
        new AccessoryDef { id = 46, meshName = "legs-52",     type = AccessoryType.Legs },
        new AccessoryDef { id = 47, meshName = "feet-52",     type = AccessoryType.Feet },
        // Commando Outfit (Set ID=8)
        new AccessoryDef { id = 48, meshName = "head-101",    type = AccessoryType.Head },
        new AccessoryDef { id = 49, meshName = "torso-101",   type = AccessoryType.Torso },
        new AccessoryDef { id = 50, meshName = "arms-101",    type = AccessoryType.Arms },
        new AccessoryDef { id = 51, meshName = "legs-101",    type = AccessoryType.Legs },
        new AccessoryDef { id = 52, meshName = "feet-101",    type = AccessoryType.Feet },
        // Cyber Outfit (Set ID=9)
        new AccessoryDef { id = 53, meshName = "head-102",    type = AccessoryType.Head },
        new AccessoryDef { id = 54, meshName = "torso-102",   type = AccessoryType.Torso },
        new AccessoryDef { id = 55, meshName = "arms-102",    type = AccessoryType.Arms },
        new AccessoryDef { id = 56, meshName = "legs-102",    type = AccessoryType.Legs },
        new AccessoryDef { id = 57, meshName = "feet-102",    type = AccessoryType.Feet },
    };

    [MenuItem("Tools/Setup Example Scene")]
    public static void Setup()
    {
        EnsureFolders();
        var accessoryPrefabs = CreateAccessoryPrefabs();
        var catalogAsset = CreateAccessoryCatalog(accessoryPrefabs);
        var playerPrefab = CreatePlayerPrefab();
        CreateExampleScene(catalogAsset, playerPrefab);
        Debug.Log("Example scene setup complete! Open Assets/Scenes/ExampleScene.unity");
    }

    static void EnsureFolders()
    {
        string[] folders = {
            "Assets/Prefabs", "Assets/Prefabs/Accessories", "Assets/Prefabs/Player",
            "Assets/ScriptableObjects", "Assets/Scenes"
        };
        foreach (var f in folders)
        {
            string parent = System.IO.Path.GetDirectoryName(f).Replace("\\", "/");
            string child = System.IO.Path.GetFileName(f);
            if (!AssetDatabase.IsValidFolder(f))
                AssetDatabase.CreateFolder(parent, child);
        }
    }

    static GameObject[] CreateAccessoryPrefabs()
    {
        var sourceFbx = AssetDatabase.LoadAssetAtPath<GameObject>(CHARACTER_FBX);
        if (sourceFbx == null)
        {
            Debug.LogError($"Character FBX not found: {CHARACTER_FBX}");
            return new GameObject[0];
        }

        var prefabs = new GameObject[Accessories.Length];
        for (int i = 0; i < Accessories.Length; i++)
        {
            var def = Accessories[i];
            string prefabPath = $"Assets/Prefabs/Accessories/{def.meshName}.prefab";

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null)
            {
                prefabs[i] = existing;
                continue;
            }

            prefabs[i] = ExtractMeshAsPrefab(sourceFbx, def.meshName, prefabPath);
        }
        AssetDatabase.SaveAssets();
        return prefabs;
    }

    static Transform FindChildRecursive(Transform parent, string childName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == childName) return child;
            var found = FindChildRecursive(child, childName);
            if (found != null) return found;
        }
        return null;
    }

    static GameObject ExtractMeshAsPrefab(GameObject sourceFbx, string meshName, string savePath)
    {
        var instance = Object.Instantiate(sourceFbx);
        var targetMesh = FindChildRecursive(instance.transform, meshName);

        if (targetMesh == null)
        {
            Debug.LogError($"Mesh '{meshName}' not found in FBX");
            Object.DestroyImmediate(instance);
            return null;
        }

        bool isSkinned = targetMesh.GetComponent<SkinnedMeshRenderer>() != null;

        if (isSkinned)
        {
            // Skinned mesh: keep the mesh + rig, remove all other meshes
            var toRemove = new List<Transform>();
            foreach (Transform child in instance.transform)
            {
                if (child.name == "Rig" || child.name == "Armature") continue;
                if (child == targetMesh) continue;
                toRemove.Add(child);
            }
            foreach (var t in toRemove)
                Object.DestroyImmediate(t.gameObject);
        }
        else
        {
            // Static mesh (hat, glasses, mask): extract as prefab, preserving bone-relative offset
            var wrapper = new GameObject(meshName);
            // worldPositionStays=false preserves the bone-relative local offset
            targetMesh.SetParent(wrapper.transform, false);
            Object.DestroyImmediate(instance);
            instance = wrapper;
        }

        instance.name = meshName;
        var prefab = PrefabUtility.SaveAsPrefabAsset(instance, savePath);
        Object.DestroyImmediate(instance);
        return prefab;
    }

    static AccessorySO CreateAccessoryCatalog(GameObject[] prefabs)
    {
        string path = "Assets/ScriptableObjects/AccessoryCatalog.asset";
        var existing = AssetDatabase.LoadAssetAtPath<AccessorySO>(path);
        if (existing != null)
        {
            existing.accessories = new AccessoryItemData[Accessories.Length];
            for (int i = 0; i < Accessories.Length; i++)
            {
                existing.accessories[i] = new AccessoryItemData
                {
                    itemId = Accessories[i].id,
                    prefab = prefabs[i],
                    accessoryType = Accessories[i].type,
                };
            }
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();
            return existing;
        }

        var catalog = ScriptableObject.CreateInstance<AccessorySO>();
        catalog.accessories = new AccessoryItemData[Accessories.Length];
        for (int i = 0; i < Accessories.Length; i++)
        {
            catalog.accessories[i] = new AccessoryItemData
            {
                itemId = Accessories[i].id,
                prefab = prefabs[i],
                accessoryType = Accessories[i].type,
            };
        }
        AssetDatabase.CreateAsset(catalog, path);
        AssetDatabase.SaveAssets();
        return catalog;
    }

    static GameObject CreatePlayerPrefab()
    {
        string path = "Assets/Prefabs/Player/PlayerAvatar.prefab";

        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            AssetDatabase.DeleteAsset(path);

        var sourceFbx = AssetDatabase.LoadAssetAtPath<GameObject>(CHARACTER_FBX);
        if (sourceFbx == null)
        {
            Debug.LogError($"Character FBX not found: {CHARACTER_FBX}");
            return null;
        }

        var root = (GameObject)PrefabUtility.InstantiatePrefab(sourceFbx);
        root.name = "PlayerAvatar";

        // Keep only default base meshes + rig, remove everything else
        var defaultSet = new HashSet<string>(DefaultBaseMeshes);

        // Collect non-default direct children to destroy
        var toDestroy = new List<GameObject>();
        foreach (Transform child in root.transform)
        {
            if (child.name == "Rig" || child.name == "Armature")
                continue;
            if (!defaultSet.Contains(child.name))
                toDestroy.Add(child.gameObject);
        }
        foreach (var go in toDestroy)
            Object.DestroyImmediate(go);

        // Remove all MeshRenderer accessories nested inside the Rig (hats, weapons, etc.)
        var rigRoot = root.transform.Find("Rig") ?? root.transform.Find("Armature");
        if (rigRoot != null)
        {
            var nestedAccessories = rigRoot.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var meshRenderer in nestedAccessories)
                Object.DestroyImmediate(meshRenderer.gameObject);
        }

        // Scale up — model is ~0.5 units tall, scale to ~1.7 units (human height)
        root.transform.localScale = Vector3.one * 3.4f;

        root.AddComponent<NetworkObject>();
        root.AddComponent<AccessoryPool>();
        root.AddComponent<PlayerAccessory>();
        root.AddComponent<PlayerAvatarSetup>();
        root.AddComponent<ClientNetworkTransform>();
        root.AddComponent<SimplePlayerMovement>();

        var animator = root.GetComponent<Animator>();
        if (animator == null) animator = root.AddComponent<Animator>();
        var animController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
            "Assets/Models/Character/PlayerAnimator.controller");
        if (animController != null)
            animator.runtimeAnimatorController = animController;

        var networkAnimator = root.AddComponent<ClientNetworkAnimator>();
        networkAnimator.Animator = animator;

        // Nameplate: world-space TMP child above the avatar's head.
        // Scale is neutralized (1/3.4) so fontSize is in true world-space units.
        // localPosition y=0.40 × scale 3.4 ≈ 1.36 m world height (~38 cm above HeadTop_End bone, clears helms).
        var nameplateGo = new GameObject("Nameplate");
        nameplateGo.transform.SetParent(root.transform, false);
        nameplateGo.transform.localPosition = new Vector3(0f, 0.40f, 0f);
        float inversePlayerScale = 1f / 3.4f;
        nameplateGo.transform.localScale = new Vector3(inversePlayerScale, inversePlayerScale, inversePlayerScale);
        var tmpLabel = nameplateGo.AddComponent<TextMeshPro>();
        tmpLabel.text = "";
        tmpLabel.fontSize = 0.35f;
        tmpLabel.fontStyle = FontStyles.Bold;
        tmpLabel.alignment = TextAlignmentOptions.Center;
        tmpLabel.enableWordWrapping = false;
        tmpLabel.overflowMode = TextOverflowModes.Overflow;
        tmpLabel.color = Color.white;
        // World-space TMP changes depth testing on its material when an avatar spawns.
        // Keep that material separate from the default font material used by overlay UI.
        const string nameplateMaterialPath = "Assets/Prefabs/Player/Nameplate.mat";
        var nameplateMaterial = AssetDatabase.LoadAssetAtPath<Material>(nameplateMaterialPath);
        if (nameplateMaterial == null)
        {
            nameplateMaterial = new Material(tmpLabel.font.material);
            AssetDatabase.CreateAsset(nameplateMaterial, nameplateMaterialPath);
        }
        nameplateMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.15f);
        nameplateMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color32(0, 0, 0, 200));
        EditorUtility.SetDirty(nameplateMaterial);
        tmpLabel.fontSharedMaterial = nameplateMaterial;

        var nameplateComponent = root.AddComponent<PlayerNameplate>();
        var nameplateSo = new SerializedObject(nameplateComponent);
        nameplateSo.FindProperty("nameplateLabel").objectReferenceValue = tmpLabel;
        nameplateSo.ApplyModifiedPropertiesWithoutUndo();

        root.tag = "Player";

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();

        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }


    static void CreateExampleScene(AccessorySO catalog, GameObject playerPrefab)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // NetworkManager
        var nmGo = new GameObject("NetworkManager");
        var nm = nmGo.AddComponent<NetworkManager>();
        nmGo.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        nmGo.AddComponent<AutoConnect>();
        nm.NetworkConfig = new NetworkConfig();
        nm.NetworkConfig.PlayerPrefab = playerPrefab;

        // Singletons
        var clientAvatars = new GameObject("ClientPlayerAvatars");
        clientAvatars.AddComponent<ClientPlayerAvatars>();

        var registryGo = new GameObject("AccessoryRegistry");
        var registry = registryGo.AddComponent<AccessoryRegistry>();
        var regSo = new SerializedObject(registry);
        regSo.FindProperty("masterCatalogSO").objectReferenceValue = catalog;
        regSo.ApplyModifiedPropertiesWithoutUndo();

        var devAuth = new GameObject("DevAuthBootstrap");
        var devAuthComponent = devAuth.AddComponent<DevAuthBootstrap>();

        var equipServiceGo = new GameObject("AccessoryEquipService");
        equipServiceGo.AddComponent<AccessoryEquipService>();

        var achievementServiceGo = new GameObject("AchievementService");
        var achievementService = achievementServiceGo.AddComponent<AchievementService>();
        var achievementCatalog = AssetDatabase.LoadAssetAtPath<AchievementCatalogSO>(
            "Assets/ScriptableObjects/AchievementCatalog.asset");
        if (achievementCatalog != null)
        {
            var asSo = new SerializedObject(achievementService);
            asSo.FindProperty("masterCatalog").objectReferenceValue = achievementCatalog;
            asSo.ApplyModifiedPropertiesWithoutUndo();
        }

        // Ground
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(5, 1, 5);
        var groundMat = new Material(Shader.Find("Standard"));
        groundMat.color = new Color(0.4f, 0.6f, 0.4f);
        ground.GetComponent<Renderer>().sharedMaterial = groundMat;

        // Third-person camera on Main Camera
        var mainCam = Camera.main;
        if (mainCam != null)
            mainCam.gameObject.AddComponent<ThirdPersonCamera>();

        // UI Canvas
        CreateUICanvas(devAuthComponent);

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/ExampleScene.unity");
        EditorSceneManager.MarkSceneDirty(scene);
    }

    static GameObject CreateUICanvas(DevAuthBootstrap devAuthComponent)
    {
        var canvasGo = new GameObject("UI Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.additionalShaderChannels =
            AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.Normal |
            AdditionalCanvasShaderChannels.Tangent;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // Wallet bar — full width, thin strip at the very top
        var walletGo = new GameObject("WalletDisplay");
        walletGo.transform.SetParent(canvasGo.transform, false);
        var walletRect = walletGo.AddComponent<RectTransform>();
        walletRect.anchorMin = new Vector2(0, 1);
        walletRect.anchorMax = new Vector2(1, 1);
        walletRect.pivot = new Vector2(0.5f, 1);
        walletRect.anchoredPosition = Vector2.zero;
        walletRect.sizeDelta = new Vector2(0, 36);
        var walletBg = walletGo.AddComponent<Image>();
        walletBg.color = new Color(0.08f, 0.08f, 0.12f, 0.95f);
        var walletLayout = walletGo.AddComponent<HorizontalLayoutGroup>();
        walletLayout.padding = new RectOffset(12, 6, 0, 0);
        walletLayout.spacing = 6;
        walletLayout.childAlignment = TextAnchor.MiddleCenter;
        walletLayout.childControlWidth = true;
        walletLayout.childControlHeight = true;
        walletLayout.childForceExpandWidth = true;
        walletLayout.childForceExpandHeight = true;

        var walletTextGo = new GameObject("WalletText");
        walletTextGo.transform.SetParent(walletGo.transform, false);
        var walletTmp = walletTextGo.AddComponent<TextMeshProUGUI>();
        walletTmp.text = "";
        walletTmp.fontSize = 14;
        walletTmp.alignment = TextAlignmentOptions.MidlineLeft;
        walletTmp.color = new Color(0.85f, 0.85f, 0.9f);

        // Reset button — rightmost in wallet bar
        var resetBtnGo = new GameObject("ResetButton");
        resetBtnGo.transform.SetParent(walletGo.transform, false);
        var resetBtnLayout = resetBtnGo.AddComponent<LayoutElement>();
        resetBtnLayout.minWidth = 44;
        resetBtnLayout.preferredWidth = 44;
        resetBtnLayout.flexibleWidth = 0;
        var resetBtnImg = resetBtnGo.AddComponent<Image>();
        resetBtnImg.color = new Color(0.45f, 0.18f, 0.18f);
        var resetBtn = resetBtnGo.AddComponent<Button>();
        var resetBtnColors = resetBtn.colors;
        resetBtnColors.highlightedColor = new Color(0.55f, 0.22f, 0.22f);
        resetBtnColors.pressedColor = new Color(0.35f, 0.12f, 0.12f);
        resetBtn.colors = resetBtnColors;

        var resetTextGo = new GameObject("Text");
        resetTextGo.transform.SetParent(resetBtnGo.transform, false);
        var resetTextRect = resetTextGo.AddComponent<RectTransform>();
        resetTextRect.anchorMin = Vector2.zero;
        resetTextRect.anchorMax = Vector2.one;
        resetTextRect.sizeDelta = Vector2.zero;
        var resetTmp = resetTextGo.AddComponent<TextMeshProUGUI>();
        resetTmp.text = "Reset";
        resetTmp.fontSize = 11;
        resetTmp.alignment = TextAlignmentOptions.Center;
        resetTmp.color = new Color(0.85f, 0.85f, 0.9f);

        var walletDisplay = walletGo.AddComponent<WalletDisplay>();
        var wdSo = new SerializedObject(walletDisplay);
        wdSo.FindProperty("walletText").objectReferenceValue = walletTmp;
        wdSo.FindProperty("resetButton").objectReferenceValue = resetBtn;
        wdSo.ApplyModifiedPropertiesWithoutUndo();

        // Side panel — right 30% of screen, below wallet bar
        var sidePanel = new GameObject("SidePanel");
        sidePanel.transform.SetParent(canvasGo.transform, false);
        var sidePanelRect = sidePanel.AddComponent<RectTransform>();
        sidePanelRect.anchorMin = new Vector2(0.7f, 0);
        sidePanelRect.anchorMax = new Vector2(1, 1);
        sidePanelRect.pivot = new Vector2(0.5f, 0.5f);
        sidePanelRect.offsetMin = new Vector2(0, 0);
        sidePanelRect.offsetMax = new Vector2(0, -36);
        var sidePanelBg = sidePanel.AddComponent<Image>();
        sidePanelBg.color = new Color(0.08f, 0.08f, 0.12f, 0.85f);

        // Tab bar — flush at top of side panel, no extra padding
        var tabBar = new GameObject("TabBar");
        tabBar.transform.SetParent(sidePanel.transform, false);
        var tabRect = tabBar.AddComponent<RectTransform>();
        tabRect.anchorMin = new Vector2(0, 1);
        tabRect.anchorMax = new Vector2(1, 1);
        tabRect.pivot = new Vector2(0.5f, 1);
        tabRect.anchoredPosition = Vector2.zero;
        tabRect.sizeDelta = new Vector2(0, 36);
        var tabLayout = tabBar.AddComponent<HorizontalLayoutGroup>();
        tabLayout.spacing = 1;
        tabLayout.padding = new RectOffset(0, 0, 0, 0);
        tabLayout.childAlignment = TextAnchor.MiddleCenter;
        tabLayout.childControlWidth = true;
        tabLayout.childControlHeight = true;
        tabLayout.childForceExpandWidth = true;
        tabLayout.childForceExpandHeight = true;

        // Create tab buttons
        string[] tabNames = { "Shop", "Sets", "Inventory", "Daily", "Achievements" };
        var tabButtons = new List<Button>();
        foreach (var name in tabNames)
        {
            var tabBtnGo = new GameObject($"Tab_{name}");
            tabBtnGo.transform.SetParent(tabBar.transform, false);
            var tabBtnImg = tabBtnGo.AddComponent<Image>();
            tabBtnImg.color = new Color(0.2f, 0.2f, 0.25f);
            var tabBtn = tabBtnGo.AddComponent<Button>();
            tabButtons.Add(tabBtn);

            var tabTextGo = new GameObject("Text");
            tabTextGo.transform.SetParent(tabBtnGo.transform, false);
            var tabTextRect = tabTextGo.AddComponent<RectTransform>();
            tabTextRect.anchorMin = Vector2.zero;
            tabTextRect.anchorMax = Vector2.one;
            tabTextRect.sizeDelta = Vector2.zero;
            var tabTmp = tabTextGo.AddComponent<TextMeshProUGUI>();
            tabTmp.text = name;
            tabTmp.fontSize = 13;
            tabTmp.alignment = TextAlignmentOptions.Center;
            tabTmp.color = Color.white;
        }

        // Content area inside side panel — below tab bar, above status text
        var contentArea = new GameObject("ContentArea");
        contentArea.transform.SetParent(sidePanel.transform, false);
        var contentAreaRect = contentArea.AddComponent<RectTransform>();
        contentAreaRect.anchorMin = new Vector2(0, 0);
        contentAreaRect.anchorMax = new Vector2(1, 1);
        contentAreaRect.offsetMin = new Vector2(0, 26);
        contentAreaRect.offsetMax = new Vector2(0, -38);

        // Status text at bottom of side panel
        var statusGo = new GameObject("StatusText");
        statusGo.transform.SetParent(sidePanel.transform, false);
        var statusRect = statusGo.AddComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0, 0);
        statusRect.anchorMax = new Vector2(1, 0);
        statusRect.pivot = new Vector2(0.5f, 0);
        statusRect.anchoredPosition = new Vector2(0, 4);
        statusRect.sizeDelta = new Vector2(0, 24);
        var statusTmp = statusGo.AddComponent<TextMeshProUGUI>();
        statusTmp.text = "";
        statusTmp.fontSize = 12;
        statusTmp.alignment = TextAlignmentOptions.Center;
        statusTmp.color = new Color(1f, 0.4f, 0.4f);

        // Create tab panels (one per tab)
        var tabPanels = new List<GameObject>();

        // --- Shop tab panel (with ScrollRect) ---
        var shopPanel = CreateShopTabPanel(contentArea.transform, statusTmp);
        tabPanels.Add(shopPanel);

        // --- Sets tab panel ---
        var setsPanel = CreateScrollableTabPanel<SetsPanel>(contentArea.transform, "SetsPanel", statusTmp);
        tabPanels.Add(setsPanel);

        // --- Inventory tab panel ---
        var inventoryPanel = CreateScrollableTabPanel<InventoryPanel>(contentArea.transform, "InventoryPanel", statusTmp);
        tabPanels.Add(inventoryPanel);

        // --- Daily login calendar tab panel ---
        var dailyPanel = CreateDailyLoginTabPanel(contentArea.transform, statusTmp);
        tabPanels.Add(dailyPanel);

        // --- Achievements tab panel ---
        var achievePanel = CreateScrollableTabPanel<AchievementsPanel>(contentArea.transform, "AchievementsPanel", statusTmp);
        tabPanels.Add(achievePanel);

        // Wire TabManager
        var tabManager = sidePanel.AddComponent<TabManager>();
        var tmSo = new SerializedObject(tabManager);
        var tabBtnsProp = tmSo.FindProperty("tabButtons");
        tabBtnsProp.arraySize = tabButtons.Count;
        for (int i = 0; i < tabButtons.Count; i++)
            tabBtnsProp.GetArrayElementAtIndex(i).objectReferenceValue = tabButtons[i];
        var tabPanelsProp = tmSo.FindProperty("tabPanels");
        tabPanelsProp.arraySize = tabPanels.Count;
        for (int i = 0; i < tabPanels.Count; i++)
            tabPanelsProp.GetArrayElementAtIndex(i).objectReferenceValue = tabPanels[i];
        tmSo.ApplyModifiedPropertiesWithoutUndo();

        // Achievement toast — must be a Canvas child for GetComponentInParent<Canvas>() to work
        var toastGo = new GameObject("AchievementToast");
        toastGo.transform.SetParent(canvasGo.transform, false);
        toastGo.AddComponent<RectTransform>();
        toastGo.AddComponent<AchievementToast>();

        var loginPanel = CreateLoginPanel(canvasGo.transform, devAuthComponent);

        return canvasGo;
    }

    static GameObject CreateShopTabPanel(Transform parent, TMP_Text statusText)
    {
        var panelGo = new GameObject("ShopPanel");
        panelGo.transform.SetParent(parent, false);
        var panelRect = panelGo.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.sizeDelta = Vector2.zero;

        // ScrollRect
        var scrollRect = panelGo.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        // Viewport (masks content via RectMask2D — no Image alpha issues)
        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(panelGo.transform, false);
        var viewportRect = viewport.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.sizeDelta = Vector2.zero;
        viewport.AddComponent<RectMask2D>();

        // Content container (vertical layout)
        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0, 0);
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 2;
        vlg.padding = new RectOffset(2, 2, 2, 2);
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;

        // ShopPanel component
        var shopPanel = panelGo.AddComponent<ShopPanel>();
        var spSo = new SerializedObject(shopPanel);
        spSo.FindProperty("contentContainer").objectReferenceValue = content.transform;
        spSo.FindProperty("statusText").objectReferenceValue = statusText;
        spSo.ApplyModifiedPropertiesWithoutUndo();

        return panelGo;
    }

    static GameObject CreateScrollableTabPanel<T>(Transform parent, string panelName, TMP_Text statusText) where T : MonoBehaviour
    {
        var panelGo = new GameObject(panelName);
        panelGo.transform.SetParent(parent, false);
        var panelRect = panelGo.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.sizeDelta = Vector2.zero;

        var scrollRect = panelGo.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(panelGo.transform, false);
        var viewportRect = viewport.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.sizeDelta = Vector2.zero;
        viewport.AddComponent<RectMask2D>();

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0, 0);
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 2;
        vlg.padding = new RectOffset(2, 2, 2, 2);
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;

        var panelComponent = panelGo.AddComponent<T>();
        var so = new SerializedObject(panelComponent);
        so.FindProperty("contentContainer").objectReferenceValue = content.transform;
        so.FindProperty("statusText").objectReferenceValue = statusText;
        so.ApplyModifiedPropertiesWithoutUndo();

        panelGo.SetActive(false);
        return panelGo;
    }

    static GameObject CreateDailyLoginTabPanel(Transform parent, TMP_Text statusText)
    {
        var panelGo = new GameObject("DailyPanel");
        panelGo.transform.SetParent(parent, false);
        var panelRect = panelGo.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.sizeDelta = Vector2.zero;

        var panelLayout = panelGo.AddComponent<VerticalLayoutGroup>();
        panelLayout.spacing = 4;
        panelLayout.padding = new RectOffset(4, 4, 4, 4);
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        // Month header
        var headerGo = new GameObject("MonthHeader");
        headerGo.transform.SetParent(panelGo.transform, false);
        var headerLayout = headerGo.AddComponent<LayoutElement>();
        headerLayout.minHeight = 28;
        headerLayout.preferredHeight = 28;
        var headerBg = headerGo.AddComponent<Image>();
        headerBg.color = new Color(0.2f, 0.25f, 0.35f, 0.9f);
        var headerTextGo = new GameObject("Text");
        headerTextGo.transform.SetParent(headerGo.transform, false);
        var headerTextRect = headerTextGo.AddComponent<RectTransform>();
        headerTextRect.anchorMin = Vector2.zero;
        headerTextRect.anchorMax = Vector2.one;
        headerTextRect.sizeDelta = Vector2.zero;
        var monthHeaderTmp = headerTextGo.AddComponent<TextMeshProUGUI>();
        monthHeaderTmp.text = "Loading...";
        monthHeaderTmp.fontSize = 15;
        monthHeaderTmp.fontStyle = FontStyles.Bold;
        monthHeaderTmp.alignment = TextAlignmentOptions.Center;
        monthHeaderTmp.color = new Color(0.7f, 0.8f, 1f);

        // Day-of-week labels row
        var dayLabelsGo = new GameObject("DayLabels");
        dayLabelsGo.transform.SetParent(panelGo.transform, false);
        var dayLabelsLayout = dayLabelsGo.AddComponent<LayoutElement>();
        dayLabelsLayout.minHeight = 18;
        dayLabelsLayout.preferredHeight = 18;
        var dayLabelsGrid = dayLabelsGo.AddComponent<GridLayoutGroup>();
        dayLabelsGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        dayLabelsGrid.constraintCount = 7;
        dayLabelsGrid.cellSize = new Vector2(70, 18);
        dayLabelsGrid.spacing = new Vector2(2, 0);
        dayLabelsGrid.childAlignment = TextAnchor.MiddleCenter;

        string[] dayNames = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        foreach (var dayName in dayNames)
        {
            var labelGo = new GameObject($"Label_{dayName}");
            labelGo.transform.SetParent(dayLabelsGo.transform, false);
            var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.text = dayName;
            labelTmp.fontSize = 10;
            labelTmp.alignment = TextAlignmentOptions.Center;
            labelTmp.color = new Color(0.6f, 0.6f, 0.7f);
        }

        // Calendar grid
        var gridGo = new GameObject("CalendarGrid");
        gridGo.transform.SetParent(panelGo.transform, false);
        var gridLayoutElement = gridGo.AddComponent<LayoutElement>();
        gridLayoutElement.flexibleHeight = 1;
        var gridLayout = gridGo.AddComponent<GridLayoutGroup>();
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = 7;
        gridLayout.cellSize = new Vector2(70, 55);
        gridLayout.spacing = new Vector2(2, 2);
        gridLayout.childAlignment = TextAnchor.UpperCenter;

        // DailyLoginPanel component
        var dailyLoginPanel = panelGo.AddComponent<DailyLoginPanel>();
        var dlpSo = new SerializedObject(dailyLoginPanel);
        dlpSo.FindProperty("calendarGrid").objectReferenceValue = gridGo.transform;
        dlpSo.FindProperty("monthHeaderText").objectReferenceValue = monthHeaderTmp;
        dlpSo.FindProperty("statusText").objectReferenceValue = statusText;
        dlpSo.ApplyModifiedPropertiesWithoutUndo();

        panelGo.SetActive(false);
        return panelGo;
    }

    static GameObject CreateLoginPanel(Transform canvasTransform, DevAuthBootstrap devAuthComponent)
    {
        var panelGo = new GameObject("LoginPanel");
        panelGo.transform.SetParent(canvasTransform, false);
        var panelRect = panelGo.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.sizeDelta = Vector2.zero;
        var panelBg = panelGo.AddComponent<Image>();
        panelBg.color = new Color(0.06f, 0.06f, 0.1f, 1f);

        var formGo = new GameObject("LoginForm");
        formGo.transform.SetParent(panelGo.transform, false);
        var formRect = formGo.AddComponent<RectTransform>();
        formRect.anchorMin = new Vector2(0.5f, 0.5f);
        formRect.anchorMax = new Vector2(0.5f, 0.5f);
        formRect.pivot = new Vector2(0.5f, 0.5f);
        formRect.sizeDelta = new Vector2(380, 260);
        var formLayout = formGo.AddComponent<VerticalLayoutGroup>();
        formLayout.spacing = 14;
        formLayout.padding = new RectOffset(32, 32, 28, 24);
        formLayout.childAlignment = TextAnchor.MiddleCenter;
        formLayout.childControlWidth = true;
        formLayout.childControlHeight = true;
        formLayout.childForceExpandWidth = true;
        formLayout.childForceExpandHeight = false;

        var titleGo = new GameObject("Title");
        titleGo.transform.SetParent(formGo.transform, false);
        var titleLayout = titleGo.AddComponent<LayoutElement>();
        titleLayout.preferredHeight = 32;
        var titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
        titleTmp.text = "Multiplayer Live-Ops Toolkit";
        titleTmp.fontSize = 20;
        titleTmp.fontStyle = FontStyles.Bold;
        titleTmp.alignment = TextAlignmentOptions.Center;
        titleTmp.color = new Color(0.9f, 0.9f, 0.95f);

        var inputGo = new GameObject("UsernameInput");
        inputGo.transform.SetParent(formGo.transform, false);
        var inputLayout = inputGo.AddComponent<LayoutElement>();
        inputLayout.preferredHeight = 38;
        var inputBg = inputGo.AddComponent<Image>();
        inputBg.color = new Color(0.12f, 0.12f, 0.17f);

        var inputTextArea = new GameObject("Text Area");
        inputTextArea.transform.SetParent(inputGo.transform, false);
        var textAreaRect = inputTextArea.AddComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(12, 4);
        textAreaRect.offsetMax = new Vector2(-12, -4);

        var placeholderGo = new GameObject("Placeholder");
        placeholderGo.transform.SetParent(inputTextArea.transform, false);
        var placeholderRect = placeholderGo.AddComponent<RectTransform>();
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.sizeDelta = Vector2.zero;
        var placeholderTmp = placeholderGo.AddComponent<TextMeshProUGUI>();
        placeholderTmp.text = "Enter username...";
        placeholderTmp.fontSize = 15;
        placeholderTmp.fontStyle = FontStyles.Italic;
        placeholderTmp.color = new Color(0.4f, 0.4f, 0.5f);
        placeholderTmp.alignment = TextAlignmentOptions.MidlineLeft;

        var inputTextGo = new GameObject("Text");
        inputTextGo.transform.SetParent(inputTextArea.transform, false);
        var inputTextRect = inputTextGo.AddComponent<RectTransform>();
        inputTextRect.anchorMin = Vector2.zero;
        inputTextRect.anchorMax = Vector2.one;
        inputTextRect.sizeDelta = Vector2.zero;
        var inputTextTmp = inputTextGo.AddComponent<TextMeshProUGUI>();
        inputTextTmp.fontSize = 15;
        inputTextTmp.color = Color.white;
        inputTextTmp.alignment = TextAlignmentOptions.MidlineLeft;

        var inputField = inputGo.AddComponent<TMP_InputField>();
        inputField.textViewport = textAreaRect;
        inputField.textComponent = inputTextTmp;
        inputField.placeholder = placeholderTmp;
        inputField.fontAsset = inputTextTmp.font;
        inputField.pointSize = 15;
        inputField.characterLimit = 30;

        var hintGo = new GameObject("HintText");
        hintGo.transform.SetParent(formGo.transform, false);
        var hintLayout = hintGo.AddComponent<LayoutElement>();
        hintLayout.preferredHeight = 16;
        var hintTmp = hintGo.AddComponent<TextMeshProUGUI>();
        hintTmp.text = "New name creates an account, existing name logs you back in";
        hintTmp.fontSize = 11;
        hintTmp.alignment = TextAlignmentOptions.Center;
        hintTmp.color = new Color(0.42f, 0.44f, 0.52f);

        var playBtnGo = new GameObject("PlayButton");
        playBtnGo.transform.SetParent(formGo.transform, false);
        var playBtnLayout = playBtnGo.AddComponent<LayoutElement>();
        playBtnLayout.preferredHeight = 38;
        var playBtnImg = playBtnGo.AddComponent<Image>();
        playBtnImg.color = new Color(0.18f, 0.45f, 0.28f);
        var playBtn = playBtnGo.AddComponent<Button>();
        var playBtnColors = playBtn.colors;
        playBtnColors.highlightedColor = new Color(0.22f, 0.55f, 0.33f);
        playBtnColors.pressedColor = new Color(0.14f, 0.36f, 0.22f);
        playBtnColors.disabledColor = new Color(0.12f, 0.12f, 0.17f);
        playBtn.colors = playBtnColors;

        var playTextGo = new GameObject("Text");
        playTextGo.transform.SetParent(playBtnGo.transform, false);
        var playTextRect = playTextGo.AddComponent<RectTransform>();
        playTextRect.anchorMin = Vector2.zero;
        playTextRect.anchorMax = Vector2.one;
        playTextRect.sizeDelta = Vector2.zero;
        var playTmp = playTextGo.AddComponent<TextMeshProUGUI>();
        playTmp.text = "Play";
        playTmp.fontSize = 16;
        playTmp.fontStyle = FontStyles.Bold;
        playTmp.alignment = TextAlignmentOptions.Center;
        playTmp.color = Color.white;

        var statusGo = new GameObject("StatusLabel");
        statusGo.transform.SetParent(formGo.transform, false);
        var statusLayout = statusGo.AddComponent<LayoutElement>();
        statusLayout.preferredHeight = 18;
        var statusTmp = statusGo.AddComponent<TextMeshProUGUI>();
        statusTmp.text = "";
        statusTmp.fontSize = 12;
        statusTmp.alignment = TextAlignmentOptions.Center;
        statusTmp.color = new Color(0.55f, 0.65f, 0.85f);

        var daSo = new SerializedObject(devAuthComponent);
        daSo.FindProperty("loginPanel").objectReferenceValue = panelGo;
        daSo.FindProperty("usernameInput").objectReferenceValue = inputField;
        daSo.FindProperty("playButton").objectReferenceValue = playBtn;
        daSo.FindProperty("statusLabel").objectReferenceValue = statusTmp;
        daSo.ApplyModifiedPropertiesWithoutUndo();

        return panelGo;
    }

}
#endif
