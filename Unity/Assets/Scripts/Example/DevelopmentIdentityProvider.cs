using System;
using System.Collections;
using MultiplayerLiveOpsToolkit.Identity;

/// <summary>Username-only sample credential. Never enable this provider in a production game.</summary>
public sealed class DevelopmentIdentityProvider : IIdentityProvider
{
    private readonly IdentityCredential credential;

    public DevelopmentIdentityProvider(string username)
    {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        throw new InvalidOperationException("The development identity provider requires an Editor or Development Build. Configure a verified production provider.");
#else
        credential = new IdentityCredential("development", username);
#endif
    }

    public IEnumerator GetCredential(Action<IdentityCredential> onCredential)
    {
        if (onCredential == null) throw new ArgumentNullException(nameof(onCredential));
        onCredential(credential);
        yield break;
    }
}
