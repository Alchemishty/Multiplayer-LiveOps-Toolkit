using System;
using System.Collections;

namespace MultiplayerLiveOpsToolkit.Identity
{
    public interface IIdentityProvider
    {
        IEnumerator GetCredential(Action<IdentityCredential> onCredential);
    }
}
