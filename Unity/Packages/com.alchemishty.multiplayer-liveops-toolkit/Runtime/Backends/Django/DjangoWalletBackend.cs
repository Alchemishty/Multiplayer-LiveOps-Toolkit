using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MultiplayerLiveOpsToolkit.Wallet;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace MultiplayerLiveOpsToolkit.Backends.Django
{
    /// <summary>Reference Django wallet transport; all requests use the explicitly supplied player session.</summary>
    public sealed class DjangoWalletBackend : IWalletBackend
    {
        private readonly string baseUrl;
        private readonly IAuthenticatedSession session;
        public DjangoWalletBackend(string baseUrl, IAuthenticatedSession session)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var address)
                || (address.Scheme != Uri.UriSchemeHttps && !(address.Scheme == Uri.UriSchemeHttp && address.IsLoopback))
                || !string.IsNullOrEmpty(address.UserInfo) || !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))
                throw new ArgumentException("Wallet backend requires HTTPS, or HTTP on loopback, without URL credentials/query/fragment.", nameof(baseUrl));
            this.baseUrl = baseUrl.TrimEnd('/');
        }
        public IEnumerator Fetch(Action<WalletSnapshot> onSuccess, Action<WalletError> onFailure)
        {
            ValidateCallbacks(onSuccess, onFailure);
            return Send(false, null, 0, onSuccess, onFailure);
        }
        public IEnumerator Convert(string offerId, long amount, Action<WalletSnapshot> onSuccess, Action<WalletError> onFailure)
        {
            ValidateCallbacks(onSuccess, onFailure);
            if (offerId == null || !Regex.IsMatch(offerId, @"\A[A-Za-z0-9_.:-]{1,128}\z")) throw new ArgumentException("Wallet conversion requires an offer ID.", nameof(offerId));
            if (amount < 1 || amount > WalletBalance.MAX_AMOUNT) throw new ArgumentOutOfRangeException(nameof(amount));
            return Send(true, offerId, amount, onSuccess, onFailure);
        }
        private IEnumerator Send(bool isConversion, string offerId, long amount, Action<WalletSnapshot> onSuccess, Action<WalletError> onFailure)
        {
            bool hasCompleted = false;
            try
            {
                int version = session.SessionVersion;
                if (!session.IsActive)
                { hasCompleted = true; onFailure(new WalletError(WalletErrorCode.Cancelled, "Player session is not active.")); yield break; }
                using (var request = new UnityWebRequest(baseUrl + (isConversion ? "/api/wallet/convert/" : "/api/wallet/"), isConversion ? "POST" : "GET"))
                {
                    request.downloadHandler = new DownloadHandlerBuffer();
                    if (isConversion) request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { offer_id = offerId, amount })));
                    var response = new ApiResponse();
                    yield return session.Send(request, response);
                    if (!session.IsActive || session.SessionVersion != version)
                    { hasCompleted = true; onFailure(new WalletError(WalletErrorCode.Cancelled, "Player session changed before wallet response.")); yield break; }
                    if (!response.IsSuccess)
                    { var failure = ClassifyFailure(response, isConversion); hasCompleted = true; onFailure(failure); yield break; }
                    WalletSnapshot result = null;
                    try { result = ParseSnapshot(response.Text); }
                    catch (Exception exception) when (exception is JsonException || exception is ArgumentException || exception is FormatException || exception is OverflowException || exception is InvalidCastException)
                    { /* Untrusted protocol fields never become diagnostic text. */ }
                    hasCompleted = true;
                    if (result == null) onFailure(new WalletError(isConversion ? WalletErrorCode.UnknownOutcome : WalletErrorCode.InvalidResponse,
                        isConversion ? "Conversion response was invalid. Refresh balances before trying again." : "Wallet response was invalid."));
                    else onSuccess(result);
                }
            }
            finally { if (!hasCompleted) onFailure(new WalletError(WalletErrorCode.Cancelled, "Wallet request was disposed.")); }
        }
        internal static WalletSnapshot ParseSnapshot(string json)
        {
            var root = ReadObject(json);
            if (root.Count != 2) throw new FormatException();
            var balances = root["balances"] as JArray;
            var offers = root["offers"] as JArray;
            if (balances == null || offers == null || balances.Count > 256 || offers.Count > 128) throw new FormatException();
            var parsedBalances = new List<WalletBalance>();
            foreach (var entry in balances)
            {
                var balance = entry as JObject;
                if (balance == null || balance.Count != 2) throw new FormatException();
                parsedBalances.Add(new WalletBalance(ReadString(balance, "currency_id"), ReadInteger(balance, "amount")));
            }
            var parsedOffers = new List<WalletConversionOffer>();
            foreach (var entry in offers)
            {
                var offer = entry as JObject;
                if (offer == null || offer.Count != 5) throw new FormatException();
                parsedOffers.Add(new WalletConversionOffer(ReadString(offer, "offer_id"), ReadString(offer, "source_currency_id"), ReadString(offer, "target_currency_id"),
                    ReadInteger(offer, "source_units"), ReadInteger(offer, "target_units")));
            }
            return new WalletSnapshot(parsedBalances, parsedOffers);
        }
        private static WalletError ClassifyFailure(ApiResponse response, bool isConversion)
        {
            string code = null;
            try { var error = ReadObject(response.Text); code = ReadString(error, "code"); }
            catch (Exception exception) when (exception is JsonException || exception is FormatException || exception is ArgumentException) { }
            WalletErrorCode category;
            switch (code)
            {
                case "insufficient_funds": category = WalletErrorCode.InsufficientFunds; break;
                case "invalid_units": category = WalletErrorCode.InvalidUnits; break;
                case "balance_limit": category = WalletErrorCode.BalanceLimit; break;
                case "missing_wallet": case "invalid_wallet_configuration": case "identity_not_initialized": category = WalletErrorCode.Misconfigured; break;
                case "offer_unavailable": case "invalid_request": category = WalletErrorCode.Rejected; break;
                default: category = isConversion ? WalletErrorCode.UnknownOutcome : WalletErrorCode.Unavailable; break;
            }
            return new WalletError(category, category == WalletErrorCode.UnknownOutcome ? "Conversion could not be confirmed. Refresh balances." : "Wallet request failed: " + category + ".");
        }
        private static JObject ReadObject(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 131072) throw new FormatException();
            using (var text = new StringReader(json))
            using (var reader = new JsonTextReader(text) { MaxDepth = 6, DateParseHandling = DateParseHandling.None })
            {
                var result = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (result == null || reader.Read()) throw new FormatException();
                return result;
            }
        }
        private static string ReadString(JObject value, string field)
        {
            var token = value[field];
            if (token == null || token.Type != JTokenType.String || string.IsNullOrEmpty((string)token) || ((string)token).Length > 128) throw new FormatException();
            return (string)token;
        }
        private static long ReadInteger(JObject value, string field)
        { var token = value[field]; if (token == null || token.Type != JTokenType.Integer) throw new FormatException(); return (long)token; }
        private static void ValidateCallbacks(Action<WalletSnapshot> onSuccess, Action<WalletError> onFailure)
        { if (onSuccess == null) throw new ArgumentNullException(nameof(onSuccess)); if (onFailure == null) throw new ArgumentNullException(nameof(onFailure)); }
    }
}
