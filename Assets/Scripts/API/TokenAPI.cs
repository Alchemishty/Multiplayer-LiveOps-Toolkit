using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using System;
using Newtonsoft.Json;

[Serializable]
public class TokenResponse
{
    public string access;
    public string refresh;
}

[Serializable]
public class RefreshTokenRequest
{
    public string refresh;
}

/// <summary>
/// JWT token manager. Stores access/refresh tokens in PlayerPrefs.
/// Use SendWithTokenRefresh on any UnityWebRequest for automatic auth.
/// </summary>
public class TokenAPI : MonoBehaviour
{
    private static string ApiUrl => ApiConfig.BASE_URL + "/api/auth/refresh/";
    private const string ACCESS_TOKEN_KEY = "access_token";
    private const string REFRESH_TOKEN_KEY = "refresh_token";
    private const string USERNAME_KEY = "username";

    private static TokenAPI instance;
    public static TokenAPI Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject go = new GameObject("TokenManager");
                instance = go.AddComponent<TokenAPI>();
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
        {
            Destroy(gameObject);
        }
    }

    public string GetAccessToken()
    {
        string stored = PlayerPrefs.GetString(ACCESS_TOKEN_KEY, "");
        if (string.IsNullOrEmpty(stored)) return "";
        try { return System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(stored)); }
        catch { return stored; }
    }

    /// <summary>Store both JWT tokens. Called after login or refresh.</summary>
    public void SetTokens(string accessToken, string refreshToken)
    {
        PlayerPrefs.SetString(ACCESS_TOKEN_KEY, System.Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(accessToken)));
        PlayerPrefs.SetString(REFRESH_TOKEN_KEY, System.Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(refreshToken)));
        PlayerPrefs.Save();
    }

    public string GetUsername() => PlayerPrefs.GetString(USERNAME_KEY, "");

    public void SetUsername(string username)
    {
        PlayerPrefs.SetString(USERNAME_KEY, username);
        PlayerPrefs.Save();
    }

    public void ClearTokens()
    {
        PlayerPrefs.DeleteKey(ACCESS_TOKEN_KEY);
        PlayerPrefs.DeleteKey(REFRESH_TOKEN_KEY);
        PlayerPrefs.Save();
    }

    /// <summary>POST /api/auth/refresh/ — Exchange refresh token for a new access token.</summary>
    public IEnumerator RefreshToken(Action<bool> onComplete = null)
    {
        string stored = PlayerPrefs.GetString(REFRESH_TOKEN_KEY, "");
        string refreshToken = "";

        if (!string.IsNullOrEmpty(stored))
        {
            try { refreshToken = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(stored)); }
            catch { refreshToken = stored; }
        }

        if (string.IsNullOrEmpty(refreshToken))
        {
            Debug.LogWarning("No refresh token available");
            onComplete?.Invoke(false);
            yield break;
        }

        var requestData = new RefreshTokenRequest { refresh = refreshToken };
        string jsonData = JsonConvert.SerializeObject(requestData);

        using (UnityWebRequest request = new UnityWebRequest(ApiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    TokenResponse response = JsonConvert.DeserializeObject<TokenResponse>(request.downloadHandler.text);
                    if (!string.IsNullOrEmpty(response.access))
                    {
                        SetTokens(response.access, refreshToken);
                        onComplete?.Invoke(true);
                    }
                    else
                    {
                        ClearTokens();
                        onComplete?.Invoke(false);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to parse token refresh response: {e.Message}");
                    onComplete?.Invoke(false);
                }
            }
            else
            {
                if (request.responseCode == 401)
                    Debug.LogError("Refresh token is invalid or expired");

                onComplete?.Invoke(false);
            }
        }
    }
}

public class ApiResponse
{
    public UnityWebRequest.Result Result;
    public long ResponseCode;
    public string Error;
    public string Text;
    public bool IsSuccess => Result == UnityWebRequest.Result.Success;

    public void CopyFrom(UnityWebRequest request)
    {
        Result = request.result;
        ResponseCode = request.responseCode;
        Error = request.error;
        Text = request.downloadHandler?.text;
    }
}

/// <summary>
/// Extension method for UnityWebRequest. Attaches Bearer token, sends the
/// request, and retries once after a 401 by transparently refreshing the JWT.
/// Usage: var response = new ApiResponse();
///        yield return request.SendWithTokenRefresh(response);
/// </summary>
public static class UnityWebRequestExtensions
{
    public static IEnumerator SendWithTokenRefresh(this UnityWebRequest request, ApiResponse response, Action<bool> onComplete = null)
    {
        string accessToken = TokenAPI.Instance.GetAccessToken();
        request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.responseCode == 401)
        {
            bool refreshSuccess = false;
            yield return TokenAPI.Instance.RefreshToken(success => refreshSuccess = success);

            if (refreshSuccess)
            {
                using (UnityWebRequest retryRequest = new UnityWebRequest(request.url, request.method))
                {
                    if (request.uploadHandler?.data != null &&
                        (request.method == UnityWebRequest.kHttpVerbPOST || request.method == UnityWebRequest.kHttpVerbPUT))
                    {
                        retryRequest.uploadHandler = new UploadHandlerRaw(request.uploadHandler.data);
                    }
                    retryRequest.downloadHandler = new DownloadHandlerBuffer();
                    retryRequest.SetRequestHeader("Content-Type", "application/json");
                    retryRequest.SetRequestHeader("Authorization", $"Bearer {TokenAPI.Instance.GetAccessToken()}");

                    yield return retryRequest.SendWebRequest();

                    response.CopyFrom(retryRequest);
                }
                onComplete?.Invoke(response.IsSuccess);
                yield break;
            }
        }

        response.CopyFrom(request);
        onComplete?.Invoke(response.IsSuccess);
    }
}
