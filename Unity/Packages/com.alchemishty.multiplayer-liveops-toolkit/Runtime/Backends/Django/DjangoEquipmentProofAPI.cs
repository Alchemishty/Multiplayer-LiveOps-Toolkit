using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using MultiplayerLiveOpsToolkit.Identity;
using MultiplayerLiveOpsToolkit.Equipment;

namespace MultiplayerLiveOpsToolkit.Backends.Django
{
    /// <summary>Authenticated EquipmentProof requests using the shared player-proof transport.</summary>
    public sealed class DjangoEquipmentProofAPI : IEquipmentProofBackend
    {
        private readonly DjangoPlayerProofRequest requests;
        public DjangoEquipmentProofAPI(string baseUrl, IAuthenticatedSession session)
        { requests = new DjangoPlayerProofRequest(baseUrl, session, "/api/equipment/proof/", 32768); }
        public IEnumerator RequestProof(string sessionId, string challenge, Action<string> success, Action<IdentityProofRequestFailure> failure)
        { return requests.RequestProof(sessionId, challenge, success, failure); }
    }
}
