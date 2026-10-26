using System;
using System.Collections;

namespace MultiplayerLiveOpsToolkit.Identity
{
    public interface IIdentityProofBackend
    {
        IEnumerator RequestProof(string sessionId, string challenge, Action<string> onAssertion,
            Action<IdentityProofRequestFailure> onFailure);
    }
}
