using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using System.Collections;
using System;
using TMPro;
using Newtonsoft.Json;

public class DevAuthBootstrap : MonoBehaviour
{
    public static DevAuthBootstrap Instance { get; private set; }
    public bool IsAuthenticated { get; private set; }
    public event Action OnAuthenticated;

    [SerializeField] private GameObject loginPanel;
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private Button playButton;
    [SerializeField] private TMP_Text statusLabel;

    private string submittedUsername;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
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

        loginPanel.SetActive(true);
        playButton.interactable = false;
        usernameInput.onValueChanged.AddListener(OnUsernameChanged);
        playButton.onClick.AddListener(OnPlayClicked);

        DevAuthResponse response = null;

        while (response == null)
        {
            while (submittedUsername == null)
                yield return null;

            statusLabel.text = "Authenticating...";
            statusLabel.color = new Color(0.7f, 0.8f, 1f);
            playButton.interactable = false;
            usernameInput.interactable = false;

            string url = $"{ApiConfig.BASE_URL}/api/auth/dev/";
            var requestBody = new DevAuthRequest { username = submittedUsername };
            string json = JsonConvert.SerializeObject(requestBody);

            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        response = JsonConvert.DeserializeObject<DevAuthResponse>(request.downloadHandler.text)
                            ?? throw new InvalidOperationException("DevAuthBootstrap: auth response deserialized to null");
                    }
                    catch (Exception)
                    {
                        statusLabel.text = "Server returned unexpected response. Please retry.";
                        statusLabel.color = new Color(1f, 0.4f, 0.4f);
                        playButton.interactable = true;
                        usernameInput.interactable = true;
                        submittedUsername = null;
                    }
                }
                else
                {
                    statusLabel.text = "Connection failed. Is the backend running?";
                    statusLabel.color = new Color(1f, 0.4f, 0.4f);
                    playButton.interactable = true;
                    usernameInput.interactable = true;
                    submittedUsername = null;
                }
            }
        }
        TokenAPI.Instance.SetTokens(response.access, response.refresh);
        TokenAPI.Instance.SetUsername(submittedUsername);

        IsAuthenticated = true;
        statusLabel.text = "Connecting...";
        OnAuthenticated?.Invoke();

        while (ClientPlayerAvatars.Instance == null
               || ClientPlayerAvatars.Instance.currentClientAvatar == null)
            yield return null;

        loginPanel.SetActive(false);
    }

    private void OnUsernameChanged(string value)
    {
        playButton.interactable = !string.IsNullOrWhiteSpace(value);
    }

    private void OnPlayClicked()
    {
        string trimmed = usernameInput.text.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return;
        submittedUsername = trimmed;
    }

    [Serializable]
    private class DevAuthRequest
    {
        public string username;
    }

    [Serializable]
    private class DevAuthResponse
    {
        public string access;
        public string refresh;
        public string username;
    }
}
