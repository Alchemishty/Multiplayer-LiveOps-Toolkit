using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MultiplayerLiveOpsToolkit.Identity
{
    /// <summary>Reads the public-only JSON exported by Django's export_identity_trust command.</summary>
    public static class IdentityTrustConfiguration
    {
        public static IdentityProofVerifier CreateVerifier(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 16384)
                throw new ArgumentException("Identity public trust JSON is missing or oversized.", nameof(json));
            try
            {
                using (var input = new StringReader(json))
                using (var reader = new JsonTextReader(input) { MaxDepth = 4, DateParseHandling = DateParseHandling.None })
                {
                    var configuration = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (configuration == null || reader.Read() || configuration.Count != 7) throw new FormatException();
                    var policy = new IdentityProofPolicy(ReadString(configuration, "issuer"), ReadString(configuration, "game_id"),
                        ReadString(configuration, "environment"), ReadString(configuration, "content_version"),
                        ReadInteger(configuration, "max_lifetime_seconds"), ReadInteger(configuration, "clock_skew_seconds"));
                    var keys = configuration["public_keys"] as JArray;
                    if (keys == null || keys.Count < 1 || keys.Count > 8) throw new FormatException();
                    var publicKeysById = new Dictionary<string, RSAParameters>(StringComparer.Ordinal);
                    foreach (var entry in keys)
                    {
                        var key = entry as JObject;
                        if (key == null || key.Count != 3) throw new FormatException();
                        publicKeysById.Add(ReadString(key, "key_id"), new RSAParameters {
                            Modulus = Convert.FromBase64String(ReadString(key, "modulus")),
                            Exponent = Convert.FromBase64String(ReadString(key, "exponent")) });
                    }
                    return new IdentityProofVerifier(publicKeysById, policy);
                }
            }
            catch (Exception exception) when (exception is JsonException || exception is FormatException || exception is ArgumentException || exception is OverflowException || exception is InvalidCastException)
            { throw new ArgumentException("Identity public trust requires a valid policy and unique RSA public keys.", nameof(json)); }
        }
        private static string ReadString(JObject value, string field)
        {
            var token = value[field];
            if (token == null || token.Type != JTokenType.String || string.IsNullOrEmpty((string)token)) throw new FormatException();
            return (string)token;
        }
        private static long ReadInteger(JObject value, string field)
        {
            var token = value[field];
            if (token == null || token.Type != JTokenType.Integer) throw new FormatException();
            return (long)token;
        }
    }
}
