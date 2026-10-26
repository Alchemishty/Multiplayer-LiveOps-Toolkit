using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using MultiplayerLiveOpsToolkit.Shop;
using MultiplayerLiveOpsToolkit.Wallet;
using MultiplayerLiveOpsToolkit.Inventory;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Object = UnityEngine.Object;

public class ShopPanelTests
{
    private GameObject root;
    private Transform content;
    private WalletModule wallet;
    private InventoryModule inventory;
    private ShopModule shop;
    private Backend backend;
    private GameObject controllerObject;
    [UnitySetUp]
    public IEnumerator SetUp()
    {
        backend = new Backend();
        wallet = new WalletModule(backend, exception => Assert.Fail(exception.Message));
        inventory = new InventoryModule(backend, exception => Assert.Fail(exception.Message));
        yield return wallet.Initialize(); yield return inventory.Initialize();
        shop = new ShopModule(backend, wallet, inventory, exception => Assert.Fail(exception.Message));
        yield return shop.Initialize();
        root = new GameObject("Shop without avatar", typeof(RectTransform)); root.SetActive(false);
        content = new GameObject("Content", typeof(RectTransform)).transform; content.SetParent(root.transform);
        var status = new GameObject("Status", typeof(RectTransform)).AddComponent<TextMeshProUGUI>(); status.transform.SetParent(root.transform);
        var panel = root.AddComponent<ShopPanel>();
#if UNITY_EDITOR
        var fields = new SerializedObject(panel); fields.FindProperty("contentContainer").objectReferenceValue = content; fields.FindProperty("statusText").objectReferenceValue = status; fields.ApplyModifiedPropertiesWithoutUndo();
#endif
        panel.Bind(shop, inventory);
        root.SetActive(true); yield return null;
    }
    [TearDown]
    public void TearDown() { shop.Shutdown(); inventory.Shutdown(); wallet.Shutdown(); Object.DestroyImmediate(root); if (controllerObject != null) Object.DestroyImmediate(controllerObject); }
    [UnityTest]
    public IEnumerator BoundShop_RendersPriceAndBuyWithoutAvatarOrRegistry()
    {
        Assert.That(content.Find("Offer_hat-offer"), Is.Not.Null);
        var button = content.GetComponentInChildren<Button>();
        Assert.That(Array.Exists(content.GetComponentsInChildren<TMP_Text>(), label => label.text == "25 gold"), Is.True);
        Assert.That(button.interactable, Is.True);
        Assert.That(button.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("Buy"));
        yield break;
    }
    [UnityTest]
    public IEnumerator Catalog_MixedCategories_GroupsWithInventoryStyleHeadersAndSortsNames()
    {
        backend.MixedCategories = true;
        yield return shop.Refresh(() => { }, error => Assert.Fail(error.Message));
        var names = new System.Collections.Generic.List<string>();
        foreach (Transform child in content) names.Add(child.name);
        Assert.That(names, Is.EqualTo(new[] { "Header_Back", "Offer_pack-offer", "Header_Headwear", "Offer_cap-offer", "Offer_hat-offer", "Header_Main Hand", "Offer_sword-offer", "Header_seasonal", "Offer_glow-offer" }));
        Assert.That(content.Find("Header_Main Hand").GetComponentInChildren<TMP_Text>().text, Is.EqualTo("Main Hand"));
        Assert.That(content.Find("Offer_outfit-offer"), Is.Null);
    }
    [UnityTest]
    public IEnumerator ConfirmedOwnership_UpdatesRowAndShutdownClearsCatalog()
    {
        yield return shop.Purchase("hat-offer", state => { }, error => Assert.Fail(error.Message));
        yield return null;
        Assert.That(content.GetComponentInChildren<Button>().interactable, Is.False);
        shop.Shutdown(); yield return null;
        Assert.That(content.childCount, Is.Zero);
    }
    [UnityTest]
    public IEnumerator PurchaseRequest_OwnedBySampleController_OutlivesDisabledPanel()
    {
        CreateController();
        backend.ShouldWait = true;
        content.GetComponentInChildren<Button>().onClick.Invoke();
        root.SetActive(false);
        backend.ShouldWait = false;
        yield return null;
        yield return null;
        Assert.That(inventory.Snapshot.Items.ContainsKey("hat"), Is.True);
        Assert.That(wallet.Snapshot.Balances["gold"].Amount, Is.EqualTo(75));
    }
    [UnityTest]
    public IEnumerator PreviewBinding_EmitsIntentThenShowsBuy_WithoutExecutingPurchase()
    {
        var panel = root.GetComponent<ShopPanel>();
        string requested = null;
        Action<string> preview = offerId => requested = offerId;
        panel.PreviewRequested += preview;
        try
        {
            panel.SetPreview(null, true);
            content.GetComponentInChildren<Button>().onClick.Invoke();
            Assert.That(requested, Is.EqualTo("hat-offer"));
            panel.SetPreview("hat-offer", true);
            Assert.That(content.GetComponentInChildren<Button>().GetComponentInChildren<TMP_Text>().text, Is.EqualTo("Buy"));
            Assert.That(inventory.Snapshot.Items, Is.Empty);
        }
        finally { panel.PreviewRequested -= preview; }
        yield break;
    }
    [UnityTest]
    public IEnumerator DisabledController_CancelsPurchaseAndReleasesAccountReservations()
    {
        CreateController();
        backend.ShouldWait = true;
        content.GetComponentInChildren<Button>().onClick.Invoke();
        controllerObject.SetActive(false);
        yield return null;
        Assert.That(shop.IsBusy || wallet.IsBusy || inventory.IsBusy, Is.False);
        Assert.That(shop.RequiresRefresh, Is.True);
    }
    [UnityTest]
    public IEnumerator SetsView_FiltersOffersAndSharesConfirmedOwnership()
    {
        var setObject = new GameObject("Sets view", typeof(RectTransform)); setObject.SetActive(false); setObject.transform.SetParent(root.transform);
        var setContent = new GameObject("Set content", typeof(RectTransform)).transform; setContent.SetParent(setObject.transform);
        var status = new GameObject("Set status", typeof(RectTransform)).AddComponent<TextMeshProUGUI>(); status.transform.SetParent(setObject.transform);
        var panel = setObject.AddComponent<SetsPanel>();
#if UNITY_EDITOR
        var fields = new SerializedObject(panel); fields.FindProperty("contentContainer").objectReferenceValue = setContent; fields.FindProperty("statusText").objectReferenceValue = status; fields.ApplyModifiedPropertiesWithoutUndo();
#endif
        panel.Bind(shop, inventory); setObject.SetActive(true); yield return null;
        Assert.That(content.Find("Offer_outfit-offer"), Is.Null);
        Assert.That(setContent.Find("Offer_outfit-offer"), Is.Not.Null);
        yield return shop.Purchase("hat-offer", state => { }, error => Assert.Fail(error.Message));
        Assert.That(setContent.GetComponentInChildren<Button>().interactable, Is.False);
    }
    [UnityTest]
    public IEnumerator ShopTeardown_ClosesPurchasePromptWhileControllerRemainsEnabled()
    {
        CreateController();
        root.AddComponent<Canvas>();
        var previewObject = new GameObject("Optional sample preview");
        try
        {
            var controller = controllerObject.GetComponent<ShopSampleController>();
            controller.BindPreview(previewObject.AddComponent<AccessoryEquipService>());
            controller.PurchaseOffer("hat-offer");
            yield return null;
            var prompt = root.GetComponentInChildren<PurchaseConfirmationPrompt>();
            Assert.That(prompt, Is.Not.Null);
            shop.Shutdown();
            yield return null;
            Assert.That(prompt == null, Is.True);
            Assert.That(controller.enabled, Is.True);
        }
        finally { Object.DestroyImmediate(previewObject); }
    }
    [UnityTest]
    public IEnumerator ControllerDisabledDuringCommittedCompletion_DoesNotOpenNewPrompt()
    {
        CreateController();
        root.AddComponent<Canvas>();
        var previewObject = new GameObject("Optional sample preview");
        Action disableOwner = () => { if (!shop.IsBusy) controllerObject.SetActive(false); };
        try
        {
            var controller = controllerObject.GetComponent<ShopSampleController>();
            controller.BindPreview(previewObject.AddComponent<AccessoryEquipService>());
            shop.StateChanged += disableOwner;
            controller.PurchaseOffer("hat-offer");
            yield return null;
            Assert.That(controllerObject.activeSelf, Is.False);
            Assert.That(root.GetComponentInChildren<PurchaseConfirmationPrompt>(), Is.Null);
        }
        finally { shop.StateChanged -= disableOwner; Object.DestroyImmediate(previewObject); }
    }
    private void CreateController()
    {
        controllerObject = new GameObject("Persistent shop sample owner"); controllerObject.SetActive(false);
        var controller = controllerObject.AddComponent<ShopSampleController>();
#if UNITY_EDITOR
        var fields = new SerializedObject(controller); fields.FindProperty("shopPanel").objectReferenceValue = root.GetComponent<ShopPanel>(); fields.ApplyModifiedPropertiesWithoutUndo();
#endif
        controller.Bind(shop, inventory, new SessionStub());
        controllerObject.SetActive(true);
    }
    private sealed class SessionStub : IAuthenticatedSession
    {
        public event Action<string> SessionEnded { add { } remove { } }
        public bool IsActive => true;
        public int SessionVersion => 0;
        public IEnumerator Send(UnityEngine.Networking.UnityWebRequest request, ApiResponse response, Action<bool> onComplete = null) { throw new NotSupportedException(); }
        public void RejectSession(int expectedVersion, string reason = null) { Assert.Fail(reason); }
        public void Dispose() { }
    }
    private sealed class Backend : IShopBackend, IWalletBackend, IInventoryBackend
    {
        public bool ShouldWait;
        public bool MixedCategories;
        private ShopState State(bool isOwned)
        {
            var offers = new System.Collections.Generic.List<ShopOffer> { new ShopOffer("hat-offer", "Hat", "headwear", false, new[] { "hat" }, new[] { new ShopPrice("gold", 25) }), new ShopOffer("outfit-offer", "Outfit", "bundles", true, new[] { "hat" }, new[] { new ShopPrice("gold", 50) }) };
            if (MixedCategories)
            {
                offers.Add(new ShopOffer("sword-offer", "Sword", "mainhand", false, new[] { "sword" }, Array.Empty<ShopPrice>()));
                offers.Add(new ShopOffer("cap-offer", "Cap", "headwear", false, new[] { "cap" }, Array.Empty<ShopPrice>()));
                offers.Add(new ShopOffer("pack-offer", "Pack", "back", false, new[] { "pack" }, Array.Empty<ShopPrice>()));
                offers.Add(new ShopOffer("glow-offer", "Glow", "seasonal", false, new[] { "glow" }, Array.Empty<ShopPrice>()));
            }
            return new ShopState(new ShopSnapshot(offers),
                new WalletSnapshot(new[] { new WalletBalance("gold", isOwned ? 75 : 100) }, Array.Empty<WalletConversionOffer>()),
                new InventorySnapshot(isOwned ? new[] { new CosmeticItem("hat", "Hat", "headwear") } : Array.Empty<CosmeticItem>(), Array.Empty<CosmeticSet>()), isOwned ? "hat-offer" : null);
        }
        public IEnumerator Fetch(Action<ShopState> onSuccess, Action<ShopError> onFailure) { onSuccess(State(false)); yield break; }
        public IEnumerator Fetch(Action<WalletSnapshot> onSuccess, Action<WalletError> onFailure) { onSuccess(State(false).Wallet); yield break; }
        public IEnumerator Fetch(Action<InventorySnapshot> onSuccess, Action<InventoryError> onFailure) { onSuccess(State(false).Inventory); yield break; }
        public IEnumerator Convert(string offerId, long amount, Action<WalletSnapshot> onSuccess, Action<WalletError> onFailure) { throw new NotSupportedException(); }
        public IEnumerator Purchase(string offerId, Action<ShopState> onSuccess, Action<ShopError> onFailure) { while (ShouldWait) yield return null; onSuccess(State(true)); }
    }
}
