using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using Newtonsoft.Json;

public class DailyLoginAPI : MonoBehaviour
{
    private static string CalendarUrl => ApiConfig.BASE_URL + "/api/daily-login/calendar/";
    private static string ClaimUrl => ApiConfig.BASE_URL + "/api/daily-login/claim/";

    private static DailyLoginAPI instance;
    public static DailyLoginAPI Instance
    {
        get
        {
            if (instance == null)
            {
                var gameObject = new GameObject("DailyLoginAPI");
                instance = gameObject.AddComponent<DailyLoginAPI>();
                DontDestroyOnLoad(gameObject);
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

    public IEnumerator GetCalendar(Action<DailyLoginCalendarResponse> onSuccess, Action<string> onError)
    {
        UnityWebRequest request = UnityWebRequest.Get(CalendarUrl);
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
                var parsed = JsonConvert.DeserializeObject<DailyLoginCalendarResponse>(response.Text)
                    ?? throw new InvalidOperationException(
                        $"DailyLoginAPI.GetCalendar: deserialized to null from: {response.Text}");
                onSuccess?.Invoke(parsed);
            }
            catch (Exception exception)
            {
                onError?.Invoke($"Error parsing calendar response: {exception.Message}");
            }
        }
    }

    public IEnumerator ClaimDailyReward(Action<DailyLoginClaimResponse> onSuccess, Action<string> onError)
    {
        UnityWebRequest request = new UnityWebRequest(ClaimUrl, "POST");
        request.uploadHandler = new UploadHandlerRaw(new byte[0]);
        request.downloadHandler = new DownloadHandlerBuffer();
        var response = new ApiResponse();
        yield return request.SendWithTokenRefresh(response);

        if (!response.IsSuccess)
        {
            if (response.ResponseCode == 400)
            {
                try
                {
                    var errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(response.Text);
                    onError?.Invoke(errorResponse?.message ?? "Already claimed today");
                }
                catch
                {
                    onError?.Invoke("Already claimed today");
                }
            }
            else
            {
                onError?.Invoke(response.Error ?? "Request failed");
            }
        }
        else
        {
            try
            {
                var parsed = JsonConvert.DeserializeObject<DailyLoginClaimResponse>(response.Text)
                    ?? throw new InvalidOperationException(
                        $"DailyLoginAPI.ClaimDailyReward: deserialized to null from: {response.Text}");
                onSuccess?.Invoke(parsed);
            }
            catch (Exception exception)
            {
                onError?.Invoke($"Error parsing claim response: {exception.Message}");
            }
        }
    }
}
