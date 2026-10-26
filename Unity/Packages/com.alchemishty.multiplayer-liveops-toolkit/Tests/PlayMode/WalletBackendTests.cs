using MultiplayerLiveOpsToolkit.Backends.Django;
using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using System.Text;
using MultiplayerLiveOpsToolkit.Wallet;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.Networking;
using UnityEngine.TestTools;

public class WalletBackendTests
{
    private const string SNAPSHOT = "{\"balances\":[{\"currency_id\":\"gold\",\"amount\":150},{\"currency_id\":\"gears\",\"amount\":3}],\"offers\":[{\"offer_id\":\"gear-offer\",\"source_currency_id\":\"gears\",\"target_currency_id\":\"gold\",\"source_units\":1,\"target_units\":25}]}";
    [UnityTest]
    public IEnumerator Fetch_UsesExplicitOwnerAndReturnsValidatedSnapshot()
    {
        var session = new SessionStub { Body = SNAPSHOT };
        var backend = new DjangoWalletBackend("https://wallet.example.test", session);
        WalletSnapshot result = null;
        yield return backend.Fetch(snapshot => result = snapshot, error => Assert.Fail(error.Message));
        Assert.That(result.Balances["gold"].Amount, Is.EqualTo(150));
        Assert.That(session.Url, Is.EqualTo("https://wallet.example.test/api/wallet/"));
        Assert.That(session.RequestCount, Is.EqualTo(1));
    }
    [UnityTest]
    public IEnumerator Convert_SendsOnlyOfferAndAmount_WithoutReceiptRequirement()
    {
        var session = new SessionStub { Body = SNAPSHOT };
        WalletSnapshot result = null;
        yield return new DjangoWalletBackend("https://wallet.example.test", session).Convert("gear-offer", 2, snapshot => result = snapshot, error => Assert.Fail(error.Message));
        var body = JObject.Parse(session.Upload);
        Assert.That(body.Count, Is.EqualTo(2));
        Assert.That((string)body["offer_id"], Is.EqualTo("gear-offer"));
        Assert.That((long)body["amount"], Is.EqualTo(2));
        Assert.That(result.Balances["gears"].Amount, Is.EqualTo(3));
        Assert.That(session.RequestCount, Is.EqualTo(1));
    }
    [UnityTest]
    public IEnumerator Convert_InvalidResponse_ReportsUnknownOutcomeWithoutRetry()
    {
        var session = new SessionStub { Body = "{}" };
        WalletError failure = null;
        yield return new DjangoWalletBackend("https://wallet.example.test", session).Convert("gear-offer", 2, snapshot => Assert.Fail("Malformed conversion confirmed"), error => failure = error);
        Assert.That(failure.Code, Is.EqualTo(WalletErrorCode.UnknownOutcome));
        Assert.That(session.RequestCount, Is.EqualTo(1));
    }
    [UnityTest]
    public IEnumerator Convert_BusinessRejection_ReturnsTypedFailure()
    {
        var session = new SessionStub { Body = "{\"code\":\"insufficient_funds\"}", Status = 400 };
        WalletError failure = null;
        yield return new DjangoWalletBackend("https://wallet.example.test", session).Convert("gear-offer", 2, snapshot => Assert.Fail("Rejected conversion applied"), error => failure = error);
        Assert.That(failure.Code, Is.EqualTo(WalletErrorCode.InsufficientFunds));
    }
    [UnityTest]
    public IEnumerator Fetch_AccountChangesBeforeResponse_DoesNotPublishOldBalances()
    {
        var session = new SessionStub { Body = SNAPSHOT, ShouldSwitchAccount = true };
        int failures = 0;
        yield return new DjangoWalletBackend("https://wallet.example.test", session).Fetch(snapshot => Assert.Fail("Old account data published"), error => { failures++; Assert.That(error.Code, Is.EqualTo(WalletErrorCode.Cancelled)); });
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
        public string Upload;
        public bool ShouldSwitchAccount;
        public IEnumerator Send(UnityWebRequest request, ApiResponse response, Action<bool> onComplete = null)
        {
            RequestCount++; Url = request.url;
            Upload = request.uploadHandler == null ? "" : Encoding.UTF8.GetString(request.uploadHandler.data);
            yield return null;
            if (ShouldSwitchAccount) SessionVersion++;
            response.Result = Status == 200 ? UnityWebRequest.Result.Success : UnityWebRequest.Result.ProtocolError;
            response.ResponseCode = Status; response.Text = Body;
        }
        public void RejectSession(int expectedVersion, string reason = null) { }
        public void Dispose() { }
    }
}
