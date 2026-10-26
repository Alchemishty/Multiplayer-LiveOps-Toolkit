using MultiplayerLiveOpsToolkit.Backends.Django;
using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using MultiplayerLiveOpsToolkit.Inventory;
using NUnit.Framework;
using UnityEngine.Networking;
using UnityEngine.TestTools;

public class InventoryBackendTests
{
    private const string SNAPSHOT = "{\"items\":[{\"item_id\":\"hat\",\"display_name\":\"Hat\",\"category_id\":\"headwear\"}],\"sets\":[{\"set_id\":\"outfit\",\"display_name\":\"Outfit\",\"item_ids\":[\"hat\"]}]}";
    [UnityTest]
    public IEnumerator Fetch_UsesInjectedSessionAndIndependentEndpoint()
    {
        var session = new SessionStub { Body = SNAPSHOT };
        InventorySnapshot result = null;
        yield return new DjangoInventoryBackend("https://inventory.example.test", session).Fetch(snapshot => result = snapshot, error => Assert.Fail(error.Message));
        Assert.That(session.Url, Is.EqualTo("https://inventory.example.test/api/inventory/"));
        Assert.That(session.RequestCount, Is.EqualTo(1));
        Assert.That(result.Items["hat"].DisplayName, Is.EqualTo("Hat"));
        Assert.That(result.Sets["outfit"].ItemIds[0], Is.EqualTo("hat"));
    }
    [UnityTest]
    public IEnumerator Fetch_AccountReplaced_DoesNotPublishOldOwnership()
    {
        var session = new SessionStub { Body = SNAPSHOT, ShouldSwitchAccount = true };
        int failures = 0;
        yield return new DjangoInventoryBackend("https://inventory.example.test", session).Fetch(snapshot => Assert.Fail("Old ownership published"), error => { failures++; Assert.That(error.Code, Is.EqualTo(InventoryErrorCode.Cancelled)); });
        Assert.That(failures, Is.EqualTo(1));
    }
    [UnityTest]
    public IEnumerator Fetch_UnownedSetMember_ReturnsInvalidResponseWithoutRetry()
    {
        var session = new SessionStub { Body = SNAPSHOT.Replace("[\"hat\"]", "[\"missing\"]") };
        InventoryError failure = null;
        yield return new DjangoInventoryBackend("https://inventory.example.test", session).Fetch(snapshot => Assert.Fail("Invalid set published"), error => failure = error);
        Assert.That(failure.Code, Is.EqualTo(InventoryErrorCode.InvalidResponse));
        Assert.That(session.RequestCount, Is.EqualTo(1));
    }
    [UnityTest]
    public IEnumerator Fetch_MissingIdentity_ReturnsConfigurationError()
    {
        var session = new SessionStub { Body = "{\"code\":\"identity_not_initialized\"}", Status = 409 };
        InventoryError failure = null;
        yield return new DjangoInventoryBackend("https://inventory.example.test", session).Fetch(snapshot => Assert.Fail("Invalid identity published"), error => failure = error);
        Assert.That(failure.Code, Is.EqualTo(InventoryErrorCode.Misconfigured));
    }
    [UnityTest]
    public IEnumerator Fetch_DisposedRequest_CompletesCancellationOnce()
    {
        var session = new SessionStub { Body = SNAPSHOT };
        int failures = 0;
        var operation = new DjangoInventoryBackend("https://inventory.example.test", session).Fetch(snapshot => Assert.Fail("Disposed operation completed"), error => failures++);
        Assert.That(operation.MoveNext(), Is.True);
        ((IDisposable)operation).Dispose();
        yield return null;
        Assert.That(failures, Is.EqualTo(1));
    }
    private sealed class SessionStub : IAuthenticatedSession
    {
        public event Action<string> SessionEnded { add { } remove { } }
        public bool IsActive => true;
        public int SessionVersion { get; private set; }
        public int RequestCount;
        public int Status = 200;
        public string Body;
        public string Url;
        public bool ShouldSwitchAccount;
        public IEnumerator Send(UnityWebRequest request, ApiResponse response, Action<bool> onComplete = null)
        {
            RequestCount++; Url = request.url;
            yield return null;
            if (ShouldSwitchAccount) SessionVersion++;
            response.Result = Status == 200 ? UnityWebRequest.Result.Success : UnityWebRequest.Result.ProtocolError;
            response.ResponseCode = Status; response.Text = Body;
        }
        public void RejectSession(int expectedVersion, string reason = null) { }
        public void Dispose() { }
    }
}
