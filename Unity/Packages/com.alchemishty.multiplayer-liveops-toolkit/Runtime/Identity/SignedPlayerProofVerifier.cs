using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MultiplayerLiveOpsToolkit.Identity
{
    /// <summary>Protocol-v1 signature/context verification using only configured public keys.</summary>
    public sealed class SignedPlayerProofVerifier
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly Regex AssertionIdPattern = new Regex(@"\A[a-f0-9]{32}\z", RegexOptions.CultureInvariant);
        private readonly Dictionary<string, RSAParameters> publicKeysById = new Dictionary<string, RSAParameters>(StringComparer.Ordinal);
        private readonly IdentityProofPolicy policy;

        public SignedPlayerProofVerifier(IDictionary<string, RSAParameters> trustedKeys, IdentityProofPolicy policy)
        {
            this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
            if (trustedKeys == null) throw new ArgumentNullException(nameof(trustedKeys));
            if (trustedKeys.Count == 0) throw new ArgumentException("Identity proof verification requires trusted public keys.", nameof(trustedKeys));
            foreach (var keyPair in trustedKeys)
            {
                RSAParameters source = keyPair.Value;
                if (!ProofWireFormat.IsIdentifier(keyPair.Key) || keyPair.Key.Length > 64
                    || source.Modulus == null || source.Exponent == null
                    || source.D != null || source.P != null || source.Q != null || source.DP != null || source.DQ != null || source.InverseQ != null)
                    throw new ArgumentException("Identity proof trust requires key IDs and public RSA parameters only.", nameof(trustedKeys));
                var snapshot = new RSAParameters { Modulus = (byte[])source.Modulus.Clone(), Exponent = (byte[])source.Exponent.Clone() };
                try
                {
                    using (var validator = RSA.Create())
                    {
                        validator.ImportParameters(snapshot);
                        if (validator.KeySize < 2048 || validator.KeySize > 4096) throw new CryptographicException();
                    }
                }
                catch (CryptographicException)
                {
                    throw new ArgumentException("Identity proof trust requires valid 2048–4096 bit RSA public keys.", nameof(trustedKeys));
                }
                publicKeysById.Add(keyPair.Key, snapshot);
            }
        }

        public bool TryVerify(string assertion, string expectedType, int additionalClaimCount, int maximumAssertionLength, Func<JObject, bool> validateAdditionalClaims, IdentityChallenge challenge, long now, out PlayerIdentity identity, out IdentityProofFailure failure)
        {
            if (validateAdditionalClaims == null) throw new ArgumentNullException(nameof(validateAdditionalClaims));
            if (string.IsNullOrEmpty(expectedType) || additionalClaimCount < 0 || maximumAssertionLength < 1 || maximumAssertionLength > 32768) throw new ArgumentException("A fixed proof protocol is required.");
            if (challenge == null) throw new ArgumentNullException(nameof(challenge));
            if (now < 0 || now > 253402300000) throw new ArgumentOutOfRangeException(nameof(now));
            identity = null;
            failure = IdentityProofFailure.Malformed;
            if (challenge.IsConsumed) { failure = IdentityProofFailure.AlreadyConsumed; return false; }
            if (now >= challenge.ExpiresAt) { failure = IdentityProofFailure.Expired; return false; }
            if (string.IsNullOrEmpty(assertion) || assertion.Length > maximumAssertionLength) return false;
            try
            {
                string[] segments = assertion.Split('.');
                if (segments.Length != 3) return false;
                byte[] headerBytes = ProofWireFormat.Decode(segments[0]);
                byte[] payloadBytes = ProofWireFormat.Decode(segments[1]);
                byte[] signatureBytes = ProofWireFormat.Decode(segments[2]);
                if (headerBytes.Length > 512) return false;
                var header = ReadObject(headerBytes);
                if (header.Count != 3 || ReadString(header, "alg", 10) != "RS256"
                    || ReadString(header, "typ", 64) != expectedType) return false;
                string keyId = ReadString(header, "kid", 64);
                if (!ProofWireFormat.IsIdentifier(keyId)) return false;
                if (!publicKeysById.TryGetValue(keyId, out var publicKey))
                {
                    failure = IdentityProofFailure.UntrustedKey;
                    return false;
                }
                using (var signatureVerifier = RSA.Create())
                {
                    signatureVerifier.ImportParameters(publicKey);
                    if (signatureBytes.Length != publicKey.Modulus.Length
                        || !signatureVerifier.VerifyData(Encoding.ASCII.GetBytes(segments[0] + "." + segments[1]),
                            signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    {
                        failure = IdentityProofFailure.InvalidSignature;
                        return false;
                    }
                }

                var claims = ReadObject(payloadBytes);
                if (claims.Count != 13 + additionalClaimCount) return false;
                string playerId = ReadString(claims, "sub", 128);
                string displayName = ReadString(claims, "name", 600);
                if (!ProofWireFormat.IsIdentifier(playerId) || string.IsNullOrWhiteSpace(displayName)
                    || StrictUtf8.GetByteCount(displayName) > 600) return false;
                foreach (char character in displayName)
                    if (character < 32 || character == 127) return false;
                if (!AssertionIdPattern.IsMatch(ReadString(claims, "jti", 32))) return false;
                if (ReadString(claims, "iss", 256) != policy.Issuer || ReadString(claims, "aud", 128) != policy.GameId
                    || ReadString(claims, "env", 128) != policy.EnvironmentId || ReadString(claims, "content", 128) != policy.ContentVersion
                    || ReadString(claims, "sid", 128) != challenge.SessionId || ReadString(claims, "nonce", 43) != challenge.Nonce
                    || ReadInteger(claims, "ver") != 1)
                {
                    failure = IdentityProofFailure.InvalidContext;
                    return false;
                }
                long issuedAt = ReadInteger(claims, "iat");
                long notBefore = ReadInteger(claims, "nbf");
                long expiresAt = ReadInteger(claims, "exp");
                if (issuedAt <= 0 || expiresAt > 253402300799 || expiresAt <= issuedAt
                    || expiresAt - issuedAt > policy.MaxLifetimeSeconds || notBefore != issuedAt) return false;
                if (issuedAt > now + policy.ClockSkewSeconds)
                {
                    failure = IdentityProofFailure.NotYetValid;
                    return false;
                }
                if (expiresAt <= now - policy.ClockSkewSeconds)
                {
                    failure = IdentityProofFailure.Expired;
                    return false;
                }
                if (!validateAdditionalClaims(claims)) return false;
                var verifiedIdentity = new PlayerIdentity(playerId, displayName);
                if (!challenge.TryConsume()) { failure = IdentityProofFailure.AlreadyConsumed; return false; }
                identity = verifiedIdentity;
                failure = IdentityProofFailure.None;
                return true;
            }
            catch (CryptographicException)
            {
                failure = IdentityProofFailure.InvalidSignature;
                return false;
            }
            catch (Exception exception) when (exception is JsonException || exception is FormatException
                || exception is ArgumentException || exception is OverflowException || exception is InvalidCastException)
            {
                failure = IdentityProofFailure.Malformed;
                return false;
            }
        }

        private static JObject ReadObject(byte[] bytes)
        {
            using (var text = new StringReader(StrictUtf8.GetString(bytes)))
            using (var reader = new JsonTextReader(text) { MaxDepth = 4, DateParseHandling = DateParseHandling.None })
            {
                var value = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (value == null || reader.Read()) throw new FormatException("Identity proof requires one JSON object.");
                return value;
            }
        }

        private static string ReadString(JObject value, string field, int maximumLength)
        {
            var token = value[field];
            if (token == null || token.Type != JTokenType.String) throw new FormatException("Identity proof has an invalid string field.");
            string text = (string)token;
            if (string.IsNullOrEmpty(text) || text.Length > maximumLength) throw new FormatException("Identity proof string length is invalid.");
            return text;
        }

        private static long ReadInteger(JObject value, string field)
        {
            var token = value[field];
            if (token == null || token.Type != JTokenType.Integer) throw new FormatException("Identity proof requires an integer field.");
            return (long)token;
        }
    }
}
