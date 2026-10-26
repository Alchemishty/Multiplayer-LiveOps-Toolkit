using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MultiplayerLiveOpsToolkit.Equipment;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace MultiplayerLiveOpsToolkit.Backends.Django
{
    /// <summary>Explicit-session reference equipment adapter; mutations are never automatically retried.</summary>
    public sealed class DjangoEquipmentBackend : IEquipmentBackend
    {
        private readonly string endpoint;
        private readonly IAuthenticatedSession session;
        public DjangoEquipmentBackend(string baseUrl, IAuthenticatedSession session)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var address)
                || (address.Scheme != Uri.UriSchemeHttps && !(address.Scheme == Uri.UriSchemeHttp && address.IsLoopback))
                || !string.IsNullOrEmpty(address.UserInfo) || !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))
                throw new ArgumentException("Equipment backend requires credential-free HTTPS, or loopback HTTP.", nameof(baseUrl));
            endpoint = baseUrl.TrimEnd('/') + "/api/equipment/loadout/";
        }
        public IEnumerator Fetch(Action<EquipmentState> onSuccess, Action<EquipmentError> onFailure)
        { ValidateCallbacks(onSuccess, onFailure); return Send(null, 0, onSuccess, onFailure); }
        public IEnumerator Replace(IReadOnlyList<string> itemIds, long expectedRevision, Action<EquipmentState> onSuccess, Action<EquipmentError> onFailure)
        {
            ValidateCallbacks(onSuccess, onFailure);
            if (itemIds == null) throw new ArgumentNullException(nameof(itemIds));
            if (expectedRevision < 0 || expectedRevision >= long.MaxValue - 1) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
            // Copy before returning an iterator so callers cannot change an accepted request.
            var unique = new HashSet<string>(StringComparer.Ordinal);
            var copy = new List<string>();
            foreach (string id in itemIds) { if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || !unique.Add(id)) throw new ArgumentException("Unique bounded cosmetic IDs required.", nameof(itemIds)); copy.Add(id); }
            if (copy.Count > 32) throw new ArgumentException("Equipment accepts at most 32 items.", nameof(itemIds));
            return Send(copy.AsReadOnly(), expectedRevision, onSuccess, onFailure);
        }
        private IEnumerator Send(IReadOnlyList<string> ids, long revision, Action<EquipmentState> onSuccess, Action<EquipmentError> onFailure)
        {
            bool hasCompleted = false;
            try
            {
                int version = session.SessionVersion;
                if (!session.IsActive) { hasCompleted = true; onFailure(new EquipmentError(EquipmentErrorCode.Cancelled, "Player session is inactive.")); yield break; }
                using (var request = new UnityWebRequest(endpoint, ids == null ? "GET" : "POST"))
                {
                    if (ids != null) request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { item_ids = ids, expected_revision = revision })));
                    request.downloadHandler = new DownloadHandlerBuffer();
                    var response = new ApiResponse();
                    yield return session.Send(request, response);
                    if (!session.IsActive || session.SessionVersion != version) { hasCompleted = true; onFailure(new EquipmentError(EquipmentErrorCode.Cancelled, "Equipment account changed.")); yield break; }
                    if (!response.IsSuccess)
                    {
                        var code = Classify(response, ids != null);
                        hasCompleted = true; onFailure(new EquipmentError(code, "Equipment request failed: " + code + ".")); yield break;
                    }
                    EquipmentState result = null;
                    try { result = ParseState(response.Text); }
                    catch (Exception exception) when (exception is JsonException || exception is ArgumentException || exception is FormatException || exception is InvalidCastException || exception is OverflowException) { /* Untrusted response stays out of diagnostics. */ }
                    hasCompleted = true;
                    if (result == null) onFailure(new EquipmentError(ids == null ? EquipmentErrorCode.InvalidResponse : EquipmentErrorCode.UnknownOutcome, "Equipment response was invalid."));
                    else onSuccess(result);
                }
            }
            finally { if (!hasCompleted) onFailure(new EquipmentError(EquipmentErrorCode.Cancelled, "Equipment request was disposed.")); }
        }
        private static EquipmentErrorCode Classify(ApiResponse response, bool mutation)
        {
            if (!string.IsNullOrEmpty(response.Text) && response.Text.Length <= 4096)
            {
                try
                {
                    var body = ReadObject(response.Text);
                    if (body["code"]?.Type == JTokenType.String)
                    {
                        string code = (string)body["code"];
                        if (code == "stale_equipment") return EquipmentErrorCode.StaleRevision;
                        if (code == "equipment_configuration" || code == "identity_not_initialized") return EquipmentErrorCode.Misconfigured;
                        if (code == "invalid_loadout" || code == "invalid_request") return EquipmentErrorCode.Rejected;
                    }
                }
                catch (Exception exception) when (exception is JsonException || exception is FormatException) { }
            }
            return mutation ? EquipmentErrorCode.UnknownOutcome : EquipmentErrorCode.Unavailable;
        }
        internal static EquipmentState ParseState(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 1048576) throw new FormatException();
            var root = ReadObject(json);
            if (root.Count != 3 || !(root["slots"] is JArray slots) || !(root["items"] is JArray items) || !(root["loadout"] is JObject loadout) || slots.Count > 32 || items.Count > 4096 || loadout.Count != 2 || loadout["revision"]?.Type != JTokenType.Integer || !(loadout["items"] is JObject equipped)) throw new FormatException();
            var definitions = new List<EquipmentSlot>();
            foreach (var token in slots)
            {
                if (!(token is JObject slot) || slot.Count != 2 || !(slot["conflicts"] is JArray conflicts) || conflicts.Count > 32) throw new FormatException();
                var ids = new List<string>(); foreach (var conflict in conflicts) { if (conflict.Type != JTokenType.String) throw new FormatException(); ids.Add((string)conflict); }
                definitions.Add(new EquipmentSlot(ReadString(slot, "slot_id"), ids));
            }
            var mappings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var token in items) { if (!(token is JObject item) || item.Count != 2) throw new FormatException(); mappings.Add(ReadString(item, "item_id"), ReadString(item, "slot_id")); }
            var active = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in equipped.Properties()) { if (item.Value.Type != JTokenType.String) throw new FormatException(); active.Add(item.Name, (string)item.Value); }
            return new EquipmentState(definitions, mappings, new EquipmentLoadout((long)loadout["revision"], active));
        }
        private static JObject ReadObject(string json)
        {
            using (var text = new StringReader(json))
            using (var reader = new JsonTextReader(text) { MaxDepth = 6, DateParseHandling = DateParseHandling.None })
            { var result = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }); if (reader.Read()) throw new FormatException(); return result; }
        }
        private static string ReadString(JObject value, string field) { if (value[field]?.Type != JTokenType.String) throw new FormatException(); return (string)value[field]; }
        private static void ValidateCallbacks(Action<EquipmentState> success, Action<EquipmentError> failure) { if (success == null) throw new ArgumentNullException(nameof(success)); if (failure == null) throw new ArgumentNullException(nameof(failure)); }
    }
}
