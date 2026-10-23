using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System;
using Newtonsoft.Json;

public class AchievementAPI : MonoBehaviour
{
    private static string DefinitionsUrl => ApiConfig.BASE_URL + "/api/achievements/definitions/";
    private static string ProgressUrl => ApiConfig.BASE_URL + "/api/achievements/progress/";
    private static string UnlockUrl => ApiConfig.BASE_URL + "/api/achievements/unlock/";
    private static string ProgressUpdateUrl => ApiConfig.BASE_URL + "/api/achievements/progress/update/";

    private static AchievementAPI instance;
    public static AchievementAPI Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject achievementApiObject = new GameObject("AchievementAPI");
                instance = achievementApiObject.AddComponent<AchievementAPI>();
                DontDestroyOnLoad(achievementApiObject);
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

    public IEnumerator GetDefinitions(Action<AchievementDefinitionResponse> onSuccess, Action<string> onError)
    {
        UnityWebRequest request = UnityWebRequest.Get(DefinitionsUrl);
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
                var parsed = JsonConvert.DeserializeObject<AchievementDefinitionResponse>(response.Text)
                    ?? throw new InvalidOperationException(
                        "AchievementAPI: failed to deserialize achievement definitions response");
                onSuccess?.Invoke(parsed);
            }
            catch (Exception exception)
            {
                onError?.Invoke($"Error parsing achievement definitions: {exception.Message}");
            }
        }
    }

    public IEnumerator GetProgress(Action<AchievementProgressResponse> onSuccess, Action<string> onError)
    {
        UnityWebRequest request = UnityWebRequest.Get(ProgressUrl);
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
                var parsed = JsonConvert.DeserializeObject<AchievementProgressResponse>(response.Text)
                    ?? throw new InvalidOperationException(
                        "AchievementAPI: failed to deserialize achievement progress response");
                onSuccess?.Invoke(parsed);
            }
            catch (Exception exception)
            {
                onError?.Invoke($"Error parsing achievement progress: {exception.Message}");
            }
        }
    }

    public IEnumerator ReportUnlock(string achievementId, int currentCount,
        Action onSuccess, Action<string> onError)
    {
        var requestBody = new AchievementUnlockRequest
        {
            achievementId = achievementId,
            currentCount = currentCount,
        };
        string jsonData = JsonConvert.SerializeObject(requestBody);

        UnityWebRequest request = new UnityWebRequest(UnlockUrl, "POST");
        request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonData));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        var response = new ApiResponse();
        yield return request.SendWithTokenRefresh(response);

        if (response.IsSuccess)
            onSuccess?.Invoke();
        else
            onError?.Invoke(response.Error);
    }

    public IEnumerator ReportProgressUpdate(string achievementId, int currentCount,
        Action onSuccess, Action<string> onError)
    {
        var requestBody = new AchievementProgressUpdateRequest
        {
            achievementId = achievementId,
            currentCount = currentCount,
        };
        string jsonData = JsonConvert.SerializeObject(requestBody);

        UnityWebRequest request = new UnityWebRequest(ProgressUpdateUrl, "POST");
        request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonData));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        var response = new ApiResponse();
        yield return request.SendWithTokenRefresh(response);

        if (response.IsSuccess)
            onSuccess?.Invoke();
        else
            onError?.Invoke(response.Error);
    }
}
