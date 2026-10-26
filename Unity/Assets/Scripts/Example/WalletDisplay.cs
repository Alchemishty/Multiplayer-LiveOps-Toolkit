using System;
using System.Collections;
using System.Text;
using MultiplayerLiveOpsToolkit.Wallet;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WalletDisplay : MonoBehaviour
{
    public static WalletDisplay Instance { get; private set; }
    [SerializeField] private TMP_Text walletText;
    [SerializeField] private DevAuthBootstrap authBootstrap;
    [SerializeField] private Button convertSnowflakesButton;
    [SerializeField] private Button convertGearsButton;
    [SerializeField] private Button refreshButton;
    private IWalletService wallet;
    private string errorMessage;
    private bool hasListeners;
    private TMP_Text snowflakesLabel;
    private TMP_Text gearsLabel;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }
    private void OnEnable()
    {
        if (walletText == null || convertSnowflakesButton == null || convertGearsButton == null || refreshButton == null)
            throw new InvalidOperationException("WalletDisplay requires its balance text, conversion buttons and refresh button.");
        snowflakesLabel = convertSnowflakesButton.GetComponentInChildren<TMP_Text>()
            ?? throw new InvalidOperationException("WalletDisplay snowflakes button requires a TMP label.");
        gearsLabel = convertGearsButton.GetComponentInChildren<TMP_Text>()
            ?? throw new InvalidOperationException("WalletDisplay gears button requires a TMP label.");
        walletText.richText = false;
        convertSnowflakesButton.onClick.AddListener(OnSnowflakesClicked);
        convertGearsButton.onClick.AddListener(OnGearsClicked);
        refreshButton.onClick.AddListener(OnRefreshClicked);
        hasListeners = true;
        if (wallet != null) wallet.SnapshotChanged += Render;
        Render();
    }
    private IEnumerator Start()
    {
        if (wallet != null) yield break; // Explicit binding supports a UI-only consumer.
        if (authBootstrap == null) throw new InvalidOperationException("WalletDisplay needs an explicit wallet binding or sample authBootstrap.");
        while (!authBootstrap.IsAuthenticated) yield return null;
        Bind(authBootstrap.Wallet ?? throw new InvalidOperationException("WalletDisplay requires Wallet enabled in the sample bootstrap."));
    }
    public void Bind(IWalletService walletService)
    {
        if (walletService == null) throw new ArgumentNullException(nameof(walletService));
        if (hasListeners && wallet != null) wallet.SnapshotChanged -= Render;
        wallet = walletService;
        errorMessage = null;
        if (hasListeners) { wallet.SnapshotChanged += Render; Render(); }
    }
    public IEnumerator Refresh()
    {
        var service = wallet ?? throw new InvalidOperationException("WalletDisplay must be bound before refreshing.");
        errorMessage = null;
        var operation = service.Refresh(() => { errorMessage = null; }, OnFailure);
        Render();
        try { yield return operation; }
        finally { (operation as IDisposable)?.Dispose(); Render(); }
    }
    private IEnumerator Convert(WalletConversionOffer offer)
    {
        errorMessage = null;
        var operation = wallet.Convert(offer.OfferId, offer.SourceUnits, () => { errorMessage = null; }, OnFailure);
        Render();
        try { yield return operation; }
        finally { (operation as IDisposable)?.Dispose(); Render(); }
    }
    private void OnFailure(WalletError error)
    {
        errorMessage = error.Message;
        if (error.Code == WalletErrorCode.Misconfigured && authBootstrap != null && authBootstrap.Context != null)
        {
            var session = authBootstrap.Context.Session;
            session.RejectSession(session.SessionVersion, error.Message);
        }
    }
    private WalletConversionOffer FindOffer(string sourceCurrency)
    {
        if (wallet == null || !wallet.HasSnapshot) return null;
        foreach (var offer in wallet.Snapshot.Offers.Values)
            if (offer.SourceCurrencyId == sourceCurrency) return offer;
        return null; // A backend can intentionally omit a conversion.
    }
    private void OnSnowflakesClicked() => StartConversion("snowflakes");
    private void OnGearsClicked() => StartConversion("gears");
    private void StartConversion(string currency)
    {
        var offer = FindOffer(currency);
        if (offer != null && !wallet.IsBusy) StartCoroutine(Convert(offer));
    }
    private void OnRefreshClicked() { if (wallet != null && !wallet.IsBusy) StartCoroutine(Refresh()); }
    private void Render()
    {
        if (!hasListeners) return;
        var display = new StringBuilder();
        if (wallet != null && wallet.HasSnapshot)
            foreach (var balance in wallet.Snapshot.Balances.Values)
                display.Append(balance.CurrencyId).Append(": ").Append(balance.Amount).Append("  ");
        if (!string.IsNullOrEmpty(errorMessage)) display.Append(errorMessage);
        walletText.text = display.ToString().Trim();
        SetConversionButton(convertSnowflakesButton, snowflakesLabel, FindOffer("snowflakes"));
        SetConversionButton(convertGearsButton, gearsLabel, FindOffer("gears"));
        refreshButton.interactable = wallet != null && !wallet.IsBusy;
    }
    private void SetConversionButton(Button button, TMP_Text label, WalletConversionOffer offer)
    {
        button.interactable = offer != null && !wallet.IsBusy;
        label.richText = false;
        label.text = offer == null ? "Unavailable" : $"{offer.SourceUnits} {offer.SourceCurrencyId} > {offer.TargetUnits} {offer.TargetCurrencyId}";
    }
    private void OnDisable()
    {
        if (!hasListeners) return;
        if (wallet != null) wallet.SnapshotChanged -= Render;
        convertSnowflakesButton.onClick.RemoveListener(OnSnowflakesClicked);
        convertGearsButton.onClick.RemoveListener(OnGearsClicked);
        refreshButton.onClick.RemoveListener(OnRefreshClicked);
        hasListeners = false;
        convertSnowflakesButton.interactable = false;
        convertGearsButton.interactable = false;
        refreshButton.interactable = false;
    }
    private void OnDestroy() { if (Instance == this) Instance = null; }
}
