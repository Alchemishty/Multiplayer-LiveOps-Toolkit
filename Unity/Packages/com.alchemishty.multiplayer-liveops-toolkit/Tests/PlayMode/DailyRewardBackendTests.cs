using MultiplayerLiveOpsToolkit.Backends.Django;
using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using System.Linq;
using System.Text;
using MultiplayerLiveOpsToolkit.DailyRewards;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.Networking;
using UnityEngine.TestTools;

public class DailyRewardBackendTests
{
    public static string State(bool claimed = false) => new JObject {
        ["calendar"] = new JObject { ["today_date"] = "2026-10-25", ["days"] = new JArray(Enumerable.Range(1,31).Select(day => new JObject { ["day"] = day, ["reward"] = day == 25 ? new JObject { ["currency_id"]="gold",["amount"]=50 } : null,["is_claimed"]=claimed && day==25 })) },
        ["wallet"] = new JObject { ["balances"] = new JArray(new JObject { ["currency_id"]="gold",["amount"]=claimed?150:100 }),["offers"]=new JArray() },["claimed_date"]=claimed?"2026-10-25":null }.ToString();
    [UnityTest] public IEnumerator Fetch_UsesExplicitSessionAndServerDate()
    {
        var session = new Session(); DailyRewardState result = null;
        yield return new DjangoDailyRewardBackend("https://daily.example.test",session).Fetch(value=>result=value,error=>Assert.Fail(error.Message));
        Assert.That(session.Url,Does.EndWith("/api/daily-rewards/state/"));Assert.That(result.Calendar.Today,Is.EqualTo(new DateTime(2026,10,25)));
    }
    [UnityTest] public IEnumerator Claim_SendsDisplayedDateOnly_AndConfirmsAttendance()
    {
        var session = new Session { Response=State(true) }; DailyRewardState result=null;
        yield return new DjangoDailyRewardBackend("https://daily.example.test",session).Claim(new DateTime(2026,10,25),value=>result=value,error=>Assert.Fail(error.Message));
        Assert.That(session.Body,Is.EqualTo("{\"date\":\"2026-10-25\"}"));Assert.That(result.Calendar.CanClaimToday,Is.False);
    }
    [UnityTest] public IEnumerator InvalidOrWrongDateClaim_IsUnknownWithoutRetry()
    {
        var session=new Session {Response=State(true).Replace("2026-10-25","2026-10-26")}; DailyRewardError failure=null;
        yield return new DjangoDailyRewardBackend("https://daily.example.test",session).Claim(new DateTime(2026,10,25),value=>Assert.Fail(),error=>failure=error);
        Assert.That(failure.Code,Is.EqualTo(DailyRewardErrorCode.UnknownOutcome));Assert.That(session.RequestCount,Is.EqualTo(1));
    }
    [UnityTest] public IEnumerator AccountChange_RejectsOldPrivateCalendar()
    {
        var session=new Session { SwitchAccount=true };DailyRewardError failure=null;
        yield return new DjangoDailyRewardBackend("https://daily.example.test",session).Fetch(value=>Assert.Fail(),error=>failure=error);
        Assert.That(failure.Code,Is.EqualTo(DailyRewardErrorCode.Cancelled));
    }
    [UnityTest] public IEnumerator RolloverAndConfigurationErrors_AreTyped()
    {
        var session=new Session { Status=400,Response="{\"code\":\"day_changed\"}" };DailyRewardError failure=null;
        var backend=new DjangoDailyRewardBackend("https://daily.example.test",session);
        yield return backend.Claim(new DateTime(2026,10,25),value=>Assert.Fail(),error=>failure=error);Assert.That(failure.Code,Is.EqualTo(DailyRewardErrorCode.DayChanged));
        session.Status=409;session.Response="{\"code\":\"invalid_daily_reward_configuration\"}";
        yield return backend.Fetch(value=>Assert.Fail(),error=>failure=error);Assert.That(failure.Code,Is.EqualTo(DailyRewardErrorCode.Misconfigured));
    }
    private sealed class Session:IAuthenticatedSession
    {
        public bool IsActive=>true;public int SessionVersion{get;private set;}public event Action<string> SessionEnded{add{}remove{}}
        public string Response=State();public int Status=200;public bool SwitchAccount;public string Body;public string Url;public int RequestCount;
        public IEnumerator Send(UnityWebRequest request,ApiResponse response,Action<bool> complete=null)
        {
            RequestCount++;Url=request.url;Body=request.uploadHandler==null?null:Encoding.UTF8.GetString(request.uploadHandler.data);
            yield return null;if(SwitchAccount)SessionVersion++;
            response.Text=Response;response.ResponseCode=Status;response.Result=Status==200?UnityWebRequest.Result.Success:UnityWebRequest.Result.ProtocolError;
        }
        public void RejectSession(int version,string reason=null){}public void Dispose(){}
    }
}
