using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using MultiplayerLiveOpsToolkit.Identity;

namespace MultiplayerLiveOpsToolkit.Backends.Django
{
    /// <summary>Authenticated IdentityProof requests using the shared player-proof transport.</summary>
    public sealed class DjangoIdentityProofAPI : IIdentityProofBackend
    {
        private readonly DjangoPlayerProofRequest requests;
        public DjangoIdentityProofAPI(string baseUrl, IAuthenticatedSession session)
        { requests = new DjangoPlayerProofRequest(baseUrl, session, "/api/auth/identity-proof/", 8192); }
        public IEnumerator RequestProof(string sessionId, string challenge, Action<string> success, Action<IdentityProofRequestFailure> failure)
        { return requests.RequestProof(sessionId, challenge, success, failure); }
    }
}
