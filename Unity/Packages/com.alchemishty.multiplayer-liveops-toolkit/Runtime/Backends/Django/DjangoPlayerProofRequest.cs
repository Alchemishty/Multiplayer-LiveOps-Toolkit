using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MultiplayerLiveOpsToolkit.Identity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace MultiplayerLiveOpsToolkit.Backends.Django
{
    /// <summary>Requests an opaque identity assertion over the player's authenticated backend channel.</summary>
    internal sealed class DjangoPlayerProofRequest
    {
        private readonly string endpoint;
        private readonly IAuthenticatedSession session;
        private readonly int maximumAssertionLength;

        public DjangoPlayerProofRequest(string baseUrl, IAuthenticatedSession session, string route, int maximumAssertionLength)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var backendUri)
                || (backendUri.Scheme != Uri.UriSchemeHttps && !(backendUri.Scheme == Uri.UriSchemeHttp && backendUri.IsLoopback))
                || !string.IsNullOrEmpty(backendUri.UserInfo) || !string.IsNullOrEmpty(backendUri.Query) || !string.IsNullOrEmpty(backendUri.Fragment))
                throw new ArgumentException("Player proof requests require a credential-free HTTPS backend, or HTTP on loopback.", nameof(baseUrl));
            endpoint = baseUrl.TrimEnd('/') + route;
            this.maximumAssertionLength = maximumAssertionLength;
        }

        public IEnumerator RequestProof(string sessionId, string challenge, Action<string> onAssertion, Action<IdentityProofRequestFailure> onFailure)
        {
            if (onAssertion == null) throw new ArgumentNullException(nameof(onAssertion));
            if (onFailure == null) throw new ArgumentNullException(nameof(onFailure));
            if (sessionId == null || !Regex.IsMatch(sessionId, @"\A[A-Za-z0-9_.:-]{1,128}\z", RegexOptions.CultureInvariant))
                throw new ArgumentException("Player proof request requires a session identifier.", nameof(sessionId));
            if (challenge == null || !Regex.IsMatch(challenge, @"\A[A-Za-z0-9_-]{43}\z", RegexOptions.CultureInvariant))
                throw new ArgumentException("Player proof request requires a canonical 32-byte challenge.", nameof(challenge));
            byte[] decoded = Convert.FromBase64String(challenge.Replace('-', '+').Replace('_', '/') + "=");
            if (Convert.ToBase64String(decoded).TrimEnd('=').Replace('+', '-').Replace('/', '_') != challenge)
                throw new ArgumentException("Player proof request requires a canonical 32-byte challenge.", nameof(challenge));
            return SendProofRequest(sessionId, challenge, onAssertion, onFailure);
        }

        private IEnumerator SendProofRequest(string sessionId, string challenge, Action<string> onAssertion, Action<IdentityProofRequestFailure> onFailure)
        {
            bool hasCompleted = false;
            try
            {
                var tokens = session;
                int sessionVersion = tokens.SessionVersion;
                if (!tokens.IsActive)
                {
                    hasCompleted = true;
                    onFailure(IdentityProofRequestFailure.Rejected);
                    yield break;
                }
                string json = JsonConvert.SerializeObject(new ProofRequest { SessionId = sessionId, Challenge = challenge });
                using (var request = new UnityWebRequest(endpoint, "POST"))
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    request.downloadHandler = new DownloadHandlerBuffer();
                    var response = new ApiResponse();
                    yield return session.Send(request, response);
                    if (!tokens.IsActive || tokens.SessionVersion != sessionVersion)
                    {
                        hasCompleted = true;
                        onFailure(IdentityProofRequestFailure.Cancelled);
                        yield break;
                    }
                    if (!response.IsSuccess)
                    {
                        var failure = ClassifyFailure(response);
                        hasCompleted = true;
                        onFailure(failure);
                        yield break;
                    }
                    string assertion = ReadAssertion(response.Text);
                    hasCompleted = true;
                    if (assertion == null) onFailure(IdentityProofRequestFailure.InvalidResponse);
                    else onAssertion(assertion);
                }
            }
            finally
            {
                // Context-owned runners dispose pending operations during shutdown.
                if (!hasCompleted) onFailure(IdentityProofRequestFailure.Cancelled);
            }
        }

        private string ReadAssertion(string body)
        {
            if (string.IsNullOrEmpty(body) || body.Length > maximumAssertionLength * 2) return null;
            try
            {
                JObject response;
                using (var input = new StringReader(body))
                using (var reader = new JsonTextReader(input) { MaxDepth = 3, DateParseHandling = DateParseHandling.None })
                {
                    response = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (reader.Read()) return null;
                }
                if (response == null) return null;
                var assertion = response["assertion"];
                if (response.Count != 1 || assertion == null || assertion.Type != JTokenType.String) return null;
                string text = (string)assertion;
                return string.IsNullOrWhiteSpace(text) || text.Length > maximumAssertionLength ? null : text;
            }
            catch (JsonException) { return null; }
        }

        private static IdentityProofRequestFailure ClassifyFailure(ApiResponse response)
        {
            if (response.ResponseCode == 503 && !string.IsNullOrEmpty(response.Text) && response.Text.Length <= 4096)
            {
                try
                {
                    var error = JObject.Parse(response.Text);
                    if (error == null) return IdentityProofRequestFailure.Unavailable;
                    var code = error["code"];
                    if (code != null && code.Type == JTokenType.String && (string)code == "proof_configuration")
                        return IdentityProofRequestFailure.Misconfigured;
                }
                catch (JsonException) { /* Error bodies are untrusted; never log response fragments. */ }
            }
            return response.ResponseCode == 0 || response.ResponseCode >= 500 || response.ResponseCode == 429
                ? IdentityProofRequestFailure.Unavailable : IdentityProofRequestFailure.Rejected;
        }

        private sealed class ProofRequest
        {
            [JsonProperty("session_id")] public string SessionId;
            [JsonProperty("challenge")] public string Challenge;
        }
    }
}
