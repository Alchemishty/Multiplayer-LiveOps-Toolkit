using MultiplayerLiveOpsToolkit.Backends.Django;
using System;
using System.Text;
using NUnit.Framework;

public class TokenSessionTests
{
    public static string Access(long expiresAt)
    {
        return "header." + Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"exp\":" + expiresAt + "}")).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".signature";
    }

    [Test]
    public void NeedsRenewal_WithinThirtySecondsOfExpiry_ReturnsTrue()
    {
        var session = new TokenSession();
        session.Begin(Access(100), "refresh");
        Assert.That(session.NeedsRenewal(69), Is.False);
        Assert.That(session.NeedsRenewal(70), Is.True);
        Assert.That(session.ExpiresAt, Is.EqualTo(100));
    }

    [Test]
    public void TryRenew_CurrentSession_PreservesVersionAndRefreshTokenWhenNotRotated()
    {
        var session = new TokenSession();
        session.Begin(Access(100), "refresh");
        int version = session.Version;
        Assert.That(session.TryRenew(version, Access(200), null), Is.True);
        Assert.That(session.Version, Is.EqualTo(version));
        Assert.That(session.AccessToken, Is.EqualTo(Access(200)));
        Assert.That(session.RefreshToken, Is.EqualTo("refresh"));
        Assert.That(session.TryRenew(version, Access(300), "rotated"), Is.True);
        Assert.That(session.RefreshToken, Is.EqualTo("rotated"));
    }

    [Test]
    public void TryRenew_PreviousAccount_CannotOverwriteNewSession()
    {
        var session = new TokenSession();
        session.Begin(Access(100), "first");
        int version = session.Version;
        session.Begin(Access(200), "second");
        Assert.That(session.TryRenew(version, Access(300), "late"), Is.False);
        Assert.That(session.RefreshToken, Is.EqualTo("second"));
        session.Clear();
        Assert.That(session.TryRenew(session.Version, Access(300), "late"), Is.False);
        Assert.That(session.AccessToken, Is.Empty);
        Assert.That(session.RefreshToken, Is.Empty);
        Assert.That(session.NeedsRenewal(999), Is.False);
    }

    [TestCase("invalid")]
    [TestCase("header.bnVsbA.signature")]
    [TestCase("header.e30.signature")]
    public void Begin_InvalidExpiry_RejectsWithoutChangingAccount(string access)
    {
        var session = new TokenSession();
        session.Begin(Access(100), "original");
        Assert.Throws<ArgumentException>(() => session.Begin(access, "new"));
        Assert.That(session.RefreshToken, Is.EqualTo("original"));
    }
}
