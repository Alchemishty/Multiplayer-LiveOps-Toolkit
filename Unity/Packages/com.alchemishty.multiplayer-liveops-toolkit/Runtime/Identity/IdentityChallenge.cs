using System;
using System.Security.Cryptography;
using System.Threading;

namespace MultiplayerLiveOpsToolkit.Identity
{
    /// <summary>One server-owned peer challenge. Never reconstruct it from an untrusted response.</summary>
    public sealed class IdentityChallenge
    {
        private int consumed;
        public string SessionId { get; }
        public string Nonce { get; }
        public long ExpiresAt { get; }
        public bool IsConsumed => Volatile.Read(ref consumed) != 0;

        public IdentityChallenge(string sessionId, string nonce, long expiresAt)
        {
            if (!ProofWireFormat.IsIdentifier(sessionId)) throw new ArgumentException("Identity challenge requires a session ID.", nameof(sessionId));
            try
            {
                if (nonce == null || nonce.Length != 43 || ProofWireFormat.Decode(nonce).Length != 32) throw new FormatException();
            }
            catch (FormatException) { throw new ArgumentException("Identity challenge requires a canonical 32-byte nonce.", nameof(nonce)); }
            if (expiresAt <= 0 || expiresAt > 253402300000) throw new ArgumentOutOfRangeException(nameof(expiresAt));
            SessionId = sessionId;
            Nonce = nonce;
            ExpiresAt = expiresAt;
        }

        public static IdentityChallenge Create(string sessionId, long expiresAt)
        {
            var randomBytes = new byte[32];
            using (var generator = RandomNumberGenerator.Create()) generator.GetBytes(randomBytes);
            return new IdentityChallenge(sessionId, ProofWireFormat.Encode(randomBytes), expiresAt);
        }

        internal bool TryConsume() => Interlocked.CompareExchange(ref consumed, 1, 0) == 0;
    }
}
