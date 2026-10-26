using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using System.Linq;
using MultiplayerLiveOpsToolkit.DailyRewards;
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
using Object=UnityEngine.Object;
public class DailyRewardsPanelTests
{
    private GameObject root;
    private GameObject panelRoot;
    private Transform calendarGrid;
    private DailyRewardsSampleController controller;
    private DailyRewardsModule module;
    private WalletModule wallet;
    private Backend backend;
    [UnitySetUp] public IEnumerator SetUp()
    {
        wallet=new WalletModule(new WalletBackend(),exception=>Assert.Fail(exception.Message));yield return wallet.Initialize();
        backend=new Backend();module=new DailyRewardsModule(backend,exception=>Assert.Fail(exception.Message),wallet);yield return module.Initialize();
        root=new GameObject("Daily sample without Netcode",typeof(Canvas));panelRoot=new GameObject("Daily panel",typeof(RectTransform));panelRoot.transform.SetParent(root.transform);panelRoot.SetActive(false);
        calendarGrid=new GameObject("Calendar",typeof(RectTransform)).transform;calendarGrid.SetParent(panelRoot.transform);
        var status=new GameObject("Status",typeof(RectTransform)).AddComponent<TextMeshProUGUI>();status.transform.SetParent(panelRoot.transform);
        var header=new GameObject("Month",typeof(RectTransform)).AddComponent<TextMeshProUGUI>();header.transform.SetParent(panelRoot.transform);
        var panel=panelRoot.AddComponent<DailyLoginPanel>();
        var owner=new GameObject("Durable Daily Rewards owner");owner.transform.SetParent(root.transform);owner.SetActive(false);controller=owner.AddComponent<DailyRewardsSampleController>();
#if UNITY_EDITOR
        var fields=new SerializedObject(panel);fields.FindProperty("calendarGrid").objectReferenceValue=calendarGrid;fields.FindProperty("statusText").objectReferenceValue=status;fields.FindProperty("monthHeaderText").objectReferenceValue=header;fields.ApplyModifiedPropertiesWithoutUndo();
        var controllerFields=new SerializedObject(controller);controllerFields.FindProperty("panel").objectReferenceValue=panel;controllerFields.ApplyModifiedPropertiesWithoutUndo();
#endif
        controller.Bind(module,new Session());owner.SetActive(true);panelRoot.SetActive(true);yield return null;
    }
    [TearDown] public void TearDown(){module.Shutdown();wallet.Shutdown();Object.DestroyImmediate(root);}
    [UnityTest] public IEnumerator Calendar_UsesBackendDateAndOffersExplicitRefresh()
    {
        Assert.That(calendarGrid.Find("Day_25").GetComponent<Button>().interactable,Is.True);
        Assert.That(calendarGrid.Find("Day_26").GetComponent<Button>().interactable,Is.False);
        Assert.That(panelRoot.transform.Find("DailyRewardsRefresh"),Is.Not.Null);yield break;
    }
    [UnityTest] public IEnumerator Calendar_WithSparePanelHeight_KeepsRefreshRowCompact()
    {
        var panelRect=panelRoot.GetComponent<RectTransform>();panelRect.sizeDelta=new Vector2(600,800);
        var layout=panelRoot.AddComponent<VerticalLayoutGroup>();layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandHeight=false;
        calendarGrid.gameObject.AddComponent<LayoutElement>().flexibleHeight=1;
        Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);yield return null;
        var refreshRect=panelRoot.transform.Find("DailyRewardsRefresh").GetComponent<RectTransform>();
        Assert.That(refreshRect.rect.height,Is.LessThanOrEqualTo(60),"Calendar refresh must remain a compact action row rather than consume spare panel height.");
        Assert.That(refreshRect.rect.height,Is.GreaterThanOrEqualTo(32));
    }
    [UnityTest] public IEnumerator Calendar_RefreshAction_HasReadableLabelContrast()
    {
        var button=panelRoot.transform.Find("DailyRewardsRefresh").GetComponentInChildren<Button>();
        var label=button.GetComponentInChildren<TMP_Text>();
        Assert.That(Mathf.Abs(label.color.grayscale-button.image.color.grayscale),Is.GreaterThan(0.3f),"Refresh label must contrast against its action background.");
        yield break;
    }
    [UnityTest] public IEnumerator HiddenTab_DoesNotCancelClaimOrConfirmedNotification()
    {
        backend.Wait=true;int notifications=0;Action confirmed=()=>notifications++;controller.AttendanceConfirmed+=confirmed;
        try
        {
            calendarGrid.Find("Day_25").GetComponent<Button>().onClick.Invoke();panelRoot.SetActive(false);yield return null;backend.Wait=false;
            float deadline=Time.realtimeSinceStartup+2;while(module.IsBusy && Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(module.Snapshot.CanClaimToday,Is.False);Assert.That(wallet.Snapshot.Balances["gold"].Amount,Is.EqualTo(150));Assert.That(notifications,Is.EqualTo(1));
            panelRoot.SetActive(true);yield return null;Assert.That(calendarGrid.Find("Day_25").GetComponent<Button>().interactable,Is.False);
        }
        finally{controller.AttendanceConfirmed-=confirmed;}
    }
    [UnityTest] public IEnumerator DisableDuringReservation_ReleasesUnstartedClaim()
    {
        Action disable=()=>{if(module.IsBusy)controller.gameObject.SetActive(false);};module.StateChanged+=disable;
        try{controller.ClaimToday();Assert.That(module.IsBusy || wallet.IsBusy,Is.False);Assert.That(backend.Claimed,Is.False);}
        finally{module.StateChanged-=disable;}yield break;
    }
    [UnityTest] public IEnumerator OwnerDisable_CancelsPendingClaimAndTeardownClearsCalendar()
    {
        backend.Wait=true;controller.ClaimToday();yield return null;controller.gameObject.SetActive(false);
        Assert.That(module.IsBusy || wallet.IsBusy,Is.False);Assert.That(module.RequiresRefresh,Is.True);
        module.Shutdown();yield return null;Assert.That(calendarGrid.childCount,Is.Zero);
    }
    private static WalletSnapshot Balance(long amount)=>new WalletSnapshot(new[]{new WalletBalance("gold",amount)},Array.Empty<WalletConversionOffer>());
    private sealed class Backend:IDailyRewardBackend
    {
        public bool Wait;public bool Claimed;
        private DailyRewardState State(bool claim=false)=>new DailyRewardState(new DailyRewardSnapshot(new DateTime(2026,10,25),Enumerable.Range(1,31).Select(day=>new DailyRewardDay(day,day==25?new DailyCurrencyReward("gold",50):null,day==25 && Claimed))),Balance(Claimed?150:100),claim?new DateTime(2026,10,25):(DateTime?)null);
        public IEnumerator Fetch(Action<DailyRewardState> success,Action<DailyRewardError> failure){success(State());yield break;}
        public IEnumerator Claim(DateTime date,Action<DailyRewardState> success,Action<DailyRewardError> failure){while(Wait)yield return null;Claimed=true;success(State(true));}
    }
    private sealed class WalletBackend:IWalletBackend
    {
        public IEnumerator Fetch(Action<WalletSnapshot> success,Action<WalletError> failure){success(Balance(100));yield break;}
        public IEnumerator Convert(string id,long amount,Action<WalletSnapshot> success,Action<WalletError> failure)=>throw new NotSupportedException();
    }
    private sealed class Session:IAuthenticatedSession
    {
        public bool IsActive=>true;public int SessionVersion=>1;public event Action<string> SessionEnded{add{}remove{}}
        public IEnumerator Send(UnityWebRequest request,ApiResponse response,Action<bool> complete=null)=>throw new NotSupportedException();
        public void RejectSession(int version,string reason=null){Assert.Fail(reason);}public void Dispose(){}
    }
}
