using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System;
using Newtonsoft.Json;

/// <summary>
/// Coroutine-based REST client for all shop, inventory, and currency endpoints.
/// Uses SendWithTokenRefresh for automatic JWT handling on every request.
/// </summary>
public class ShopAPI : MonoBehaviour
{
    private static string AccessoriesApiUrl => ApiConfig.BASE_URL + "/api/shop/accessories/";
    private static string AccessorySetsApiUrl => ApiConfig.BASE_URL + "/api/shop/sets/";
    private static string PurchaseApiUrl => ApiConfig.BASE_URL + "/api/shop/purchase/";
    private static string PurchaseSetApiUrl => ApiConfig.BASE_URL + "/api/shop/purchase-set/";
    private static string ConvertCurrencyApiUrl => ApiConfig.BASE_URL + "/api/shop/convert-currency/";
    private static string UpdateEquippedApiUrl => ApiConfig.BASE_URL + "/api/accessories/update-equipped/";
    private static string EquippedAccessoriesApiUrl => ApiConfig.BASE_URL + "/api/accessories/get-equipped/";

    private static ShopAPI instance;
    public static ShopAPI Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject go = new GameObject("ShopManager");
                instance = go.AddComponent<ShopAPI>();
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

    /// <summary>
    /// GET /api/shop/accessories/ — Full shop catalog grouped by type with ownership and costs.
    /// Response: {"accessories": {"head": [{"id":1, "name":"Crown", "is_owned":false, "costs":[...]}]}}
    /// </summary>
    public IEnumerator GetAccessories(Action<AccessoriesResponse> onSuccess, Action<string> onError)
    {
        UnityWebRequest request = UnityWebRequest.Get(AccessoriesApiUrl);
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
                var parsed = JsonConvert.DeserializeObject<AccessoriesResponse>(response.Text)
                    ?? throw new InvalidOperationException(
                        $"ShopAPI.GetAccessories: deserialized to null from: {response.Text}");
                onSuccess?.Invoke(parsed);
            }
            catch (Exception e)
            {
                onError?.Invoke($"Error parsing response: {e.Message}");
            }
        }
    }

    /// <summary>
    /// POST /api/shop/purchase/ — Purchase an accessory. Deducts all required currencies atomically.
    /// Body: {"accessory_id": 1}. Returns (true, message) on success, (false, reason) on 400.
    /// </summary>
    public IEnumerator PurchaseAccessory(int accessoryId, Action<bool, string> onSuccess, Action<string> onError)
    {
        var requestData = new PurchaseRequest { accessory_id = accessoryId };
        string jsonData = JsonConvert.SerializeObject(requestData);

        UnityWebRequest request = new UnityWebRequest(PurchaseApiUrl, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();

        var response = new ApiResponse();
        yield return request.SendWithTokenRefresh(response);

        if (!response.IsSuccess)
        {
            if (response.ResponseCode == 400)
            {
                var errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(response.Text);
                onSuccess?.Invoke(false, errorResponse.message);
            }
            else
            {
                onError?.Invoke($"Request failed: {response.Error}");
            }
        }
        else
        {
            onSuccess?.Invoke(true, "Accessory purchased successfully");
        }
    }

    /// <summary>
    /// GET /api/shop/sets/ — All accessory sets grouped by category with ownership and costs.
    /// </summary>
    public IEnumerator GetAccessorySets(Action<AccessorySetResponse> onSuccess, Action<string> onError)
    {
        UnityWebRequest request = UnityWebRequest.Get(AccessorySetsApiUrl);
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
                var parsed = JsonConvert.DeserializeObject<AccessorySetResponse>(response.Text)
                    ?? throw new InvalidOperationException(
                        $"ShopAPI.GetAccessorySets: deserialized to null from: {response.Text}");
                onSuccess?.Invoke(parsed);
            }
            catch (Exception e)
            {
                onError?.Invoke($"Error parsing sets response: {e.Message}");
            }
        }
    }

    public IEnumerator PurchaseSet(int setId, Action<bool, string, List<int>> onSuccess, Action<string> onError)
    {
        var requestData = new PurchaseSetRequest { set_id = setId };
        string jsonData = JsonConvert.SerializeObject(requestData);

        UnityWebRequest request = new UnityWebRequest(PurchaseSetApiUrl, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();

        var response = new ApiResponse();
        yield return request.SendWithTokenRefresh(response);

        if (!response.IsSuccess)
        {
            if (response.ResponseCode == 400)
            {
                var errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(response.Text)
                    ?? throw new InvalidOperationException(
                        $"ShopAPI.PurchaseSet: failed to deserialize error from: {response.Text}");
                onSuccess?.Invoke(false, errorResponse.message ?? "Purchase failed", null);
            }
            else
            {
                onError?.Invoke($"Request failed: {response.Error}");
            }
        }
        else
        {
            try
            {
                var parsed = JsonConvert.DeserializeObject<PurchaseSetResponse>(response.Text)
                    ?? throw new InvalidOperationException(
                        $"ShopAPI.PurchaseSet: deserialized to null from: {response.Text}");
                onSuccess?.Invoke(true, "Set purchased successfully", parsed.accessory_ids);
            }
            catch (Exception e)
            {
                onError?.Invoke($"Error parsing purchase-set response: {e.Message}");
            }
        }
    }

    /// <summary>
    /// POST /api/accessories/update-equipped/ — Replace-all equip. Sends the full set of IDs to equip.
    /// Body: {"accessory_ids": [1, 5, 12]}
    /// </summary>
    public IEnumerator UpdateEquippedAccessories(List<int> accessoryIds, Action<bool, string> onSuccess, Action<string> onError)
    {
        var payload = new { accessory_ids = accessoryIds };
        string jsonData = JsonConvert.SerializeObject(payload);

        UnityWebRequest request = new UnityWebRequest(UpdateEquippedApiUrl, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();

        var response = new ApiResponse();
        yield return request.SendWithTokenRefresh(response);

        if (response.IsSuccess)
            onSuccess?.Invoke(true, "Equipped accessories updated");
        else
            onError?.Invoke($"Failed to update equipped accessories: {response.Error}");
    }

    /// <summary>
    /// GET /api/accessories/get-equipped/ — Currently equipped accessory IDs.
    /// Response: {"accessory_ids": [1, 5, 12]}
    /// </summary>
    public IEnumerator GetEquippedAccessories(Action<List<int>> onSuccess, Action<string> onError)
    {
        UnityWebRequest request = UnityWebRequest.Get(EquippedAccessoriesApiUrl);
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
                var parsed = JsonConvert.DeserializeObject<EquippedIdsResponse>(response.Text)
                    ?? throw new InvalidOperationException(
                        $"ShopAPI.GetEquippedAccessories: deserialized to null from: {response.Text}");
                onSuccess?.Invoke(parsed.accessory_ids);
            }
            catch (Exception e)
            {
                onError?.Invoke($"Error parsing response: {e.Message}");
            }
        }
    }

    /// <summary>
    /// POST /api/shop/convert-currency/ — Convert secondary currency to gold.
    /// Body: {"currency_type": "snowflakes", "amount": 100}
    /// Returns (true, goldAmount, message) or (false, 0, errorMessage).
    /// </summary>
    public IEnumerator ConvertCurrency(string currencyType, int amount, Action<bool, int, string> onSuccess, Action<string> onError)
    {
        WWWForm form = new WWWForm();
        form.AddField("currency_type", currencyType);
        form.AddField("amount", amount.ToString());

        UnityWebRequest request = UnityWebRequest.Post(ConvertCurrencyApiUrl, form);
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
                if (response.ResponseCode == 200)
                {
                    var parsed = JsonConvert.DeserializeObject<ConvertCurrencyResponse>(response.Text)
                        ?? throw new InvalidOperationException(
                            $"ShopAPI.ConvertCurrency: deserialized to null from: {response.Text}");
                    onSuccess?.Invoke(true, parsed.gold_amount, "Currency converted successfully");
                }
                else if (response.ResponseCode == 400)
                {
                    var error = JsonConvert.DeserializeObject<ErrorResponse>(response.Text)
                        ?? throw new InvalidOperationException(
                            $"ShopAPI.ConvertCurrency: deserialized error to null from: {response.Text}");
                    onSuccess?.Invoke(false, 0, error.message);
                }
                else
                {
                    onError?.Invoke($"Unexpected response code: {response.ResponseCode}");
                }
            }
            catch (Exception e)
            {
                onError?.Invoke($"Error parsing response: {e.Message}");
            }
        }
    }
}

[System.Serializable]
public class PurchaseRequest
{
    public int accessory_id;
}

/// <summary>Standard error format from all backend endpoints: {"message": "..."}</summary>
[System.Serializable]
public class ErrorResponse
{
    public string message;
}

[System.Serializable]
public class ConvertCurrencyResponse
{
    public int gold_amount;
}

[System.Serializable]
public class EquippedIdsResponse
{
    public List<int> accessory_ids;
}
