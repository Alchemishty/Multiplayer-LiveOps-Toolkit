using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Globalization;
using MultiplayerLiveOpsToolkit.DailyRewards;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace MultiplayerLiveOpsToolkit.Backends.Django
{
    public sealed class DjangoDailyRewardBackend : IDailyRewardBackend
    {
        private readonly string baseUrl;
        private readonly IAuthenticatedSession session;
        public DjangoDailyRewardBackend(string baseUrl, IAuthenticatedSession session)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var address)
                || (address.Scheme != Uri.UriSchemeHttps && !(address.Scheme == Uri.UriSchemeHttp && address.IsLoopback))
                || !string.IsNullOrEmpty(address.UserInfo) || !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))
                throw new ArgumentException("Daily Rewards backend requires HTTPS, or HTTP on loopback, without URL credentials/query/fragment.", nameof(baseUrl));
            this.baseUrl = baseUrl.TrimEnd('/');
        }
        public IEnumerator Fetch(Action<DailyRewardState> onSuccess, Action<DailyRewardError> onFailure)
        { ValidateCallbacks(onSuccess, onFailure); return Send(null, onSuccess, onFailure); }
        public IEnumerator Claim(DateTime expectedDate, Action<DailyRewardState> onSuccess, Action<DailyRewardError> onFailure)
        {
            ValidateCallbacks(onSuccess, onFailure);
            if (expectedDate != expectedDate.Date) throw new ArgumentException("Daily claim requires the displayed backend date.", nameof(expectedDate));
            return Send(expectedDate, onSuccess, onFailure);
        }
        private IEnumerator Send(DateTime? expectedDate, Action<DailyRewardState> onSuccess, Action<DailyRewardError> onFailure)
        {
            bool hasCompleted = false;
            bool isClaim = expectedDate != null;
            try
            {
                int version = session.SessionVersion;
                if (!session.IsActive) { hasCompleted = true; onFailure(new DailyRewardError(DailyRewardErrorCode.Cancelled, "Player session is not active.")); yield break; }
                using (var request = new UnityWebRequest(baseUrl + (isClaim ? "/api/daily-rewards/claim/" : "/api/daily-rewards/state/"), isClaim ? "POST" : "GET"))
                {
                    request.downloadHandler = new DownloadHandlerBuffer();
                    if (isClaim) request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { date = expectedDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) })));
                    var response = new ApiResponse();
                    yield return session.Send(request, response);
                    if (!session.IsActive || session.SessionVersion != version)
                    { hasCompleted = true; onFailure(new DailyRewardError(DailyRewardErrorCode.Cancelled, "Player session changed before daily reward response.")); yield break; }
                    if (!response.IsSuccess) { var failure = ClassifyFailure(response, isClaim); hasCompleted = true; onFailure(failure); yield break; }
                    DailyRewardState result = null;
                    try { result = ParseState(response.Text, expectedDate); }
                    catch (Exception exception) when (exception is JsonException || exception is ArgumentException || exception is FormatException || exception is InvalidCastException || exception is OverflowException)
                    { /* Untrusted response fragments are never diagnostics. */ }
                    hasCompleted = true;
                    if (result == null) onFailure(new DailyRewardError(isClaim ? DailyRewardErrorCode.UnknownOutcome : DailyRewardErrorCode.InvalidResponse, isClaim ? "Claim could not be confirmed. Refresh Daily Rewards." : "Daily Rewards response was invalid."));
                    else onSuccess(result);
                }
            }
            finally { if (!hasCompleted) onFailure(new DailyRewardError(DailyRewardErrorCode.Cancelled, "Daily Rewards request was disposed.")); }
        }
        private static DailyRewardState ParseState(string json, DateTime? expectedDate)
        {
            var root = ReadObject(json);
            if (root.Count != 3 || !(root["calendar"] is JObject calendar) || calendar.Count != 2
                || !(calendar["days"] is JArray days) || days.Count > 31 || root["wallet"] == null) throw new FormatException();
            DateTime today = ReadDate(ReadString(calendar, "today_date"));
            string claim = ReadNullableString(root, "claimed_date");
            DateTime? claimed = claim == null ? (DateTime?)null : ReadDate(claim);
            if (claimed != expectedDate) throw new FormatException();
            var parsedDays = new List<DailyRewardDay>();
            foreach (var token in days)
            {
                if (!(token is JObject day) || day.Count != 3 || day["reward"] == null) throw new FormatException();
                DailyCurrencyReward reward = null;
                if (day["reward"].Type != JTokenType.Null)
                {
                    if (!(day["reward"] is JObject grant) || grant.Count != 2) throw new FormatException();
                    reward = new DailyCurrencyReward(ReadString(grant, "currency_id"), ReadInteger(grant, "amount"));
                }
                parsedDays.Add(new DailyRewardDay(checked((int)ReadInteger(day, "day")), reward, ReadBoolean(day, "is_claimed")));
            }
            var balances = root["wallet"].Type == JTokenType.Null ? null : DjangoWalletBackend.ParseSnapshot(root["wallet"].ToString(Formatting.None));
            return new DailyRewardState(new DailyRewardSnapshot(today, parsedDays), balances, claimed);
        }
        private static DateTime ReadDate(string value)
        {
            if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw new FormatException();
            return date;
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
        private static DailyRewardError ClassifyFailure(ApiResponse response, bool isClaim)
        {
            string code = null;
            try { code = ReadString(ReadObject(response.Text), "code"); }
            catch (Exception exception) when (exception is JsonException || exception is FormatException) { }
            DailyRewardErrorCode category;
            switch (code)
            {
                case "already_claimed": category = DailyRewardErrorCode.AlreadyClaimed; break;
                case "invalid_daily_reward_configuration": case "missing_identity": category = DailyRewardErrorCode.Misconfigured; break;
                case "day_changed": category = DailyRewardErrorCode.DayChanged; break;
                case "invalid_request": category = DailyRewardErrorCode.Rejected; break;
                default: category = isClaim ? DailyRewardErrorCode.UnknownOutcome : DailyRewardErrorCode.Unavailable; break;
            }
            return new DailyRewardError(category, category == DailyRewardErrorCode.UnknownOutcome ? "Claim outcome is unknown. Refresh Daily Rewards." : "Daily Rewards request failed: " + category + ".");
        }
        private static JObject ReadObject(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 262144) throw new FormatException();
            using (var text = new StringReader(json))
            using (var reader = new JsonTextReader(text) { MaxDepth = 8, DateParseHandling = DateParseHandling.None })
            { var root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }); if (reader.Read()) throw new FormatException(); return root; }
        }
        private static string ReadString(JObject value, string field)
        { var token = value[field]; if (token == null || token.Type != JTokenType.String) throw new FormatException(); return (string)token; }
        private static void ValidateCallbacks(Action<DailyRewardState> onSuccess, Action<DailyRewardError> onFailure)
        { if (onSuccess == null) throw new ArgumentNullException(nameof(onSuccess)); if (onFailure == null) throw new ArgumentNullException(nameof(onFailure)); }
    }
}
