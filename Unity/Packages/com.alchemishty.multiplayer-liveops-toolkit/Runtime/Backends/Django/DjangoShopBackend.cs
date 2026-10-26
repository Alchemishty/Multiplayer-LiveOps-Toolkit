using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MultiplayerLiveOpsToolkit.Shop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace MultiplayerLiveOpsToolkit.Backends.Django
{
    public sealed class DjangoShopBackend : IShopBackend
    {
        private readonly string baseUrl;
        private readonly IAuthenticatedSession session;
        public DjangoShopBackend(string baseUrl, IAuthenticatedSession session)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var address)
                || (address.Scheme != Uri.UriSchemeHttps && !(address.Scheme == Uri.UriSchemeHttp && address.IsLoopback))
                || !string.IsNullOrEmpty(address.UserInfo) || !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))
                throw new ArgumentException("Shop backend requires HTTPS, or HTTP on loopback, without URL credentials/query/fragment.", nameof(baseUrl));
            this.baseUrl = baseUrl.TrimEnd('/');
        }
        public IEnumerator Fetch(Action<ShopState> onSuccess, Action<ShopError> onFailure)
        { ValidateCallbacks(onSuccess, onFailure); return Send(null, onSuccess, onFailure); }
        public IEnumerator Purchase(string offerId, Action<ShopState> onSuccess, Action<ShopError> onFailure)
        {
            ValidateCallbacks(onSuccess, onFailure);
            if (offerId == null || !Regex.IsMatch(offerId, @"\A[A-Za-z0-9_.:-]{1,128}\z")) throw new ArgumentException("Shop purchase requires an offer ID.", nameof(offerId));
            return Send(offerId, onSuccess, onFailure);
        }
        private IEnumerator Send(string offerId, Action<ShopState> onSuccess, Action<ShopError> onFailure)
        {
            bool hasCompleted = false;
            bool isPurchase = offerId != null;
            try
            {
                int version = session.SessionVersion;
                if (!session.IsActive) { hasCompleted = true; onFailure(new ShopError(ShopErrorCode.Cancelled, "Player session is not active.")); yield break; }
                using (var request = new UnityWebRequest(baseUrl + (isPurchase ? "/api/shop/purchase-offer/" : "/api/shop/catalog/"), isPurchase ? "POST" : "GET"))
                {
                    request.downloadHandler = new DownloadHandlerBuffer();
                    if (isPurchase) request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { offer_id = offerId })));
                    var response = new ApiResponse();
                    yield return session.Send(request, response);
                    if (!session.IsActive || session.SessionVersion != version)
                    { hasCompleted = true; onFailure(new ShopError(ShopErrorCode.Cancelled, "Player session changed before shop response.")); yield break; }
                    if (!response.IsSuccess) { var failure = ClassifyFailure(response, isPurchase); hasCompleted = true; onFailure(failure); yield break; }
                    ShopState result = null;
                    try { result = ParseState(response.Text, offerId); }
                    catch (Exception exception) when (exception is JsonException || exception is ArgumentException || exception is FormatException || exception is InvalidCastException || exception is OverflowException)
                    { /* Untrusted response fragments are never diagnostics. */ }
                    hasCompleted = true;
                    if (result == null) onFailure(new ShopError(isPurchase ? ShopErrorCode.UnknownOutcome : ShopErrorCode.InvalidResponse, isPurchase ? "Purchase could not be confirmed. Refresh Shop." : "Shop response was invalid."));
                    else onSuccess(result);
                }
            }
            finally { if (!hasCompleted) onFailure(new ShopError(ShopErrorCode.Cancelled, "Shop request was disposed.")); }
        }
        private static ShopState ParseState(string json, string expectedOfferId)
        {
            var root = ReadObject(json);
            if (root.Count != (expectedOfferId == null ? 3 : 4) || !(root["offers"] is JArray offers) || offers.Count > 8192) throw new FormatException();
            string purchased = expectedOfferId == null ? null : ReadString(root, "purchased_offer_id");
            if (purchased != expectedOfferId) throw new FormatException();
            var parsedOffers = new List<ShopOffer>();
            foreach (var token in offers)
            {
                if (!(token is JObject offer) || offer.Count != 6 || offer["is_set"]?.Type != JTokenType.Boolean || !(offer["item_ids"] is JArray members) || members.Count > 4096 || !(offer["costs"] is JArray costs) || costs.Count > 256) throw new FormatException();
                var itemIds = new List<string>();
                foreach (var member in members) { if (member.Type != JTokenType.String) throw new FormatException(); itemIds.Add((string)member); }
                var prices = new List<ShopPrice>();
                foreach (var priceToken in costs)
                {
                    if (!(priceToken is JObject price) || price.Count != 2 || price["amount"]?.Type != JTokenType.Integer) throw new FormatException();
                    prices.Add(new ShopPrice(ReadString(price, "currency_id"), (long)price["amount"]));
                }
                parsedOffers.Add(new ShopOffer(ReadString(offer, "offer_id"), ReadString(offer, "display_name"), ReadString(offer, "category_id"), (bool)offer["is_set"], itemIds, prices));
            }
            if (!(root["wallet"] is JObject wallet) || !(root["inventory"] is JObject inventory)) throw new FormatException();
            return new ShopState(new ShopSnapshot(parsedOffers), DjangoWalletBackend.ParseSnapshot(wallet.ToString(Formatting.None)), DjangoInventoryBackend.ParseSnapshot(inventory.ToString(Formatting.None)), purchased);
        }
        private static ShopError ClassifyFailure(ApiResponse response, bool isPurchase)
        {
            string code = null;
            try { code = ReadString(ReadObject(response.Text), "code"); }
            catch (Exception exception) when (exception is JsonException || exception is FormatException) { }
            ShopErrorCode category;
            switch (code)
            {
                case "already_owned": category = ShopErrorCode.AlreadyOwned; break;
                case "insufficient_funds": category = ShopErrorCode.InsufficientFunds; break;
                case "invalid_shop_configuration": case "identity_not_initialized": category = ShopErrorCode.Misconfigured; break;
                case "offer_unavailable": case "invalid_request": category = ShopErrorCode.Rejected; break;
                default: category = isPurchase ? ShopErrorCode.UnknownOutcome : ShopErrorCode.Unavailable; break;
            }
            return new ShopError(category, category == ShopErrorCode.UnknownOutcome ? "Purchase outcome is unknown. Refresh Shop." : "Shop request failed: " + category + ".");
        }
        private static JObject ReadObject(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 2097152) throw new FormatException();
            using (var text = new StringReader(json))
            using (var reader = new JsonTextReader(text) { MaxDepth = 8, DateParseHandling = DateParseHandling.None })
            { var root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }); if (reader.Read()) throw new FormatException(); return root; }
        }
        private static string ReadString(JObject value, string field)
        { var token = value[field]; if (token == null || token.Type != JTokenType.String) throw new FormatException(); return (string)token; }
        private static void ValidateCallbacks(Action<ShopState> onSuccess, Action<ShopError> onFailure)
        { if (onSuccess == null) throw new ArgumentNullException(nameof(onSuccess)); if (onFailure == null) throw new ArgumentNullException(nameof(onFailure)); }
    }
}
