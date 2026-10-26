using MultiplayerLiveOpsToolkit.Backends.Django;
using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using System.Text;
using MultiplayerLiveOpsToolkit.Achievements;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.Networking;
using UnityEngine.TestTools;

public class AchievementBackendTests
{
    public const string STATE = "{\"achievements\":{\"requires_inventory\":false,\"entries\":[{\"achievement_id\":\"first\",\"display_name\":\"First\",\"description\":\"Backend goal\",\"current_count\":1,\"target_count\":1,\"is_unlocked\":true,\"is_claimed\":false,\"unlocked_at\":1900000000000,\"reward\":{\"kind\":\"currency\",\"currency_id\":\"gold\",\"item_id\":null,\"amount\":10}}]},\"wallet\":{\"balances\":[{\"currency_id\":\"gold\",\"amount\":100}],\"offers\":[]},\"inventory\":null,\"claimed_achievement_id\":null}";
    [UnityTest] public IEnumerator Fetch_UsesExplicitSessionAndBackendDefinitions()
    {
        var session = new Session(); AchievementState state = null;
        yield return new DjangoAchievementBackend("https://achievement.example.test", session).Fetch(value => state = value, error => Assert.Fail(error.Message));
        Assert.That(session.Url, Does.EndWith("/api/achievements/state/")); Assert.That(state.Achievements.Entries["first"].CanClaim, Is.True);
    }
    [UnityTest] public IEnumerator Claim_SendsAchievementIdOnlyAndReadsConfirmedResult()
    {
        var root = JObject.Parse(STATE); root["claimed_achievement_id"] = "first"; root["achievements"]["entries"][0]["is_claimed"] = true;
        var session = new Session { Response = root.ToString() }; AchievementState state = null;
        yield return new DjangoAchievementBackend("https://achievement.example.test", session).Claim("first", value => state = value, error => Assert.Fail(error.Message));
        Assert.That(session.Url, Does.EndWith("/api/achievements/claim/")); Assert.That(session.Body, Is.EqualTo("{\"achievement_id\":\"first\"}")); Assert.That(state.ClaimedAchievementId, Is.EqualTo("first"));
    }
    [UnityTest] public IEnumerator MalformedClaim_IsUnknownWithoutRetry()
    {
        var session = new Session { Response = STATE.Replace("\"amount\":10", "\"amount\":-1") }; AchievementError error = null;
        yield return new DjangoAchievementBackend("https://achievement.example.test", session).Claim("first", value => Assert.Fail(), value => error = value);
        Assert.That(error.Code, Is.EqualTo(AchievementErrorCode.UnknownOutcome)); Assert.That(session.Count, Is.EqualTo(1));
    }
    [UnityTest] public IEnumerator ChangedAccount_CancelsOldPrivateProgress()
    {
        var session = new Session { SwitchAccount = true }; AchievementError error = null;
        yield return new DjangoAchievementBackend("https://achievement.example.test", session).Fetch(value => Assert.Fail(), value => error = value);
        Assert.That(error.Code, Is.EqualTo(AchievementErrorCode.Cancelled));
    }
    [UnityTest] public IEnumerator AlreadyClaimed_IsTypedAndConfigurationIsFatalCategory()
    {
        var session = new Session { Status = 400, Response = "{\"code\":\"already_claimed\"}" }; AchievementError error = null;
        var backend = new DjangoAchievementBackend("https://achievement.example.test", session);
        yield return backend.Claim("first", value => Assert.Fail(), value => error = value); Assert.That(error.Code, Is.EqualTo(AchievementErrorCode.AlreadyClaimed));
        session.Status = 409; session.Response = "{\"code\":\"achievement_configuration\"}";
        yield return backend.Fetch(value => Assert.Fail(), value => error = value); Assert.That(error.Code, Is.EqualTo(AchievementErrorCode.Misconfigured));
    }
    [UnityTest] public IEnumerator DisposedRequest_CompletesCancellationExactlyOnce()
    {
        int completed = 0;
        var operation = new DjangoAchievementBackend("https://achievement.example.test", new Session()).Fetch(value => Assert.Fail(), error => completed++);
        Assert.That(operation.MoveNext(), Is.True); ((IDisposable)operation).Dispose(); yield return null; Assert.That(completed, Is.EqualTo(1));
    }
    private sealed class Session : IAuthenticatedSession
    {
        public bool IsActive => true; public int SessionVersion { get; private set; } public event Action<string> SessionEnded { add { } remove { } }
        public string Response = STATE; public int Status = 200; public bool SwitchAccount; public string Body; public string Url; public int Count;
        public IEnumerator Send(UnityWebRequest request, ApiResponse response, Action<bool> complete = null)
        {
            Count++; Url = request.url; Body = request.uploadHandler == null ? null : Encoding.UTF8.GetString(request.uploadHandler.data);
            yield return null; if (SwitchAccount) SessionVersion++;
            response.Text = Response; response.ResponseCode = Status; response.Result = Status == 200 ? UnityWebRequest.Result.Success : UnityWebRequest.Result.ProtocolError;
        }
        public void RejectSession(int version, string reason = null) { } public void Dispose() { }
    }
}
