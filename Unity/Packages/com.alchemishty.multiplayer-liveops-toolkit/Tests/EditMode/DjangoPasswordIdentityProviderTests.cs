using MultiplayerLiveOpsToolkit.Backends.Django;
using System;
using MultiplayerLiveOpsToolkit.Identity;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

public class DjangoPasswordIdentityProviderTests
{
    [Test]
    public void GetCredential_ValidPassword_PreservesExactCharactersInPayload()
    {
        var provider = new DjangoPasswordIdentityProvider("player", " space \\\" password ");
        IdentityCredential credential = null;
        var routine = provider.GetCredential(value => credential = value);
        while (routine.MoveNext()) { }
        Assert.That(credential.ProviderId, Is.EqualTo("django-password"));
        var payload = JObject.Parse(credential.Value);
        Assert.That((string)payload["username"], Is.EqualTo("player"));
        Assert.That((string)payload["password"], Is.EqualTo(" space \\\" password "));
        Assert.Throws<InvalidOperationException>(() => provider.GetCredential(value => { }));
    }

    [TestCase(null, "password")]
    [TestCase(" ", "password")]
    [TestCase("player", null)]
    [TestCase("player", "")]
    public void Constructor_MissingCredentials_RejectsInput(string username, string password)
    {
        Assert.Throws<ArgumentException>(() => new DjangoPasswordIdentityProvider(username, password));
    }

    [Test]
    public void Constructor_ExcessiveInputLength_RejectsBeforeDispatch()
    {
        Assert.Throws<ArgumentException>(() => new DjangoPasswordIdentityProvider(new string('x', 151), "password"));
        Assert.Throws<ArgumentException>(() => new DjangoPasswordIdentityProvider("player", new string('x', 4097)));
    }

    [Test]
    public void GetCredential_MissingCallback_ThrowsBeforeConsumption()
    {
        var provider = new DjangoPasswordIdentityProvider("player", "password");
        Assert.Throws<ArgumentNullException>(() => provider.GetCredential(null));
        IdentityCredential credential = null;
        var routine = provider.GetCredential(value => credential = value);
        while (routine.MoveNext()) { }
        Assert.That(credential, Is.Not.Null);
    }
}
