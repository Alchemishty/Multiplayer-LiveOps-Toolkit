using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using MultiplayerLiveOpsToolkit.Wallet;
using MultiplayerLiveOpsToolkit.Inventory;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>Sample-only account reset; deliberately outside the Wallet module.</summary>
public sealed class DevelopmentAccountReset : MonoBehaviour
{
    [SerializeField] private DevAuthBootstrap authBootstrap;
    [SerializeField] private Button resetButton;
    private IAuthenticatedSession session;
    private string backendUrl;
    private IWalletService wallet;
    private IInventoryService inventory;
    private MultiplayerLiveOpsToolkit.Equipment.IEquipmentService equipment;
    private MultiplayerLiveOpsToolkit.Achievements.IAchievementService achievements;
    private MultiplayerLiveOpsToolkit.DailyRewards.IDailyRewardService dailyRewards;
    private bool isResetting;
    private bool hasListener;

    public void Bind(IAuthenticatedSession authenticatedSession, string baseUrl, IWalletService walletService, IInventoryService inventoryService, MultiplayerLiveOpsToolkit.Equipment.IEquipmentService equipmentService = null, MultiplayerLiveOpsToolkit.Achievements.IAchievementService achievementService = null, MultiplayerLiveOpsToolkit.DailyRewards.IDailyRewardService dailyRewardService = null)
    {
        session = authenticatedSession ?? throw new ArgumentNullException(nameof(authenticatedSession));
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("Reset requires a backend URL.", nameof(baseUrl));
        wallet = walletService ?? throw new ArgumentNullException(nameof(walletService));
        inventory = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
        equipment = equipmentService;
        achievements = achievementService;
        dailyRewards = dailyRewardService;
        backendUrl = baseUrl.TrimEnd('/');
    }
    private IEnumerator Start()
    {
        if (resetButton == null || authBootstrap == null)
            throw new InvalidOperationException("DevelopmentAccountReset requires resetButton and authBootstrap.");
        resetButton.interactable = false;
        while (!authBootstrap.IsAuthenticated) yield return null;
        Bind(authBootstrap.Context.Session, ApiConfig.BASE_URL, authBootstrap.Wallet, authBootstrap.Inventory, authBootstrap.Equipment, authBootstrap.Achievements, authBootstrap.DailyRewards);
        resetButton.onClick.AddListener(OnResetClicked);
        hasListener = true;
        resetButton.interactable = true;
    }
    public IEnumerator ResetBackendAccount(ApiResponse response)
    {
        if (response == null) throw new ArgumentNullException(nameof(response));
        if (session == null) throw new InvalidOperationException("DevelopmentAccountReset requires an explicitly bound session.");
        while (wallet.IsBusy || inventory.IsBusy || (equipment != null && equipment.IsBusy) || (achievements != null && achievements.IsBusy) || (dailyRewards != null && dailyRewards.IsBusy)) yield return null;
        using (var request = new UnityWebRequest(backendUrl + "/api/auth/reset/", "POST"))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            yield return session.Send(request, response);
        }
        if (response.IsSuccess)
        {
            while (wallet.IsBusy) yield return null;
            WalletError failure = null;
            yield return wallet.Refresh(() => { }, error => failure = error);
            if (failure != null) throw new InvalidOperationException("Account reset wallet refresh failed: " + failure.Message);
            while (inventory.IsBusy) yield return null;
            InventoryError inventoryFailure = null;
            yield return inventory.Refresh(() => { }, error => inventoryFailure = error);
            if (inventoryFailure != null) throw new InvalidOperationException("Account reset inventory refresh failed: " + inventoryFailure.Message);
            if (equipment != null)
            {
                while (equipment.IsBusy) yield return null;
                MultiplayerLiveOpsToolkit.Equipment.EquipmentError equipmentFailure = null;
                yield return equipment.Refresh(() => { }, error => equipmentFailure = error);
                if (equipmentFailure != null) throw new InvalidOperationException("Account reset equipment refresh failed: " + equipmentFailure.Message);
            }
            if (achievements != null)
            {
                while (achievements.IsBusy) yield return null;
                MultiplayerLiveOpsToolkit.Achievements.AchievementError achievementFailure = null;
                yield return achievements.Refresh(() => { }, error => achievementFailure = error);
                if (achievementFailure != null) throw new InvalidOperationException("Account reset achievement refresh failed: " + achievementFailure.Message);
            }
            if (dailyRewards != null)
            {
                while (dailyRewards.IsBusy) yield return null;
                MultiplayerLiveOpsToolkit.DailyRewards.DailyRewardError dailyRewardFailure = null;
                yield return dailyRewards.Refresh(() => { }, error => dailyRewardFailure = error);
                if (dailyRewardFailure != null) throw new InvalidOperationException("Account reset daily reward refresh failed: " + dailyRewardFailure.Message);
            }
        }
    }
    private void OnResetClicked() { if (!isResetting) StartCoroutine(ResetAccount()); }
    private IEnumerator ResetAccount()
    {
        var walletView = WalletDisplay.Instance ?? throw new InvalidOperationException("Account reset requires the sample WalletDisplay.");
        bool wasViewEnabled = walletView.enabled;
        isResetting = true;
        try
        {
            resetButton.interactable = false;
            walletView.enabled = false;
            var response = new ApiResponse();
            yield return ResetBackendAccount(response);
            if (!response.IsSuccess) throw new InvalidOperationException("Account reset failed: " + response.Error);
            // Preview/avatar integrations are optional before a player has spawned.
            if (AccessoryEquipService.Instance != null)
            {
                AccessoryEquipService.Instance.ClearPreview();
                AccessoryEquipService.Instance.ClearSetPreview();
            }

        }
        finally { if (walletView != null) walletView.enabled = wasViewEnabled; isResetting = false; if (resetButton != null) resetButton.interactable = session != null && session.IsActive; }
    }
    private void OnDestroy() { if (hasListener && resetButton != null) resetButton.onClick.RemoveListener(OnResetClicked); }
}
