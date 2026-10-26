using System;
using System.Collections;
using System.Text;
using MultiplayerLiveOpsToolkit.Identity;
using Newtonsoft.Json;
using UnityEngine.Networking;

namespace MultiplayerLiveOpsToolkit.Backends.Django
{
    /// <summary>Exchanges Django password or development credentials for a private session.</summary>
    public sealed class DjangoIdentityBackend : IIdentityBackend
    {
        private readonly string baseUrl;
        private readonly ITokenStore tokenStore;
        private bool isStopped;

        public DjangoIdentityBackend(string baseUrl, ITokenStore tokenStore)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var backendUri)
                || (backendUri.Scheme != Uri.UriSchemeHttp && backendUri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("Django identity backend requires an absolute HTTP(S) URL.", nameof(baseUrl));
            if (backendUri.Scheme != Uri.UriSchemeHttps && !backendUri.IsLoopback)
                throw new ArgumentException("Django credentials require HTTPS except on loopback development servers.", nameof(baseUrl));
            if (!string.IsNullOrEmpty(backendUri.UserInfo) || !string.IsNullOrEmpty(backendUri.Query) || !string.IsNullOrEmpty(backendUri.Fragment))
                throw new ArgumentException("Django backend URL must not include credentials, a query, or a fragment.", nameof(baseUrl));
            this.baseUrl = baseUrl.TrimEnd('/');
            this.tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        }

        public IEnumerator ExchangeCredential(IdentityCredential credential, Action<PlayerIdentity> onAuthenticated)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            if (onAuthenticated == null) throw new ArgumentNullException(nameof(onAuthenticated));
            if (credential.ProviderId != "development" && credential.ProviderId != "django-password")
                throw new InvalidOperationException("Django identity backend does not support this credential provider.");
            if (isStopped) throw new InvalidOperationException("Django identity session has stopped. Create a new adapter.");
            return SendCredential(credential, onAuthenticated);
        }

        public void ClearSession()
        {
            if (isStopped) return;
            isStopped = true;
            tokenStore.ClearTokens();
        }

        private IEnumerator SendCredential(IdentityCredential credential, Action<PlayerIdentity> onAuthenticated)
        {
            if (isStopped) yield break;
            bool isDevelopmentCredential = credential.ProviderId == "development";
            string endpoint = baseUrl + (isDevelopmentCredential ? "/api/auth/dev/" : "/api/auth/password/");
            string requestJson = isDevelopmentCredential
                ? JsonConvert.SerializeObject(new DevelopmentAuthRequest { Username = credential.Value })
                : credential.Value;
            using (var request = new UnityWebRequest(endpoint, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestJson));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 15;
                request.redirectLimit = 0;
                request.SetRequestHeader("Content-Type", "application/json");
                yield return request.SendWebRequest();
                if (isStopped) yield break;
                if (request.responseCode == 401)
                    throw new InvalidOperationException("Invalid username or password.");
                if (request.responseCode == 429)
                    throw new InvalidOperationException("Too many login attempts. Please wait before trying again.");
                if (request.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException($"Django authentication failed (HTTP {request.responseCode}). Check backend availability and configuration.");

                IdentityResponse response;
                try { response = JsonConvert.DeserializeObject<IdentityResponse>(request.downloadHandler.text); }
                catch (JsonException)
                {
                    // Parsing exceptions can contain response fragments, including credentials.
                    throw new InvalidOperationException("Django authentication returned malformed JSON.");
                }
                if (response == null) throw new InvalidOperationException("Django authentication response was null.");
                var player = new PlayerIdentity(response.PlayerId, response.Username);
                if (string.IsNullOrWhiteSpace(response.Access) || string.IsNullOrWhiteSpace(response.Refresh))
                    throw new InvalidOperationException("Django authentication response requires access and refresh tokens.");
                tokenStore.SetTokens(response.Access, response.Refresh, baseUrl + "/api/auth/refresh/");
                onAuthenticated(player);
            }
        }

        private sealed class DevelopmentAuthRequest
        {
            [JsonProperty("username")] public string Username;
        }

        private sealed class IdentityResponse
        {
            [JsonProperty("access")] public string Access;
            [JsonProperty("refresh")] public string Refresh;
            [JsonProperty("username")] public string Username;
            [JsonProperty("player_id")] public string PlayerId;
        }
    }
}
