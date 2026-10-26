using System;
using System.Collections;
using System.Linq;
using MultiplayerLiveOpsToolkit.DailyRewards;
using MultiplayerLiveOpsToolkit.Wallet;
using NUnit.Framework;

public class DailyRewardsModuleTests
{
    private WalletModule wallet;
    private DailyRewardsModule module;
    private Backend backend;
    [SetUp] public void SetUp()
    {
        wallet = new WalletModule(new WalletBackend(), exception => Assert.Fail(exception.Message)); Run(wallet.Initialize());
        backend = new Backend(); module = new DailyRewardsModule(backend, exception => Assert.Fail(exception.Message), wallet);
    }
    [TearDown] public void TearDown() { module.Shutdown(); wallet.Shutdown(); }
    [Test] public void Initialize_DeclaresOnlySelectedDependencies_AndSupportsAttendanceOnly()
    {
        var attendance = new DailyRewardsModule(new Backend { HasReward = false }, exception => Assert.Fail(exception.Message));
        try { Run(attendance.Initialize()); Assert.That(attendance.Dependencies, Is.EqualTo(new[] { "identity" })); Assert.That(attendance.Snapshot.CanClaimToday, Is.True); }
        finally { attendance.Shutdown(); }
        Run(module.Initialize()); Assert.That(module.Dependencies, Does.Contain("wallet"));
    }
    [Test] public void CurrencySchedule_RequiresExplicitWalletCapability()
    {
        var incomplete = new DailyRewardsModule(new Backend(), exception => { });
        try { Assert.Throws<InvalidOperationException>(() => Run(incomplete.Initialize())); }
        finally { incomplete.Shutdown(); }
    }
    [Test] public void Claim_StagesCalendarAndWalletBeforeObservers_RejectsLocalDuplicate()
    {
        Run(module.Initialize()); int notifications = 0;
        Action changed = () => { notifications++; Assert.That(module.Snapshot.CanClaimToday, Is.False); Assert.That(wallet.Snapshot.Balances["gold"].Amount, Is.EqualTo(150)); };
        wallet.SnapshotChanged += changed;
        try { Run(module.ClaimToday(() => { }, Fail)); }
        finally { wallet.SnapshotChanged -= changed; }
        Assert.That(notifications, Is.EqualTo(1)); DailyRewardError failure = null;
        Run(module.ClaimToday(() => Assert.Fail(), error => failure = error)); Assert.That(failure.Code, Is.EqualTo(DailyRewardErrorCode.AlreadyClaimed)); Assert.That(backend.Claims, Is.EqualTo(1));
    }
    [Test] public void UnknownClaim_InvalidatesWalletAndRequiresReadWithoutRetry()
    {
        Run(module.Initialize()); backend.LoseResponse = true; DailyRewardError failure = null;
        Run(module.ClaimToday(() => Assert.Fail(), error => failure = error)); Assert.That(failure.Code, Is.EqualTo(DailyRewardErrorCode.UnknownOutcome)); Assert.That(wallet.HasSnapshot, Is.False);
        Run(module.ClaimToday(() => Assert.Fail(), error => failure = error)); Assert.That(failure.Code, Is.EqualTo(DailyRewardErrorCode.RefreshRequired)); Assert.That(backend.Claims, Is.EqualTo(1));
        Run(module.Refresh(() => { }, Fail)); Assert.That(module.Snapshot.CanClaimToday, Is.False); Assert.That(wallet.Snapshot.Balances["gold"].Amount, Is.EqualTo(150));
    }
    [Test] public void DayChanged_RequiresRefresh_ThenUsesNewBackendDate()
    {
        Run(module.Initialize()); backend.ChangeDay = true; DailyRewardError failure = null;
        Run(module.ClaimToday(() => Assert.Fail(), error => failure = error)); Assert.That(failure.Code, Is.EqualTo(DailyRewardErrorCode.DayChanged)); Assert.That(module.RequiresRefresh, Is.True);
        Run(module.Refresh(() => { }, Fail)); Assert.That(module.Snapshot.Today, Is.EqualTo(new DateTime(2026,10,26))); Assert.That(wallet.HasSnapshot, Is.True);
    }
    [Test] public void UnstartedAndPendingCancellation_ReleasesWalletAndCompletesOnce()
    {
        Run(module.Initialize()); int callbacks = 0; var operation = module.ClaimToday(() => Assert.Fail(), error => callbacks++);
        Assert.That(wallet.IsBusy, Is.True); ((IDisposable)operation).Dispose(); Assert.That(callbacks, Is.EqualTo(1)); Assert.That(module.RequiresRefresh, Is.False);
        backend.Wait = true; operation = module.ClaimToday(() => Assert.Fail(), error => callbacks++); Assert.That(operation.MoveNext(), Is.True);
        module.Shutdown(); ((IDisposable)operation).Dispose(); Assert.That(callbacks, Is.EqualTo(2)); Assert.That(module.HasSnapshot || wallet.IsBusy, Is.False);
    }
    [Test] public void Models_RejectIncompleteCalendarInvalidAmountsAndUnconfirmedClaims()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DailyCurrencyReward("gold",0));
        Assert.Throws<ArgumentException>(() => new DailyRewardSnapshot(new DateTime(2026,10,25), Array.Empty<DailyRewardDay>()));
        Assert.Throws<ArgumentException>(() => new DailyRewardState(Calendar(false,true)));
        Assert.Throws<ArgumentException>(() => new DailyRewardState(Calendar(false,false),claimedDate:new DateTime(2026,10,25)));
    }
    public static DailyRewardSnapshot Calendar(bool claimed, bool hasReward, int day = 25) => new DailyRewardSnapshot(new DateTime(2026,10,day),Enumerable.Range(1,31).Select(number => new DailyRewardDay(number,hasReward && number == day ? new DailyCurrencyReward("gold",50) : null,number == day && claimed)));
    private static WalletSnapshot Balance(long amount) => new WalletSnapshot(new[] { new WalletBalance("gold",amount) },Array.Empty<WalletConversionOffer>());
    private static void Run(IEnumerator operation) => WalletModuleTests.Drain(operation);
    private static void Fail(DailyRewardError error) => Assert.Fail(error.Message);
    private sealed class Backend : IDailyRewardBackend
    {
        public bool HasReward = true; public bool Claimed; public bool Wait; public bool LoseResponse; public bool ChangeDay; public int Claims;
        public IEnumerator Fetch(Action<DailyRewardState> success,Action<DailyRewardError> failure) { success(new DailyRewardState(Calendar(Claimed,HasReward,ChangeDay?26:25),HasReward?Balance(Claimed?150:100):null));yield break; }
        public IEnumerator Claim(DateTime expected,Action<DailyRewardState> success,Action<DailyRewardError> failure)
        {
            Claims++; while(Wait) yield return null;
            if(ChangeDay) {failure(new DailyRewardError(DailyRewardErrorCode.DayChanged,"Refresh calendar"));yield break;}
            Claimed=true;if(LoseResponse)failure(new DailyRewardError(DailyRewardErrorCode.UnknownOutcome,"Lost"));else success(new DailyRewardState(Calendar(true,HasReward),HasReward?Balance(150):null,expected));
        }
    }
    private sealed class WalletBackend : IWalletBackend
    {
        public IEnumerator Fetch(Action<WalletSnapshot> success,Action<WalletError> failure) {success(Balance(100));yield break;}
        public IEnumerator Convert(string id,long amount,Action<WalletSnapshot> success,Action<WalletError> failure)=>throw new NotSupportedException();
    }
}
