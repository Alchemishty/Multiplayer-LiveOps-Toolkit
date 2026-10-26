using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MultiplayerLiveOpsToolkit.Inventory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace MultiplayerLiveOpsToolkit.Backends.Django
{
    /// <summary>Reference cosmetic ownership transport, bound to one authenticated session.</summary>
    public sealed class DjangoInventoryBackend : IInventoryBackend
    {
        private readonly string baseUrl;
        private readonly IAuthenticatedSession session;
        public DjangoInventoryBackend(string baseUrl, IAuthenticatedSession session)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var address)
                || (address.Scheme != Uri.UriSchemeHttps && !(address.Scheme == Uri.UriSchemeHttp && address.IsLoopback))
                || !string.IsNullOrEmpty(address.UserInfo) || !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))
                throw new ArgumentException("Inventory backend requires HTTPS, or HTTP on loopback, without URL credentials/query/fragment.", nameof(baseUrl));
            this.baseUrl = baseUrl.TrimEnd('/');
        }
        public IEnumerator Fetch(Action<InventorySnapshot> onSuccess, Action<InventoryError> onFailure)
        {
            if (onSuccess == null) throw new ArgumentNullException(nameof(onSuccess));
            if (onFailure == null) throw new ArgumentNullException(nameof(onFailure));
            return Send(onSuccess, onFailure);
        }
        private IEnumerator Send(Action<InventorySnapshot> onSuccess, Action<InventoryError> onFailure)
        {
            bool hasCompleted = false;
            try
            {
                int version = session.SessionVersion;
                if (!session.IsActive) { hasCompleted = true; onFailure(new InventoryError(InventoryErrorCode.Cancelled, "Player session is not active.")); yield break; }
                using (var request = UnityWebRequest.Get(baseUrl + "/api/inventory/"))
                {
                    var response = new ApiResponse();
                    yield return session.Send(request, response);
                    if (!session.IsActive || version != session.SessionVersion)
                    { hasCompleted = true; onFailure(new InventoryError(InventoryErrorCode.Cancelled, "Player session changed before inventory response.")); yield break; }
                    if (!response.IsSuccess)
                    {
                        var category = response.ResponseCode == 409 ? InventoryErrorCode.Misconfigured : InventoryErrorCode.Unavailable;
                        hasCompleted = true;
                        onFailure(new InventoryError(category, "Inventory request failed: " + category + "."));
                        yield break;
                    }
                    InventorySnapshot result = null;
                    try { result = ParseSnapshot(response.Text); }
                    catch (Exception exception) when (exception is JsonException || exception is ArgumentException || exception is FormatException || exception is InvalidCastException)
                    { /* Do not include untrusted payloads in diagnostics. */ }
                    hasCompleted = true;
                    if (result == null) onFailure(new InventoryError(InventoryErrorCode.InvalidResponse, "Inventory response was invalid."));
                    else onSuccess(result);
                }
            }
            finally { if (!hasCompleted) onFailure(new InventoryError(InventoryErrorCode.Cancelled, "Inventory request was disposed.")); }
        }
        internal static InventorySnapshot ParseSnapshot(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 1048576) throw new FormatException();
            JObject root;
            using (var text = new StringReader(json))
            using (var reader = new JsonTextReader(text) { MaxDepth = 6, DateParseHandling = DateParseHandling.None })
            {
                root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new FormatException();
            }
            if (root.Count != 2 || !(root["items"] is JArray items) || !(root["sets"] is JArray sets) || items.Count > 4096 || sets.Count > 1024) throw new FormatException();
            var ownedItems = new List<CosmeticItem>();
            foreach (var token in items)
            {
                if (!(token is JObject item) || item.Count != 3) throw new FormatException();
                ownedItems.Add(new CosmeticItem(ReadString(item, "item_id"), ReadString(item, "display_name"), ReadString(item, "category_id")));
            }
            var ownedSets = new List<CosmeticSet>();
            foreach (var token in sets)
            {
                if (!(token is JObject cosmeticSet) || cosmeticSet.Count != 3 || !(cosmeticSet["item_ids"] is JArray members) || members.Count > 4096) throw new FormatException();
                var itemIds = new List<string>();
                foreach (var member in members)
                {
                    if (member.Type != JTokenType.String) throw new FormatException();
                    itemIds.Add((string)member);
                }
                ownedSets.Add(new CosmeticSet(ReadString(cosmeticSet, "set_id"), ReadString(cosmeticSet, "display_name"), itemIds));
            }
            return new InventorySnapshot(ownedItems, ownedSets);
        }
        private static string ReadString(JObject value, string field)
        {
            var token = value[field];
            if (token == null || token.Type != JTokenType.String) throw new FormatException();
            return (string)token;
        }
    }
}
