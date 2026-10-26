using System;
using System.Collections;
using MultiplayerLiveOpsToolkit.Backends.Django;
using MultiplayerLiveOpsToolkit.Core;
using MultiplayerLiveOpsToolkit.Identity;
using MultiplayerLiveOpsToolkit.UnityIntegration;
using NUnit.Framework;
using UnityEngine.Networking;

public class DjangoPlayerModulesTests
{
    [TestCase(DjangoModules.Shop)]
    [TestCase(DjangoModules.Equipment)]
    [TestCase((DjangoModules)128)]
    public void Constructor_InvalidSelection_RejectsBeforeSessionSideEffects(DjangoModules selection)
    {
        var session = new SessionStub();
        Assert.Throws<ArgumentException>(() => new DjangoPlayerModules("http://localhost:8000", session, session, selection, exception => Assert.Fail(exception.Message)));
        Assert.That(session.DisposeCount, Is.Zero);
    }
    [Test]
    public void Initialize_IdentityOnlyProviderFailure_CanRetryWithoutSelectingOtherModules()
    {
        var session = new SessionStub();
        var provider = new ProviderStub();
        using (var client = new DjangoPlayerModules("http://localhost:8000", session, session, DjangoModules.Identity, exception => Assert.Fail(exception.Message)))
        {
            int failures = 0;
            WalletModuleTests.Drain(client.Initialize(provider, () => Assert.Fail("No credential"), error => failures++));
            WalletModuleTests.Drain(client.Initialize(provider, () => Assert.Fail("No credential"), error => failures++));
            Assert.That(failures, Is.EqualTo(2));
            Assert.That(provider.CallCount, Is.EqualTo(2));
            Assert.That(client.Wallet, Is.Null);
            Assert.That(client.Inventory, Is.Null);
            Assert.That(client.Equipment, Is.Null);
            Assert.That(client.Context.IsAuthenticated, Is.False);
        }
        Assert.That(session.DisposeCount, Is.EqualTo(1));
    }
    [Test]
    public void Initialize_AllModules_ComposesSelectedServicesAndProofsWithoutUI()
    {
        var session = new SessionStub();
        using (var client = new DjangoPlayerModules("http://localhost:8000", session, session, DjangoModules.All, exception => Assert.Fail(exception.Message)))
        {
            WalletModuleTests.Drain(client.Initialize(new ProviderStub(), () => Assert.Fail("No credential"), error => { }));
            Assert.That(client.Wallet, Is.Not.Null);
            Assert.That(client.Inventory, Is.Not.Null);
            Assert.That(client.Shop, Is.Not.Null);
            Assert.That(client.Equipment, Is.Not.Null);
            Assert.That(client.EquipmentProofs, Is.Not.Null);
            Assert.That(client.Achievements, Is.Not.Null);
            Assert.That(client.DailyRewards, Is.Not.Null);
        }
    }
    [Test]
    public void Initialize_ConcurrentRequestRejected_PreservesAcceptedServiceReferences()
    {
        var session = new SessionStub();
        using (var client = new DjangoPlayerModules("http://localhost:8000", session, session, DjangoModules.All, exception => Assert.Fail(exception.Message)))
        {
            var pending = client.Initialize(new ProviderStub(), () => { }, error => { });
            var wallet = client.Wallet; var inventory = client.Inventory; var equipment = client.Equipment;
            Assert.Throws<InvalidOperationException>(() => client.Initialize(new ProviderStub(), () => { }, error => { }));
            Assert.That(client.Wallet, Is.SameAs(wallet));
            Assert.That(client.Inventory, Is.SameAs(inventory));
            Assert.That(client.Equipment, Is.SameAs(equipment));
            ((IDisposable)pending).Dispose();
        }
    }
    private sealed class ProviderStub : IIdentityProvider
    {
        public int CallCount;
        public IEnumerator GetCredential(Action<IdentityCredential> onCredential) { CallCount++; yield break; }
    }
    private sealed class SessionStub : IAuthenticatedSession, ITokenStore
    {
        public int DisposeCount;
        public event Action<string> SessionEnded { add { } remove { } }
        public int SessionVersion => 0;
        public bool IsActive => false;
        public IEnumerator Send(UnityWebRequest request, ApiResponse response, Action<bool> onComplete = null) => throw new NotSupportedException();
        public void RejectSession(int version, string reason = null) => throw new NotSupportedException();
        public void SetTokens(string access, string refresh, string endpoint) => throw new NotSupportedException();
        public void ClearTokens() { }
        public void Dispose() { DisposeCount++; }
    }
}
