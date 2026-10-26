using MultiplayerLiveOpsToolkit.Backends.Django;
using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using MultiplayerLiveOpsToolkit.Identity;
using NUnit.Framework;

public class DjangoIdentityBackendTests
{
    private sealed class TokenStore : ITokenStore
    {
        public int ClearCount;
        public void SetTokens(string accessToken, string refreshToken, string renewalEndpoint) { }
        public void ClearTokens() { ClearCount++; }
    }

    [TestCase("http://example.com")]
    [TestCase("ftp://example.com")]
    [TestCase("https://user:password@example.com")]
    [TestCase("https://example.com?secret=value")]
    [TestCase("https://example.com#fragment")]
    public void Constructor_UnsafeEndpoint_RejectsBeforeCredentialDispatch(string endpoint)
    {
        Assert.Throws<ArgumentException>(() => new DjangoIdentityBackend(endpoint, new TokenStore()));
    }

    [Test]
    public void ExchangeCredential_InvalidBinding_RejectsBeforeDispatch()
    {
        var backend = new DjangoIdentityBackend("https://example.com", new TokenStore());
        Assert.Throws<ArgumentNullException>(() => backend.ExchangeCredential(null, player => { }));
        Assert.Throws<ArgumentNullException>(() => backend.ExchangeCredential(new IdentityCredential("development", "player"), null));
        Assert.Throws<InvalidOperationException>(() => backend.ExchangeCredential(new IdentityCredential("unknown", "credential"), player => { }));
    }

    [Test]
    public void ClearSession_RepeatedShutdown_ClearsStorageOnceAndRejectsReuse()
    {
        var store = new TokenStore();
        var backend = new DjangoIdentityBackend("http://127.0.0.1:8000", store);
        backend.ClearSession();
        backend.ClearSession();
        Assert.That(store.ClearCount, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => backend.ExchangeCredential(new IdentityCredential("development", "player"), player => { }));
    }
    [Test]
    public void ProofRequest_InvalidArguments_RejectsBeforeDispatch()
    {
        var api = new DjangoIdentityProofAPI("https://example.com", new SessionStub());
        string nonce = IdentityProofTestVector.NONCE;
        Assert.Throws<ArgumentNullException>(() => api.RequestProof("session", nonce, null, failure => { }));
        Assert.Throws<ArgumentNullException>(() => api.RequestProof("session", nonce, assertion => { }, null));
        Assert.Throws<ArgumentException>(() => api.RequestProof("with spaces", nonce, assertion => { }, failure => { }));
        Assert.Throws<ArgumentException>(() => api.RequestProof("session", "invalid", assertion => { }, failure => { }));
        Assert.Throws<ArgumentException>(() => new DjangoIdentityProofAPI("http://example.com", new SessionStub()));
    }
    private sealed class SessionStub : IAuthenticatedSession
    {
        public event Action<string> SessionEnded { add { } remove { } }
        public int SessionVersion => 0;
        public bool IsActive => false;
        public System.Collections.IEnumerator Send(UnityEngine.Networking.UnityWebRequest request, ApiResponse response, Action<bool> onComplete = null) { yield break; }
        public void RejectSession(int expectedVersion, string reason = null) { }
        public void Dispose() { }
    }

}
