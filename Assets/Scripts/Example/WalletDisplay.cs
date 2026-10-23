using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class WalletDisplay : MonoBehaviour
{
    public static WalletDisplay Instance { get; private set; }

    [SerializeField] private TMP_Text walletText;
    [SerializeField] private Button resetButton;

    private bool isResetting;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        while (DevAuthBootstrap.Instance == null || !DevAuthBootstrap.Instance.IsAuthenticated)
            yield return null;

        if (resetButton != null)
            resetButton.onClick.AddListener(OnResetClicked);

        yield return Refresh();
    }

    public IEnumerator Refresh()
    {
        bool done = false;
        yield return WalletAPI.Instance.GetWallet(
            (List<WalletEntry> wallets) =>
            {
                string display = "";
                foreach (var w in wallets)
                    display += $"{w.currency.name}: {w.amount}  ";
                if (walletText != null)
                    walletText.text = display.Trim();
                done = true;
            },
            (string error) =>
            {
                Debug.LogWarning($"Failed to fetch wallet: {error}");
                done = true;
            }
        );
        while (!done) yield return null;
    }

    private void OnResetClicked()
    {
        if (isResetting) return;
        StartCoroutine(ResetAccount());
    }

    private IEnumerator ResetAccount()
    {
        isResetting = true;
        resetButton.interactable = false;

        string url = $"{ApiConfig.BASE_URL}/api/auth/reset/";
        using var request = new UnityWebRequest(url, "POST");
        request.downloadHandler = new DownloadHandlerBuffer();
        var response = new ApiResponse();
        yield return request.SendWithTokenRefresh(response);

        if (response.IsSuccess)
        {
            if (AccessoryEquipService.Instance != null)
            {
                AccessoryEquipService.Instance.ClearPreview();
                AccessoryEquipService.Instance.ClearSetPreview();
            }

            yield return Refresh();

            var registry = AccessoryRegistry.Instance
                ?? throw new InvalidOperationException(
                    $"{nameof(AccessoryRegistry)} not initialized during reset");
            yield return registry.ReloadShopCatalog();

            var localAvatar = ClientPlayerAvatars.Instance != null
                ? ClientPlayerAvatars.Instance.currentClientAvatar
                : null;
            if (localAvatar != null)
            {
                var playerAccessory = localAvatar.GetComponent<PlayerAccessory>();
                if (playerAccessory != null)
                    playerAccessory.ClearAllEquipped();
            }
        }
        else
        {
            Debug.LogWarning($"Account reset failed: {response.Error}");
        }

        resetButton.interactable = true;
        isResetting = false;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
