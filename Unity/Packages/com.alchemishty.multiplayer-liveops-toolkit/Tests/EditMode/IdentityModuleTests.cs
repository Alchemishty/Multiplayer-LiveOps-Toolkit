using System;
using System.Collections;
using MultiplayerLiveOpsToolkit.Core;
using MultiplayerLiveOpsToolkit.Identity;
using NUnit.Framework;

public class IdentityModuleTests
{
    private sealed class Provider : IIdentityProvider
    {
        public Action<IdentityCredential> Callback;
        public bool ShouldReturnCredential = true;
        public bool ShouldReturnNull;
        public bool ShouldWait;
        public int CallCount;
        public IEnumerator GetCredential(Action<IdentityCredential> onCredential)
        {
            CallCount++;
            Callback = onCredential;
            if (ShouldWait) yield return null;
            if (ShouldReturnCredential) onCredential(ShouldReturnNull ? null : new IdentityCredential("test", "secret"));
        }
    }

    private sealed class Backend : IIdentityBackend
    {
        public int ExchangeCount;
        public int ClearCount;
        public bool ShouldFail;
        public bool ShouldReturnPlayer = true;
        public bool ShouldWait;
        public IdentityCredential Credential;
        public Action<PlayerIdentity> Callback;
        public IEnumerator ExchangeCredential(IdentityCredential credential, Action<PlayerIdentity> onAuthenticated)
        {
            ExchangeCount++;
            Credential = credential;
            Callback = onAuthenticated;
            if (ShouldWait) yield return null;
            if (ShouldFail) throw new InvalidOperationException("Authentication rejected.");
            if (ShouldReturnPlayer) onAuthenticated(new PlayerIdentity("player-42", "Backend name"));
        }
        public void ClearSession() { ClearCount++; }
    }

    private static void Drain(IEnumerator routine)
    {
        while (routine.MoveNext()) { }
    }

    [Test]
    public void Initialize_VerifiedBackendIdentity_BecomesReady()
    {
        var provider = new Provider();
        var backend = new Backend();
        var identity = new IdentityModule(provider, backend);
        var runtime = new LiveOpsRuntime(new[] { identity });
        bool isReady = false;
        Drain(runtime.Initialize(() => isReady = true, error => Assert.Fail(error.Message)));
        Assert.That(isReady, Is.True);
        Assert.That(identity.CurrentPlayer.PlayerId, Is.EqualTo("player-42"));
        Assert.That(identity.CurrentPlayer.DisplayName, Is.EqualTo("Backend name"));
        Assert.That(backend.Credential.ProviderId, Is.EqualTo("test"));
        Assert.That(provider.CallCount, Is.EqualTo(1));
        runtime.Shutdown();
        Assert.That(backend.ClearCount, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => { var player = identity.CurrentPlayer; });
        runtime.Shutdown();
        Assert.That(backend.ClearCount, Is.EqualTo(1));
    }

    [Test]
    public void CurrentPlayer_BeforeAuthentication_ThrowsDiagnostic()
    {
        var identity = new IdentityModule(new Provider(), new Backend());
        Assert.Throws<InvalidOperationException>(() => { var player = identity.CurrentPlayer; });
    }

    [Test]
    public void Constructor_MissingBindings_ThrowsBeforeSideEffects()
    {
        Assert.Throws<ArgumentNullException>(() => new IdentityModule(null, new Backend()));
        Assert.Throws<ArgumentNullException>(() => new IdentityModule(new Provider(), null));
    }

    [TestCase(false, false)]
    [TestCase(true, true)]
    public void Initialize_InvalidProviderCompletion_RollsBackWithoutBackendCall(bool shouldReturnCredential, bool shouldReturnNull)
    {
        var provider = new Provider { ShouldReturnCredential = shouldReturnCredential, ShouldReturnNull = shouldReturnNull };
        var backend = new Backend();
        var identity = new IdentityModule(provider, backend);
        var runtime = new LiveOpsRuntime(new[] { identity });
        LiveOpsError failure = null;
        Drain(runtime.Initialize(() => Assert.Fail("Unexpected ready"), error => failure = error));
        Assert.That(failure.ModuleId, Is.EqualTo("identity"));
        Assert.That(backend.ExchangeCount, Is.Zero);
        Assert.That(runtime.State, Is.EqualTo(LiveOpsRuntimeState.Stopped));
    }

    [TestCase(true, true)]
    [TestCase(false, false)]
    public void Initialize_BackendFailureOrMissingCompletion_ClearsSession(bool shouldFail, bool shouldReturnPlayer)
    {
        var backend = new Backend { ShouldFail = shouldFail, ShouldReturnPlayer = shouldReturnPlayer };
        var identity = new IdentityModule(new Provider(), backend);
        var runtime = new LiveOpsRuntime(new[] { identity });
        LiveOpsError failure = null;
        Drain(runtime.Initialize(() => Assert.Fail("Unexpected ready"), error => failure = error));
        Assert.That(failure.Code, Is.EqualTo(LiveOpsErrorCode.ModuleInitializationFailed));
        Assert.That(backend.ClearCount, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => { var player = identity.CurrentPlayer; });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Shutdown_PendingAuthentication_IgnoresLateCallbacks(bool isBackendPending)
    {
        var provider = new Provider { ShouldWait = !isBackendPending };
        var backend = new Backend { ShouldWait = isBackendPending };
        var identity = new IdentityModule(provider, backend);
        var runtime = new LiveOpsRuntime(new[] { identity });
        int failureCount = 0;
        var routine = runtime.Initialize(() => Assert.Fail("Unexpected ready"), error => failureCount++);
        Assert.That(routine.MoveNext(), Is.True);
        runtime.Shutdown();
        if (isBackendPending) backend.Callback(new PlayerIdentity("late", "Late"));
        else provider.Callback(new IdentityCredential("late", "late"));
        Assert.That(routine.MoveNext(), Is.False);
        Assert.That(failureCount, Is.EqualTo(1));
        Assert.That(backend.ClearCount, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => { var player = identity.CurrentPlayer; });
    }

    [TestCase(null, "value")]
    [TestCase(" ", "value")]
    [TestCase("provider", "")]
    public void Credential_InvalidFields_RejectsConfiguration(string providerId, string value)
    {
        Assert.Throws<ArgumentException>(() => new IdentityCredential(providerId, value));
    }

    [TestCase(null, "Name")]
    [TestCase("42", " ")]
    public void PlayerIdentity_InvalidFields_RejectsBackendData(string playerId, string displayName)
    {
        Assert.Throws<ArgumentException>(() => new PlayerIdentity(playerId, displayName));
    }
}
