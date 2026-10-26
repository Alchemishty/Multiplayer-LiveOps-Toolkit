using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MultiplayerLiveOpsToolkit.Backends.Django
{
    /// <summary>Private session state. JWT expiry is a scheduling hint, never identity verification.</summary>
    public sealed class TokenSession
    {
        public string AccessToken { get; private set; } = "";
        public string RefreshToken { get; private set; } = "";
        public long ExpiresAt { get; private set; }
        public int Version { get; private set; }

        public void Begin(string accessToken, string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken)) throw new ArgumentException("Session requires a refresh token.", nameof(refreshToken));
            long expiry = ReadExpiry(accessToken);
            Version++;
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            ExpiresAt = expiry;
        }

        public bool TryRenew(int version, string accessToken, string refreshToken)
        {
            if (version != Version || string.IsNullOrEmpty(RefreshToken)) return false;
            long expiry = ReadExpiry(accessToken);
            AccessToken = accessToken;
            if (!string.IsNullOrEmpty(refreshToken)) RefreshToken = refreshToken;
            ExpiresAt = expiry;
            return true;
        }

        public bool NeedsRenewal(long now) => !string.IsNullOrEmpty(RefreshToken) && now >= ExpiresAt - 30;

        public void Clear()
        {
            Version++;
            AccessToken = "";
            RefreshToken = "";
            ExpiresAt = 0;
        }

        private static long ReadExpiry(string accessToken)
        {
            try
            {
                string[] segments = (accessToken ?? "").Split('.');
                if (segments.Length != 3) throw new FormatException();
                string payload = segments[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
                var claims = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
                var expiry = claims["exp"];
                if (expiry == null || expiry.Type != JTokenType.Integer || (long)expiry <= 0) throw new FormatException();
                return (long)expiry;
            }
            catch (Exception exception) when (exception is FormatException || exception is JsonException || exception is OverflowException || exception is ArgumentException)
            {
                throw new ArgumentException("Access token requires a valid numeric JWT expiry.", nameof(accessToken));
            }
        }
    }
}
