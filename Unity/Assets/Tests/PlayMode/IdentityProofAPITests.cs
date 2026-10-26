using MultiplayerLiveOpsToolkit.Backends.Django;
#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using MultiplayerLiveOpsToolkit.Identity;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class IdentityProofAPITests
{
    private HttpListener listener;
    private Task serverTask;
    private DjangoPlayerSession tokens;
    private string originalUrl;
    private string endpoint;
    private string requestBody;
    private string requestPath;
    private string authorization;
    private int requestCount;
    private string accessToken;
    private const string CHALLENGE = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8";

    [SetUp]
    public void SetUp()
    {
        requestCount = 0;
        originalUrl = ApiConfig.BASE_URL;
        accessToken = SessionRenewalTests.Access(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600);
        tokens = new GameObject("Session under test").AddComponent<DjangoPlayerSession>();
    }

    [TearDown]
    public void TearDown()
    {
        tokens.ClearTokens();
        Object.DestroyImmediate(tokens.gameObject);
        listener.Close();
        try { serverTask.GetAwaiter().GetResult(); }
        catch (HttpListenerException) { }
        catch (ObjectDisposedException) { }
        ApiConfig.SetBaseUrl(originalUrl);
    }

    [UnityTest]
    public IEnumerator RequestProof_AuthenticatedSession_SendsOnlyChallengeContextInBody()
    {
        StartServer("{\"assertion\":\"signed-assertion\"}", 200);
        string assertion = null;
        yield return new DjangoIdentityProofAPI(endpoint, tokens).RequestProof("session-1", CHALLENGE,
            value => assertion = value, failure => Assert.Fail(failure.ToString()));
        Assert.That(assertion, Is.EqualTo("signed-assertion"));
        Assert.That(requestPath, Is.EqualTo("/api/auth/identity-proof/"));
        Assert.That(authorization, Is.EqualTo("Bearer " + accessToken));
        var payload = JObject.Parse(requestBody);
        Assert.That(payload.Count, Is.EqualTo(2));
        Assert.That((string)payload["session_id"], Is.EqualTo("session-1"));
        Assert.That((string)payload["challenge"], Is.EqualTo(CHALLENGE));
    }

    [UnityTest]
    public IEnumerator RequestProof_MissingAssertion_CompletesOnceWithInvalidResponse()
    {
        StartServer("{}", 200);
        int failureCount = 0;
        yield return new DjangoIdentityProofAPI(endpoint, tokens).RequestProof("session-1", CHALLENGE,
            assertion => Assert.Fail("Unexpected assertion"), failure =>
            {
                failureCount++;
                Assert.That(failure, Is.EqualTo(IdentityProofRequestFailure.InvalidResponse));
            });
        Assert.That(failureCount, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator RequestProof_BackendConfigurationFailure_ReturnsTypedFailure()
    {
        StartServer("{\"code\":\"proof_configuration\"}", 503);
        bool hasFailed = false;
        yield return new DjangoIdentityProofAPI(endpoint, tokens).RequestProof("session-1", CHALLENGE,
            assertion => Assert.Fail("Unexpected assertion"), failure =>
            {
                hasFailed = true;
                Assert.That(failure, Is.EqualTo(IdentityProofRequestFailure.Misconfigured));
            });
        Assert.That(hasFailed, Is.True);
    }

    [UnityTest]
    public IEnumerator RequestProof_AccountSwitch_DoesNotPublishOldAssertion()
    {
        StartServer("{\"assertion\":\"old-account-assertion\"}", 200, 150);
        int failureCount = 0;
        tokens.StartCoroutine(new DjangoIdentityProofAPI(endpoint, tokens).RequestProof("session-1", CHALLENGE,
            assertion => Assert.Fail("Old identity must not escape its context"), failure =>
            {
                failureCount++;
                Assert.That(failure, Is.EqualTo(IdentityProofRequestFailure.Cancelled));
            }));
        float deadline = Time.realtimeSinceStartup + 3;
        while (requestCount == 0 && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(requestCount, Is.EqualTo(1));
        tokens.SetTokens(SessionRenewalTests.Access(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1200), "next-refresh", endpoint + "/api/auth/refresh/");
        while (failureCount == 0 && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(failureCount, Is.EqualTo(1));
    }

    private void StartServer(string responseBody, int status, int delay = 0)
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        endpoint = $"http://127.0.0.1:{port}";
        listener = new HttpListener();
        listener.Prefixes.Add(endpoint + "/");
        listener.Start();
        ApiConfig.SetBaseUrl(endpoint);
        tokens.SetTokens(accessToken, "refresh", endpoint + "/api/auth/refresh/");
        serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            System.Threading.Interlocked.Increment(ref requestCount);
            requestPath = context.Request.Url.AbsolutePath;
            authorization = context.Request.Headers["Authorization"];
            using (var reader = new StreamReader(context.Request.InputStream)) requestBody = await reader.ReadToEndAsync();
            if (delay > 0) await Task.Delay(delay);
            byte[] bytes = Encoding.UTF8.GetBytes(responseBody);
            context.Response.StatusCode = status;
            context.Response.ContentLength64 = bytes.Length;
            try { await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length); }
            catch (HttpListenerException) { }
            finally { context.Response.Close(); }
        });
    }
}
#endif
