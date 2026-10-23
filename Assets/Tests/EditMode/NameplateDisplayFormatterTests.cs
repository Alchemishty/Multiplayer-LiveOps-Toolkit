using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class NameplateDisplayFormatterTests
{
    [Test]
    public void FormatDisplayName_NotHost_ReturnsUsernameUnchanged()
    {
        Assert.AreEqual("Alice", NameplateDisplayFormatter.FormatDisplayName("Alice", false));
    }

    [Test]
    public void FormatDisplayName_IsHost_PrependsHostPrefix()
    {
        Assert.AreEqual("[HOST] Alice", NameplateDisplayFormatter.FormatDisplayName("Alice", true));
    }

    [Test]
    public void FormatDisplayName_EmptyUsername_ReturnsEmptyForNonHost()
    {
        Assert.AreEqual("", NameplateDisplayFormatter.FormatDisplayName("", false));
    }

    [Test]
    public void FormatDisplayName_EmptyUsername_ReturnsHostPrefixOnly()
    {
        Assert.AreEqual("[HOST] ", NameplateDisplayFormatter.FormatDisplayName("", true));
    }

    [Test]
    public void GetDisplayColor_NotHost_ReturnsWhite()
    {
        Assert.AreEqual(Color.white, NameplateDisplayFormatter.GetDisplayColor(false));
    }

    [Test]
    public void GetDisplayColor_IsHost_ReturnsWarmRed()
    {
        Color hostColor = NameplateDisplayFormatter.GetDisplayColor(true);
        Assert.AreEqual(1f, hostColor.r, 0.001f);
        Assert.AreEqual(0.28f, hostColor.g, 0.001f);
        Assert.AreEqual(0.28f, hostColor.b, 0.001f);
    }

    [Test]
    public void ComputeAlpha_AtFadeStart_ReturnsOne()
    {
        Assert.AreEqual(1f, NameplateDisplayFormatter.ComputeAlpha(15f, 15f, 20f));
    }

    [Test]
    public void ComputeAlpha_AtFadeEnd_ReturnsZero()
    {
        Assert.AreEqual(0f, NameplateDisplayFormatter.ComputeAlpha(20f, 15f, 20f));
    }

    [Test]
    public void ComputeAlpha_BeyondFadeEnd_ReturnsZero()
    {
        Assert.AreEqual(0f, NameplateDisplayFormatter.ComputeAlpha(25f, 15f, 20f));
    }

    [Test]
    public void ComputeAlpha_BeforeFadeStart_ReturnsOne()
    {
        Assert.AreEqual(1f, NameplateDisplayFormatter.ComputeAlpha(5f, 15f, 20f));
    }

    [Test]
    public void ComputeAlpha_AtMidpoint_ReturnsHalf()
    {
        Assert.AreEqual(0.5f, NameplateDisplayFormatter.ComputeAlpha(17.5f, 15f, 20f), 0.001f);
    }

    // TruncateToUtf8ByteLimit
    [Test]
    public void TruncateToUtf8ByteLimit_ShortAsciiUsername_ReturnsUnchanged()
    {
        Assert.AreEqual("Alice", NameplateDisplayFormatter.TruncateToUtf8ByteLimit("Alice", 63));
    }

    [Test]
    public void TruncateToUtf8ByteLimit_ExactlyAtLimit_ReturnsUnchanged()
    {
        string exactly63 = new string('A', 63);
        Assert.AreEqual(exactly63, NameplateDisplayFormatter.TruncateToUtf8ByteLimit(exactly63, 63));
    }

    [Test]
    public void TruncateToUtf8ByteLimit_OneBeyondLimit_TruncatesToLimit()
    {
        string input = new string('A', 64);
        string result = NameplateDisplayFormatter.TruncateToUtf8ByteLimit(input, 63);
        Assert.AreEqual(63, System.Text.Encoding.UTF8.GetByteCount(result));
    }

    [Test]
    public void TruncateToUtf8ByteLimit_EmptyString_ReturnsEmpty()
    {
        Assert.AreEqual("", NameplateDisplayFormatter.TruncateToUtf8ByteLimit("", 63));
    }
}
