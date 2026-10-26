using System;
using MultiplayerLiveOpsToolkit.Identity;
using NUnit.Framework;
public class DevelopmentIdentityProviderTests
{
    [Test]
    public void DevelopmentProvider_ValidUsername_DeliversDevelopmentCredential()
    {
        var provider = new DevelopmentIdentityProvider("sample player");
        IdentityCredential credential = null;
        var routine = provider.GetCredential(value => credential = value);
        while (routine.MoveNext()) { }
        Assert.That(credential.ProviderId, Is.EqualTo("development"));
        Assert.That(credential.Value, Is.EqualTo("sample player"));
        Assert.Throws<ArgumentNullException>(() =>
        {
            var invalidRoutine = provider.GetCredential(null);
            while (invalidRoutine.MoveNext()) { }
        });
        Assert.Throws<ArgumentException>(() => new DevelopmentIdentityProvider(" "));
    }
}
