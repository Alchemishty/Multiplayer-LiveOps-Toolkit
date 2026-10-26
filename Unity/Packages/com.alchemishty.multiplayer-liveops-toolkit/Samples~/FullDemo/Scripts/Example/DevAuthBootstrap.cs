using MultiplayerLiveOpsToolkit.Backends.Django;
using MultiplayerLiveOpsToolkit.UnityIntegration;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System;
using TMPro;
using MultiplayerLiveOpsToolkit.Core;
using MultiplayerLiveOpsToolkit.Identity;
using MultiplayerLiveOpsToolkit.Wallet;
using MultiplayerLiveOpsToolkit.Inventory;
using MultiplayerLiveOpsToolkit.Shop;
using MultiplayerLiveOpsToolkit.Equipment;
using MultiplayerLiveOpsToolkit.Achievements;
using MultiplayerLiveOpsToolkit.DailyRewards;
using System.Collections.Generic;

[RequireComponent(typeof(DjangoPlayerSession))]
public class DevAuthBootstrap : MonoBehaviour
{
    public static DevAuthBootstrap Instance { get; private set; }
    public PlayerContext Context { get; private set; }
    public bool IsAuthenticated => Context != null && Context.IsAuthenticated;
    public event Action OnAuthenticated;

    [SerializeField] private bool enableWallet = true;
    [SerializeField] private bool enableInventory = true;
    [SerializeField] private bool enableShop = true;
    [SerializeField] private bool enableEquipment = true;
    [SerializeField] private AccessoryEquipService equipmentSample;
    public IEquipmentService Equipment { get; private set; }
    public IEquipmentProofBackend EquipmentProofs { get; private set; }
    [SerializeField] private bool enableDailyRewards = true;
    [SerializeField] private DailyRewardsSampleController dailyRewardsSample;
    public IDailyRewardService DailyRewards { get; private set; }
    [SerializeField] private bool enableAchievements = true;
    [SerializeField] private AchievementSampleController achievementSample;
    public IAchievementService Achievements { get; private set; }
    public IShopService Shop { get; private set; }
    public IInventoryService Inventory { get; private set; }
    public IWalletService Wallet { get; private set; }

    [SerializeField] private GameObject loginPanel;
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private Button playButton;
    [SerializeField] private TMP_Text statusLabel;

    [SerializeField] private InventoryPanel inventoryPanel;
    [SerializeField] private ShopSampleController shopSample;

    private IIdentityProvider submittedProvider;
    private bool hasRegisteredListeners;
    private DjangoPlayerSession playerSession;
    private string backendUrl;
    private DjangoPlayerModules playerModules;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        // Only the legacy demo migrates its old preference keys.
        PlayerPrefs.DeleteKey("access_token");
        PlayerPrefs.DeleteKey("refresh_token");
        PlayerPrefs.DeleteKey("username");
        PlayerPrefs.Save();
    }

    IEnumerator Start()
    {
        if (loginPanel == null)
            throw new InvalidOperationException(
                $"{nameof(DevAuthBootstrap)}: loginPanel is not assigned");
        if (playButton == null)
            throw new InvalidOperationException(
                $"{nameof(DevAuthBootstrap)}: playButton is not assigned");
        if (usernameInput == null)
            throw new InvalidOperationException(
                $"{nameof(DevAuthBootstrap)}: usernameInput is not assigned");
        if (statusLabel == null)
            throw new InvalidOperationException(
                $"{nameof(DevAuthBootstrap)}: statusLabel is not assigned");

        if (passwordInput == null)
            throw new InvalidOperationException($"{nameof(DevAuthBootstrap)}: passwordInput is not assigned. Regenerate the example scene.");
        passwordInput.contentType = TMP_InputField.ContentType.Password;
        passwordInput.text = "";
        loginPanel.SetActive(true);
        playButton.interactable = false;
        usernameInput.onValueChanged.AddListener(OnCredentialsChanged);
        passwordInput.onValueChanged.AddListener(OnCredentialsChanged);
        playButton.onClick.AddListener(OnPlayClicked);

        playerSession = GetComponent<DjangoPlayerSession>();
        backendUrl = ApiConfig.BASE_URL;
        var selection = DjangoModules.Identity;
        if (enableWallet) selection |= DjangoModules.Wallet;
        if (enableInventory) selection |= DjangoModules.Inventory;
        if (enableShop) selection |= DjangoModules.Shop;
        if (enableEquipment) selection |= DjangoModules.Equipment;
        if (enableAchievements) selection |= DjangoModules.Achievements;
        if (enableDailyRewards) selection |= DjangoModules.DailyRewards;
        playerModules = new DjangoPlayerModules(backendUrl, playerSession, playerSession, selection, Debug.LogException);
        Context = playerModules.Context;
        Context.SessionEnded += OnSessionEnded;
        hasRegisteredListeners = true;
        while (!IsAuthenticated)
        {
            while (submittedProvider == null)
                yield return null;

            statusLabel.text = "Authenticating...";
            statusLabel.color = new Color(0.7f, 0.8f, 1f);
            playButton.interactable = false;
            usernameInput.interactable = false;
            passwordInput.interactable = false;

            var provider = submittedProvider;
            submittedProvider = null;
            LiveOpsError failure = null;
            var initialization = playerModules.Initialize(provider, () => { }, error => failure = error);
            Wallet = playerModules.Wallet; Inventory = playerModules.Inventory; Shop = playerModules.Shop;
            Equipment = playerModules.Equipment; EquipmentProofs = playerModules.EquipmentProofs;
            Achievements = playerModules.Achievements; DailyRewards = playerModules.DailyRewards;
            yield return initialization;
            if (!IsAuthenticated)
            {
                statusLabel.text = (failure ?? throw new InvalidOperationException("Identity initialization stopped without a diagnostic.")).Message;
                statusLabel.color = new Color(1f, 0.4f, 0.4f);
                usernameInput.interactable = true;
                passwordInput.interactable = true;
                OnCredentialsChanged("");
            }
        }
        if (Inventory != null && inventoryPanel != null) inventoryPanel.Bind(Inventory);
        if (Equipment != null && equipmentSample != null)
        {
            equipmentSample.Bind(Equipment, Inventory, Context.Session, inventoryPanel);
            equipmentSample.BindAvatars(ClientPlayerAvatars.Instance ?? throw new InvalidOperationException("Sample equipment requires ClientPlayerAvatars."));
        }
        if (Shop != null && shopSample != null) shopSample.Bind(Shop, Inventory, Context.Session);
        if (Achievements != null && achievementSample != null) achievementSample.Bind(Achievements, Context.Session);
        if (DailyRewards != null && dailyRewardsSample != null) dailyRewardsSample.Bind(DailyRewards, Context.Session);
        statusLabel.text = "Connecting...";
        var subscribers = OnAuthenticated;
        if (subscribers != null)
        {
            foreach (Action subscriber in subscribers.GetInvocationList())
            {
                try { subscriber(); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        while (ClientPlayerAvatars.Instance == null
               || ClientPlayerAvatars.Instance.currentClientAvatar == null)
            yield return null;

        if (Shop != null && shopSample != null && AccessoryEquipService.Instance != null) shopSample.BindPreview(AccessoryEquipService.Instance);
        loginPanel.SetActive(false);
    }

    public void RefreshInventory()
    {
        if (Inventory == null) throw new InvalidOperationException("The sample requires Inventory enabled before refreshing ownership.");
        if (IsAuthenticated) StartCoroutine(RefreshInventoryState());
    }
    private IEnumerator RefreshInventoryState()
    {
        var service = Inventory ?? throw new InvalidOperationException("The sample requires Inventory enabled before refreshing ownership.");
        while (service.IsBusy && IsAuthenticated) yield return null;
        if (!IsAuthenticated) yield break; // Terminal account teardown already owns disconnection.
        InventoryError failure = null;
        yield return service.Refresh(() => { }, error => failure = error);
        if (failure != null && failure.Code != InventoryErrorCode.Cancelled)
            Context.Session.RejectSession(Context.Session.SessionVersion, failure.Message);
    }

    private void OnCredentialsChanged(string value)
    {
        playButton.interactable = usernameInput.interactable && passwordInput.interactable
            && !string.IsNullOrWhiteSpace(usernameInput.text) && !string.IsNullOrEmpty(passwordInput.text);
    }

    private void OnPlayClicked()
    {
        string trimmed = usernameInput.text.Trim();
        if (submittedProvider != null || !usernameInput.interactable || string.IsNullOrEmpty(trimmed) || string.IsNullOrEmpty(passwordInput.text))
            return;
        submittedProvider = new DjangoPasswordIdentityProvider(trimmed, passwordInput.text);
        passwordInput.text = "";
    }

    private void OnSessionEnded(string reason)
    {
        EndSession();
        loginPanel.SetActive(true);
        usernameInput.interactable = false;
        passwordInput.interactable = false;
        playButton.interactable = false;
        statusLabel.text = reason;
        statusLabel.color = new Color(1f, 0.4f, 0.4f);
    }

    private void OnDisable()
    {
        if (Instance == this) EndSession();
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        EndSession();
    }

    private void EndSession()
    {
        submittedProvider = null;
        try { if (playerModules != null) playerModules.Dispose(); else Context?.Dispose(); }
        finally
        {
            // No network session exists before joining; pending clients also count as listening.
            var networkManager = Unity.Netcode.NetworkManager.Singleton;
            if (networkManager != null && networkManager.IsListening) networkManager.Shutdown();
            StopAllCoroutines();
            if (hasRegisteredListeners)
            {
                // These UI objects can already have been destroyed during a scene unload.
                if (Context != null) Context.SessionEnded -= OnSessionEnded;
                if (usernameInput != null) usernameInput.onValueChanged.RemoveListener(OnCredentialsChanged);
                if (passwordInput != null)
                {
                    passwordInput.onValueChanged.RemoveListener(OnCredentialsChanged);
                    passwordInput.text = "";
                }
                if (playButton != null) playButton.onClick.RemoveListener(OnPlayClicked);
                hasRegisteredListeners = false;
            }
        }
    }
}
