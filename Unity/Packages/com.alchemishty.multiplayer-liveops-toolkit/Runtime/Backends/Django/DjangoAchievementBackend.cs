using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MultiplayerLiveOpsToolkit.Achievements;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace MultiplayerLiveOpsToolkit.Backends.Django
{
    public sealed class DjangoAchievementBackend : IAchievementBackend
    {
        private readonly string baseUrl;
        private readonly IAuthenticatedSession session;
        public DjangoAchievementBackend(string baseUrl, IAuthenticatedSession session)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var address)
                || (address.Scheme != Uri.UriSchemeHttps && !(address.Scheme == Uri.UriSchemeHttp && address.IsLoopback))
                || !string.IsNullOrEmpty(address.UserInfo) || !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))
                throw new ArgumentException("Achievement backend requires HTTPS, or HTTP on loopback, without URL credentials/query/fragment.", nameof(baseUrl));
            this.baseUrl = baseUrl.TrimEnd('/');
        }
        public IEnumerator Fetch(Action<AchievementState> onSuccess, Action<AchievementError> onFailure)
        { ValidateCallbacks(onSuccess, onFailure); return Send(null, onSuccess, onFailure); }
        public IEnumerator Claim(string achievementId, Action<AchievementState> onSuccess, Action<AchievementError> onFailure)
        {
            ValidateCallbacks(onSuccess, onFailure);
            if (achievementId == null || !Regex.IsMatch(achievementId, @"\A[A-Za-z0-9_.:-]{1,128}\z")) throw new ArgumentException("Achievement claim requires an achievement ID.", nameof(achievementId));
            return Send(achievementId, onSuccess, onFailure);
        }
        private IEnumerator Send(string achievementId, Action<AchievementState> onSuccess, Action<AchievementError> onFailure)
        {
            bool hasCompleted = false;
            bool isClaim = achievementId != null;
            try
            {
                int version = session.SessionVersion;
                if (!session.IsActive) { hasCompleted = true; onFailure(new AchievementError(AchievementErrorCode.Cancelled, "Player session is not active.")); yield break; }
                using (var request = new UnityWebRequest(baseUrl + (isClaim ? "/api/achievements/claim/" : "/api/achievements/state/"), isClaim ? "POST" : "GET"))
                {
                    request.downloadHandler = new DownloadHandlerBuffer();
                    if (isClaim) request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { achievement_id = achievementId })));
                    var response = new ApiResponse();
                    yield return session.Send(request, response);
                    if (!session.IsActive || session.SessionVersion != version)
                    { hasCompleted = true; onFailure(new AchievementError(AchievementErrorCode.Cancelled, "Player session changed before achievement response.")); yield break; }
                    if (!response.IsSuccess) { var failure = ClassifyFailure(response, isClaim); hasCompleted = true; onFailure(failure); yield break; }
                    AchievementState result = null;
                    try { result = ParseState(response.Text, achievementId); }
                    catch (Exception exception) when (exception is JsonException || exception is ArgumentException || exception is FormatException || exception is InvalidCastException || exception is OverflowException)
                    { /* Untrusted response fragments are never diagnostics. */ }
                    hasCompleted = true;
                    if (result == null) onFailure(new AchievementError(isClaim ? AchievementErrorCode.UnknownOutcome : AchievementErrorCode.InvalidResponse, isClaim ? "Claim could not be confirmed. Refresh Achievements." : "Achievement response was invalid."));
                    else onSuccess(result);
                }
            }
            finally { if (!hasCompleted) onFailure(new AchievementError(AchievementErrorCode.Cancelled, "Achievement request was disposed.")); }
        }
        private static AchievementState ParseState(string json, string expectedAchievementId)
        {
            var root = ReadObject(json);
            if (root.Count != 4 || !(root["achievements"] is JObject achievements) || achievements.Count != 2
                || achievements["requires_inventory"]?.Type != JTokenType.Boolean || !(achievements["entries"] is JArray entries) || entries.Count > 1024) throw new FormatException();
            string claimed = ReadNullableString(root, "claimed_achievement_id");
            if (claimed != expectedAchievementId) throw new FormatException();
            var parsed = new List<AchievementEntry>();
            foreach (var token in entries)
            {
                if (!(token is JObject entry) || entry.Count != 9 || !(entry["reward"] is JObject reward) || reward.Count != 4) throw new FormatException();
                AchievementRewardKind kind;
                switch (ReadString(reward, "kind"))
                {
                    case "none": kind = AchievementRewardKind.None; break;
                    case "currency": kind = AchievementRewardKind.Currency; break;
                    case "accessory": kind = AchievementRewardKind.Cosmetic; break;
                    default: throw new FormatException();
                }
                var grant = new AchievementReward(kind, ReadNullableString(reward, "currency_id"), ReadNullableString(reward, "item_id"), ReadInteger(reward, "amount"));
                parsed.Add(new AchievementEntry(ReadString(entry, "achievement_id"), ReadString(entry, "display_name"), ReadString(entry, "description"),
                    ReadInteger(entry, "current_count"), ReadInteger(entry, "target_count"), ReadBoolean(entry, "is_unlocked"), ReadBoolean(entry, "is_claimed"), ReadInteger(entry, "unlocked_at"), grant));
            }
            if (root["wallet"] == null || root["inventory"] == null) throw new FormatException();
            var balances = root["wallet"].Type == JTokenType.Null ? null : DjangoWalletBackend.ParseSnapshot(root["wallet"].ToString(Formatting.None));
            var owned = root["inventory"].Type == JTokenType.Null ? null : DjangoInventoryBackend.ParseSnapshot(root["inventory"].ToString(Formatting.None));
            return new AchievementState(new AchievementSnapshot(parsed, (bool)achievements["requires_inventory"]), balances, owned, claimed);
        }
        private static string ReadNullableString(JObject value, string field)
        {
            var token = value[field]; if (token == null || (token.Type != JTokenType.Null && token.Type != JTokenType.String)) throw new FormatException();
            return token.Type == JTokenType.Null ? null : (string)token;
        }
        private static long ReadInteger(JObject value, string field)
        { if (value[field]?.Type != JTokenType.Integer) throw new FormatException(); return (long)value[field]; }
        private static bool ReadBoolean(JObject value, string field)
        { if (value[field]?.Type != JTokenType.Boolean) throw new FormatException(); return (bool)value[field]; }
        private static AchievementError ClassifyFailure(ApiResponse response, bool isClaim)
        {
            string code = null;
            try { code = ReadString(ReadObject(response.Text), "code"); }
            catch (Exception exception) when (exception is JsonException || exception is FormatException) { }
            AchievementErrorCode category;
            switch (code)
            {
                case "already_claimed": category = AchievementErrorCode.AlreadyClaimed; break;
                case "achievement_configuration": case "identity_not_initialized": category = AchievementErrorCode.Misconfigured; break;
                case "achievement_unavailable": case "condition_not_met": case "no_reward": case "invalid_request": category = AchievementErrorCode.Rejected; break;
                default: category = isClaim ? AchievementErrorCode.UnknownOutcome : AchievementErrorCode.Unavailable; break;
            }
            return new AchievementError(category, category == AchievementErrorCode.UnknownOutcome ? "Claim outcome is unknown. Refresh Achievements." : "Achievement request failed: " + category + ".");
        }
        private static JObject ReadObject(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 8388608) throw new FormatException();
            using (var text = new StringReader(json))
            using (var reader = new JsonTextReader(text) { MaxDepth = 8, DateParseHandling = DateParseHandling.None })
            { var root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }); if (reader.Read()) throw new FormatException(); return root; }
        }
        private static string ReadString(JObject value, string field)
        { var token = value[field]; if (token == null || token.Type != JTokenType.String) throw new FormatException(); return (string)token; }
        private static void ValidateCallbacks(Action<AchievementState> onSuccess, Action<AchievementError> onFailure)
        { if (onSuccess == null) throw new ArgumentNullException(nameof(onSuccess)); if (onFailure == null) throw new ArgumentNullException(nameof(onFailure)); }
    }
}
