using MultiplayerLiveOpsToolkit.UnityIntegration;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using System;
using Newtonsoft.Json;

namespace MultiplayerLiveOpsToolkit.Backends.Django
{
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

    /// <summary>Unity session owner. Credentials stay in memory; renewal runs independently of request views.</summary>
    public sealed class DjangoPlayerSession : MonoBehaviour, ITokenStore, IAuthenticatedSession
    {
        private bool isDisposed;
        private readonly System.Collections.Generic.HashSet<UnityWebRequest> activeRequests = new System.Collections.Generic.HashSet<UnityWebRequest>();
        private readonly TokenSession session = new TokenSession();
        private RenewalAttempt renewal;
        private UnityWebRequest renewalRequest;
        private string renewalEndpoint;

        public event Action<string> SessionEnded;
        public int SessionVersion => session.Version;
        public bool IsActive => !isDisposed && !string.IsNullOrEmpty(session.AccessToken);
        internal bool NeedsRenewal => renewal != null || session.NeedsRenewal(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        private void OnEnable()
        {
            if (!isDisposed) StartCoroutine(MonitorExpiry());
        }

        private void OnDisable()
        {
            bool hasActiveSession = IsActive;
            Dispose();
            if (hasActiveSession) NotifySessionEnded("Session owner stopped. Sign in again.");
        }

        private void OnDestroy() => Dispose();

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;
            try { ClearTokens(); }
            finally { StopAllCoroutines(); }
        }

        public string GetAccessToken() => session.AccessToken;

        public void SetTokens(string accessToken, string refreshToken, string renewalEndpoint)
        {
            if (isDisposed || !isActiveAndEnabled) throw new InvalidOperationException("DjangoPlayerSession has stopped. Create an enabled session owner.");
            string endpoint = renewalEndpoint;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
                || (endpointUri.Scheme != Uri.UriSchemeHttps && !(endpointUri.Scheme == Uri.UriSchemeHttp && endpointUri.IsLoopback))
                || !string.IsNullOrEmpty(endpointUri.UserInfo) || !string.IsNullOrEmpty(endpointUri.Query)
                || !string.IsNullOrEmpty(endpointUri.Fragment))
                throw new ArgumentException("Session renewal requires a credential-free HTTPS endpoint, or HTTP on loopback.", nameof(renewalEndpoint));
            session.Begin(accessToken, refreshToken);
            this.renewalEndpoint = endpointUri.AbsoluteUri;
            CancelRenewal();
            AbortRequests();
        }

        public void ClearTokens()
        {
            session.Clear();
            renewalEndpoint = null;
            CancelRenewal();
            AbortRequests();
        }

        internal bool CanSendCredentialsTo(string requestUrl)
        {
            return !string.IsNullOrEmpty(session.AccessToken)
                && Uri.TryCreate(requestUrl, UriKind.Absolute, out var target)
                && Uri.TryCreate(renewalEndpoint, UriKind.Absolute, out var authenticatedEndpoint)
                && string.IsNullOrEmpty(target.UserInfo)
                && target.Scheme == authenticatedEndpoint.Scheme
                && string.Equals(target.Host, authenticatedEndpoint.Host, StringComparison.OrdinalIgnoreCase)
                && target.Port == authenticatedEndpoint.Port;
        }

        public void RejectSession(int expectedVersion, string reason = null)
        {
            if (session.Version != expectedVersion || string.IsNullOrEmpty(session.AccessToken)) return;
            ClearTokens();
            NotifySessionEnded(reason ?? "Your session was rejected after renewal. Sign in again.");
        }

        public IEnumerator RefreshToken(Action<bool> onComplete = null)
        {
            if (string.IsNullOrEmpty(session.RefreshToken))
            {
                onComplete?.Invoke(false);
                yield break;
            }
            bool shouldStart = renewal == null;
            var attempt = renewal ?? new RenewalAttempt(session.Version, renewalEndpoint);
            renewal = attempt;
            if (onComplete != null) attempt.Callbacks.Add(onComplete);
            if (shouldStart) StartCoroutine(RenewSession(attempt));
            while (!attempt.HasCompleted) yield return null;
        }

        private IEnumerator MonitorExpiry()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(1);
                if (session.NeedsRenewal(DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
                    yield return RefreshToken();
            }
        }

        private IEnumerator RenewSession(RenewalAttempt attempt)
        {
            bool wasSuccessful = false;
            string failureReason = "Session renewal failed. Sign in again.";
            try
            {
                // Two bounded attempts: at most 5s + 1s + 5s before terminal cleanup.
                for (int retryIndex = 0; retryIndex < 2 && !attempt.HasCompleted; retryIndex++)
                {
                    string json = JsonConvert.SerializeObject(new RefreshTokenRequest { refresh = session.RefreshToken });
                    using (var request = new UnityWebRequest(attempt.Endpoint, "POST"))
                    {
                        renewalRequest = request;
                        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                        request.downloadHandler = new DownloadHandlerBuffer();
                        request.timeout = 5;
                        request.redirectLimit = 0;
                        request.SetRequestHeader("Content-Type", "application/json");
                        yield return request.SendWebRequest();
                        if (attempt.HasCompleted || attempt.Version != session.Version) yield break;
                        if (request.result == UnityWebRequest.Result.Success)
                        {
                            TokenResponse response = null;
                            try { response = JsonConvert.DeserializeObject<TokenResponse>(request.downloadHandler.text); }
                            catch (JsonException) { /* Invalid protocol response is terminal; never log its contents. */ }
                            try
                            {
                                wasSuccessful = response != null && session.TryRenew(attempt.Version, response.access, response.refresh);
                            }
                            catch (ArgumentException) { wasSuccessful = false; }
                            if (wasSuccessful && session.NeedsRenewal(DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
                            {
                                wasSuccessful = false;
                                failureReason = "Backend returned an access token too close to expiry. Check server/client clocks.";
                            }
                            break;
                        }
                        if (request.responseCode == 401 || request.responseCode == 403)
                        {
                            failureReason = "Your session expired or was rejected. Sign in again.";
                            break;
                        }
                        if (request.responseCode != 0 && request.responseCode < 500) break;
                    }
                    renewalRequest = null;
                    if (retryIndex == 0) yield return new WaitForSecondsRealtime(1);
                }
            }
            finally
            {
                if (renewal == attempt) renewalRequest = null;
                if (!attempt.HasCompleted)
                {
                    if (attempt.Version == session.Version && !wasSuccessful)
                    {
                        session.Clear();
                        renewalEndpoint = null;
                        AbortRequests();
                    }
                    if (renewal == attempt) renewal = null;
                    if (!wasSuccessful && session.Version == attempt.Version + 1)
                        NotifySessionEnded(failureReason);
                    CompleteAttempt(attempt, wasSuccessful);
                }
            }
        }

        private void CancelRenewal()
        {
            var cancelled = renewal;
            renewal = null;
            renewalRequest?.Abort();
            renewalRequest = null;
            if (cancelled != null) CompleteAttempt(cancelled, false);
        }

        private static void CompleteAttempt(RenewalAttempt attempt, bool isSuccessful)
        {
            attempt.HasCompleted = true;
            var callbacks = attempt.Callbacks.ToArray();
            attempt.Callbacks.Clear();
            foreach (var callback in callbacks)
            {
                try { callback(isSuccessful); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        private void NotifySessionEnded(string reason)
        {
            var subscribers = SessionEnded;
            if (subscribers == null) return;
            foreach (Action<string> subscriber in subscribers.GetInvocationList())
            {
                try { subscriber(reason); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        private sealed class RenewalAttempt
        {
            public readonly int Version;
            public readonly string Endpoint;
            public readonly System.Collections.Generic.List<Action<bool>> Callbacks = new System.Collections.Generic.List<Action<bool>>();
            public bool HasCompleted;
            public RenewalAttempt(int version, string endpoint) { Version = version; Endpoint = endpoint; }
        }

        private IEnumerator SendTracked(UnityWebRequest request)
        {
            activeRequests.Add(request);
            try { yield return request.SendWebRequest(); }
            finally { activeRequests.Remove(request); }
        }

        private void AbortRequests()
        {
            var requests = new UnityWebRequest[activeRequests.Count];
            activeRequests.CopyTo(requests);
            activeRequests.Clear();
            foreach (var request in requests)
            {
                try { request.Abort(); }
                catch (ObjectDisposedException) { /* The request's caller already released it. */ }
            }
        }

        public IEnumerator Send(UnityWebRequest request, ApiResponse response, Action<bool> onComplete = null)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (response == null) throw new ArgumentNullException(nameof(response));
            var tokens = this;
            int sessionVersion = tokens.SessionVersion;
            if (!tokens.CanSendCredentialsTo(request.url))
            {
                const string ENDPOINT_MISMATCH_REASON = "Authenticated request does not target the signed-in backend. Check endpoint configuration.";
                response.Result = UnityWebRequest.Result.ConnectionError;
                response.Error = ENDPOINT_MISMATCH_REASON;
                response.Text = null;
                response.ResponseCode = 0;
                tokens.RejectSession(sessionVersion, ENDPOINT_MISMATCH_REASON);
                onComplete?.Invoke(false);
                yield break;
            }
            if (tokens.NeedsRenewal)
            {
                bool isRenewed = false;
                yield return tokens.RefreshToken(isSuccessful => isRenewed = isSuccessful);
                if (!isRenewed || tokens == null || tokens.SessionVersion != sessionVersion)
                {
                    response.Result = UnityWebRequest.Result.ConnectionError;
                    response.Error = "Session renewal did not complete for this request.";
                    response.Text = null;
                    response.ResponseCode = 0;
                    onComplete?.Invoke(false);
                    yield break;
                }
            }
            string accessToken = tokens.GetAccessToken();
            request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
            request.SetRequestHeader("Content-Type", "application/json");
            if (request.timeout == 0) request.timeout = 15;
            request.redirectLimit = 0;

            yield return SendTracked(request);

            if (tokens == null || tokens.SessionVersion != sessionVersion)
            {
                response.Result = UnityWebRequest.Result.ConnectionError;
                response.Error = "Session changed before the request completed.";
                response.Text = null;
                response.ResponseCode = 0;
                onComplete?.Invoke(false);
                yield break;
            }
            if (request.responseCode == 401)
            {
                bool hasRefreshed = !string.IsNullOrEmpty(tokens.GetAccessToken()) && accessToken != tokens.GetAccessToken();
                if (!hasRefreshed) yield return tokens.RefreshToken(isSuccessful => hasRefreshed = isSuccessful);
                if (tokens == null || tokens.SessionVersion != sessionVersion)
                {
                    response.Result = UnityWebRequest.Result.ConnectionError;
                    response.Error = "Session changed during renewal; the request was not replayed.";
                    response.Text = null;
                    response.ResponseCode = 0;
                    onComplete?.Invoke(false);
                    yield break;
                }

                if (hasRefreshed)
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
                        retryRequest.SetRequestHeader("Authorization", $"Bearer {tokens.GetAccessToken()}");
                        retryRequest.timeout = 15;
                        retryRequest.redirectLimit = 0;

                        yield return SendTracked(retryRequest);

                        if (tokens == null || tokens.SessionVersion != sessionVersion)
                        {
                            response.Result = UnityWebRequest.Result.ConnectionError;
                            response.Error = "Session changed before the retry completed.";
                            response.Text = null;
                            response.ResponseCode = 0;
                        }
                        else
                        {
                            response.CopyFrom(retryRequest);
                            if (retryRequest.responseCode == 401) tokens.RejectSession(sessionVersion);
                        }
                    }
                    onComplete?.Invoke(response.IsSuccess);
                    yield break;
                }
            }

            response.CopyFrom(request);
            onComplete?.Invoke(response.IsSuccess);
        }
    }
}
