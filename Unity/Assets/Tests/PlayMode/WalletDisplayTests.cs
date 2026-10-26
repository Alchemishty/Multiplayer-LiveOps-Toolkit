using System;
using System.Collections;
using MultiplayerLiveOpsToolkit.Wallet;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Object = UnityEngine.Object;

public class WalletDisplayTests
{
    private GameObject root;
    private WalletModule module;
    private TextMeshProUGUI label;
    private Button gearsButton;
    private BackendStub backend;
    [UnitySetUp]
    public IEnumerator SetUp()
    {
        backend = new BackendStub();
        module = new WalletModule(backend, exception => Assert.Fail(exception.Message));
        yield return module.Initialize();
        root = new GameObject("Wallet UI without networking", typeof(RectTransform));
        root.SetActive(false);
        label = new GameObject("Balance text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        label.transform.SetParent(root.transform);
        gearsButton = CreateButton("Convert gears");
        var snowButton = CreateButton("Convert snowflakes");
        var refresh = CreateButton("Refresh");
        var view = root.AddComponent<WalletDisplay>();
#if UNITY_EDITOR
        var fields = new SerializedObject(view);
        fields.FindProperty("walletText").objectReferenceValue = label;
        fields.FindProperty("convertGearsButton").objectReferenceValue = gearsButton;
        fields.FindProperty("convertSnowflakesButton").objectReferenceValue = snowButton;
        fields.FindProperty("refreshButton").objectReferenceValue = refresh;
        fields.ApplyModifiedPropertiesWithoutUndo();
#endif
        view.Bind(module);
        root.SetActive(true);
        yield return null;
    }
    [TearDown]
    public void TearDown() { module.Shutdown(); Object.DestroyImmediate(root); }
    [UnityTest]
    public IEnumerator BoundService_RendersWithoutSampleLoginOrNetcode_AndClearsOnShutdown()
    {
        Assert.That(label.text, Does.Contain("gold: 10"));
        module.Shutdown();
        yield return null;
        Assert.That(label.text, Does.Not.Contain("gold: 10"));
        Assert.That(gearsButton.interactable, Is.False);
    }
    [UnityTest]
    public IEnumerator ConversionButton_UsesBackendOfferAndRendersConfirmedBalance()
    {
        gearsButton.onClick.Invoke();
        yield return null;
        Assert.That(backend.ConvertCount, Is.EqualTo(1));
        Assert.That(backend.LastAmount, Is.EqualTo(1));
        Assert.That(label.text, Does.Contain("gold: 35"));
    }
    [UnityTest]
    public IEnumerator DisabledView_DisablesConversionAndRefreshControls()
    {
        root.GetComponent<WalletDisplay>().enabled = false;
        yield return null;
        Assert.That(gearsButton.interactable, Is.False);
    }
    private Button CreateButton(string name)
    {
        var button = new GameObject(name, typeof(RectTransform)).AddComponent<Button>();
        button.transform.SetParent(root.transform);
        var text = new GameObject("Label", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        text.transform.SetParent(button.transform);
        return button;
    }
    private sealed class BackendStub : IWalletBackend
    {
        public int ConvertCount;
        public long LastAmount;
        private WalletSnapshot State(long gold) => new WalletSnapshot(new[] { new WalletBalance("gold", gold), new WalletBalance("gears", 5) }, new[] { new WalletConversionOffer("gear-offer", "gears", "gold", 1, 25) });
        public IEnumerator Fetch(Action<WalletSnapshot> onSuccess, Action<WalletError> onFailure) { onSuccess(State(10)); yield break; }
        public IEnumerator Convert(string offerId, long amount, Action<WalletSnapshot> onSuccess, Action<WalletError> onFailure) { ConvertCount++; LastAmount = amount; onSuccess(State(35)); yield break; }
    }
}
