using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Shop;
using MultiplayerLiveOpsToolkit.Inventory;

/// <summary>
/// Central singleton that owns the runtime shop catalog.
/// Maps confirmed Shop/Inventory snapshots to the local master catalog,
/// and provides all lookups for shop browsing, ownership, and pricing.
///
/// Backend requests and module lifetimes belong to the explicit sample context.
/// </summary>
public class AccessoryRegistry : MonoBehaviour
{
    public static AccessoryRegistry Instance { get; private set; }

    [SerializeField] private AccessorySO masterCatalogSO;

    private ShopCatalog activeShopCatalog = new ShopCatalog();
    private bool shopCatalogLoaded = false;
    private IShopService shop;
    private IInventoryService inventory;
    private bool hasBindings;

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

    public void Bind(IShopService shopService, IInventoryService ownership)
    {
        if (shopService == null) throw new ArgumentNullException(nameof(shopService));
        if (ownership == null) throw new ArgumentNullException(nameof(ownership));
        Unbind();
        shop = shopService; inventory = ownership;
        shop.StateChanged += RebuildCatalog;
        inventory.SnapshotChanged += RebuildCatalog;
        hasBindings = true;
        RebuildCatalog();
    }
    private void RebuildCatalog()
    {
        activeShopCatalog = new ShopCatalog();
        shopCatalogLoaded = false;
        if (!shop.HasSnapshot || !inventory.HasSnapshot) { Notify(AccessoryOwnershipChanged); return; }
        var accessories = new AccessoriesResponse { accessories = new Dictionary<string, List<Accessory>>() };
        var sets = new AccessorySetResponse { sets = new Dictionary<string, List<AccessorySet>>() };
        foreach (var offer in shop.Snapshot.Offers.Values)
        {
            var costs = new List<AccessoryCost>();
            foreach (var cost in offer.Costs) costs.Add(new AccessoryCost { currency = cost.Key, amount = checked((int)cost.Value) });
            bool isOwned = true;
            var memberIds = new List<int>();
            foreach (var memberId in offer.ItemIds)
            {
                int id = ParseSampleId(memberId);
                if (masterCatalogSO.GetAccessoryTypeById(id) == AccessoryType.None || masterCatalogSO.GetAccessoryPrefabById(id) == null)
                    throw new InvalidOperationException("Missing sample cosmetic mapping for item " + memberId);
                memberIds.Add(id);
                isOwned &= inventory.Snapshot.Items.ContainsKey(memberId);
            }
            if (offer.IsSet)
            {
                if (!sets.sets.TryGetValue(offer.CategoryId, out var group)) { group = new List<AccessorySet>(); sets.sets.Add(offer.CategoryId, group); }
                group.Add(new AccessorySet { id = ParseSampleId(offer.OfferId.Substring(4)), name = offer.DisplayName, costs = costs, isOwned = isOwned, accessoryIDs = memberIds });
            }
            else
            {
                if (!accessories.accessories.TryGetValue(offer.CategoryId, out var group)) { group = new List<Accessory>(); accessories.accessories.Add(offer.CategoryId, group); }
                group.Add(new Accessory { id = memberIds[0], name = offer.DisplayName, costs = costs, isOwned = isOwned });
            }
        }
        var lookup = masterCatalogSO.CreateLookup();
        activeShopCatalog.BuildFromBackendResponse(accessories, lookup);
        activeShopCatalog.BuildSetsFromBackendResponse(sets, lookup);
        shopCatalogLoaded = true;
        Notify(ShopCatalogLoaded);
        Notify(AccessoryOwnershipChanged);
    }
    private static int ParseSampleId(string value)
    {
        if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id) || id < 1)
            throw new InvalidOperationException("Sample cosmetic bindings require a positive seeded numeric ID: " + value);
        return id;
    }
    private static void Notify(Action subscribers)
    {
        if (subscribers == null) return;
        foreach (Action subscriber in subscribers.GetInvocationList())
        { try { subscriber(); } catch (Exception exception) { Debug.LogException(exception); } }
    }
    private void Unbind()
    {
        if (!hasBindings) return;
        shop.StateChanged -= RebuildCatalog;
        inventory.SnapshotChanged -= RebuildCatalog;
        hasBindings = false;
    }
    private void OnDestroy() { Unbind(); if (Instance == this) Instance = null; }

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

}
