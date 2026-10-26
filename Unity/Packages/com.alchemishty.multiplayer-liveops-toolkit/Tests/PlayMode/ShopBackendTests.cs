using MultiplayerLiveOpsToolkit.Backends.Django;
using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using System.Text;
using MultiplayerLiveOpsToolkit.Shop;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.Networking;
using UnityEngine.TestTools;

public class ShopBackendTests
{
    private const string STATE = "{\"offers\":[{\"offer_id\":\"hat-offer\",\"display_name\":\"Hat\",\"category_id\":\"headwear\",\"is_set\":false,\"item_ids\":[\"hat\"],\"costs\":[{\"currency_id\":\"gold\",\"amount\":25}]}],\"wallet\":{\"balances\":[{\"currency_id\":\"gold\",\"amount\":75}],\"offers\":[]},\"inventory\":{\"items\":[{\"item_id\":\"hat\",\"display_name\":\"Hat\",\"category_id\":\"headwear\"}],\"sets\":[]}}";
    [UnityTest]
    public IEnumerator Fetch_UsesInjectedOwnerAndReturnsValidatedCatalogAndAccountState()
    {
        var session = new SessionStub { Body = STATE };
        ShopState result = null;
        yield return new DjangoShopBackend("https://shop.example.test", session).Fetch(state => result = state, error => Assert.Fail(error.Message));
        Assert.That(session.Url, Is.EqualTo("https://shop.example.test/api/shop/catalog/"));
        Assert.That(result.Catalog.Offers["hat-offer"].Costs["gold"], Is.EqualTo(25));
        Assert.That(result.Wallet.Balances["gold"].Amount, Is.EqualTo(75));
        Assert.That(result.Inventory.Items.ContainsKey("hat"), Is.True);
    }
    [UnityTest]
    public IEnumerator Purchase_SendsOnlyOfferIdAndConfirmsMatchingResponse()
    {
        var session = new SessionStub { Body = STATE.Insert(1, "\"purchased_offer_id\":\"hat-offer\",") };
        ShopState result = null;
        yield return new DjangoShopBackend("https://shop.example.test", session).Purchase("hat-offer", state => result = state, error => Assert.Fail(error.Message));
        Assert.That(JObject.Parse(session.Upload).Count, Is.EqualTo(1));
        Assert.That((string)JObject.Parse(session.Upload)["offer_id"], Is.EqualTo("hat-offer"));
        Assert.That(result.PurchasedOfferId, Is.EqualTo("hat-offer"));
        Assert.That(session.RequestCount, Is.EqualTo(1));
    }
    [UnityTest]
    public IEnumerator Purchase_MismatchedOrMalformedConfirmation_UnknownOutcomeWithoutRetry()
    {
        var session = new SessionStub { Body = STATE.Insert(1, "\"purchased_offer_id\":\"wrong-offer\",") };
        ShopError failure = null;
        yield return new DjangoShopBackend("https://shop.example.test", session).Purchase("hat-offer", state => Assert.Fail("Wrong purchase confirmed"), error => failure = error);
        Assert.That(failure.Code, Is.EqualTo(ShopErrorCode.UnknownOutcome));
        Assert.That(session.RequestCount, Is.EqualTo(1));
    }
    [UnityTest]
    public IEnumerator Purchase_BusinessRejection_ReturnsTypedFailure()
    {
        var session = new SessionStub { Body = "{\"code\":\"insufficient_funds\"}", Status = 400 };
        ShopError failure = null;
        yield return new DjangoShopBackend("https://shop.example.test", session).Purchase("hat-offer", state => Assert.Fail("Rejected purchase"), error => failure = error);
        Assert.That(failure.Code, Is.EqualTo(ShopErrorCode.InsufficientFunds));
    }
    [UnityTest]
    public IEnumerator Fetch_AccountChanged_DoesNotPublishPrivateState()
    {
        var session = new SessionStub { Body = STATE, ShouldSwitchAccount = true };
        int failures = 0;
        yield return new DjangoShopBackend("https://shop.example.test", session).Fetch(state => Assert.Fail("Old account published"), error => { failures++; Assert.That(error.Code, Is.EqualTo(ShopErrorCode.Cancelled)); });
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
