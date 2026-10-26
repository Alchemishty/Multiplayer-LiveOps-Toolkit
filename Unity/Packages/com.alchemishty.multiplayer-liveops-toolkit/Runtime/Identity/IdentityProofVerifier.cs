using System.Collections.Generic;
using System.Security.Cryptography;
namespace MultiplayerLiveOpsToolkit.Identity
{
    /// <summary>Identity protocol over the shared signed player-proof verifier.</summary>
    public sealed class IdentityProofVerifier
    {
        public SignedPlayerProofVerifier PlayerProofs { get; }
        public IdentityProofVerifier(IDictionary<string, RSAParameters> trustedKeys, IdentityProofPolicy policy)
        { PlayerProofs = new SignedPlayerProofVerifier(trustedKeys, policy); }
        public bool TryVerify(string assertion, IdentityChallenge challenge, long now, out PlayerIdentity identity, out IdentityProofFailure failure)
        { return PlayerProofs.TryVerify(assertion, "liveops-identity+jwt", 0, 8192, claims => true, challenge, now, out identity, out failure); }
    }
}
