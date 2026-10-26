using MultiplayerLiveOpsToolkit.Backends.Django;
using MultiplayerLiveOpsToolkit.UnityIntegration;
#if UNITY_EDITOR
using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TestTools;
using MultiplayerLiveOpsToolkit.Identity;
using Object = UnityEngine.Object;

public class SessionRenewalTests
{
    private HttpListener listener;
    private Task serverTask;
    private DjangoPlayerSession tokens;
    private string originalUrl;
    private int requestCount;
    private readonly System.Collections.Generic.List<string> authorizationHeaders = new System.Collections.Generic.List<string>();

    public static string Access(long expiresAt)
    {
        return "header." + Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"exp\":" + expiresAt + "}")).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".signature";
    }

    [SetUp]
    public void SetUp()
    {
        requestCount = 0;
        authorizationHeaders.Clear();
        originalUrl = ApiConfig.BASE_URL;
        tokens = new GameObject("Session under test").AddComponent<DjangoPlayerSession>();
        tokens.ClearTokens();
        tokens.SetTokens(Access(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600), "refresh", ApiConfig.BASE_URL + "/api/auth/refresh/");
    }

    [TearDown]
    public void TearDown()
    {
        if (tokens != null) { tokens.ClearTokens(); Object.DestroyImmediate(tokens.gameObject); }
        listener?.Close();
        if (serverTask != null)
        {
            try { serverTask.GetAwaiter().GetResult(); }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
        }
        ApiConfig.SetBaseUrl(originalUrl);
    }

    [UnityTest]
    public IEnumerator SetTokens_WithoutExplicitEndpoint_RejectsBeforeStoringCredentials()
    {
        tokens.ClearTokens();
        Assert.Throws<ArgumentException>(() => tokens.SetTokens(Access(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600), "refresh", null));
        Assert.That(tokens.IsActive, Is.False);
        yield break;
    }

    [UnityTest]
    public IEnumerator SessionOwner_PreservesConsumingGamePreferences()
    {
        string[] keys = { "username", "access_token", "refresh_token" };
        foreach (string key in keys) PlayerPrefs.SetString(key, "consuming-game-value");
        var owner = new GameObject("Independent consuming game session");
        try
        {
            owner.AddComponent<DjangoPlayerSession>();
            foreach (string key in keys) Assert.That(PlayerPrefs.GetString(key), Is.EqualTo("consuming-game-value"));
        }
        finally { Object.DestroyImmediate(owner); foreach (string key in keys) PlayerPrefs.DeleteKey(key); PlayerPrefs.Save(); }
        yield break;
    }

    [UnityTest]
    public IEnumerator RefreshToken_ConcurrentCallers_ShareOneRenewal()
    {
        string renewed = Access(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1200);
        StartServer("{\"access\":\"" + renewed + "\",\"refresh\":\"rotated\"}", 200, 100);
        int completions = 0;
        int successes = 0;
        Action<bool> complete = success => { completions++; if (success) successes++; };
        tokens.StartCoroutine(tokens.RefreshToken(complete));
        tokens.StartCoroutine(tokens.RefreshToken(complete));
        yield return WaitFor(() => completions == 2);
        Assert.That(successes, Is.EqualTo(2));
        Assert.That(requestCount, Is.EqualTo(1));
        Assert.That(tokens.GetAccessToken(), Is.EqualTo(renewed));
    }

    [UnityTest]
    public IEnumerator RefreshToken_MalformedResponse_ClearsPrivateSession()
    {
        StartServer("{}", 200);
        bool hasCompleted = false;
        bool wasSuccessful = true;
        yield return tokens.RefreshToken(success => { hasCompleted = true; wasSuccessful = success; });
        Assert.That(hasCompleted, Is.True);
        Assert.That(wasSuccessful, Is.False);
        Assert.That(tokens.GetAccessToken(), Is.Empty);
    }

    [UnityTest]
    public IEnumerator RefreshToken_NewLoginDuringRequest_DiscardsLateResponse()
    {
        StartServer("{\"access\":\"" + Access(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1200) + "\"}", 200, 200);
        bool hasCompleted = false;
        bool wasSuccessful = true;
        tokens.StartCoroutine(tokens.RefreshToken(success => { hasCompleted = true; wasSuccessful = success; }));
        yield return WaitFor(() => requestCount == 1);
        string nextAccount = Access(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1800);
        tokens.SetTokens(nextAccount, "next-account", ApiConfig.BASE_URL + "/api/auth/refresh/");
        yield return WaitFor(() => hasCompleted);
        Assert.That(wasSuccessful, Is.False);
        Assert.That(tokens.GetAccessToken(), Is.EqualTo(nextAccount));
    }

    [UnityTest]
    public IEnumerator MonitorExpiry_NearExpiration_RenewsWithoutGameplayRequest()
    {
        string renewed = Access(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1200);
        StartServer("{\"access\":\"" + renewed + "\"}", 200);
        tokens.SetTokens(Access(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 5), "refresh", ApiConfig.BASE_URL + "/api/auth/refresh/");
        yield return WaitFor(() => tokens.GetAccessToken() == renewed);
        Assert.That(requestCount, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator RefreshToken_RejectedSession_NotifiesOnceAfterClearingState()
    {
        StartServer("{}", 401);
        int notifications = 0;
        Action<string> onEnded = reason =>
        {
            notifications++;
            Assert.That(tokens.GetAccessToken(), Is.Empty);
        };
        tokens.SessionEnded += onEnded;
        try
        {
            yield return tokens.RefreshToken();
            Assert.That(notifications, Is.EqualTo(1));
            yield return tokens.RefreshToken();
            Assert.That(notifications, Is.EqualTo(1));
        }
        finally { tokens.SessionEnded -= onEnded; }
    }

    [UnityTest]
    public IEnumerator RefreshToken_TransientFailure_RecoversOnOneRetry()
    {
        string renewed = Access(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1200);
        StartServer("{\"access\":\"" + renewed + "\"}", 200, transientFailures: 1);
        bool wasSuccessful = false;
        yield return tokens.RefreshToken(success => wasSuccessful = success);
        Assert.That(wasSuccessful, Is.True);
        Assert.That(requestCount, Is.EqualTo(2));
        Assert.That(tokens.GetAccessToken(), Is.EqualTo(renewed));
    }

    [UnityTest]
    public IEnumerator RefreshToken_PersistentFailure_StopsAfterTwoAttempts()
    {
        StartServer("{}", 503, responses: 2);
        bool wasSuccessful = true;
        yield return tokens.RefreshToken(success => wasSuccessful = success);
        Assert.That(wasSuccessful, Is.False);
        Assert.That(requestCount, Is.EqualTo(2));
        Assert.That(tokens.GetAccessToken(), Is.Empty);
    }

    [UnityTest]
    public IEnumerator Send_RejectedRetry_EndsSessionAfterSingleReplay()
    {
        string renewed = Access(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1200);
        StartServer("{\"access\":\"" + renewed + "\"}", 200, responses: 3, gameplay: true, rejectRetry: true);
        using (var request = UnityWebRequest.Get(ApiConfig.BASE_URL + "/gameplay"))
        {
            var response = new ApiResponse();
            yield return tokens.Send(request, response);
            Assert.That(response.IsSuccess, Is.False);
            Assert.That(requestCount, Is.EqualTo(3));
            Assert.That(tokens.GetAccessToken(), Is.Empty);
        }
    }

    [UnityTest]
    public IEnumerator Send_ExpiredAccess_RenewsThenReplaysOnce()
    {
        string renewed = Access(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1200);
        StartServer("{\"access\":\"" + renewed + "\"}", 200, responses: 3, gameplay: true);
        using (var request = UnityWebRequest.Get(ApiConfig.BASE_URL + "/gameplay"))
        {
            var response = new ApiResponse();
            yield return tokens.Send(request, response);
            Assert.That(response.IsSuccess, Is.True);
            Assert.That(requestCount, Is.EqualTo(3));
            Assert.That(tokens.GetAccessToken(), Is.EqualTo(renewed));
        }
    }

    [UnityTest]
    public IEnumerator Send_NearExpiry_RenewsBeforeDispatchingGameplay()
    {
        string renewed = Access(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1200);
        StartServer("{\"access\":\"" + renewed + "\"}", 200, responses: 3, gameplay: true);
        tokens.SetTokens(Access(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 5), "refresh", ApiConfig.BASE_URL + "/api/auth/refresh/");
        using (var request = UnityWebRequest.Get(ApiConfig.BASE_URL + "/gameplay"))
        {
            var response = new ApiResponse();
            yield return tokens.Send(request, response);
            Assert.That(response.IsSuccess, Is.True);
            Assert.That(requestCount, Is.EqualTo(2));
        }
    }

    [UnityTest]
    public IEnumerator Send_AccountChangedInRenewalCallback_DoesNotReplayForNewAccount()
    {
        string renewed = Access(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1200);
        string nextAccount = Access(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1800);
        StartServer("{\"access\":\"" + renewed + "\"}", 200, 100, responses: 3, gameplay: true);
        using (var request = UnityWebRequest.Get(ApiConfig.BASE_URL + "/gameplay"))
        {
            var response = new ApiResponse();
            bool hasCompleted = false;
            tokens.StartCoroutine(tokens.Send(request, response, success => hasCompleted = true));
            yield return WaitFor(() => requestCount == 2);
            tokens.StartCoroutine(tokens.RefreshToken(success =>
            {
                if (success) tokens.SetTokens(nextAccount, "next-account", ApiConfig.BASE_URL + "/api/auth/refresh/");
            }));
            yield return WaitFor(() => hasCompleted);
            Assert.That(response.IsSuccess, Is.False);
            Assert.That(requestCount, Is.EqualTo(2));
            Assert.That(tokens.GetAccessToken(), Is.EqualTo(nextAccount));
        }
    }

    [UnityTest]
    public IEnumerator Disable_PendingRenewal_ClearsSessionAndCompletesWaiters()
    {
        StartServer("{\"access\":\"" + Access(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1200) + "\"}", 200, 200);
        int completions = 0;
        bool wasSuccessful = true;
        tokens.StartCoroutine(tokens.RefreshToken(success => { completions++; wasSuccessful = success; }));
        yield return WaitFor(() => requestCount == 1);
        tokens.gameObject.SetActive(false);
        Assert.That(completions, Is.EqualTo(1));
        Assert.That(wasSuccessful, Is.False);
        Assert.That(tokens.GetAccessToken(), Is.Empty);
    }

    [UnityTest]
    public IEnumerator RefreshToken_ConfigurationChanged_KeepsCredentialsBoundToOriginalBackend()
    {
        yield return VerifyEndpointBinding(false);
    }

    [UnityTest]
    public IEnumerator ExchangeCredential_ExplicitBackend_RenewsAtSameBackendDespiteGlobalConfiguration()
    {
        yield return VerifyEndpointBinding(true);
    }

    [UnityTest]
    public IEnumerator Send_DifferentBackend_RejectsWithoutSendingCredentials()
    {
        yield return VerifyEndpointBinding(false, true);
    }

    [UnityTest]
    public IEnumerator Send_TwoSessions_KeepCredentialsAndDisposalIndependent()
    {
        StartServer("{}", 200, responses: 2);
        string firstAccess = tokens.GetAccessToken();
        var other = new GameObject("Independent player session").AddComponent<DjangoPlayerSession>();
        string secondAccess = Access(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1200);
        other.SetTokens(secondAccess, "other-refresh", ApiConfig.BASE_URL + "/api/auth/refresh/");
        try
        {
            using (var firstRequest = UnityWebRequest.Get(ApiConfig.BASE_URL + "/first"))
            {
                var response = new ApiResponse();
                yield return tokens.Send(firstRequest, response);
                Assert.That(response.IsSuccess, Is.True);
            }
            tokens.Dispose();
            Assert.That(other.IsActive, Is.True);
            using (var secondRequest = UnityWebRequest.Get(ApiConfig.BASE_URL + "/second"))
            {
                var response = new ApiResponse();
                yield return other.Send(secondRequest, response);
                Assert.That(response.IsSuccess, Is.True);
            }
            Assert.That(authorizationHeaders, Is.EqualTo(new[] { "Bearer " + firstAccess, "Bearer " + secondAccess }));
        }
        finally { Object.DestroyImmediate(other.gameObject); }
    }

    [UnityTest]
    public IEnumerator Dispose_InFlightRequest_AbortsWithoutWaitingForBackend()
    {
        StartServer("{}", 200, delayMilliseconds: 1500);
        var caller = new GameObject("Surviving request caller").AddComponent<DjangoPlayerSession>();
        using (var request = UnityWebRequest.Get(ApiConfig.BASE_URL + "/slow"))
        {
            try
            {
                var response = new ApiResponse();
                caller.StartCoroutine(tokens.Send(request, response));
                yield return WaitFor(() => requestCount == 1);
                tokens.Dispose();
                float abortDeadline = Time.realtimeSinceStartup + 0.5f;
                while (!request.isDone && Time.realtimeSinceStartup < abortDeadline) yield return null;
                Assert.That(request.isDone, Is.True, "Disposed context must abort its pending HTTP request immediately.");
                Assert.That(tokens.IsActive, Is.False);
            }
            finally { Object.DestroyImmediate(caller.gameObject); }
        }
    }

    private IEnumerator VerifyEndpointBinding(bool usesExplicitAdapter, bool sendsGameplayToAlternative = false)
    {
        string renewed = Access(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1200);
        StartServer("{\"access\":\"" + renewed + "\",\"refresh\":\"original-refresh\",\"username\":\"player\",\"player_id\":\"42\"}", 200, responses: usesExplicitAdapter ? 2 : 1);
        string originalBackend = ApiConfig.BASE_URL;
        // Establish the session while the original backend is selected.
        tokens.SetTokens(tokens.GetAccessToken(), "original-refresh", ApiConfig.BASE_URL + "/api/auth/refresh/");
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int alternativePort = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        using (var alternative = new HttpListener())
        {
            string alternativeUrl = $"http://127.0.0.1:{alternativePort}";
            alternative.Prefixes.Add(alternativeUrl + "/");
            alternative.Start();
            int alternativeRequests = 0;
            var alternativeTask = Task.Run(async () =>
            {
                try
                {
                    var context = await alternative.GetContextAsync();
                    System.Threading.Interlocked.Increment(ref alternativeRequests);
                    byte[] bytes = Encoding.UTF8.GetBytes("{\"access\":\"" + renewed + "\"}");
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                    context.Response.Close();
                }
                catch (HttpListenerException) { }
                catch (ObjectDisposedException) { }
            });
            try
            {
                ApiConfig.SetBaseUrl(alternativeUrl);
                if (usesExplicitAdapter)
                {
                    var backend = new DjangoIdentityBackend(originalBackend, tokens);
                    PlayerIdentity player = null;
                    yield return backend.ExchangeCredential(new IdentityCredential("development", "player"), value => player = value);
                    Assert.That(player.PlayerId, Is.EqualTo("42"));
                }
                if (sendsGameplayToAlternative)
                {
                    using (var request = UnityWebRequest.Get(alternativeUrl + "/gameplay"))
                    {
                        var response = new ApiResponse();
                        yield return tokens.Send(request, response);
                        Assert.That(response.IsSuccess, Is.False);
                    }
                    Assert.That(tokens.GetAccessToken(), Is.Empty, "Endpoint mismatch is a terminal configuration error.");
                }
                else
                {
                    bool wasSuccessful = false;
                    yield return tokens.RefreshToken(isSuccessful => wasSuccessful = isSuccessful);
                    Assert.That(wasSuccessful, Is.True);
                }
                Assert.That(alternativeRequests, Is.Zero, "Session refresh credentials must not reach the reconfigured backend.");
                Assert.That(requestCount, Is.EqualTo(sendsGameplayToAlternative ? 0 : (usesExplicitAdapter ? 2 : 1)));
            }
            finally
            {
                alternative.Close();
                alternativeTask.GetAwaiter().GetResult();
            }
        }
    }

    private void StartServer(string body, int status, int delayMilliseconds = 0, int transientFailures = 0, int responses = 1, bool gameplay = false, bool rejectRetry = false)
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        string url = $"http://127.0.0.1:{port}";
        listener = new HttpListener();
        listener.Prefixes.Add(url + "/");
        listener.Start();
        ApiConfig.SetBaseUrl(url);
        tokens.SetTokens(tokens.GetAccessToken(), "refresh", url + "/api/auth/refresh/");
        serverTask = Task.Run(async () =>
        {
            for (int responseIndex = 0; responseIndex < transientFailures + responses; responseIndex++)
            {
                var context = await listener.GetContextAsync();
                authorizationHeaders.Add(context.Request.Headers["Authorization"]);
                System.Threading.Interlocked.Increment(ref requestCount);
                if (delayMilliseconds > 0) await Task.Delay(delayMilliseconds);
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                context.Response.StatusCode = gameplay && context.Request.Url.AbsolutePath == "/gameplay"
                    ? (responseIndex == 0 || rejectRetry ? 401 : 200)
                    : (responseIndex < transientFailures ? 503 : status);
                context.Response.ContentLength64 = bytes.Length;
                try { await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length); }
                catch (HttpListenerException) { }
                finally { context.Response.Close(); }
            }
        });
    }

    private static IEnumerator WaitFor(Func<bool> predicate)
    {
        float deadline = Time.realtimeSinceStartup + 3;
        while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(predicate(), Is.True, "Session operation did not complete before the test deadline.");
    }
}
#endif
