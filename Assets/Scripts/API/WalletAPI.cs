using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System;
using Newtonsoft.Json;

public class WalletAPI : MonoBehaviour
{
    private static string WalletUrl => ApiConfig.BASE_URL + "/api/wallet/";

    private static WalletAPI instance;
    public static WalletAPI Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject go = new GameObject("WalletAPI");
                instance = go.AddComponent<WalletAPI>();
                DontDestroyOnLoad(go);
            }
            return instance;
        }
    }

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
            Destroy(gameObject);
    }

    public IEnumerator GetWallet(Action<List<WalletEntry>> onSuccess, Action<string> onError)
    {
        UnityWebRequest request = UnityWebRequest.Get(WalletUrl);
        var response = new ApiResponse();
        yield return request.SendWithTokenRefresh(response);

        if (!response.IsSuccess)
        {
            onError?.Invoke(response.Error);
        }
        else
        {
            try
            {
                var wallets = JsonConvert.DeserializeObject<List<WalletEntry>>(response.Text)
                    ?? throw new InvalidOperationException(
                        $"WalletAPI.GetWallet: deserialized to null from: {response.Text}");
                onSuccess?.Invoke(wallets);
            }
            catch (Exception e)
            {
                onError?.Invoke($"Error parsing wallet response: {e.Message}");
            }
        }
    }

    public IEnumerator ConvertCurrency(string currencyType, int amount, Action<bool, int, string> onSuccess, Action<string> onError)
    {
        yield return ShopAPI.Instance.ConvertCurrency(currencyType, amount, onSuccess, onError);
    }
}

[System.Serializable]
public class WalletCurrencyInfo
{
    public string name;
}

[System.Serializable]
public class WalletEntry
{
    public WalletCurrencyInfo currency;
    public int amount;
}
