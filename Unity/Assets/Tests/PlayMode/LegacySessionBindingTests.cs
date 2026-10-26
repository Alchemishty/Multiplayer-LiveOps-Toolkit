using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using System.Text;
using MultiplayerLiveOpsToolkit.Wallet;
using MultiplayerLiveOpsToolkit.Inventory;
using MultiplayerLiveOpsToolkit.Equipment;
using MultiplayerLiveOpsToolkit.Achievements;
using MultiplayerLiveOpsToolkit.DailyRewards;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class LegacySessionBindingTests
{
    private DevelopmentAccountReset reset;

    private string originalUrl;
    [SetUp]
    public void SetUp()
    {
        originalUrl = ApiConfig.BASE_URL;
        ApiConfig.SetBaseUrl("http://127.0.0.1:19001");
        reset = new GameObject("Account reset test").AddComponent<DevelopmentAccountReset>();
        reset.enabled = false;
    }
    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(reset.gameObject);
        ApiConfig.SetBaseUrl(originalUrl);
    }
    [UnityTest]
    public IEnumerator ResetAccount_UsesInjectedSessionAndPropagatesResponse()
    {
        var session = new SessionStub { ResponseText = "reset-result" };
        reset.Bind(session, ApiConfig.BASE_URL, new WalletStub(), new InventoryStub());
        var response = new ApiResponse();
        yield return reset.ResetBackendAccount(response);
        Assert.That(session.RequestCount, Is.EqualTo(1));
        Assert.That(session.RequestPath, Is.EqualTo("/api/auth/reset/"));
        Assert.That(session.Method, Is.EqualTo("POST"));
        Assert.That(response.IsSuccess, Is.True);
        Assert.That(response.Text, Is.EqualTo("reset-result"));
    }
    [UnityTest]
    public IEnumerator ResetAccount_WaitsForWalletAndRefreshesAfterReset()
    {
        var session = new SessionStub { ResponseText = "reset-result" };
        var wallet = new WalletStub { IsBusy = true };
        reset.Bind(session, ApiConfig.BASE_URL, wallet, new InventoryStub());
        var operation = reset.ResetBackendAccount(new ApiResponse());
        Assert.That(operation.MoveNext(), Is.True);
        Assert.That(session.RequestCount, Is.Zero);
        wallet.IsBusy = false;
        yield return operation;
        Assert.That(session.RequestCount, Is.EqualTo(1));
        Assert.That(wallet.RefreshCount, Is.EqualTo(1));
    }
    [UnityTest]
    public IEnumerator ResetAccount_WalletBecomesBusyDuringHttp_WaitsThenRefreshesAllState()
    {
        var wallet = new WalletStub();
        var inventory = new InventoryStub();
        var equipment = new EquipmentStub();
        var achievements = new AchievementStub();
        var dailyRewards = new DailyRewardStub();
        var session = new SessionStub { OnResponse = () => wallet.IsBusy = true };
        reset.Bind(session, ApiConfig.BASE_URL, wallet, inventory, equipment, achievements, dailyRewards);
        var operation = reset.ResetBackendAccount(new ApiResponse());
        Assert.That(operation.MoveNext(), Is.True);
        yield return (IEnumerator)operation.Current;
        Assert.That(operation.MoveNext(), Is.True);
        Assert.That(operation.Current, Is.Null, "Reset must wait for work started during its HTTP request.");
        Assert.That(wallet.RefreshCount, Is.Zero);
        wallet.IsBusy = false;
        yield return operation;
        Assert.That(wallet.RefreshCount, Is.EqualTo(1));
        Assert.That(inventory.RefreshCount, Is.EqualTo(1));
        Assert.That(equipment.RefreshCount, Is.EqualTo(1));
        Assert.That(achievements.RefreshCount, Is.EqualTo(1));
        Assert.That(dailyRewards.RefreshCount, Is.EqualTo(1));
    }
    [UnityTest]
    public IEnumerator ResetAccount_WaitsForInventoryAndRefreshesOwnershipAfterReset()
    {
        var session = new SessionStub { ResponseText = "reset-result" };
        var inventory = new InventoryStub { IsBusy = true };
        reset.Bind(session, ApiConfig.BASE_URL, new WalletStub(), inventory);
        var operation = reset.ResetBackendAccount(new ApiResponse());
        Assert.That(operation.MoveNext(), Is.True);
        Assert.That(operation.Current, Is.Null, "Reset must wait before dispatching HTTP");
        inventory.IsBusy = false;
        yield return operation;
        Assert.That(session.RequestCount, Is.EqualTo(1));
        Assert.That(inventory.RefreshCount, Is.EqualTo(1));
    }
    [UnityTest]
    public IEnumerator ResetAccount_WaitsForEquipmentAndRefreshesCanonicalLoadout()
    {
        var session = new SessionStub { ResponseText = "reset-result" };
        var equipment = new EquipmentStub { IsBusy = true };
        reset.Bind(session, ApiConfig.BASE_URL, new WalletStub(), new InventoryStub(), equipment);
        var operation = reset.ResetBackendAccount(new ApiResponse());
        Assert.That(operation.MoveNext(), Is.True);
        Assert.That(operation.Current, Is.Null);
        Assert.That(session.RequestCount, Is.Zero);
        equipment.IsBusy = false;
        yield return operation;
        Assert.That(equipment.RefreshCount, Is.EqualTo(1));
    }
    [UnityTest]
    public IEnumerator ResetAccount_WaitsForAchievementsAndRefreshesClaimState()
    {
        var session = new SessionStub { ResponseText = "reset-result" };
        var achievements = new AchievementStub { IsBusy = true };
        reset.Bind(session, ApiConfig.BASE_URL, new WalletStub(), new InventoryStub(), null, achievements);
        var operation = reset.ResetBackendAccount(new ApiResponse());
        Assert.That(operation.MoveNext(), Is.True);
        Assert.That(operation.Current, Is.Null);
        Assert.That(session.RequestCount, Is.Zero);
        achievements.IsBusy = false;
        yield return operation;
        Assert.That(achievements.RefreshCount, Is.EqualTo(1));
    }
    [UnityTest]
    public IEnumerator ResetAccount_WaitsForDailyRewardsAndRefreshesAttendance()
    {
        var session = new SessionStub { ResponseText = "reset-result" };
        var dailyRewards = new DailyRewardStub { IsBusy = true };
        reset.Bind(session, ApiConfig.BASE_URL, new WalletStub(), new InventoryStub(), null, null, dailyRewards);
        var operation = reset.ResetBackendAccount(new ApiResponse());
        Assert.That(operation.MoveNext(), Is.True); Assert.That(operation.Current, Is.Null); Assert.That(session.RequestCount, Is.Zero);
        dailyRewards.IsBusy = false; yield return operation;
        Assert.That(dailyRewards.RefreshCount, Is.EqualTo(1));
    }
    private sealed class DailyRewardStub : IDailyRewardService
    {
        public bool IsBusy { get; set; } public int RefreshCount;
        public bool HasSnapshot => true; public bool RequiresRefresh => false;
        public DailyRewardSnapshot Snapshot => throw new NotSupportedException();
        public event Action StateChanged { add { } remove { } }
        public IEnumerator Refresh(Action success, Action<DailyRewardError> failure) { RefreshCount++; success(); yield break; }
        public IEnumerator ClaimToday(Action success, Action<DailyRewardError> failure) => throw new NotSupportedException();
    }
    private sealed class AchievementStub : IAchievementService
    {
        public bool IsBusy { get; set; } public int RefreshCount;
        public bool HasSnapshot => true; public bool RequiresRefresh => false;
        public AchievementSnapshot Snapshot => throw new NotSupportedException();
        public event Action StateChanged { add { } remove { } }
        public event Action<string> AchievementUnlocked { add { } remove { } }
        public IEnumerator Refresh(Action success, Action<AchievementError> failure) { RefreshCount++; success(); yield break; }
        public IEnumerator Claim(string id, Action success, Action<AchievementError> failure) => throw new NotSupportedException();
    }
    private sealed class EquipmentStub : IEquipmentService
    {
        public bool IsBusy { get; set; } public int RefreshCount;
        public bool HasSnapshot => true; public bool RequiresRefresh => false; public bool IsPreviewActive => false;
        public EquipmentState Snapshot => throw new NotSupportedException(); public EquipmentLoadout Presentation => throw new NotSupportedException();
        public event Action StateChanged { add { } remove { } } public event Action PreviewChanged { add { } remove { } }
        public IEnumerator Refresh(Action success, Action<EquipmentError> failure) { RefreshCount++; success(); yield break; }
        public IEnumerator ApplyItems(IEnumerable<string> ids,Action success,Action<EquipmentError> failure)=>throw new NotSupportedException();
        public IEnumerator Unequip(IEnumerable<string> ids,Action success,Action<EquipmentError> failure)=>throw new NotSupportedException();
        public IEnumerator Clear(Action success,Action<EquipmentError> failure)=>throw new NotSupportedException();
        public void Preview(IEnumerable<string> ids)=>throw new NotSupportedException();public void ClearPreview() { }
    }
    private sealed class InventoryStub : IInventoryService
    {
        public event Action SnapshotChanged { add { } remove { } }
        public bool IsBusy { get; set; }
        public bool HasSnapshot => true;
        public InventorySnapshot Snapshot => throw new NotSupportedException();
        public int RefreshCount;
        public IEnumerator Refresh(Action onSuccess, Action<InventoryError> onFailure) { RefreshCount++; onSuccess(); yield break; }
    }
    private sealed class WalletStub : IWalletService
    {
        public event Action SnapshotChanged { add { } remove { } }
        public bool IsBusy { get; set; }
        public bool HasSnapshot => true;
        public WalletSnapshot Snapshot => throw new NotSupportedException();
        public int RefreshCount;
        public IEnumerator Refresh(Action onSuccess, Action<WalletError> onFailure) { RefreshCount++; onSuccess(); yield break; }
        public IEnumerator Convert(string offerId, long amount, Action onSuccess, Action<WalletError> onFailure) { throw new NotSupportedException(); }
    }
    private sealed class SessionStub : IAuthenticatedSession
    {
        public event Action<string> SessionEnded { add { } remove { } }
        public bool IsActive => true;
        public int SessionVersion => 0;
        public int RequestCount;
        public string RequestPath;
        public string Method;
        public string Body;
        public string ResponseText;
        public Action OnResponse;
        public IEnumerator Send(UnityWebRequest request, ApiResponse response, Action<bool> onComplete = null)
        {
            RequestCount++;
            RequestPath = new Uri(request.url).AbsolutePath;
            Method = request.method;
            Body = request.uploadHandler == null ? "" : Encoding.UTF8.GetString(request.uploadHandler.data);
            response.Result = UnityWebRequest.Result.Success;
            response.ResponseCode = 200;
            response.Text = ResponseText;
            OnResponse?.Invoke();
            onComplete?.Invoke(true);
            yield break;
        }
        public void RejectSession(int expectedVersion, string reason = null) { }
        public void Dispose() { }
    }
}
