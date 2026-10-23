using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;

/// <summary>
/// Central singleton that owns the runtime shop catalog.
/// Loads shop data from the backend, merges it with the local master catalog,
/// and provides all lookups for shop browsing, ownership, and pricing.
///
/// Waits for Netcode's NetworkManager before initializing (client-only).
/// </summary>
public class AccessoryRegistry : MonoBehaviour
{
    public static AccessoryRegistry Instance { get; private set; }

    [SerializeField] private AccessorySO masterCatalogSO;

    private ShopCatalog activeShopCatalog = new ShopCatalog();
    private bool shopCatalogLoaded = false;

    public event Action ShopCatalogLoaded;
    public event Action AccessoryOwnershipChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (masterCatalogSO == null)
            throw new System.InvalidOperationException(
                "AccessoryRegistry: masterCatalogSO not assigned in inspector");

        transform.parent = null;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        StartCoroutine(Init());
    }

    IEnumerator Init()
    {
        while (NetworkManager.Singleton == null)
            yield return null;

        while (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
            yield return null;

        if (!NetworkManager.Singleton.IsClient)
            yield break;

        StartCoroutine(LoadShopCatalog());
    }

    /// <summary>Fetch accessories + sets from backend and merge with master catalog.</summary>
    public IEnumerator LoadShopCatalog(Action onComplete = null, Action<string> onError = null)
    {
        bool accessoriesDone = false;
        bool setsDone = false;
        string accessoryError = null;
        var lookup = masterCatalogSO.CreateLookup();

        yield return ShopAPI.Instance.GetAccessories(
            (response) =>
            {
                activeShopCatalog.BuildFromBackendResponse(response, lookup);
                accessoriesDone = true;
            },
            (error) =>
            {
                accessoryError = $"Failed to load accessories: {error}";
                Debug.LogError(accessoryError);
                accessoriesDone = true;
            }
        );

        yield return ShopAPI.Instance.GetAccessorySets(
            (setsResponse) =>
            {
                activeShopCatalog.BuildSetsFromBackendResponse(setsResponse, lookup);
                setsDone = true;
            },
            (error) =>
            {
                Debug.LogWarning($"Sets not available: {error}");
                setsDone = true;
            }
        );

        while (!accessoriesDone || !setsDone) yield return null;

        if (accessoryError != null)
        {
            onError?.Invoke(accessoryError);
        }
        else
        {
            shopCatalogLoaded = true;
            try { ShopCatalogLoaded?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            onComplete?.Invoke();
        }
    }

    public IEnumerator ReloadShopCatalog()
    {
        shopCatalogLoaded = false;
        activeShopCatalog = new ShopCatalog();
        yield return LoadShopCatalog();
    }

    public bool IsShopCatalogLoaded() => shopCatalogLoaded;

    // --- Delegated lookups (tries master catalog first, falls back to shop catalog) ---

    public GameObject GetAccessoryPrefabById(int accessoryId, bool fromMasterOnly = false)
    {
        GameObject prefab = masterCatalogSO.GetAccessoryPrefabById(accessoryId);
        if (prefab == null && !fromMasterOnly && shopCatalogLoaded)
            prefab = activeShopCatalog.GetPrefabById(accessoryId);
        return prefab;
    }

    public int GetAccessoryIdByPrefab(GameObject prefab) =>
        masterCatalogSO.GetAccessoryIdByPrefab(prefab);

    public AccessoryType GetAccessoryTypeById(int accessoryId) =>
        masterCatalogSO.GetAccessoryTypeById(accessoryId);

    public GameObject[] GetAccessoriesForType(AccessoryType type)
    {
        if (!shopCatalogLoaded) return new GameObject[0];
        return activeShopCatalog.GetPrefabsForType(type);
    }

    public ShopAccessoryItem GetShopItemById(int accessoryId) =>
        shopCatalogLoaded ? activeShopCatalog.GetItemById(accessoryId) : null;

    public List<ShopAccessoryItem> GetShopItemsForType(AccessoryType type) =>
        shopCatalogLoaded ? activeShopCatalog.GetItemsForType(type) : new();

    public AccessorySetData[] GetAllSets() =>
        shopCatalogLoaded ? activeShopCatalog.GetAllAvailableSets() : new AccessorySetData[0];

    public AccessorySetData GetSetById(int setId) =>
        shopCatalogLoaded ? activeShopCatalog.GetSetById(setId) : null;

    public List<int> GetAccessoryIdsInSet(int setId)
    {
        var set = GetSetById(setId);
        return set?.accessoryIDs ?? new List<int>();
    }

    public bool IsOwned(GameObject accessory) =>
        shopCatalogLoaded && activeShopCatalog.IsOwned(GetAccessoryIdByPrefab(accessory));

    public bool IsSetOwned(int setId) =>
        shopCatalogLoaded && activeShopCatalog.IsSetOwned(setId);

    public bool TryGetGoldCost(GameObject accessory, out int goldAmount)
    {
        goldAmount = 0;
        if (!shopCatalogLoaded) return false;
        return activeShopCatalog.TryGetGoldCost(GetAccessoryIdByPrefab(accessory), out goldAmount);
    }

    public bool TryGetSetGoldCost(int setId, out int goldAmount)
    {
        goldAmount = 0;
        if (!shopCatalogLoaded) return false;
        return activeShopCatalog.TryGetSetGoldCost(setId, out goldAmount);
    }

    public void MarkAccessoryOwned(int accessoryId)
    {
        if (shopCatalogLoaded)
        {
            activeShopCatalog.MarkOwned(accessoryId);
            try { AccessoryOwnershipChanged?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    public void MarkSetOwned(int setId)
    {
        if (!shopCatalogLoaded) return;

        activeShopCatalog.MarkSetOwned(setId);

        var accessoryIds = GetAccessoryIdsInSet(setId);
        foreach (int accessoryId in accessoryIds)
        {
            activeShopCatalog.MarkOwned(accessoryId);
        }

        try { AccessoryOwnershipChanged?.Invoke(); }
        catch (Exception e) { Debug.LogException(e); }
    }
}
