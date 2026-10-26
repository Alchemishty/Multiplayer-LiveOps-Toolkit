using NUnit.Framework;
using System.Collections.Generic;

[TestFixture]
public class ShopDisplayFormatterTests
{
    [Test]
    public void FormatCosts_NullCosts_ReturnsFree()
    {
        Assert.AreEqual("Free", ShopDisplayFormatter.FormatCosts(null));
    }

    [Test]
    public void FormatCosts_EmptyCosts_ReturnsFree()
    {
        Assert.AreEqual("Free", ShopDisplayFormatter.FormatCosts(new Dictionary<string, long>()));
    }

    [Test]
    public void FormatCosts_SingleCost_ReturnsFormatted()
    {
        var costs = new Dictionary<string, long>
        {
            { "gold", 100 }
        };
        Assert.AreEqual("100 gold", ShopDisplayFormatter.FormatCosts(costs));
    }

    [Test]
    public void FormatCosts_MultipleCosts_JoinsWithPlus()
    {
        var costs = new Dictionary<string, long>
        {
            { "gold", 100 },
            { "gems", 50 }
        };
        Assert.AreEqual("100 gold + 50 gems", ShopDisplayFormatter.FormatCosts(costs));
    }

    [Test]
    public void GetDisplayName_MainHand_ReturnsMainHand()
    {
        Assert.AreEqual("Main Hand", ShopDisplayFormatter.GetDisplayName(AccessoryType.MainHand));
    }

    [Test]
    public void GetDisplayName_OffHand_ReturnsOffHand()
    {
        Assert.AreEqual("Off Hand", ShopDisplayFormatter.GetDisplayName(AccessoryType.OffHand));
    }

    [Test]
    public void GetDisplayName_Headwear_ReturnsHeadwear()
    {
        Assert.AreEqual("Headwear", ShopDisplayFormatter.GetDisplayName(AccessoryType.Headwear));
    }

    [Test]
    public void GetDisplayName_UndefinedType_FallsBackToToString()
    {
        Assert.AreEqual("Torso", ShopDisplayFormatter.GetDisplayName(AccessoryType.Torso));
    }

}
