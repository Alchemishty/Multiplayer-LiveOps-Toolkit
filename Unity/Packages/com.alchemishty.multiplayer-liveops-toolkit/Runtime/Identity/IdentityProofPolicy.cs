using System;

namespace MultiplayerLiveOpsToolkit.Identity
{
    public sealed class IdentityProofPolicy
    {
        public string Issuer { get; }
        public string GameId { get; }
        public string EnvironmentId { get; }
        public string ContentVersion { get; }
        public long MaxLifetimeSeconds { get; }
        public long ClockSkewSeconds { get; }

        public IdentityProofPolicy(string issuer, string gameId, string environmentId, string contentVersion,
            long maxLifetimeSeconds, long clockSkewSeconds)
        {
            if (string.IsNullOrEmpty(issuer) || issuer.Length > 256)
                throw new ArgumentException("Identity proof policy requires an issuer of at most 256 characters.", nameof(issuer));
            foreach (char character in issuer)
                if (character < 33 || character > 126) throw new ArgumentException("Identity proof issuer requires printable ASCII without spaces.", nameof(issuer));
            if (!ProofWireFormat.IsIdentifier(gameId)) throw new ArgumentException("Identity proof policy requires a game ID.", nameof(gameId));
            if (!ProofWireFormat.IsIdentifier(environmentId)) throw new ArgumentException("Identity proof policy requires an environment ID.", nameof(environmentId));
            if (!ProofWireFormat.IsIdentifier(contentVersion)) throw new ArgumentException("Identity proof policy requires a content version.", nameof(contentVersion));
            if (maxLifetimeSeconds < 1 || maxLifetimeSeconds > 300) throw new ArgumentOutOfRangeException(nameof(maxLifetimeSeconds));
            if (clockSkewSeconds < 0 || clockSkewSeconds > 30) throw new ArgumentOutOfRangeException(nameof(clockSkewSeconds));
            Issuer = issuer;
            GameId = gameId;
            EnvironmentId = environmentId;
            ContentVersion = contentVersion;
            MaxLifetimeSeconds = maxLifetimeSeconds;
            ClockSkewSeconds = clockSkewSeconds;
        }
    }
}
