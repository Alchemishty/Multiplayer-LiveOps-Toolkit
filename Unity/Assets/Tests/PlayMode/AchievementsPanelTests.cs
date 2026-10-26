using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using MultiplayerLiveOpsToolkit.Achievements;
using MultiplayerLiveOpsToolkit.Wallet;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Object = UnityEngine.Object;

public class AchievementsPanelTests
{
    private GameObject canvasRoot;
    private GameObject panelRoot;
    private Transform content;
    private AchievementsModule module;
    private WalletModule wallet;
    private Backend backend;
    private AchievementSampleController controller;
    [UnitySetUp] public IEnumerator SetUp()
    {
        wallet = new WalletModule(new WalletBackend(), exception => Assert.Fail(exception.Message)); yield return wallet.Initialize();
        backend = new Backend(); module = new AchievementsModule(backend, exception => Assert.Fail(exception.Message), wallet); yield return module.Initialize();
        canvasRoot = new GameObject("Achievement sample without avatar or Netcode", typeof(Canvas));
        panelRoot = new GameObject("Achievements", typeof(RectTransform)); panelRoot.transform.SetParent(canvasRoot.transform); panelRoot.SetActive(false);
        content = new GameObject("Content", typeof(RectTransform)).transform; content.SetParent(panelRoot.transform);
        var status = new GameObject("Status", typeof(RectTransform)).AddComponent<TextMeshProUGUI>(); status.transform.SetParent(panelRoot.transform);
        var panel = panelRoot.AddComponent<AchievementsPanel>();
        var toast = new GameObject("Achievement toast", typeof(RectTransform)).AddComponent<AchievementToast>(); toast.transform.SetParent(canvasRoot.transform);
        var owner = new GameObject("Durable achievement owner"); owner.transform.SetParent(canvasRoot.transform); owner.SetActive(false); controller = owner.AddComponent<AchievementSampleController>();
#if UNITY_EDITOR
        var fields = new SerializedObject(panel); fields.FindProperty("contentContainer").objectReferenceValue = content; fields.FindProperty("statusText").objectReferenceValue = status; fields.ApplyModifiedPropertiesWithoutUndo();
        var ownerFields = new SerializedObject(controller); ownerFields.FindProperty("panel").objectReferenceValue = panel; ownerFields.FindProperty("toast").objectReferenceValue = toast; ownerFields.ApplyModifiedPropertiesWithoutUndo();
#endif
        controller.Bind(module, new Session()); owner.SetActive(true); panelRoot.SetActive(true); yield return null;
    }
    [TearDown] public void TearDown() { module.Shutdown(); wallet.Shutdown(); Object.DestroyImmediate(canvasRoot); }
    [UnityTest] public IEnumerator BackendDefinitions_RenderWithoutLocalCatalogAvatarOrNetwork()
    {
        var row = content.Find("Achievement_first"); Assert.That(row, Is.Not.Null); Assert.That(row.GetComponentInChildren<Button>().interactable, Is.False);
        foreach (var label in row.GetComponentsInChildren<TMP_Text>()) Assert.That(label.richText, Is.False); yield break;
    }
    [UnityTest] public IEnumerator RefreshAction_HasReadableLabelContrast()
    {
        var button=content.Find("AchievementsRefresh").GetComponentInChildren<Button>();
        Assert.That(Mathf.Abs(button.GetComponentInChildren<TMP_Text>().color.grayscale-button.image.color.grayscale),Is.GreaterThan(0.3f));
        Assert.That(button.interactable,Is.True);yield break;
    }
    [UnityTest] public IEnumerator ClaimedAchievement_IsClearlyLabeledAndVisuallyDistinctFromProgress()
    {
        var pendingColor=content.Find("Achievement_first").GetComponentInChildren<Button>().image.color;
        backend.Unlocked=true;yield return module.Refresh(()=>{},error=>Assert.Fail(error.Message));
        yield return module.Claim("first",()=>{},error=>Assert.Fail(error.Message));
        var button=content.Find("Achievement_first").GetComponentInChildren<Button>();
        Assert.That(button.GetComponentInChildren<TMP_Text>().text,Is.EqualTo("Claimed"));
        Assert.That(button.interactable,Is.False);
        Assert.That(button.image.color,Is.Not.EqualTo(pendingColor));
        Assert.That(button.image.color.g,Is.GreaterThan(button.image.color.r));
        Assert.That(Mathf.Abs(button.GetComponentInChildren<TMP_Text>().color.grayscale-button.image.color.grayscale),Is.GreaterThan(0.3f));
    }
    [UnityTest] public IEnumerator CompletedAchievement_WithoutReward_ShowsReadableCompletedState()
    {
        backend.Unlocked=true;backend.WithoutReward=true;
        yield return module.Refresh(()=>{},error=>Assert.Fail(error.Message));
        var button=content.Find("Achievement_first").GetComponentInChildren<Button>();
        Assert.That(button.GetComponentInChildren<TMP_Text>().text,Is.EqualTo("Completed"));
        Assert.That(button.interactable,Is.False);
        Assert.That(button.image.color.g,Is.GreaterThan(button.image.color.r));
    }
    [UnityTest] public IEnumerator ConfirmedCompletion_UpdatesViewAndShowsToast_ThenTeardownClearsBoth()
    {
        backend.Unlocked = true; yield return module.Refresh(() => { }, error => Assert.Fail(error.Message)); yield return null;
        Assert.That(content.Find("Achievement_first").GetComponentInChildren<Button>().interactable, Is.True);
        Assert.That(canvasRoot.transform.Find("AchievementToastPopup"), Is.Not.Null);
        module.Shutdown(); yield return null; Assert.That(content.childCount, Is.Zero); Assert.That(canvasRoot.transform.Find("AchievementToastPopup"), Is.Null);
    }
    [UnityTest] public IEnumerator Claim_OutlivesHiddenPanelAndDisplaysConfirmedReward()
    {
        backend.Unlocked = true; yield return module.Refresh(() => { }, error => Assert.Fail(error.Message));
        backend.Wait = true; content.Find("Achievement_first").GetComponentInChildren<Button>().onClick.Invoke(); panelRoot.SetActive(false); yield return null;
        backend.Wait = false; float deadline = Time.realtimeSinceStartup + 2; while (module.IsBusy && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(module.Snapshot.Entries["first"].IsClaimed, Is.True); Assert.That(wallet.Snapshot.Balances["gold"].Amount, Is.EqualTo(110));
        panelRoot.SetActive(true); yield return null; Assert.That(content.Find("Achievement_first").GetComponentInChildren<Button>().interactable, Is.False);
    }
    [UnityTest] public IEnumerator DisableOwner_CancelsActualPendingClaimAndReleasesReservation()
    {
        backend.Unlocked = true; yield return module.Refresh(() => { }, error => Assert.Fail(error.Message)); backend.Wait = true;
        controller.ClaimAchievement("first"); yield return null; Assert.That(module.IsBusy, Is.True);
        controller.gameObject.SetActive(false); yield return null; Assert.That(module.IsBusy || wallet.IsBusy, Is.False); Assert.That(module.RequiresRefresh, Is.True);
    }
    [UnityTest] public IEnumerator DisableDuringClaimReservation_ReleasesUnstartedOperation()
    {
        backend.Unlocked = true; yield return module.Refresh(() => { }, error => Assert.Fail(error.Message));
        Action disableOwner = () => { if (module.IsBusy) controller.gameObject.SetActive(false); };
        module.StateChanged += disableOwner;
        try
        {
            controller.ClaimAchievement("first");
            Assert.That(module.IsBusy || wallet.IsBusy, Is.False);
            Assert.That(backend.Claimed, Is.False);
        }
        finally { module.StateChanged -= disableOwner; }
    }
    [UnityTest] public IEnumerator DisableDuringRefreshReservation_ReleasesUnstartedOperation()
    {
        Action disableOwner = () => { if (module.IsBusy) controller.gameObject.SetActive(false); };
        module.StateChanged += disableOwner;
        try
        {
            controller.RefreshAchievements(); yield return null;
            Assert.That(module.IsBusy || wallet.IsBusy, Is.False);
        }
        finally { module.StateChanged -= disableOwner; }
    }
    private sealed class Backend : IAchievementBackend
    {
        public bool Unlocked; public bool Claimed; public bool Wait; public bool WithoutReward;
        private AchievementState State(string claim = null) => new AchievementState(new AchievementSnapshot(new[] { new AchievementEntry("first", "<b>First</b>", "Backend definition", Unlocked ? 1 : 0, 1, Unlocked, Claimed, Unlocked ? 1900000000000 : 0, WithoutReward ? new AchievementReward(AchievementRewardKind.None) : new AchievementReward(AchievementRewardKind.Currency,"gold",amount:10)) }), Balance(Claimed ? 110 : 100), null, claim);
        public IEnumerator Fetch(Action<AchievementState> success,Action<AchievementError> failure) { success(State()); yield break; }
        public IEnumerator Claim(string id,Action<AchievementState> success,Action<AchievementError> failure) { while(Wait) yield return null; Claimed=true;success(State(id)); }
    }
    private static WalletSnapshot Balance(long amount)=>new WalletSnapshot(new[]{new WalletBalance("gold",amount)},Array.Empty<WalletConversionOffer>());
    private sealed class WalletBackend:IWalletBackend
    {
        public IEnumerator Fetch(Action<WalletSnapshot> success,Action<WalletError> failure) { success(Balance(100));yield break; }
        public IEnumerator Convert(string id,long amount,Action<WalletSnapshot> success,Action<WalletError> failure)=>throw new NotSupportedException();
    }
    private sealed class Session:IAuthenticatedSession
    {
        public bool IsActive=>true;public int SessionVersion=>1;public event Action<string> SessionEnded{add{}remove{}}
        public IEnumerator Send(UnityWebRequest request,ApiResponse response,Action<bool> complete=null)=>throw new NotSupportedException();
        public void RejectSession(int version,string reason=null) {Assert.Fail(reason);}public void Dispose(){}
    }
}
