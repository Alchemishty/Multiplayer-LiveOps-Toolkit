using System;
using System.Collections;

namespace MultiplayerLiveOpsToolkit.Identity
{
    public interface IIdentityBackend
    {
        IEnumerator ExchangeCredential(IdentityCredential credential, Action<PlayerIdentity> onAuthenticated);
        void ClearSession();
    }
}
