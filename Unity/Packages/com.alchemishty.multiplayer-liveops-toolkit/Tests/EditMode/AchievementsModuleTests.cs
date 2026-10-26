using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Achievements;
using MultiplayerLiveOpsToolkit.Wallet;
using MultiplayerLiveOpsToolkit.Inventory;
using NUnit.Framework;

public class AchievementsModuleTests
{
    private AchievementsModule achievements;
    private WalletModule wallet;
    private InventoryModule inventory;
    private Backend backend;
    private int diagnosticCount;
    [SetUp] public void SetUp()
    {
        wallet = new WalletModule(new WalletBackend(), exception => Assert.Fail(exception.Message));
        inventory = new InventoryModule(new InventoryBackend(), exception => Assert.Fail(exception.Message));
        Run(wallet.Initialize()); Run(inventory.Initialize());
        backend = new Backend(); diagnosticCount = 0;
        achievements = new AchievementsModule(backend, exception => diagnosticCount++, wallet, inventory);
        Run(achievements.Initialize());
    }
    [TearDown] public void TearDown() { achievements.Shutdown(); inventory.Shutdown(); wallet.Shutdown(); }
    private void Unlock() { backend.Unlocked = true; Run(achievements.Refresh(() => { }, Fail)); }
    [Test] public void IdentityOnly_NoRewardDefinitions_WorkWithoutEconomyOrDemo()
    {
        var module = new AchievementsModule(new Backend { Kind = AchievementRewardKind.None }, exception => Assert.Fail(exception.Message));
        try { Run(module.Initialize()); Assert.That(module.Snapshot.Entries["first"].Reward.Kind, Is.EqualTo(AchievementRewardKind.None)); }
        finally { module.Shutdown(); }
    }
    [Test] public void Refresh_NotifiesOnlyNewBackendConfirmedCompletion()
    {
        var ids = new List<string>(); achievements.AchievementUnlocked += ids.Add;
        Unlock(); Run(achievements.Refresh(() => { }, Fail));
        Assert.That(ids, Is.EqualTo(new[] { "first" })); Assert.That(achievements.Snapshot.Entries["first"].CanClaim, Is.True);
        achievements.AchievementUnlocked -= ids.Add;
    }
    [Test] public void Claim_ObserversSeeAchievementAndAccountSnapshotsTogether()
    {
        Unlock(); bool observed = false;
        Action changed = () => { observed = true; Assert.That(achievements.Snapshot.Entries["first"].IsClaimed, Is.True); Assert.That(wallet.Snapshot.Balances["gold"].Amount, Is.EqualTo(110)); };
        wallet.SnapshotChanged += changed;
        try { Run(achievements.Claim("first", () => { }, Fail)); Assert.That(observed, Is.True); Assert.That(backend.Claims, Is.EqualTo(1)); }
        finally { wallet.SnapshotChanged -= changed; }
    }
    [Test] public void Claim_LockedAndAlreadyClaimed_AreRejectedLocally()
    {
        AchievementError error = null;
        Run(achievements.Claim("first", () => Assert.Fail(), value => error = value)); Assert.That(error.Code, Is.EqualTo(AchievementErrorCode.Rejected)); Assert.That(backend.Claims, Is.Zero);
        Unlock(); Run(achievements.Claim("first", () => { }, Fail));
        Run(achievements.Claim("first", () => Assert.Fail(), value => error = value)); Assert.That(error.Code, Is.EqualTo(AchievementErrorCode.AlreadyClaimed)); Assert.That(backend.Claims, Is.EqualTo(1));
    }
    [Test] public void LostClaim_RequiresRefreshWithoutRetryAndPreservesUnaffectedInventory()
    {
        Unlock(); backend.LoseResponse = true; AchievementError error = null;
        Run(achievements.Claim("first", () => Assert.Fail(), value => error = value));
        Assert.That(error.Code, Is.EqualTo(AchievementErrorCode.UnknownOutcome)); Assert.That(wallet.HasSnapshot, Is.False); Assert.That(inventory.HasSnapshot, Is.True);
        Run(achievements.Claim("first", () => Assert.Fail(), value => error = value)); Assert.That(error.Code, Is.EqualTo(AchievementErrorCode.RefreshRequired)); Assert.That(backend.Claims, Is.EqualTo(1));
        backend.LoseResponse = false; Run(achievements.Refresh(() => { }, Fail)); Assert.That(wallet.Snapshot.Balances["gold"].Amount, Is.EqualTo(110)); Assert.That(achievements.Snapshot.Entries["first"].IsClaimed, Is.True);
    }
    [Test] public void Claim_ReservesAccountStateAndDisposesUnstartedOrPendingWorkOnce()
    {
        Unlock(); int callbacks = 0;
        var operation = achievements.Claim("first", () => Assert.Fail(), error => callbacks++);
        WalletError walletError = null; Run(wallet.Refresh(() => Assert.Fail(), error => walletError = error)); Assert.That(walletError.Code, Is.EqualTo(WalletErrorCode.Busy));
        ((IDisposable)operation).Dispose(); Assert.That(callbacks, Is.EqualTo(1)); Assert.That(wallet.IsBusy || achievements.IsBusy, Is.False);
        backend.Wait = true; operation = achievements.Claim("first", () => Assert.Fail(), error => callbacks++); Assert.That(operation.MoveNext(), Is.True);
        achievements.Shutdown(); ((IDisposable)operation).Dispose(); Assert.That(callbacks, Is.EqualTo(2)); Assert.That(achievements.HasSnapshot, Is.False);
    }
    [Test] public void ConfiguredRewardsRequireExplicitSelectedCapabilities()
    {
        var module = new AchievementsModule(new Backend(), exception => { });
        try { Assert.Throws<InvalidOperationException>(() => Run(module.Initialize())); Assert.That(module.HasSnapshot, Is.False); }
        finally { module.Shutdown(); }
    }
    [Test] public void CompletionSubscriberFailure_DoesNotBlockOtherObservers()
    {
        int notified = 0; Action<string> broken = id => throw new InvalidOperationException("observer"); Action<string> healthy = id => notified++;
        achievements.AchievementUnlocked += broken; achievements.AchievementUnlocked += healthy;
        try { Unlock(); Assert.That(notified, Is.EqualTo(1)); Assert.That(diagnosticCount, Is.EqualTo(1)); }
        finally { achievements.AchievementUnlocked -= broken; achievements.AchievementUnlocked -= healthy; }
    }
    [Test] public void Claim_BusinessRejection_KeepsConfirmedWallet()
    {
        Unlock(); backend.Reject = true; AchievementError error = null;
        Run(achievements.Claim("first", () => Assert.Fail(), value => error = value)); Assert.That(error.Code, Is.EqualTo(AchievementErrorCode.Rejected)); Assert.That(wallet.Snapshot.Balances["gold"].Amount, Is.EqualTo(100)); Assert.That(achievements.RequiresRefresh, Is.False);
    }
    [Test] public void Models_RejectInvalidRewardsCountsAndUnconfirmedClaims()
    {
        Assert.Throws<ArgumentException>(() => new AchievementReward(AchievementRewardKind.Currency, "gold", amount: -1));
        Assert.Throws<ArgumentException>(() => new AchievementEntry("first", "First", "", 0, 1, false, true, 0, new AchievementReward(AchievementRewardKind.None)));
        Assert.Throws<ArgumentException>(() => new AchievementState(new AchievementSnapshot(new[] { Backend.Entry(false,false,AchievementRewardKind.Currency) })));
    }
    private static void Run(IEnumerator operation) => WalletModuleTests.Drain(operation);
    private static void Fail(AchievementError error) => Assert.Fail(error.Message);
    private sealed class Backend : IAchievementBackend
    {
        public bool Unlocked; public bool Claimed; public bool Wait; public bool LoseResponse; public bool Reject; public int Claims; public AchievementRewardKind Kind = AchievementRewardKind.Currency;
        public static AchievementEntry Entry(bool unlocked, bool claimed, AchievementRewardKind kind) => new AchievementEntry("first", "First", "A backend goal", unlocked ? 1 : 0, 1, unlocked, claimed, unlocked ? 1900000000000 : 0, new AchievementReward(kind, kind == AchievementRewardKind.Currency ? "gold" : null, amount: kind == AchievementRewardKind.Currency ? 10 : 0));
        private AchievementState State(string claimedId = null) => new AchievementState(new AchievementSnapshot(new[] { Entry(Unlocked, Claimed, Kind) }), Kind == AchievementRewardKind.Currency ? Balance(Claimed ? 110 : 100) : null, null, claimedId);
        public IEnumerator Fetch(Action<AchievementState> success, Action<AchievementError> failure) { success(State()); yield break; }
        public IEnumerator Claim(string id, Action<AchievementState> success, Action<AchievementError> failure)
        {
            Claims++; while (Wait) yield return null;
            if (Reject) { failure(new AchievementError(AchievementErrorCode.Rejected, "Rejected")); yield break; }
            Claimed = true;
            if (LoseResponse) failure(new AchievementError(AchievementErrorCode.UnknownOutcome, "Lost response")); else success(State(id));
        }
    }
    private static WalletSnapshot Balance(long amount) => new WalletSnapshot(new[] { new WalletBalance("gold", amount) }, Array.Empty<WalletConversionOffer>());
    private sealed class WalletBackend : IWalletBackend
    {
        public IEnumerator Fetch(Action<WalletSnapshot> success, Action<WalletError> failure) { success(Balance(100)); yield break; }
        public IEnumerator Convert(string id,long amount,Action<WalletSnapshot> success,Action<WalletError> failure) => throw new NotSupportedException();
    }
    private sealed class InventoryBackend : IInventoryBackend
    {
        public IEnumerator Fetch(Action<InventorySnapshot> success, Action<InventoryError> failure) { success(new InventorySnapshot(Array.Empty<CosmeticItem>(),Array.Empty<CosmeticSet>())); yield break; }
    }
}
